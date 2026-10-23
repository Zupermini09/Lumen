using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

public sealed partial class ConnectViewModel : PageViewModel
{
    private readonly IJellyfinClient _client;
    private readonly ISessionService _sessions;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _address = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public ConnectViewModel(IJellyfinClient client, ISessionService sessions, INavigator navigator, IPageFactory pages, string? error = null)
    {
        _client = client;
        _sessions = sessions;
        _navigator = navigator;
        _pages = pages;
        _error = error;
        RecentServers = sessions.RecentServers.ToList();
        _address = RecentServers.FirstOrDefault()?.Address ?? "";
    }

    public override string Title => "Connect to a server";

    public IReadOnlyList<SavedServer> RecentServers { get; }
    public bool HasRecentServers => RecentServers.Count > 0;

    private bool CanConnect() => !IsBusy && !string.IsNullOrWhiteSpace(Address);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => ConnectToAsync(Address);

    [RelayCommand]
    private Task ConnectRecentAsync(SavedServer server)
    {
        Address = server.Address;
        return ConnectToAsync(server.Address);
    }

    [RelayCommand]
    private void PlayLocalFile() => _navigator.Navigate(_pages.PlaybackTest());

    private async Task ConnectToAsync(string address)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Error = null;
        try
        {
            var server = await _client.FindServerAsync(address, _cts.Token);
            _sessions.RememberServer(server);
            _navigator.Navigate(_pages.Login(server));
        }
        catch (ServerConnectionException ex)
        {
            Error = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override void Dispose()
    {
        _cts?.Cancel();
        base.Dispose();
    }
}
