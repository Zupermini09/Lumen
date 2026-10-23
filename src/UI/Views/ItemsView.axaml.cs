using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class ItemsView : UserControl
{
    public ItemsView()
    {
        InitializeComponent();
        ScrollMemory.Attach(Scroller);
        Scroller.SizeChanged += (_, _) => UpdateColumns();
    }

    private ItemsViewModel? ViewModel => DataContext as ItemsViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateColumns();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemsViewModel.CardSlotWidth))
            UpdateColumns();
    }

    // How many cards fit across: the grid is a virtualized list of rows of this many cards.
    private void UpdateColumns()
    {
        if (ViewModel is not { } vm || Scroller.Bounds.Width <= 0)
            return;
        var usable = Scroller.Bounds.Width - 16 + ItemsViewModel.CardSpacing; // room for the scroll bar
        vm.SetColumns((int)(usable / vm.CardSlotWidth));
    }
}
