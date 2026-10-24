using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumen.Core;
using Lumen.Core.Playback;
using Lumen.UI.ViewModels;
using Serilog;

namespace Lumen.UI.Views;

/// <summary>
/// Base for pages with a video surface: creates the native surface when shown, drops it when hidden, routes the
/// player keyboard shortcuts and handles full screen.
/// <para>
/// The video fills the whole page. Where the system can draw transparent windows, the page's <see cref="Chrome"/>
/// (header, controls) moves into a <see cref="PlayerOverlayWindow"/> laid over the video as semi-transparent glass; it
/// gets real pointer events, so clicking the video pauses it, a (fast) double click toggles full screen, and the controls
/// and the cursor hide together after a few idle seconds of playback. Elsewhere (Linux without a compositor) the
/// chrome stays in its own rows beside the video, and the mouse is polled instead: the native video window swallows it.
/// </para>
/// </summary>
public abstract class VideoPage : UserControl
{
    private static readonly TimeSpan ChromeTimeout = ChromePolicy.IdleTimeout;
    private static readonly TimeSpan MaxClickDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMilliseconds(500);
    private const int ClickSlop = 8;

    private enum ChromeLayout
    {
        /// <summary>Not decided yet (the video has no size yet).</summary>
        Pending,
        /// <summary>Header and controls float over the video in an overlay window.</summary>
        Overlay,
        /// <summary>Header and controls sit in rows around the video.</summary>
        Rows,
    }

    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _pointerPoll;
    private readonly PlayButtonOverlay _playButton;
    private readonly ClickTracker _clicks = new();
    private Window? _window;
    private Point _lastPointer;
    private (int X, int Y)? _lastScreenPointer;
    private int _pollTicks;

    // Floating layout (see PlayerOverlayWindow).
    private ChromeLayout _layout;
    private PlayerOverlayWindow? _overlay;
    private bool _deciding;
    private bool _chromeVisible = true;
    private TimeSpan _lastActivity;
    private readonly List<(Control Element, Panel Parent, int Index, Thickness Margin)> _moved = [];

    // Click detection on the video in the row layout (see PollPointer).
    private bool _buttonWasDown;
    private (int X, int Y, DateTime At)? _press;

    /// <summary>Set once at startup by the composition root (platform-specific).</summary>
    public static IPointerPosition? PointerPosition { get; set; }

