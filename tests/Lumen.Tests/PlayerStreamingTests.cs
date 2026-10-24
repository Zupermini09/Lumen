using System.Collections.Concurrent;
using Lumen.Core;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Lumen.Platform;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.Tests;

public class StreamingSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lumen-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_original_is_the_default_and_the_limit_survives_a_restart()
    {
        Assert.Null(new AppSettings().MaxStreamingBitrate);

        var log = new LoggerConfiguration().CreateLogger();
        var first = new JsonSettingsStore(_dir, log);
        first.Settings.MaxStreamingBitrate = 4_000_000;
        first.Save();
        Assert.Equal(4_000_000, new JsonSettingsStore(_dir, log).Settings.MaxStreamingBitrate);
    }

    [Fact]
    public void The_settings_page_lists_the_qualities_and_saves_the_choice()
    {
        var store = new MemorySettings();
        store.Settings.MaxStreamingBitrate = 8_000_000;
        var page = new SettingsViewModel(store, new NoThemes());

        Assert.Equal(8_000_000, page.StreamingQuality.Value);
        Assert.Equal(QualityPresets.All.Select(p => p.Label), page.StreamingQualityChoices.Select(c => c.Label));
        Assert.Equal(0, store.Saves);

        page.StreamingQuality = page.StreamingQualityChoices.Single(c => c.Value == 1_500_000);
        Assert.Equal(1_500_000, store.Settings.MaxStreamingBitrate);
        page.StreamingQuality = page.StreamingQualityChoices[0];
        Assert.Null(store.Settings.MaxStreamingBitrate);
        Assert.Equal(2, store.Saves);
    }

    private sealed class NoThemes : IThemeService
    {
        public void Apply(AppTheme theme) { }
    }
}

public class PlayerStreamingTests
{
    private static readonly MediaItem Movie = new()
    {
        Id = "m1", Name = "Film", Type = "Movie", MediaType = "Video", RunTime = TimeSpan.FromMinutes(100),
        PlaybackPosition = TimeSpan.FromMinutes(12),
    };

    private static readonly IReadOnlyList<MediaStreamInfo> Streams =
    [
        new(0, "Video", "hevc", null, "HEVC", true, false, false),
        new(1, "Audio", "ac3", "eng", "English", true, false, false),
        new(2, "Audio", "dts", "fra", "French", false, false, false),
        new(3, "Subtitle", "subrip", "eng", "English", false, false, false),
        new(4, "Subtitle", "PGSSUB", "fra", "French", false, false, false),
    ];

    private sealed class FakePlayer : IPlayerService
    {
        public PlaybackState State { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
        public int Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public int VideoOutputCount => 1;
        public string EngineDescription => "Fake";
        public IReadOnlyList<TrackInfo> AudioTracks => [];
        public IReadOnlyList<TrackInfo> SubtitleTracks => [];
        public int AudioTrackId => -1;
        public int SubtitleTrackId => -1;
        public TimeSpan SubtitleDelay { get; set; }
        public double Rate { get; set; } = 1;
        public List<(Uri Uri, TimeSpan? StartAt)> Played { get; } = [];
        public int Stops { get; private set; }

        public void SelectAudioTrack(int id) { }
        public void SelectSubtitleTrack(int id) { }
        public void AddExternalSubtitle(Uri uri, bool select) { }
        public void Dispose() { }
        public void TogglePause() { }
        public void Seek(TimeSpan position) { }

        public Task PlayAsync(Uri source, TimeSpan? startAt = null, CancellationToken cancellationToken = default)
        {
            Played.Add((source, startAt));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stops++;
            return Task.CompletedTask;
        }

#pragma warning disable CS0067 // never raised: the tests set the state on the page model directly
        public event EventHandler? TracksChanged;
        public event EventHandler<PlaybackState>? StateChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<string>? ErrorOccurred;
#pragma warning restore CS0067
    }

    private sealed class FakeSurfaces : IVideoSurfaceFactory
    {
        public object CreateSurface(object? overlay = null) => new object();
    }

    /// <summary>A server that answers PlaybackInfo the way the test scripts it and records every request and report.</summary>
    private sealed class FakeServer : StubJellyfinClient
    {
        public ConcurrentQueue<PlaybackRequest> Requests { get; } = [];
        public ConcurrentQueue<(string Kind, PlaybackReport Report)> Reports { get; } = [];
        public ConcurrentQueue<string> StoppedEncodings { get; } = [];
        public Func<PlaybackRequest, PlaybackDetails> Answer { get; set; } = _ => Direct();
        public bool Unreachable { get; set; }

