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
        Navigation.ItemInvoked += OnItemInvoked;
        Navigation.BackRequested += (_, _) => ViewModel?.GoBack();
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        // Tunnelling, so a text box (the search box) does not swallow it as "move the cursor left".
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Full screen hides the navigation and status bar so only the page (the video) is shown.</summary>
    public void ToggleFullScreen()
    {
        var full = WindowState != WindowState.FullScreen;
        WindowState = full ? WindowState.FullScreen : WindowState.Normal;
        Navigation.IsPaneVisible = !full;
        Navigation.IsBackButtonVisible = !full;
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
