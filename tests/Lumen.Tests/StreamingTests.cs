using System.Net;
using System.Text.Json;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Lumen.Jellyfin;
using Serilog;

namespace Lumen.Tests;

public class QualityPresetTests
{
    [Fact]
    public void The_menu_starts_with_the_original_and_goes_down_in_bitrate()
    {
        Assert.Null(QualityPresets.All[0].MaxBitrate);
        Assert.Equal("Original (direct play)", QualityPresets.All[0].Label);
        Assert.Equal([40_000_000, 20_000_000, 10_000_000, 8_000_000, 4_000_000, 2_000_000, 1_500_000, 720_000],
            QualityPresets.All.Skip(1).Select(p => p.MaxBitrate!.Value));
        Assert.Equal(QualityPresets.All.Skip(1).Select(p => p.MaxBitrate).OrderDescending(), QualityPresets.All.Skip(1).Select(p => p.MaxBitrate));
    }

    [Theory]
    [InlineData(20_000_000, "1080p · 20 Mbps")]
    [InlineData(4_000_000, "720p · 4 Mbps")]
    [InlineData(1_500_000, "480p · 1.5 Mbps")]
    [InlineData(720_000, "360p · 720 kbps")]
    public void Presets_are_labelled_with_resolution_and_bitrate(int bitrate, string label) =>
        Assert.Equal(label, QualityPresets.For(bitrate).Label);

    [Theory]
    [InlineData(40_000_000, "40 Mbps")]
    [InlineData(1_500_000, "1.5 Mbps")]
    [InlineData(720_000, "720 kbps")]
    [InlineData(13_000_000, "13 Mbps")]
    public void Bitrates_are_formatted_for_people(int bitrate, string text) =>
        Assert.Equal(text, QualityPresets.FormatBitrate(bitrate));

    [Fact]
    public void A_bitrate_from_an_edited_settings_file_stays_selectable()
    {
        Assert.Same(QualityPresets.All, QualityPresets.With(null));
        Assert.Same(QualityPresets.All, QualityPresets.With(4_000_000));
        var custom = QualityPresets.With(13_000_000);
        Assert.Equal(QualityPresets.All.Count + 1, custom.Count);
        Assert.Equal("13 Mbps", custom[^1].Label);
    }

    [Fact]
    public void The_button_text_is_short_for_the_original_and_the_label_otherwise()
    {
        Assert.Equal("Original", QualityPresets.ShortLabel(null));
        Assert.Equal("720p · 4 Mbps", QualityPresets.ShortLabel(4_000_000));
    }
}

public class StreamingPolicyTests
{
    [Fact]
    public void Direct_play_that_fails_right_after_opening_falls_back_once()
    {
        var soon = TimeSpan.FromSeconds(2);
        Assert.True(StreamingPolicy.ShouldFallBack(PlayMethod.DirectPlay, alreadyTried: false, isVideo: true, soon));
        Assert.True(StreamingPolicy.ShouldFallBack(PlayMethod.DirectStream, false, true, soon));
        Assert.False(StreamingPolicy.ShouldFallBack(PlayMethod.DirectPlay, alreadyTried: true, true, soon));
    }

    [Fact]
    public void Other_failures_do_not_start_a_conversion()
    {
        Assert.False(StreamingPolicy.ShouldFallBack(PlayMethod.Transcode, false, true, TimeSpan.FromSeconds(2))); // already converted
        Assert.False(StreamingPolicy.ShouldFallBack(PlayMethod.DirectPlay, false, true, TimeSpan.FromMinutes(5))); // network drop mid-film
        Assert.False(StreamingPolicy.ShouldFallBack(PlayMethod.DirectPlay, false, isVideo: false, TimeSpan.FromSeconds(2))); // music
    }

    [Fact]
    public void The_fallback_request_forbids_direct_play_and_keeps_the_users_limit()
    {
        var withLimit = StreamingPolicy.FallbackRequest(new PlaybackRequest { MaxBitrate = 4_000_000, AudioStreamIndex = 2 });
        Assert.False(withLimit.AllowDirectPlay);
        Assert.Equal(4_000_000, withLimit.MaxBitrate);
        Assert.Equal(2, withLimit.AudioStreamIndex);

        var original = StreamingPolicy.FallbackRequest(new PlaybackRequest());
        Assert.False(original.AllowDirectPlay);
        Assert.Equal(StreamingPolicy.FallbackBitrate, original.MaxBitrate);
    }

