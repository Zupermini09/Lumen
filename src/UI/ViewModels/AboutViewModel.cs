using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

public sealed class AboutViewModel(AppInfo appInfo, IPlayerService player, ISessionService sessions) : PageViewModel
{
    public override string Title => "About";

    public string Version { get; } = appInfo.Version;
    public string Engine { get; } = player.EngineDescription;
    public string Runtime { get; } = $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";
    public string LogDirectory { get; } = appInfo.LogDirectory;
    public string Server { get; } = sessions.Current is { } s
        ? $"{s.Server.Name} ({s.Server.Address}) as {s.User.Name}"
        : "Not signed in";
}
