using Avalonia;
using Avalonia.Styling;
using Lumen.Core;

namespace Lumen.App;

/// <summary>Applies the chosen theme to the Avalonia application (FluentAvalonia follows the requested variant).</summary>
internal sealed class AvaloniaThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.System => ThemeVariant.Default, // follows the operating system
                _ => ThemeVariant.Dark,
            };
    }
}
