using System.Net;
using Lumen.Core.Jellyfin;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.Tests;

public class GroupedCardGridTests
{
    private sealed class ImageClient : StubJellyfinClient
    {
        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private static MediaItem Item(string id, string type) => new() { Id = id, Name = id, Type = type };

    [Fact]
    public void Items_are_grouped_in_display_order_and_unknown_types_are_left_out()
    {
        var groups = GroupedCardGrid.Group(
            [Item("e1", "Episode"), Item("m1", "Movie"), Item("x", "Person"), Item("m2", "Movie"), Item("v", "MusicVideo")], new ImageClient());

        Assert.Equal(["Movies", "Episodes", "Videos"], groups.Select(g => g.Title));
        Assert.Equal(["m1", "m2"], groups[0].Cards.Select(c => c.Item.Id));
    }

    [Fact]
    public void Rows_hold_as_many_cards_as_fit_under_a_heading_per_group()
    {
        var grid = new GroupedCardGrid();
        var client = new ImageClient();
        grid.SetGroups(
        [
            GroupedCardGrid.Cards("Movies", Enumerable.Range(0, 7).Select(i => Item($"m{i}", "Movie")).ToList(), client),
            GroupedCardGrid.Cards("Episodes", Enumerable.Range(0, 3).Select(i => Item($"e{i}", "Episode")).ToList(), client),
        ]);
        Assert.Empty(grid.Rows); // the width is not known yet

        // Posters are 150 wide and episodes 260, plus 16 between cards: 560 fits three posters and two episode cards.
        grid.SetWidth(560);

        Assert.Equal(new GroupHeaderRow("Movies", 7), grid.Rows[0]);
        Assert.Equal([3, 3, 1], grid.Rows.OfType<CardRow>().Take(3).Select(r => r.Cards.Count));
        Assert.Equal(new GroupHeaderRow("Episodes", 3), grid.Rows[4]);
        Assert.Equal([2, 1], grid.Rows.Skip(5).Cast<CardRow>().Select(r => r.Cards.Count));
        Assert.False(grid.IsEmpty);
        Assert.Equal(10, grid.CardCount);

        // Wider: all seven posters fit in one row (7 * 150 + 6 * 16 = 1146) and the episodes in one.
        grid.SetWidth(1150);
        Assert.Equal([7, 3], grid.Rows.OfType<CardRow>().Select(r => r.Cards.Count));
    }

    [Fact]
    public void An_empty_grid_has_no_rows()
    {
        var grid = new GroupedCardGrid();
        grid.SetWidth(800);
        grid.SetGroups([]);
        Assert.Empty(grid.Rows);
        Assert.True(grid.IsEmpty);
    }

    [Theory]
    [InlineData(150, 150, 1)]
    [InlineData(10, 150, 1)]
    [InlineData(316, 150, 2)]
    [InlineData(315, 150, 1)]
    public void Columns_fit_cards_and_spacing(double width, double card, int expected) =>
        Assert.Equal(expected, GroupedCardGrid.ColumnsFor(width, card));
}

public class SearchViewModelTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private sealed class SearchClient : StubJellyfinClient
    {
        public List<(string Term, string Types)> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Func<string, string, IReadOnlyList<MediaItem>> Respond { get; set; } = (_, _) => [];
        public Func<string, string, Exception?> Fail { get; set; } = (_, _) => null;
        public TaskCompletionSource? Gate { get; set; }

