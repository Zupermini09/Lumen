using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>The details of a movie or an episode: metadata, cast, tracks, and the Play / Resume / Start over buttons.</summary>
public sealed partial class ItemDetailsViewModel : DetailsViewModelBase
{
    private ItemDetails? _streamsOf;
    private IReadOnlyList<StreamRow> _streamRows = [];

    public ItemDetailsViewModel(MediaItem item, IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
        : base(item, client, navigator, pages, log.ForContext<ItemDetailsViewModel>())
    {
        _ = LoadAsync();
    }

    public bool IsEpisode => Item.Type == "Episode";

    /// <summary>Episodes: the show, which the page links to.</summary>
    public string? SeriesName => IsEpisode ? Item.SeriesName : null;
    public bool HasSeriesLink => IsEpisode && Item.SeriesId is not null;
    public string? EpisodeCode => IsEpisode ? ItemFormat.EpisodeCode(Item.ParentIndexNumber, Item.IndexNumber) : null;

    public override string? MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (EpisodeCode is { } code)
                parts.Add(code);
            if (Item.ProductionYear is { } year)
                parts.Add(year.ToString());
            if (Item.RunTime is { } runTime && runTime > TimeSpan.Zero)
                parts.Add(TimeFormat.Duration(runTime));
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    public override double PosterWidth => IsEpisode ? 400 : 220;
    public override double PosterHeight => IsEpisode ? 225 : 330;

    public override Uri? PosterUri
    {
        get
        {
            var height = (int)PosterHeight * 2;
            if (Item.PrimaryImageTag is { } tag)
                return Client.GetImageUri(Item.Id, ImageKind.Primary, tag, height);
            // Episodes without a still show the series' poster.
            return Item is { SeriesId: { } seriesId, SeriesPrimaryImageTag: { } seriesTag }
                ? Client.GetImageUri(seriesId, ImageKind.Primary, seriesTag, height)
                : null;
        }
    }

    public bool CanResume => Item.ResumePosition > TimeSpan.Zero;
    public string PlayLabel => CanResume ? $"Resume from {TimeFormat.Playback(Item.ResumePosition)}" : "Play";
    public IReadOnlyList<StreamRow> StreamRows
    {
        get
        {
            if (!ReferenceEquals(_streamsOf, Details))
            {
                _streamsOf = Details;
                _streamRows = Details is { } d ? StreamSummary.Build(d.Streams) : [];
            }
            return _streamRows;
        }
    }

    public bool HasStreams => StreamRows.Count > 0;

    // The page shows the card's data at once; the full details replace it.
    private async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            ApplyDetails(await Client.GetItemDetailsAsync(Item.Id, Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Log.Warning("Could not load details of {Item}: {Error}", Item.Name, ex.Message);
            Error = $"Could not load {Item.Name}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not show details of {Item}", Item.Name);
            Error = $"Could not show {Item.Name}.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Back from the player: the resume point and watched state have changed.
    public override async void OnNavigatedTo()
    {
        try
        {
            await WaitForPlayerReportAsync();
            ApplyDetails(await Client.GetItemDetailsAsync(Item.Id, Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Log.Information("Could not refresh {Item}: {Error}", Item.Name, ex.Message);
        }
    }

    [RelayCommand]
    private void Play() => Navigator.Navigate(Pages.Player(Item));

    // The player resumes from the item's saved position; without it, it starts at the beginning.
    [RelayCommand]
    private void StartOver() => Navigator.Navigate(Pages.Player(Item with { PlaybackPosition = TimeSpan.Zero }));

    [RelayCommand]
    private void OpenSeries()
    {
        if (Item.SeriesId is { } id)
            Navigator.Navigate(Pages.Series(new MediaItem { Id = id, Name = Item.SeriesName ?? "", Type = "Series", IsFolder = true }));
    }
}
