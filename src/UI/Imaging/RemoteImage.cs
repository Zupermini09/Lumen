using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Lumen.UI.Imaging;

/// <summary>
/// Attached property that loads an <see cref="Image"/>'s bitmap from a URL: <c>imaging:RemoteImage.Uri="{Binding ImageUri}"</c>.
/// Works with virtualized lists: when a recycled container gets a new URL, the previous load is cancelled.
/// </summary>
public static class RemoteImage
{
    /// <summary>Set once at startup by the composition root.</summary>
    public static ImageLoader? Loader { get; set; }

    public static readonly AttachedProperty<Uri?> UriProperty =
        AvaloniaProperty.RegisterAttached<Image, Uri?>("Uri", typeof(RemoteImage));

    private static readonly AttachedProperty<CancellationTokenSource?> PendingProperty =
        AvaloniaProperty.RegisterAttached<Image, CancellationTokenSource?>("Pending", typeof(RemoteImage));

    static RemoteImage() => UriProperty.Changed.AddClassHandler<Image>(OnUriChanged);

    public static Uri? GetUri(Image image) => image.GetValue(UriProperty);
    public static void SetUri(Image image, Uri? value) => image.SetValue(UriProperty, value);

    private static async void OnUriChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.GetValue(PendingProperty)?.Cancel();
        image.Source = null;

        if (e.NewValue is not Uri uri || Loader is not { } loader)
            return;

        var cts = new CancellationTokenSource();
        image.SetValue(PendingProperty, cts);

        // Decode at twice the layout height so images stay sharp on high-DPI screens.
        var height = double.IsNaN(image.Height) ? 300 : (int)Math.Ceiling(image.Height * 2);
        try
        {
            var bitmap = await loader.LoadAsync(uri, Math.Max(height, 32), cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cts.IsCancellationRequested && Equals(GetUri(image), uri))
                    image.Source = bitmap;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
