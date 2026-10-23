using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

public enum CardShape
{
    Poster, // 2:3 movies, shows, seasons
    Wide,   // 16:9 episodes, libraries, continue watching
    Square, // albums, tracks
}

/// <summary>One tile in a grid or row: an item with its title, subtitle, image and progress.</summary>
public sealed class MediaCardViewModel
{
    private MediaCardViewModel(MediaItem item, CardShape shape)
    {
        Item = item;
        Shape = shape;
        (Width, ImageHeight) = shape switch
        {
            CardShape.Poster => (150d, 225d),
            CardShape.Wide => (260d, 146d),
            _ => (160d, 160d),
        };
    }

    public MediaItem Item { get; }
    public CardShape Shape { get; }
    public double Width { get; }
    public double ImageHeight { get; }
    public string Title { get; private init; } = "";
    public string? Subtitle { get; private init; }
    public Uri? ImageUri { get; private init; }

    /// <summary>Watched fraction 0..1 for partly watched items.</summary>
    public double Progress { get; private init; }
    public bool HasProgress => Progress > 0;
    public bool IsPlayed => Item.Played;

    public static CardShape ShapeFor(MediaItem item) => item.Type switch
    {
        "Episode" or "CollectionFolder" or "UserView" or "Video" or "MusicVideo" => CardShape.Wide,
        "MusicAlbum" or "Audio" or "MusicArtist" or "Playlist" => CardShape.Square,
        _ => CardShape.Poster,
    };

    public static MediaCardViewModel Create(MediaItem item, IJellyfinClient client, CardShape? shape = null)
    {
        var cardShape = shape ?? ShapeFor(item);
        var (title, subtitle) = Describe(item);
        var progress = item.RunTime is { Ticks: > 0 } runTime && item.PlaybackPosition > TimeSpan.Zero && !item.Played
            ? Math.Clamp(item.PlaybackPosition / runTime, 0, 1)
            : 0;

        return new MediaCardViewModel(item, cardShape)
        {
            Title = title,
            Subtitle = subtitle,
            Progress = progress,
            ImageUri = ImageFor(item, cardShape, client),
        };
    }

    internal static (string Title, string? Subtitle) Describe(MediaItem item) => item.Type switch
    {
        "Episode" => (
            item.SeriesName ?? item.Name,
            item is { ParentIndexNumber: { } season, IndexNumber: { } episode }
                ? $"S{season}:E{episode} · {item.Name}"
                : item.Name),
        "Audio" => (item.IndexNumber is { } track ? $"{track}. {item.Name}" : item.Name, item.AlbumArtist),
        "MusicAlbum" => (item.Name, item.AlbumArtist),
        _ => (item.Name, item.ProductionYear?.ToString()),
    };

    private static Uri? ImageFor(MediaItem item, CardShape shape, IJellyfinClient client)
    {
        var height = shape == CardShape.Wide ? 146 : shape == CardShape.Poster ? 225 : 160;
        // Request at 2x for high-DPI screens; the server scales it down before sending.
        height *= 2;

        if (shape == CardShape.Wide && item.ThumbImageTag is { } thumb)
            return client.GetImageUri(item.Id, ImageKind.Thumb, thumb, height);
        if (item.PrimaryImageTag is { } primary)
            return client.GetImageUri(item.Id, ImageKind.Primary, primary, height);
        if (item.SeriesId is { } seriesId && item.SeriesPrimaryImageTag is { } seriesTag)
            return client.GetImageUri(seriesId, ImageKind.Primary, seriesTag, height);
        if (item.AlbumId is { } albumId && item.AlbumPrimaryImageTag is { } albumTag)
            return client.GetImageUri(albumId, ImageKind.Primary, albumTag, height);
        return null;
    }
}

/// <summary>A row of cards in a virtualized grid (the list virtualizes rows; each row lays out its cards).</summary>
public sealed record CardRow(IReadOnlyList<MediaCardViewModel> Cards);

/// <summary>A page that shows cards and opens them when clicked.</summary>
public interface ICardHost
{
    void Open(MediaCardViewModel card);
}
