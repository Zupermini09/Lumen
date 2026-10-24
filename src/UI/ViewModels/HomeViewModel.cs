using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>One titled row of cards on the Home page. Empty rows are hidden.</summary>
public sealed partial class HomeRow : ObservableObject
{
    private IReadOnlyList<MediaItem> _items = [];

    public HomeRow(string key, string title, Action<MediaItem>? openLibrary = null, MediaItem? library = null)
    {
        Key = key;
        Title = title;
        if (openLibrary is not null && library is not null)
            SeeAllCommand = new RelayCommand(() => openLibrary(library));
    }

    /// <summary>"See all": opens the library behind the row; null for rows that have no page of their own.</summary>
    public IRelayCommand? SeeAllCommand { get; }

    public bool HasSeeAll => SeeAllCommand is not null;

    /// <summary>Identifies the row across refreshes ("resume", "latest:{library id}"...).</summary>
    public string Key { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    private IReadOnlyList<MediaCardViewModel> _cards = [];

    public bool HasCards => Cards.Count > 0;

    /// <summary>Shows new items; false (and nothing touched, so no flicker) when they are the ones already shown.</summary>
    public bool Update(IReadOnlyList<MediaItem> items, IJellyfinClient client, CardShape? shape = null)
    {
        if (items.SequenceEqual(_items))
            return false;
        _items = items;
        var cardShape = shape ?? (items.Count == 0
            ? CardShape.Poster
            : items.GroupBy(MediaCardViewModel.ShapeFor).OrderByDescending(g => g.Count()).First().Key);
        Cards = items.Select(i => MediaCardViewModel.Create(i, client, cardShape)).ToList();
        return true;
    }
}

/// <summary>The big card at the top of Home: backdrop, title, progress and the Resume (or Play) button.</summary>
public sealed class FeaturedViewModel
{
    private FeaturedViewModel(MediaItem item, string kicker)
    {
        Item = item;
        Kicker = kicker;
    }

    public MediaItem Item { get; }

    /// <summary>Where the item comes from: "Continue watching", "Next up" or "New".</summary>
    public string Kicker { get; }

    public string Title { get; private init; } = "";
    public string? Subtitle { get; private init; }
    public Uri? ImageUri { get; private init; }
    public double Progress { get; private init; }
    public bool HasProgress => Progress > 0;

    /// <summary>"Resume 1:08:56", "Play", or "Open" for items that do not play (a series).</summary>
    public string ActionLabel { get; private init; } = "Play";

    /// <summary>False for folders and series: the main button then opens the item, as the cards do.</summary>
    public bool CanPlay => Item.IsPlayable;

    public static FeaturedViewModel Create(MediaItem item, string kicker, IJellyfinClient client)
    {
        var (title, subtitle) = MediaCardViewModel.Describe(item);
        var resume = item.ResumePosition;
        const int height = 640; // shown 320 high, at 2x for high-DPI screens
        var image = item switch
        {
            { BackdropItemId: { } id, BackdropTag: { } tag } => client.GetImageUri(id, ImageKind.Backdrop, tag, height),
            { ThumbImageTag: { } thumb } => client.GetImageUri(item.Id, ImageKind.Thumb, thumb, height),
            { PrimaryImageTag: { } primary } => client.GetImageUri(item.Id, ImageKind.Primary, primary, height),
            { SeriesId: { } seriesId, SeriesPrimaryImageTag: { } seriesTag } => client.GetImageUri(seriesId, ImageKind.Primary, seriesTag, height),
            _ => null,
        };
        return new FeaturedViewModel(item, kicker)
        {
            Title = title,
            Subtitle = subtitle,
            ImageUri = image,
            Progress = item.RunTime is { Ticks: > 0 } runTime && resume > TimeSpan.Zero ? Math.Clamp(resume / runTime, 0, 1) : 0,
            ActionLabel = !item.IsPlayable ? "Open" : resume > TimeSpan.Zero ? $"Resume {TimeFormat.Playback(resume)}" : "Play",
        };
    }
}

public sealed partial class HomeViewModel : PageViewModel, ICardHost
{
    public const int RowLimit = 12;
    public const int LatestLimit = 16;

    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _error;

