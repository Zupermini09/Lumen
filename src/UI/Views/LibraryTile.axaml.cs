using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lumen.UI.Views;

public partial class LibraryTile : UserControl
{
    public LibraryTile() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => MediaCard.OpenFrom(this);
}
