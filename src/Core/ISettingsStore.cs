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

    /// <summary>Per segment type: show a skip button (default), skip automatically, or ignore.</summary>
    public Dictionary<Playback.SegmentType, Playback.SegmentAction> SegmentActions { get; set; } = new();

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
