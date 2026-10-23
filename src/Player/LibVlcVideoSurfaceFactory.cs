using LibVLCSharp.Avalonia;
using Lumen.Core.Playback;

namespace Lumen.Player;

public sealed class LibVlcVideoSurfaceFactory(LibVlcEngine engine) : IVideoSurfaceFactory
{
    // VideoView hosts a native child window; Content is rendered in a separate overlay window above it,
    // which is how controls can be drawn over the video despite the native-window "airspace" limits.
    public object CreateSurface(object? overlay = null) => new VideoView
    {
        MediaPlayer = engine.MediaPlayer,
        Content = overlay,
    };
}