        public override Task<PlaybackDetails> GetPlaybackDetailsAsync(MediaItem item, PlaybackRequest? request = null, CancellationToken cancellationToken = default)
        {
            request ??= new PlaybackRequest();
            Requests.Enqueue(request);
            return Unreachable ? throw new HttpRequestException("down") : Task.FromResult(Answer(request));
        }

        public override Uri GetStreamUri(MediaItem item, string? mediaSourceId = null) => new($"https://media.example.org/Videos/{item.Id}/stream?static=true");
        public override Task<MediaItem?> GetNextEpisodeAsync(MediaItem episode, CancellationToken cancellationToken = default) => Task.FromResult<MediaItem?>(null);
        public override Task<IReadOnlyList<MediaSegment>> GetMediaSegmentsAsync(MediaItem item, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaSegment>>([]);
        public override Uri GetSubtitleUri(MediaItem item, string mediaSourceId, MediaStreamInfo stream) => new($"https://media.example.org/sub/{stream.Index}.srt");

        public override Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken = default) => Record("start", report);
        public override Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default) => Record("progress", report);
        public override Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default) => Record("stop", report);

        public override Task StopTranscodingAsync(string playSessionId, CancellationToken cancellationToken = default)
        {
            StoppedEncodings.Enqueue(playSessionId);
            return Task.CompletedTask;
        }

