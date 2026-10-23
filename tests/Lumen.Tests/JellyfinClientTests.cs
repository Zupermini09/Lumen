using System.Text.Json;
using Lumen.Core.Jellyfin;
using Lumen.Jellyfin;

namespace Lumen.Tests;

public class JellyfinClientTests
{
    [Fact]
    public void Authorization_header_matches_the_jellyfin_format()
    {
        var identity = new ClientIdentity("Lumen", "1.2.3", "My PC", "abc123");
        Assert.Equal("MediaBrowser Client=\"Lumen\", Device=\"My%20PC\", DeviceId=\"abc123\", Version=\"1.2.3\"",
            identity.AuthorizationHeader(null));
        Assert.EndsWith(", Token=\"tok\"", identity.AuthorizationHeader("tok"));
    }

    [Fact]
    public void Authorization_header_escapes_quotes_in_device_names() =>
        Assert.DoesNotContain("\"Bob\"", new ClientIdentity("Lumen", "1", "\"Bob\" PC", "id").AuthorizationHeader(null)[30..]);

    [Theory]
    [InlineData("""{"Id":"1","Name":"Ep","Type":"Episode","IsFolder":false,"MediaType":"Video","RunTimeTicks":600000000,"UserData":{"PlaybackPositionTicks":300000000,"Played":false},"ImageTags":{"Primary":"tag1"},"SeriesName":"Show","SeriesId":"s1","IndexNumber":2,"ParentIndexNumber":1}""")]
    [InlineData("""{"id":"1","name":"Ep","type":"Episode","isFolder":false,"mediaType":"Video","runTimeTicks":600000000,"userData":{"playbackPositionTicks":300000000,"played":false},"imageTags":{"Primary":"tag1"},"seriesName":"Show","seriesId":"s1","indexNumber":2,"parentIndexNumber":1}""")]
    public void Items_parse_in_pascal_and_camel_case(string json)
    {
        var item = JsonSerializer.Deserialize<BaseItemDto>(json, Json.Options)!.ToModel();
        Assert.Equal("Episode", item.Type);
        Assert.True(item.IsPlayable);
        Assert.Equal(TimeSpan.FromMinutes(1), item.RunTime);
        Assert.Equal(TimeSpan.FromSeconds(30), item.PlaybackPosition);
        Assert.Equal("tag1", item.PrimaryImageTag);
        Assert.Equal(1, item.ParentIndexNumber);
    }

    [Fact]
    public void Folders_are_not_playable()
    {
        var item = new MediaItem { Id = "1", Name = "Show", Type = "Series", IsFolder = true };
        Assert.False(item.IsPlayable);
    }

    [Theory]
    [InlineData("movies", "includeItemTypes=Movie")]
    [InlineData("tvshows", "includeItemTypes=Series")]
    [InlineData("music", "includeItemTypes=MusicAlbum")]
    public void Library_roots_list_their_main_type_recursively(string collectionType, string expected)
    {
        var library = new MediaItem { Id = "lib", Name = "L", Type = "CollectionFolder", IsFolder = true, CollectionType = collectionType };
        var query = JellyfinClient.ChildrenQuery(library, "u1");
        Assert.Contains("recursive=true", query);
        Assert.Contains(expected, query);
        Assert.Contains("parentId=lib", query);
    }

    [Fact]
    public void Other_folders_list_direct_children_in_episode_order()
    {
        var season = new MediaItem { Id = "s", Name = "Season 1", Type = "Season", IsFolder = true };
        var query = JellyfinClient.ChildrenQuery(season, "u1");
        Assert.DoesNotContain("recursive", query);
        Assert.Contains("sortBy=ParentIndexNumber,IndexNumber,SortName", query);
    }

    [Fact]
    public void Untrusted_certificate_gets_a_clear_message()
    {
        var error = new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("bad cert"));
        Assert.Contains("certificate is not trusted", JellyfinClient.DescribeConnectionError("media.example.org", error));
    }
}
