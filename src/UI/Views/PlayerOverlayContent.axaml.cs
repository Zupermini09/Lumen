using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Lumen.UI.Views;

public partial class PlayerOverlayContent : UserControl
{
    public PlayerOverlayContent() => InitializeComponent();

    /// <summary>Header and controls are added here (rows 0 and 2); the scrims behind them hide with them.</summary>
    internal Grid Chrome => Layer;

    internal void SetChromeScrimsVisible(bool visible) => TopScrim.IsVisible = BottomScrim.IsVisible = visible;

    internal void SetPlayButtonVisible(bool visible) => PlayButton.IsVisible = visible;

    /// <summary>True if the element a pointer event came from is the open video or the play button (not the controls).</summary>
    internal bool IsVideoElement(object? source) =>
        source is Avalonia.Visual visual
        && (ReferenceEquals(visual, ClickSurface) || ReferenceEquals(visual, PlayButton) || PlayButton.IsVisualAncestorOf(visual));
}
