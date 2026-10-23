namespace Lumen.Core.Jellyfin;

/// <summary>A server that answered on <see cref="Address"/>.</summary>
public sealed record ServerInfo(string Id, string Name, string Version, Uri Address);

public sealed record UserInfo(string Id, string Name, string? PrimaryImageTag, bool HasPassword);

/// <summary>An authenticated connection: which server, which user, and the access token.</summary>
public sealed record SessionInfo(ServerInfo Server, UserInfo User, string AccessToken);

public enum ImageKind
{
    Primary,
    Thumb,
    Backdrop,
}

/// <summary>The subset of Jellyfin's BaseItemDto that the UI needs.</summary>
public sealed record MediaItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Jellyfin item type: Movie, Series, Season, Episode, MusicAlbum, Audio, CollectionFolder...</summary>
    public required string Type { get; init; }

    public bool IsFolder { get; init; }

    /// <summary>For library views: movies, tvshows, music, homevideos, boxsets, playlists, livetv...</summary>
    public string? CollectionType { get; init; }

    /// <summary>Video, Audio, Photo, Book or null for folders.</summary>
    public string? MediaType { get; init; }

    public int? ProductionYear { get; init; }
    public string? SeriesName { get; init; }
    public string? SeriesId { get; init; }
    public int? IndexNumber { get; init; }
    public int? ParentIndexNumber { get; init; }
    public string? AlbumArtist { get; init; }

    public TimeSpan? RunTime { get; init; }
    public TimeSpan PlaybackPosition { get; init; }
    public bool Played { get; init; }
    public bool IsFavorite { get; init; }

    /// <summary>Only present where the request asked for it (details pages, episode lists).</summary>
    public string? Overview { get; init; }

    /// <summary>For episodes: the season they belong to.</summary>
    public string? SeasonId { get; init; }

    public string? PrimaryImageTag { get; init; }
    public string? ThumbImageTag { get; init; }

    /// <summary>For episodes without their own image: the series' primary image.</summary>
    public string? SeriesPrimaryImageTag { get; init; }

    /// <summary>For tracks without their own image: the album's primary image.</summary>
    public string? AlbumId { get; init; }
    public string? AlbumPrimaryImageTag { get; init; }

    public bool IsPlayable => !IsFolder && MediaType is "Video" or "Audio";

    /// <summary>Where playback resumes: the saved position, unless it is just the start or the item was watched to the end.</summary>
    public TimeSpan ResumePosition =>
        PlaybackPosition > TimeSpan.FromSeconds(5) && !Played ? PlaybackPosition : TimeSpan.Zero;
}

/// <summary>An actor, director, writer... listed on an item.</summary>
public sealed record PersonInfo(string Id, string Name, string? Role, string Type, string? PrimaryImageTag);

/// <summary>An item with everything the details pages show (the list requests only fetch <see cref="MediaItem"/>).</summary>
public sealed record ItemDetails
{
    public required MediaItem Item { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public string? OfficialRating { get; init; }
    public double? CommunityRating { get; init; }
    public string? Tagline { get; init; }
    public IReadOnlyList<PersonInfo> People { get; init; } = [];

    /// <summary>The streams of the item's first file version (video, audio and subtitle tracks).</summary>
    public IReadOnlyList<MediaStreamInfo> Streams { get; init; } = [];

    /// <summary>Series only: "Continuing" or "Ended", and the year the last episode aired.</summary>
    public string? Status { get; init; }
    public int? EndYear { get; init; }

    /// <summary>The item that owns the backdrop: the item itself, or for episodes and seasons the series.</summary>
    public string? BackdropItemId { get; init; }
    public string? BackdropTag { get; init; }
}

public sealed record MediaStreamInfo(
    int Index,
    string Type,
    string? Codec,
    string? Language,
    string DisplayTitle,
    bool IsDefault,
    bool IsForced,
    bool IsExternal);

public sealed record ChapterInfo(string Name, TimeSpan Start);

/// <summary>What the player needs from the server before starting: which file version, its tracks and defaults.</summary>
public sealed record PlaybackDetails(
    string MediaSourceId,
    string? PlaySessionId,
    IReadOnlyList<MediaStreamInfo> Streams,
    int? DefaultAudioStreamIndex,
    int? DefaultSubtitleStreamIndex,
    IReadOnlyList<ChapterInfo> Chapters)
{
    public IEnumerable<MediaStreamInfo> AudioStreams => Streams.Where(s => s.Type == "Audio");
    public IEnumerable<MediaStreamInfo> SubtitleStreams => Streams.Where(s => s.Type == "Subtitle");

    /// <summary>When the server knows nothing more (old server, error): play the item's own file with player defaults.</summary>
    public static PlaybackDetails Fallback(MediaItem item) => new(item.Id, null, [], null, null, []);
}
