using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

public sealed partial class HomeViewModel : PageViewModel, ICardHost
{
    private readonly IJellyfinClient _client;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();

    [ObservableProperty]
    private IReadOnlyList<MediaCardViewModel> _continueWatching = [];

    [ObservableProperty]
    private IReadOnlyList<MediaCardViewModel> _nextUp = [];

    [ObservableProperty]
    private IReadOnlyList<MediaCardViewModel> _libraries = [];

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
        _ = LoadAsync();
    }

    public override string Title => "Home";
    public string Greeting { get; }
    public bool HasContinueWatching => ContinueWatching.Count > 0;
    public bool HasNextUp => NextUp.Count > 0;

    partial void OnContinueWatchingChanged(IReadOnlyList<MediaCardViewModel> value) => OnPropertyChanged(nameof(HasContinueWatching));
    partial void OnNextUpChanged(IReadOnlyList<MediaCardViewModel> value) => OnPropertyChanged(nameof(HasNextUp));

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    public void Open(MediaCardViewModel card) => ItemNavigation.Open(card.Item, _navigator, _pages);

    private async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        var token = _lifetime.Token;
        try
        {
            var libraries = _client.GetLibrariesAsync(token);
            var resume = _client.GetResumeItemsAsync(12, token);
            var nextUp = _client.GetNextUpAsync(12, token);

            Libraries = (await libraries).Select(i => MediaCardViewModel.Create(i, _client, CardShape.Wide)).ToList();
            ContinueWatching = await Optional(resume, CardShape.Wide);
            NextUp = await Optional(nextUp, CardShape.Wide);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Warning("Home failed to load: {Error}", ex.Message);
            Error = $"Could not load your libraries: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    // A failing secondary row (e.g. Next Up on a server without TV libraries) must not hide the libraries.
    private async Task<IReadOnlyList<MediaCardViewModel>> Optional(Task<IReadOnlyList<MediaItem>> load, CardShape shape)
    {
        try
        {
            return (await load).Select(i => MediaCardViewModel.Create(i, _client, shape)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _log.Information("Home row failed to load: {Error}", ex.Message);
            return [];
        }
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}
