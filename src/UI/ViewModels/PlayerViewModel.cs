using Avalonia.Threading;
using Lumen.Core;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// Plays a Jellyfin item (direct play): asks the server for the file version, tracks and the user's default track
/// choices, resumes from the saved position, and reports progress so "Continue watching" and watched state work like
/// in the other clients.
/// </summary>
public sealed partial class PlayerViewModel : TransportViewModel
{
    private const int NextEpisodeCountdownSeconds = 10;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TrackDefaultsTimeout = TimeSpan.FromSeconds(5);

    private readonly MediaItem _item;
    private readonly IJellyfinClient _client;
    private readonly ILogger _log;
    private readonly DispatcherTimer _progressTimer;
    private readonly ISettingsStore _settings;
    private readonly INavigator _navigator;
    private readonly IPageFactory _pages;
    private readonly DispatcherTimer _countdownTimer;
    private MediaItem? _nextEpisode;
    private int _countdown;
    private PlaybackDetails _details;
    private SegmentTracker _segments = new([], _ => SegmentAction.Off);
    private MediaSegment? _skipSegment;
    private string _playSessionId = Guid.NewGuid().ToString("N");
    private bool _reportedStart;
    private bool _stopped;
    private bool _tracksPrepared;
    private int? _pendingSubtitlePosition;
    private DateTime _pendingSubtitleDeadline;
    private TimeSpan _lastPosition;

    public PlayerViewModel(MediaItem item, IJellyfinClient client, IPlayerService player, IVideoSurfaceFactory surfaceFactory,
        ISettingsStore settings, INavigator navigator, IPageFactory pages, ILogger log)
        : base(player, surfaceFactory)
    {
        _item = item;
        _client = client;
        _settings = settings;
        _navigator = navigator;
        _pages = pages;
        _countdownTimer = Timers.Create(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, CountdownTick);
        _details = PlaybackDetails.Fallback(item);
        _log = log.ForContext<PlayerViewModel>();
        (ItemTitle, ItemSubtitle) = MediaCardViewModel.Describe(item);
        _isResuming = StartPosition > TimeSpan.Zero;
        _progressTimer = Timers.Create(ProgressInterval, DispatcherPriority.Background, ReportProgress);
        Player.TracksChanged += OnTracksChanged;
    }

    public override string Title => ItemTitle;
    public override bool KeepInHistory => false;

    public string ItemTitle { get; }
    public string? ItemSubtitle { get; }
    public MediaItem Item => _item;
    public IReadOnlyList<ChapterInfo> Chapters => _details.Chapters;

    /// <summary>Resume where the user left off, unless they (nearly) finished it.</summary>
    public TimeSpan StartPosition => _item.ResumePosition;

    /// <summary>Shows "Start over" while playback resumed from a saved position.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isResuming;

    /// <summary>Starts playback; the view calls this once the video surface is attached.</summary>
    public async Task StartAsync()
    {
        ErrorMessage = null;
        try
        {
            _details = await _client.GetPlaybackDetailsAsync(_item);
            if (_details.PlaySessionId is { } sessionId)
                _playSessionId = sessionId;
            OnPropertyChanged(nameof(Chapters));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // Without PlaybackInfo the file still plays; only server track names and defaults are missing.
            _log.Information("Playback info unavailable, playing with player defaults: {Error}", ex.Message);
        }

        _ = LoadSegmentsAsync();
        _ = LoadNextEpisodeAsync();

        try
        {
            await Player.PlayAsync(_client.GetStreamUri(_item, _details.MediaSourceId), StartPosition);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not start playback");
            ErrorMessage = "Playback could not be started.";
        }
    }

    public async Task StartOverAsync()
    {
        IsResuming = false;
        await Player.StopAsync();
        _reportedStart = false;
        _tracksPrepared = false;
        await Player.PlayAsync(_client.GetStreamUri(_item, _details.MediaSourceId));
    }

