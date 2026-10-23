using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumen.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject;

/// <summary>A page shown in the shell. Disposed when the user navigates away so it releases its resources.</summary>
public abstract class PageViewModel : ViewModelBase, IDisposable
{
    public abstract string Title { get; }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
