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
        _hideTimer = Timers.Create(ChromeTimeout, DispatcherPriority.Normal, HideChromeIfIdle);
        // Pointer events over the native video window do not reach Avalonia, so in full screen the mouse position is
        // polled to bring the controls back when the mouse moves.
        _pointerPoll = Timers.Create(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, PollPointer);
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

    private TransportViewModel? _bound;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focusable = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.PropertyChanged += OnWindowPropertyChanged;
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
        if (_window is not null)
            _window.PropertyChanged -= OnWindowPropertyChanged;
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
        switch (e.PropertyName)
        {
            case nameof(TransportViewModel.State):
                UpdateChrome(show: Transport?.State != PlaybackState.Playing);
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
        if (TransportBar.IsMenuOpen || Transport?.HasSkip == true || (Transport as PlayerViewModel)?.IsCountingDown == true)
        {
            _hideTimer.Start();
            return;
        }
        if (IsFullScreen && Transport?.State == PlaybackState.Playing)
            UpdateChrome(show: false);
    }
}