    [Fact]
    public void The_outcome_tells_a_refused_conversion_from_a_missing_stream()
    {
        var item = new MediaItem { Id = "1", Name = "F", Type = "Movie" };
        var details = PlaybackDetails.Fallback(item);
        Assert.Equal(StreamOutcome.Play, StreamingPolicy.Evaluate(details));
        Assert.Equal(StreamOutcome.TranscodingNotAllowed,
            StreamingPolicy.Evaluate(details with { Playable = false, TranscodingAllowed = false }));
        Assert.Equal(StreamOutcome.NoCompatibleStream, StreamingPolicy.Evaluate(details with { Playable = false }));
        Assert.Equal("Server does not allow transcoding for this user", StreamingPolicy.NoStreamMessage(StreamOutcome.TranscodingNotAllowed));
    }

    [Fact]
    public void The_playback_mode_is_described_for_the_controls()
    {
        Assert.Equal("Direct play", StreamingPolicy.Describe(PlayMethod.DirectPlay, null));
        Assert.Equal("Direct play", StreamingPolicy.Describe(PlayMethod.DirectPlay, 4_000_000));
        Assert.Equal("Direct stream", StreamingPolicy.Describe(PlayMethod.DirectStream, null));
        Assert.Equal("Transcoding 4 Mbps", StreamingPolicy.Describe(PlayMethod.Transcode, 4_000_000));
        Assert.Equal("Transcoding 720 kbps", StreamingPolicy.Describe(PlayMethod.Transcode, 720_000));
        Assert.Equal("Transcoding", StreamingPolicy.Describe(PlayMethod.Transcode, null));
    }

    [Fact]
    public void Transcode_reasons_are_put_in_words()
    {
        Assert.Equal("Transcoding: the video codec is not supported, the bitrate is above the limit",
            StreamingPolicy.TranscodeNotice(["VideoCodecNotSupported", "ContainerBitrateExceedsLimit", "VideoBitrateNotSupported"]));
        Assert.Equal("Transcoding: direct play failed", StreamingPolicy.TranscodeNotice(["DirectPlayError"]));
        Assert.Equal("Transcoding: video bit depth not supported", StreamingPolicy.TranscodeNotice(["VideoBitDepthNotSupported"]));
        Assert.Equal("Transcoding: the server converts this video", StreamingPolicy.TranscodeNotice([]));
    }

    [Theory]
    [InlineData("PGSSUB", true)]
    [InlineData("dvdsub", true)]
    [InlineData("hdmv_pgs_subtitle", true)]
    [InlineData("subrip", false)]
    [InlineData("ass", false)]
    [InlineData(null, false)]
    public void Picture_subtitles_are_the_ones_that_get_burned_in(string? codec, bool image) =>
        Assert.Equal(image, StreamingPolicy.IsImageSubtitle(codec));

    [Fact]
    public void Transcoding_urls_never_reach_the_logs_with_their_token() =>
        Assert.Equal("Retrieving /videos/1/main.m3u8?DeviceId=d&PlaySessionId=p&ApiKey=*** next",
            Lumen.Player.LibVlcEngine.Redact("Retrieving /videos/1/main.m3u8?DeviceId=d&PlaySessionId=p&ApiKey=0123abcd next"));
}

public class PlaybackInfoTests
{
    private static readonly Uri Server = new("https://media.example.org/");

    private static JsonElement Body(PlaybackRequest request) =>
        JsonSerializer.SerializeToElement(JellyfinClient.BuildPlaybackInfoBody(request, "u1"));

    [Fact]
    public void The_original_asks_for_no_limit_and_leaves_direct_play_on()
    {
        var body = Body(new PlaybackRequest());
        Assert.Equal(StreamingPolicy.UnlimitedBitrate, body.GetProperty("MaxStreamingBitrate").GetInt32());
        Assert.Equal(StreamingPolicy.UnlimitedBitrate, body.GetProperty("DeviceProfile").GetProperty("MaxStreamingBitrate").GetInt32());
        Assert.Equal("u1", body.GetProperty("UserId").GetString());
        Assert.False(body.TryGetProperty("EnableDirectPlay", out _));
        Assert.False(body.TryGetProperty("AudioStreamIndex", out _));
        Assert.False(body.TryGetProperty("SubtitleStreamIndex", out _));
    }

