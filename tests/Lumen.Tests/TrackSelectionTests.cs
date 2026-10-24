using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;

namespace Lumen.Tests;

public class LanguageCodesTests
{
    [Theory]
    [InlineData("eng", "eng", true)]
    [InlineData("ENG", "eng", true)]
    [InlineData("fre", "fra", true)]   // bibliographic vs terminological
    [InlineData("ger", "de", true)]    // two-letter form
    [InlineData("nob", "nor", true)]   // Norwegian Bokmål is Norwegian
    [InlineData("eng", "jpn", false)]
    [InlineData("und", "und", false)]  // unknown languages never match
    [InlineData(null, "eng", false)]
    [InlineData("", "", false)]
    public void Languages_are_compared_by_normalised_code(string? a, string? b, bool same) =>
        Assert.Equal(same, LanguageCodes.Same(a, b));

    [Fact]
    public void Names_come_from_the_common_list_with_the_code_as_fallback()
    {
        Assert.Equal("French", LanguageCodes.NameOf("fra"));
        Assert.Equal("xyz", LanguageCodes.NameOf("xyz"));
    }
}

public class TrackSelectorTests
{
    private static MediaStreamInfo Audio(int index, string? language, bool isDefault = false) =>
        new(index, "Audio", "aac", language, $"Audio {index}", isDefault, false, false);

    private static MediaStreamInfo Sub(int index, string? language, bool forced = false, bool isDefault = false, bool external = false) =>
        new(index, "Subtitle", "subrip", language, $"Sub {index}", isDefault, forced, external);

    private static MediaStreamInfo Video(int index) => new(index, "Video", "h264", null, "Video", true, false, false);

    private static TrackChoice Select(IReadOnlyList<MediaStreamInfo> streams, int? serverAudio, int? serverSubtitle,
        SubtitleMode mode = SubtitleMode.ServerDefault, string? audio = null, string? subtitles = null) =>
        TrackSelector.Select(streams, serverAudio, serverSubtitle, new TrackPreferences(audio, subtitles, mode));

    private static readonly MediaStreamInfo[] Anime =
    [
        Video(0), Audio(1, "jpn", isDefault: true), Audio(2, "eng"),
        Sub(3, "eng"), Sub(4, "eng", forced: true), Sub(5, "nor"), Sub(6, "jpn"),
    ];

    [Fact]
    public void Without_preferences_the_server_defaults_are_used()
    {
        Assert.Equal(new TrackChoice(1, 3), Select(Anime, 1, 3));
        Assert.Equal(new TrackChoice(1, null), Select(Anime, 1, null)); // the server chose no subtitles
        Assert.Equal(new TrackChoice(null, null), Select(Anime, null, null));
    }

    [Fact]
    public void Preferred_audio_language_wins_over_the_server_default()
    {
        Assert.Equal(2, Select(Anime, 1, null, audio: "eng").AudioStreamIndex);
        Assert.Equal(2, Select(Anime, 1, null, audio: "en").AudioStreamIndex); // two-letter code
    }

    [Fact]
    public void Preferred_audio_language_falls_back_to_the_server_default_when_the_file_has_none()
    {
        Assert.Equal(1, Select(Anime, 1, null, audio: "fra").AudioStreamIndex);
        Assert.Null(Select(Anime, null, null, audio: "fra").AudioStreamIndex);
    }

    [Fact]
    public void Several_tracks_in_the_preferred_audio_language_prefer_the_server_default_then_the_flagged_default()
    {
        var streams = new[] { Audio(1, "eng"), Audio(2, "eng", isDefault: true), Audio(3, "eng") };
        Assert.Equal(3, Select(streams, 3, null, audio: "eng").AudioStreamIndex);
        Assert.Equal(2, Select(streams, null, null, audio: "eng").AudioStreamIndex);
    }

    [Fact]
    public void Always_shows_a_full_track_in_the_preferred_language()
    {
        // Track 4 is a forced English track; the full one (3) is better for "always on".
        Assert.Equal(3, Select(Anime, 1, null, SubtitleMode.Always, subtitles: "eng").SubtitleStreamIndex);
        Assert.Equal(5, Select(Anime, 1, null, SubtitleMode.Always, subtitles: "nor").SubtitleStreamIndex);
    }

