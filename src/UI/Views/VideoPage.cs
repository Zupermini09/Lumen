using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

/// <summary>
/// Base for pages with a video surface: creates the native surface when shown, drops it when hidden, and routes
/// the player keyboard shortcuts.
/// </summary>
public abstract class VideoPage : UserControl
{
    protected abstract ContentControl VideoHost { get; }

    protected TransportViewModel? Transport => DataContext as TransportViewModel;

    /// <summary>Content drawn on top of the video (optional).</summary>
    protected virtual Control? CreateOverlay() => null;

    /// <summary>Called once the surface exists (e.g. to start playback).</summary>
    protected virtual void OnSurfaceReady() { }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        if (VideoHost.Content is null && Transport is { } vm)
        {
            VideoHost.Content = vm.CreateVideoSurface(CreateOverlay());
            OnSurfaceReady();
        }
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Drop the native video surface with the page so its window and resources are released.
        VideoHost.Content = null;
        (TopLevel.GetTopLevel(this) as MainWindow)?.ExitFullScreen();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && Transport is { } vm && TransportBar.HandleKey(e, vm, this))
            e.Handled = true;
    }
}
