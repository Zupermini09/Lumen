using Avalonia.Threading;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// Plays a Jellyfin item (direct play), resumes from the saved position and reports progress to the server so
/// "Continue watching" and watched state work like in the other clients.
/// </summary>
public sealed class PlayerViewModel : TransportViewModel
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);

    private readonly MediaItem _item;
    private readonly IJellyfinClient _client;
    private readonly ILogger _log;
    private readonly string _playSessionId = Guid.NewGuid().ToString("N");
    private readonly DispatcherTimer _progressTimer;
    private bool _reportedStart;
    private bool _stopped;
    private TimeSpan _lastPosition;

    public PlayerViewModel(MediaItem item, IJellyfinClient client, IPlayerService player, IVideoSurfaceFactory surfaceFactory, ILogger log)
        : base(player, surfaceFactory)
    {
        _item = item;
        _client = client;
        _log = log.ForContext<PlayerViewModel>();
        (ItemTitle, ItemSubtitle) = MediaCardViewModel.Describe(item);
        _progressTimer = new DispatcherTimer(ProgressInterval, DispatcherPriority.Background, (_, _) => ReportProgress());
    }

    public override string Title => ItemTitle;
    public override bool KeepInHistory => false;

    public string ItemTitle { get; }
    public string? ItemSubtitle { get; }
    public MediaItem Item => _item;

    /// <summary>Resume where the user left off, unless they (nearly) finished it.</summary>
    public TimeSpan StartPosition =>
        _item.PlaybackPosition > TimeSpan.FromSeconds(5) && !_item.Played ? _item.PlaybackPosition : TimeSpan.Zero;

    public bool IsResuming => StartPosition > TimeSpan.Zero;

    /// <summary>Starts playback; the view calls this once the video surface is attached.</summary>
    public async Task StartAsync()
    {
        ErrorMessage = null;
        try
        {
            await Player.PlayAsync(_client.GetStreamUri(_item), StartPosition);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not start playback");
            ErrorMessage = "Playback could not be started.";
        }
    }

    public async Task StartOverAsync()
    {
        await Player.StopAsync();
        _reportedStart = false;
        await Player.PlayAsync(_client.GetStreamUri(_item));
    }

    protected override void OnPlaybackStateChanged(PlaybackState state)
    {
        if (state == PlaybackState.Playing && !_reportedStart)
        {
            _reportedStart = true;
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

        var report = new PlaybackReport(_item.Id, _playSessionId, _lastPosition, State == PlaybackState.Paused);
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
        ReportStopped();
        _progressTimer.Stop();
        _ = Player.StopAsync();
        base.Dispose();
    }
}
