using Lumen.Core;
using Lumen.Core.Playback;
using Lumen.Platform;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lumen-tests").FullName;
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_keep_the_behaviour_before_the_settings_page_existed()
    {
        var s = new AppSettings();
        Assert.Equal(10, s.NextEpisodeCountdownSeconds);
        Assert.Equal(15, s.SkipBackSeconds);
        Assert.Equal(15, s.SkipForwardSeconds);
        Assert.True(s.AutoPlayNextEpisode);
        Assert.True(s.HardwareDecoding);
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.Null(s.PreferredAudioLanguage);
        Assert.Null(s.PreferredSubtitleLanguage);
        Assert.Equal(SubtitleMode.ServerDefault, s.SubtitleMode);
        Assert.Equal(SegmentAction.Ask, s.ActionFor(SegmentType.Intro));
        Assert.Equal(SegmentAction.Off, s.ActionFor(SegmentType.Unknown));
    }

    [Fact]
    public void All_playback_settings_round_trip_through_the_json_store()
    {
        var first = new JsonSettingsStore(_dir, Log);
        var s = first.Settings;
        s.NextEpisodeCountdownSeconds = 30;
        s.SkipBackSeconds = 5;
        s.SkipForwardSeconds = 30;
        s.AutoPlayNextEpisode = false;
        s.PreferredAudioLanguage = "jpn";
        s.PreferredSubtitleLanguage = "nor";
        s.SubtitleMode = SubtitleMode.WhenAudioDiffers;
        s.HardwareDecoding = false;
        s.Theme = AppTheme.System;
        s.SegmentActions[SegmentType.Intro] = SegmentAction.Skip;
        s.SegmentActions[SegmentType.Outro] = SegmentAction.Off;
        first.Save();

        var loaded = new JsonSettingsStore(_dir, Log).Settings;
        Assert.Equal(30, loaded.NextEpisodeCountdownSeconds);
        Assert.Equal(5, loaded.SkipBackSeconds);
        Assert.Equal(30, loaded.SkipForwardSeconds);
        Assert.False(loaded.AutoPlayNextEpisode);
        Assert.Equal("jpn", loaded.PreferredAudioLanguage);
        Assert.Equal("nor", loaded.PreferredSubtitleLanguage);
        Assert.Equal(SubtitleMode.WhenAudioDiffers, loaded.SubtitleMode);
        Assert.False(loaded.HardwareDecoding);
        Assert.Equal(AppTheme.System, loaded.Theme);
        Assert.Equal(SegmentAction.Skip, loaded.ActionFor(SegmentType.Intro));
        Assert.Equal(SegmentAction.Off, loaded.ActionFor(SegmentType.Outro));
        Assert.Equal(SegmentAction.Ask, loaded.ActionFor(SegmentType.Recap));
    }

    [Fact]
    public void A_settings_file_from_before_the_new_fields_loads_with_defaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "DeviceId": "abc", "LastServerId": "srv", "Servers": [], "Volume": 40, "AutoPlayNextEpisode": false, "SegmentActions": { "Intro": 1 } }""");

        var s = new JsonSettingsStore(_dir, Log).Settings;
        Assert.Equal("abc", s.DeviceId);
        Assert.Equal(40, s.Volume);
        Assert.False(s.AutoPlayNextEpisode);
        Assert.Equal(SegmentAction.Skip, s.ActionFor(SegmentType.Intro));
        Assert.Equal(10, s.NextEpisodeCountdownSeconds);
        Assert.Equal(15, s.SkipBackSeconds);
        Assert.Equal(15, s.SkipForwardSeconds);
        Assert.True(s.HardwareDecoding);
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.Equal(SubtitleMode.ServerDefault, s.SubtitleMode);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json.corrupt")));
    }

    [Fact]
    public void Track_preferences_are_taken_from_the_settings()
    {
        var s = new AppSettings { PreferredAudioLanguage = "eng", PreferredSubtitleLanguage = "nor", SubtitleMode = SubtitleMode.Always };
        Assert.Equal(new TrackPreferences("eng", "nor", SubtitleMode.Always), s.GetTrackPreferences());
    }
}

public class SettingsViewModelTests
{
    private sealed class RecordingThemes : IThemeService
    {
        public List<AppTheme> Applied { get; } = [];
        public void Apply(AppTheme theme) => Applied.Add(theme);
    }

    private static (SettingsViewModel Page, MemorySettings Store, RecordingThemes Themes) Create(Action<AppSettings>? configure = null)
    {
        var store = new MemorySettings();
        configure?.Invoke(store.Settings);
        var themes = new RecordingThemes();
        return (new SettingsViewModel(store, themes), store, themes);
    }

    [Fact]
    public void The_page_shows_the_current_settings_without_saving_anything()
    {
        var (page, store, themes) = Create(s =>
        {
            s.SkipBackSeconds = 5;
            s.PreferredSubtitleLanguage = "nor";
            s.SubtitleMode = SubtitleMode.Always;
            s.SegmentActions[SegmentType.Recap] = SegmentAction.Skip;
        });

        Assert.Equal(5, page.SkipBack.Value);
        Assert.Equal(15, page.SkipForward.Value);
        Assert.Equal("nor", page.SubtitleLanguage.Value);
        Assert.Equal(SubtitleMode.Always, page.SelectedSubtitleMode.Value);
        Assert.Equal(SegmentAction.Skip, page.SegmentRows.Single(r => r.Type == SegmentType.Recap).Selected.Value);
        Assert.Equal(SegmentAction.Ask, page.SegmentRows.Single(r => r.Type == SegmentType.Intro).Selected.Value);
        Assert.Equal(0, store.Saves);
        Assert.Empty(themes.Applied);
    }

    [Fact]
    public void The_segment_rows_cover_every_skippable_type_in_playback_order()
    {
        var (page, _, _) = Create();
        Assert.Equal([SegmentType.Intro, SegmentType.Recap, SegmentType.Outro, SegmentType.Preview, SegmentType.Commercial],
            page.SegmentRows.Select(r => r.Type));
        Assert.Equal(["Show skip button", "Skip automatically", "Off"], page.SegmentRows[0].Choices.Select(c => c.Label));
    }

    [Fact]
    public void Changes_are_applied_to_the_settings_and_saved_immediately()
    {
        var (page, store, _) = Create();

        page.SegmentRows.Single(r => r.Type == SegmentType.Intro).Selected = SettingChoices.SegmentActions[1];
        Assert.Equal(SegmentAction.Skip, store.Settings.ActionFor(SegmentType.Intro));
        Assert.Equal(1, store.Saves);

        page.AutoPlayNextEpisode = false;
        page.Countdown = page.CountdownChoices.Single(c => c.Value == 30);
        page.SkipBack = page.SkipBackChoices.Single(c => c.Value == 5);
        page.SkipForward = page.SkipForwardChoices.Single(c => c.Value == 10);
        page.HardwareDecoding = false;
        page.AudioLanguage = page.AudioLanguageChoices.Single(c => c.Value == "jpn");
        page.SelectedSubtitleMode = SettingChoices.SubtitleModes.Single(c => c.Value == SubtitleMode.OnlyForced);
        page.SubtitleLanguage = page.SubtitleLanguageChoices.Single(c => c.Value == "eng");

        var s = store.Settings;
        Assert.False(s.AutoPlayNextEpisode);
        Assert.Equal(30, s.NextEpisodeCountdownSeconds);
        Assert.Equal(5, s.SkipBackSeconds);
        Assert.Equal(10, s.SkipForwardSeconds);
        Assert.False(s.HardwareDecoding);
        Assert.Equal("jpn", s.PreferredAudioLanguage);
        Assert.Equal(SubtitleMode.OnlyForced, s.SubtitleMode);
        Assert.Equal("eng", s.PreferredSubtitleLanguage);
        Assert.Equal(9, store.Saves);
    }

    [Fact]
    public void Choosing_server_default_clears_a_language()
    {
        var (page, store, _) = Create(s => s.PreferredAudioLanguage = "jpn");
        page.AudioLanguage = page.AudioLanguageChoices[0];
        Assert.Null(store.Settings.PreferredAudioLanguage);
    }

    [Fact]
    public void Changing_the_theme_applies_it_at_once()
    {
        var (page, store, themes) = Create();
        page.Theme = SettingChoices.Themes.Single(c => c.Value == AppTheme.Light);
        page.Theme = SettingChoices.Themes.Single(c => c.Value == AppTheme.System);
        Assert.Equal([AppTheme.Light, AppTheme.System], themes.Applied);
        Assert.Equal(AppTheme.System, store.Settings.Theme);
    }

    [Fact]
    public void The_subtitle_language_is_only_enabled_for_modes_that_use_it()
    {
        var (page, _, _) = Create();
        Assert.False(page.UsesSubtitleLanguage); // server default
        page.SelectedSubtitleMode = SettingChoices.SubtitleModes.Single(c => c.Value == SubtitleMode.Always);
        Assert.True(page.UsesSubtitleLanguage);
        page.SelectedSubtitleMode = SettingChoices.SubtitleModes.Single(c => c.Value == SubtitleMode.Off);
        Assert.False(page.UsesSubtitleLanguage);
    }

    [Fact]
    public void Values_outside_the_lists_are_kept_and_shown()
    {
        var (page, _, _) = Create(s =>
        {
            s.SkipForwardSeconds = 20;
            s.PreferredAudioLanguage = "swa"; // not in the common list
        });
        Assert.Equal(20, page.SkipForward.Value);
        Assert.Equal([5, 10, 15, 20, 30], page.SkipForwardChoices.Select(c => c.Value));
        Assert.Equal("swa", page.AudioLanguage.Value);
    }

    [Fact]
    public void A_language_in_another_form_selects_the_listed_entry()
    {
        var (page, _, _) = Create(s => s.PreferredSubtitleLanguage = "fra");
        Assert.Equal("fre", page.SubtitleLanguage.Value);
        Assert.Equal("French", page.SubtitleLanguage.Label);
    }
}
