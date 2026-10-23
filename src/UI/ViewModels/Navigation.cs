using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

public interface INavigator
{
    bool CanGoBack { get; }

    /// <summary>Shows a page on top of the current one (Back returns to it).</summary>
    void Navigate(PageViewModel page);

    /// <summary>Shows a page and clears the history (e.g. Home after signing in).</summary>
    void NavigateRoot(PageViewModel page);

    void GoBack();
}

/// <summary>Creates pages with their dependencies; implemented by the composition root.</summary>
public interface IPageFactory
{
    PageViewModel Connect(string? error = null);
    PageViewModel Login(ServerInfo server);
    PageViewModel Home();
    PageViewModel Items(MediaItem parent);
    PageViewModel Player(MediaItem item);
    PageViewModel PlaybackTest();
    PageViewModel About();
}

public static class ItemNavigation
{
    /// <summary>Playable items open the player; folders (libraries, shows, seasons, albums...) open their contents.</summary>
    public static void Open(MediaItem item, INavigator navigator, IPageFactory pages) =>
        navigator.Navigate(item.IsPlayable ? pages.Player(item) : pages.Items(item));
}
