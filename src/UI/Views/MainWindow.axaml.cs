using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConfigureBackdrop();
        Navigation.ItemInvoked += OnItemInvoked;
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        // Tunnelling, so a text box (the search box) does not swallow it as "move the cursor left".
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Asks the OS to blur the window background where it can do so cheaply: Mica on Windows 11, vibrancy on macOS.
    /// Linux (and anything that cannot) stays on the solid colour. The page is only made see-through when the OS
    /// confirms the effect is active.
    /// </summary>
    private void ConfigureBackdrop()
    {
        if (OperatingSystem.IsWindows())
            TransparencyLevelHint = [WindowTransparencyLevel.Mica];
        else if (OperatingSystem.IsMacOS())
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
        else
            return;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
                Classes.Set("backdrop", ActualTransparencyLevel != WindowTransparencyLevel.None);
        };
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => ViewModel?.GoBack();

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Full screen hides the navigation and status bar so only the page (the video) is shown.</summary>
    public void ToggleFullScreen()
    {
        var full = WindowState != WindowState.FullScreen;
        WindowState = full ? WindowState.FullScreen : WindowState.Normal;
        Navigation.IsPaneVisible = !full;
        TopBar.IsVisible = !full;
        StatusBar.IsVisible = !full;
    }

    public void ExitFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
            ToggleFullScreen();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)
        {
            ExitFullScreen();
            ViewModel?.GoBack();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is (Key.F or Key.K) && ViewModel?.OpenSearch() == true)
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    // The mouse "back" button.
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.XButton1)
            ViewModel?.GoBack();
    }

    private async void OnItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.IsSettingsInvoked && ViewModel is { } settingsVm)
        {
            ExitFullScreen();
            await settingsVm.OpenMenuAsync("Settings");
        }
        else if (e.InvokedItemContainer is NavigationViewItem { Tag: string tag } && ViewModel is { } vm)
        {
            ExitFullScreen();
            await vm.OpenMenuAsync(tag);
        }
    }
}
