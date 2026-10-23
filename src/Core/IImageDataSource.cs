namespace Lumen.Core;

/// <summary>Fetches encoded image bytes (from a disk cache or the network). Decoding happens in the UI layer.</summary>
public interface IImageDataSource
{
    Task<byte[]?> GetAsync(Uri uri, CancellationToken cancellationToken);
}
