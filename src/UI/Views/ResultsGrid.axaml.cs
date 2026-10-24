using Avalonia.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class ResultsGrid : UserControl
{
    public ResultsGrid()
    {
        InitializeComponent();
        ScrollMemory.Attach(Scroller);
        Scroller.SizeChanged += (_, _) => UpdateWidth();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateWidth();
    }

    // The grid builds its rows for this width (room is left for the scroll bar).
    private void UpdateWidth()
    {
        if (DataContext is GroupedCardGrid grid)
            grid.SetWidth(Scroller.Bounds.Width - 16);
    }
}
