using Avalonia;
using Lumen.Core.Playback;
using Lumen.UI.Views;

namespace Lumen.Tests;

public class PlayerInteractionTests
{
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(500);
    private static readonly Size DoubleClickSize = new(4, 4);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static ClickKind Click(ClickTracker tracker, double ms, double x = 100, double y = 100) =>
        tracker.Register(Ms(ms), new Point(x, y), DoubleClickTime, DoubleClickSize);

    [Fact]
    public void A_single_click_is_single()
    {
        Assert.Equal(ClickKind.Single, Click(new ClickTracker(), 1000));
    }

    [Fact]
    public void Two_quick_clicks_in_the_same_place_are_a_double_click()
    {
        var tracker = new ClickTracker();
        Click(tracker, 1000);
        Assert.Equal(ClickKind.Double, Click(tracker, 1200));
    }

    [Fact]
    public void A_slow_second_click_is_just_another_click()
    {
        var tracker = new ClickTracker();
        Click(tracker, 1000);
        Assert.Equal(ClickKind.Single, Click(tracker, 1800));
        // ...and it starts a new pair: a quick third click is then the double click.
        Assert.Equal(ClickKind.Double, Click(tracker, 1900));
    }

    [Fact]
    public void The_double_click_time_is_the_systems()
    {
        var tracker = new ClickTracker();
        tracker.Register(Ms(0), new Point(10, 10), Ms(200), DoubleClickSize);
        Assert.Equal(ClickKind.Single, tracker.Register(Ms(300), new Point(10, 10), Ms(200), DoubleClickSize));
        tracker.Register(Ms(1000), new Point(10, 10), Ms(400), DoubleClickSize);
        Assert.Equal(ClickKind.Double, tracker.Register(Ms(1300), new Point(10, 10), Ms(400), DoubleClickSize));
    }

    [Fact]
    public void A_second_click_far_away_is_not_a_double_click()
    {
        var tracker = new ClickTracker();
        Click(tracker, 1000, 100, 100);
        Assert.Equal(ClickKind.Single, Click(tracker, 1100, 140, 100));
    }

    [Fact]
    public void The_distance_is_half_the_system_size_either_way()
    {
        var tracker = new ClickTracker();
        Click(tracker, 0, 100, 100);
        Assert.Equal(ClickKind.Double, Click(tracker, 100, 102, 98));
        Click(tracker, 1000, 100, 100);
        Assert.Equal(ClickKind.Single, Click(tracker, 1100, 103, 100));
    }

    [Fact]
    public void A_double_click_uses_up_its_clicks()
    {
        var tracker = new ClickTracker();
        Click(tracker, 0);
        Assert.Equal(ClickKind.Double, Click(tracker, 100));
        Assert.Equal(ClickKind.Single, Click(tracker, 200));
    }

    [Fact]
    public void Reset_forgets_the_last_click()
    {
        var tracker = new ClickTracker();
        Click(tracker, 0);
        tracker.Reset();
        Assert.Equal(ClickKind.Single, Click(tracker, 100));
    }

    [Theory]
    [InlineData(0, 350)]
    [InlineData(-5, 350)]
    [InlineData(50, 150)]
    [InlineData(500, 500)]
    [InlineData(5000, 600)]
    public void Platform_double_click_times_are_kept_in_sane_limits(double ms, double expected) =>
        Assert.Equal(Ms(expected), ClickTracker.Clamp(Ms(ms)));

    [Fact]
    public void Controls_hide_only_after_the_idle_timeout_while_playing()
    {
        Assert.False(ChromePolicy.ShouldHide(PlaybackState.Playing, Ms(2900), false, false, false, false));
        Assert.True(ChromePolicy.ShouldHide(PlaybackState.Playing, Ms(3000), false, false, false, false));
    }

    [Theory]
    [InlineData(PlaybackState.Paused)]
    [InlineData(PlaybackState.Buffering)]
    [InlineData(PlaybackState.Opening)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Ended)]
    [InlineData(PlaybackState.Error)]
    public void Controls_stay_up_unless_playing(PlaybackState state) =>
        Assert.False(ChromePolicy.ShouldHide(state, Ms(60_000), false, false, false, false));

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Controls_stay_up_under_the_mouse_with_a_menu_open_a_skip_button_or_a_countdown(
        bool pointerOver, bool menuOpen, bool hasSkip, bool countingDown) =>
        Assert.False(ChromePolicy.ShouldHide(PlaybackState.Playing, Ms(60_000), pointerOver, menuOpen, hasSkip, countingDown));
}
