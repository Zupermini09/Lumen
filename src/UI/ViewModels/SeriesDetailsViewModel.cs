using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Formatting;
using Lumen.Core.Jellyfin;
using Serilog;

namespace Lumen.UI.ViewModels;

/// <summary>
/// A series: its details, a Play / Resume button for the next episode to watch, the seasons, and the episodes of the
/// selected season. Opened with a season, it is the same page with that season selected.
/// </summary>
public sealed partial class SeriesDetailsViewModel : DetailsViewModelBase
{
    private readonly MediaItem _series;
    private string? _wantedSeasonId;
    private bool _selectingSeason;
    private MediaItem? _playTarget;
    private CancellationTokenSource? _episodesLoad;

    [ObservableProperty]
    private IReadOnlyList<MediaItem> _seasons = [];

    [ObservableProperty]
    private MediaItem? _selectedSeason;

    [ObservableProperty]
    private IReadOnlyList<EpisodeRowViewModel> _episodes = [];

    [ObservableProperty]
    private bool _isLoadingEpisodes;

    [ObservableProperty]
    private string? _episodesError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayTarget))]
    private string _playLabel = "Play";

    public SeriesDetailsViewModel(MediaItem item, IJellyfinClient client, INavigator navigator, IPageFactory pages, ILogger log)
        : base(SeriesOf(item), client, navigator, pages, log.ForContext<SeriesDetailsViewModel>())
    {
        _series = Item;
        _wantedSeasonId = item.Type == "Season" ? item.Id : null;
        _ = LoadAsync();
    }

    // A season knows its series' id and name, which is enough to open the series page.
    private static MediaItem SeriesOf(MediaItem item) => item.Type == "Season"
        ? new MediaItem { Id = item.SeriesId!, Name = item.SeriesName ?? item.Name, Type = "Series", IsFolder = true }
        : item;

    public override string? MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (ItemFormat.YearRange(Item.ProductionYear, Details?.EndYear, Details?.Status) is { } years)
                parts.Add(years);
            if (Seasons.Count(s => s.IndexNumber > 0) is > 0 and var count)
                parts.Add(count == 1 ? "1 season" : $"{count} seasons");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    public override double PosterWidth => 220;
    public override double PosterHeight => 330;

    public override Uri? PosterUri =>
        Item.PrimaryImageTag is { } tag ? Client.GetImageUri(Item.Id, ImageKind.Primary, tag, (int)PosterHeight * 2) : null;

    public bool HasPlayTarget => _playTarget is not null;
    public bool HasEpisodes => Episodes.Count > 0;
    public bool HasNoEpisodes => !IsLoadingEpisodes && EpisodesError is null && Episodes.Count == 0 && SelectedSeason is not null;

    partial void OnSeasonsChanged(IReadOnlyList<MediaItem> value) => OnPropertyChanged(nameof(MetaLine));

    partial void OnEpisodesChanged(IReadOnlyList<EpisodeRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasEpisodes));
        OnPropertyChanged(nameof(HasNoEpisodes));
    }

    partial void OnIsLoadingEpisodesChanged(bool value) => OnPropertyChanged(nameof(HasNoEpisodes));
    partial void OnEpisodesErrorChanged(string? value) => OnPropertyChanged(nameof(HasNoEpisodes));

    partial void OnSelectedSeasonChanged(MediaItem? value)
    {
        // The list clears its selection while it is being refilled; only a real choice counts.
        OnPropertyChanged(nameof(HasNoEpisodes));
        if (_selectingSeason || value is null)
            return;
        _wantedSeasonId = value.Id;
        _ = LoadEpisodesAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var details = Client.GetItemDetailsAsync(_series.Id, Token);
            var seasons = Client.GetSeasonsAsync(_series, Token);
            var nextUp = NextUpAsync();
            ApplyDetails(await details);
            await ShowSeasonsAsync(await seasons, await nextUp);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Log.Warning("Could not load {Series}: {Error}", _series.Name, ex.Message);
            Error = $"Could not load {_series.Name}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Next up is optional: without it the page still lists seasons and episodes.
    private async Task<MediaItem?> NextUpAsync()
    {
        try
        {
            return await Client.GetSeriesNextUpAsync(_series, Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Log.Information("Next up unavailable for {Series}: {Error}", _series.Name, ex.Message);
            return null;
        }
    }

    private async Task ShowSeasonsAsync(IReadOnlyList<MediaItem> seasons, MediaItem? nextUp)
    {
        // Open on the season asked for, else the one with the next episode to watch, else the first real season.
        var wanted = _wantedSeasonId ?? nextUp?.SeasonId;
        var selected = seasons.FirstOrDefault(s => s.Id == wanted)
            ?? seasons.FirstOrDefault(s => s.IndexNumber > 0)
            ?? seasons.FirstOrDefault();

        _selectingSeason = true;
        Seasons = seasons;
        SelectedSeason = selected;
        _selectingSeason = false;

        SetPlayTarget(nextUp ?? await FirstEpisodeAsync(seasons));
        await LoadEpisodesAsync();
    }

    // All episodes watched (or none aired yet): Play starts from the first episode.
    private async Task<MediaItem?> FirstEpisodeAsync(IReadOnlyList<MediaItem> seasons)
    {
        var first = seasons.FirstOrDefault(s => s.IndexNumber > 0) ?? seasons.FirstOrDefault();
        if (first is null)
            return null;
        try
        {
            return (await Client.GetEpisodesAsync(_series, first, Token)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Log.Information("Episodes unavailable for {Series}: {Error}", _series.Name, ex.Message);
            return null;
        }
    }

    private void SetPlayTarget(MediaItem? episode)
    {
        _playTarget = episode;
        var code = episode is null ? null : ItemFormat.EpisodeCode(episode.ParentIndexNumber, episode.IndexNumber);
        PlayLabel = (episode?.ResumePosition > TimeSpan.Zero ? "Resume" : "Play") + (code is null ? "" : " " + code);
        OnPropertyChanged(nameof(HasPlayTarget));
    }

    private async Task LoadEpisodesAsync()
    {
        _episodesLoad?.Cancel();
        if (SelectedSeason is not { } season)
        {
            Episodes = [];
            return;
        }

        var load = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _episodesLoad = load;
        IsLoadingEpisodes = true;
        EpisodesError = null;
        try
        {
            var episodes = await Client.GetEpisodesAsync(_series, season, load.Token);
            Episodes = episodes.Select(e => EpisodeRowViewModel.Create(e, Client, OpenEpisode, PlayEpisode)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Log.Warning("Could not load episodes of {Season}: {Error}", season.Name, ex.Message);
            EpisodesError = $"Could not load the episodes: {ex.Message}";
            Episodes = [];
        }
        catch (OperationCanceledException)
        {
            return; // another season was selected meanwhile; it shows its own state
        }
        finally
        {
            if (ReferenceEquals(_episodesLoad, load))
            {
                _episodesLoad = null;
                IsLoadingEpisodes = false;
            }
            load.Dispose();
        }
    }

    // Back from the player or an episode page: watched marks, resume points and the next episode may have changed.
    public override async void OnNavigatedTo()
    {
        try
        {
            await WaitForPlayerReportAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await RefreshAsync();
    }

    protected override Task OnUserDataChangedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var details = Client.GetItemDetailsAsync(_series.Id, Token);
            var seasons = Client.GetSeasonsAsync(_series, Token);
            var nextUp = NextUpAsync();
            var item = (await details).Item;
            Item = Item with { Played = item.Played, IsFavorite = item.IsFavorite };
            await ShowSeasonsAsync(await seasons, await nextUp);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Log.Information("Could not refresh {Series}: {Error}", _series.Name, ex.Message);
        }
    }

    private void OpenEpisode(MediaItem episode) => Navigator.Navigate(Pages.Details(episode));
    private void PlayEpisode(MediaItem episode) => Navigator.Navigate(Pages.Player(episode));

    [RelayCommand]
    private void Play()
    {
        if (_playTarget is { } episode)
            PlayEpisode(episode);
    }

    [RelayCommand]
    private Task RetryEpisodesAsync() => LoadEpisodesAsync();
}
