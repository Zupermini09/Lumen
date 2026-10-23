using Lumen.Core;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.Jellyfin;

public sealed class SessionService(
    IJellyfinClient client,
    SessionHolder holder,
    ISettingsStore settings,
    ISecretStore secrets,
    ILogger log) : ISessionService
{
    private const int MaxRecentServers = 10;
    private readonly ILogger _log = log.ForContext<SessionService>();

    public SessionInfo? Current => holder.Current;

    public IReadOnlyList<SavedServer> RecentServers => settings.Settings.Servers;

    public async Task<SessionInfo?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var saved = settings.Settings.Servers.FirstOrDefault(s => s.Id == settings.Settings.LastServerId);
        if (saved?.LastUserId is not { } userId || !Uri.TryCreate(saved.Address, UriKind.Absolute, out var address))
            return null;

        var token = secrets.Get(TokenKey(saved.Id, userId));
        if (string.IsNullOrEmpty(token))
            return null;

        var server = new ServerInfo(saved.Id, saved.Name, "", address);
        var candidate = new SessionInfo(server, new UserInfo(userId, saved.LastUserName ?? "", null, true), token);

        UserInfo? user;
        try
        {
            user = await client.ValidateSessionAsync(candidate, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServerConnectionException($"Could not reach {saved.Name} at {saved.Address}. Is the server running?", ex);
        }

        if (user is null)
        {
            _log.Information("Saved session for {Server} is no longer valid", saved.Name);
            secrets.Remove(TokenKey(saved.Id, userId));
            return null;
        }

        var session = candidate with { User = user };
        holder.Current = session;
        _log.Information("Restored session on {Server} as {User}", saved.Name, user.Name);
        return session;
    }

    public void RememberServer(ServerInfo server)
    {
        var list = settings.Settings.Servers;
        var existing = list.FirstOrDefault(s => s.Id == server.Id);
        list.RemoveAll(s => s.Id == server.Id);
        list.Insert(0, new SavedServer(server.Id, server.Name, server.Address.ToString(), existing?.LastUserId, existing?.LastUserName));
        if (list.Count > MaxRecentServers)
            list.RemoveRange(MaxRecentServers, list.Count - MaxRecentServers);
        settings.Settings.LastServerId = server.Id;
        settings.Save();
    }

    public void SignIn(SessionInfo session)
    {
        holder.Current = session;
        RememberServer(session.Server);
        var list = settings.Settings.Servers;
        list[0] = list[0] with { LastUserId = session.User.Id, LastUserName = session.User.Name };
        settings.Save();
        secrets.Set(TokenKey(session.Server.Id, session.User.Id), session.AccessToken);
        _log.Information("Signed in to {Server} as {User}", session.Server.Name, session.User.Name);
    }

    public async Task SignOutAsync()
    {
        if (holder.Current is not { } session)
            return;

        holder.Current = null;
        secrets.Remove(TokenKey(session.Server.Id, session.User.Id));
        var list = settings.Settings.Servers;
        var index = list.FindIndex(s => s.Id == session.Server.Id);
        if (index >= 0)
            list[index] = list[index] with { LastUserId = null };
        settings.Save();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.SignOutAsync(session, timeout.Token);
        _log.Information("Signed out of {Server}", session.Server.Name);
    }

    internal static string TokenKey(string serverId, string userId) => $"jellyfin-token:{serverId}:{userId}";
}