    // ---- Next episode -------------------------------------------------------------------------------------------

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasNextEpisode))]
    private string? _nextEpisodeLabel;

    /// <summary>"Next episode in 7 s" while the countdown runs, otherwise null.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsCountingDown))]
    private string? _countdownText;

    public bool HasNextEpisode => NextEpisodeLabel is not null;
    public bool IsCountingDown => CountdownText is not null;

    private async Task LoadNextEpisodeAsync()
    {
        try
        {
            _nextEpisode = await _client.GetNextEpisodeAsync(_item);
            if (_nextEpisode is { } next)
            {
                var (_, subtitle) = MediaCardViewModel.Describe(next);
                NextEpisodeLabel = subtitle ?? next.Name;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _log.Information("Next episode unavailable: {Error}", ex.Message);
        }
    }

    private void StartCountdown()
    {
        if (_nextEpisode is null || !_settings.Settings.AutoPlayNextEpisode)
            return;
        _countdown = NextEpisodeCountdownSeconds;
        CountdownText = $"Next episode in {_countdown} s";
        _countdownTimer.Start();
    }

    private void CountdownTick()
    {
        _countdown--;
        if (_countdown <= 0)
            PlayNextEpisode();
        else
            CountdownText = $"Next episode in {_countdown} s";
    }

    public void CancelCountdown()
    {
        _countdownTimer.Stop();
        CountdownText = null;
    }

    public void PlayNextEpisode()
    {
        CancelCountdown();
        if (_nextEpisode is { } next)
            _navigator.Navigate(_pages.Player(next)); // replaces this player page (players are not kept in history)
    }

    // ---- Skip segments ----------------------------------------------------------------------------------------

    private async Task LoadSegmentsAsync()
    {
        try
        {
            var segments = await _client.GetMediaSegmentsAsync(_item);
            _segments = new SegmentTracker(segments, type => _settings.Settings.ActionFor(type));
            if (segments.Count > 0)
                _log.Information("{Count} media segments for this item", segments.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _log.Information("Media segments unavailable: {Error}", ex.Message); // optional feature
        }
    }

    protected override void OnPositionUpdated(TimeSpan position)
    {
        if (!_segments.HasSegments || State is not (PlaybackState.Playing or PlaybackState.Paused))
            return;

        var decision = _segments.Update(position);
        if (decision.SeekTo is { } target)
        {
            SeekTo(target);
            return;
        }

        _skipSegment = decision.ButtonFor;
        SkipLabel = _skipSegment?.SkipLabel;
    }

    public override void Skip()
    {
        if (_skipSegment is not { } segment)
            return;
        SeekTo(_segments.Skip(segment));
        _skipSegment = null;
        SkipLabel = null;
    }

    // ---- Tracks -----------------------------------------------------------------------------------------------

    /// <summary>Server subtitle streams in the order the player lists them: embedded ones, then external files.</summary>
    private IReadOnlyList<MediaStreamInfo> ServerSubtitles =>
        _details.SubtitleStreams.Where(s => !s.IsExternal).OrderBy(s => s.Index)
            .Concat(_details.SubtitleStreams.Where(s => s.IsExternal).OrderBy(s => s.Index))
            .ToList();

    private IReadOnlyList<MediaStreamInfo> ServerAudio => _details.AudioStreams.OrderBy(s => s.Index).ToList();

    public override IReadOnlyList<MenuOption> AudioOptions()
    {
        var tracks = Player.AudioTracks;
        var server = ServerAudio;
        // Server names are nicer ("English - Dolby TrueHD + Atmos 7.1"); use them when the lists line up.
        return tracks.Select((t, i) => new MenuOption(
                tracks.Count == server.Count ? server[i].DisplayTitle : t.Name,
                t.Id == Player.AudioTrackId,
                () => Player.SelectAudioTrack(t.Id)))
            .ToList();
    }

    public override IReadOnlyList<MenuOption> SubtitleOptions()
    {
        var tracks = Player.SubtitleTracks;
        var server = ServerSubtitles;
        return tracks.Select((t, i) => new MenuOption(
                tracks.Count == server.Count ? SubtitleLabel(server[i]) : t.Name,
                t.Id == Player.SubtitleTrackId,
                () => Player.SelectSubtitleTrack(t.Id)))
            .Prepend(new MenuOption("Off", Player.SubtitleTrackId < 0, () => Player.SelectSubtitleTrack(-1)))
            .ToList();
    }

    public override IReadOnlyList<MenuOption> ChapterOptions()
    {
        var position = Player.Position;
        var current = _details.Chapters.LastOrDefault(c => c.Start <= position);
        return _details.Chapters
            .Select(c => new MenuOption($"{Core.Formatting.TimeFormat.Playback(c.Start, TimeSpan.FromSeconds(DurationSeconds))}  {c.Name}",
                c == current, () => SeekTo(c.Start)))
            .ToList();
    }

    private static string SubtitleLabel(MediaStreamInfo stream) =>
        stream.IsExternal && !stream.DisplayTitle.Contains("External", StringComparison.OrdinalIgnoreCase)
            ? $"{stream.DisplayTitle} (external)"
            : stream.DisplayTitle;

    /// <summary>
    /// Once playback runs: add the server's external subtitle files and apply the user's default audio and subtitle
    /// tracks (chosen by the server from the user's language preferences, like in the web client).
    /// </summary>
    private void PrepareTracks()
    {
        if (_tracksPrepared)
            return;
        _tracksPrepared = true;

        foreach (var external in _details.SubtitleStreams.Where(s => s.IsExternal).OrderBy(s => s.Index))
            Player.AddExternalSubtitle(_client.GetSubtitleUri(_item, _details.MediaSourceId, external), select: false);

        var audio = ServerAudio;
        if (_details.DefaultAudioStreamIndex is { } audioIndex && Player.AudioTracks is { } tracks && tracks.Count == audio.Count)
        {
            var position = audio.ToList().FindIndex(s => s.Index == audioIndex);
            if (position >= 0)
                Player.SelectAudioTrack(tracks[position].Id);
        }

        var subtitles = ServerSubtitles.ToList();
        var subtitlePosition = _details.DefaultSubtitleStreamIndex is { } subIndex ? subtitles.FindIndex(s => s.Index == subIndex) : -1;
        if (subtitlePosition < 0)
        {
            if (_details.Streams.Count > 0)
                Player.SelectSubtitleTrack(-1); // the user's preference is "no subtitles" for this item
            return;
        }

        // External files are added asynchronously; select once the player lists all subtitle tracks.
        _pendingSubtitlePosition = subtitlePosition;
        _pendingSubtitleDeadline = DateTime.UtcNow + TrackDefaultsTimeout;
        ApplyPendingSubtitle();
    }

    private void ApplyPendingSubtitle()
    {
        if (_pendingSubtitlePosition is not { } position)
            return;
        var tracks = Player.SubtitleTracks;
        if (tracks.Count == ServerSubtitles.Count)
        {
            Player.SelectSubtitleTrack(tracks[position].Id);
            _pendingSubtitlePosition = null;
        }
        else if (DateTime.UtcNow > _pendingSubtitleDeadline)
        {
            _pendingSubtitlePosition = null; // the lists never lined up; keep the player's own choice
        }
    }

    private void OnTracksChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(ApplyPendingSubtitle);

    // ---- Progress reporting ------------------------------------------------------------------------------------

    protected override void OnPlaybackStateChanged(PlaybackState state)
    {
        if (state == PlaybackState.Playing && !_reportedStart)
        {
            _reportedStart = true;
            PrepareTracks();
            _progressTimer.Start();
            Report(_client.ReportPlaybackStartAsync, "start");
        }
        else if (state is PlaybackState.Paused or PlaybackState.Playing)
        {
            ReportProgress();
        }
        else if (state == PlaybackState.Ended)
        {
            // Reporting the full runtime marks the item as watched.
            _lastPosition = TimeSpan.FromSeconds(DurationSeconds);
            ReportStopped();
            StartCountdown();
        }
    }

    private void ReportProgress()
    {
        if (!_reportedStart || _stopped)
            return;
        Report(_client.ReportPlaybackProgressAsync, "progress");
    }

    private void ReportStopped()
    {
        if (!_reportedStart || _stopped)
            return;
        _stopped = true;
        _progressTimer.Stop();
        Report(_client.ReportPlaybackStoppedAsync, "stop");
    }

    private void Report(Func<PlaybackReport, CancellationToken, Task> send, string kind)
    {
        if (State != PlaybackState.Ended)
        {
            // Right after starting, the player may not have reported time yet: use the resume point.
            var position = Player.Position > TimeSpan.Zero ? Player.Position : StartPosition;
            if (position > TimeSpan.Zero)
                _lastPosition = position;
        }

        var report = new PlaybackReport(_item.Id, _details.MediaSourceId, _playSessionId, _lastPosition, State == PlaybackState.Paused);
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await send(report, timeout.Token);
            }
            catch (Exception ex)
            {
                // Reporting is best effort; playback must never fail because of it.
                _log.Information("Playback {Kind} report failed: {Error}", kind, ex.Message);
            }
        });
    }

    public override void Dispose()
    {
        Player.TracksChanged -= OnTracksChanged;
        _countdownTimer.Stop();
        ReportStopped();
        _progressTimer.Stop();
        _ = Player.StopAsync();
        base.Dispose();
    }
}
