using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// The contents of a library, show, season, album or folder, as a virtualized grid. Libraries can be sorted and filtered;
/// the choice is remembered per library.
/// </summary>
public sealed partial class ItemsViewModel : PageViewModel, ICardHost
{
    public const double CardSpacing = 16;

    private readonly MediaItem _parent;
    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ISettingsStore _settings;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private IReadOnlyList<MediaCardViewModel> _cards = [];
    private int _columns = 1;
    private IReadOnlyList<string> _unlistedGenres = []; // saved genre filters until the genre list has loaded
    private bool _applyingOptions; // a batch of changes (clearing the filters) loads once, at the end

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private SortChoice _selectedSort = SortChoice.All[0];

    [ObservableProperty]
    private bool _isDescending;

    [ObservableProperty]
    private PlayedChoice _selectedPlayed = PlayedChoice.All[0];

    [ObservableProperty]
    private bool _favoritesOnly;

    public ItemsViewModel(MediaItem parent, IJellyfinClient client, INavigator navigator, IPageFactory pages, ISettingsStore settings, ILogger log)
    {
        _parent = parent;
        _client = client;
        _navigator = navigator;
        _pages = pages;
        _settings = settings;
        _log = log.ForContext<ItemsViewModel>();

        if (HasOptions)
        {
            if (settings.Settings.LibraryViews.TryGetValue(parent.Id, out var saved))
            {
                ShowOptions(saved);
                _unlistedGenres = saved.Genres;
            }

            _ = LoadGenresAsync();
        }

        _ = LoadAsync();
    }

    public override string Title => _parent.Name;

    /// <summary>Rows of cards; the list virtualizes rows, so only visible cards exist (and load images).</summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>Libraries (and collections) can be sorted and filtered; seasons, albums and the like keep their order.</summary>
    public bool HasOptions => _parent.Type is "CollectionFolder" or "UserView" or "BoxSet";

    public IReadOnlyList<SortChoice> SortChoices => SortChoice.All;
    public IReadOnlyList<PlayedChoice> PlayedChoices => PlayedChoice.All;

    /// <summary>The genres of the library; ticked ones filter the list.</summary>
    public ObservableCollection<GenreChoice> Genres { get; } = [];

    public bool HasGenres => Genres.Count > 0;

    public string GenreLabel => SelectedGenres().ToList() switch
    {
        [] => "Genres",
        [var one] => one,
        var many => $"{many.Count} genres",
    };

    public string DirectionLabel => IsDescending ? "Descending" : "Ascending";
    public bool HasFilters => CurrentOptions().HasFilters;

    public bool IsEmpty => !IsLoading && Error is null && _cards.Count == 0;
    public string EmptyText => HasFilters ? "Nothing matches these filters." : "Nothing here yet.";

    /// <summary>The big spinner is for an empty page; a reload over shown cards only gets a thin bar.</summary>
    public bool ShowSpinner => IsLoading && _cards.Count == 0;
    public bool ShowProgressBar => IsLoading && _cards.Count > 0;

    /// <summary>Width of one card plus spacing, so the view can work out how many fit in a row.</summary>
    public double CardSlotWidth => (_cards.FirstOrDefault()?.Width ?? 150) + CardSpacing;

