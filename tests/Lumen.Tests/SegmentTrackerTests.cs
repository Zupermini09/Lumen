using System.Text.Json;
using Lumen.Core.Playback;

namespace Lumen.Tests;

public class SegmentTrackerTests
{
    private static readonly MediaSegment Intro = new(SegmentType.Intro, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90));
    private static readonly MediaSegment Outro = new(SegmentType.Outro, TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(42));

    private static SegmentTracker Tracker(SegmentAction intro, SegmentAction outro = SegmentAction.Ask) =>
        new([Intro, Outro], t => t == SegmentType.Intro ? intro : outro);

    [Fact]
    public void Ask_shows_the_button_only_inside_the_segment()
    {
        var tracker = Tracker(SegmentAction.Ask);
        Assert.Null(tracker.Update(TimeSpan.FromSeconds(10)).ButtonFor);
        Assert.Equal(Intro, tracker.Update(TimeSpan.FromSeconds(45)).ButtonFor);
        Assert.Null(tracker.Update(TimeSpan.FromSeconds(90)).ButtonFor); // end is exclusive
        Assert.Equal("Skip Intro", Intro.SkipLabel);
        Assert.Equal("Skip Credits", Outro.SkipLabel);
    }

    [Fact]
    public void Pressing_skip_jumps_to_the_segment_end()
    {
        var tracker = Tracker(SegmentAction.Ask);
        Assert.Equal(TimeSpan.FromSeconds(90), tracker.Skip(Intro));
    }

    [Fact]
    public void Auto_skip_happens_once_even_when_seeking_back_in()
    {
        var tracker = Tracker(SegmentAction.Skip);
        Assert.Equal(TimeSpan.FromSeconds(90), tracker.Update(TimeSpan.FromSeconds(31)).SeekTo);
        Assert.Null(tracker.Update(TimeSpan.FromSeconds(40)).SeekTo);
        Assert.Null(tracker.Update(TimeSpan.FromSeconds(40)).ButtonFor);
    }

    [Fact]
    public void Off_ignores_the_segment()
    {
        var tracker = Tracker(SegmentAction.Off, SegmentAction.Off);
        Assert.Equal(default, tracker.Update(TimeSpan.FromSeconds(45)));
        Assert.Equal(default, tracker.Update(TimeSpan.FromMinutes(41)));
    }

    [Fact]
    public void Server_segments_are_parsed_sorted_and_invalid_ones_dropped()
    {
        var dto = JsonSerializer.Deserialize<Lumen.Jellyfin.MediaSegmentsResultDto>(
            """{"Items":[{"Type":"Outro","StartTicks":24000000000,"EndTicks":25200000000},{"Type":"Intro","StartTicks":300000000,"EndTicks":900000000},{"Type":"Intro","StartTicks":5,"EndTicks":5},{"Type":"Weird","StartTicks":1,"EndTicks":2}]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var segments = Lumen.Jellyfin.JellyfinClient.ParseSegments(dto);
        Assert.Equal([SegmentType.Unknown, SegmentType.Intro, SegmentType.Outro], segments.Select(s => s.Type));
        Assert.Equal(TimeSpan.FromSeconds(30), segments[1].Start);
    }

    [Fact]
    public void Default_actions_ask_for_known_types_and_ignore_unknown()
    {
        var settings = new Lumen.Core.AppSettings();
        Assert.Equal(SegmentAction.Ask, settings.ActionFor(SegmentType.Intro));
        Assert.Equal(SegmentAction.Off, settings.ActionFor(SegmentType.Unknown));
        settings.SegmentActions[SegmentType.Intro] = SegmentAction.Skip;
        Assert.Equal(SegmentAction.Skip, settings.ActionFor(SegmentType.Intro));
    }
}
