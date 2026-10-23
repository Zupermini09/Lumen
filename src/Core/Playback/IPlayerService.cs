namespace Lumen.Core.Playback;

/// <summary>
/// Playback engine abstraction. ViewModels only ever talk to this interface, so the backend
/// (LibVLC today) can be swapped later without touching the UI.
/// </summary>
/// <remarks>
/// Events may be raised on a background thread; subscribers must marshal to the UI thread themselves.
/// </remarks>
public interface IPlayerService : IDisposable
{
    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>Volume in the range 0..100.</summary>
    int Volume { get; set; }
    bool IsMuted { get; set; }

    /// <summary>Number of active video outputs (0 for audio-only media or before the first frame).</summary>
    int VideoOutputCount { get; }

    /// <summary>Human-readable engine name and version, e.g. "LibVLC 3.0.21".</summary>
    string EngineDescription { get; }

    event EventHandler<PlaybackState>? StateChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler<TimeSpan>? DurationChanged;
    event EventHandler<string>? ErrorOccurred;

    /// <param name="startAt">Position to start from (resume), or null/zero for the beginning.</param>
    Task PlayAsync(Uri source, TimeSpan? startAt = null, CancellationToken cancellationToken = default);
    void TogglePause();
    Task StopAsync();
    void Seek(TimeSpan position);
}
