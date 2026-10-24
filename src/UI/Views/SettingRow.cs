using Avalonia;
using Avalonia.Controls;

namespace Lumen.UI.Views;

/// <summary>
/// One row of the Settings page: a label with a short description on the left and the control (the content) on the
/// right. Rows are stacked inside a <c>Border.card</c>; the look lives in Styles.axaml.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
