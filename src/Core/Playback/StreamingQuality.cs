using System.Globalization;
using Lumen.Core.Jellyfin;

namespace Lumen.Core.Playback;

/// <summary>A streaming quality: the most bits per second the server may send (null = the original file).</summary>
public sealed record QualityPreset(int? MaxBitrate, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The qualities offered in the player's quality menu and on the settings page (like the web client's presets).</summary>
public static class QualityPresets
{
    public static readonly QualityPreset Original = new(null, "Original (direct play)");

    public static readonly IReadOnlyList<QualityPreset> All =
    [
        Original,
        new(40_000_000, "1080p · 40 Mbps"),
        new(20_000_000, "1080p · 20 Mbps"),
        new(10_000_000, "1080p · 10 Mbps"),
        new(8_000_000, "720p · 8 Mbps"),
        new(4_000_000, "720p · 4 Mbps"),
        new(2_000_000, "480p · 2 Mbps"),
        new(1_500_000, "480p · 1.5 Mbps"),
        new(720_000, "360p · 720 kbps"),
    ];

    /// <summary>The preset for a bitrate; a bitrate that is none of them (edited settings file) gets a label of its own.</summary>
    public static QualityPreset For(int? maxBitrate) =>
        All.FirstOrDefault(p => p.MaxBitrate == maxBitrate) ?? new QualityPreset(maxBitrate, FormatBitrate(maxBitrate!.Value));

    /// <summary>All presets, plus the current bitrate when it is not one of them (so it stays selectable).</summary>
    public static IReadOnlyList<QualityPreset> With(int? current) =>
        current is null || All.Any(p => p.MaxBitrate == current) ? All : [.. All, For(current)];

    /// <summary>"4 Mbps", "1.5 Mbps", "720 kbps".</summary>
    public static string FormatBitrate(int bitsPerSecond) =>
        bitsPerSecond >= 1_000_000
            ? $"{(bitsPerSecond / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture)} Mbps"
            : $"{(bitsPerSecond / 1000d).ToString("0.##", CultureInfo.InvariantCulture)} kbps";

    /// <summary>The quality button's text: "Original" or e.g. "720p · 4 Mbps".</summary>
    public static string ShortLabel(int? maxBitrate) => maxBitrate is null ? "Original" : For(maxBitrate).Label;
}

/// <summary>What became of a request for a stream, once the server answered.</summary>
public enum StreamOutcome
{
    /// <summary>The server has a stream to play.</summary>
    Play,
    /// <summary>The server has no stream for this request and this user may not have video converted.</summary>
    TranscodingNotAllowed,
    /// <summary>The server has no stream for this request although converting is allowed.</summary>
    NoCompatibleStream,
}

/// <summary>
/// When to direct play, when to ask the server to convert the video, and how to tell the user. Pure logic so it can
/// be unit tested; the player page (which owns timing and the network) applies it.
/// </summary>
public static class StreamingPolicy
{
    /// <summary>
    /// The limit sent for "original quality". Jellyfin applies a low default (8 Mbps) when a client sends none, which
    /// would make the server refuse to direct play any high-bitrate file.
    /// </summary>
    public const int UnlimitedBitrate = 1_000_000_000;

    /// <summary>The bitrate of a conversion the user did not pick a quality for (direct play failed or was refused).</summary>
    public const int FallbackBitrate = 20_000_000;

    /// <summary>
    /// The transcoded HLS segments Jellyfin serves carry timestamps 10 s ahead of the playlist time that the player
    /// shows. Text subtitles, which follow the video's own time, have to be delayed by this much to line up.
    /// </summary>
    public static readonly TimeSpan HlsTimestampOffset = TimeSpan.FromSeconds(10);

    /// <summary>A direct play that fails this soon after opening is blamed on the format, not on the network.</summary>
    public static readonly TimeSpan FallbackWindow = TimeSpan.FromSeconds(30);

    public static bool ShouldFallBack(PlayMethod method, bool alreadyTried, bool isVideo, TimeSpan sinceOpened) =>
        isVideo && !alreadyTried && method != PlayMethod.Transcode && sinceOpened <= FallbackWindow;

    /// <summary>The request after direct play failed: same wishes, but the server converts the video.</summary>
    public static PlaybackRequest FallbackRequest(PlaybackRequest previous) =>
        previous with { AllowDirectPlay = false, MaxBitrate = previous.MaxBitrate ?? FallbackBitrate };

    public static StreamOutcome Evaluate(PlaybackDetails details) =>
        details.Playable ? StreamOutcome.Play
        : !details.TranscodingAllowed ? StreamOutcome.TranscodingNotAllowed
        : StreamOutcome.NoCompatibleStream;

    /// <summary>"Direct play", "Direct stream" or "Transcoding 4 Mbps".</summary>
    public static string Describe(PlayMethod method, int? maxBitrate) => method switch
    {
        PlayMethod.DirectPlay => "Direct play",
        PlayMethod.DirectStream => "Direct stream",
        _ => maxBitrate is { } bitrate ? $"Transcoding {QualityPresets.FormatBitrate(bitrate)}" : "Transcoding",
    };

    public static string NoStreamMessage(StreamOutcome outcome) => outcome switch
    {
        StreamOutcome.TranscodingNotAllowed => "Server does not allow transcoding for this user",
        _ => "The server has no compatible stream for this quality",
    };

    /// <summary>"Transcoding: the video codec is not supported" for the reasons the server gives.</summary>
    public static string TranscodeNotice(IReadOnlyList<string> reasons)
    {
        var text = string.Join(", ", reasons.Select(DescribeReason).Distinct());
        return text.Length == 0 ? "Transcoding: the server converts this video" : $"Transcoding: {text}";
    }

    private static string DescribeReason(string reason) => reason switch
    {
        "ContainerNotSupported" => "the file format is not supported",
        "VideoCodecNotSupported" => "the video codec is not supported",
        "AudioCodecNotSupported" => "the audio codec is not supported",
        "SubtitleCodecNotSupported" => "the subtitles have to be burned in",
        "ContainerBitrateExceedsLimit" or "VideoBitrateNotSupported" or "AudioBitrateNotSupported" => "the bitrate is above the limit",
        "DirectPlayError" => "direct play failed",
        _ => SplitWords(reason),
    };

    // "VideoBitDepthNotSupported" -> "video bit depth not supported"
    private static string SplitWords(string name)
    {
        var words = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
                words.Append(' ');
            words.Append(char.ToLowerInvariant(c));
        }
        return words.ToString();
    }

    /// <summary>
    /// Picture-based subtitles (Blu-ray and DVD) cannot be sent as text files; in a converted video they have to be
    /// burned into the picture by the server.
    /// </summary>
    public static bool IsImageSubtitle(string? codec) =>
        codec?.ToLowerInvariant() is "pgssub" or "pgs" or "hdmv_pgs_subtitle" or "dvdsub" or "dvd_subtitle" or "dvbsub" or "dvb_subtitle" or "xsub" or "vobsub";
}
