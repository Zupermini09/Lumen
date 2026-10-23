namespace Lumen.Platform;

public enum OperatingSystemKind
{
    Windows,
    MacOS,
    Linux,
}

public static class CurrentPlatform
{
    public static OperatingSystemKind Kind =>
        OperatingSystem.IsWindows() ? OperatingSystemKind.Windows
        : OperatingSystem.IsMacOS() ? OperatingSystemKind.MacOS
        : OperatingSystem.IsLinux() ? OperatingSystemKind.Linux
        : throw new PlatformNotSupportedException("Lumen runs on Windows, macOS and Linux.");
}
