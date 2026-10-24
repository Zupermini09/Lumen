using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core;
using Lumen.Core.Playback;

namespace Lumen.UI.ViewModels;

/// <summary>
/// The settings page. Every change is applied and saved straight away; playback reads the settings when it starts,
/// so a change takes effect with the next item played.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _store;
    private readonly IThemeService _themes;

    [ObservableProperty]
    private bool _autoPlayNextEpisode;

    [ObservableProperty]
    private SettingChoice<int> _countdown;

    [ObservableProperty]
    private SettingChoice<int> _skipBack;

    [ObservableProperty]
    private SettingChoice<int> _skipForward;

    [ObservableProperty]
    private SettingChoice<string?> _audioLanguage;

    [ObservableProperty]
    private SettingChoice<string?> _subtitleLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesSubtitleLanguage))]
    private SettingChoice<SubtitleMode> _selectedSubtitleMode;

    [ObservableProperty]
    private bool _hardwareDecoding;

    [ObservableProperty]
    private SettingChoice<AppTheme> _theme;

    public SettingsViewModel(ISettingsStore store, IThemeService themes)
    {
        _store = store;
        _themes = themes;
        var settings = store.Settings;

        // The backing fields are set directly so loading the page does not "change" (and save) anything.
        SegmentRows = Enum.GetValues<SegmentType>().Where(t => t != SegmentType.Unknown)
            .OrderBy(RowOrder)
            .Select(t => new SegmentRow(t, SegmentName(t), SettingChoices.SegmentActions, settings.ActionFor(t), SetSegmentAction))
            .ToList();
        _autoPlayNextEpisode = settings.AutoPlayNextEpisode;
        CountdownChoices = SettingChoices.Seconds(settings.NextEpisodeCountdownSeconds);
        _countdown = CountdownChoices.First(c => c.Value == settings.NextEpisodeCountdownSeconds);
        SkipBackChoices = SettingChoices.Seconds(settings.SkipBackSeconds);
        _skipBack = SkipBackChoices.First(c => c.Value == settings.SkipBackSeconds);
        SkipForwardChoices = SettingChoices.Seconds(settings.SkipForwardSeconds);
        _skipForward = SkipForwardChoices.First(c => c.Value == settings.SkipForwardSeconds);

        AudioLanguageChoices = SettingChoices.Languages(settings.PreferredAudioLanguage);
        _audioLanguage = SettingChoices.LanguageFor(AudioLanguageChoices, settings.PreferredAudioLanguage);
        SubtitleLanguageChoices = SettingChoices.Languages(settings.PreferredSubtitleLanguage);
        _subtitleLanguage = SettingChoices.LanguageFor(SubtitleLanguageChoices, settings.PreferredSubtitleLanguage);
        _selectedSubtitleMode = SettingChoices.SubtitleModes.FirstOrDefault(c => c.Value == settings.SubtitleMode) ?? SettingChoices.SubtitleModes[0];

        _hardwareDecoding = settings.HardwareDecoding;
        _theme = SettingChoices.Themes.FirstOrDefault(c => c.Value == settings.Theme) ?? SettingChoices.Themes[0];
    }

    public override string Title => "Settings";

    public IReadOnlyList<SegmentRow> SegmentRows { get; }
    public IReadOnlyList<SettingChoice<int>> CountdownChoices { get; }
    public IReadOnlyList<SettingChoice<int>> SkipBackChoices { get; }
    public IReadOnlyList<SettingChoice<int>> SkipForwardChoices { get; }
    public IReadOnlyList<SettingChoice<string?>> AudioLanguageChoices { get; }
    public IReadOnlyList<SettingChoice<string?>> SubtitleLanguageChoices { get; }
    public IReadOnlyList<SettingChoice<SubtitleMode>> SubtitleModeChoices => SettingChoices.SubtitleModes;
    public IReadOnlyList<SettingChoice<AppTheme>> ThemeChoices => SettingChoices.Themes;

    /// <summary>The subtitle language only matters for the modes that pick a track.</summary>
    public bool UsesSubtitleLanguage => SelectedSubtitleMode.Value is SubtitleMode.Always or SubtitleMode.OnlyForced or SubtitleMode.WhenAudioDiffers;

    partial void OnAutoPlayNextEpisodeChanged(bool value) => Update(s => s.AutoPlayNextEpisode = value);

    partial void OnCountdownChanged(SettingChoice<int> value) => Update(s => s.NextEpisodeCountdownSeconds = value.Value);

    partial void OnSkipBackChanged(SettingChoice<int> value) => Update(s => s.SkipBackSeconds = value.Value);

    partial void OnSkipForwardChanged(SettingChoice<int> value) => Update(s => s.SkipForwardSeconds = value.Value);

    partial void OnAudioLanguageChanged(SettingChoice<string?> value) => Update(s => s.PreferredAudioLanguage = value.Value);

    partial void OnSubtitleLanguageChanged(SettingChoice<string?> value) => Update(s => s.PreferredSubtitleLanguage = value.Value);

    partial void OnSelectedSubtitleModeChanged(SettingChoice<SubtitleMode> value) => Update(s => s.SubtitleMode = value.Value);

    partial void OnHardwareDecodingChanged(bool value) => Update(s => s.HardwareDecoding = value);

    partial void OnThemeChanged(SettingChoice<AppTheme> value)
    {
        Update(s => s.Theme = value.Value);
        _themes.Apply(value.Value);
    }

    private void SetSegmentAction(SegmentType type, SegmentAction action) => Update(s => s.SegmentActions[type] = action);

    private void Update(Action<AppSettings> change)
    {
        change(_store.Settings);
        _store.Save();
    }

    private static int RowOrder(SegmentType type) => type switch
    {
        SegmentType.Intro => 0,
        SegmentType.Recap => 1,
        SegmentType.Outro => 2,
        SegmentType.Preview => 3,
        _ => 4,
    };

    private static string SegmentName(SegmentType type) => type switch
    {
        SegmentType.Intro => "Intro",
        SegmentType.Recap => "Recap",
        SegmentType.Outro => "Credits",
        SegmentType.Preview => "Preview",
        _ => "Commercial",
    };
}

/// <summary>One "skip segments" row: what to do with a segment type.</summary>
public sealed partial class SegmentRow : ObservableObject
{
    private readonly Action<SegmentType, SegmentAction> _save;

    public SegmentRow(SegmentType type, string name, IReadOnlyList<SettingChoice<SegmentAction>> choices,
        SegmentAction current, Action<SegmentType, SegmentAction> save)
    {
        Type = type;
        Name = name;
        Choices = choices;
        _selected = choices.First(c => c.Value == current);
        _save = save;
    }

    public SegmentType Type { get; }
    public string Name { get; }
    public IReadOnlyList<SettingChoice<SegmentAction>> Choices { get; }

    [ObservableProperty]
    private SettingChoice<SegmentAction> _selected;

    partial void OnSelectedChanged(SettingChoice<SegmentAction> value) => _save(Type, value.Value);
}