    [Fact]
    public void A_limit_is_sent_both_ways_and_forcing_a_conversion_switches_direct_play_off()
    {
        var body = Body(new PlaybackRequest { MaxBitrate = 4_000_000, AllowDirectPlay = false });
        Assert.Equal(4_000_000, body.GetProperty("MaxStreamingBitrate").GetInt32());
        Assert.Equal(4_000_000, body.GetProperty("DeviceProfile").GetProperty("MaxStreamingBitrate").GetInt32());
        Assert.False(body.GetProperty("EnableDirectPlay").GetBoolean());
        Assert.False(body.GetProperty("EnableDirectStream").GetBoolean());
        Assert.True(body.GetProperty("EnableTranscoding").GetBoolean());
    }

    [Fact]
    public void Track_wishes_travel_with_the_file_version_they_belong_to()
    {
        var body = Body(new PlaybackRequest { MediaSourceId = "src", AudioStreamIndex = 3, SubtitleStreamIndex = -1 });
        Assert.Equal("src", body.GetProperty("MediaSourceId").GetString());
        Assert.Equal(3, body.GetProperty("AudioStreamIndex").GetInt32());
        Assert.Equal(-1, body.GetProperty("SubtitleStreamIndex").GetInt32());
    }

    [Fact]
    public void The_device_profile_plays_files_as_they_are_and_converts_to_hls()
    {
        var profile = Body(new PlaybackRequest()).GetProperty("DeviceProfile");
        Assert.Contains(profile.GetProperty("DirectPlayProfiles").EnumerateArray(), p => p.GetProperty("Type").GetString() == "Video");

        var transcoding = profile.GetProperty("TranscodingProfiles").EnumerateArray().Single();
        Assert.Equal("hls", transcoding.GetProperty("Protocol").GetString());
        Assert.Equal("ts", transcoding.GetProperty("Container").GetString());
        Assert.Equal("Streaming", transcoding.GetProperty("Context").GetString());
        Assert.Contains("h264", transcoding.GetProperty("VideoCodec").GetString());

        // Picture subtitles are drawn by the player inside a direct play, text subtitles come as files when converted.
        var subtitles = profile.GetProperty("SubtitleProfiles").EnumerateArray()
            .Select(s => (s.GetProperty("Format").GetString(), s.GetProperty("Method").GetString())).ToList();
        Assert.Contains(("pgssub", "Embed"), subtitles);
        Assert.Contains(("srt", "External"), subtitles);
        Assert.DoesNotContain(("pgssub", "External"), subtitles);
    }

    [Theory]
    [InlineData("/videos/abc/master.m3u8?ApiKey=k", "https://media.example.org/videos/abc/master.m3u8?ApiKey=k")]
    [InlineData("videos/abc/master.m3u8?ApiKey=k", "https://media.example.org/videos/abc/master.m3u8?ApiKey=k")]
    [InlineData("https://other.example.org/x.m3u8?ApiKey=k", "https://other.example.org/x.m3u8?ApiKey=k")]
    public void The_transcoding_url_is_put_under_the_servers_address(string url, string expected) =>
        Assert.Equal(expected, JellyfinClient.ResolveTranscodingUrl(Server, url)!.AbsoluteUri);

