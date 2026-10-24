using System.Net;
using System.Text.Json;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Lumen.Jellyfin;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.Tests;

public class ItemDetailsParsingTests
{
    private const string MovieJson = """
        {"Id":"m1","Name":"Film","Type":"Movie","MediaType":"Video","ProductionYear":2014,"RunTimeTicks":6000000000,
         "Overview":"A film.","Genres":["Drama","Sci-Fi"],"OfficialRating":"PG-13","CommunityRating":8.479,
         "Taglines":["","Tag line"],
         "People":[{"Id":"p1","Name":"Actor One","Role":"Hero","Type":"Actor","PrimaryImageTag":"t1"},
                   {"Id":"p2","Name":"Dir","Type":"Director"},{"Id":"","Name":"Broken","Type":"Actor"}],
         "MediaStreams":[{"Index":0,"Type":"Video","DisplayTitle":"1080p HEVC"},{"Index":1,"Type":"Audio","DisplayTitle":"English 5.1","Language":"eng"}],
         "ImageTags":{"Primary":"pt"},"BackdropImageTags":["bd"],
         "UserData":{"PlaybackPositionTicks":3000000000,"Played":false,"IsFavorite":true}}
        """;

    private static ItemDetails Parse(string json) => JsonSerializer.Deserialize<BaseItemDto>(json, Json.Options)!.ToDetails();

    [Fact]
    public void Details_carry_metadata_people_and_user_data()
    {
        var details = Parse(MovieJson);
        Assert.Equal("A film.", details.Item.Overview);
        Assert.Equal(["Drama", "Sci-Fi"], details.Genres);
        Assert.Equal("PG-13", details.OfficialRating);
        Assert.Equal(8.479, details.CommunityRating);
        Assert.Equal("Tag line", details.Tagline);
        Assert.True(details.Item.IsFavorite);
        Assert.Equal(TimeSpan.FromMinutes(5), details.Item.ResumePosition);

        // A person without an id is dropped; a missing role stays null.
        Assert.Equal(["Actor One", "Dir"], details.People.Select(p => p.Name));
        Assert.Equal(new PersonInfo("p1", "Actor One", "Hero", "Actor", "t1"), details.People[0]);
        Assert.Null(details.People[1].Role);

        Assert.Equal(["1080p HEVC", "English 5.1"], details.Streams.Select(s => s.DisplayTitle));
        Assert.Equal(("m1", "bd"), (details.BackdropItemId, details.BackdropTag));
    }

    [Fact]
    public void Episodes_use_the_series_backdrop()
    {
        var details = Parse("""{"Id":"e1","Name":"Ep","Type":"Episode","BackdropImageTags":[],"ParentBackdropItemId":"s1","ParentBackdropImageTags":["sbd"]}""");
        Assert.Equal(("s1", "sbd"), (details.BackdropItemId, details.BackdropTag));
    }

    [Fact]
    public void Items_without_any_backdrop_have_none()
    {
        var details = Parse("""{"Id":"x","Name":"X","Type":"Movie"}""");
        Assert.Null(details.BackdropItemId);
        Assert.Null(details.BackdropTag);
    }

    [Fact]
    public void Streams_fall_back_to_the_first_media_source()
    {
        var details = Parse("""{"Id":"x","Name":"X","Type":"Movie","MediaSources":[{"Id":"a","MediaStreams":[{"Index":0,"Type":"Audio","DisplayTitle":"Deutsch"}]},{"Id":"b"}]}""");
        Assert.Equal("Deutsch", Assert.Single(details.Streams).DisplayTitle);
    }

    [Fact]
    public void Zero_rating_and_blank_official_rating_count_as_missing()
    {
        var details = Parse("""{"Id":"x","Name":"X","Type":"Movie","CommunityRating":0,"OfficialRating":" "}""");
        Assert.Null(details.CommunityRating);
        Assert.Null(details.OfficialRating);
    }

