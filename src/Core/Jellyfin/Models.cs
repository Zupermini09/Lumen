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

    public string? PrimaryImageTag { get; init; }
    public string? ThumbImageTag { get; init; }

    /// <summary>For episodes without their own image: the series' primary image.</summary>
    public string? SeriesPrimaryImageTag { get; init; }

    /// <summary>For tracks without their own image: the album's primary image.</summary>
    public string? AlbumId { get; init; }
    public string? AlbumPrimaryImageTag { get; init; }

    public bool IsPlayable => !IsFolder && MediaType is "Video" or "Audio";
}
