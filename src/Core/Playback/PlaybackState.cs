namespace Lumen.Core.Playback;

public enum PlaybackState
{
    Idle,
    Opening,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Ended,
    Error,
}
