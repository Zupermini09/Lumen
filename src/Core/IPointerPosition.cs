namespace Lumen.Core;

/// <summary>
/// Reads the mouse position in screen coordinates. Needed because pointer events over the native video window
/// never reach the UI toolkit, so full-screen controls cannot rely on them.
/// </summary>
public interface IPointerPosition
{
    bool TryGetScreenPosition(out int x, out int y);
}
