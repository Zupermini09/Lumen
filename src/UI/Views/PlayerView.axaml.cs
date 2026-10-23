using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class PlayerView : VideoPage
{
    public PlayerView() => InitializeComponent();

    protected override ContentControl VideoHost => Host;
    protected override IReadOnlyList<Control> Chrome => [Header, Controls];

    private PlayerViewModel? ViewModel => DataContext as PlayerViewModel;

    protected override async void OnSurfaceReady()
    {
        if (ViewModel is { } vm)
            await vm.StartAsync();
    }

    private async void OnStartOverClick(object? sender, RoutedEventArgs e)
    {
        StartOverButton.IsVisible = false;
        if (ViewModel is { } vm)
            await vm.StartOverAsync();
        Focus();
    }

    private void OnVideoDoubleTapped(object? sender, TappedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as MainWindow)?.ToggleFullScreen();
}
