using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Formatting;

namespace Lumen.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IPageFactory _pages;
    private readonly DispatcherTimer _memoryTimer;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    public MainWindowViewModel(IPageFactory pages, AppInfo appInfo)
    {
        _pages = pages;
        Title = "Lumen";
        Version = appInfo.Version;

        // A live RAM readout makes it easy to check the memory targets while testing.
        _memoryTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateMemory());
        _memoryTimer.Start();
        UpdateMemory();
    }

    public string Title { get; }
    public string Version { get; }

    public PageKey? CurrentPageKey { get; private set; }

    public void NavigateTo(PageKey key)
    {
        if (CurrentPageKey == key)
            return;

        var previous = CurrentPage;
        CurrentPageKey = key;
        CurrentPage = _pages.Create(key);
        previous?.Dispose();
    }

    private void UpdateMemory() =>
        MemoryText = $"RAM {TimeFormat.Megabytes(Environment.WorkingSet)} · managed {TimeFormat.Megabytes(GC.GetTotalMemory(false))}";

    public void Dispose()
    {
        _memoryTimer.Stop();
        CurrentPage?.Dispose();
        CurrentPage = null;
    }
}
