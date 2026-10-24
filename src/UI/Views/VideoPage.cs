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
/// Clicking the video pauses or resumes, double-clicking toggles full screen, and a large play button sits in the
/// middle of the video while paused.
/// </summary>
public abstract class VideoPage : UserControl
{
    private static readonly TimeSpan ChromeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxClickDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(450);
    private const int ClickSlop = 8;

    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _pointerPoll;
    private readonly PlayButtonOverlay _playButton;
    private Window? _window;
    private Point _lastPointer;
    private (int X, int Y)? _lastScreenPointer;
    private int _pollTicks;

    // Click detection on the video (see PollPointer).
    private bool _buttonWasDown;
    private (int X, int Y, DateTime At)? _press;
    private (int X, int Y, DateTime At)? _lastClick;

    /// <summary>Set once at startup by the composition root (platform-specific).</summary>
    public static IPointerPosition? PointerPosition { get; set; }

    protected VideoPage()
    {
        _hideTimer = Timers.Create(ChromeTimeout, DispatcherPriority.Normal, HideChromeIfIdle);
        // Pointer events over the native video window do not reach Avalonia, so the mouse is polled: to bring the
        // full-screen controls back when it moves, and to notice clicks on the video.
        _pointerPoll = Timers.Create(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, PollPointer);
        // handledEventsToo: the video surface and controls may mark pointer events handled.
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, handledEventsToo: true);
        _playButton = new PlayButtonOverlay(OnPlayButtonClicked);
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