    [Fact]
    public void Series_know_their_status_and_last_year()
    {
        var details = Parse("""{"Id":"s","Name":"S","Type":"Series","Status":"Ended","ProductionYear":2012,"EndDate":"2020-05-16T22:00:00.0000000Z"}""");
        Assert.Equal("Ended", details.Status);
        Assert.Equal(2020, details.EndYear);
    }

    [Fact]
    public void Episode_lists_keep_overview_season_and_resume_data()
    {
        var item = JsonSerializer.Deserialize<BaseItemDto>(
            """{"Id":"e","Name":"Ep","Type":"Episode","SeasonId":"se","Overview":"Text","UserData":{"Played":true}}""", Json.Options)!.ToModel();
        Assert.Equal("se", item.SeasonId);
        Assert.Equal("Text", item.Overview);
        Assert.True(item.Played);
    }
}

public class ItemFormatTests
{
    [Theory]
    [InlineData(2018, 2026, "Continuing", "2018 – Present")]
    [InlineData(2012, 2020, "Ended", "2012 – 2020")]
    [InlineData(2012, 2012, "Ended", "2012")]
    [InlineData(2012, null, null, "2012")]
    [InlineData(null, 2020, "Ended", null)]
    public void Year_ranges(int? year, int? endYear, string? status, string? expected) =>
        Assert.Equal(expected, ItemFormat.YearRange(year, endYear, status));

    [Theory]
    [InlineData(1, 3, "S1:E3")]
    [InlineData(null, 3, "E3")]
    [InlineData(1, null, null)]
    public void Episode_codes(int? season, int? episode, string? expected) =>
        Assert.Equal(expected, ItemFormat.EpisodeCode(season, episode));

    [Fact]
    public void Ratings_have_one_decimal_and_a_dot() => Assert.Equal("8.5", ItemFormat.Rating(8.479));

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(3, false, 0)]       // only just started: play from the beginning
    [InlineData(600, false, 600)]
    [InlineData(600, true, 0)]      // watched: no resume point
    public void Resume_position_ignores_tiny_and_watched_positions(int seconds, bool played, int expected)
    {
        var item = new MediaItem { Id = "x", Name = "x", Type = "Movie", PlaybackPosition = TimeSpan.FromSeconds(seconds), Played = played };
        Assert.Equal(TimeSpan.FromSeconds(expected), item.ResumePosition);
    }
}

public class DetailsViewPartsTests
{
    [Theory]
    [InlineData("Movie", false, ItemTarget.Details)]
    [InlineData("Episode", false, ItemTarget.Details)]
    [InlineData("Series", true, ItemTarget.Series)]
    [InlineData("Season", true, ItemTarget.Series)]
    [InlineData("CollectionFolder", true, ItemTarget.Folder)]
    [InlineData("BoxSet", true, ItemTarget.Folder)]
    [InlineData("MusicAlbum", true, ItemTarget.Folder)]
    [InlineData("MusicVideo", false, ItemTarget.Player)]
    public void Cards_open_the_right_page(string type, bool folder, ItemTarget expected)
    {
        var item = new MediaItem { Id = "x", Name = "x", Type = type, IsFolder = folder, MediaType = folder ? null : "Video", SeriesId = "s" };
        Assert.Equal(expected, ItemNavigation.TargetFor(item));
    }

    [Fact]
    public void A_season_without_its_series_opens_as_a_folder() =>
        Assert.Equal(ItemTarget.Folder, ItemNavigation.TargetFor(new MediaItem { Id = "x", Name = "x", Type = "Season", IsFolder = true }));

    [Fact]
    public void Stream_rows_list_video_audio_and_subtitles()
    {
        var streams = new[]
        {
            new MediaStreamInfo(2, "Subtitle", "srt", "eng", "English", false, false, false),
            new MediaStreamInfo(0, "Video", "hevc", null, "1080p HEVC", true, false, false),
            new MediaStreamInfo(1, "Audio", "eac3", "eng", "English 5.1", true, false, false),
            new MediaStreamInfo(3, "Subtitle", "srt", "nor", "Norsk", false, false, false),
            new MediaStreamInfo(4, "Subtitle", "srt", "nor", "Norsk", false, false, false),
        };
        Assert.Equal(
            [new StreamRow("Video", "1080p HEVC"), new StreamRow("Audio", "English 5.1"), new StreamRow("Subtitles", "English · Norsk")],
            StreamSummary.Build(streams));
    }

