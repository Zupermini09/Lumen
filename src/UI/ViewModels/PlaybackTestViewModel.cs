using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>Plays a local file or URL, without a server. Useful for checking playback and overlays on each OS.</summary>
public sealed partial class PlaybackTestViewModel(IPlayerService player, IVideoSurfaceFactory surfaceFactory, ISettingsStore settings)
    : TransportViewModel(player, surfaceFactory, settings)
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private string _source = string.Empty;

    public override string Title => "Play a file";

    public override bool KeepInHistory => false;

    private bool CanPlay() => MediaLocation.TryParse(Source, out _);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (!MediaLocation.TryParse(Source, out var uri) || uri is null)
            return;

        ErrorMessage = null;
        await Player.PlayAsync(uri);
    }

    public override void Dispose()
    {
        _ = Player.StopAsync();
        base.Dispose();
    }
}