        public override async Task<IReadOnlyList<MediaItem>> SearchAsync(string term, IReadOnlyList<string> itemTypes, int limit, CancellationToken cancellationToken = default)
        {
            var types = string.Join(',', itemTypes);
            lock (Requests)
            {
                Requests.Add((term, types));
                Tokens.Add(cancellationToken);
            }

            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            if (Fail(term, types) is { } error)
                throw error;
            return Respond(term, types);
        }

        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private static SearchViewModel Create(SearchClient client) =>
        new(client, new RecordingNavigator(), new FakePages(), Log) { Debounce = TimeSpan.FromMilliseconds(40) };

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "The page did not get there in time.");
    }

    private static MediaItem Movie(string name) => new() { Id = name, Name = name, Type = "Movie", MediaType = "Video" };

    [Fact]
    public void A_new_search_page_waits_for_something_to_be_typed()
    {
        var page = Create(new SearchClient());
        Assert.True(page.IsIdle);
        Assert.False(page.HasNoResults);
        Assert.False(page.IsSearching);
    }

    [Fact]
    public async Task Typing_searches_once_per_kind_after_the_pause_and_groups_the_results()
    {
        var client = new SearchClient
        {
            Respond = (_, types) => types switch
            {
                "Movie" => [Movie("Batman Begins"), Movie("The Batman")],
                "Episode" => [new MediaItem { Id = "e", Name = "Pilot", Type = "Episode", SeriesName = "Bat Show" }],
                _ => [],
            },
        };
        var page = Create(client);

        page.Query = "bat";
        Assert.True(page.IsSearching);
        Assert.Empty(client.Requests); // still inside the pause
        await WaitUntil(() => page.HasSearched);

        Assert.Equal(ItemGroups.All.Count, client.Requests.Count);
        Assert.All(client.Requests, r => Assert.Equal("bat", r.Term));
        Assert.Contains(client.Requests, r => r.Types == "MusicVideo,Video");
        Assert.False(page.IsSearching);
        page.Results.SetWidth(800);
        Assert.Equal(["Movies", "Episodes"], page.Results.Rows.OfType<GroupHeaderRow>().Select(h => h.Title));
        // Names that start with the text come first.
        Assert.Equal(["Batman Begins", "The Batman"], page.Results.Rows.OfType<CardRow>().First().Cards.Select(c => c.Item.Name));
    }

    [Fact]
    public async Task Typing_on_cancels_the_search_that_is_still_running_and_only_the_last_one_shows()
    {
        var client = new SearchClient { Gate = new TaskCompletionSource() };
        var page = Create(client);

        page.Query = "a";
        await WaitUntil(() => client.Requests.Count == ItemGroups.All.Count);
        var firstTokens = client.Tokens.ToList();

        client.Gate = null;
        client.Respond = (term, types) => types == "Movie" ? [Movie("Alien")] : [];
        page.Query = "al";
        await WaitUntil(() => page.HasSearched && !page.IsSearching);

        Assert.All(firstTokens, t => Assert.True(t.IsCancellationRequested));
        page.Results.SetWidth(800);
        Assert.Equal("Alien", page.Results.Rows.OfType<CardRow>().Single().Cards.Single().Item.Name);
    }

    [Fact]
    public async Task Quick_typing_inside_the_pause_sends_only_the_last_text()
    {
        var client = new SearchClient();
        var page = Create(client);

        page.Query = "b";
        page.Query = "ba";
        page.Query = "bat";
        await WaitUntil(() => page.HasSearched);

        Assert.All(client.Requests, r => Assert.Equal("bat", r.Term));
    }

    [Fact]
    public async Task No_matches_says_so_and_clearing_the_text_resets_the_page()
    {
        var page = Create(new SearchClient());
        page.Query = "zzz";
        await WaitUntil(() => page.HasSearched);

        Assert.True(page.HasNoResults);
        Assert.Equal("No results for \"zzz\".", page.NoResultsText);
        Assert.True(page.Results.IsEmpty);

        page.Query = "  ";
        Assert.True(page.IsIdle);
        Assert.False(page.HasNoResults);
        Assert.False(page.HasSearched);
        Assert.False(page.IsSearching);
    }

    [Fact]
    public async Task When_every_request_fails_the_page_shows_an_error()
    {
        var client = new SearchClient { Fail = (_, _) => new HttpRequestException("500 Internal Server Error", null, HttpStatusCode.InternalServerError) };
        var page = Create(client);
        page.Query = "x";
        await WaitUntil(() => page.Error is not null);

        Assert.Contains("500", page.Error);
        Assert.False(page.HasNoResults);
        Assert.False(page.IsSearching);
    }

    [Fact]
    public async Task One_failing_kind_does_not_hide_the_others()
    {
        var client = new SearchClient
        {
            Fail = (_, types) => types == "Series" ? new HttpRequestException("boom") : null,
            Respond = (_, types) => types == "Movie" ? [Movie("Hit")] : [],
        };
        var page = Create(client);
        page.Query = "h";
        await WaitUntil(() => page.HasSearched);

        Assert.Null(page.Error);
        page.Results.SetWidth(800);
        Assert.Equal("Hit", page.Results.Rows.OfType<CardRow>().Single().Cards.Single().Item.Name);
    }

    [Fact]
    public async Task Opening_a_result_goes_through_item_navigation()
    {
        var navigator = new RecordingNavigator();
        var client = new SearchClient { Respond = (_, types) => types == "Movie" ? [Movie("Hit")] : [] };
        var page = new SearchViewModel(client, navigator, new FakePages(), Log) { Debounce = TimeSpan.FromMilliseconds(20) };
        page.Query = "hit";
        await WaitUntil(() => page.HasSearched);
        page.Results.SetWidth(800);

        page.Open(page.Results.Rows.OfType<CardRow>().Single().Cards.Single());

        Assert.Equal("Details", Assert.IsType<FakePage>(Assert.Single(navigator.Pages)).Kind);
    }

    [Fact]
    public void Focus_requests_reach_the_view()
    {
        var page = Create(new SearchClient());
        var requests = 0;
        page.FocusRequested += (_, _) => requests++;
        page.RequestFocus();
        Assert.Equal(1, requests);
    }
}