    [Fact]
    public void Stream_rows_skip_kinds_the_file_lacks_and_cap_long_subtitle_lists()
    {
        Assert.Empty(StreamSummary.Build([]));

        var many = Enumerable.Range(0, 15).Select(i => new MediaStreamInfo(i, "Subtitle", "srt", null, $"Lang{i}", false, false, false));
        var row = Assert.Single(StreamSummary.Build(many));
        Assert.EndsWith("· +3 more", row.Text);
    }

    [Theory]
    [InlineData("Emma Stone", "ES")]
    [InlineData("Cher", "C")]
    [InlineData("Jean Claude Van Damme", "JD")]
    [InlineData("  ", "")]
    public void Cast_initials(string name, string expected) => Assert.Equal(expected, PersonCardViewModel.InitialsOf(name));
}

/// <summary>Records what the JellyfinClient sends and answers with canned responses.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
        return Task.FromResult(respond(request));
    }
}

public class DetailsClientTests
{
    private static readonly Uri Address = new("http://media.test:8096/");

    private static JellyfinClient CreateClient(RecordingHandler handler)
    {
        var session = new SessionInfo(new ServerInfo("srv", "Server", "10.11.0", Address), new UserInfo("u1", "user", null, false), "token");
        return new JellyfinClient(new HttpClient(handler), new ClientIdentity("Lumen", "1", "Test", "dev"),
            new SessionHolder { Current = session }, new LoggerConfiguration().CreateLogger());
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(false, "DELETE")]
    public async Task Watched_and_favourite_use_post_to_set_and_delete_to_clear(bool value, string method)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var client = CreateClient(handler);

        await client.SetPlayedAsync("item1", value);
        await client.SetFavoriteAsync("item1", value);

        Assert.Equal(
            [(new HttpMethod(method), "/UserPlayedItems/item1?userId=u1"), (new HttpMethod(method), "/UserFavoriteItems/item1?userId=u1")],
            handler.Requests);
    }

    [Fact]
    public async Task Older_servers_get_the_per_user_routes()
    {
        var handler = new RecordingHandler(r => new HttpResponseMessage(r.RequestUri!.AbsolutePath.StartsWith("/Users/") ? HttpStatusCode.OK : HttpStatusCode.NotFound));
        await CreateClient(handler).SetPlayedAsync("item1", true);
        Assert.Equal("/Users/u1/PlayedItems/item1", handler.Requests[^1].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[^1].Method);
    }

    [Fact]
    public async Task A_refused_change_is_an_error()
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SetFavoriteAsync("item1", true));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task Details_request_the_fields_the_page_shows()
    {
        var handler = new RecordingHandler(_ => Json("""{"Id":"m1","Name":"Film","Type":"Movie","Genres":["Drama"]}"""));
        var details = await CreateClient(handler).GetItemDetailsAsync("m1");

        Assert.Equal(["Drama"], details.Genres);
        var path = handler.Requests.Single().Path;
        Assert.StartsWith("/Items/m1?userId=u1&fields=", path);
        foreach (var field in new[] { "Overview", "Genres", "People", "MediaStreams" })
            Assert.Contains(field, path);
    }

    [Fact]
    public async Task Episodes_are_listed_per_season_with_overviews()
    {
        var handler = new RecordingHandler(_ => Json("""{"Items":[{"Id":"e1","Name":"Pilot","Type":"Episode","IndexNumber":1,"Overview":"Text"}],"TotalRecordCount":1}"""));
        var series = new MediaItem { Id = "ser", Name = "S", Type = "Series" };
        var season = new MediaItem { Id = "sea", Name = "Season 1", Type = "Season" };

        var episodes = await CreateClient(handler).GetEpisodesAsync(series, season);

        Assert.Equal("Text", Assert.Single(episodes).Overview);
        var path = handler.Requests.Single().Path;
        Assert.StartsWith("/Shows/ser/Episodes?userId=u1&seasonId=sea", path);
        Assert.Contains("Overview", path);
    }

    [Fact]
    public async Task Next_up_for_a_series_asks_for_one_episode_and_may_be_empty()
    {
        var handler = new RecordingHandler(_ => Json("""{"Items":[],"TotalRecordCount":0}"""));
        var next = await CreateClient(handler).GetSeriesNextUpAsync(new MediaItem { Id = "ser", Name = "S", Type = "Series" });

        Assert.Null(next);
        Assert.Contains("seriesId=ser", handler.Requests.Single().Path);
        Assert.Contains("limit=1", handler.Requests.Single().Path);
    }
}

