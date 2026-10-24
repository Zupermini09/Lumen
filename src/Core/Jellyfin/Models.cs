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

    /// <summary>The backdrop to show for the item: its own, or (for episodes and seasons) the series'. Only where the request asked for backdrops.</summary>
    public string? BackdropItemId { get; init; }
    public string? BackdropTag { get; init; }

    /// <summary>For episodes without their own image: the series' primary image.</summary>
    public string? SeriesPrimaryImageTag { get; init; }

    /// <summary>For episodes: the series' Thumb image (the item that owns it, and its tag).</summary>
    public string? SeriesThumbItemId { get; init; }
    public string? SeriesThumbTag { get; init; }

    /// <summary>For episodes: the series' backdrop, even when the episode has one of its own.</summary>
    public string? SeriesBackdropItemId { get; init; }
    public string? SeriesBackdropTag { get; init; }

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

/// <summary>How a stream reaches the player. The names are the values Jellyfin's playback reports use.</summary>
public enum PlayMethod
{
    /// <summary>The server sends the file as it is.</summary>
    DirectPlay,
    /// <summary>The server repackages the streams without re-encoding them.</summary>
    DirectStream,
    /// <summary>The server converts the video (HLS) to fit the bitrate or what the player can play.</summary>
    Transcode,
}

/// <summary>What the player asks the server to prepare (the PlaybackInfo request).</summary>
public sealed record PlaybackRequest
{
    /// <summary>Most bits per second the stream may use, or null for the original quality.</summary>
    public int? MaxBitrate { get; init; }

    /// <summary>False makes the server convert the video even if the file could be sent as it is (direct play failed).</summary>
    public bool AllowDirectPlay { get; init; } = true;

    /// <summary>The file version; the server only honours the stream indexes below when it is given.</summary>
    public string? MediaSourceId { get; init; }

    /// <summary>Audio stream a converted video should carry, as a server stream index; null lets the server choose.</summary>
    public int? AudioStreamIndex { get; init; }

    /// <summary>Subtitle stream to burn into a converted video, -1 for none, or null to let the server choose.</summary>
    public int? SubtitleStreamIndex { get; init; }

    /// <summary>Also load the chapter list (only the first request of a playback needs it).</summary>
    public bool IncludeChapters { get; init; } = true;
}

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

    /// <summary>How the server will deliver the stream.</summary>
    public PlayMethod Method { get; init; } = PlayMethod.DirectPlay;

    /// <summary>For <see cref="PlayMethod.Transcode"/>: the HLS playlist to open (the access token is part of it, never log it).</summary>
    public Uri? TranscodeUri { get; init; }

    /// <summary>Why the server converts the video (Jellyfin's TranscodeReasons, e.g. ContainerBitrateExceedsLimit).</summary>
    public IReadOnlyList<string> TranscodeReasons { get; init; } = [];

    /// <summary>Audio stream and burned-in subtitle stream of a converted video, as the server set them up.</summary>
    public int? TranscodeAudioStreamIndex { get; init; }
    public int? BurnedSubtitleStreamIndex { get; init; }

    /// <summary>False when the server offers neither direct play, direct stream nor a conversion for the request.</summary>
    public bool Playable { get; init; } = true;

    /// <summary>False when the server does not let this user have video converted (so only the original file plays).</summary>
    public bool TranscodingAllowed { get; init; } = true;

    /// <summary>When the server knows nothing more (old server, error): play the item's own file with player defaults.</summary>
    public static PlaybackDetails Fallback(MediaItem item) => new(item.Id, null, [], null, null, []);
}
