using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumen.Core;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Lumen.Jellyfin;
using Lumen.Platform;
using Lumen.Platform.Secrets;
using Lumen.Player;
using Lumen.UI.Imaging;
using Lumen.UI.ViewModels;
using Lumen.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Lumen.App;

public sealed class App : Application
{
    private ServiceProvider? _services;

    /// <summary>
    /// Disposes the services (and with them LibVLC). Called by <see cref="Program"/> once the UI lifetime has fully ended:
    /// disposing from the lifetime's Exit event can race with the native video surface teardown and crash libvlc.
    /// </summary>
    public void DisposeServices()
    {
        _services?.Dispose();
        _services = null;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            try
            {
                _services = ConfigureServices();
                var shell = _services.GetRequiredService<MainWindowViewModel>();
                var window = new MainWindow { DataContext = shell };
                desktop.MainWindow = window;

                if (Program.Options.SmokeTest)
                    SmokeTestRunner.Attach(window, _services, Program.Options, desktop);
                else
                    window.Opened += async (_, _) => await shell.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup failed");
                desktop.MainWindow = StartupErrorWindow.Create(ex, Program.Paths.LogDirectory);
                if (Program.Options.SmokeTest)
                {
                    Console.Error.WriteLine($"SMOKE: FAIL startup: {ex.Message}");
                    desktop.Startup += (_, _) => desktop.Shutdown(1);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(Log.Logger);
        services.AddSingleton<IAppPaths>(Program.Paths);
        services.AddSingleton(new AppInfo(Program.Version, Program.Paths.LogDirectory, Program.Options.SmokeTest));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(Program.Paths.DataDirectory, sp.GetRequiredService<ILogger>()));
        services.AddSingleton(sp => SecretStoreFactory.Create(Program.Paths.DataDirectory, sp.GetRequiredService<ILogger>()));

        // Jellyfin
        services.AddSingleton(_ => JellyfinClient.CreateHttpClient());
        services.AddSingleton(sp => new ClientIdentity(
            "Lumen", Program.Version, Environment.MachineName, sp.GetRequiredService<ISettingsStore>().Settings.DeviceId));
        services.AddSingleton<SessionHolder>();
        services.AddSingleton<IJellyfinClient, JellyfinClient>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IImageDataSource, ImageDataSource>();
        services.AddSingleton<ImageLoader>();

        // Playback: one LibVLC instance for the app lifetime.
        services.AddSingleton(sp => new LibVlcEngine(
            LibVlcLocator.PrepareNativeLibraries(typeof(LibVLCSharp.Shared.LibVLC).Assembly),
            sp.GetRequiredService<ILogger>(),
            verbose: Program.Options.SmokeTest));
        services.AddSingleton<IPlayerService, LibVlcPlayerService>();
        services.AddSingleton<IVideoSurfaceFactory, LibVlcVideoSurfaceFactory>();

        // UI: the shell is also the navigator; pages are created by the factory with their arguments.
        services.AddSingleton<IPageFactory, PageFactory>();
        services.AddSingleton(sp => new Lazy<IPageFactory>(sp.GetRequiredService<IPageFactory>));
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<MainWindowViewModel>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        RemoteImage.Loader = provider.GetRequiredService<ImageLoader>();

        // Load libvlc eagerly so a missing or broken installation is reported at startup, not on first play.
        _ = provider.GetRequiredService<IPlayerService>();
        return provider;
    }
}
