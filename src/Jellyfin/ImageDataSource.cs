using System.Security.Cryptography;
using System.Text;
using Lumen.Core;
using Serilog;

namespace Lumen.Jellyfin;

/// <summary>
/// Downloads images with a size-bounded disk cache. Image URLs carry the image tag, so a changed image gets a new URL
/// and cached files never go stale.
/// </summary>
public sealed class ImageDataSource : IImageDataSource
{
    private const long MaxDiskBytes = 300L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly ILogger _log;

    public ImageDataSource(HttpClient http, IAppPaths paths, ILogger log)
    {
        _http = http;
        _log = log.ForContext<ImageDataSource>();
        _directory = Path.Combine(paths.CacheDirectory, "images");
        Directory.CreateDirectory(_directory);
        _ = Task.Run(TrimDiskCache);
    }

    public async Task<byte[]?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, CacheKey(uri));
        if (File.Exists(path))
        {
            try
            {
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // fall through and download again
            }
        }

        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null; // items without this image type return 404

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var temp = path + "." + Environment.CurrentManagedThreadId + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _log.Debug("Image download failed: {Error}", ex.Message);
            return null;
        }
    }

    // The query has no secrets (images are public by tag), but hash anyway for a safe, fixed-length file name.
    private static string CacheKey(Uri uri) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..40];

    private void TrimDiskCache()
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles().OrderByDescending(f => f.LastAccessTimeUtc).ToList();
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
                if (total > MaxDiskBytes)
                    file.Delete();
            }
        }
        catch (Exception ex)
        {
            _log.Debug("Image cache trim failed: {Error}", ex.Message);
        }
    }
}
