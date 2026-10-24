using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this width the hero stacks: search on top, the featured item under it.</summary>
    internal const double NarrowWidth = 1000;

    private HomeViewModel? _subscribed;

    public HomeView()
    {
        InitializeComponent();
        ScrollMemory.Attach(Scroller);
        SizeChanged += (_, _) => ArrangeHero();
        SearchBox.KeyDown += OnSearchKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnViewModelChanged;
        _subscribed = DataContext as HomeViewModel;
        if (_subscribed is not null)
            _subscribed.PropertyChanged += OnViewModelChanged;
        ArrangeHero();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnViewModelChanged;
        _subscribed = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.HasFeatured))
            Dispatcher.UIThread.Post(ArrangeHero);
    }

    // Enter hands the text to the Search page.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is HomeViewModel home)
        {
            home.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Two columns when there is room; one (search above the featured card) when narrow; the search panel alone is full width.
    private void ArrangeHero()
    {
        var narrow = Bounds.Width < NarrowWidth;
        var featured = _subscribed?.HasFeatured ?? false;
        Hero.ColumnDefinitions = narrow || !featured ? new ColumnDefinitions("*") : new ColumnDefinitions("5*,6*");
        Hero.RowDefinitions = new RowDefinitions("Auto,Auto");
        Grid.SetRow(FeaturedCard, narrow ? 1 : 0);
        Grid.SetColumn(FeaturedCard, narrow || !featured ? 0 : 1);
        SearchPanel.MinHeight = narrow || !featured ? 0 : 320;
    }
}
