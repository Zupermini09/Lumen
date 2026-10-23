using Lumen.Core;
using Serilog;

namespace Lumen.Platform.Secrets;

public static class SecretStoreFactory
{
    /// <summary>
    /// The OS credential store (Windows DPAPI, macOS Keychain, Linux Secret Service), falling back to a file only the
    /// user can read when the OS store is unavailable (e.g. no keyring daemon on Linux).
    /// </summary>
    public static ISecretStore Create(string dataDirectory, ILogger log)
    {
        var fileStore = new FileSecretStore(Path.Combine(dataDirectory, "secrets"), protect: OperatingSystem.IsWindows());
        return CurrentPlatform.Kind switch
        {
            OperatingSystemKind.Windows => fileStore, // DPAPI-encrypted files
            OperatingSystemKind.MacOS => new FallbackSecretStore(new MacKeychainSecretStore(), fileStore, log),
            OperatingSystemKind.Linux => new FallbackSecretStore(new LibSecretStore(), fileStore, log),
            _ => fileStore,
        };
    }
}

/// <summary>Uses the primary store; if it fails, logs once and uses the fallback.</summary>
internal sealed class FallbackSecretStore(ISecretStore primary, ISecretStore fallback, ILogger log) : ISecretStore
{
    private bool _primaryFailed;

    public string? Get(string key) => Try(() => primary.Get(key), () => fallback.Get(key)) ?? fallback.Get(key);

    public void Set(string key, string secret) =>
        Try<object?>(() => { primary.Set(key, secret); fallback.Remove(key); return null; },
                     () => { fallback.Set(key, secret); return null; });

    public void Remove(string key)
    {
        Try<object?>(() => { primary.Remove(key); return null; }, () => null);
        fallback.Remove(key);
    }

    private T Try<T>(Func<T> usePrimary, Func<T> useFallback)
    {
        if (!_primaryFailed)
        {
            try
            {
                return usePrimary();
            }
            catch (Exception ex)
            {
                _primaryFailed = true;
                log.Warning("OS secret store unavailable, using the protected file store instead: {Error}", ex.Message);
            }
        }

        return useFallback();
    }
}
