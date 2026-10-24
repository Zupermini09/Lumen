using LibVLCSharp.Shared;
using Lumen.Core;
using Lumen.Core.Playback;
using Serilog;

namespace Lumen.Player;

public sealed class LibVlcPlayerService : IPlayerService
{
    private readonly LibVlcEngine _engine;
    private readonly ISettingsStore _settings;
    private readonly ILogger _log;

    // LibVLC's Play/Stop block while threads start or join, and must never run on the UI thread
    // or inside a LibVLC event callback. All transport operations are serialized through this gate.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Media? _media;
    private PlaybackState _state = PlaybackState.Idle;
    private bool _disposed;

    public LibVlcPlayerService(LibVlcEngine engine, ISettingsStore settings, ILogger log)
    {
        _engine = engine;
        _settings = settings;
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
        player.ESAdded += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
        player.ESDeleted += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
        player.ESSelected += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
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

    public IReadOnlyList<TrackInfo> AudioTracks => Tracks(_engine.MediaPlayer.AudioTrackDescription);
    public IReadOnlyList<TrackInfo> SubtitleTracks => Tracks(_engine.MediaPlayer.SpuDescription);
    public int AudioTrackId => _engine.MediaPlayer.AudioTrack;
    public int SubtitleTrackId => _engine.MediaPlayer.Spu;

    public void SelectAudioTrack(int id) => _engine.MediaPlayer.SetAudioTrack(id);

    public void SelectSubtitleTrack(int id) => _engine.MediaPlayer.SetSpu(id);

    public void AddExternalSubtitle(Uri uri, bool select) =>
        _engine.MediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri.AbsoluteUri, select);

    public TimeSpan SubtitleDelay
    {
        // LibVLC uses microseconds.
        get => TimeSpan.FromMicroseconds(_engine.MediaPlayer.SpuDelay);
        set => _engine.MediaPlayer.SetSpuDelay((long)value.TotalMicroseconds);
    }

    public double Rate
    {
        get => _engine.MediaPlayer.Rate;
        set => _engine.MediaPlayer.SetRate((float)Math.Clamp(value, 0.25, 4));
    }

    // LibVLC lists a "Disable" pseudo-track with id -1 first; callers use -1 explicitly instead.
    private static IReadOnlyList<TrackInfo> Tracks(LibVLCSharp.Shared.Structures.TrackDescription[]? descriptions) =>
        descriptions?.Where(d => d.Id >= 0).Select(d => new TrackInfo(d.Id, d.Name ?? $"Track {d.Id}")).ToList() ?? [];

    public event EventHandler? TracksChanged;
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task PlayAsync(Uri source, TimeSpan? startAt = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                StopAndReleaseMedia();
                // Speed and subtitle delay are per-media choices; start each item at the defaults.
                _engine.MediaPlayer.SetRate(1);
                _engine.MediaPlayer.SetSpuDelay(0);
                // Per-media options, so the settings apply to the next item without restarting the app.
                var hardware = _settings.Settings.HardwareDecoding;
                var options = new List<string> { hardware ? ":avcodec-hw=any" : ":avcodec-hw=none" };
                if (startAt is { TotalSeconds: > 0 } start)
                    options.Add($":start-time={start.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
                _media = new Media(_engine.LibVlc, source, [.. options]);
                _log.Information("Playing {Source} (hardware decoding {Mode})",
                    source.IsFile ? "<local file>" : source.GetLeftPart(UriPartial.Path), hardware ? "allowed" : "off");
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
