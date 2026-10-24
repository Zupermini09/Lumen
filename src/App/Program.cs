using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Lumen.Platform;
using Serilog;

namespace Lumen.App;

internal static class Program
{
    internal static StartupOptions Options { get; private set; } = StartupOptions.Parse([]);
    internal static AppPaths Paths { get; private set; } = null!;

    public static string Version { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    [STAThread]
    public static int Main(string[] args)
    {
        Options = StartupOptions.Parse(args);
        Paths = AppPaths.CreateForCurrentUser();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(Paths.LogDirectory, "lumen-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        try
        {
            Log.Information("Lumen {Version} starting on {OS} ({Arch})", Version,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            (Avalonia.Application.Current as App)?.DisposeServices();
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Inter ships inside Lumen.UI so the look does not depend on installed fonts.
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Lumen.UI/Assets/Fonts#Inter" })
            .LogToTrace();
}
