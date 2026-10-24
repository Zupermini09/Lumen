using System.Net;
using System.Text.Json;
using Lumen.Core;
using Lumen.Core.Jellyfin;
using Lumen.Jellyfin;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.Tests;

public class BrowseQueryTests
{
    private static readonly MediaItem Movies = new() { Id = "lib", Name = "Movies", Type = "CollectionFolder", IsFolder = true, CollectionType = "movies" };
    private static readonly MediaItem BoxSet = new() { Id = "box", Name = "Set", Type = "BoxSet", IsFolder = true };

    [Fact]
    public void A_library_without_options_still_sorts_by_name()
    {
        var plain = JellyfinClient.ChildrenQuery(Movies, "u1");
        Assert.Contains("sortBy=SortName&sortOrder=Ascending", plain);
        // The default choice is the same request, so an untouched library page loads as before.
        Assert.Equal(plain, JellyfinClient.ChildrenQuery(Movies, "u1", new ItemListOptions()));
    }

    [Theory]
    [InlineData(ItemSort.Name, false, "sortBy=SortName&sortOrder=Ascending")]
    [InlineData(ItemSort.DateAdded, true, "sortBy=DateCreated,SortName&sortOrder=Descending")]
    [InlineData(ItemSort.ReleaseDate, true, "sortBy=PremiereDate,ProductionYear,SortName&sortOrder=Descending")]
    [InlineData(ItemSort.CommunityRating, true, "sortBy=CommunityRating,SortName&sortOrder=Descending")]
    [InlineData(ItemSort.Runtime, false, "sortBy=Runtime,SortName&sortOrder=Ascending")]
    public void Libraries_sort_as_asked(ItemSort sort, bool descending, string expected)
    {
        var query = JellyfinClient.ChildrenQuery(Movies, "u1", new ItemListOptions { Sort = sort, Descending = descending });
        Assert.Contains(expected, query);
        Assert.DoesNotContain("filters=", query);
        Assert.DoesNotContain("genres=", query);
    }

    [Fact]
    public void Filters_are_passed_to_the_server()
    {
        var options = new ItemListOptions { Played = PlayedFilter.Unplayed, FavoritesOnly = true, Genres = ["Action", "Sci-Fi & Fantasy"] };
        var query = JellyfinClient.ChildrenQuery(Movies, "u1", options);

        Assert.Contains("&filters=IsUnplayed,IsFavorite", query);
        // Genres are separated by "|" (names may contain commas) and escaped like any value.
        Assert.Contains("&genres=Action%7CSci-Fi%20%26%20Fantasy", query);
        Assert.Contains("recursive=true", query);
    }

    [Fact]
    public void Played_and_unplayed_are_separate_filters() =>
        Assert.Contains("&filters=IsPlayed", JellyfinClient.FilterQuery(new ItemListOptions { Played = PlayedFilter.Played }));

    [Fact]
    public void Folders_keep_their_order_unless_a_sort_was_chosen()
    {
        Assert.Contains("sortBy=ParentIndexNumber,IndexNumber,SortName", JellyfinClient.ChildrenQuery(BoxSet, "u1", new ItemListOptions()));
        var sorted = JellyfinClient.ChildrenQuery(BoxSet, "u1", new ItemListOptions { Sort = ItemSort.ReleaseDate });
        Assert.Contains("sortBy=PremiereDate", sorted);
        Assert.DoesNotContain("recursive", sorted);
    }

    [Fact]
    public void Genres_are_listed_for_the_librarys_main_type()
    {
        var query = JellyfinClient.GenresQuery(Movies, "u1");
        Assert.Contains("parentId=lib", query);
        Assert.Contains("includeItemTypes=Movie", query);
        Assert.DoesNotContain("includeItemTypes", JellyfinClient.GenresQuery(BoxSet, "u1"));
    }

    [Fact]
    public void Search_escapes_the_text_and_lists_the_types()
    {
        var query = JellyfinClient.SearchQuery("tom & jerry", ["MusicVideo", "Video"], 24, "u1");
        Assert.Contains("searchTerm=tom%20%26%20jerry", query);
        Assert.Contains("includeItemTypes=MusicVideo,Video", query);
        Assert.Contains("recursive=true", query);
        Assert.Contains("limit=24", query);
    }