        private Task Record(string kind, PlaybackReport report)
        {
            Reports.Enqueue((kind, report));
            return Task.CompletedTask;
        }
    }

    private static PlaybackDetails Direct(string session = "direct1") =>
        new("m1", session, Streams, 1, null, []) { Method = PlayMethod.DirectPlay };

    private static PlaybackDetails Transcode(string session, string reasons = "ContainerBitrateExceedsLimit", int audio = 1, int? burned = null) =>
        new("m1", session, Streams, 1, null, [])
        {
            Method = PlayMethod.Transcode,
            Playable = true,
            TranscodeUri = new Uri($"https://media.example.org/videos/m1/master.m3u8?PlaySessionId={session}&ApiKey=secret"),
            TranscodeReasons = reasons.Split(','),
            TranscodeAudioStreamIndex = audio,
            BurnedSubtitleStreamIndex = burned,
        };

    private static PlaybackDetails NotPlayable(bool transcodingAllowed) =>
        Direct() with { Playable = false, TranscodingAllowed = transcodingAllowed };

    private static (PlayerViewModel Page, FakePlayer Player, FakeServer Server, MemorySettings Settings) Create(
        int? savedLimit = null, MediaItem? item = null)
    {
        var settings = new MemorySettings();
        settings.Settings.MaxStreamingBitrate = savedLimit;
        var player = new FakePlayer();
        var server = new FakeServer();
        var page = new PlayerViewModel(item ?? Movie, server, player, new FakeSurfaces(), settings, new RecordingNavigator(), new FakePages(),
            new LoggerConfiguration().CreateLogger());
        return (page, player, server, settings);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "timed out");
    }

    private static MenuOption Quality(PlayerViewModel page, int? bitrate) =>
        page.QualityOptions().Single(o => o.Label == QualityPresets.For(bitrate).Label);

    [Fact]
    public async Task By_default_the_original_file_is_direct_played_from_the_resume_point()
    {
        var (page, player, server, _) = Create();
        await page.StartAsync();

        var request = Assert.Single(server.Requests);
        Assert.Null(request.MaxBitrate);
        Assert.True(request.AllowDirectPlay);
        var (uri, startAt) = Assert.Single(player.Played);
        Assert.Equal("https://media.example.org/Videos/m1/stream?static=true", uri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromMinutes(12), startAt);
        Assert.Equal("Direct play", page.ModeText);
        Assert.Equal("Original", page.QualityText);
        Assert.True(page.CanChooseQuality);
        Assert.Null(page.Notice);
    }

    [Fact]
    public async Task A_saved_limit_is_asked_for_and_the_conversion_is_played_with_the_chosen_tracks()
    {
        var (page, player, server, _) = Create(savedLimit: 4_000_000);
        server.Answer = request => request.AudioStreamIndex is null ? Transcode("first") : Transcode("second");
        await page.StartAsync();

        // The first answer only tells the streams; the tracks are then asked for explicitly: audio 1, no burned-in subtitle.
        var requests = server.Requests.ToList();
        Assert.Equal(2, requests.Count);
        Assert.Equal(4_000_000, requests[0].MaxBitrate);
        Assert.Equal(4_000_000, requests[1].MaxBitrate);
        Assert.Equal("m1", requests[1].MediaSourceId);
        Assert.Equal(1, requests[1].AudioStreamIndex);
        Assert.Equal(-1, requests[1].SubtitleStreamIndex);
        Assert.False(requests[1].IncludeChapters);

        var (uri, startAt) = Assert.Single(player.Played);
        Assert.Contains("PlaySessionId=second", uri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromMinutes(12), startAt);
        Assert.Equal("Transcoding 4 Mbps", page.ModeText);
        Assert.Equal("720p · 4 Mbps", page.QualityText);
    }

    [Fact]
    public async Task A_picture_subtitle_that_is_wanted_is_burned_into_the_conversion()
    {
        var (page, _, server, settings) = Create(savedLimit: 4_000_000);
        settings.Settings.SubtitleMode = SubtitleMode.Always;
        settings.Settings.PreferredSubtitleLanguage = "fra"; // the French stream is PGS
        server.Answer = request => Transcode("s");
        await page.StartAsync();

        Assert.Equal(4, server.Requests.Last().SubtitleStreamIndex);
    }

    [Fact]
    public async Task A_conversion_the_user_did_not_ask_for_gets_a_notice_with_the_reason_and_a_sane_bitrate()
    {
        var (page, player, server, _) = Create();
        server.Answer = request => request.AudioStreamIndex is null
            ? Transcode("first", "VideoCodecNotSupported")
            : Transcode("second", "VideoCodecNotSupported");
        await page.StartAsync();

        Assert.Equal("Transcoding: the video codec is not supported", page.Notice);
        Assert.Equal(StreamingPolicy.FallbackBitrate, server.Requests.Last().MaxBitrate);
        Assert.Equal($"Transcoding {QualityPresets.FormatBitrate(StreamingPolicy.FallbackBitrate)}", page.ModeText);
        Assert.Contains("master.m3u8", player.Played.Single().Uri.AbsoluteUri);
    }

    [Fact]
    public async Task When_the_server_refuses_the_saved_limit_the_original_plays_with_a_notice()
    {
        var (page, player, server, settings) = Create(savedLimit: 2_000_000);
        server.Answer = request => request.MaxBitrate is null ? Direct() : NotPlayable(transcodingAllowed: false);
        await page.StartAsync();

        Assert.Equal("Server does not allow transcoding for this user. Playing the original.", page.Notice);
        Assert.Equal("Direct play", page.ModeText);
        Assert.Equal("Original", page.QualityText);
        Assert.StartsWith("https://media.example.org/Videos/m1/stream", Assert.Single(player.Played).Uri.AbsoluteUri);
        Assert.Equal(2_000_000, settings.Settings.MaxStreamingBitrate); // the setting itself is left alone
    }

    [Fact]
    public async Task Without_playback_info_the_file_plays_directly_like_before()
    {
        var (page, player, server, _) = Create();
        server.Unreachable = true;
        await page.StartAsync();

        Assert.StartsWith("https://media.example.org/Videos/m1/stream", Assert.Single(player.Played).Uri.AbsoluteUri);
        Assert.Equal("Direct play", page.ModeText);
    }

    [Fact]
    public async Task Choosing_a_quality_restarts_at_the_same_position_and_is_remembered()
    {
        var (page, player, server, settings) = Create();
        server.Answer = request => request.MaxBitrate is null ? Direct("direct1") : Transcode("hls1");
        await page.StartAsync();
        player.Position = TimeSpan.FromMinutes(30);
        page.State = PlaybackState.Playing; // starts reporting; the start report carries the play method
        await WaitUntil(() => server.Reports.Any(r => r.Kind == "start"));

        Quality(page, 4_000_000).Select();
        await WaitUntil(() => player.Played.Count == 2);

        var (uri, startAt) = player.Played[1];
        Assert.Contains("PlaySessionId=hls1", uri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromMinutes(30), startAt);
        Assert.Equal("Transcoding 4 Mbps", page.ModeText);
        Assert.Equal(4_000_000, settings.Settings.MaxStreamingBitrate);
        Assert.Equal(1, settings.Saves);
        Assert.Equal(1, player.Stops);

        // The old direct play session is ended with its own method and session id.
        await WaitUntil(() => server.Reports.Any(r => r.Kind == "stop"));
        var stop = server.Reports.Single(r => r.Kind == "stop").Report;
        Assert.Equal("direct1", stop.PlaySessionId);
        Assert.Equal(PlayMethod.DirectPlay, stop.Method);
        Assert.Empty(server.StoppedEncodings); // nothing was being converted yet
    }

    [Fact]
    public async Task Going_back_to_the_original_stops_the_servers_conversion()
    {
        var (page, player, server, settings) = Create(savedLimit: 4_000_000);
        server.Answer = request => request.MaxBitrate is null ? Direct("direct1") : Transcode("hls1");
        await page.StartAsync();
        player.Position = TimeSpan.FromMinutes(45);
        page.State = PlaybackState.Playing;
        await WaitUntil(() => server.Reports.Any(r => r.Kind == "start"));
        Assert.Equal(PlayMethod.Transcode, server.Reports.Single(r => r.Kind == "start").Report.Method);

        Quality(page, null).Select();
        await WaitUntil(() => player.Played.Count == 2);

        Assert.StartsWith("https://media.example.org/Videos/m1/stream", player.Played[1].Uri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromMinutes(45), player.Played[1].StartAt);
        Assert.Equal("Direct play", page.ModeText);
        Assert.Null(settings.Settings.MaxStreamingBitrate);
        await WaitUntil(() => server.StoppedEncodings.Count == 1);
        Assert.Equal("hls1", server.StoppedEncodings.Single());
        var stop = server.Reports.Single(r => r.Kind == "stop").Report;
        Assert.Equal(PlayMethod.Transcode, stop.Method);
        Assert.Equal("hls1", stop.PlaySessionId);
    }

    [Fact]
    public async Task A_limit_above_the_files_bitrate_changes_the_setting_without_restarting()
    {
        var (page, player, server, settings) = Create();
        server.Answer = _ => Direct(); // the server finds the file fits
        await page.StartAsync();

        Quality(page, 40_000_000).Select();
        await WaitUntil(() => settings.Settings.MaxStreamingBitrate == 40_000_000);

        Assert.Single(player.Played);
        Assert.Equal(0, player.Stops);
        Assert.Equal("Direct play", page.ModeText);
        Assert.Equal("1080p · 40 Mbps", page.QualityText);
    }

    [Fact]
    public async Task A_refused_quality_change_leaves_playback_and_the_setting_alone()
    {
        var (page, player, server, settings) = Create();
        server.Answer = request => request.MaxBitrate is null ? Direct() : NotPlayable(transcodingAllowed: false);
        await page.StartAsync();

        Quality(page, 4_000_000).Select();
        await WaitUntil(() => page.Notice is not null);

        Assert.Equal("Server does not allow transcoding for this user", page.Notice);
        Assert.Single(player.Played);
        Assert.Equal(0, player.Stops);
        Assert.Null(settings.Settings.MaxStreamingBitrate);
        Assert.Equal("Original", page.QualityText);
        Assert.Equal("Direct play", page.ModeText);
        Assert.True(Quality(page, null).IsSelected);
    }

    [Fact]
    public async Task A_failing_direct_play_is_retried_once_through_a_conversion_with_a_notice()
    {
        var (page, player, server, _) = Create();
        server.Answer = request => request.AllowDirectPlay ? Direct() : Transcode("hls1", "DirectPlayError");
        await page.StartAsync();

        page.State = PlaybackState.Error;
        await WaitUntil(() => player.Played.Count == 2);

        var retry = server.Requests.Last();
        Assert.False(retry.AllowDirectPlay);
        Assert.Equal(StreamingPolicy.FallbackBitrate, retry.MaxBitrate);
        Assert.Contains("master.m3u8", player.Played[1].Uri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromMinutes(12), player.Played[1].StartAt);
        Assert.Equal("Transcoding: direct play failed", page.Notice);
        Assert.Null(page.ErrorMessage);

        // A second failure is not retried again.
        page.State = PlaybackState.Playing;
        page.State = PlaybackState.Error;
        await Task.Delay(100);
        Assert.Equal(2, player.Played.Count);
    }

    [Fact]
    public async Task A_failure_with_conversion_forbidden_keeps_the_error_and_says_why()
    {
        var (page, player, server, _) = Create();
        server.Answer = request => request.AllowDirectPlay ? Direct() : NotPlayable(transcodingAllowed: false);
        await page.StartAsync();

        page.State = PlaybackState.Error;
        await WaitUntil(() => page.Notice is not null);

        Assert.Equal("Server does not allow transcoding for this user", page.Notice);
        Assert.Single(player.Played);
    }

    [Fact]
    public async Task Music_has_no_quality_menu_and_no_conversion()
    {
        var song = Movie with { Id = "a1", Type = "Audio", MediaType = "Audio" };
        var (page, player, server, _) = Create(savedLimit: 4_000_000, item: song);
        await page.StartAsync();

        Assert.False(page.CanChooseQuality);
        Assert.Null(page.ModeText);
        Assert.Null(server.Requests.Single().MaxBitrate);

        page.State = PlaybackState.Error;
        await Task.Delay(100);
        Assert.Single(player.Played);
    }

    [Fact]
    public async Task Leaving_a_converted_video_stops_the_servers_conversion()
    {
        var (page, player, server, _) = Create(savedLimit: 4_000_000);
        server.Answer = _ => Transcode("hls1");
        await page.StartAsync();

        page.Dispose();

        await WaitUntil(() => server.StoppedEncodings.Count == 1);
        Assert.Equal("hls1", server.StoppedEncodings.Single());
        Assert.Equal(1, player.Stops);
    }

    [Fact]
    public async Task Leaving_a_direct_play_does_not_touch_the_servers_encodings()
    {
        var (page, _, server, _) = Create();
        await page.StartAsync();
        page.Dispose();
        await Task.Delay(100);
        Assert.Empty(server.StoppedEncodings);
    }

    [Fact]
    public async Task The_audio_of_a_converted_video_is_changed_by_restarting_with_the_other_stream()
    {
        var (page, player, server, _) = Create(savedLimit: 4_000_000);
        server.Answer = request => Transcode("hls" + server.Requests.Count, audio: request.AudioStreamIndex ?? 1);
        await page.StartAsync();
        player.Position = TimeSpan.FromMinutes(20);

        var french = page.AudioOptions().Single(o => o.Label == "French");
        Assert.False(french.IsSelected);
        french.Select();
        await WaitUntil(() => player.Played.Count == 2);

        Assert.Equal(2, server.Requests.Last().AudioStreamIndex);
        Assert.Equal(4_000_000, server.Requests.Last().MaxBitrate);
        Assert.Equal(TimeSpan.FromMinutes(20), player.Played[1].StartAt);
        Assert.True(page.AudioOptions().Single(o => o.Label == "French").IsSelected);
    }

    [Fact]
    public async Task Picking_a_picture_subtitle_in_a_converted_video_burns_it_in_with_a_new_stream()
    {
        var (page, player, server, _) = Create(savedLimit: 4_000_000);
        server.Answer = request => Transcode("hls" + server.Requests.Count, burned: request.SubtitleStreamIndex is >= 0 ? request.SubtitleStreamIndex : null);
        await page.StartAsync();

        var options = page.SubtitleOptions();
        Assert.Equal(["Off", "English", "French (burned in)"], options.Select(o => o.Label));
        options.Single(o => o.Label == "French (burned in)").Select();
        await WaitUntil(() => player.Played.Count == 2);

        Assert.Equal(4, server.Requests.Last().SubtitleStreamIndex);
        Assert.True(page.SubtitleOptions().Single(o => o.Label == "French (burned in)").IsSelected);

        // Turning it off needs another stream, without the burn-in.
        page.SubtitleOptions().First().Select();
        await WaitUntil(() => player.Played.Count == 3);
        Assert.Equal(-1, server.Requests.Last().SubtitleStreamIndex);
    }

    [Fact]
    public async Task The_items_runtime_is_shown_while_the_player_knows_no_length()
    {
        var (page, _, _, _) = Create();
        await page.StartAsync();
        page.State = PlaybackState.Playing; // the player reports no duration yet

        Assert.Equal(TimeSpan.FromMinutes(100).TotalSeconds, page.DurationSeconds);
    }

    [Fact]
    public async Task A_converted_video_keeps_the_users_subtitle_delay_on_top_of_the_timeline_offset()
    {
        var (page, player, server, _) = Create(savedLimit: 4_000_000);
        server.Answer = _ => Transcode("hls1");
        await page.StartAsync();
        player.SubtitleDelay = StreamingPolicy.HlsTimestampOffset;
        Assert.Equal("Subtitle delay: 0.0 s", page.SubtitleDelayText);

        page.AdjustSubtitleDelay(TimeSpan.FromMilliseconds(500));
        Assert.Equal("Subtitle delay: +0.5 s", page.SubtitleDelayText);
        page.ResetSubtitleDelay();
        Assert.Equal(StreamingPolicy.HlsTimestampOffset, player.SubtitleDelay);
    }
}
