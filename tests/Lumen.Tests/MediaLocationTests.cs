using Lumen.Core.Playback;

namespace Lumen.Tests;

public class MediaLocationTests
{
    [Theory]
    [InlineData("https://example.org/video.mkv")]
    [InlineData("http://192.168.1.10:8096/Videos/abc/stream")]
    [InlineData("rtsp://camera.local/live")]
    public void Accepts_supported_urls(string input)
    {
        Assert.True(MediaLocation.TryParse(input, out var uri));
        Assert.Equal(new Uri(input), uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("relative/path.mkv")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.org")]
    public void Rejects_invalid_input(string? input) => Assert.False(MediaLocation.TryParse(input, out _));

    [Fact]
    public void Accepts_absolute_local_path_and_strips_quotes()
    {
        var path = Path.Combine(Path.GetTempPath(), "movie file.mkv");
        Assert.True(MediaLocation.TryParse($"  \"{path}\" ", out var uri));
        Assert.True(uri!.IsFile);
        Assert.Equal(Path.GetFullPath(path), uri.LocalPath);
    }
}