    /// <summary>Called by the view when its width changes.</summary>
    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == _columns && Rows.Count > 0)
            return;
        _columns = columns;
        RebuildRows();
    }

    public void Open(MediaCardViewModel card) => ItemNavigation.Open(card.Item, _navigator, _pages);

    /// <summary>The sort and filter as the bar currently shows them.</summary>
    internal ItemListOptions CurrentOptions() => new()
    {
        Sort = SelectedSort.Sort,
        Descending = IsDescending,
        Played = SelectedPlayed.Filter,
        FavoritesOnly = FavoritesOnly,
        Genres = SelectedGenres().ToList(),
    };

    private IEnumerable<string> SelectedGenres() => Genres.Where(g => g.IsSelected).Select(g => g.Name).Concat(_unlistedGenres);

    partial void OnSelectedSortChanged(SortChoice value) => OptionsChanged();
    partial void OnIsDescendingChanged(bool value) => OptionsChanged();
    partial void OnSelectedPlayedChanged(PlayedChoice value) => OptionsChanged();
    partial void OnFavoritesOnlyChanged(bool value) => OptionsChanged();
    partial void OnIsLoadingChanged(bool value) => Raise(nameof(ShowSpinner), nameof(ShowProgressBar), nameof(IsEmpty));

    private void Raise(params string[] names)
    {
        foreach (var name in names)
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void ToggleDirection() => IsDescending = !IsDescending;

    [RelayCommand]
    private void ClearFilters()
    {
        _applyingOptions = true;
        SelectedPlayed = PlayedChoice.All[0];
        FavoritesOnly = false;
        _unlistedGenres = [];
        foreach (var genre in Genres)
            genre.IsSelected = false;
        _applyingOptions = false;
        OptionsChanged();
    }

    // Show the saved choice without loading (the constructor loads once).
    private void ShowOptions(ItemListOptions options)
    {
        _applyingOptions = true;
        SelectedSort = SortChoice.All.FirstOrDefault(s => s.Sort == options.Sort) ?? SortChoice.All[0];
        IsDescending = options.Descending;
        SelectedPlayed = PlayedChoice.All.FirstOrDefault(p => p.Filter == options.Played) ?? PlayedChoice.All[0];
        FavoritesOnly = options.FavoritesOnly;
        _applyingOptions = false;
    }

    private void OptionsChanged()
    {
        Raise(nameof(DirectionLabel), nameof(HasFilters), nameof(GenreLabel), nameof(EmptyText));
        if (_applyingOptions || !HasOptions)
            return;

        // Kept per library: in the settings file, so it also survives a restart.
        var options = CurrentOptions();
        if (options.IsDefault)
            _settings.Settings.LibraryViews.Remove(_parent.Id);
        else
            _settings.Settings.LibraryViews[_parent.Id] = options;
        _settings.Save();

        _ = LoadAsync();
    }

    private async Task LoadGenresAsync()
    {
        try
        {
            var names = await _client.GetGenresAsync(_parent, _lifetime.Token);
            var selected = _unlistedGenres;
            _unlistedGenres = [];
            foreach (var name in names)
            {
                var genre = new GenreChoice(name, selected.Contains(name));
                genre.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(GenreChoice.IsSelected))
                        OptionsChanged();
                };
                Genres.Add(genre);
            }

            Raise(nameof(HasGenres), nameof(GenreLabel));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Information("Could not load the genres of {Parent}: {Error}", _parent.Name, ex.Message); // the filter is optional
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        // A newer choice replaces a load still running.
        _load?.Cancel();
        _load?.Dispose();
        var load = _load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsLoading = true;
        Error = null;
        try
        {
            var options = HasOptions ? CurrentOptions() : null;
            var items = await _client.GetChildrenAsync(_parent, options, load.Token);
            load.Token.ThrowIfCancellationRequested();

            // One shape per page, from the most common item type, so rows line up.
            var shape = items.Count == 0
                ? CardShape.Poster
                : items.GroupBy(MediaCardViewModel.ShapeFor).OrderByDescending(g => g.Count()).First().Key;
            _cards = items.Select(i => MediaCardViewModel.Create(i, _client, shape)).ToList();
            Summary = _cards.Count == 1 ? "1 item" : $"{_cards.Count} items";
            OnPropertyChanged(nameof(CardSlotWidth));
            RebuildRows();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Warning("Could not load {Parent}: {Error}", _parent.Name, ex.Message);
            _cards = [];
            Rows.Clear();
            Error = $"Could not load {_parent.Name}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_load, load))
                IsLoading = false;
            Raise(nameof(IsEmpty), nameof(ShowSpinner), nameof(ShowProgressBar));
        }
    }

    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var chunk in _cards.Chunk(_columns))
            Rows.Add(new CardRow(chunk));
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        Rows.Clear();
        base.Dispose();
    }
}
