using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>
/// M0 test page: plays a local file or URL so LibVLC, the video surface and overlays can be verified on every OS.
/// </summary>
public sealed partial class PlaybackTestViewModel : PageViewModel
{
    private readonly IPlayerService _player;
    private readonly IVideoSurfaceFactory _surfaceFactory;
    private int _positionUpdatePending;
    private bool _updatingFromPlayer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private string _source = string.Empty;

    [ObservableProperty]
    private PlaybackState _state;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _volume;

    [ObservableProperty]
    private bool _isMuted;

    public PlaybackTestViewModel(IPlayerService player, IVideoSurfaceFactory surfaceFactory)
    {
        _player = player;
        _surfaceFactory = surfaceFactory;
        _state = player.State;
        _volume = player.Volume;
        _isMuted = player.IsMuted;
        _durationSeconds = player.Duration.TotalSeconds;

        _player.StateChanged += OnPlayerStateChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.DurationChanged += OnDurationChanged;
        _player.ErrorOccurred += OnError;
    }

    public override string Title => "Playback test";

    public string Engine => _player.EngineDescription;

    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>Called by the view; the surface is a UI control owned by the player backend.</summary>
    public object CreateVideoSurface(object overlay) => _surfaceFactory.CreateSurface(overlay);

    private bool CanPlay() => MediaLocation.TryParse(Source, out _);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (!MediaLocation.TryParse(Source, out var uri) || uri is null)
            return;

        ErrorMessage = null;
        await _player.PlayAsync(uri);
    }

    [RelayCommand]
    private void TogglePause() => _player.TogglePause();

    [RelayCommand]
    private Task StopAsync() => _player.StopAsync();

    partial void OnPositionSecondsChanged(double value)
    {
        // Only seek when the user moved the slider, not when the player reported progress.
        if (!_updatingFromPlayer)
            _player.Seek(TimeSpan.FromSeconds(value));
    }

    partial void OnVolumeChanged(int value) => _player.Volume = value;

    partial void OnIsMutedChanged(bool value) => _player.IsMuted = value;

    partial void OnStateChanged(PlaybackState value) => OnPropertyChanged(nameof(IsPlaying));

    private void OnPlayerStateChanged(object? sender, PlaybackState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            State = state;
            if (state is PlaybackState.Stopped or PlaybackState.Idle)
                ApplyPosition(TimeSpan.Zero);
        });

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        // LibVLC reports time many times per second; coalesce so at most one UI update is queued at a time.
        if (Interlocked.Exchange(ref _positionUpdatePending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _positionUpdatePending, 0);
            ApplyPosition(_player.Position);
        }, DispatcherPriority.Background);
    }

    private void OnDurationChanged(object? sender, TimeSpan duration) =>
        Dispatcher.UIThread.Post(() =>
        {
            DurationSeconds = duration.TotalSeconds;
            DurationText = TimeFormat.Playback(duration);
        });

    private void OnError(object? sender, string message) => Dispatcher.UIThread.Post(() => ErrorMessage = message);

    private void ApplyPosition(TimeSpan position)
    {
        _updatingFromPlayer = true;
        try
        {
            PositionSeconds = position.TotalSeconds;
            PositionText = TimeFormat.Playback(position, TimeSpan.FromSeconds(DurationSeconds));
        }
        finally
        {
            _updatingFromPlayer = false;
        }
    }

    public override void Dispose()
    {
        _player.StateChanged -= OnPlayerStateChanged;
        _player.PositionChanged -= OnPositionChanged;
        _player.DurationChanged -= OnDurationChanged;
        _player.ErrorOccurred -= OnError;
        _ = _player.StopAsync();
        base.Dispose();
    }
}
