using Avalonia;
using Avalonia.Controls;

namespace Lumen.UI.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Remembered user name: go straight to the password.
        if (string.IsNullOrEmpty(UserBox.Text))
            UserBox.Focus();
        else
            PasswordBox.Focus();
    }
}
