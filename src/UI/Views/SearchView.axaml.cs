using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class SearchView : UserControl
{
    private SearchViewModel? _subscribed;

    public SearchView()
    {
        InitializeComponent();
        Box.KeyDown += OnBoxKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
            _subscribed.FocusRequested -= OnFocusRequested;
        _subscribed = DataContext as SearchViewModel;
        if (_subscribed is not null)
            _subscribed.FocusRequested += OnFocusRequested;
        FocusBox(selectAll: false);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.FocusRequested -= OnFocusRequested;
        _subscribed = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFocusRequested(object? sender, EventArgs e) => FocusBox(selectAll: true);

    // Posted so it runs after the page is in the tree, so typing can start at once. When asked for again (Ctrl+F on the
    // open page) the old text is selected, so typing replaces it.
    private void FocusBox(bool selectAll) => Dispatcher.UIThread.Post(() =>
    {
        Box.Focus();
        if (selectAll)
            Box.SelectAll();
        else
            Box.CaretIndex = Box.Text?.Length ?? 0;
    }, DispatcherPriority.Input);

    // Escape empties the box (and the results); a second press leaves it to the window.
    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Box.Text))
        {
            Box.Text = "";
            e.Handled = true;
        }
    }
}
