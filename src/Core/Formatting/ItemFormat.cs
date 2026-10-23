namespace Lumen.Core.Formatting;

/// <summary>Text for item metadata lines.</summary>
public static class ItemFormat
{
    /// <summary>"2018 – Present" for running shows, "2018 – 2020" for finished ones, otherwise just the year.</summary>
    public static string? YearRange(int? year, int? endYear, string? status)
    {
        if (year is not { } start)
            return null;
        if (status == "Continuing")
            return $"{start} – Present";
        return endYear is { } end && end != start ? $"{start} – {end}" : start.ToString();
    }

    /// <summary>The community rating with one decimal ("8.5"), always with a dot.</summary>
    public static string Rating(double rating) => rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"S1:E3" (as on the cards), or just "E3" without a season, or null without an episode number.</summary>
    public static string? EpisodeCode(int? season, int? episode) => (season, episode) switch
    {
        ({ } s, { } e) => $"S{s}:E{e}",
        (null, { } e) => $"E{e}",
        _ => null,
    };
}
