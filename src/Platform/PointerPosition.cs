using System.Runtime.InteropServices;
using Lumen.Core;

namespace Lumen.Platform;

public sealed class PointerPosition : IPointerPosition
{
    private IntPtr _x11Display;
    private bool _unavailable;

    public bool TryGetScreenPosition(out int x, out int y)
    {
        x = y = 0;
        if (_unavailable)
            return false;
        try
        {
            switch (CurrentPlatform.Kind)
            {
                case OperatingSystemKind.Windows:
                    if (!GetCursorPos(out var point))
                        return false;
                    (x, y) = (point.X, point.Y);
                    return true;

                case OperatingSystemKind.MacOS:
                    var evt = CGEventCreate(IntPtr.Zero);
                    if (evt == IntPtr.Zero)
                        return false;
                    var location = CGEventGetLocation(evt);
                    CFRelease(evt);
                    (x, y) = ((int)location.X, (int)location.Y);
                    return true;

                case OperatingSystemKind.Linux:
                    // X11 (also XWayland). On a pure Wayland session there is no global position: returns false.
                    if (_x11Display == IntPtr.Zero)
                        _x11Display = XOpenDisplay(IntPtr.Zero);
                    if (_x11Display == IntPtr.Zero)
                    {
                        _unavailable = true;
                        return false;
                    }
                    var root = XDefaultRootWindow(_x11Display);
                    if (!XQueryPointer(_x11Display, root, out _, out _, out var rootX, out var rootY, out _, out _, out _))
                        return false;
                    (x, y) = (rootX, rootY);
                    return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern CGPoint CGEventGetLocation(IntPtr evt);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr childReturn,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);
}
