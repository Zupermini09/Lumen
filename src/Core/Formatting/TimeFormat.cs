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

    /// <summary>A runtime for metadata lines: "1h 41m", "45m", or "30s" below a minute.</summary>
    public static string Duration(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        if (time < TimeSpan.FromMinutes(1))
            return $"{(int)time.TotalSeconds}s";
        var minutes = (int)Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero);
        return minutes >= 60
            ? minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m"
            : $"{minutes}m";
    }

    /// <summary>Formats a byte count as e.g. "182 MB".</summary>
    public static string Megabytes(long bytes) => $"{bytes / (1024 * 1024)} MB";
}
