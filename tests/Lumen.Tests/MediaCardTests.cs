using Lumen.Core.Jellyfin;
using Lumen.UI.ViewModels;

namespace Lumen.Tests;

public class MediaCardTests
{
    [Fact]
    public void Episodes_show_series_and_episode_number()
    {
        var episode = new MediaItem { Id = "e", Name = "Pilot", Type = "Episode", SeriesName = "Show", ParentIndexNumber = 1, IndexNumber = 3 };
        Assert.Equal(("Show", "S1:E3 · Pilot"), MediaCardViewModel.Describe(episode));
    }

    [Fact]
    public void Movies_show_their_year()
    {
        var movie = new MediaItem { Id = "m", Name = "Film", Type = "Movie", ProductionYear = 2024 };
        Assert.Equal(("Film", "2024"), MediaCardViewModel.Describe(movie));
    }

    [Theory]
    [InlineData("Movie", CardShape.Poster)]
    [InlineData("Series", CardShape.Poster)]
    [InlineData("Episode", CardShape.Wide)]
    [InlineData("CollectionFolder", CardShape.Wide)]
    [InlineData("MusicAlbum", CardShape.Square)]
    public void Card_shape_follows_item_type(string type, CardShape expected) =>
        Assert.Equal(expected, MediaCardViewModel.ShapeFor(new MediaItem { Id = "x", Name = "x", Type = type }));
}
