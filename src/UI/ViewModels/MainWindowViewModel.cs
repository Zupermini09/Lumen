using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, INavigator, IDisposable
{
    private readonly Lazy<IPageFactory> _pages;
    private readonly ISessionService _sessions;
    private readonly IJellyfinClient _client;
    private readonly ILogger _log;
    private readonly Stack<PageViewModel> _history = new();
    private readonly DispatcherTimer _memoryTimer;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _isSignedIn;

    // The factory needs this navigator, so it is resolved lazily to avoid a construction cycle.
    public MainWindowViewModel(Lazy<IPageFactory> pages, ISessionService sessions, IJellyfinClient client, AppInfo appInfo, ILogger log)
    {
        _pages = pages;
        _sessions = sessions;
        _client = client;
        _log = log.ForContext<MainWindowViewModel>();
        Version = appInfo.Version;

        // A live RAM readout makes it easy to check the memory targets while testing.
        _memoryTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateMemory());
        _memoryTimer.Start();
        UpdateMemory();
    }

    public string Title => CurrentPage is { } page ? $"{page.Title} - Lumen" : "Lumen";
    public string Version { get; }
    public string SignedInAs => _sessions.Current is { } s ? $"{s.User.Name} · {s.Server.Name}" : "";

    private IPageFactory Pages => _pages.Value;

    partial void OnCurrentPageChanged(PageViewModel? value) => OnPropertyChanged(nameof(Title));

    /// <summary>Decides the first page: the saved session's home, its login page, or the connect page.</summary>
    public async Task StartAsync()
    {
        NavigateRoot(new LoadingViewModel());
        try
        {
            if (await _sessions.TryRestoreAsync() is not null)
            {
                OnSignedInChanged();
                NavigateRoot(Pages.Home());
                return;
            }

            if (_sessions.RecentServers.FirstOrDefault() is { } recent)
            {
                var server = await _client.FindServerAsync(recent.Address);
                NavigateRoot(Pages.Login(server));
                return;
            }

            NavigateRoot(Pages.Connect());
        }
        catch (ServerConnectionException ex)
        {
            NavigateRoot(Pages.Connect(ex.Message));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Startup navigation failed");
            NavigateRoot(Pages.Connect("Something went wrong while connecting. Check the address and try again."));
        }
    }

    public void Navigate(PageViewModel page)
    {
        if (CurrentPage is { } current)
        {
            current.OnNavigatedFrom();
            if (current.KeepInHistory)
                _history.Push(current);
            else
                current.Dispose();
        }

        Show(page);
    }

    public void NavigateRoot(PageViewModel page)
    {
        var previous = CurrentPage;
        Show(page);
        previous?.Dispose();
        while (_history.TryPop(out var old))
            old.Dispose();
        CanGoBack = false;
        OnSignedInChanged();
    }

    public void GoBack()
    {
        if (!_history.TryPop(out var previous))
            return;
        var current = CurrentPage;
        Show(previous);
        current?.Dispose();
        previous.OnNavigatedTo();
    }

    /// <summary>
    /// Ctrl+F / Ctrl+K: opens the search page (Back returns to where you were), or puts the cursor into its box when it
    /// is already open. Returns false where search does not apply (not signed in, or while a video plays).
    /// </summary>
    public bool OpenSearch()
    {
        if (!IsSignedIn || CurrentPage is PlayerViewModel)
            return false;
        if (CurrentPage is SearchViewModel search)
            search.RequestFocus();
        else
            Navigate(Pages.Search());
        return true;
    }

    /// <summary>Handles the navigation pane: Home, Search, Favourites, Play a file, About, Sign out.</summary>
    public async Task OpenMenuAsync(string key)
    {
        switch (key)
        {
            case "Home":
                NavigateRoot(_sessions.Current is null ? Pages.Connect() : Pages.Home());
                break;
            case "Search":
                NavigateRoot(Pages.Search());
                break;
            case "Favorites":
                NavigateRoot(Pages.Favorites());
                break;
            case "PlayFile":
                Navigate(Pages.PlaybackTest());
                break;
            case "About":
                Navigate(Pages.About());
                break;
            case "SignOut":
                await _sessions.SignOutAsync();
                NavigateRoot(Pages.Connect());
                break;
        }
    }

    private void Show(PageViewModel page)
    {
        CurrentPage = page;
        CanGoBack = _history.Count > 0;
    }

    private void OnSignedInChanged()
    {
        IsSignedIn = _sessions.Current is not null;
        OnPropertyChanged(nameof(SignedInAs));
    }

    private void UpdateMemory() =>
        MemoryText = $"RAM {TimeFormat.Megabytes(Environment.WorkingSet)} · managed {TimeFormat.Megabytes(GC.GetTotalMemory(false))}";

    public void Dispose()
    {
        _memoryTimer.Stop();
        CurrentPage?.Dispose();
        CurrentPage = null;
        while (_history.TryPop(out var page))
            page.Dispose();
    }
}

/// <summary>Shown while the startup session check runs.</summary>
public sealed class LoadingViewModel : PageViewModel
{
    public override string Title => "Connecting";
}
