namespace Lumen.Core.Playback;

/// <summary>Jellyfin media segment types (from server-side providers such as the Intro Skipper plugin).</summary>
public enum SegmentType
{
    Unknown,
    Commercial,
    Preview,
    Recap,
    Outro,
    Intro,
}

public enum SegmentAction
{
    /// <summary>Show a "Skip ..." button while inside the segment.</summary>
    Ask,
    /// <summary>Jump over the segment once when playback enters it.</summary>
    Skip,
    /// <summary>Do nothing.</summary>
    Off,
}

public sealed record MediaSegment(SegmentType Type, TimeSpan Start, TimeSpan End)
{
    public bool Contains(TimeSpan position) => position >= Start && position < End;

    public string SkipLabel => Type switch
    {
        SegmentType.Intro => "Skip Intro",
        SegmentType.Recap => "Skip Recap",
        SegmentType.Outro => "Skip Credits",
        SegmentType.Preview => "Skip Preview",
        SegmentType.Commercial => "Skip Commercial",
        _ => "Skip",
    };
}

/// <summary>What the player should do at the current position.</summary>
public readonly record struct SegmentDecision(MediaSegment? ButtonFor, TimeSpan? SeekTo);

/// <summary>
/// Decides, from the playback position, whether to show a skip button or skip automatically. Pure logic so it can be
/// unit tested; the player feeds it position updates.
/// </summary>
public sealed class SegmentTracker(IReadOnlyList<MediaSegment> segments, Func<SegmentType, SegmentAction> actionFor)
{
    // Segments already auto-skipped (or dismissed): seeking back into one must not skip again.
    private readonly HashSet<MediaSegment> _handled = [];

    public bool HasSegments => segments.Count > 0;

    public SegmentDecision Update(TimeSpan position)
    {
        var segment = segments.FirstOrDefault(s => s.Contains(position) && actionFor(s.Type) != SegmentAction.Off);
        if (segment is null)
            return default;

        if (actionFor(segment.Type) == SegmentAction.Skip && _handled.Add(segment))
            return new SegmentDecision(null, segment.End);

        return actionFor(segment.Type) == SegmentAction.Ask ? new SegmentDecision(segment, null) : default;
    }

    /// <summary>The user pressed the skip button.</summary>
    public TimeSpan Skip(MediaSegment segment)
    {
        _handled.Add(segment);
        return segment.End;
    }
}
