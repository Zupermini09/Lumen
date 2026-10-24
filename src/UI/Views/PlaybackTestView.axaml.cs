using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class PlaybackTestView : VideoPage
{
    public PlaybackTestView() => InitializeComponent();

    protected override ContentControl VideoHost => Host;
    protected override IReadOnlyList<Control> Chrome => [Controls];
    protected override IReadOnlyList<Control> WindowedOnly => [SourceBar];

    private PlaybackTestViewModel? ViewModel => DataContext as PlaybackTestViewModel;

    /// <summary>
    /// A label drawn on top of the video. If it is visible while a video plays, overlays work on this OS
    /// (the native video window would otherwise hide it).
    /// </summary>
    protected override Control CreateOverlay()
    {
        var label = new TextBlock { Text = "Overlay test: this label should stay visible over the video" };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("LumenOnScrimBrush"));
        var overlay = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = label,
        };
        overlay.Bind(Border.BackgroundProperty, overlay.GetResourceObservable("LumenScrimBrush"));
        return overlay;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || ViewModel is not { } vm)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a media file",
            AllowMultiple = false,
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.Source = path;
            if (vm.PlayCommand.CanExecute(null))
                await vm.PlayCommand.ExecuteAsync(null);
        }
    }
}
