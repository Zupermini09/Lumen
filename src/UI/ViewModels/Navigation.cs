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

    /// <summary>The details page of a movie or episode.</summary>
    PageViewModel Details(MediaItem item);

    /// <summary>The page of a series (or of one of its seasons, which is then preselected).</summary>
    PageViewModel Series(MediaItem item);
    PageViewModel PlaybackTest();
    PageViewModel About();
}

public enum ItemTarget
{
    Folder,
    Player,
    Details,
    Series,
}

public static class ItemNavigation
{
    /// <summary>
    /// Like the web client: movies and episodes open their details, series and seasons the series page; other playable
    /// items (music videos, tracks...) play; folders (libraries, albums, collections...) open their contents.
    /// </summary>
    public static ItemTarget TargetFor(MediaItem item) => item.Type switch
    {
        "Movie" or "Episode" => ItemTarget.Details,
        "Series" => ItemTarget.Series,
        "Season" when item.SeriesId is not null => ItemTarget.Series,
        _ => item.IsPlayable ? ItemTarget.Player : ItemTarget.Folder,
    };

    public static void Open(MediaItem item, INavigator navigator, IPageFactory pages) =>
        navigator.Navigate(TargetFor(item) switch
        {
            ItemTarget.Details => pages.Details(item),
            ItemTarget.Series => pages.Series(item),
            ItemTarget.Player => pages.Player(item),
            _ => pages.Items(item),
        });
}