    public HomeViewModel(IJellyfinClient client, ISessionService sessions, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _client = client;
        _navigator = navigator;
        _pages = pages;
        _log = log.ForContext<HomeViewModel>();
        Greeting = GreetingFor(DateTime.Now, sessions.Current?.User.Name);
        ServerName = sessions.Current?.Server.Name ?? "";
        _ = LoadAsync(silent: false);
    }

    public override string Title => "Home";

    /// <summary>"Good evening, Ada".</summary>
    public string Greeting { get; }

    /// <summary>Shown under the greeting.</summary>
    public string ServerName { get; }

    /// <summary>What is typed into the search box at the top; Enter hands it to the Search page.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>The big card: the latest thing being watched, else what is next up, else something new.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatured))]
    private FeaturedViewModel? _featured;

    public bool HasFeatured => Featured is not null;

    /// <summary>The libraries, as tiles.</summary>
    public HomeRow Libraries { get; } = new("media", "My media");

    /// <summary>Continue watching (without the featured item), Next up, then "Latest in ..." for each library, in that order.</summary>
    public ObservableCollection<HomeRow> Rows { get; } = [];

    private IReadOnlyList<MediaItem> _resume = [];
    private IReadOnlyList<MediaItem> _nextUp = [];
    private readonly Dictionary<string, MediaItem> _latestFirst = [];

    internal static string GreetingFor(DateTime now, string? user)
    {
        var part = now.Hour switch { >= 5 and < 12 => "Good morning", >= 12 and < 18 => "Good afternoon", _ => "Good evening" };
        return string.IsNullOrWhiteSpace(user) ? part : $"{part}, {user}";
    }

    /// <summary>The item for the big card and where it comes from; null when there is nothing at all.</summary>
    internal static (MediaItem Item, string Kicker)? ChooseFeatured(IReadOnlyList<MediaItem> resume, IReadOnlyList<MediaItem> nextUp, IEnumerable<MediaItem> latest)
    {
        if (resume.Count > 0)
            return (resume[0], "Continue watching");
        if (nextUp.Count > 0)
            return (nextUp[0], "Next up");
        return latest.FirstOrDefault() is { } item ? (item, "New") : null;
    }

    [RelayCommand]
    private void Search()
    {
        var term = SearchText.Trim();
        if (term.Length == 0 || _pages.Search() is not SearchViewModel search)
            return;
        search.Query = term;
        _navigator.Navigate(search);
    }

    [RelayCommand]
    private void PlayFeatured()
    {
        if (Featured is { } featured)
            ItemNavigation.Open(featured.Item, _navigator, _pages, play: true);
    }

