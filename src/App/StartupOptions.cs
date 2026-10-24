namespace Lumen.App;

/// <summary>Command-line options. Only used for automated testing today.</summary>
/// <param name="SmokeServer">End-to-end test against a real server; the account comes from the
/// LUMEN_SMOKE_USER / LUMEN_SMOKE_PASSWORD environment variables (never the command line).</param>
/// <param name="VlcVerbose">Write every libvlc message to the log (diagnostics: decoder choice, track selection...).</param>
public sealed record StartupOptions(
    bool SmokeTest, string? SmokePlayPath, bool SmokeRequireVideo, TimeSpan SmokeTimeout, string? SmokeServer = null, bool VlcVerbose = false)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var smoke = false;
        string? play = null;
        string? server = null;
        var requireVideo = true;
        var vlcVerbose = false;
        var timeout = TimeSpan.FromSeconds(30);

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--smoke-test":
                    smoke = true;
                    break;
                case "--smoke-play" when i + 1 < args.Count:
                    play = args[++i];
                    break;
                case "--smoke-server" when i + 1 < args.Count:
                    smoke = true;
                    server = args[++i];
                    break;
                case "--smoke-no-video":
                    // For runners without a usable display/GPU for video output (playback itself is still checked).
                    requireVideo = false;
                    break;
                case "--vlc-verbose":
                    vlcVerbose = true;
                    break;
                case "--smoke-timeout" when i + 1 < args.Count && int.TryParse(args[i + 1], out var seconds):
                    timeout = TimeSpan.FromSeconds(seconds);
                    i++;
                    break;
            }
        }

        return new StartupOptions(smoke, play, requireVideo, timeout, server, vlcVerbose);
    }
}
