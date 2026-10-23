namespace Lumen.Core.Playback;

/// <summary>
/// Creates the platform video surface for the active player backend.
/// The returned object is a UI control (an Avalonia <c>Control</c>); Core stays UI-agnostic, so it is typed as object.
/// </summary>
public interface IVideoSurfaceFactory
{
    /// <param name="overlay">Optional UI content drawn on top of the video (playback controls, skip buttons...).</param>
    object CreateSurface(object? overlay = null);
}