internal sealed class RecordingNavigator : INavigator
{
    public List<PageViewModel> Pages { get; } = [];
    public bool CanGoBack => false;
    public void Navigate(PageViewModel page) => Pages.Add(page);
    public void NavigateRoot(PageViewModel page) => Pages.Add(page);
    public void GoBack() { }
}

/// <summary>A page that remembers which item it was made for.</summary>
internal sealed class FakePage(string kind, MediaItem item) : PageViewModel
{
    public string Kind => kind;
    public MediaItem Item => item;
    public override string Title => kind;
}

internal sealed class FakePages : IPageFactory
{
    public PageViewModel Connect(string? error = null) => throw new NotSupportedException();
    public PageViewModel Login(ServerInfo server) => throw new NotSupportedException();
    public PageViewModel Home() => throw new NotSupportedException();
    public PageViewModel? SearchPage { get; set; }
    public PageViewModel Search() => SearchPage ?? throw new NotSupportedException();
    public PageViewModel Favorites() => throw new NotSupportedException();
    public PageViewModel Items(MediaItem parent) => new FakePage("Items", parent);
    public PageViewModel Player(MediaItem item) => new FakePage("Player", item);
    public PageViewModel Details(MediaItem item) => new FakePage("Details", item);
    public PageViewModel Series(MediaItem item) => new FakePage("Series", item);
    public PageViewModel PlaybackTest() => throw new NotSupportedException();
    public PageViewModel Settings() => throw new NotSupportedException();
    public PageViewModel About() => throw new NotSupportedException();
}

