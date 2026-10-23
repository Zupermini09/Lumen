using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>The user's favourites, grouped by kind (movies, shows, episodes...).</summary>
public sealed partial class FavoritesViewModel : PageViewModel, ICardHost
{
    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private IReadOnlyList<MediaItem> _shown = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _summary = "";

    public FavoritesViewModel(IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _client = client;
        _navigator = navigator;
        _pages = pages;
        _log = log.ForContext<FavoritesViewModel>();
        _ = LoadAsync(silent: false);
    }

    public override string Title => "Favourites";

    public GroupedCardGrid Results { get; } = new();

    public bool IsEmpty => !IsLoading && Error is null && Results.IsEmpty;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(IsEmpty));

    public void Open(MediaCardViewModel card) => ItemNavigation.Open(card.Item, _navigator, _pages);

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(silent: false);

    // Back from a details page: an item may have been added or removed there.
    public override void OnNavigatedTo() => _ = LoadAsync(silent: true);

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
            var items = await _client.GetFavoritesAsync(ItemGroups.AllTypes, token);
            token.ThrowIfCancellationRequested();
            Error = null;

            // Nothing changed: keep the cards (and their images) as they are.
            if (!items.SequenceEqual(_shown))
            {
                _shown = items;
                Results.SetGroups(GroupedCardGrid.Group(items, _client));
                Summary = items.Count == 1 ? "1 item" : $"{items.Count} items";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Warning("Could not load favourites: {Error}", ex.Message);
            if (!silent)
                Error = $"Could not load your favourites: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_load, load))
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}
