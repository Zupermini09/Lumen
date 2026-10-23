using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Lumen.Core.Playback;
using Lumen.UI.ViewModels;
using Lumen.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Lumen.App;

/// <summary>
/// Automated end-to-end check used by CI (<c>--smoke-test [--smoke-play file] [--smoke-no-video] [--smoke-timeout seconds]</c>):
/// the window opens, libvlc is loaded and, if a file is given, it plays for real. Exits 0 on success, 1 on failure.
/// </summary>
internal static class SmokeTestRunner
{
    private static readonly TimeSpan MinimumPlayed = TimeSpan.FromSeconds(2);

    private static volatile string _stage = "starting";

    public static void Attach(MainWindow window, IServiceProvider services, StartupOptions options, IClassicDesktopStyleApplicationLifetime desktop)
    {
        StartWatchdog(options.SmokeTimeout + TimeSpan.FromSeconds(60));
        _stage = "waiting for the main window to open";

        window.Opened += async (_, _) =>
        {
            var (passed, detail) = await RunAsync(window, services, options);
            var line = $"SMOKE: {(passed ? "PASS" : "FAIL")} {detail} (RAM {Environment.WorkingSet / (1024 * 1024)} MB)";
            Console.WriteLine(line);
            Log.Information("{SmokeResult}", line);
            _stage = "shutting down";
            desktop.Shutdown(passed ? 0 : 1);
        };
    }

    /// <summary>
    /// Hard deadline for the whole run, independent of the UI thread: a hang anywhere (window never opening,
    /// a deadlock on shutdown) becomes a fast, logged failure instead of a stuck CI job.
    /// </summary>
    private static void StartWatchdog(TimeSpan limit)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(limit);
            var line = $"SMOKE: FAIL watchdog: no result after {limit.TotalSeconds:0}s (stage: {_stage})";
            Console.WriteLine(line);
            Log.Error("{SmokeResult}", line);
            Log.CloseAndFlush();
            Environment.Exit(2);
        })
        { IsBackground = true, Name = "Smoke test watchdog" };
        thread.Start();
    }

    private static async Task<(bool Passed, string Detail)> RunAsync(MainWindow window, IServiceProvider services, StartupOptions options)
    {
        try
        {
            var player = services.GetRequiredService<IPlayerService>();
            _stage = "window opened";
            if (options.SmokePlayPath is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                return (true, $"window opened, {player.EngineDescription} loaded");
            }

            if (!MediaLocation.TryParse(options.SmokePlayPath, out var uri) || uri is null)
                return (false, $"invalid path '{options.SmokePlayPath}'");

            window.Select(PageKey.PlaybackTest);
            await Task.Delay(TimeSpan.FromSeconds(1)); // let the video surface attach
            _stage = "starting playback";
            await player.PlayAsync(uri);
            _stage = "playing";

            var deadline = DateTime.UtcNow + options.SmokeTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (player.State == PlaybackState.Error)
                    return (false, "player reported an error");
                if (player.State is PlaybackState.Playing or PlaybackState.Ended && player.Position > MinimumPlayed)
                    break;
                await Task.Delay(250);
            }

            var position = player.Position;
            var state = player.State;
            var videoOutputs = player.VideoOutputCount;
            _stage = "stopping playback";
            await player.StopAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { });

            if (position <= MinimumPlayed)
                return (false, $"playback did not advance (state {state}, position {position.TotalSeconds:0.0}s)");
            if (options.SmokeRequireVideo && videoOutputs == 0)
                return (false, $"played to {position.TotalSeconds:0.0}s but no video output was created");

            return (true, $"played to {position.TotalSeconds:0.0}s, {videoOutputs} video output(s), {player.EngineDescription}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Smoke test failed");
            return (false, ex.Message);
        }
    }
}
