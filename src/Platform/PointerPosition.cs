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

    public bool TryGetPrimaryButton(out bool pressed)
    {
        pressed = false;
        if (_unavailable)
            return false;
        try
        {
            switch (CurrentPlatform.Kind)
            {
                case OperatingSystemKind.Windows:
                    // GetAsyncKeyState reads physical buttons: with swapped buttons the primary one is the right.
                    var button = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
                    pressed = (GetAsyncKeyState(button) & 0x8000) != 0;
                    return true;

                case OperatingSystemKind.MacOS:
                    pressed = CGEventSourceButtonState(CombinedSessionState, LeftMouseButton);
                    return true;

                case OperatingSystemKind.Linux:
                    if (_x11Display == IntPtr.Zero)
                        _x11Display = XOpenDisplay(IntPtr.Zero);
                    if (_x11Display == IntPtr.Zero)
                    {
                        _unavailable = true;
                        return false;
                    }
                    var root = XDefaultRootWindow(_x11Display);
                    if (!XQueryPointer(_x11Display, root, out _, out _, out _, out _, out _, out _, out var mask))
                        return false;
                    pressed = (mask & Button1Mask) != 0;
                    return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
        }
        return false;
    }

    public bool? IsPointerOverWindow(IntPtr topLevelHandle)
    {
        if (_unavailable || topLevelHandle == IntPtr.Zero)
            return null;
        try
        {
            switch (CurrentPlatform.Kind)
            {
                case OperatingSystemKind.Windows:
                    if (!GetCursorPos(out var point))
                        return null;
                    var hwnd = WindowFromPoint(point);
                    return hwnd != IntPtr.Zero && GetAncestor(hwnd, GaRoot) == topLevelHandle;

                case OperatingSystemKind.Linux:
                    if (_x11Display == IntPtr.Zero)
                        return null;
                    // Descend from the root to the deepest window under the pointer, then walk up to see whether it is
                    // inside our window (the video is a child window of it).
                    var window = XDefaultRootWindow(_x11Display);
                    for (var depth = 0; depth < 32; depth++)
                    {
                        if (!XQueryPointer(_x11Display, window, out _, out var child, out _, out _, out _, out _, out _)
                            || child == IntPtr.Zero)
                            break;
                        window = child;
                    }
                    for (var depth = 0; depth < 32 && window != IntPtr.Zero; depth++)
                    {
                        if (window == topLevelHandle)
                            return true;
                        if (XQueryTree(_x11Display, window, out var root, out var parent, out var children, out _) == 0)
                            return null;
                        if (children != IntPtr.Zero)
                            XFree(children);
                        if (parent == root)
                            return false;
                        window = parent;
                    }
                    return false;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
        }
        return null;
    }

    private const uint GaRoot = 2;
    private const int SmSwapButton = 23;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int CombinedSessionState = 0;
    private const int LeftMouseButton = 0;
    private const uint Button1Mask = 1 << 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceButtonState(int stateId, int button);

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

    [DllImport("libX11.so.6")]
    private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr parentReturn,
        out IntPtr childrenReturn, out uint childCount);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);
}
