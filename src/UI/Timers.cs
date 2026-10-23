using Avalonia.Threading;

namespace Lumen.UI;

internal static class Timers
{
    /// <summary>
    /// A stopped UI-thread timer. (DispatcherTimer's constructor that takes a callback starts the timer right away,
    /// which is never what these call sites want.)
    /// </summary>
    public static DispatcherTimer Create(TimeSpan interval, DispatcherPriority priority, Action onTick)
    {
        var timer = new DispatcherTimer(priority) { Interval = interval };
        timer.Tick += (_, _) => onTick();
        return timer;
    }
}
