using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lumen.UI.Views;

/// <summary>
/// A transparent, borderless window laid exactly over the video. The native video surface swallows the mouse and cannot
/// be drawn on, but a separate window above it can: this one carries the player's header, controls and play button, and
/// being a real Avalonia window it gets real pointer events (hover, clicks, double clicks, a hidden cursor) everywhere
/// over the picture. It is owned by the main window, so it stays with Lumen and is shown without taking the focus.
/// </summary>
internal sealed class PlayerOverlayWindow : Window
{
    /// <summary>A click that moves further than this between press and release is not a click.</summary>
    private const double ClickSlop = 6;
    private static readonly TimeSpan MaxClickDuration = TimeSpan.FromMilliseconds(600);

    private Point _lastMove;
    private (Point Position, DateTime At)? _press;

    public PlayerOverlayWindow()
    {
        Body = new PlayerOverlayContent();
        Content = Body;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Lumen player";

        // handledEventsToo: buttons and sliders mark their pointer events handled, but they still count as mouse activity.
        AddHandler(PointerMovedEvent, OnMoved, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPressedAnywhere, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPressed);
        AddHandler(PointerReleasedEvent, OnReleased);
        AddHandler(PointerReleasedEvent, OnReleasedAnywhere, handledEventsToo: true);
    }

    // A Window subclass has no template of its own.
    protected override Type StyleKeyOverride => typeof(Window);

    public PlayerOverlayContent Body { get; }

    /// <summary>False when the system cannot draw transparent windows (Linux without a compositor): the overlay would hide the video.</summary>
    public bool IsTransparent => ActualTransparencyLevel != WindowTransparencyLevel.None;

    /// <summary>The mouse moved, pressed or left a button over the overlay.</summary>
    public event Action? PointerActivity;

    /// <summary>A left click landed on the video itself (not on the controls); the point is in the overlay's coordinates.</summary>
    public event Action<Point>? VideoClicked;

    /// <summary>A mouse button was released anywhere in the overlay (the page then takes the focus back).</summary>
    public event Action? PointerReleasedAnywhere;

    /// <summary>A key was pressed while this window had the focus and nothing handled it.</summary>
    public event Action<KeyEventArgs>? KeyUnhandled;

    /// <summary>Hides the mouse cursor over the whole overlay (or brings it back).</summary>
    public void SetCursorHidden(bool hidden) =>
        Body.Cursor = hidden ? new Cursor(StandardCursorType.None) : null;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
            KeyUnhandled?.Invoke(e);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        // Ignore synthetic "moves" with no actual movement (some platforms send them when the window appears).
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _lastMove.X) < 2 && Math.Abs(position.Y - _lastMove.Y) < 2)
            return;
        _lastMove = position;
        PointerActivity?.Invoke();
    }

    private void OnPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        _press = null;
        PointerActivity?.Invoke();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Body.IsVideoElement(e.Source))
            _press = (e.GetPosition(this), DateTime.UtcNow);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_press is not { } press || e.InitialPressMouseButton != MouseButton.Left)
            return;
        _press = null;
        var position = e.GetPosition(this);
        if (DateTime.UtcNow - press.At <= MaxClickDuration && Body.IsVideoElement(e.Source)
            && Math.Abs(position.X - press.Position.X) <= ClickSlop && Math.Abs(position.Y - press.Position.Y) <= ClickSlop)
            VideoClicked?.Invoke(position);
    }

    private void OnReleasedAnywhere(object? sender, PointerReleasedEventArgs e) => PointerReleasedAnywhere?.Invoke();
}
