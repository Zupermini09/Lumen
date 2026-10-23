using System.Reflection;
using System.Runtime.InteropServices;

namespace Lumen.Platform;

/// <summary>
/// Finds the libvlc native libraries for the current OS and prepares the process environment before LibVLC is loaded.
/// </summary>
/// <remarks>
/// Windows: bundled by the VideoLAN.LibVLC.Windows package in libvlc/win-x64 next to the executable; LibVLCSharp finds it.
/// macOS:   bundled by VideoLAN.LibVLC.Mac in libvlc/osx-{arch}/lib inside the .app; we pass the path and the plugin dir.
/// Linux:   system libvlc for the .deb; for the AppImage, AppRun puts the bundled copy on LD_LIBRARY_PATH and sets
///          VLC_PLUGIN_PATH, so the normal library search finds it. LibVLCSharp asks for the unversioned "libvlc",
///          which only exists when the -dev package is installed, so we map it to the runtime soname.
/// </remarks>
public static class LibVlcLocator
{
    /// <summary>
    /// Returns the directory to pass to <c>LibVLCSharp.Core.Initialize</c>, or null to let LibVLCSharp search the default locations.
    /// </summary>
    /// <param name="libVlcSharpAssembly">The LibVLCSharp assembly, whose native imports are redirected on Linux.</param>
    public static string? PrepareNativeLibraries(Assembly libVlcSharpAssembly)
    {
        if (OperatingSystem.IsLinux())
            NativeLibrary.SetDllImportResolver(libVlcSharpAssembly, ResolveLinuxImport);

        var location = Resolve(CurrentPlatform.Kind, RuntimeInformation.ProcessArchitecture, AppContext.BaseDirectory, Directory.Exists);
        if (location?.PluginDirectory is { } pluginDir && Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH") is null)
            SetNativeEnvironmentVariable("VLC_PLUGIN_PATH", pluginDir);
        return location?.LibraryDirectory;
    }

    internal sealed record Location(string LibraryDirectory, string? PluginDirectory);

    internal static Location? Resolve(OperatingSystemKind os, Architecture arch, string baseDirectory, Func<string, bool> directoryExists)
    {
        if (os != OperatingSystemKind.MacOS)
            return null;

        var rid = arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        var lib = Path.Combine(baseDirectory, "libvlc", rid, "lib");
        if (!directoryExists(lib))
            return null;

        var plugins = Path.Combine(lib, "vlc", "plugins");
        return new Location(lib, directoryExists(plugins) ? plugins : null);
    }

    /// <summary>Runtime sonames to try for a library name used in LibVLCSharp's DllImports, most specific first.</summary>
    internal static string[] LinuxLibraryCandidates(string libraryName) => libraryName switch
    {
        "libvlc" => ["libvlc.so.5", "libvlc.so"],
        "libvlccore" => ["libvlccore.so.9", "libvlccore.so"],
        _ => [],
    };

    private static IntPtr ResolveLinuxImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (var candidate in LinuxLibraryCandidates(libraryName))
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                return handle;
        }

        return IntPtr.Zero; // fall back to the default probing
    }

    /// <summary>
    /// <see cref="Environment.SetEnvironmentVariable(string, string?)"/> does not reach native code on Unix,
    /// so libvlc would never see the value. Call libc's setenv directly.
    /// </summary>
    private static void SetNativeEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (!OperatingSystem.IsWindows())
            _ = setenv(name, value, 1);
    }

    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);
}
