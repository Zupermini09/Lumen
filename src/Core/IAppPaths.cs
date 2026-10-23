namespace Lumen.Core;

/// <summary>Per-user folders for settings, logs and caches.</summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string LogDirectory { get; }
    string CacheDirectory { get; }
}
