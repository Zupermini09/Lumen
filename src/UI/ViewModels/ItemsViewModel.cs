using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>The contents of a library, show, season, album or folder, as a virtualized grid.</summary>
public sealed partial class ItemsViewModel : PageViewModel, ICardHost
{
    public const double CardSpacing = 16;

    private readonly MediaItem _parent;
    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<MediaCardViewModel> _cards = [];
    private int _columns = 1;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _summary = "";

    public ItemsViewModel(MediaItem parent, IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _parent = parent;
        _client = client;
        _navigator = navigator;
        _pages = pages;
        _log = log.ForContext<ItemsViewModel>();
        _ = LoadAsync();
    }

    public override string Title => _parent.Name;

    /// <summary>Rows of cards; the list virtualizes rows, so only visible cards exist (and load images).</summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    public bool IsEmpty => !IsLoading && Error is null && _cards.Count == 0;

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

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var items = await _client.GetChildrenAsync(_parent, _lifetime.Token);
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
            Error = $"Could not load {_parent.Name}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
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
