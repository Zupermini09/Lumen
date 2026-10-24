using Lumen.Core.Jellyfin;

namespace Lumen.Core;

/// <summary>Non-secret app settings, persisted as JSON in the per-user data folder.</summary>
public sealed class AppSettings
{
    /// <summary>Identifies this installation to Jellyfin (shown in the server's device list).</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

    public string? LastServerId { get; set; }
    public List<SavedServer> Servers { get; set; } = [];
    public int Volume { get; set; } = 100;

    /// <summary>Play the next episode automatically (after a countdown) when one ends.</summary>
    public bool AutoPlayNextEpisode { get; set; } = true;

    /// <summary>Seconds the next-episode countdown runs before the next episode starts by itself.</summary>
    public int NextEpisodeCountdownSeconds { get; set; } = 10;

    /// <summary>Seconds the skip back / skip forward buttons and the Left / Right keys jump.</summary>
    public int SkipBackSeconds { get; set; } = 15;
    public int SkipForwardSeconds { get; set; } = 15;

    /// <summary>Preferred audio and subtitle language as an ISO 639-2 code (eng, nor, jpn...); null follows the server.</summary>
    public string? PreferredAudioLanguage { get; set; }
    public string? PreferredSubtitleLanguage { get; set; }
    public Playback.SubtitleMode SubtitleMode { get; set; }

    /// <summary>Let the GPU decode video where the system supports it; turn off if playback shows glitches.</summary>
    public bool HardwareDecoding { get; set; } = true;

    /// <summary>Most bits per second a video may use; the server converts anything above it. Null plays the original file.</summary>
    public int? MaxStreamingBitrate { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Per segment type: show a skip button (default), skip automatically, or ignore.</summary>
    public Dictionary<Playback.SegmentType, Playback.SegmentAction> SegmentActions { get; set; } = new();

    /// <summary>The sort and filter last chosen on each library page, by library id.</summary>
    public Dictionary<string, ItemListOptions> LibraryViews { get; set; } = new();

    public Playback.TrackPreferences GetTrackPreferences() =>
        new(PreferredAudioLanguage, PreferredSubtitleLanguage, SubtitleMode);

    public Playback.SegmentAction ActionFor(Playback.SegmentType type) =>
        SegmentActions.TryGetValue(type, out var action) ? action
        : type == Playback.SegmentType.Unknown ? Playback.SegmentAction.Off
        : Playback.SegmentAction.Ask;
}

public interface ISettingsStore
{
    AppSettings Settings { get; }
    void Save();
}
