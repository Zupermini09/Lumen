using Avalonia.Media.Imaging;
using Lumen.Core;
using Lumen.Core.Caching;

namespace Lumen.UI.Imaging;

/// <summary>
/// Loads remote images decoded at display size, with a bounded in-memory cache (on top of the disk cache in
/// <see cref="IImageDataSource"/>). Decoding runs off the UI thread.
/// </summary>
public sealed class ImageLoader(IImageDataSource source)
{
    // About 100 MB of decoded pixels. Evicted bitmaps are not disposed: an Image may still show one; the GC frees it.
    private readonly LruCache<string, Bitmap> _memory = new(100L * 1024 * 1024);

    public async Task<Bitmap?> LoadAsync(Uri uri, int decodeHeight, CancellationToken cancellationToken)
    {
        var key = $"{uri.AbsoluteUri}#{decodeHeight}";
        if (_memory.TryGet(key, out var cached))
            return cached;

        var bytes = await source.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (bytes is null || cancellationToken.IsCancellationRequested)
            return null;

        var bitmap = await Task.Run(() =>
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                return Bitmap.DecodeToHeight(stream, decodeHeight, BitmapInterpolationMode.MediumQuality);
            }
            catch (Exception)
            {
                return null; // corrupt or unsupported image: show the placeholder
            }
        }, cancellationToken).ConfigureAwait(false);

        if (bitmap is not null)
            _memory.Add(key, bitmap, (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
        return bitmap;
    }
}
