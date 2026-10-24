using Lumen.Core.Playback;

namespace Lumen.UI.Views;

/// <summary>When the player's floating header and controls may disappear.</summary>
internal static class ChromePolicy
{
    /// <summary>How long the mouse must rest before the controls (and the cursor) go away during playback.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// True if the controls should hide now: only while playing, once the mouse has rested for the timeout, and never
    /// while the mouse is over them, a menu is open, the skip button is up or the next-episode countdown runs.
    /// </summary>
    public static bool ShouldHide(PlaybackState state, TimeSpan idleFor, bool pointerOverChrome, bool menuOpen,
        bool hasSkip, bool countingDown) =>
        state == PlaybackState.Playing && idleFor >= IdleTimeout
        && !pointerOverChrome && !menuOpen && !hasSkip && !countingDown;
}
