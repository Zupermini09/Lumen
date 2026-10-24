using System.Text.Json;
using Lumen.Core.Jellyfin;

namespace Lumen.Jellyfin;

// Jellyfin's JSON field names. Deserialized case-insensitively: some endpoints answer in camelCase, most in PascalCase.
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };
}

internal sealed class PublicSystemInfoDto
{
    public string? Id { get; set; }
    public string? ServerName { get; set; }
    public string? Version { get; set; }
    public string? ProductName { get; set; }
}

internal sealed class UserDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PrimaryImageTag { get; set; }
    public bool HasPassword { get; set; }

    public UserInfo ToModel() => new(Id, Name, PrimaryImageTag, HasPassword);
}

internal sealed class AuthenticationResultDto
{
    public UserDto? User { get; set; }
    public string? AccessToken { get; set; }
}

internal sealed class QuickConnectResultDto
{
    public string? Secret { get; set; }
    public string? Code { get; set; }
    public bool Authenticated { get; set; }
}

internal sealed class ItemsResultDto
{
    public List<BaseItemDto> Items { get; set; } = [];
    public int TotalRecordCount { get; set; }
}

internal sealed class UserItemDataDto
{
    public long PlaybackPositionTicks { get; set; }
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
}

internal sealed class PersonDto
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Role { get; set; }
    public string? Type { get; set; }
    public string? PrimaryImageTag { get; set; }
}

internal sealed class BaseItemDto
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool IsFolder { get; set; }
    public string? CollectionType { get; set; }
    public string? MediaType { get; set; }
    public int? ProductionYear { get; set; }
    public string? SeriesName { get; set; }
    public string? SeriesId { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public string? AlbumArtist { get; set; }
    public long? RunTimeTicks { get; set; }
    public UserItemDataDto? UserData { get; set; }
    public Dictionary<string, string>? ImageTags { get; set; }
    public string? SeriesPrimaryImageTag { get; set; }
    public string? AlbumId { get; set; }
    public string? AlbumPrimaryImageTag { get; set; }
    public string? Overview { get; set; }
    public string? SeasonId { get; set; }

    // Details pages only (fields=Overview,Genres,People,MediaStreams...).
    public List<string>? Genres { get; set; }
    public string? OfficialRating { get; set; }
    public double? CommunityRating { get; set; }
    public List<string>? Taglines { get; set; }
    public List<PersonDto>? People { get; set; }
    public List<MediaStreamDto>? MediaStreams { get; set; }
    public List<MediaSourceDto>? MediaSources { get; set; }
    public string? Status { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string>? BackdropImageTags { get; set; }
    public string? ParentBackdropItemId { get; set; }
    public List<string>? ParentBackdropImageTags { get; set; }

    public MediaItem ToModel() => new()
    {
        Id = Id,
        Name = Name ?? "",
        Type = Type ?? "",
        IsFolder = IsFolder,
        CollectionType = CollectionType,
        MediaType = MediaType,
        ProductionYear = ProductionYear,
        SeriesName = SeriesName,
        SeriesId = SeriesId,
        IndexNumber = IndexNumber,
        ParentIndexNumber = ParentIndexNumber,
        AlbumArtist = AlbumArtist,
        RunTime = RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
        PlaybackPosition = TimeSpan.FromTicks(UserData?.PlaybackPositionTicks ?? 0),
        Played = UserData?.Played ?? false,
        IsFavorite = UserData?.IsFavorite ?? false,
        Overview = Overview,
        SeasonId = SeasonId,
        PrimaryImageTag = ImageTags?.GetValueOrDefault("Primary"),
        ThumbImageTag = ImageTags?.GetValueOrDefault("Thumb"),
        SeriesPrimaryImageTag = SeriesPrimaryImageTag,
        AlbumId = AlbumId,
        AlbumPrimaryImageTag = AlbumPrimaryImageTag,
    };

    public ItemDetails ToDetails()
    {
        // Episodes and seasons have no backdrop of their own: like the web client, show the series'.
        var (backdropItem, backdropTag) =
            BackdropImageTags is { Count: > 0 } own ? (Id, own[0])
            : ParentBackdropItemId is { Length: > 0 } parent && ParentBackdropImageTags is { Count: > 0 } inherited ? (parent, inherited[0])
            : ((string?)null, (string?)null);

        // The top-level list is the first file version's; older servers only fill it inside MediaSources.
        var streams = MediaStreams is { Count: > 0 } ? MediaStreams : MediaSources?.FirstOrDefault()?.MediaStreams;

        return new ItemDetails
        {
            Item = ToModel(),
            Genres = Genres ?? [],
            OfficialRating = string.IsNullOrWhiteSpace(OfficialRating) ? null : OfficialRating,
            CommunityRating = CommunityRating is > 0 ? CommunityRating : null,
            Tagline = Taglines?.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
            People = People?.Where(p => p.Id.Length > 0 && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new PersonInfo(p.Id, p.Name!, string.IsNullOrWhiteSpace(p.Role) ? null : p.Role, p.Type ?? "", p.PrimaryImageTag)).ToList() ?? [],
            Streams = streams?.Select(s => s.ToModel()).ToList() ?? [],
            Status = Status,
            EndYear = EndDate?.Year,
            BackdropItemId = backdropItem,
            BackdropTag = backdropTag,
        };
    }
}

internal sealed class MediaStreamDto
{
    public int Index { get; set; }
    public string? Type { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? DisplayTitle { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsExternal { get; set; }

    public MediaStreamInfo ToModel() => new(Index, Type ?? "", Codec, Language,
        DisplayTitle ?? Title ?? Language ?? $"Track {Index}", IsDefault, IsForced, IsExternal);
}

internal sealed class MediaSourceDto
{
    public string? Id { get; set; }
    public List<MediaStreamDto> MediaStreams { get; set; } = [];
    public int? DefaultAudioStreamIndex { get; set; }
    public int? DefaultSubtitleStreamIndex { get; set; }

    // What the server can do for the PlaybackInfo request; absent on old servers (then the file plays as it is).
    public bool? SupportsDirectPlay { get; set; }
    public bool? SupportsDirectStream { get; set; }
    public bool? SupportsTranscoding { get; set; }

    /// <summary>Server-relative URL of the HLS playlist, with the access token and play session in its query.</summary>
    public string? TranscodingUrl { get; set; }
}

internal sealed class PlaybackInfoDto
{
    public List<MediaSourceDto> MediaSources { get; set; } = [];
    public string? PlaySessionId { get; set; }
    public string? ErrorCode { get; set; }
}

internal sealed class ChapterDto
{
    public string? Name { get; set; }
    public long StartPositionTicks { get; set; }
}

internal sealed class ItemChaptersDto
{
    public List<ChapterDto>? Chapters { get; set; }
}

internal sealed class MediaSegmentDto
{
    public string? Type { get; set; }
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
}

internal sealed class MediaSegmentsResultDto
{
    public List<MediaSegmentDto> Items { get; set; } = [];
}
