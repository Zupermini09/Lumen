namespace Lumen.Core.Formatting;

public static class TimeFormat
{
    /// <summary>
    /// Formats a playback time as "m:ss", or "h:mm:ss" when the time (or the reference duration) is an hour or longer,
    /// so the position and duration labels have the same shape.
    /// </summary>
    public static string Playback(TimeSpan time, TimeSpan? reference = null)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        var showHours = time.TotalHours >= 1 || (reference?.TotalHours ?? 0) >= 1;
        return showHours
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    /// <summary>Formats a byte count as e.g. "182 MB".</summary>
    public static string Megabytes(long bytes) => $"{bytes / (1024 * 1024)} MB";
}
