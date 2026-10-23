using System.Text;

namespace Lumen.Jellyfin;

/// <summary>How Lumen identifies itself to the server (shown in the dashboard's device and session lists).</summary>
public sealed record ClientIdentity(string ClientName, string Version, string DeviceName, string DeviceId)
{
    /// <summary>The value of the Authorization header Jellyfin expects ("MediaBrowser ..." scheme).</summary>
    public string AuthorizationHeader(string? accessToken)
    {
        var builder = new StringBuilder("MediaBrowser ");
        builder.Append($"Client=\"{Escape(ClientName)}\", Device=\"{Escape(DeviceName)}\", DeviceId=\"{Escape(DeviceId)}\", Version=\"{Escape(Version)}\"");
        if (!string.IsNullOrEmpty(accessToken))
            builder.Append($", Token=\"{Escape(accessToken)}\"");
        return builder.ToString();
    }

    // Quotes and commas would break the header's key="value" list; the server URL-decodes values.
    private static string Escape(string value) => Uri.EscapeDataString(value);
}