    protected VideoPage()
    {
        _hideTimer = Timers.Create(ChromeTimeout, DispatcherPriority.Normal, HideChromeIfIdle);
        _idleTimer = Timers.Create(IdleCheck, DispatcherPriority.Normal, HideOverlayChromeIfIdle);
        // Row layout only: pointer events over the native video window do not reach Avalonia, so the mouse is polled to
        // bring the full-screen controls back when it moves, and to notice clicks on the video.
        _pointerPoll = Timers.Create(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, PollPointer);
        // handledEventsToo: the video surface and controls may mark pointer events handled.
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, handledEventsToo: true);
        _playButton = new PlayButtonOverlay(OnPlayButtonClicked);
    }

    protected abstract ContentControl VideoHost { get; }

    /// <summary>The header and controls: they float over the video, or sit in rows beside it, and hide when idle.</summary>
    protected abstract IReadOnlyList<Control> Chrome { get; }

    /// <summary>Elements that are never shown in full screen (e.g. the source box on the test page).</summary>
    protected virtual IReadOnlyList<Control> WindowedOnly => [];

    /// <summary>Margin of the <see cref="Chrome"/> elements while they float over the video; null keeps their own.</summary>
    protected virtual Thickness? FloatingMargin => null;

    protected TransportViewModel? Transport => DataContext as TransportViewModel;

    protected bool IsFullScreen => _window?.WindowState == WindowState.FullScreen;

    /// <summary>Content drawn on top of the video (optional; only shown when the video has an overlay window).</summary>
    protected virtual Control? CreateOverlay() => null;

    /// <summary>Called once the surface exists (e.g. to start playback).</summary>
    protected virtual void OnSurfaceReady() { }

    private TransportViewModel? _bound;

    private static TimeSpan Now() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
            _window.PositionChanged += OnWindowPositionChanged;
            _window.LayoutUpdated += OnWindowLayoutUpdated;
        }
        VideoHost.SizeChanged += OnVideoHostSizeChanged;
        TransportBar.MenuClosed += OnMenuClosed;
        Bind(Transport);
        Focus();
    }

    // Avalonia reuses this view when one player page replaces another (e.g. the next episode), so the new page
    // model arrives as a DataContext change, not as a new attach.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_overlay is not null)
            _overlay.DataContext = DataContext;
        if (VisualRoot is not null)
            Bind(Transport);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _hideTimer.Stop();
        _idleTimer.Stop();
        _pointerPoll.Stop();
        _playButton.Close();
        CloseOverlay();
        TransportBar.MenuClosed -= OnMenuClosed;
        VideoHost.SizeChanged -= OnVideoHostSizeChanged;
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window.PositionChanged -= OnWindowPositionChanged;
            _window.LayoutUpdated -= OnWindowLayoutUpdated;
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
        VideoHost.Content ??= vm.CreateVideoSurface(null);
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
        SyncOverlay();
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
        if (_layout == ChromeLayout.Overlay)
            return;
        // Ignore synthetic "moves" with no actual movement (some platforms send them when layout changes).
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _lastPointer.X) < 2 && Math.Abs(position.Y - _lastPointer.Y) < 2)
            return;
        _lastPointer = position;
        UpdateChrome(show: true);
    }

    // ---- Floating layout: header and controls in an overlay window over the video ----

    private void OnWindowLayoutUpdated(object? sender, EventArgs e) => SyncOverlay();

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        SyncOverlay();
        RepositionPausedButton();
    }

    private void OnVideoHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        SyncOverlay();
        RepositionPausedButton();
    }

    private bool CanPlaceOverlay() =>
        _window is { WindowState: not WindowState.Minimized } && VisualRoot is not null && VideoHost.Content is not null
        && VideoHost.IsEffectivelyVisible && VideoHost.Bounds.Width >= 1 && VideoHost.Bounds.Height >= 1;

    /// <summary>Decides the layout once the video has a size, then keeps the overlay window over the video.</summary>
    private void SyncOverlay()
    {
        switch (_layout)
        {
            case ChromeLayout.Pending:
                if (!_deciding && CanPlaceOverlay())
                {
                    _deciding = true;
                    // Not inside a layout pass: showing a window there fights the layout.
                    Dispatcher.UIThread.Post(ChooseLayout, DispatcherPriority.Background);
                }
                break;
            case ChromeLayout.Overlay when _overlay is not null:
                if (!CanPlaceOverlay())
                {
                    if (_overlay.IsVisible)
                        _overlay.Hide();
                    break;
                }
                PlaceOverlay(_overlay);
                if (!_overlay.IsVisible && _window is not null)
                    _overlay.Show(_window);
                break;
        }
    }

    private void PlaceOverlay(Window overlay)
    {
        var position = VideoHost.PointToScreen(default);
        var size = VideoHost.Bounds.Size;
        if (overlay.Width != size.Width)
            overlay.Width = size.Width;
        if (overlay.Height != size.Height)
            overlay.Height = size.Height;
        if (overlay.Position != position)
            overlay.Position = position;
    }

    private void ChooseLayout()
    {
        _deciding = false;
        if (_layout != ChromeLayout.Pending)
            return;
        if (_window is null || !CanPlaceOverlay())
            return; // try again at the next layout
        if (Environment.GetEnvironmentVariable("LUMEN_PLAYER_OVERLAY") == "0" || !TryShowOverlay())
        {
            Log.Information("Player chrome: rows beside the video (no transparent windows)");
            UseRowLayout();
        }
        else
        {
            Log.Information("Player chrome: floating over the video");
        }
    }

    private bool TryShowOverlay()
    {
        var overlay = new PlayerOverlayWindow { DataContext = DataContext };
        PlaceOverlay(overlay);
        overlay.Show(_window!);
        // No transparency (Linux without a compositor): the overlay would be an opaque rectangle hiding the video.
        if (!overlay.IsTransparent)
        {
            overlay.Close();
            return false;
        }

        _overlay = overlay;
        overlay.PointerActivity += OnOverlayPointerActivity;
        overlay.VideoClicked += OnOverlayVideoClicked;
        overlay.PointerReleasedAnywhere += ReactivateWindow;
        overlay.KeyUnhandled += ForwardKey;

        if (CreateOverlay() is { } extra)
        {
            extra.IsHitTestVisible = false;
            Grid.SetRowSpan(extra, 3);
            overlay.Body.Chrome.Children.Add(extra);
        }
        // Two steps, with a layout pass in between: a control that is still queued for layout in the main window must
        // not be attached to another window in the same pass (Avalonia throws "wrong LayoutManager").
        var detached = new List<Control>();
        foreach (var element in Chrome)
        {
            if (element.Parent is not Panel parent)
                continue;
            _moved.Add((element, parent, parent.Children.IndexOf(element), element.Margin));
            parent.Children.Remove(element);
            detached.Add(element);
        }
        Dispatcher.UIThread.Post(() => AddChromeToOverlay(overlay, detached), DispatcherPriority.Background);

        _layout = ChromeLayout.Overlay;
        Cursor = null;
        UpdatePausedButton();
        ShowOverlayChrome();
        return true;
    }

    private void AddChromeToOverlay(PlayerOverlayWindow overlay, List<Control> elements)
    {
        if (!ReferenceEquals(overlay, _overlay))
            return; // the overlay was closed meanwhile; CloseOverlay put the elements back
        foreach (var element in elements)
        {
            overlay.Body.Chrome.Children.Add(element);
            element.Classes.Add("floating");
            if (FloatingMargin is { } margin)
                element.Margin = margin;
        }
    }

    private void UseRowLayout()
    {
        _layout = ChromeLayout.Rows;
        _pointerPoll.Start();
        UpdateChrome(show: true);
        UpdatePausedButton();
    }

    /// <summary>Closes the overlay window; the header and controls go back to their rows once the page has finished detaching.</summary>
    private void CloseOverlay()
    {
        _layout = ChromeLayout.Pending;
        _deciding = false;
        if (_overlay is null)
            return;
        var moved = _moved.ToList();
        _moved.Clear();
        foreach (var (element, _, _, _) in moved)
            _overlay.Body.Chrome.Children.Remove(element); // no-op if it never got there
        _overlay.PointerActivity -= OnOverlayPointerActivity;
        _overlay.VideoClicked -= OnOverlayVideoClicked;
        _overlay.PointerReleasedAnywhere -= ReactivateWindow;
        _overlay.KeyUnhandled -= ForwardKey;
        _overlay.Close();
        _overlay = null;
        _chromeVisible = true;
        // Not now: the page's own panels are mid-detach and refuse new children.
        Dispatcher.UIThread.Post(() => RestoreChrome(moved), DispatcherPriority.Background);
    }

    private static void RestoreChrome(List<(Control Element, Panel Parent, int Index, Thickness Margin)> moved)
    {
        foreach (var (element, parent, index, margin) in moved)
        {
            if (element.Parent is not null)
                continue;
            parent.Children.Insert(Math.Min(index, parent.Children.Count), element);
            element.Classes.Remove("floating");
            element.Margin = margin;
            element.IsVisible = true;
        }
    }

    private void OnOverlayPointerActivity() => ShowOverlayChrome();

    /// <summary>Shows the header and controls (and the cursor) and restarts the idle countdown.</summary>
    private void ShowOverlayChrome()
    {
        if (_overlay is null)
            return;
        _lastActivity = Now();
        if (!_chromeVisible)
            SetOverlayChromeVisible(true);
        if (!_idleTimer.IsEnabled)
            _idleTimer.Start();
    }

    private void SetOverlayChromeVisible(bool visible)
    {
        if (_overlay is null)
            return;
        _chromeVisible = visible;
        foreach (var element in Chrome)
            element.IsVisible = visible;
        _overlay.Body.SetChromeScrimsVisible(visible);
        _overlay.SetCursorHidden(!visible);
        if (!visible)
            _idleTimer.Stop();
    }

    private void HideOverlayChromeIfIdle()
    {
        if (!_chromeVisible)
        {
            _idleTimer.Stop();
            return;
        }
        var pointerOverChrome = Chrome.Any(element => element.IsPointerOver);
        if (ChromePolicy.ShouldHide(Transport?.State ?? PlaybackState.Idle, Now() - _lastActivity, pointerOverChrome,
                TransportBar.IsMenuOpen, Transport?.HasSkip == true, (Transport as PlayerViewModel)?.IsCountingDown == true))
            SetOverlayChromeVisible(false);
    }

    private void OnOverlayVideoClicked(Point position)
    {
        // The click that closes a menu must not also pause.
        if (Transport is not { } vm || TransportBar.IsMenuOpen
            || DateTime.UtcNow - TransportBar.LastMenuClosed <= TimeSpan.FromMilliseconds(300))
            return;
        ShowOverlayChrome();
        // Every click toggles pause; the second click of a double click also toggles full screen, so a double click
        // leaves playback as it was (like web video players).
        if (vm.State is PlaybackState.Playing or PlaybackState.Paused)
            vm.TogglePauseCommand.Execute(null);
        RegisterClick(position);
        Focus();
    }

    /// <summary>Counts a click on the video; the second click of a double click (by the system's timing) toggles full screen.</summary>
    private void RegisterClick(Point position)
    {
        var (time, size) = ClickTracker.PlatformLimits(_window);
        if (_clicks.Register(Now(), position, time, size) == ClickKind.Double)
            (_window as MainWindow)?.ToggleFullScreen();
    }

    // Clicking the overlay window activates it; give the focus back to the main window so the shortcuts keep working.
    // Not while a menu is open: the menu belongs to the overlay, and activating the main window would close it.
    private void ReactivateWindow() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!TransportBar.IsMenuOpen)
                _window?.Activate();
        }, DispatcherPriority.Background);

    private void OnMenuClosed()
    {
        if (_layout == ChromeLayout.Overlay)
        {
            ReactivateWindow();
            ShowOverlayChrome();
        }
    }

    // A key pressed while the overlay has the focus goes to the page as if the main window had it.
    private void ForwardKey(KeyEventArgs e)
    {
        var forwarded = new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers,
            PhysicalKey = e.PhysicalKey,
            KeyDeviceType = e.KeyDeviceType,
            KeySymbol = e.KeySymbol,
            Source = this,
        };
        RaiseEvent(forwarded);
        e.Handled = forwarded.Handled;
    }

    // ---- Row layout: the mouse is polled, because the native video window swallows it ----

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
                OnVideoClicked(x, y);
        }
        _buttonWasDown = down;
    }

    private void OnVideoClicked(int x, int y)
    {
        if (Transport is not { } vm)
            return;
        // Every click toggles pause; the second click of a double click also toggles full screen, so a double click
        // leaves playback as it was (like web video players).
        if (vm.State is PlaybackState.Playing or PlaybackState.Paused)
            vm.TogglePauseCommand.Execute(null);
        RegisterClick(new Point(x, y));
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
        if (PointerPosition is not null && PointerPosition.TryGetScreenPosition(out var x, out var y))
            RegisterClick(new Point(x, y));
        // Clicking the overlay window may have taken the focus: give it back so the shortcuts keep working.
        _window?.Activate();
        Focus();
    }

    /// <summary>Shows the big play button in the middle of the video while paused.</summary>
    private void UpdatePausedButton()
    {
        var paused = Transport?.State == PlaybackState.Paused;
        if (_layout == ChromeLayout.Overlay)
        {
            _overlay?.Body.SetPlayButtonVisible(paused);
            return;
        }
        var show = VisualRoot is not null && _window is { WindowState: not WindowState.Minimized }
                   && VideoHost.Bounds.Width > 0 && paused;
        if (show)
            _playButton.Show(_window!, VideoHost);
        else
            _playButton.Hide();
    }

    // The play button window is separate: move it along when the video area moves or resizes (full screen...).
    private void RepositionPausedButton()
    {
        if (_playButton.IsShown)
            Dispatcher.UIThread.Post(UpdatePausedButton, DispatcherPriority.Background);
    }

    private void UpdateChrome(bool show)
    {
        var full = IsFullScreen;
        foreach (var element in WindowedOnly)
            element.IsVisible = !full;

        // Floating controls: any activity brings them back, and they hide on their own after the idle timeout.
        if (_layout == ChromeLayout.Overlay)
        {
            ShowOverlayChrome();
            return;
        }

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
