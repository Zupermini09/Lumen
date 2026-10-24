using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// What movie, episode and series pages share: the item with its details, backdrop and cast, and the watched and
/// favourite toggles. The page shows what the list already knew about the item at once and fills in the rest when
/// the details arrive.
/// </summary>
public abstract partial class DetailsViewModelBase : PageViewModel
{
    private const int MaxCast = 30;

    private readonly CancellationTokenSource _lifetime = new();

    protected DetailsViewModelBase(MediaItem item, IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
    {
        _item = item;
        Client = client;
        Navigator = navigator;
        Pages = pages;
        Log = log;
    }

    protected IJellyfinClient Client { get; }
    protected INavigator Navigator { get; }
    protected IPageFactory Pages { get; }
    protected ILogger Log { get; }

    /// <summary>Cancelled when the page is closed.</summary>
    protected CancellationToken Token => _lifetime.Token;

    [ObservableProperty]
    private MediaItem _item;

    [ObservableProperty]
    private ItemDetails? _details;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>The page could not be loaded at all.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>A button action (watched, favourite) failed; the page is still usable.</summary>
    [ObservableProperty]
    private string? _actionError;

    public override string Title => Item.Name;

    public bool IsPlayed => Item.Played;
    public bool IsFavorite => Item.IsFavorite;
    public string WatchedLabel => IsPlayed ? "Mark unwatched" : "Mark watched";
    public string FavoriteLabel => IsFavorite ? "Remove favourite" : "Favourite";
    public string? Overview => string.IsNullOrWhiteSpace(Item.Overview) ? null : Item.Overview.Trim();
    public string? Tagline => Details?.Tagline;
    public string? OfficialRating => Details?.OfficialRating;
    public string? CommunityRating => Details?.CommunityRating is { } rating ? "★ " + ItemFormat.Rating(rating) : null;
    public string? Genres => Details is { Genres.Count: > 0 } d ? string.Join(" · ", d.Genres) : null;
    public string? Directors => PeopleOfType("Director", "Director");
    public string? Writers => PeopleOfType("Writer", "Writer");

    /// <summary>Actors and guest stars (crew is listed as text).</summary>
    public IReadOnlyList<PersonCardViewModel> Cast { get; private set; } = [];
    public bool HasCast => Cast.Count > 0;

    public Uri? BackdropUri => Details is { BackdropItemId: { } id, BackdropTag: { } tag } ? Client.GetImageUri(id, ImageKind.Backdrop, tag, 1280) : null;

    /// <summary>Year, runtime... under the title; each page decides what it shows.</summary>
    public abstract string? MetaLine { get; }

    /// <summary>The picture next to the title (a poster, or an episode still).</summary>
    public abstract Uri? PosterUri { get; }
    public abstract double PosterWidth { get; }
    public abstract double PosterHeight { get; }

    protected void ApplyDetails(ItemDetails details)
    {
        Cast = details.People.Where(p => p.Type is "Actor" or "GuestStar").Take(MaxCast).Select(p => PersonCardViewModel.Create(p, Client)).ToList();
        Details = details;
        Item = details.Item;
        // The item may equal the one the list gave (no change notice), yet everything derived from the details changed.
        OnPropertyChanged(string.Empty);
    }

    partial void OnItemChanged(MediaItem value) => OnPropertyChanged(string.Empty);

    private string? PeopleOfType(string type, string label)
    {
        if (Details?.People.Where(p => p.Type == type).Select(p => p.Name).Distinct().ToList() is not { Count: > 0 } names)
            return null;
        return $"{label}{(names.Count > 1 ? "s" : "")}: {string.Join(", ", names)}";
    }

    /// <summary>
    /// Waits a moment before a refresh after the player closed: it reports its final position in the background, and
    /// the refresh should see it.
    /// </summary>
    protected Task WaitForPlayerReportAsync() => Task.Delay(TimeSpan.FromMilliseconds(800), Token);

    /// <summary>After the server accepted a watched/favourite change; a series page also refreshes its episodes.</summary>
    protected virtual Task OnUserDataChangedAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        var played = !Item.Played;
        await ChangeAsync(() => Client.SetPlayedAsync(Item.Id, played, Token),
            // The server forgets the resume point when an item is marked watched or unwatched.
            Item with { Played = played, PlaybackPosition = TimeSpan.Zero },
            "Could not change the watched state");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var favorite = !Item.IsFavorite;
        await ChangeAsync(() => Client.SetFavoriteAsync(Item.Id, favorite, Token), Item with { IsFavorite = favorite }, "Could not change the favourite");
    }

    // Updates the page at once and takes the change back if the server refuses it.
    private async Task ChangeAsync(Func<Task> send, MediaItem changed, string failure)
    {
        var before = Item;
        ActionError = null;
        Item = changed;
        try
        {
            await send();
            await OnUserDataChangedAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (Token.IsCancellationRequested)
                return;
            Log.Warning("{Failure} for {Item}: {Error}", failure, before.Name, ex.Message);
            Item = before;
            ActionError = $"{failure}: {ex.Message}";
        }
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        base.Dispose();
    }
}
