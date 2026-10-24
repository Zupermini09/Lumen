using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

/// <summary>
/// Keeps a scroll viewer's position in its data context (<see cref="IScrollMemory"/>): a page's view is created anew each
/// time the page is shown, so without this, Back would land at the top. The position is restored as soon as the content is
/// tall enough to reach it, which for a virtualized list can be a layout pass or two later.
/// </summary>
internal static class ScrollMemory
{
    public static void Attach(ScrollViewer viewer)
    {
        double? pending = null;

        void TryRestore()
        {
            if (pending is not { } target)
                return;
            if (viewer.Extent.Height - viewer.Viewport.Height >= target - 0.5 && viewer.Viewport.Height > 0)
            {
                viewer.Offset = new Vector(viewer.Offset.X, target);
                pending = null;
            }
        }

        viewer.DataContextChanged += (_, _) =>
        {
            pending = viewer.DataContext is IScrollMemory { ScrollOffset: > 0 } memory ? memory.ScrollOffset : null;
            TryRestore();
        };

        viewer.ScrollChanged += (_, _) =>
        {
            if (pending is not null)
            {
                TryRestore(); // content is still arriving: keep the remembered position until it can be reached
                return;
            }

            if (viewer.DataContext is IScrollMemory memory && viewer.GetVisualRoot() is not null)
                memory.ScrollOffset = viewer.Offset.Y;
        };

        // The user takes over: stop trying to restore.
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => pending = null, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerPressedEvent, (_, _) => pending = null, handledEventsToo: true);
        viewer.AddHandler(InputElement.KeyDownEvent, (_, _) => pending = null, handledEventsToo: true);
    }
}
