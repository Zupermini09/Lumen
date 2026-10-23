using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>One titled row of cards on the Home page. Empty rows are hidden.</summary>
public sealed partial class HomeRow : ObservableObject
{
    private IReadOnlyList<MediaItem> _items = [];

    public HomeRow(string key, string title)
    {
        Key = key;
        Title = title;
    }

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
        Greeting = sessions.Current is { } s ? $"{s.User.Name} on {s.Server.Name}" : "";
        _ = LoadAsync(silent: false);
    }

    public override string Title => "Home";
    public string Greeting { get; }

    /// <summary>My media, Continue watching, Next up, then "Latest in ..." for each library, in that order.</summary>
    public ObservableCollection<HomeRow> Rows { get; } = [];

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(silent: false);

    public void Open(MediaCardViewModel card) => ItemNavigation.Open(card.Item, _navigator, _pages);

    // Back from a page that can change what Home shows (playing something moves it in or out of Continue watching).
    // The rows update in place, and only where something changed.
    public override void OnNavigatedTo() => _ = LoadAsync(silent: true);

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
            Row("media", "My media", 0).Update(views, _client, CardShape.Wide);

            // The "latest" rows load alongside; they appear (in library order) as they arrive.
            var latest = views.Where(HasLatestRow).Select(v => (Library: v, Items: Optional(_client.GetLatestAsync(v, LatestLimit, token)))).ToList();
            SetLatestRows(latest.Select(l => l.Library));

            if (await Optional(resume) is { } resumeItems)
                Row("resume", "Continue watching", 1).Update(resumeItems, _client, CardShape.Wide);
            if (await Optional(nextUp) is { } nextUpItems)
                Row("nextup", "Next up", 2).Update(nextUpItems, _client, CardShape.Wide);
            Error = null;
            if (!token.IsCancellationRequested)
                IsLoading = false;

            foreach (var (library, items) in latest)
                if (await items is { } latestItems)
                    Row($"latest:{library.Id}", $"Latest in {library.Name}", 0).Update(latestItems, _client);
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

    // The row with this key, created at its place if it is new. Fixed rows come first (index), then the latest rows.
    private HomeRow Row(string key, string title, int index)
    {
        if (Rows.FirstOrDefault(r => r.Key == key) is { } existing)
            return existing;
        var row = new HomeRow(key, title);
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
            Row($"latest:{library.Id}", $"Latest in {library.Name}", 0).Title = $"Latest in {library.Name}";
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
