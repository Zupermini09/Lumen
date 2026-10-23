using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Lumen.UI.ViewModels;
using Lumen.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Lumen.App;

/// <summary>
/// Automated end-to-end checks, used by CI and for testing against a real server:
/// <list type="bullet">
/// <item><c>--smoke-test [--smoke-play file] [--smoke-no-video] [--smoke-timeout seconds]</c>: the window opens,
/// libvlc loads and, if a file is given, it plays for real.</item>
/// <item><c>--smoke-server address</c> with LUMEN_SMOKE_USER and LUMEN_SMOKE_PASSWORD set: connects, signs in,
/// loads the home page and a library, then plays the first playable item through the real player page.</item>
/// </list>
/// Exits 0 on success, 1 on failure, 2 if the watchdog fires. Never touches the user's saved sessions.
/// </summary>
internal static class SmokeTestRunner
{
    private static readonly TimeSpan MinimumPlayed = TimeSpan.FromSeconds(2);
    private static volatile string _stage = "starting";

    public static void Attach(MainWindow window, IServiceProvider services, StartupOptions options, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var extra = options.SmokeServer is null ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(120);
        StartWatchdog(options.SmokeTimeout + extra);
        _stage = "waiting for the main window to open";

        window.Opened += async (_, _) =>
        {
            var (passed, detail) = options.SmokeServer is { } server
                ? await RunServerAsync(services, server, options)
                : await RunLocalAsync(services, options);
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

    private static async Task<(bool Passed, string Detail)> RunLocalAsync(IServiceProvider services, StartupOptions options)
    {
        try
        {
            _stage = "window opened";
            var player = services.GetRequiredService<IPlayerService>();
            if (options.SmokePlayPath is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                return (true, $"window opened, {player.EngineDescription} loaded");
            }

            if (!MediaLocation.TryParse(options.SmokePlayPath, out var uri) || uri is null)
                return (false, $"invalid path '{options.SmokePlayPath}'");

            var navigator = services.GetRequiredService<INavigator>();
            navigator.NavigateRoot(services.GetRequiredService<IPageFactory>().PlaybackTest());
            await Task.Delay(TimeSpan.FromSeconds(1)); // let the video surface attach
            _stage = "starting playback";
            await player.PlayAsync(uri);
            return await WaitForPlaybackAsync(player, options);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Smoke test failed");
            return (false, ex.Message);
        }
    }

    private static async Task<(bool Passed, string Detail)> RunServerAsync(IServiceProvider services, string address, StartupOptions options)
    {
        var user = Environment.GetEnvironmentVariable("LUMEN_SMOKE_USER");
        var password = Environment.GetEnvironmentVariable("LUMEN_SMOKE_PASSWORD") ?? "";
        if (string.IsNullOrEmpty(user))
            return (false, "set LUMEN_SMOKE_USER (and LUMEN_SMOKE_PASSWORD) for --smoke-server");

        var client = services.GetRequiredService<IJellyfinClient>();
        var holder = services.GetRequiredService<Jellyfin.SessionHolder>();
        var navigator = services.GetRequiredService<INavigator>();
        var pages = services.GetRequiredService<IPageFactory>();
        var player = services.GetRequiredService<IPlayerService>();
        var steps = new List<string>();

        try
        {
            _stage = "finding the server";
            var server = await client.FindServerAsync(address);
            steps.Add($"server {server.Version} at {server.Address.Scheme}");

            _stage = "signing in";
            // Sign in without saving: the smoke test must not replace the user's own saved session.
            var session = await client.AuthenticateAsync(server, user, password);
            holder.Current = session;
            steps.Add("signed in");

            _stage = "loading home";
            var libraries = await client.GetLibrariesAsync();
            var resume = await client.GetResumeItemsAsync(12);
            steps.Add($"{libraries.Count} libraries, {resume.Count} to resume");
            navigator.NavigateRoot(pages.Home());

            _stage = "finding something to play";
            var playable = await FindPlayableAsync(client, libraries);
            if (playable is null)
                return (false, string.Join(", ", steps) + ", but found nothing playable");
            steps.Add($"playing a {playable.Type}");

            _stage = "playing";
            navigator.Navigate(pages.Player(playable));
            var result = await WaitForPlaybackAsync(player, options);

            _stage = "leaving the player";
            navigator.GoBack(); // reports playback stopped, like a user pressing Back
            await Task.Delay(TimeSpan.FromSeconds(2));
            await client.SignOutAsync(session);
            holder.Current = null;
            return (result.Passed, string.Join(", ", steps) + "; " + result.Detail);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Server smoke test failed");
            return (false, string.Join(", ", steps.Append($"failed while {_stage}: {ex.Message}")));
        }
    }

    // Breadth-first through libraries -> shows -> seasons, a few levels deep, until something plays.
    private static async Task<MediaItem?> FindPlayableAsync(IJellyfinClient client, IReadOnlyList<MediaItem> libraries)
    {
        var queue = new Queue<(MediaItem Item, int Depth)>(libraries.Select(l => (l, 0)));
        while (queue.TryDequeue(out var entry))
        {
            var children = await client.GetChildrenAsync(entry.Item);
            if (children.FirstOrDefault(c => c is { IsPlayable: true, MediaType: "Video" }) is { } video)
                return video;
            if (entry.Depth < 3)
                foreach (var folder in children.Where(c => c.IsFolder).Take(3))
                    queue.Enqueue((folder, entry.Depth + 1));
        }

        return null;
    }

    private static async Task<(bool Passed, string Detail)> WaitForPlaybackAsync(IPlayerService player, StartupOptions options)
    {
        var deadline = DateTime.UtcNow + options.SmokeTimeout;
        var startPosition = player.Position;
        while (DateTime.UtcNow < deadline)
        {
            if (player.State == PlaybackState.Error)
                return (false, "player reported an error");
            if (player.State is PlaybackState.Playing or PlaybackState.Ended && player.Position - startPosition > MinimumPlayed)
                break;
            await Task.Delay(250);
        }

        var position = player.Position;
        var state = player.State;
        var videoOutputs = player.VideoOutputCount;
        _stage = "stopping playback";
        await player.StopAsync();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        if (position <= MinimumPlayed && position - startPosition <= MinimumPlayed)
            return (false, $"playback did not advance (state {state}, position {position.TotalSeconds:0.0}s)");
        if (options.SmokeRequireVideo && videoOutputs == 0)
            return (false, $"played to {position.TotalSeconds:0.0}s but no video output was created");

        return (true, $"played to {position.TotalSeconds:0.0}s, {videoOutputs} video output(s), {player.EngineDescription}");
    }
}