    [Fact]
    public void Favourites_ask_for_the_favourite_filter_across_all_libraries()
    {
        var query = JellyfinClient.FavoritesQuery(["Movie", "Series"], "u1");
        Assert.Contains("filters=IsFavorite", query);
        Assert.Contains("recursive=true", query);
        Assert.Contains("includeItemTypes=Movie,Series", query);
        Assert.DoesNotContain("parentId", query);
    }

    [Fact]
    public void Options_survive_the_settings_file()
    {
        var settings = new AppSettings();
        settings.LibraryViews["lib"] = new ItemListOptions { Sort = ItemSort.Runtime, Descending = true, Played = PlayedFilter.Played, Genres = ["Drama"] };

        var text = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(text)!.LibraryViews["lib"];

        Assert.Equal((ItemSort.Runtime, true, PlayedFilter.Played), (back.Sort, back.Descending, back.Played));
        Assert.Equal(["Drama"], back.Genres);
        Assert.True(back.HasFilters);
        Assert.False(back.IsDefault);
        Assert.Contains("\"Runtime\"", text); // names, not numbers, so reordering the enum cannot change a saved choice
    }

    [Fact]
    public void Sorting_alone_is_not_a_filter()
    {
        var options = new ItemListOptions { Sort = ItemSort.DateAdded, Descending = true };
        Assert.False(options.HasFilters);
        Assert.False(options.IsDefault);
        Assert.True(new ItemListOptions().IsDefault);
    }
}

public class BrowseClientTests
{
    private static readonly Uri Address = new("http://media.test:8096/");

    private static JellyfinClient CreateClient(RecordingHandler handler)
    {
        var session = new SessionInfo(new ServerInfo("srv", "Server", "10.11.0", Address), new UserInfo("u1", "user", null, false), "token");
        return new JellyfinClient(new HttpClient(handler), new ClientIdentity("Lumen", "1", "Test", "dev"),
            new SessionHolder { Current = session }, new LoggerConfiguration().CreateLogger());
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static readonly MediaItem Library = new() { Id = "lib", Name = "TV", Type = "CollectionFolder", IsFolder = true, CollectionType = "tvshows" };

    [Fact]
    public async Task Latest_items_come_back_as_a_bare_array()
    {
        var handler = new RecordingHandler(_ => Json("""[{"Id":"a","Name":"Show","Type":"Series"},{"Id":"b","Name":"Film","Type":"Movie"}]"""));
        var items = await CreateClient(handler).GetLatestAsync(Library, 16);

        Assert.Equal(["Show", "Film"], items.Select(i => i.Name));
        var path = handler.Requests.Single().Path;
        Assert.StartsWith("/Items/Latest?userId=u1&parentId=lib&limit=16", path);
    }

    [Fact]
    public async Task Latest_items_fall_back_to_the_per_user_route_on_older_servers()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/Users/") ? Json("[]") : new HttpResponseMessage(HttpStatusCode.NotFound));
        var items = await CreateClient(handler).GetLatestAsync(Library, 16);

        Assert.Empty(items);
        Assert.StartsWith("/Users/u1/Items/Latest?parentId=lib", handler.Requests[^1].Path);
    }

    [Fact]
    public async Task Genres_are_the_names_of_the_returned_items()
    {
        var handler = new RecordingHandler(_ => Json("""{"Items":[{"Id":"g1","Name":"Action"},{"Id":"g2","Name":"Drama"},{"Id":"g3"}],"TotalRecordCount":3}"""));
        var genres = await CreateClient(handler).GetGenresAsync(Library);

        Assert.Equal(["Action", "Drama"], genres);
        Assert.StartsWith("/Genres?userId=u1&parentId=lib", handler.Requests.Single().Path);
    }

