using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

/// <summary>One person in the cast list.</summary>
public sealed class PersonCardViewModel
{
    public const double ImageSize = 96;

    private PersonCardViewModel(PersonInfo person) => Person = person;

    public PersonInfo Person { get; }
    public string Name => Person.Name;
    public string? Role => Person.Role;
    public Uri? ImageUri { get; private init; }

    /// <summary>Shown while the picture loads, or when the person has none.</summary>
    public string Initials { get; private init; } = "";

    public static PersonCardViewModel Create(PersonInfo person, IJellyfinClient client) => new(person)
    {
        ImageUri = person.PrimaryImageTag is { } tag ? client.GetImageUri(person.Id, ImageKind.Primary, tag, (int)ImageSize * 2) : null,
        Initials = InitialsOf(person.Name),
    };

    internal static string InitialsOf(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "",
            1 => words[0][..1].ToUpperInvariant(),
            _ => (words[0][..1] + words[^1][..1]).ToUpperInvariant(),
        };
    }
}

/// <summary>One episode in a series page's list: thumbnail, number, title, overview, watched state.</summary>
public sealed partial class EpisodeRowViewModel
{
    public const double ImageWidth = 176;
    public const double ImageHeight = 99;

    private readonly Action<MediaItem> _open;
    private readonly Action<MediaItem> _play;

    private EpisodeRowViewModel(MediaItem item, Action<MediaItem> open, Action<MediaItem> play)
    {
        Item = item;
        _open = open;
        _play = play;
    }

    public MediaItem Item { get; }
    public string Title => Item.IndexNumber is { } number ? $"{number}. {Item.Name}" : Item.Name;
    public string? Overview => Item.Overview;
    public string? Runtime => Item.RunTime is { } runTime ? TimeFormat.Duration(runTime) : null;
    public Uri? ImageUri { get; private init; }
    public bool IsPlayed => Item.Played;

    /// <summary>Watched fraction 0..1 for partly watched episodes.</summary>
    public double Progress { get; private init; }
    public bool HasProgress => Progress > 0;

    public static EpisodeRowViewModel Create(MediaItem item, IJellyfinClient client, Action<MediaItem> open, Action<MediaItem> play) => new(item, open, play)
    {
        ImageUri = item.PrimaryImageTag is { } tag ? client.GetImageUri(item.Id, ImageKind.Primary, tag, (int)ImageHeight * 2) : null,
        Progress = item.RunTime is { Ticks: > 0 } runTime && item.ResumePosition > TimeSpan.Zero ? Math.Clamp(item.ResumePosition / runTime, 0, 1) : 0,
    };

    [RelayCommand]
    private void Open() => _open(Item);

    [RelayCommand]
    private void Play() => _play(Item);
}

/// <summary>A labelled line in the "media info" block of a details page.</summary>
public sealed record StreamRow(string Label, string Text);

public static class StreamSummary
{
    private const int MaxSubtitles = 12;

    /// <summary>Video, audio and subtitle tracks as "Video / Audio / Subtitles" rows; kinds the file lacks are left out.</summary>
    public static IReadOnlyList<StreamRow> Build(IEnumerable<MediaStreamInfo> streams)
    {
        var all = streams.OrderBy(s => s.Index).ToList();
        var rows = new List<StreamRow>();

        var video = Titles(all, "Video");
        if (video.Count > 0)
            rows.Add(new StreamRow("Video", string.Join("\n", video)));

        var audio = Titles(all, "Audio");
        if (audio.Count > 0)
            rows.Add(new StreamRow("Audio", string.Join("\n", audio)));

        var subtitles = Titles(all, "Subtitle");
        if (subtitles.Count > 0)
        {
            var shown = string.Join(" · ", subtitles.Take(MaxSubtitles));
            rows.Add(new StreamRow("Subtitles", subtitles.Count > MaxSubtitles ? $"{shown} · +{subtitles.Count - MaxSubtitles} more" : shown));
        }

        return rows;
    }

    private static List<string> Titles(List<MediaStreamInfo> streams, string type) =>
        streams.Where(s => s.Type == type).Select(s => s.DisplayTitle).Where(t => t.Length > 0).Distinct().ToList();
}
