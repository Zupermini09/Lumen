using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Navigation.SelectionChanged += OnNavigationSelectionChanged;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { CurrentPageKey: null })
            Select(PageKey.PlaybackTest);
    }

    /// <summary>Selects a page in the navigation pane (which navigates to it).</summary>
    public void Select(PageKey key)
    {
        var items = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>();
        Navigation.SelectedItem = items.FirstOrDefault(i => i.Tag as string == key.ToString());
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem { Tag: string tag } && Enum.TryParse<PageKey>(tag, out var key))
            ViewModel?.NavigateTo(key);
    }
}