    [Fact]
    public async Task Search_and_favourites_parse_item_lists()
    {
        var handler = new RecordingHandler(_ => Json("""{"Items":[{"Id":"m","Name":"Film","Type":"Movie","UserData":{"IsFavorite":true}}],"TotalRecordCount":1}"""));
        var client = CreateClient(handler);

        var found = Assert.Single(await client.SearchAsync("fil", ["Movie"], 10));
        var favourite = Assert.Single(await client.GetFavoritesAsync(["Movie"]));

        Assert.True(found.IsFavorite);
        Assert.Equal("m", favourite.Id);
        Assert.Contains("searchTerm=fil", handler.Requests[0].Path);
        Assert.Contains("filters=IsFavorite", handler.Requests[1].Path);
    }

    [Fact]
    public async Task Children_are_requested_with_the_chosen_options()
    {
        var handler = new RecordingHandler(_ => Json("""{"Items":[],"TotalRecordCount":0}"""));
        await CreateClient(handler).GetChildrenAsync(Library, new ItemListOptions { Sort = ItemSort.CommunityRating, Descending = true });

        var path = handler.Requests.Single().Path;
        Assert.Contains("sortBy=CommunityRating,SortName&sortOrder=Descending", path);
    }
}

internal sealed class MemorySettings : ISettingsStore
{
    public AppSettings Settings { get; } = new();
    public int Saves { get; private set; }
    public void Save() => Saves++;
}

public class LibraryOptionsViewModelTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();
    private static readonly MediaItem Library = new() { Id = "lib", Name = "Movies", Type = "CollectionFolder", IsFolder = true, CollectionType = "movies" };

    private sealed class LibraryClient : StubJellyfinClient
    {
        public List<ItemListOptions?> Requests { get; } = [];
        public IReadOnlyList<string> Genres { get; set; } = ["Action", "Drama"];
        public TaskCompletionSource<IReadOnlyList<MediaItem>>? Gate { get; set; }

        public override Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, ItemListOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(options);
            if (Gate is { } gate)
            {
                Gate = null; // only the first call waits
                return gate.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<MediaItem> items = [new MediaItem { Id = "m1", Name = "Film", Type = "Movie", MediaType = "Video" }];
            return Task.FromResult(items);
        }

        public override Task<IReadOnlyList<string>> GetGenresAsync(MediaItem library, CancellationToken cancellationToken = default) => Task.FromResult(Genres);
        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private static ItemsViewModel Create(LibraryClient client, MemorySettings settings, MediaItem? parent = null) =>
        new(parent ?? Library, client, new RecordingNavigator(), new FakePages(), settings, Log);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "The page did not get there in time.");
    }

    [Fact]
    public async Task A_new_library_page_loads_by_name_and_offers_the_genres()
    {
        var client = new LibraryClient();
        var page = Create(client, new MemorySettings());
        await WaitUntil(() => page.HasGenres && !page.IsLoading);

        Assert.True(page.HasOptions);
        Assert.True(Assert.Single(client.Requests)!.IsDefault);
        Assert.Equal(["Action", "Drama"], page.Genres.Select(g => g.Name));
        Assert.Equal("Genres", page.GenreLabel);
        Assert.False(page.HasFilters);
    }

    [Fact]
    public async Task Choosing_a_sort_reloads_and_is_remembered_for_the_library()
    {
        var client = new LibraryClient();
        var settings = new MemorySettings();
        var page = Create(client, settings);
        await WaitUntil(() => !page.IsLoading);

        page.SelectedSort = SortChoice.All.Single(s => s.Sort == ItemSort.DateAdded);
        page.ToggleDirectionCommand.Execute(null);
        await WaitUntil(() => client.Requests.Count == 3 && !page.IsLoading);

        var last = client.Requests[^1]!;
        Assert.Equal((ItemSort.DateAdded, true), (last.Sort, last.Descending));
        Assert.Equal("Descending", page.DirectionLabel);
        Assert.Equal(ItemSort.DateAdded, settings.Settings.LibraryViews["lib"].Sort);
        Assert.True(settings.Saves >= 2);

        // Opening the library again (a new page) starts with the saved choice, in its first request.
        var again = new LibraryClient();
        var reopened = Create(again, settings);
        await WaitUntil(() => !reopened.IsLoading);
        Assert.Equal((ItemSort.DateAdded, true), (again.Requests[0]!.Sort, again.Requests[0]!.Descending));
        Assert.Equal(SortChoice.All[1], reopened.SelectedSort);
        Assert.True(reopened.IsDescending);
    }

    [Fact]
    public async Task Filters_apply_together_and_can_be_cleared_in_one_load()
    {
        var client = new LibraryClient();
        var page = Create(client, new MemorySettings());
        await WaitUntil(() => page.HasGenres && !page.IsLoading);

        page.SelectedPlayed = PlayedChoice.All.Single(p => p.Filter == PlayedFilter.Unplayed);
        page.FavoritesOnly = true;
        page.Genres[1].IsSelected = true;
        await WaitUntil(() => client.Requests.Count == 4 && !page.IsLoading);

        var filtered = client.Requests[^1]!;
        Assert.Equal((PlayedFilter.Unplayed, true, 1), (filtered.Played, filtered.FavoritesOnly, filtered.Genres.Count));
        Assert.Equal("Drama", page.GenreLabel);
        Assert.True(page.HasFilters);

        page.ClearFiltersCommand.Execute(null);
        await WaitUntil(() => client.Requests.Count == 5 && !page.IsLoading);
        Assert.False(client.Requests[^1]!.HasFilters);
        Assert.False(page.HasFilters);
        Assert.Equal("Genres", page.GenreLabel);
    }

    [Fact]
    public async Task A_saved_genre_filter_is_in_the_first_request_before_the_genre_list_arrives()
    {
        var settings = new MemorySettings();
        settings.Settings.LibraryViews["lib"] = new ItemListOptions { Genres = ["Drama"] };
        var client = new LibraryClient();
        var page = Create(client, settings);
        await WaitUntil(() => page.HasGenres && !page.IsLoading);

        Assert.Equal(["Drama"], client.Requests[0]!.Genres);
        Assert.True(page.Genres.Single(g => g.Name == "Drama").IsSelected);
        Assert.Equal(1, page.Genres.Count(g => g.IsSelected));
    }

    [Fact]
    public async Task Going_back_to_the_default_forgets_the_saved_choice()
    {
        var settings = new MemorySettings();
        var client = new LibraryClient();
        var page = Create(client, settings);
        await WaitUntil(() => !page.IsLoading);

        page.FavoritesOnly = true;
        Assert.True(settings.Settings.LibraryViews.ContainsKey("lib"));
        page.FavoritesOnly = false;
        Assert.False(settings.Settings.LibraryViews.ContainsKey("lib"));
    }

    [Fact]
    public async Task A_newer_choice_replaces_a_load_still_running()
    {
        var client = new LibraryClient { Gate = new TaskCompletionSource<IReadOnlyList<MediaItem>>() };
        var page = Create(client, new MemorySettings());
        await WaitUntil(() => client.Requests.Count == 1);

        page.FavoritesOnly = true;
        await WaitUntil(() => client.Requests.Count == 2 && !page.IsLoading);

        Assert.Equal("1 item", page.Summary);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public void Seasons_and_albums_have_no_sort_and_filter_bar_and_save_nothing()
    {
        var season = new MediaItem { Id = "s", Name = "Season 1", Type = "Season", IsFolder = true };
        var settings = new MemorySettings();
        var client = new LibraryClient();
        var page = Create(client, settings, season);

        Assert.False(page.HasOptions);
        page.FavoritesOnly = true; // cannot happen from the UI; must not touch the settings or the request
        Assert.Empty(settings.Settings.LibraryViews);
    }

    [Fact]
    public async Task An_empty_filtered_library_says_so()
    {
        var client = new EmptyClient();
        var page = new ItemsViewModel(Library, client, new RecordingNavigator(), new FakePages(), new MemorySettings(), Log);
        await WaitUntil(() => !page.IsLoading);
        Assert.True(page.IsEmpty);
        Assert.Equal("Nothing here yet.", page.EmptyText);

        page.FavoritesOnly = true;
        await WaitUntil(() => !page.IsLoading);
        Assert.Equal("Nothing matches these filters.", page.EmptyText);
    }

    private sealed class EmptyClient : StubJellyfinClient
    {
        public override Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, ItemListOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>([]);
        public override Task<IReadOnlyList<string>> GetGenresAsync(MediaItem library, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
