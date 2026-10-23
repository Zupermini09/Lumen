using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumen.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject;

/// <summary>A page shown in the shell. Disposed when it leaves the navigation history, so it releases its resources.</summary>
public abstract class PageViewModel : ViewModelBase, IDisposable
{
    public abstract string Title { get; }

    /// <summary>False for pages that must not stay alive in the back stack (the player stops when you leave it).</summary>
    public virtual bool KeepInHistory => true;

    /// <summary>Called when another page is shown on top of this one.</summary>
    public virtual void OnNavigatedFrom() { }

    /// <summary>Called when the page is shown again after the page on top of it was closed (Back).</summary>
    public virtual void OnNavigatedTo() { }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
