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
        MediaPlayer = new MediaPlayer(LibVlc);

        _log.Information("LibVLC {Version} initialized (directory: {Directory})", LibVlc.Version, libVlcDirectory ?? "<default>");
    }

    public LibVLC LibVlc { get; }
    public MediaPlayer MediaPlayer { get; }

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        if (_verbose)
            _log.Information("libvlc {Level} [{Module}] {Message}", e.Level, e.Module, e.Message);
        // libvlc is very chatty at debug level; keep warnings and errors only.
        else if (e.Level == LogLevel.Error)
            _log.Warning("libvlc [{Module}] {Message}", e.Module, e.Message);
        else if (e.Level == LogLevel.Warning)
            _log.Debug("libvlc [{Module}] {Message}", e.Module, e.Message);
    }

    public void Dispose()
    {
        LibVlc.Log -= OnLibVlcLog;
        MediaPlayer.Dispose();
        LibVlc.Dispose();
    }
}
