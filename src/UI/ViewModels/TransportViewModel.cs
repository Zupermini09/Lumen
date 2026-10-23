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

    /// <summary>Speeds offered in the speed menu (like the web client).</summary>
    public static readonly double[] Speeds = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];

    [ObservableProperty]
    private string _speedText = "1×";

    /// <summary>Text of the skip button ("Skip Intro"...), or null when there is nothing to skip right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkip))]
    private string? _skipLabel;

    public bool HasSkip => SkipLabel is not null;

    /// <summary>Skips the current segment (button or S key).</summary>
    public virtual void Skip() { }

    /// <summary>Called on the UI thread whenever the displayed position changes.</summary>
    protected virtual void OnPositionUpdated(TimeSpan position) { }

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

    public void SeekTo(TimeSpan position) => Player.Seek(position);

    // ---- Tracks, subtitle delay, speed (menus are built when opened, so they always show the current state) ----

    public virtual IReadOnlyList<MenuOption> AudioOptions() =>
        Player.AudioTracks.Select(t => new MenuOption(t.Name, t.Id == Player.AudioTrackId, () => Player.SelectAudioTrack(t.Id))).ToList();

    public virtual IReadOnlyList<MenuOption> SubtitleOptions() =>
        Player.SubtitleTracks
            .Select(t => new MenuOption(t.Name, t.Id == Player.SubtitleTrackId, () => Player.SelectSubtitleTrack(t.Id)))
            .Prepend(new MenuOption("Off", Player.SubtitleTrackId < 0, () => Player.SelectSubtitleTrack(-1)))
            .ToList();

    /// <summary>Chapter list (empty when the item has none).</summary>
    public virtual IReadOnlyList<MenuOption> ChapterOptions() => [];

    public IReadOnlyList<MenuOption> SpeedOptions() =>
        Speeds.Select(s => new MenuOption(FormatSpeed(s), Math.Abs(Player.Rate - s) < 0.01, () => SetSpeed(s))).ToList();

    public string SubtitleDelayText => $"Subtitle delay: {Player.SubtitleDelay.TotalSeconds:+0.0;-0.0;0.0} s";

    public void AdjustSubtitleDelay(TimeSpan change)
    {
        Player.SubtitleDelay += change;
        OnPropertyChanged(nameof(SubtitleDelayText));
    }

    public void ResetSubtitleDelay()
    {
        Player.SubtitleDelay = TimeSpan.Zero;
        OnPropertyChanged(nameof(SubtitleDelayText));
    }

    public void SetSpeed(double speed)
    {
        Player.Rate = speed;
        SpeedText = FormatSpeed(speed);
    }

    public void ChangeSpeed(int steps)
    {
        var index = Array.FindIndex(Speeds, s => Math.Abs(s - Player.Rate) < 0.01);
        SetSpeed(Speeds[Math.Clamp((index < 0 ? Array.IndexOf(Speeds, 1d) : index) + steps, 0, Speeds.Length - 1)]);
    }

    private static string FormatSpeed(double speed) => $"{speed:0.##}×";

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
            if (state == PlaybackState.Opening)
                SpeedText = FormatSpeed(1); // the player resets speed for each item
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
            OnPositionUpdated(position);
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

/// <summary>An entry in a player menu (audio track, subtitle track, speed...).</summary>
public sealed record MenuOption(string Label, bool IsSelected, Action Select);
