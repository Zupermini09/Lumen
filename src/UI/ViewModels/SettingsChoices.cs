using Lumen.Core;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>An entry of a settings list (what the combo box shows) with the value it stands for.</summary>
public sealed record SettingChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The lists offered on the settings page.</summary>
public static class SettingChoices
{
    public static readonly IReadOnlyList<SettingChoice<SegmentAction>> SegmentActions =
    [
        new(SegmentAction.Ask, "Show skip button"),
        new(SegmentAction.Skip, "Skip automatically"),
        new(SegmentAction.Off, "Off"),
    ];

    public static readonly IReadOnlyList<SettingChoice<SubtitleMode>> SubtitleModes =
    [
        new(SubtitleMode.ServerDefault, "Server default"),
        new(SubtitleMode.Always, "Always on"),
        new(SubtitleMode.OnlyForced, "Only forced"),
        new(SubtitleMode.Off, "Off"),
        new(SubtitleMode.WhenAudioDiffers, "Only when the audio is in another language"),
    ];

    public static readonly IReadOnlyList<SettingChoice<AppTheme>> Themes =
    [
        new(AppTheme.Dark, "Dark"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.System, "Use system setting"),
    ];

    private static readonly int[] Steps = [5, 10, 15, 30];

    /// <summary>5 / 10 / 15 / 30 seconds, plus the current value when a settings file holds another one.</summary>
    public static IReadOnlyList<SettingChoice<int>> Seconds(int current) =>
        Steps.Append(current).Where(s => s > 0).Distinct().Order().Select(s => new SettingChoice<int>(s, $"{s} seconds")).ToList();

    /// <summary>"Server default", the common languages, and the saved code when it is not one of them.</summary>
    public static IReadOnlyList<SettingChoice<string?>> Languages(string? current)
    {
        var choices = new List<SettingChoice<string?>> { new(null, "Server default") };
        choices.AddRange(LanguageCodes.Common.Select(l => new SettingChoice<string?>(l.Code, l.Name)));
        if (current is not null && !choices.Any(c => LanguageCodes.Same(c.Value, current)))
            choices.Add(new(current, current));
        return choices;
    }

    /// <summary>The entry for a stored value; a stored code that differs only in form (fra / fre) selects the listed one.</summary>
    public static SettingChoice<string?> LanguageFor(IReadOnlyList<SettingChoice<string?>> choices, string? code) =>
        choices.FirstOrDefault(c => c.Value == code) ?? choices.FirstOrDefault(c => LanguageCodes.Same(c.Value, code)) ?? choices[0];
}
