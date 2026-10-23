namespace Lumen.Core.Jellyfin;

public static class ServerAddress
{
    private const int DefaultHttpPort = 8096;
    private const int DefaultHttpsPort = 8920;

    /// <summary>
    /// The base URLs to try for what the user typed, most likely first. Like the web client, an address without
    /// a scheme is tried over https first, then http (and on Jellyfin's default port when none is given).
    /// </summary>
    public static IReadOnlyList<Uri> Candidates(string? input)
    {
        var raw = input?.Trim() ?? string.Empty;
        var text = raw.TrimEnd('/');
        if (text.Length == 0 || text.Any(char.IsWhiteSpace))
            return [];

        if (raw.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(raw, UriKind.Absolute, out var explicitUri)
                   && explicitUri.Scheme is "http" or "https"
                   && explicitUri.Host.Length > 0
                ? [Normalize(explicitUri)]
                : [];
        }

        if (!Uri.TryCreate("http://" + text, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
            return [];

        var hasPort = HasExplicitPort(text);
        var path = parsed.AbsolutePath.TrimEnd('/');
        // Uri.Authority hides default ports (":80"), so build host:port ourselves when one was typed.
        var hostAndPath = (hasPort ? $"{parsed.Host}:{parsed.Port}" : parsed.Host) + path;
        var candidates = new List<string>();

        if (!hasPort)
        {
            candidates.Add($"https://{hostAndPath}");
            candidates.Add($"http://{parsed.Host}:{DefaultHttpPort}{path}");
            candidates.Add($"http://{hostAndPath}");
        }
        else if (parsed.Port == DefaultHttpPort)
        {
            candidates.Add($"http://{hostAndPath}");
            candidates.Add($"https://{hostAndPath}");
        }
        else
        {
            candidates.Add($"https://{hostAndPath}");
            candidates.Add($"http://{hostAndPath}");
        }

        return candidates.Distinct().Select(c => Normalize(new Uri(c))).ToList();
    }

    private static bool HasExplicitPort(string text)
    {
        var authority = text.Split('/')[0];
        // IPv6 literal: [::1]:8096
        if (authority.StartsWith('['))
            return authority.Contains("]:", StringComparison.Ordinal);
        return authority.Contains(':');
    }

    /// <summary>Base URL without query/fragment and with a trailing slash, so relative API paths combine correctly.</summary>
    private static Uri Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";
        return builder.Uri;
    }
}
