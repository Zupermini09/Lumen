using Lumen.Core.Jellyfin;

namespace Lumen.Tests;

/// <summary>Test base: every call throws unless a test overrides it, so new interface members need only one update.</summary>
internal abstract class StubJellyfinClient : IJellyfinClient
{
    public virtual Task<ServerInfo> FindServerAsync(string userInput, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<UserInfo>> GetPublicUsersAsync(ServerInfo server, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<SessionInfo> AuthenticateAsync(ServerInfo server, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> IsQuickConnectEnabledAsync(ServerInfo server, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<QuickConnectRequest> StartQuickConnectAsync(ServerInfo server, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> IsQuickConnectAuthorizedAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<SessionInfo> AuthenticateWithQuickConnectAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<UserInfo?> ValidateSessionAsync(SessionInfo session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task SignOutAsync(SessionInfo session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetLibrariesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetResumeItemsAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetNextUpAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<MediaItem?> GetNextEpisodeAsync(MediaItem episode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<ItemDetails> GetItemDetailsAsync(string itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetSeasonsAsync(MediaItem series, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(MediaItem series, MediaItem? season, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<MediaItem?> GetSeriesNextUpAsync(MediaItem series, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task SetPlayedAsync(string itemId, bool played, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task SetFavoriteAsync(string itemId, bool favorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => throw new NotSupportedException();
    public virtual Uri GetUserImageUri(ServerInfo server, UserInfo user, int maxHeight) => throw new NotSupportedException();
    public virtual Task<PlaybackDetails> GetPlaybackDetailsAsync(MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<Lumen.Core.Playback.MediaSegment>> GetMediaSegmentsAsync(MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Uri GetStreamUri(MediaItem item, string? mediaSourceId = null) => throw new NotSupportedException();
    public virtual Uri GetSubtitleUri(MediaItem item, string mediaSourceId, MediaStreamInfo stream) => throw new NotSupportedException();
    public virtual Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
