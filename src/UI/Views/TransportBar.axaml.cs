using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Lumen.UI.ViewModels;

namespace Lumen.UI.Views;

public partial class TransportBar : UserControl
{
    public static readonly IValueConverter PlayPauseSymbol =
        new FuncValueConverter<bool, Symbol>(playing => playing ? Symbol.Pause : Symbol.Play);

    public TransportBar() => InitializeComponent();

    private TransportViewModel? ViewModel => DataContext as TransportViewModel;

    private void OnSkipClick(object? sender, RoutedEventArgs e) => ViewModel?.Skip();

    private void OnBackClick(object? sender, RoutedEventArgs e) => ViewModel?.SkipBack();

    private void OnForwardClick(object? sender, RoutedEventArgs e) => ViewModel?.SkipForward();

    private void OnAudioClick(object? sender, RoutedEventArgs e) =>
        ShowMenu(sender, ViewModel?.AudioOptions(), "No audio tracks");

    private void OnChaptersClick(object? sender, RoutedEventArgs e) =>
        ShowMenu(sender, ViewModel?.ChapterOptions(), "No chapters");

    private void OnSpeedClick(object? sender, RoutedEventArgs e) =>
        ShowMenu(sender, ViewModel?.SpeedOptions(), "");

    private void OnSubtitlesClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        var delay = new MenuItem { Header = vm.SubtitleDelayText };
        delay.Items.Add(Item("0.1 s earlier (G)", () => vm.AdjustSubtitleDelay(TimeSpan.FromMilliseconds(-100))));
        delay.Items.Add(Item("0.1 s later (H)", () => vm.AdjustSubtitleDelay(TimeSpan.FromMilliseconds(100))));
        delay.Items.Add(Item("Reset", vm.ResetSubtitleDelay));
        ShowMenu(sender, vm.SubtitleOptions(), "No subtitles", extra: [new Separator(), delay]);
    }

    /// <summary>True while a player menu is open, so full screen does not hide the controls under it.</summary>
    public static bool IsMenuOpen { get; private set; }

    /// <summary>When the last player menu closed (UTC); the click that closes a menu must not reach the video.</summary>
    public static DateTime LastMenuClosed { get; private set; }

    private static void ShowMenu(object? anchor, IReadOnlyList<MenuOption>? options, string emptyText, IEnumerable<Control>? extra = null)
    {
        if (anchor is not Control control || options is null)
            return;

        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        foreach (var option in options)
        {
            var item = Item(option.Label, option.Select);
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = option.IsSelected;
            flyout.Items.Add(item);
        }
        if (options.Count == 0 && emptyText.Length > 0)
            flyout.Items.Add(new MenuItem { Header = emptyText, IsEnabled = false });
        foreach (var control2 in extra ?? [])
            flyout.Items.Add(control2);

        flyout.Opened += (_, _) => IsMenuOpen = true;
        flyout.Closed += (_, _) =>
        {
            IsMenuOpen = false;
            LastMenuClosed = DateTime.UtcNow;
        };
        flyout.ShowAt(control);
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

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
                vm.SkipBack();
                return true;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                vm.SkipForward();
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
            case Key.S when vm.HasSkip:
                vm.Skip();
                return true;
            case Key.G:
                vm.AdjustSubtitleDelay(TimeSpan.FromMilliseconds(-100));
                return true;
            case Key.H:
                vm.AdjustSubtitleDelay(TimeSpan.FromMilliseconds(100));
                return true;
            case Key.OemOpenBrackets:
                vm.ChangeSpeed(-1);
                return true;
            case Key.OemCloseBrackets:
                vm.ChangeSpeed(1);
                return true;
            default:
                return false;
        }
    }
}