    [RelayCommand]
    private void OpenFeatured()
    {
        if (Featured is { } featured)
            ItemNavigation.Open(featured.Item, _navigator, _pages);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(silent: false);

    public void Open(MediaCardViewModel card) => Open(card.Item);

    private void Open(MediaItem item) => ItemNavigation.Open(item, _navigator, _pages);

    // Back from a page that can change what Home shows (playing something moves it in or out of Continue watching).
    // The rows update in place, and only where something changed.
    public override void OnNavigatedTo()
    {
        SearchText = "";
        _ = LoadAsync(silent: true);
    }

    /// <summary>Like the web client: live TV, playlists and collections have no "latest" row.</summary>
    internal static bool HasLatestRow(MediaItem library) => library.CollectionType is not ("livetv" or "playlists" or "boxsets");

    private async Task LoadAsync(bool silent)
    {
        _load?.Cancel();
        _load?.Dispose();
        var load = _load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = load.Token;
        if (!silent)
        {
            IsLoading = true;
            Error = null;
        }

        try
        {
            var libraries = _client.GetLibrariesAsync(token);
            var resume = _client.GetResumeItemsAsync(RowLimit, token);
            var nextUp = _client.GetNextUpAsync(RowLimit, token);

            var views = await libraries;
            Libraries.Update(views, _client, CardShape.Wide);

            // The "latest" rows load alongside; they appear (in library order) as they arrive.
            var latest = views.Where(HasLatestRow).Select(v => (Library: v, Items: Optional(_client.GetLatestAsync(v, LatestLimit, token)))).ToList();
            SetLatestRows(latest.Select(l => l.Library));

            if (await Optional(resume) is { } resumeItems)
                _resume = resumeItems;
            if (await Optional(nextUp) is { } nextUpItems)
                _nextUp = nextUpItems;
            ShowFeaturedAndRows();
            Error = null;
            if (!token.IsCancellationRequested)
                IsLoading = false;

            foreach (var (library, items) in latest)
                if (await items is { } latestItems)
                {
                    Row($"latest:{library.Id}", $"Latest in {library.Name}", 0, library).Update(latestItems, _client);
                    if (latestItems.Count > 0)
                        _latestFirst[library.Id] = latestItems[0];
                    else
                        _latestFirst.Remove(library.Id);
                    if (_resume.Count == 0 && _nextUp.Count == 0)
                        ShowFeaturedAndRows();
                }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Warning("Home failed to load: {Error}", ex.Message);
            // A failed refresh keeps what is shown; only a first load has nothing to fall back on.
            if (!silent)
                Error = $"Could not load your libraries: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_load, load))
                IsLoading = false;
        }
    }

    // The featured card, and Continue watching without the item that is featured (it would show twice).
    private void ShowFeaturedAndRows()
    {
        var chosen = ChooseFeatured(_resume, _nextUp, _latestFirst.Values);
        if (chosen is null)
            Featured = null;
        else if (Featured is null || Featured.Item != chosen.Value.Item || Featured.Kicker != chosen.Value.Kicker)
            Featured = FeaturedViewModel.Create(chosen.Value.Item, chosen.Value.Kicker, _client);

        var shownInResume = Featured is { Kicker: "Continue watching" } ? _resume.Skip(1).ToList() : _resume;
        Row("resume", "Continue watching", 0).Update(shownInResume, _client, CardShape.Wide);
        Row("nextup", "Next up", 1).Update(_nextUp, _client, CardShape.Wide);
    }

    // The row with this key, created at its place if it is new. Fixed rows come first (index), then the latest rows.
    private HomeRow Row(string key, string title, int index, MediaItem? library = null)
    {
        if (Rows.FirstOrDefault(r => r.Key == key) is { } existing)
            return existing;
        var row = new HomeRow(key, title, Open, library);
        Rows.Insert(key.StartsWith("latest:", StringComparison.Ordinal) ? Rows.Count : Math.Min(index, Rows.Count(r => !r.Key.StartsWith("latest:", StringComparison.Ordinal))), row);
        return row;
    }

    // Rows for libraries that are gone disappear; new libraries get a row at the end, in library order.
    private void SetLatestRows(IEnumerable<MediaItem> libraries)
    {
        var keys = libraries.Select(l => $"latest:{l.Id}").ToHashSet();
        foreach (var gone in Rows.Where(r => r.Key.StartsWith("latest:", StringComparison.Ordinal) && !keys.Contains(r.Key)).ToList())
            Rows.Remove(gone);
        foreach (var library in libraries)
            Row($"latest:{library.Id}", $"Latest in {library.Name}", 0, library).Title = $"Latest in {library.Name}";
    }

    // A failing secondary row (e.g. Next Up on a server without TV libraries) must not hide the libraries;
    // null means "keep what is shown".
    private async Task<IReadOnlyList<MediaItem>?> Optional(Task<IReadOnlyList<MediaItem>> load)
    {
        try
        {
            return await load;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Information("Home row failed to load: {Error}", ex.Message);
            return null;
        }
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}
