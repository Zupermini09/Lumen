namespace Lumen.Core.Playback;

/// <summary>A language the settings page offers: an ISO 639-2 code and its English name.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Language codes as Jellyfin reports them (ISO 639-2, usually the bibliographic form: "fre", "ger", "chi"). Users and
/// files may also use the terminological form ("fra") or the two-letter code ("fr"), so codes are compared normalised.
/// </summary>
public static class LanguageCodes
{
    /// <summary>The languages offered in the settings, most common first.</summary>
    public static readonly IReadOnlyList<LanguageOption> Common =
    [
        new("eng", "English"), new("spa", "Spanish"), new("fre", "French"), new("ger", "German"), new("ita", "Italian"),
        new("por", "Portuguese"), new("dut", "Dutch"), new("swe", "Swedish"), new("nor", "Norwegian"), new("dan", "Danish"),
        new("fin", "Finnish"), new("ice", "Icelandic"), new("pol", "Polish"), new("cze", "Czech"), new("hun", "Hungarian"),
        new("rum", "Romanian"), new("gre", "Greek"), new("tur", "Turkish"), new("rus", "Russian"), new("ukr", "Ukrainian"),
        new("heb", "Hebrew"), new("ara", "Arabic"), new("per", "Persian"), new("hin", "Hindi"), new("tha", "Thai"),
        new("vie", "Vietnamese"), new("ind", "Indonesian"), new("chi", "Chinese"), new("jpn", "Japanese"), new("kor", "Korean"),
    ];

    // Terminological and alternative codes mapped to one canonical code per language.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fra"] = "fre", ["deu"] = "ger", ["zho"] = "chi", ["nld"] = "dut", ["ell"] = "gre", ["ces"] = "cze",
        ["isl"] = "ice", ["fas"] = "per", ["ron"] = "rum", ["slk"] = "slo", ["msa"] = "may", ["mkd"] = "mac",
        ["mri"] = "mao", ["mya"] = "bur", ["sqi"] = "alb", ["hye"] = "arm", ["eus"] = "baq", ["kat"] = "geo",
        ["bod"] = "tib", ["cym"] = "wel",
        ["nob"] = "nor", ["nno"] = "nor", // Norwegian Bokmål and Nynorsk count as Norwegian
        ["en"] = "eng", ["es"] = "spa", ["fr"] = "fre", ["de"] = "ger", ["it"] = "ita", ["pt"] = "por", ["nl"] = "dut",
        ["sv"] = "swe", ["no"] = "nor", ["nb"] = "nor", ["nn"] = "nor", ["da"] = "dan", ["fi"] = "fin", ["is"] = "ice",
        ["pl"] = "pol", ["cs"] = "cze", ["hu"] = "hun", ["ro"] = "rum", ["el"] = "gre", ["tr"] = "tur", ["ru"] = "rus",
        ["uk"] = "ukr", ["he"] = "heb", ["ar"] = "ara", ["fa"] = "per", ["hi"] = "hin", ["th"] = "tha", ["vi"] = "vie",
        ["id"] = "ind", ["zh"] = "chi", ["ja"] = "jpn", ["ko"] = "kor",
    };

    // "Undetermined", "no linguistic content", "multiple" and "uncoded" say nothing about the language.
    private static readonly HashSet<string> Unknown = new(StringComparer.OrdinalIgnoreCase) { "und", "zxx", "mul", "mis" };

    /// <summary>The canonical lower-case code, or null when the language is empty or unknown.</summary>
    public static string? Normalize(string? code)
    {
        code = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || Unknown.Contains(code))
            return null;
        return Aliases.TryGetValue(code, out var canonical) ? canonical : code;
    }

    /// <summary>True when both codes name the same (known) language.</summary>
    public static bool Same(string? a, string? b) => Normalize(a) is { } x && x == Normalize(b);

    /// <summary>The English name for a code, or the code itself when it is not in the common list.</summary>
    public static string NameOf(string code) =>
        Common.FirstOrDefault(l => Same(l.Code, code))?.Name ?? code;
}
