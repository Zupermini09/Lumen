using System.Runtime.InteropServices;

namespace Lumen.Platform;

public static class NativeMemory
{
    /// <summary>
    /// Returns freed native heap memory to the OS. On Linux, glibc keeps memory that libvlc's decoder threads freed
    /// (hundreds of MB after 4K playback) in its arenas, so the process looks as big as during playback until it is
    /// trimmed. Windows and macOS allocators release it on their own, so this is a no-op there.
    /// </summary>
    public static void ReleaseFreedMemory()
    {
        if (!OperatingSystem.IsLinux())
            return;
        try
        {
            _ = malloc_trim(0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // musl and other C libraries have no malloc_trim; nothing to do.
        }
    }

    [DllImport("libc")]
    private static extern int malloc_trim(nuint pad);
}
