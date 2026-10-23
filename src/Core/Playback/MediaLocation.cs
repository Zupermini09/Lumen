namespace Lumen.Core.Playback;

/// <summary>Turns user input (a local path or a URL) into a URI the player can open.</summary>
public static class MediaLocation
{
    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "file", "rtsp", "rtmp", "smb", "ftp", "sftp", "udp", "rtp", "mms",
    };

    public static bool TryParse(string? input, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // "Copy as path" on Windows wraps the path in quotes.
        var text = input.Trim().Trim('"', '\'').Trim();
        if (text.Length == 0)
            return false;

        if (Path.IsPathFullyQualified(text) || text.StartsWith("\\\\", StringComparison.Ordinal))
        {
            uri = new Uri(Path.GetFullPath(text));
            return true;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed) && SupportedSchemes.Contains(parsed.Scheme))
        {
            uri = parsed;
            return true;
        }

        return false;
    }
}
