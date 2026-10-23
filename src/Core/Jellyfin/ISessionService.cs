namespace Lumen.Core.Jellyfin;

/// <summary>Holds the signed-in session and persists it (token in the OS secret store, the rest in settings).</summary>
public interface ISessionService
{
    SessionInfo? Current { get; }

    /// <summary>Servers the user connected to before, most recent first.</summary>
    IReadOnlyList<SavedServer> RecentServers { get; }

    /// <summary>Restores the last session if its token is still valid.</summary>
    Task<SessionInfo?> TryRestoreAsync(CancellationToken cancellationToken = default);

    void RememberServer(ServerInfo server);
    void SignIn(SessionInfo session);
    Task SignOutAsync();
}

public sealed record SavedServer(string Id, string Name, string Address, string? LastUserId, string? LastUserName);