    [Fact]
    public void Always_uses_a_forced_track_only_when_nothing_else_is_in_the_language()
    {
        var streams = new[] { Audio(1, "jpn"), Sub(2, "eng", forced: true) };
        Assert.Equal(2, Select(streams, 1, null, SubtitleMode.Always, subtitles: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void Always_falls_back_to_the_server_default_when_the_language_is_missing()
    {
        Assert.Equal(6, Select(Anime, 1, 6, SubtitleMode.Always, subtitles: "fra").SubtitleStreamIndex);
        Assert.Null(Select(Anime, 1, null, SubtitleMode.Always, subtitles: "fra").SubtitleStreamIndex);
    }

    [Fact]
    public void Always_without_a_language_still_shows_something()
    {
        // The server default (6) wins when there is one, otherwise the first full track.
        Assert.Equal(6, Select(Anime, 1, 6, SubtitleMode.Always).SubtitleStreamIndex);
        Assert.Equal(3, Select(Anime, 1, null, SubtitleMode.Always).SubtitleStreamIndex);
        Assert.Null(Select([Video(0), Audio(1, "eng")], 1, null, SubtitleMode.Always).SubtitleStreamIndex); // no tracks at all
    }

    [Fact]
    public void Off_never_shows_subtitles_even_when_the_server_default_does()
    {
        Assert.Null(Select(Anime, 1, 3, SubtitleMode.Off, subtitles: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void Only_forced_picks_the_forced_track_in_the_subtitle_language()
    {
        Assert.Equal(4, Select(Anime, 2, 3, SubtitleMode.OnlyForced, subtitles: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void Only_forced_uses_the_audio_language_when_no_subtitle_language_matches()
    {
        // English audio plays (preference), the forced track is English; the subtitle language (Norwegian) has none.
        Assert.Equal(4, Select(Anime, 1, null, SubtitleMode.OnlyForced, audio: "eng", subtitles: "nor").SubtitleStreamIndex);
    }

    [Fact]
    public void Only_forced_shows_nothing_when_no_forced_track_fits()
    {
        Assert.Null(Select(Anime, 1, 3, SubtitleMode.OnlyForced, subtitles: "nor").SubtitleStreamIndex);
        Assert.Null(Select([Audio(1, "eng"), Sub(2, "eng")], 1, 2, SubtitleMode.OnlyForced).SubtitleStreamIndex); // none forced
    }

    [Fact]
    public void Only_forced_without_any_language_to_go_by_takes_the_first_forced_track()
    {
        var streams = new[] { Audio(1, null), Sub(2, "eng"), Sub(3, "fre", forced: true) };
        Assert.Equal(3, Select(streams, 1, null, SubtitleMode.OnlyForced).SubtitleStreamIndex);
    }

    [Fact]
    public void When_audio_differs_subtitles_show_only_for_other_audio_languages()
    {
        // Japanese audio (server default) with English preferred: show English subtitles.
        Assert.Equal(3, Select(Anime, 1, null, SubtitleMode.WhenAudioDiffers, subtitles: "eng").SubtitleStreamIndex);
        // The English audio track is chosen by the audio preference: no subtitles needed.
        Assert.Null(Select(Anime, 1, 3, SubtitleMode.WhenAudioDiffers, audio: "eng", subtitles: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void When_audio_differs_uses_the_preferred_audio_language_if_no_subtitle_language_is_set()
    {
        Assert.Null(Select(Anime, 2, 3, SubtitleMode.WhenAudioDiffers, audio: "eng").SubtitleStreamIndex);
        // Japanese audio only: the preferred (English) language differs, so English subtitles show.
        var streams = new[] { Audio(1, "jpn"), Sub(2, "eng") };
        Assert.Equal(2, Select(streams, 1, null, SubtitleMode.WhenAudioDiffers, audio: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void When_audio_differs_shows_nothing_if_there_is_no_track_in_the_language()
    {
        Assert.Null(Select(Anime, 1, 6, SubtitleMode.WhenAudioDiffers, subtitles: "fra").SubtitleStreamIndex);
    }

    [Fact]
    public void When_audio_differs_cannot_decide_without_a_language_and_follows_the_server()
    {
        Assert.Equal(3, Select(Anime, 1, 3, SubtitleMode.WhenAudioDiffers).SubtitleStreamIndex); // no preferred language
        var unknownAudio = new[] { Audio(1, "und"), Sub(2, "eng") };
        Assert.Equal(2, Select(unknownAudio, 1, 2, SubtitleMode.WhenAudioDiffers, subtitles: "eng").SubtitleStreamIndex);
    }

    [Fact]
    public void External_subtitle_files_take_part_like_embedded_ones()
    {
        var streams = new[] { Audio(1, "jpn"), Sub(2, "nor", external: true), Sub(3, "eng", external: true) };
        Assert.Equal(3, Select(streams, 1, null, SubtitleMode.Always, subtitles: "eng").SubtitleStreamIndex);
    }
}