    [Fact]
    public void A_server_under_a_base_path_keeps_it()
    {
        var uri = JellyfinClient.ResolveTranscodingUrl(new Uri("https://example.org/jellyfin/"), "/videos/abc/master.m3u8?a=1");
        Assert.Equal("https://example.org/jellyfin/videos/abc/master.m3u8?a=1", uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_transcoding_url_means_none(string? url) => Assert.Null(JellyfinClient.ResolveTranscodingUrl(Server, url));

    private static readonly MediaItem Item = new() { Id = "item", Name = "Film", Type = "Movie", MediaType = "Video" };

    private static PlaybackDetails Parse(string json) =>
        JellyfinClient.ToPlaybackDetails(Item, Server, JsonSerializer.Deserialize<PlaybackInfoDto>(json, Json.Options), []);

    private const string Streams = """
        "MediaStreams":[{"Index":0,"Type":"Video","Codec":"hevc"},{"Index":1,"Type":"Audio","Codec":"ac3","Language":"eng"},
                        {"Index":2,"Type":"Subtitle","Codec":"PGSSUB","Language":"eng"}]
        """;

    [Fact]
    public void A_conversion_answer_gives_the_hls_url_and_the_reasons()
    {
        var details = Parse($$"""
            {"PlaySessionId":"ps1","MediaSources":[{"Id":"src",{{Streams}},"SupportsDirectPlay":false,"SupportsDirectStream":false,"SupportsTranscoding":true,
             "DefaultAudioStreamIndex":1,"TranscodingSubProtocol":"hls",
             "TranscodingUrl":"/videos/item/master.m3u8?DeviceId=d&AudioStreamIndex=1&SubtitleStreamIndex=2&PlaySessionId=ps1&ApiKey=k&SubtitleMethod=Encode&TranscodeReasons=VideoCodecNotSupported,ContainerBitrateExceedsLimit"}]}
            """);

        Assert.Equal(PlayMethod.Transcode, details.Method);
        Assert.StartsWith("https://media.example.org/videos/item/master.m3u8?", details.TranscodeUri!.AbsoluteUri);
        Assert.Equal("ps1", details.PlaySessionId);
        Assert.Equal("src", details.MediaSourceId);
        Assert.Equal(["VideoCodecNotSupported", "ContainerBitrateExceedsLimit"], details.TranscodeReasons);
        Assert.Equal(1, details.TranscodeAudioStreamIndex);
        Assert.Equal(2, details.BurnedSubtitleStreamIndex);
        Assert.True(details.Playable);
        Assert.Equal(3, details.Streams.Count);
    }

    [Fact]
    public void Text_subtitles_delivered_as_files_are_not_burned_in()
    {
        var details = Parse($$"""
            {"PlaySessionId":"ps1","MediaSources":[{"Id":"src",{{Streams}},"SupportsDirectPlay":false,"SupportsTranscoding":true,
             "TranscodingUrl":"/videos/item/master.m3u8?AudioStreamIndex=1&ApiKey=k&TranscodeReasons=ContainerBitrateExceedsLimit"}]}
            """);
        Assert.Null(details.BurnedSubtitleStreamIndex);
    }

    [Fact]
    public void A_direct_answer_has_no_url()
    {
        var details = Parse($$"""
            {"PlaySessionId":"ps1","MediaSources":[{"Id":"src",{{Streams}},"SupportsDirectPlay":true,"SupportsDirectStream":true,"SupportsTranscoding":true}]}
            """);
        Assert.Equal(PlayMethod.DirectPlay, details.Method);
        Assert.Null(details.TranscodeUri);
        Assert.Empty(details.TranscodeReasons);
        Assert.True(details.Playable);
    }

    [Fact]
    public void Direct_stream_is_what_the_server_offers_when_direct_play_is_not_possible_but_remuxing_is()
    {
        var details = Parse("""{"MediaSources":[{"Id":"src","SupportsDirectPlay":false,"SupportsDirectStream":true,"SupportsTranscoding":true}]}""");
        Assert.Equal(PlayMethod.DirectStream, details.Method);
        Assert.True(details.Playable);
    }

    [Fact]
    public void An_answer_with_no_way_to_play_says_whether_converting_is_forbidden()
    {
        var refused = Parse("""{"MediaSources":[{"Id":"src","SupportsDirectPlay":false,"SupportsDirectStream":false,"SupportsTranscoding":false}]}""");
        Assert.False(refused.Playable);
        Assert.False(refused.TranscodingAllowed);
        Assert.Equal(StreamOutcome.TranscodingNotAllowed, StreamingPolicy.Evaluate(refused));

        var none = Parse("""{"MediaSources":[{"Id":"src","SupportsDirectPlay":false,"SupportsDirectStream":false,"SupportsTranscoding":true}]}""");
        Assert.False(none.Playable);
        Assert.True(none.TranscodingAllowed);
    }

    [Fact]
    public void An_old_server_that_sends_no_flags_plays_the_file_as_it_is()
    {
        var details = Parse("""{"PlaySessionId":"ps1","MediaSources":[{"Id":"src"}]}""");
        Assert.Equal(PlayMethod.DirectPlay, details.Method);
        Assert.True(details.Playable);
        Assert.True(details.TranscodingAllowed);
    }

    [Fact]
    public void Without_a_media_source_the_item_plays_with_player_defaults()
    {
        var details = Parse("""{"MediaSources":[],"ErrorCode":"NoCompatibleStream"}""");
        Assert.Equal(PlayMethod.DirectPlay, details.Method);
        Assert.Equal("item", details.MediaSourceId);
        Assert.Null(details.PlaySessionId);
    }
}

public class PlaybackRequestsTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];
        public string Response { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return request.RequestUri.AbsolutePath.EndsWith("ActiveEncodings") || request.RequestUri.AbsolutePath.Contains("Sessions/Playing")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) };
        }
    }

    private static (JellyfinClient Client, Handler Handler) Create()
    {
        var handler = new Handler();
        var server = new ServerInfo("srv", "Test", "10.10.0", new Uri("https://media.example.org/"));
        var holder = new SessionHolder { Current = new SessionInfo(server, new UserInfo("u1", "user", null, true), "tok") };
        var client = new JellyfinClient(new HttpClient(handler), new ClientIdentity("Lumen", "1", "PC", "dev1"), holder,
            new LoggerConfiguration().CreateLogger());
        return (client, handler);
    }

    private static readonly MediaItem Item = new() { Id = "item", Name = "Film", Type = "Movie", MediaType = "Video" };

    [Fact]
    public async Task The_playback_info_request_goes_to_the_item_with_the_wishes_in_its_body()
    {
        var (client, handler) = Create();
        handler.Response = """{"PlaySessionId":"ps","MediaSources":[{"Id":"src","SupportsDirectPlay":false,"TranscodingUrl":"/videos/item/master.m3u8?ApiKey=k"}]}""";

        var details = await client.GetPlaybackDetailsAsync(Item,
            new PlaybackRequest { MaxBitrate = 2_000_000, IncludeChapters = false, AudioStreamIndex = 1, MediaSourceId = "src" });

        var request = Assert.Single(handler.Requests); // no chapters request
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/Items/item/PlaybackInfo?userId=u1", request.Path);
        Assert.Equal(2_000_000, JsonDocument.Parse(request.Body).RootElement.GetProperty("MaxStreamingBitrate").GetInt32());
        Assert.Equal(PlayMethod.Transcode, details.Method);
        Assert.Equal("https://media.example.org/videos/item/master.m3u8?ApiKey=k", details.TranscodeUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_chapters_are_only_fetched_when_asked_for()
    {
        var (client, handler) = Create();
        handler.Response = """{"MediaSources":[{"Id":"src"}]}""";
        await client.GetPlaybackDetailsAsync(Item);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("fields=Chapters", handler.Requests[1].Path);
    }

    [Theory]
    [InlineData(PlayMethod.DirectPlay, "DirectPlay")]
    [InlineData(PlayMethod.DirectStream, "DirectStream")]
    [InlineData(PlayMethod.Transcode, "Transcode")]
    public async Task Reports_carry_the_play_method_and_the_play_session(PlayMethod method, string expected)
    {
        var (client, handler) = Create();
        var report = new PlaybackReport("item", "src", "ps1", TimeSpan.FromSeconds(90), IsPaused: false, method);

        await client.ReportPlaybackStartAsync(report);
        await client.ReportPlaybackProgressAsync(report);
        await client.ReportPlaybackStoppedAsync(report);

        Assert.Equal(["/Sessions/Playing", "/Sessions/Playing/Progress", "/Sessions/Playing/Stopped"], handler.Requests.Select(r => r.Path));
        foreach (var request in handler.Requests)
        {
            var body = JsonDocument.Parse(request.Body).RootElement;
            Assert.Equal(expected, body.GetProperty("PlayMethod").GetString());
            Assert.Equal("ps1", body.GetProperty("PlaySessionId").GetString());
            Assert.Equal("src", body.GetProperty("MediaSourceId").GetString());
            Assert.Equal(TimeSpan.FromSeconds(90).Ticks, body.GetProperty("PositionTicks").GetInt64());
        }
    }

    [Fact]
    public async Task Stopping_a_conversion_deletes_the_active_encoding_of_the_play_session()
    {
        var (client, handler) = Create();
        await client.StopTranscodingAsync("ps 1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/Videos/ActiveEncodings?deviceId=dev1&playSessionId=ps%201", request.Path);
    }
}
