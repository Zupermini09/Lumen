using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class PlaybackTestView : UserControl
{
    public static readonly IValueConverter PlayPauseSymbol =
        new FuncValueConverter<bool, Symbol>(playing => playing ? Symbol.Pause : Symbol.Play);

    public PlaybackTestView() => InitializeComponent();

    private PlaybackTestViewModel? ViewModel => DataContext as PlaybackTestViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (VideoHost.Content is null && ViewModel is { } vm)
            VideoHost.Content = vm.CreateVideoSurface(CreateOverlayProbe());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Drop the native video surface with the page so its window and resources are released.
        VideoHost.Content = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A label drawn on top of the video. If it is visible while a video plays, overlays work on this OS
    /// (the native video window would otherwise hide it).
    /// </summary>
    private static Control CreateOverlayProbe() => new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 4),
        Margin = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = "Overlay test: this label should stay visible over the video", Foreground = Brushes.White },
    };

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
