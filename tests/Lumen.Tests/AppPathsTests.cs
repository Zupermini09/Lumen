using Lumen.Platform;

namespace Lumen.Tests;

public class AppPathsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        key => values.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void Windows_uses_local_app_data()
    {
        var paths = AppPaths.Resolve(OperatingSystemKind.Windows, Env(("LOCALAPPDATA", @"C:\Users\a\AppData\Local")), @"C:\Users\a");
        Assert.Equal(Path.Combine(@"C:\Users\a\AppData\Local", "Lumen"), paths.DataDirectory);
        Assert.Equal(Path.Combine(@"C:\Users\a\AppData\Local", "Lumen", "logs"), paths.LogDirectory);
    }

    [Fact]
    public void MacOS_uses_library_folders()
    {
        var paths = AppPaths.Resolve(OperatingSystemKind.MacOS, Env(), "/Users/a");
        Assert.Equal("/Users/a/Library/Application Support/Lumen", paths.DataDirectory);
        Assert.Equal("/Users/a/Library/Logs/Lumen", paths.LogDirectory);
        Assert.Equal("/Users/a/Library/Caches/Lumen", paths.CacheDirectory);
    }

    [Fact]
    public void Linux_defaults_to_xdg_locations()
    {
        var paths = AppPaths.Resolve(OperatingSystemKind.Linux, Env(), "/home/a");
        Assert.Equal("/home/a/.config/lumen", paths.DataDirectory);
        Assert.Equal("/home/a/.cache/lumen", paths.CacheDirectory);
        Assert.Equal("/home/a/.local/state/lumen/logs", paths.LogDirectory);
    }

    [Fact]
    public void Linux_honours_xdg_overrides()
    {
        var paths = AppPaths.Resolve(OperatingSystemKind.Linux,
            Env(("XDG_CONFIG_HOME", "/cfg"), ("XDG_CACHE_HOME", "/cache"), ("XDG_STATE_HOME", "/state")), "/home/a");
        Assert.Equal("/cfg/lumen", paths.DataDirectory);
        Assert.Equal("/cache/lumen", paths.CacheDirectory);
        Assert.Equal("/state/lumen/logs", paths.LogDirectory);
    }
}
