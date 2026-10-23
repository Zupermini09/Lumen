using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lumen.App;

/// <summary>Shown instead of the main window when the app cannot start (e.g. libvlc is missing).</summary>
internal static class StartupErrorWindow
{
    public static Window Create(Exception error, string logDirectory)
    {
        var hint = OperatingSystem.IsLinux()
            ? "\n\nOn Linux, Lumen needs VLC's libraries. Install them with your package manager, e.g. 'sudo apt install libvlc5 vlc-plugin-base', or use the AppImage, which includes them."
            : string.Empty;

        return new Window
        {
            Title = "Lumen could not start",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Lumen could not start", FontSize = 20 },
                    new SelectableTextBlock { Text = error.Message + hint, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new SelectableTextBlock { Text = $"Logs: {logDirectory}", Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right }.WithClick(b => (TopLevel.GetTopLevel(b) as Window)?.Close()),
                },
            },
        };
    }

    private static Button WithClick(this Button button, Action<Button> onClick)
    {
        button.Click += (_, _) => onClick(button);
        return button;
    }
}