public class DetailsViewModelTests
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static MediaItem Movie(Func<MediaItem, MediaItem>? change = null)
    {
        var item = new MediaItem { Id = "m1", Name = "Film", Type = "Movie", MediaType = "Video", RunTime = TimeSpan.FromMinutes(100) };
        return change?.Invoke(item) ?? item;
    }

    private sealed class MovieClient(MediaItem item) : StubJellyfinClient
    {
        public bool FailChanges { get; set; }
        public List<string> Calls { get; } = [];

        public override Task<ItemDetails> GetItemDetailsAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ItemDetails { Item = item, Genres = ["Drama", "Comedy"], CommunityRating = 7.25 });

        public override Task SetPlayedAsync(string itemId, bool played, CancellationToken cancellationToken = default)
        {
            Calls.Add($"played={played}");
            return FailChanges ? Task.FromException(new HttpRequestException("no", null, HttpStatusCode.Forbidden)) : Task.CompletedTask;
        }

        public override Task SetFavoriteAsync(string itemId, bool favorite, CancellationToken cancellationToken = default)
        {
            Calls.Add($"favorite={favorite}");
            return FailChanges ? Task.FromException(new HttpRequestException("no", null, HttpStatusCode.Forbidden)) : Task.CompletedTask;
        }
    }

    private static async Task<T> WaitFor<T>(Func<T?> get) where T : class
    {
        for (var i = 0; i < 200; i++)
        {
            if (get() is { } value)
                return value;
            await Task.Delay(10);
        }
        throw new TimeoutException("The page did not finish loading.");
    }

    [Fact]
    public async Task Movie_page_shows_the_loaded_details()
    {
        var page = new ItemDetailsViewModel(Movie(), new MovieClient(Movie()), new RecordingNavigator(), new FakePages(), Log);
        await WaitFor(() => page.Genres);

        Assert.Equal("Drama · Comedy", page.Genres);
        Assert.Equal("★ 7.3", page.CommunityRating);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public void Play_resumes_and_start_over_plays_from_the_beginning()
    {
        var item = Movie(i => i with { PlaybackPosition = TimeSpan.FromMinutes(12) });
        var navigator = new RecordingNavigator();
        var page = new ItemDetailsViewModel(item, new MovieClient(item), navigator, new FakePages(), Log);

        Assert.True(page.CanResume);
        Assert.Equal("Resume from 12:00", page.PlayLabel);

        page.PlayCommand.Execute(null);
        page.StartOverCommand.Execute(null);

        var resumed = Assert.IsType<FakePage>(navigator.Pages[0]);
        var restarted = Assert.IsType<FakePage>(navigator.Pages[1]);
        Assert.Equal(TimeSpan.FromMinutes(12), resumed.Item.ResumePosition);
        Assert.Equal(TimeSpan.Zero, restarted.Item.ResumePosition);
    }

    [Fact]
    public void Without_a_saved_position_the_button_says_play()
    {
        var page = new ItemDetailsViewModel(Movie(), new MovieClient(Movie()), new RecordingNavigator(), new FakePages(), Log);
        Assert.False(page.CanResume);
        Assert.Equal("Play", page.PlayLabel);
    }

    [Fact]
    public async Task Toggling_watched_updates_the_page_and_the_server()
    {
        var item = Movie(i => i with { PlaybackPosition = TimeSpan.FromMinutes(12) });
        var client = new MovieClient(item);
        var page = new ItemDetailsViewModel(item, client, new RecordingNavigator(), new FakePages(), Log);

        await page.ToggleWatchedCommand.ExecuteAsync(null);

        Assert.True(page.IsPlayed);
        Assert.False(page.CanResume); // watched items have no resume point
        Assert.Equal("Mark unwatched", page.WatchedLabel);
        Assert.Contains("played=True", client.Calls);

        await page.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(page.IsPlayed);
        Assert.Contains("played=False", client.Calls);
    }

    [Fact]
    public async Task A_refused_toggle_is_taken_back_with_a_message()
    {
        var item = Movie();
        var client = new MovieClient(item) { FailChanges = true };
        var page = new ItemDetailsViewModel(item, client, new RecordingNavigator(), new FakePages(), Log);

        await page.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(page.IsFavorite);
        Assert.Equal("Favourite", page.FavoriteLabel);
        Assert.NotNull(page.ActionError);
    }

    [Fact]
    public void Episode_pages_link_to_their_series()
    {
        var episode = new MediaItem { Id = "e", Name = "Pilot", Type = "Episode", MediaType = "Video", SeriesId = "s", SeriesName = "Show", ParentIndexNumber = 2, IndexNumber = 5 };
        var navigator = new RecordingNavigator();
        var page = new ItemDetailsViewModel(episode, new MovieClient(episode), navigator, new FakePages(), Log);

        Assert.True(page.HasSeriesLink);
        Assert.Equal("S2:E5", page.EpisodeCode);
        page.OpenSeriesCommand.Execute(null);

        var series = Assert.IsType<FakePage>(Assert.Single(navigator.Pages));
        Assert.Equal(("Series", "s"), (series.Kind, series.Item.Id));
    }

    // ---- Series ----

    private sealed class SeriesClient : StubJellyfinClient
    {
        public MediaItem[] Seasons { get; } =
        [
            new() { Id = "sp", Name = "Specials", Type = "Season", IndexNumber = 0 },
            new() { Id = "s1", Name = "Season 1", Type = "Season", IndexNumber = 1 },
            new() { Id = "s2", Name = "Season 2", Type = "Season", IndexNumber = 2 },
        ];
        public MediaItem? NextUp { get; set; }
        public List<string> EpisodeRequests { get; } = [];

        public override Task<ItemDetails> GetItemDetailsAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ItemDetails { Item = new MediaItem { Id = itemId, Name = "Show", Type = "Series", ProductionYear = 2018 }, Status = "Continuing" });

        public override Task<IReadOnlyList<MediaItem>> GetSeasonsAsync(MediaItem series, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>(Seasons);

        public override Task<MediaItem?> GetSeriesNextUpAsync(MediaItem series, CancellationToken cancellationToken = default) => Task.FromResult(NextUp);

        public override Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(MediaItem series, MediaItem? season, CancellationToken cancellationToken = default)
        {
            EpisodeRequests.Add(season?.Id ?? "all");
            IReadOnlyList<MediaItem> episodes = [new MediaItem { Id = $"{season?.Id}-e1", Name = "One", Type = "Episode", MediaType = "Video", ParentIndexNumber = season?.IndexNumber, IndexNumber = 1, SeasonId = season?.Id }];
            return Task.FromResult(episodes);
        }

        public override Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight) => new($"http://media.test/{itemId}");
    }

    private static async Task<SeriesDetailsViewModel> OpenSeries(SeriesClient client, MediaItem item, RecordingNavigator? navigator = null)
    {
        var page = new SeriesDetailsViewModel(item, client, navigator ?? new RecordingNavigator(), new FakePages(), Log);
        await WaitFor(() => page.Episodes.Count > 0 ? page : null);
        return page;
    }

    [Fact]
    public async Task Series_open_on_the_first_real_season_and_play_the_first_episode()
    {
        var client = new SeriesClient();
        var page = await OpenSeries(client, new MediaItem { Id = "ser", Name = "Show", Type = "Series" });

        Assert.Equal("s1", page.SelectedSeason?.Id); // not the specials
        Assert.Equal("2018 – Present · 2 seasons", page.MetaLine);
        Assert.Equal("Play S1:E1", page.PlayLabel);
        Assert.True(page.HasPlayTarget);
    }

    [Fact]
    public async Task Series_open_on_the_season_of_the_next_episode()
    {
        var next = new MediaItem { Id = "n", Name = "Next", Type = "Episode", SeasonId = "s2", ParentIndexNumber = 2, IndexNumber = 4, PlaybackPosition = TimeSpan.FromMinutes(5), RunTime = TimeSpan.FromMinutes(40) };
        var client = new SeriesClient { NextUp = next };
        var navigator = new RecordingNavigator();
        var page = await OpenSeries(client, new MediaItem { Id = "ser", Name = "Show", Type = "Series" }, navigator);

        Assert.Equal("s2", page.SelectedSeason?.Id);
        Assert.Equal("Resume S2:E4", page.PlayLabel);

        page.PlayCommand.Execute(null);
        Assert.Equal("n", Assert.IsType<FakePage>(Assert.Single(navigator.Pages)).Item.Id);
    }

    [Fact]
    public async Task A_season_opens_the_series_page_with_that_season_selected()
    {
        var season = new MediaItem { Id = "s2", Name = "Season 2", Type = "Season", IsFolder = true, SeriesId = "ser", SeriesName = "Show", IndexNumber = 2 };
        var client = new SeriesClient();
        var page = await OpenSeries(client, season);

        Assert.Equal("Show", page.Title);
        Assert.Equal("s2", page.SelectedSeason?.Id);
        Assert.Equal("s2-e1", page.Episodes.Single().Item.Id);
        Assert.Equal("s2", client.EpisodeRequests[^1]); // the first season is only asked for to find the episode Play starts with
    }

    [Fact]
    public async Task Choosing_a_season_loads_its_episodes_and_opening_one_shows_its_details()
    {
        var client = new SeriesClient();
        var navigator = new RecordingNavigator();
        var page = await OpenSeries(client, new MediaItem { Id = "ser", Name = "Show", Type = "Series" }, navigator);

        page.SelectedSeason = page.Seasons[2];
        var episodes = await WaitFor(() => page.Episodes.FirstOrDefault(e => e.Item.SeasonId == "s2") is not null ? page.Episodes : null);

        Assert.Equal("1. One", episodes[0].Title);
        episodes[0].OpenCommand.Execute(null);
        episodes[0].PlayCommand.Execute(null);
        Assert.Equal(["Details", "Player"], navigator.Pages.Cast<FakePage>().Select(p => p.Kind));
    }
}
