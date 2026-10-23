using LibVLCSharp.Shared;
using Lumen.Core.Playback;
using Serilog;

namespace Lumen.Player;

public sealed class LibVlcPlayerService : IPlayerService
{
    private readonly LibVlcEngine _engine;
    private readonly ILogger _log;

    // LibVLC's Play/Stop block while threads start or join, and must never run on the UI thread
    // or inside a LibVLC event callback. All transport operations are serialized through this gate.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Media? _media;
    private PlaybackState _state = PlaybackState.Idle;
    private bool _disposed;

    public LibVlcPlayerService(LibVlcEngine engine, ILogger log)
    {
        _engine = engine;
        _log = log.ForContext<LibVlcPlayerService>();

        var player = engine.MediaPlayer;
        player.Opening += (_, _) => SetState(PlaybackState.Opening);
        player.Buffering += OnBuffering;
        player.Playing += (_, _) => SetState(PlaybackState.Playing);
        player.Paused += (_, _) => SetState(PlaybackState.Paused);
        player.Stopped += (_, _) => SetState(PlaybackState.Stopped);
        player.EndReached += (_, _) => SetState(PlaybackState.Ended);
        player.EncounteredError += OnError;
        player.TimeChanged += (_, e) => PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));
        player.LengthChanged += (_, e) => DurationChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Length));
    }

    public PlaybackState State => _state;
    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, _engine.MediaPlayer.Time));
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _engine.MediaPlayer.Length));
    public int VideoOutputCount => (int)_engine.MediaPlayer.VoutCount;
    public string EngineDescription => $"LibVLC {_engine.LibVlc.Version}";

    public int Volume
    {
        get => Math.Clamp(_engine.MediaPlayer.Volume, 0, 100);
        set => _engine.MediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public bool IsMuted
    {
        get => _engine.MediaPlayer.Mute;
        set => _engine.MediaPlayer.Mute = value;
    }

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task PlayAsync(Uri source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                StopAndReleaseMedia();
                _media = new Media(_engine.LibVlc, source);
                _log.Information("Playing {Source}", source.IsFile ? "<local file>" : source.GetLeftPart(UriPartial.Path));
                if (!_engine.MediaPlayer.Play(_media))
                    ReportError("Playback could not be started.");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void TogglePause()
    {
        if (_engine.MediaPlayer.CanPause)
            _engine.MediaPlayer.Pause();
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(StopAndReleaseMedia).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Seek(TimeSpan position)
    {
        var player = _engine.MediaPlayer;
        if (!player.IsSeekable)
            return;

        var length = player.Length;
        var ms = (long)position.TotalMilliseconds;
        player.Time = length > 0 ? Math.Clamp(ms, 0, length) : Math.Max(0, ms);
    }

    private void StopAndReleaseMedia()
    {
        if (_media is null)
            return;

        _engine.MediaPlayer.Stop();
        _media.Dispose();
        _media = null;
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        // LibVLC also reports buffering while playing network streams; only surface it before playback starts.
        if (e.Cache < 100 && _state is PlaybackState.Opening or PlaybackState.Buffering)
            SetState(PlaybackState.Buffering);
    }

    private void OnError(object? sender, EventArgs e) => ReportError("The media could not be played.");

    private void ReportError(string message)
    {
        _log.Warning("Playback error: {Message}", message);
        SetState(PlaybackState.Error);
        ErrorOccurred?.Invoke(this, message);
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Let an in-flight play/stop finish first; it runs on the thread pool and also touches _media.
        var acquired = _gate.Wait(TimeSpan.FromSeconds(5));
        try
        {
            StopAndReleaseMedia();
        }
        finally
        {
            if (acquired)
                _gate.Release();
        }
    }
}