public class FavoritesViewModelTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private sealed class FavoritesClient : StubJellyfinClient
    {
        public List<MediaItem> Items { get; set; } =
        [
            new() { Id = "m", Name = "Film", Type = "Movie", IsFavorite = true },
            new() { Id = "s", Name = "Show", Type = "Series", IsFavorite = true },
        ];
        public int Requests { get; private set; }
        public bool Fail { get; set; }
        public IReadOnlyList<string> RequestedTypes { get; private set; } = [];

        public override Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(IReadOnlyList<string> itemTypes, CancellationToken cancellationToken = default)
        {
            Requests++;
            RequestedTypes = itemTypes;
            return Fail ? Task.FromException<IReadOnlyList<MediaItem>>(new HttpRequestException("down")) : Task.FromResult<IReadOnlyList<MediaItem>>(Items.ToList());
        }

        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "The page did not get there in time.");
    }

    [Fact]
    public async Task Favourites_are_listed_by_kind()
    {
        var client = new FavoritesClient();
        var page = new FavoritesViewModel(client, new RecordingNavigator(), new FakePages(), Log);
        await WaitUntil(() => !page.IsLoading);
        page.Results.SetWidth(800);

        Assert.Equal(["Movies", "Shows"], page.Results.Rows.OfType<GroupHeaderRow>().Select(h => h.Title));
        Assert.Equal("2 items", page.Summary);
        Assert.False(page.IsEmpty);
        Assert.Equal(ItemGroups.AllTypes, client.RequestedTypes);
    }

    [Fact]
    public async Task No_favourites_is_an_empty_page_not_an_error()
    {
        var page = new FavoritesViewModel(new FavoritesClient { Items = [] }, new RecordingNavigator(), new FakePages(), Log);
        await WaitUntil(() => !page.IsLoading);

        Assert.True(page.IsEmpty);
        Assert.Null(page.Error);
    }

    [Fact]
    public async Task A_failed_load_shows_an_error_and_a_failed_refresh_keeps_the_list()
    {
        var client = new FavoritesClient { Fail = true };
        var page = new FavoritesViewModel(client, new RecordingNavigator(), new FakePages(), Log);
        await WaitUntil(() => !page.IsLoading);
        Assert.Contains("down", page.Error);
        Assert.False(page.IsEmpty);

        client.Fail = false;
        page.RefreshCommand.Execute(null);
        await WaitUntil(() => page.Error is null && !page.IsLoading);
        page.Results.SetWidth(800);
        var shown = page.Results.Rows.OfType<CardRow>().First().Cards[0];

        client.Fail = true;
        page.OnNavigatedTo();
        await WaitUntil(() => client.Requests == 3);
        await Task.Delay(30);
        Assert.Null(page.Error); // silent
        Assert.Same(shown, page.Results.Rows.OfType<CardRow>().First().Cards[0]);
    }

    [Fact]
    public async Task Coming_back_refreshes_in_place_only_when_something_changed()
    {
        var client = new FavoritesClient();
        var page = new FavoritesViewModel(client, new RecordingNavigator(), new FakePages(), Log);
        await WaitUntil(() => !page.IsLoading);
        page.Results.SetWidth(800);
        var rows = page.Results.Rows.ToList();

        page.OnNavigatedTo();
        await WaitUntil(() => client.Requests == 2);
        await Task.Delay(30);
        Assert.Equal(rows, page.Results.Rows); // same row objects: nothing was rebuilt

        client.Items.RemoveAt(0); // unfavourited on the details page
        page.OnNavigatedTo();
        await WaitUntil(() => page.Summary == "1 item");
        Assert.Equal(["Shows"], page.Results.Rows.OfType<GroupHeaderRow>().Select(h => h.Title));
    }
}

