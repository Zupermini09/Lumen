using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace Lumen.UI.Views;

/// <summary>
/// The large play button shown in the middle of the video while paused, for pages that cannot float their controls in a
/// <see cref="PlayerOverlayWindow"/> (Linux without a compositor); elsewhere the overlay window carries the button.
/// Only a separate window can be drawn over the native video surface; this one is owned by the main window (so it stays
/// with Lumen and never floats over other apps, unlike a popup on X11) and is shown without taking focus, so the
/// keyboard shortcuts keep working.
/// </summary>
internal sealed class PlayButtonOverlay
{
    private const double Size = 88;
    private readonly Action _onClick;
    private Window? _overlay;

    public PlayButtonOverlay(Action onClick) => _onClick = onClick;

    public bool IsShown => _overlay?.IsVisible == true;

    /// <summary>The button's area in screen pixels, or null when hidden.</summary>
    public PixelRect? ScreenBounds =>
        _overlay is { IsVisible: true } window
            ? new PixelRect(window.Position, PixelSize.FromSize(window.ClientSize, window.RenderScaling))
            : null;

    /// <summary>Shows the button centred on <paramref name="target"/>, or moves it there if already shown.</summary>
    public void Show(Window owner, Visual target)
    {
        _overlay ??= CreateWindow();
        var center = new Point(target.Bounds.Width / 2 - Size / 2, target.Bounds.Height / 2 - Size / 2);
        var position = target.PointToScreen(center);
        if (_overlay.Position != position)
            _overlay.Position = position;
        if (!_overlay.IsVisible)
            _overlay.Show(owner);
    }

    public void Hide() => _overlay?.Hide();

    public void Close()
    {
        _overlay?.Close();
        _overlay = null;
    }

    private Window CreateWindow()
    {
        var button = new PlayOverlayButton { Width = Size, Height = Size };
        button.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                _onClick();
        };

        return new Window
        {
            Content = button,
            SizeToContent = SizeToContent.WidthAndHeight,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.Manual,
            Title = "Play",
        };
    }
}
