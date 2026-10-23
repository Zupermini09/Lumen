using Avalonia;
using Avalonia.Controls;

namespace Lumen.UI.Views;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AddressBox.Focus();
        AddressBox.SelectAll();
    }
}
