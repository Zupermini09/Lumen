using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class MediaCard : UserControl
{
    public MediaCard() => InitializeComponent();

    // The page (Home or a library grid) that owns this card handles opening it.
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaCardViewModel card)
            return;
        var host = this.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ICardHost>().FirstOrDefault();
        host?.Open(card);
    }
}
