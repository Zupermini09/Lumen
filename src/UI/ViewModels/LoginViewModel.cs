using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

public sealed partial class LoginViewModel : PageViewModel
{
    private static readonly TimeSpan QuickConnectPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QuickConnectTimeout = TimeSpan.FromMinutes(10);

    private readonly ServerInfo _server;
    private readonly IJellyfinClient _client;
    private readonly ISessionService _sessions;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _quickConnect;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private IReadOnlyList<UserCardViewModel> _users = [];

    [ObservableProperty]
    private bool _isQuickConnectAvailable;

    [ObservableProperty]
    private string? _quickConnectCode;

    public LoginViewModel(ServerInfo server, IJellyfinClient client, ISessionService sessions, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _server = server;
        _client = client;
        _sessions = sessions;
        _navigator = navigator;
        _pages = pages;
        _log = log.ForContext<LoginViewModel>();
        _userName = sessions.RecentServers.FirstOrDefault(s => s.Id == server.Id)?.LastUserName ?? "";
        _ = LoadAsync();
    }

    public override string Title => $"Sign in to {_server.Name}";
    public string ServerName => _server.Name;
    public string ServerDescription => $"{_server.Address} · Jellyfin {_server.Version}";
    public bool HasUsers => Users.Count > 0;
    public bool IsQuickConnectActive => QuickConnectCode is not null;

    partial void OnUsersChanged(IReadOnlyList<UserCardViewModel> value) => OnPropertyChanged(nameof(HasUsers));
    partial void OnQuickConnectCodeChanged(string? value) => OnPropertyChanged(nameof(IsQuickConnectActive));

    private async Task LoadAsync()
    {
        try
        {
            var users = await _client.GetPublicUsersAsync(_server, _lifetime.Token);
            Users = users.Select(u => new UserCardViewModel(u, _client.GetUserImageUri(_server, u, 200))).ToList();
            IsQuickConnectAvailable = await _client.IsQuickConnectEnabledAsync(_server, _lifetime.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _log.Information("Could not load login options: {Error}", ex.Message);
        }
    }

    [RelayCommand]
    private void SelectUser(UserCardViewModel user)
    {
        UserName = user.Name;
        Password = "";
        Error = null;
    }

    private bool CanSignIn() => !IsBusy && !string.IsNullOrWhiteSpace(UserName);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var session = await _client.AuthenticateAsync(_server, UserName.Trim(), Password, _lifetime.Token);
            Complete(session);
        }
        catch (AuthenticationException ex)
        {
            Error = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            _log.Warning(ex, "Sign-in failed");
            Error = $"Could not sign in: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
            Password = "";
        }
    }

    [RelayCommand]
    private async Task StartQuickConnectAsync()
    {
        CancelQuickConnect();
        _quickConnect = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _quickConnect.CancelAfter(QuickConnectTimeout);
        var token = _quickConnect.Token;
        Error = null;

        try
        {
            var request = await _client.StartQuickConnectAsync(_server, token);
            QuickConnectCode = request.Code;

            while (!await _client.IsQuickConnectAuthorizedAsync(_server, request, token))
                await Task.Delay(QuickConnectPollInterval, token);

            Complete(await _client.AuthenticateWithQuickConnectAsync(_server, request, token));
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested && _quickConnect?.Token == token)
                Error = "Quick Connect timed out. Start it again to get a new code.";
        }
        catch (Exception ex) when (ex is HttpRequestException or ServerConnectionException or AuthenticationException)
        {
            Error = $"Quick Connect failed: {ex.Message}";
        }
        finally
        {
            if (_quickConnect?.Token == token)
                QuickConnectCode = null;
        }
    }

    [RelayCommand]
    private void CancelQuickConnect()
    {
        _quickConnect?.Cancel();
        _quickConnect = null;
        QuickConnectCode = null;
    }

    [RelayCommand]
    private void ChangeServer() => _navigator.NavigateRoot(_pages.Connect());

    private void Complete(SessionInfo session)
    {
        _sessions.SignIn(session);
        _navigator.NavigateRoot(_pages.Home());
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}

public sealed record UserCardViewModel(UserInfo User, Uri? ImageUri)
{
    public string Name => User.Name;
    public string Initial => User.Name.Length > 0 ? User.Name[..1].ToUpperInvariant() : "?";
}
