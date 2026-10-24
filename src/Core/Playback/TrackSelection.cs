using Lumen.Core.Jellyfin;

namespace Lumen.Core.Playback;

/// <summary>When subtitles are shown by default.</summary>
public enum SubtitleMode
{
    /// <summary>Whatever the server picks from the user's server-side preferences.</summary>
    ServerDefault,
    /// <summary>Always show a subtitle track (in the preferred language when there is one).</summary>
    Always,
    /// <summary>Only tracks flagged as forced (translations of foreign-language dialogue).</summary>
    OnlyForced,
    Off,
    /// <summary>Only when the audio is not in the preferred language.</summary>
    WhenAudioDiffers,
}

/// <param name="AudioLanguage">Preferred audio language (ISO 639-2), or null to follow the server.</param>
/// <param name="SubtitleLanguage">Preferred subtitle language (ISO 639-2), or null.</param>
public sealed record TrackPreferences(string? AudioLanguage, string? SubtitleLanguage, SubtitleMode SubtitleMode);

/// <summary>The streams to start with, as server stream indexes; null audio keeps the player's choice, null subtitles means off.</summary>
public readonly record struct TrackChoice(int? AudioStreamIndex, int? SubtitleStreamIndex);

/// <summary>
/// Picks the audio and subtitle stream to start with from the user's preferences. The server's own defaults
/// (<c>DefaultAudioStreamIndex</c> / <c>DefaultSubtitleStreamIndex</c>) are the fallback whenever a preference does not
/// apply or the file has no matching stream. Pure logic so it can be unit tested.
/// </summary>
public static class TrackSelector
{
    public static TrackChoice Select(
        IReadOnlyList<MediaStreamInfo> streams, int? serverAudio, int? serverSubtitle, TrackPreferences preferences)
    {
        var audioStreams = streams.Where(s => s.Type == "Audio").OrderBy(s => s.Index).ToList();
        var subtitles = streams.Where(s => s.Type == "Subtitle").OrderBy(s => s.Index).ToList();

        var audio = ChooseAudio(audioStreams, serverAudio, preferences.AudioLanguage);
        var audioIndex = audio?.Index ?? serverAudio;

        // The audio the file will actually play with decides the language-dependent subtitle modes.
        var playing = audio ?? audioStreams.FirstOrDefault(s => s.Index == serverAudio) ?? audioStreams.FirstOrDefault(s => s.IsDefault);
        var subtitle = ChooseSubtitle(subtitles, serverSubtitle, playing, preferences);
        return new TrackChoice(audioIndex, subtitle);
    }

    private static MediaStreamInfo? ChooseAudio(List<MediaStreamInfo> audio, int? serverDefault, string? language)
    {
        if (language is null)
            return null;
        var matches = audio.Where(s => LanguageCodes.Same(s.Language, language)).ToList();
        return matches.FirstOrDefault(s => s.Index == serverDefault) ?? matches.FirstOrDefault(s => s.IsDefault) ?? matches.FirstOrDefault();
    }

    private static int? ChooseSubtitle(
        List<MediaStreamInfo> subtitles, int? serverDefault, MediaStreamInfo? playingAudio, TrackPreferences preferences)
    {
        var language = preferences.SubtitleLanguage;
        switch (preferences.SubtitleMode)
        {
            case SubtitleMode.Off:
                return null;

            case SubtitleMode.Always:
            {
                // With a language, only that language qualifies (else the server's choice); without, any track does.
                var candidates = language is null ? subtitles : InLanguage(subtitles, language);
                return Best(candidates, serverDefault)?.Index ?? serverDefault;
            }

            case SubtitleMode.OnlyForced:
            {
                var forced = subtitles.Where(s => s.IsForced).ToList();
                var audioLanguage = LanguageCodes.Normalize(playingAudio?.Language);
                // Forced tracks translate foreign speech into the viewer's language: prefer the subtitle language, then
                // the language of the audio being played.
                if (language is not null && InLanguage(forced, language) is { Count: > 0 } preferred)
                    return Best(preferred, serverDefault)?.Index;
                if (audioLanguage is not null && InLanguage(forced, audioLanguage) is { Count: > 0 } sameAsAudio)
                    return Best(sameAsAudio, serverDefault)?.Index;
                // No language to go by: any forced track is the best guess. Otherwise none of them fits.
                return language is null && audioLanguage is null ? Best(forced, serverDefault)?.Index : null;
            }

            case SubtitleMode.WhenAudioDiffers:
            {
                // The preferred language is the subtitle language, or else the audio language.
                var preferred = language ?? preferences.AudioLanguage;
                var playingLanguage = LanguageCodes.Normalize(playingAudio?.Language);
                if (preferred is null || playingLanguage is null)
                    return serverDefault; // cannot tell whether the audio differs
                if (LanguageCodes.Same(playingLanguage, preferred))
                    return null;
                return Best(InLanguage(subtitles, preferred), serverDefault)?.Index;
            }

            default:
                return serverDefault;
        }
    }

    private static List<MediaStreamInfo> InLanguage(IEnumerable<MediaStreamInfo> streams, string language) =>
        streams.Where(s => LanguageCodes.Same(s.Language, language)).ToList();

    /// <summary>Full tracks before forced ones, then the server's choice, then tracks flagged default, then file order.</summary>
    private static MediaStreamInfo? Best(IEnumerable<MediaStreamInfo> candidates, int? serverDefault) =>
        candidates
            .OrderBy(s => s.IsForced)
            .ThenByDescending(s => s.Index == serverDefault)
            .ThenByDescending(s => s.IsDefault)
            .ThenBy(s => s.Index)
            .FirstOrDefault();
}
