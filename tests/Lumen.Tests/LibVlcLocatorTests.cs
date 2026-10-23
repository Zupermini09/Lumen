using System.Runtime.InteropServices;
using Lumen.Platform;

namespace Lumen.Tests;

public class LibVlcLocatorTests
{
    [Theory]
    [InlineData(OperatingSystemKind.Windows)]
    [InlineData(OperatingSystemKind.Linux)]
    public void Uses_default_search_outside_macOS(OperatingSystemKind os) =>
        Assert.Null(LibVlcLocator.Resolve(os, Architecture.X64, "/app", _ => true));

    [Theory]
    [InlineData(Architecture.Arm64, "osx-arm64")]
    [InlineData(Architecture.X64, "osx-x64")]
    public void MacOS_uses_bundled_libvlc_for_the_process_architecture(Architecture arch, string rid)
    {
        var location = LibVlcLocator.Resolve(OperatingSystemKind.MacOS, arch, "/app", _ => true);
        Assert.NotNull(location);
        Assert.Equal(Path.Combine("/app", "libvlc", rid, "lib"), location.LibraryDirectory);
        Assert.Equal(Path.Combine("/app", "libvlc", rid, "lib", "vlc", "plugins"), location.PluginDirectory);
    }

    [Fact]
    public void MacOS_falls_back_to_default_search_when_not_bundled() =>
        Assert.Null(LibVlcLocator.Resolve(OperatingSystemKind.MacOS, Architecture.Arm64, "/app", _ => false));

    [Fact]
    public void Linux_maps_libvlc_to_runtime_sonames()
    {
        Assert.Equal(["libvlc.so.5", "libvlc.so"], LibVlcLocator.LinuxLibraryCandidates("libvlc"));
        Assert.Equal(["libvlccore.so.9", "libvlccore.so"], LibVlcLocator.LinuxLibraryCandidates("libvlccore"));
        Assert.Empty(LibVlcLocator.LinuxLibraryCandidates("libX11"));
    }
}
