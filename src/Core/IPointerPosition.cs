namespace Lumen.Core;

/// <summary>
/// Reads the mouse position in screen coordinates. Needed because pointer events over the native video window
/// never reach the UI toolkit, so full-screen controls cannot rely on them.
/// </summary>
public interface IPointerPosition
{
    bool TryGetScreenPosition(out int x, out int y);

    /// <summary>Reads whether the primary mouse button is held down right now (anywhere on screen).</summary>
    bool TryGetPrimaryButton(out bool pressed);

    /// <summary>
    /// Whether the window under the mouse belongs to the given top-level window (not covered by another app).
    /// Null when the platform cannot tell.
    /// </summary>
    bool? IsPointerOverWindow(IntPtr topLevelHandle);
}
