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
    /// <summary>The episode after this one in the series (across seasons), or null for the last one.</summary>
    Task<MediaItem?> GetNextEpisodeAsync(MediaItem episode, CancellationToken cancellationToken = default);

    /// <summary>The contents of a library, folder, show or album; <paramref name="options"/> sorts and filters it (library pages).</summary>
    Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, ItemListOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>The genres used by the items of a library, for its filter list.</summary>
    Task<IReadOnlyList<string>> GetGenresAsync(MediaItem library, CancellationToken cancellationToken = default);

    /// <summary>The most recently added items of a library (episodes are grouped by series, like the web client's Home).</summary>
    Task<IReadOnlyList<MediaItem>> GetLatestAsync(MediaItem library, int limit, CancellationToken cancellationToken = default);

    /// <summary>Items of the given types whose name matches the text, by name.</summary>
    Task<IReadOnlyList<MediaItem>> SearchAsync(string term, IReadOnlyList<string> itemTypes, int limit, CancellationToken cancellationToken = default);

    /// <summary>All of the user's favourites of the given types, by name.</summary>
    Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(IReadOnlyList<string> itemTypes, CancellationToken cancellationToken = default);

    /// <summary>One item with overview, genres, cast, streams... (the details pages).</summary>
    Task<ItemDetails> GetItemDetailsAsync(string itemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> GetSeasonsAsync(MediaItem series, CancellationToken cancellationToken = default);

    /// <summary>The episodes of a series, or of one season of it, with overviews.</summary>
    Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(MediaItem series, MediaItem? season, CancellationToken cancellationToken = default);

    /// <summary>The episode to watch next in a series, or null when there is none (nothing aired yet, or all watched).</summary>
    Task<MediaItem?> GetSeriesNextUpAsync(MediaItem series, CancellationToken cancellationToken = default);

    /// <summary>Marks an item (or all episodes of a series/season) as watched or not watched.</summary>
    Task SetPlayedAsync(string itemId, bool played, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(string itemId, bool favorite, CancellationToken cancellationToken = default);

    Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight);
    Uri GetUserImageUri(ServerInfo server, UserInfo user, int maxHeight);

    /// <summary>
    /// File version, tracks with the user's default choices and chapters, and how the server will deliver the stream
    /// for <paramref name="request"/> (PlaybackInfo): as the file, or converted with a transcoding URL.
    /// </summary>
    Task<PlaybackDetails> GetPlaybackDetailsAsync(MediaItem item, PlaybackRequest? request = null, CancellationToken cancellationToken = default);

    /// <summary>Intro/credits/... segments for an item; empty when the server has none or is too old for the API.</summary>
    Task<IReadOnlyList<Playback.MediaSegment>> GetMediaSegmentsAsync(MediaItem item, CancellationToken cancellationToken = default);

    /// <summary>A direct-play URL the player can open (the server sends the file as is).</summary>
    Uri GetStreamUri(MediaItem item, string? mediaSourceId = null);

    /// <summary>URL of an external subtitle file, converted by the server to a format the player reads.</summary>
    Uri GetSubtitleUri(MediaItem item, string mediaSourceId, MediaStreamInfo stream);

    Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken = default);
    Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default);
    Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default);

    /// <summary>Stops the server's conversion job of a playback session (when playback ends or the quality changes).</summary>
    Task StopTranscodingAsync(string playSessionId, CancellationToken cancellationToken = default);
}

public sealed record QuickConnectRequest(string Code, string Secret);

public sealed record PlaybackReport(string ItemId, string MediaSourceId, string PlaySessionId, TimeSpan Position, bool IsPaused,
    PlayMethod Method = PlayMethod.DirectPlay);

public class ServerConnectionException(string message, Exception? inner = null) : Exception(message, inner);

public class AuthenticationException(string message) : Exception(message);
