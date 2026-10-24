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

    /// <summary>Audio tracks of the current media (VLC's view: embedded tracks in container order).</summary>
    IReadOnlyList<TrackInfo> AudioTracks { get; }

    /// <summary>Subtitle tracks: embedded ones in container order, then added external ones.</summary>
    IReadOnlyList<TrackInfo> SubtitleTracks { get; }

    int AudioTrackId { get; }

    /// <summary>Selected subtitle track id, or -1 when subtitles are off.</summary>
    int SubtitleTrackId { get; }

    void SelectAudioTrack(int id);

    /// <param name="id">Track id, or -1 to turn subtitles off.</param>
    void SelectSubtitleTrack(int id);

    /// <summary>Adds a subtitle file (e.g. served by Jellyfin) to the current media; it is appended to <see cref="SubtitleTracks"/>.</summary>
    void AddExternalSubtitle(Uri uri, bool select);

    /// <summary>Positive values show subtitles later.</summary>
    TimeSpan SubtitleDelay { get; set; }

    /// <summary>Playback speed, 1.0 = normal.</summary>
    double Rate { get; set; }

    /// <summary>Raised (on any thread) when tracks are added, removed or selected.</summary>
    event EventHandler? TracksChanged;

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
