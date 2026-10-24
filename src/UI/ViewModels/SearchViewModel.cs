using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// Search as you type: a short pause after the last key, then one request per kind of item (so a flood of episodes
/// cannot push the movies out), shown as groups of cards. A newer query cancels the one still running.
/// </summary>
public sealed partial class SearchViewModel : PageViewModel, ICardHost
{
    public const int ResultsPerGroup = 24;

    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _search;
    private string _shownTerm = "";

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _error;

    /// <summary>True once results (possibly none) are shown for the current text.</summary>
    [ObservableProperty]
    private bool _hasSearched;

    public SearchViewModel(IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _client = client;
        _navigator = navigator;
        _pages = pages;
        _log = log.ForContext<SearchViewModel>();
    }

    public override string Title => "Search";

    public GroupedCardGrid Results { get; } = new();

    /// <summary>The pause after typing before the server is asked.</summary>
    internal TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(300);

    public bool IsIdle => string.IsNullOrWhiteSpace(Query);
    public bool HasNoResults => HasSearched && Error is null && Results.IsEmpty;
    public string NoResultsText => $"No results for \"{_shownTerm}\".";

    /// <summary>The view should put the cursor into the search box (Ctrl+F or Ctrl+K while this page is open).</summary>
    public event EventHandler? FocusRequested;

    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    public void Open(MediaCardViewModel card) => ItemNavigation.Open(card.Item, _navigator, _pages);

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsIdle));
        _search?.Cancel();
        _search?.Dispose();
        var term = value.Trim();
        if (term.Length == 0)
        {
            _search = null;
            Results.SetGroups([]);
            Error = null;
            HasSearched = false;
            IsSearching = false;
            return;
        }

        var search = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _search = search;
        IsSearching = true;
        _ = SearchAsync(term, search);
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasNoResults));

    partial void OnHasSearchedChanged(bool value) => OnPropertyChanged(nameof(HasNoResults));

    private async Task SearchAsync(string term, CancellationTokenSource search)
    {
        var token = search.Token;
        try
        {
            await Task.Delay(Debounce, token);
            var requests = ItemGroups.All.Select(group => SearchGroupAsync(group, term, token)).ToList();
            var results = await Task.WhenAll(requests);
            token.ThrowIfCancellationRequested();

            if (results.All(r => r.Failure is not null))
            {
                Results.SetGroups([]);
                Error = $"Search failed: {results[0].Failure}";
            }
            else
            {
                Error = null;
                Results.SetGroups(results.Where(r => r.Items.Count > 0)
                    .Select(r => GroupedCardGrid.Cards(r.Group.Title, Rank(r.Items, term), _client)).ToList());
            }

            _shownTerm = term;
            OnPropertyChanged(nameof(NoResultsText));
            HasSearched = true;
            OnPropertyChanged(nameof(HasNoResults));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // A newer search owns the spinner now.
            if (ReferenceEquals(_search, search))
                IsSearching = false;
        }
    }

    // A failing kind (e.g. a server that rejects one type) must not hide the others.
    private async Task<(ItemGroup Group, IReadOnlyList<MediaItem> Items, string? Failure)> SearchGroupAsync(ItemGroup group, string term, CancellationToken token)
    {
        try
        {
            return (group, await _client.SearchAsync(term, group.Types, ResultsPerGroup, token), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !token.IsCancellationRequested)) // the request timed out
        {
            _log.Warning("Search for {Group} failed: {Error}", group.Title, ex.Message);
            return (group, [], ex.Message);
        }
    }

    /// <summary>Names that start with the text come before names that merely contain it; otherwise the server's order stays.</summary>
    internal static IReadOnlyList<MediaItem> Rank(IReadOnlyList<MediaItem> items, string term) =>
        items.OrderBy(i => i.Name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ToList();

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}