    private TransportViewModel? _bound;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
            _window.PositionChanged += OnWindowPositionChanged;
        }
        VideoHost.SizeChanged += OnVideoHostSizeChanged;
        _pointerPoll.Start();
        Bind(Transport);
        Focus();
    }

    // Avalonia reuses this view when one player page replaces another (e.g. the next episode), so the new page
    // model arrives as a DataContext change, not as a new attach.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
            Bind(Transport);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _hideTimer.Stop();
        _pointerPoll.Stop();
        _playButton.Close();
        VideoHost.SizeChanged -= OnVideoHostSizeChanged;
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window.PositionChanged -= OnWindowPositionChanged;
        }
        Bind(null);
        (_window as MainWindow)?.ExitFullScreen();
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Bind(TransportViewModel? vm)
    {
        if (ReferenceEquals(vm, _bound))
            return;
        if (_bound is not null)
            _bound.PropertyChanged -= OnTransportPropertyChanged;
        _bound = vm;
        if (vm is null)
        {
            // Leaving the page: drop the native video surface so its window and resources are released.
            VideoHost.Content = null;
            return;
        }

        vm.PropertyChanged += OnTransportPropertyChanged;
        // A reused view (next episode) keeps its surface: it shows the one shared player, and re-creating a native
        // window in the middle of a layout pass leaves it mispositioned.
        VideoHost.Content ??= vm.CreateVideoSurface(CreateOverlay());
        OnSurfaceReady();
        UpdateChrome(show: true);
        UpdatePausedButton();
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
        UpdateChrome(show: !IsFullScreen);
        UpdatePausedButton();
    }

    private void OnTransportPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TransportViewModel.State):
                UpdateChrome(show: Transport?.State != PlaybackState.Playing);
                UpdatePausedButton();
                break;
            // A skip button or the next-episode countdown must be seen, even in full screen.
            case nameof(TransportViewModel.HasSkip) when Transport?.HasSkip == true:
            case nameof(PlayerViewModel.IsCountingDown):
                UpdateChrome(show: true);
                break;
        }
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

        // Mouse movement in full screen brings the controls back (checked every ~200 ms).
        if (++_pollTicks % 8 == 0)
        {
            var previous = _lastScreenPointer;
            _lastScreenPointer = (x, y);
            if (IsFullScreen && previous is { } p && (Math.Abs(p.X - x) > 2 || Math.Abs(p.Y - y) > 2))
                UpdateChrome(show: true);
        }

        if (!PointerPosition.TryGetPrimaryButton(out var down))
            return;
        var now = DateTime.UtcNow;
        if (down && !_buttonWasDown)
        {
            // A press only counts if it lands on the video (not on another app in front of it) and no menu is in the way
            // (the press that closes a menu must not also pause).
            _press = IsPointerOverOurWindow() && !TransportBar.IsMenuOpen
                     && now - TransportBar.LastMenuClosed > TimeSpan.FromMilliseconds(300)
                     && IsOnVideo(x, y)
                ? (x, y, now)
                : null;
        }
        else if (!down && _buttonWasDown && _press is { } press)
        {
            _press = null;
            if (now - press.At <= MaxClickDuration && Near(press.X, press.Y, x, y) && IsOnVideo(x, y))
                OnVideoClicked(x, y, now);
        }
        _buttonWasDown = down;
    }

    private void OnVideoClicked(int x, int y, DateTime now)
    {
        if (Transport is not { } vm)
            return;
        // Every click toggles pause; the second click of a double click also toggles full screen, so a double click
        // leaves playback as it was (like web video players).
        if (vm.State is PlaybackState.Playing or PlaybackState.Paused)
            vm.TogglePauseCommand.Execute(null);
        if (_lastClick is { } last && now - last.At <= DoubleClickTime && Near(last.X, last.Y, x, y))
        {
            _lastClick = null;
            (_window as MainWindow)?.ToggleFullScreen();
        }
        else
        {
            _lastClick = (x, y, now);
        }
        Focus();
    }

    private static bool Near(int x1, int y1, int x2, int y2) =>
        Math.Abs(x1 - x2) <= ClickSlop && Math.Abs(y1 - y2) <= ClickSlop;

    /// <summary>True if the screen point is over the video (and not over the play button floating on it).</summary>
    private bool IsOnVideo(int x, int y)
    {
        if (!VideoHost.IsEffectivelyVisible || VideoHost.Bounds.Width <= 0)
            return false;
        if (!ScreenRect(VideoHost).Contains(new PixelPoint(x, y)))
            return false;
        return _playButton.ScreenBounds is not { } button || !button.Contains(new PixelPoint(x, y));
    }

    private bool IsPointerOverOurWindow()
    {
        if (_window is null)
            return false;
        // Linux: Avalonia's IsActive turns false once the native video child window takes the X focus, so ask the
        // window system what is under the mouse instead. Platforms that cannot tell fall back to IsActive.
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        return PointerPosition?.IsPointerOverWindow(handle) ?? _window.IsActive;
    }

    private static PixelRect ScreenRect(Visual visual)
    {
        var topLeft = visual.PointToScreen(new Point(0, 0));
        var bottomRight = visual.PointToScreen(new Point(visual.Bounds.Width, visual.Bounds.Height));
        return new PixelRect(topLeft, bottomRight);
    }

    private void OnPlayButtonClicked()
    {
        if (Transport?.State == PlaybackState.Paused)
            Transport.TogglePauseCommand.Execute(null);
        // The first click of a double click on the middle of the video pauses, which brings this button up under
        // the mouse, so the second click lands here: still a double click.
        if (_lastClick is { } last && DateTime.UtcNow - last.At <= DoubleClickTime)
        {
            _lastClick = null;
            (_window as MainWindow)?.ToggleFullScreen();
        }
        // Clicking the overlay window may have taken the focus: give it back so the shortcuts keep working.
        _window?.Activate();
        Focus();
    }

    /// <summary>Shows the big play button in the middle of the video while paused.</summary>
    private void UpdatePausedButton()
    {
        var show = VisualRoot is not null && _window is { WindowState: not WindowState.Minimized }
                   && VideoHost.Bounds.Width > 0 && Transport?.State == PlaybackState.Paused;
        if (show)
            _playButton.Show(_window!, VideoHost);
        else
            _playButton.Hide();
    }

    // The overlay is a separate window: move it along when the video area moves or resizes (full screen...).
    private void RepositionPausedButton()
    {
        if (_playButton.IsShown)
            Dispatcher.UIThread.Post(UpdatePausedButton, DispatcherPriority.Background);
    }

    private void OnVideoHostSizeChanged(object? sender, SizeChangedEventArgs e) => RepositionPausedButton();

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => RepositionPausedButton();

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
        if (TransportBar.IsMenuOpen || Transport?.HasSkip == true || (Transport as PlayerViewModel)?.IsCountingDown == true)
        {
            _hideTimer.Start();
            return;
        }
        if (IsFullScreen && Transport?.State == PlaybackState.Playing)
            UpdateChrome(show: false);
    }
}
