using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class TransportBar : UserControl
{
    public static readonly IValueConverter PlayPauseSymbol =
        new FuncValueConverter<bool, Symbol>(playing => playing ? Symbol.Pause : Symbol.Play);

    public TransportBar() => InitializeComponent();

    private void OnFullScreenClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as MainWindow)?.ToggleFullScreen();

    /// <summary>Player keyboard shortcuts; called by the page views (not while typing in a text box).</summary>
    public static bool HandleKey(KeyEventArgs e, TransportViewModel vm, Control origin)
    {
        if (e.Source is TextBox)
            return false;

        switch (e.Key)
        {
            case Key.Space or Key.K:
                vm.TogglePauseCommand.Execute(null);
                return true;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                vm.SeekBy(TimeSpan.FromSeconds(-10));
                return true;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                vm.SeekBy(TimeSpan.FromSeconds(30));
                return true;
            case Key.Up:
                vm.Volume = Math.Min(100, vm.Volume + 5);
                return true;
            case Key.Down:
                vm.Volume = Math.Max(0, vm.Volume - 5);
                return true;
            case Key.M:
                vm.IsMuted = !vm.IsMuted;
                return true;
            case Key.F:
                (TopLevel.GetTopLevel(origin) as MainWindow)?.ToggleFullScreen();
                return true;
            default:
                return false;
        }
    }
}
