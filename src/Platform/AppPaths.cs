using Lumen.Core;

namespace Lumen.Platform;

public sealed class AppPaths : IAppPaths
{
    private const string AppFolderName = "Lumen";

    public required string DataDirectory { get; init; }
    public required string LogDirectory { get; init; }
    public required string CacheDirectory { get; init; }

    /// <summary>Resolves the folders for the current user and makes sure they exist.</summary>
    public static AppPaths CreateForCurrentUser()
    {
        var paths = Resolve(
            CurrentPlatform.Kind,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Directory.CreateDirectory(paths.CacheDirectory);
        return paths;
    }

    internal static AppPaths Resolve(OperatingSystemKind os, Func<string, string?> env, string home)
    {
        switch (os)
        {
            case OperatingSystemKind.Windows:
            {
                var local = NonEmpty(env("LOCALAPPDATA")) ?? Path.Combine(home, "AppData", "Local");
                var root = Path.Combine(local, AppFolderName);
                return new AppPaths
                {
                    DataDirectory = root,
                    LogDirectory = Path.Combine(root, "logs"),
                    CacheDirectory = Path.Combine(root, "cache"),
                };
            }
            case OperatingSystemKind.MacOS:
            {
                var library = Path.Combine(home, "Library");
                return new AppPaths
                {
                    DataDirectory = Path.Combine(library, "Application Support", AppFolderName),
                    LogDirectory = Path.Combine(library, "Logs", AppFolderName),
                    CacheDirectory = Path.Combine(library, "Caches", AppFolderName),
                };
            }
            case OperatingSystemKind.Linux:
            {
                // XDG base directory spec; lower-case folder name is the Linux convention.
                var config = NonEmpty(env("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
                var cache = NonEmpty(env("XDG_CACHE_HOME")) ?? Path.Combine(home, ".cache");
                var state = NonEmpty(env("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state");
                return new AppPaths
                {
                    DataDirectory = Path.Combine(config, "lumen"),
                    LogDirectory = Path.Combine(state, "lumen", "logs"),
                    CacheDirectory = Path.Combine(cache, "lumen"),
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(os), os, null);
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
