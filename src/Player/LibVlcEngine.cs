using System.Text.RegularExpressions;
using LibVLCSharp.Shared;
using Serilog;

namespace Lumen.Player;

/// <summary>
/// Owns the single <see cref="LibVLC"/> instance for the app lifetime and the one shared <see cref="MediaPlayer"/>.
/// </summary>
public sealed class LibVlcEngine : IDisposable
{
    private static readonly string[] DefaultOptions =
    [
        "--no-video-title-show", // don't draw the file name over the video on start
        "--no-snapshot-preview",
        "--no-stats",
    ];

    // Stream URLs carry the access token (the player cannot send auth headers); never let it reach the logs.
    private static readonly Regex TokenPattern = new(@"(api_key|ApiKey|Token)=[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger _log;
    private readonly bool _verbose;

    /// <param name="verbose">Log every libvlc message (diagnostics only; libvlc is very chatty).</param>
    public LibVlcEngine(string? libVlcDirectory, ILogger log, bool verbose = false)
    {
        _log = log.ForContext<LibVlcEngine>();
        _verbose = verbose;

        LibVLCSharp.Shared.Core.Initialize(libVlcDirectory);
        LibVlc = new LibVLC(DefaultOptions);
        LibVlc.Log += OnLibVlcLog;
        MediaPlayer = new MediaPlayer(LibVlc)
        {
            // Let mouse and keyboard events reach the app (double-click for full screen, showing the controls on mouse
            // move, shortcuts) instead of being swallowed by VLC's video window.
            EnableMouseInput = false,
            EnableKeyInput = false,
        };

        _log.Information("LibVLC {Version} initialized (directory: {Directory})", LibVlc.Version, libVlcDirectory ?? "<default>");
    }

    public LibVLC LibVlc { get; }
    public MediaPlayer MediaPlayer { get; }

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        var message = Redact(e.Message);
        if (_verbose)
            _log.Information("libvlc {Level} [{Module}] {Message}", e.Level, e.Module, message);
        // libvlc is very chatty at debug level; keep warnings and errors only.
        else if (e.Level == LogLevel.Error)
            _log.Warning("libvlc [{Module}] {Message}", e.Module, message);
        else if (e.Level == LogLevel.Warning)
            _log.Debug("libvlc [{Module}] {Message}", e.Module, message);
    }

    internal static string Redact(string message) => TokenPattern.Replace(message, "$1=***");

    public void Dispose()
    {
        LibVlc.Log -= OnLibVlcLog;
        MediaPlayer.Dispose();
        LibVlc.Dispose();
    }
}
