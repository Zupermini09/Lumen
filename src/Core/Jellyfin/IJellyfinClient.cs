namespace Lumen.Core.Jellyfin;

/// <summary>
/// Thin client for the Jellyfin HTTP API. Server-level calls take an address; user-level calls use the
/// current <see cref="ISessionService.Current"/> session.
/// </summary>
public interface IJellyfinClient
{
    /// <summary>Finds a Jellyfin server for what the user typed (with or without scheme/port).</summary>
    /// <exception cref="ServerConnectionException">No server answered on any candidate address.</exception>
    Task<ServerInfo> FindServerAsync(string userInput, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserInfo>> GetPublicUsersAsync(ServerInfo server, CancellationToken cancellationToken = default);

    /// <exception cref="AuthenticationException">Wrong user name or password.</exception>
    Task<SessionInfo> AuthenticateAsync(ServerInfo server, string userName, string password, CancellationToken cancellationToken = default);

    Task<bool> IsQuickConnectEnabledAsync(ServerInfo server, CancellationToken cancellationToken = default);
    Task<QuickConnectRequest> StartQuickConnectAsync(ServerInfo server, CancellationToken cancellationToken = default);
    Task<bool> IsQuickConnectAuthorizedAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default);
    Task<SessionInfo> AuthenticateWithQuickConnectAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Checks that a saved token still works; returns the current user, or null if the token was revoked.</summary>
    Task<UserInfo?> ValidateSessionAsync(SessionInfo session, CancellationToken cancellationToken = default);
    Task SignOutAsync(SessionInfo session, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> GetLibrariesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> GetResumeItemsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> GetNextUpAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, CancellationToken cancellationToken = default);

    Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight);
    Uri GetUserImageUri(ServerInfo server, UserInfo user, int maxHeight);

    /// <summary>A direct-play URL the player can open (the server sends the file as is).</summary>
    Uri GetStreamUri(MediaItem item);

    Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken = default);
    Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default);
    Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default);
}

public sealed record QuickConnectRequest(string Code, string Secret);

public sealed record PlaybackReport(string ItemId, string PlaySessionId, TimeSpan Position, bool IsPaused);

public class ServerConnectionException(string message, Exception? inner = null) : Exception(message, inner);

public class AuthenticationException(string message) : Exception(message);
