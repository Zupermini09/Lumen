using Lumen.Core.Formatting;

namespace Lumen.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(36_125, "10:02:05")]
    public void Formats_playback_time(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Playback(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Uses_hours_when_the_reference_duration_is_an_hour_or_more() =>
        Assert.Equal("0:01:05", TimeFormat.Playback(TimeSpan.FromSeconds(65), TimeSpan.FromHours(2)));

    [Fact]
    public void Negative_time_is_clamped_to_zero() =>
        Assert.Equal("0:00", TimeFormat.Playback(TimeSpan.FromSeconds(-3)));

    [Fact]
    public void Formats_megabytes() => Assert.Equal("150 MB", TimeFormat.Megabytes(150L * 1024 * 1024 + 12));
}
