using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>
/// Shared playback state and controls (position, seek, pause, volume) for pages that show the video surface.
/// </summary>
public abstract partial class TransportViewModel : PageViewModel
{
    private readonly IVideoSurfaceFactory _surfaceFactory;
    private int _positionUpdatePending;
    private bool _updatingFromPlayer;

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

    protected TransportViewModel(IPlayerService player, IVideoSurfaceFactory surfaceFactory)
    {
        Player = player;
        _surfaceFactory = surfaceFactory;
        _state = player.State;
        _volume = player.Volume;
        _isMuted = player.IsMuted;

        Player.StateChanged += OnPlayerStateChanged;
        Player.PositionChanged += OnPositionChanged;
        Player.DurationChanged += OnDurationChanged;
        Player.ErrorOccurred += OnError;
    }

    protected IPlayerService Player { get; }

    public string Engine => Player.EngineDescription;

    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>Called by the view; the surface is a UI control owned by the player backend.</summary>
    public object CreateVideoSurface(object? overlay) => _surfaceFactory.CreateSurface(overlay);

    [RelayCommand]
    private void TogglePause() => Player.TogglePause();

    [RelayCommand]
    private Task StopAsync() => Player.StopAsync();

    public void SeekBy(TimeSpan offset) => Player.Seek(Player.Position + offset);

    partial void OnPositionSecondsChanged(double value)
    {
        // Only seek when the user moved the slider, not when the player reported progress.
        if (!_updatingFromPlayer)
            Player.Seek(TimeSpan.FromSeconds(value));
    }

    partial void OnVolumeChanged(int value) => Player.Volume = value;

    partial void OnIsMutedChanged(bool value) => Player.IsMuted = value;

    partial void OnStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPlaybackStateChanged(value);
    }

    /// <summary>Runs on the UI thread after every state change.</summary>
    protected virtual void OnPlaybackStateChanged(PlaybackState state) { }

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
            ApplyPosition(Player.Position);
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
        Player.StateChanged -= OnPlayerStateChanged;
        Player.PositionChanged -= OnPositionChanged;
        Player.DurationChanged -= OnDurationChanged;
        Player.ErrorOccurred -= OnError;
        base.Dispose();
    }
}
