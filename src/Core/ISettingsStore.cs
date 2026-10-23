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
}

public interface ISettingsStore
{
    AppSettings Settings { get; }
    void Save();
}