public class HomeViewModelTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static MediaItem Library(string id, string name, string? type) =>
        new() { Id = id, Name = name, Type = "CollectionFolder", IsFolder = true, CollectionType = type };

    private sealed class HomeClient : StubJellyfinClient
    {
        public List<MediaItem> Libraries { get; set; } =
        [
            Library("movies", "Movies", "movies"),
            Library("tv", "TV Shows", "tvshows"),
            Library("live", "Live TV", "livetv"),
            Library("lists", "Playlists", "playlists"),
        ];
        public List<MediaItem> Resume { get; set; } =
        [
            new() { Id = "r1", Name = "Resume me", Type = "Movie", MediaType = "Video", PlaybackPosition = TimeSpan.FromMinutes(5), RunTime = TimeSpan.FromMinutes(90) },
            new() { Id = "r2", Name = "Then this", Type = "Movie", MediaType = "Video", PlaybackPosition = TimeSpan.FromMinutes(1), RunTime = TimeSpan.FromMinutes(90) },
        ];
        public List<MediaItem> NextUp { get; set; } = [];
        public Dictionary<string, List<MediaItem>> Latest { get; } = new()
        {
            ["movies"] = [new() { Id = "m1", Name = "New film", Type = "Movie" }],
            ["tv"] = [new() { Id = "s1", Name = "New show", Type = "Series" }],
        };
        public HashSet<string> FailLatest { get; } = [];
        public bool FailAll { get; set; }
        public List<string> LatestRequests { get; } = [];
        public int LoadCount { get; private set; }

        public override Task<IReadOnlyList<MediaItem>> GetLibrariesAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return FailAll ? Task.FromException<IReadOnlyList<MediaItem>>(new HttpRequestException("offline")) : Task.FromResult<IReadOnlyList<MediaItem>>(Libraries.ToList());
        }

        public override Task<IReadOnlyList<MediaItem>> GetResumeItemsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>(Resume.ToList());

        public override Task<IReadOnlyList<MediaItem>> GetNextUpAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>(NextUp.ToList());

        public override Task<IReadOnlyList<MediaItem>> GetLatestAsync(MediaItem library, int limit, CancellationToken cancellationToken = default)
        {
            LatestRequests.Add(library.Id);
            if (FailLatest.Contains(library.Id))
                return Task.FromException<IReadOnlyList<MediaItem>>(new HttpRequestException("nope"));
            return Task.FromResult<IReadOnlyList<MediaItem>>(Latest.GetValueOrDefault(library.Id)?.ToList() ?? []);
        }

        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private sealed class Sessions : ISessionService
    {
        public SessionInfo? Current => null;
        public IReadOnlyList<SavedServer> RecentServers => [];
        public Task<SessionInfo?> TryRestoreAsync(CancellationToken cancellationToken = default) => Task.FromResult<SessionInfo?>(null);
        public void RememberServer(ServerInfo server) { }
        public void SignIn(SessionInfo session) { }
        public Task SignOutAsync() => Task.CompletedTask;
    }

    private static HomeViewModel Create(HomeClient client, RecordingNavigator? navigator = null, FakePages? pages = null) =>
        new(client, new Sessions(), navigator ?? new RecordingNavigator(), pages ?? new FakePages(), Log);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "The page did not get there in time.");
    }

    private static string[] VisibleTitles(HomeViewModel page) => page.Rows.Where(r => r.HasCards).Select(r => r.Title).ToArray();

    [Fact]
    public async Task Home_lists_the_fixed_rows_then_the_latest_of_each_library()
    {
        var client = new HomeClient();
        var page = Create(client);
        await WaitUntil(() => page.Rows.Count(r => r.HasCards) == 3);

        // The libraries are tiles of their own; the item watched last is the featured card, not repeated in its row.
        Assert.Equal(["Continue watching", "Latest in Movies", "Latest in TV Shows"], VisibleTitles(page));
        Assert.Equal(["Then this"], page.Rows.Single(r => r.Key == "resume").Cards.Select(c => c.Title));
        Assert.Equal(4, page.Libraries.Cards.Count);
        Assert.Equal("Resume me", page.Featured?.Title);
        // Live TV and playlists have no "latest" row, and are not even asked.
        Assert.Equal(["movies", "tv"], client.LatestRequests.Order());
        Assert.False(page.IsLoading);
    }

    [Theory]
    [InlineData("movies", true)]
    [InlineData("tvshows", true)]
    [InlineData("music", true)]
    [InlineData(null, true)]
    [InlineData("livetv", false)]
    [InlineData("playlists", false)]
    [InlineData("boxsets", false)]
    public void Only_media_libraries_have_a_latest_row(string? collectionType, bool expected) =>
        Assert.Equal(expected, HomeViewModel.HasLatestRow(Library("x", "X", collectionType)));

    [Fact]
    public async Task A_failing_latest_row_is_just_missing()
    {
        var client = new HomeClient();
        client.FailLatest.Add("movies");
        var page = Create(client);
        await WaitUntil(() => page.Rows.Any(r => r.Key == "latest:tv" && r.HasCards));

        Assert.Equal(["Continue watching", "Latest in TV Shows"], VisibleTitles(page));
        Assert.Null(page.Error);
    }

    [Fact]
    public async Task Coming_back_updates_continue_watching_without_touching_the_rows_that_did_not_change()
    {
        var client = new HomeClient();
        var page = Create(client);
        await WaitUntil(() => page.Rows.Count(r => r.HasCards) == 3);
        var media = page.Libraries.Cards;
        var latest = page.Rows.Single(r => r.Key == "latest:movies").Cards;
        var resume = page.Rows.Single(r => r.Key == "resume");
        var before = resume.Cards;

        // Played to the end meanwhile: gone from Continue watching, and the next episode is up.
        client.Resume.Clear();
        client.NextUp.Add(new MediaItem { Id = "n1", Name = "Next", Type = "Episode", SeriesName = "Show" });
        page.OnNavigatedTo();
        await WaitUntil(() => page.Rows.Any(r => r.Key == "nextup" && r.HasCards));

        Assert.False(resume.HasCards);
        Assert.NotSame(before, resume.Cards);
        Assert.Same(media, page.Libraries.Cards);   // same card objects: no reload, no flicker
        Assert.Same(latest, page.Rows.Single(r => r.Key == "latest:movies").Cards);
        Assert.Equal(["Next up", "Latest in Movies", "Latest in TV Shows"], VisibleTitles(page));
        Assert.Equal("Show", page.Featured?.Title); // nothing to resume any more: the next episode is featured
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task Rows_keep_their_places_when_a_row_appears_later()
    {
        var client = new HomeClient();
        client.Resume.Clear();
        var page = Create(client);
        await WaitUntil(() => page.Rows.Count(r => r.HasCards) == 2);

        client.NextUp.Add(new MediaItem { Id = "n1", Name = "Next", Type = "Episode" });
        page.OnNavigatedTo();
        await WaitUntil(() => page.Rows.Any(r => r.Key == "nextup"));
        client.Resume.Add(new MediaItem { Id = "r1", Name = "Again", Type = "Movie" });
        page.OnNavigatedTo();
        await WaitUntil(() => page.Rows.Any(r => r.Key == "resume"));

        Assert.Equal(["resume", "nextup", "latest:movies", "latest:tv"], page.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task A_removed_library_loses_its_row_and_a_failed_refresh_changes_nothing()
    {
        var client = new HomeClient();
        var page = Create(client);
        await WaitUntil(() => page.Rows.Count(r => r.HasCards) == 3);

        client.FailAll = true;
        page.OnNavigatedTo();
        await WaitUntil(() => client.LoadCount == 2);
        await Task.Delay(30);
        Assert.Null(page.Error); // a silent refresh does not turn a working page into an error
        Assert.Equal(3, page.Rows.Count(r => r.HasCards));

        client.FailAll = false;
        client.Libraries.RemoveAll(l => l.Id == "tv");
        page.OnNavigatedTo();
        await WaitUntil(() => page.Rows.All(r => r.Key != "latest:tv"));
        Assert.Equal(["Continue watching", "Latest in Movies"], VisibleTitles(page));
    }

    [Fact]
    public async Task A_first_load_that_fails_shows_the_error()
    {
        var page = Create(new HomeClient { FailAll = true });
        await WaitUntil(() => !page.IsLoading);
        Assert.Contains("offline", page.Error);
    }

    [Fact]
    public async Task The_featured_card_resumes_the_item_watched_last()
    {
        var navigator = new RecordingNavigator();
        var page = Create(new HomeClient(), navigator);
        await WaitUntil(() => page.HasFeatured);

        Assert.Equal("Continue watching", page.Featured!.Kicker);
        Assert.Equal("Resume 5:00", page.Featured.ActionLabel);
        Assert.Equal(5d / 90, page.Featured.Progress, 3);

        page.PlayFeaturedCommand.Execute(null);
        var played = Assert.IsType<FakePage>(Assert.Single(navigator.Pages));
        Assert.Equal(("Player", "r1"), (played.Kind, played.Item.Id));
        Assert.Equal(TimeSpan.FromMinutes(5), played.Item.ResumePosition);

        page.OpenFeaturedCommand.Execute(null);
        Assert.Equal("Details", Assert.IsType<FakePage>(navigator.Pages[1]).Kind);
    }

    [Fact]
    public void Featured_choice_prefers_resume_then_next_up_then_something_new()
    {
        MediaItem Item(string id) => new() { Id = id, Name = id, Type = "Movie" };
        Assert.Equal(("r", "Continue watching"), Choose([Item("r")], [Item("n")], [Item("l")]));
        Assert.Equal(("n", "Next up"), Choose([], [Item("n")], [Item("l")]));
        Assert.Equal(("l", "New"), Choose([], [], [Item("l")]));
        Assert.Null(HomeViewModel.ChooseFeatured([], [], []));

        static (string, string)? Choose(MediaItem[] resume, MediaItem[] nextUp, MediaItem[] latest) =>
            HomeViewModel.ChooseFeatured(resume, nextUp, latest) is { } c ? (c.Item.Id, c.Kicker) : null;
    }

    [Fact]
    public async Task Without_anything_to_resume_the_featured_card_plays_from_the_start_or_opens_a_series()
    {
        var client = new HomeClient();
        client.Resume.Clear();
        client.Latest["movies"] = [new() { Id = "s1", Name = "New show", Type = "Series" }];
        client.Latest.Remove("tv");
        var navigator = new RecordingNavigator();
        var page = Create(client, navigator);
        await WaitUntil(() => page.Featured is { Kicker: "New" });

        Assert.Equal("Open", page.Featured!.ActionLabel);
        Assert.False(page.Featured.CanPlay);
        page.PlayFeaturedCommand.Execute(null);
        Assert.Equal("Series", Assert.IsType<FakePage>(Assert.Single(navigator.Pages)).Kind);

        Assert.Equal("Play", FeaturedViewModel.Create(new MediaItem { Id = "m", Name = "M", Type = "Movie", MediaType = "Video" }, "New", client).ActionLabel);
    }

    [Fact]
    public async Task Typing_in_the_home_search_hands_the_text_to_the_search_page()
    {
        var navigator = new RecordingNavigator();
        var searchPage = new SearchViewModel(new HomeClient(), navigator, new FakePages(), Log);
        var page = Create(new HomeClient(), navigator, new FakePages { SearchPage = searchPage });
        await WaitUntil(() => !page.IsLoading);

        page.SearchCommand.Execute(null); // nothing typed: nothing happens
        Assert.Empty(navigator.Pages);

        page.SearchText = "  alien ";
        page.SearchCommand.Execute(null);

        Assert.Same(searchPage, Assert.Single(navigator.Pages));
        Assert.Equal("alien", searchPage.Query);

        page.OnNavigatedTo(); // back on Home the box is empty again
        Assert.Equal("", page.SearchText);
    }

    [Theory]
    [InlineData(6, "Good morning, Ada")]
    [InlineData(13, "Good afternoon, Ada")]
    [InlineData(21, "Good evening, Ada")]
    [InlineData(2, "Good evening, Ada")]
    public void The_greeting_follows_the_time_of_day(int hour, string expected) =>
        Assert.Equal(expected, HomeViewModel.GreetingFor(new DateTime(2026, 1, 1, hour, 0, 0), "Ada"));

    [Fact]
    public void The_greeting_works_without_a_name() =>
        Assert.Equal("Good morning", HomeViewModel.GreetingFor(new DateTime(2026, 1, 1, 8, 0, 0), null));
}
