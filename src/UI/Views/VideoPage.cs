using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumen.Core;
using Lumen.Core.Playback;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

/// <summary>
/// Base for pages with a video surface: creates the native surface when shown, drops it when hidden, routes the
/// player keyboard shortcuts, and handles full screen: the page's chrome (header, controls) is hidden so the video
/// fills the screen, and the controls come back while the mouse moves or playback is paused.
/// </summary>
public abstract class VideoPage : UserControl
{
    private static readonly TimeSpan ChromeTimeout = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _pointerPoll;
    private Window? _window;
    private Point _lastPointer;
    private (int X, int Y)? _lastScreenPointer;

    /// <summary>Set once at startup by the composition root (platform-specific).</summary>
    public static IPointerPosition? PointerPosition { get; set; }

    protected VideoPage()
    {
        _hideTimer = new DispatcherTimer(ChromeTimeout, DispatcherPriority.Normal, (_, _) => HideChromeIfIdle());
        // Pointer events over the native video window do not reach Avalonia, so in full screen the mouse position is
        // polled to bring the controls back when the mouse moves.
        _pointerPoll = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => PollPointer());
        // handledEventsToo: the video surface and controls may mark pointer events handled.
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, handledEventsToo: true);
    }

    protected abstract ContentControl VideoHost { get; }

    /// <summary>Elements hidden in full screen until the mouse moves (title, transport bar...).</summary>
    protected abstract IReadOnlyList<Control> Chrome { get; }

    /// <summary>Elements that are never shown in full screen (e.g. the source box on the test page).</summary>
    protected virtual IReadOnlyList<Control> WindowedOnly => [];

    protected TransportViewModel? Transport => DataContext as TransportViewModel;

    protected bool IsFullScreen => _window?.WindowState == WindowState.FullScreen;

    /// <summary>Content drawn on top of the video (optional).</summary>
    protected virtual Control? CreateOverlay() => null;

    /// <summary>Called once the surface exists (e.g. to start playback).</summary>
    protected virtual void OnSurfaceReady() { }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.PropertyChanged += OnWindowPropertyChanged;
        if (Transport is { } vm)
        {
            vm.PropertyChanged += OnTransportPropertyChanged;
            if (VideoHost.Content is null)
            {
                VideoHost.Content = vm.CreateVideoSurface(CreateOverlay());
                OnSurfaceReady();
            }
        }
        UpdateChrome(show: true);
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _hideTimer.Stop();
        _pointerPoll.Stop();
        if (_window is not null)
            _window.PropertyChanged -= OnWindowPropertyChanged;
        if (Transport is { } vm)
            vm.PropertyChanged -= OnTransportPropertyChanged;
        // Drop the native video surface with the page so its window and resources are released.
        VideoHost.Content = null;
        (_window as MainWindow)?.ExitFullScreen();
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && Transport is { } vm && TransportBar.HandleKey(e, vm, this))
        {
            e.Handled = true;
            UpdateChrome(show: true);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
            return;
        _lastScreenPointer = null;
        if (IsFullScreen)
            _pointerPoll.Start();
        else
            _pointerPoll.Stop();
        UpdateChrome(show: !IsFullScreen);
    }

    private void OnTransportPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportViewModel.State))
            UpdateChrome(show: Transport?.State != PlaybackState.Playing);
    }

    private void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        // Ignore synthetic "moves" with no actual movement (some platforms send them when layout changes).
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _lastPointer.X) < 2 && Math.Abs(position.Y - _lastPointer.Y) < 2)
            return;
        _lastPointer = position;
        UpdateChrome(show: true);
    }

    private void PollPointer()
    {
        if (PointerPosition is null || !PointerPosition.TryGetScreenPosition(out var x, out var y))
            return;
        var previous = _lastScreenPointer;
        _lastScreenPointer = (x, y);
        if (previous is { } p && (Math.Abs(p.X - x) > 2 || Math.Abs(p.Y - y) > 2))
            UpdateChrome(show: true);
    }

    private void UpdateChrome(bool show)
    {
        var full = IsFullScreen;
        foreach (var element in WindowedOnly)
            element.IsVisible = !full;

        var visible = !full || show;
        foreach (var element in Chrome)
            element.IsVisible = visible;
        Cursor = visible ? Cursor.Default : new Cursor(StandardCursorType.None);

        _hideTimer.Stop();
        if (full && visible)
            _hideTimer.Start();
    }

    private void HideChromeIfIdle()
    {
        _hideTimer.Stop();
        // Keep the controls up while paused or stopped; hide them only during playback.
        if (TransportBar.IsMenuOpen)
        {
            _hideTimer.Start();
            return;
        }
        if (IsFullScreen && Transport?.State == PlaybackState.Playing)
            UpdateChrome(show: false);
    }
}
