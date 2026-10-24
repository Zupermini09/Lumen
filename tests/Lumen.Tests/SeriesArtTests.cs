using System.Text.Json;
using Lumen.Core.Jellyfin;
using Lumen.Jellyfin;
using Lumen.UI.ViewModels;

namespace Lumen.Tests;

public class SeriesArtTests
{
    private sealed class UriClient : StubJellyfinClient
    {
        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) =>
            new($"http://x/{itemId}/{kind}/{tag}");
    }

    private static MediaItem Parse(string json) => JsonSerializer.Deserialize<BaseItemDto>(json, Json.Options)!.ToModel();

    private static MediaItem Episode(Func<MediaItem, MediaItem>? tweak = null)
    {
        var episode = new MediaItem
        {
            Id = "e", Name = "The Dragon", Type = "Episode", SeriesId = "s", SeriesName = "Show", ParentIndexNumber = 6, IndexNumber = 19,
            PrimaryImageTag = "still",
            SeriesThumbItemId = "s", SeriesThumbTag = "sthumb",
            SeriesBackdropItemId = "s", SeriesBackdropTag = "sback",
            SeriesPrimaryImageTag = "sprim",
        };
        return tweak?.Invoke(episode) ?? episode;
    }

    private static string? CardImage(MediaItem item, bool seriesArt = true) =>
        MediaCardViewModel.Create(item, new UriClient(), CardShape.Wide, seriesArt).ImageUri?.AbsolutePath;

    [Fact]
    public void Dto_maps_the_series_thumb_and_backdrop()
    {
        var item = Parse("""
            {"Id":"e","Type":"Episode","SeriesId":"s","ImageTags":{"Primary":"still"},"SeriesThumbImageTag":"t1",
             "ParentBackdropItemId":"s","ParentBackdropImageTags":["b1"]}
            """);
        Assert.Equal(("s", "t1"), (item.SeriesThumbItemId, item.SeriesThumbTag));
        Assert.Equal(("s", "b1"), (item.SeriesBackdropItemId, item.SeriesBackdropTag));
    }

    [Fact]
    public void Dto_falls_back_to_the_inherited_parent_thumb()
    {
        var item = Parse("""{"Id":"e","Type":"Episode","ParentThumbItemId":"p","ParentThumbImageTag":"t2"}""");
        Assert.Equal(("p", "t2"), (item.SeriesThumbItemId, item.SeriesThumbTag));
    }

    [Fact]
    public void Episode_in_continue_watching_shows_the_series_thumb() =>
        Assert.Equal("/s/Thumb/sthumb", CardImage(Episode()));

    [Fact]
    public void Series_art_falls_back_from_thumb_to_backdrop_to_primary_to_the_still()
    {
        var noThumb = Episode(e => e with { SeriesThumbItemId = null, SeriesThumbTag = null });
        Assert.Equal("/s/Backdrop/sback", CardImage(noThumb));
        var noBackdrop = noThumb with { SeriesBackdropItemId = null, SeriesBackdropTag = null };
        Assert.Equal("/s/Primary/sprim", CardImage(noBackdrop));
        Assert.Equal("/e/Primary/still", CardImage(noBackdrop with { SeriesPrimaryImageTag = null }));
    }

    [Fact]
    public void Other_cards_keep_the_episode_still() =>
        Assert.Equal("/e/Primary/still", CardImage(Episode(), seriesArt: false));

    [Fact]
    public void Series_art_leaves_movies_alone()
    {
        var movie = new MediaItem { Id = "m", Name = "Film", Type = "Movie", PrimaryImageTag = "p", SeriesThumbItemId = "s", SeriesThumbTag = "t" };
        Assert.Equal("/m/Primary/p", CardImage(movie));
    }

    [Fact]
    public void Series_art_keeps_the_episode_caption_and_progress()
    {
        var card = MediaCardViewModel.Create(Episode(e => e with { RunTime = TimeSpan.FromMinutes(40), PlaybackPosition = TimeSpan.FromMinutes(10) }),
            new UriClient(), CardShape.Wide, seriesArt: true);
        Assert.Equal("Show", card.Title);
        Assert.Equal("S6:E19 · The Dragon", card.Subtitle);
        Assert.Equal(0.25, card.Progress, 3);
    }

    [Fact]
    public void Featured_card_prefers_the_series_backdrop_for_episodes() =>
        Assert.Equal("/s/Backdrop/sback", FeaturedViewModel.Create(Episode(), "Next up", new UriClient()).ImageUri?.AbsolutePath);
}
