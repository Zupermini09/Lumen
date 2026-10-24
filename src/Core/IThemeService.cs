namespace Lumen.Core;

public enum AppTheme
{
    Dark,
    Light,
    /// <summary>Follow the operating system's light or dark mode.</summary>
    System,
}

/// <summary>Applies the colour theme to the running app (implemented by the shell, which owns the Avalonia application).</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}
