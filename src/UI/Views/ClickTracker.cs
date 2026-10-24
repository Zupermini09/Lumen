using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Lumen.UI.Views;

internal enum ClickKind
{
    Single,
    Double,
}

/// <summary>
/// Tells a single click from the second click of a double click, with the operating system's double-click time and
/// distance (so a slow second click is just another click, and a fast one is a double click).
/// </summary>
internal sealed class ClickTracker
{
    /// <summary>Used when the platform does not say how long a double click may take.</summary>
    public static readonly TimeSpan DefaultTime = TimeSpan.FromMilliseconds(350);

    private static readonly Size DefaultSize = new(8, 8);
    private static readonly TimeSpan MinTime = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MaxTime = TimeSpan.FromMilliseconds(600);

    private (TimeSpan At, Point Position)? _last;

    /// <summary>
    /// Records a click at <paramref name="position"/>. <paramref name="at"/> is any monotonic clock.
    /// <paramref name="doubleClickSize"/> is the width and height of the area around the first click in which the
    /// second must land (the Windows setting; it is centred on the first click).
    /// </summary>
    public ClickKind Register(TimeSpan at, Point position, TimeSpan doubleClickTime, Size doubleClickSize)
    {
        if (_last is { } last
            && at - last.At >= TimeSpan.Zero && at - last.At <= doubleClickTime
            && Math.Abs(position.X - last.Position.X) <= doubleClickSize.Width / 2
            && Math.Abs(position.Y - last.Position.Y) <= doubleClickSize.Height / 2)
        {
            _last = null;
            return ClickKind.Double;
        }
        _last = (at, position);
        return ClickKind.Single;
    }

    public void Reset() => _last = null;

    /// <summary>The double-click time and distance of the operating system, kept within sane limits.</summary>
    public static (TimeSpan Time, Size Size) PlatformLimits(TopLevel? topLevel)
    {
        var settings = topLevel?.PlatformSettings;
        var time = settings?.GetDoubleTapTime(PointerType.Mouse) ?? DefaultTime;
        var size = settings?.GetDoubleTapSize(PointerType.Mouse) ?? DefaultSize;
        return (Clamp(time), size.Width < 2 || size.Height < 2 ? DefaultSize : size);
    }

    internal static TimeSpan Clamp(TimeSpan time) =>
        time <= TimeSpan.Zero ? DefaultTime : time < MinTime ? MinTime : time > MaxTime ? MaxTime : time;
}
