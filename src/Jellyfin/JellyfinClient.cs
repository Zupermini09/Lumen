using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using Lumen.Core.Jellyfin;
using Lumen.Core.Playback;
using Serilog;
using AuthenticationException = Lumen.Core.Jellyfin.AuthenticationException;

namespace Lumen.Jellyfin;

public sealed class JellyfinClient : IJellyfinClient
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);
    private const string ItemFields = "PrimaryImageAspectRatio,MediaSourceCount";
    private const string DetailsFields = "Overview,Genres,People,MediaStreams,Taglines,PrimaryImageAspectRatio";
    private const string EpisodeFields = "Overview,PrimaryImageAspectRatio";

    private readonly HttpClient _http;
    private readonly ClientIdentity _identity;
    private readonly SessionHolder _session;
    private readonly ILogger _log;

    public JellyfinClient(HttpClient http, ClientIdentity identity, SessionHolder session, ILogger log)
    {
        _http = http;
        _identity = identity;
        _session = session;
        _log = log.ForContext<JellyfinClient>();
    }

    /// <summary>The shared HttpClient: pooled connections that are recycled so DNS changes are picked up.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ---- Server and authentication -------------------------------------------------------------------------

    public async Task<ServerInfo> FindServerAsync(string userInput, CancellationToken cancellationToken = default)
    {
        var candidates = ServerAddress.Candidates(userInput);
        if (candidates.Count == 0)
            throw new ServerConnectionException("Enter a server address, for example 192.168.1.10:8096 or https://jellyfin.example.com.");

        Exception? lastError = null;
        foreach (var baseUri in candidates)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                var info = await SendAsync<PublicSystemInfoDto>(HttpMethod.Get, baseUri, "System/Info/Public", token: null, body: null, timeout.Token);
                if (info?.Id is { Length: > 0 } id && info.Version is { Length: > 0 } version)
                {
                    _log.Information("Found {Product} {Version} at {Address}", info.ProductName ?? "Jellyfin", version, baseUri);
                    return new ServerInfo(id, string.IsNullOrWhiteSpace(info.ServerName) ? baseUri.Host : info.ServerName, version, baseUri);
                }

                lastError = new ServerConnectionException($"{baseUri} answered, but it is not a Jellyfin server.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new ServerConnectionException($"{baseUri} did not answer in time.");
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                lastError = ex;
            }

            _log.Information("No Jellyfin server at {Address}: {Error}", baseUri, lastError?.Message);
        }

        throw new ServerConnectionException(DescribeConnectionError(userInput, lastError), lastError);
    }

    public async Task<IReadOnlyList<UserInfo>> GetPublicUsersAsync(ServerInfo server, CancellationToken cancellationToken = default)
    {
        var users = await SendAsync<List<UserDto>>(HttpMethod.Get, server.Address, "Users/Public", null, null, cancellationToken);
        return users?.Select(u => u.ToModel()).ToList() ?? [];
    }

    public async Task<SessionInfo> AuthenticateAsync(ServerInfo server, string userName, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await SendAsync<AuthenticationResultDto>(HttpMethod.Post, server.Address, "Users/AuthenticateByName", null,
                new { Username = userName, Pw = password }, cancellationToken);
            return ToSession(server, result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AuthenticationException("Wrong user name or password.");
        }
    }

    public async Task<bool> IsQuickConnectEnabledAsync(ServerInfo server, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<bool>(HttpMethod.Get, server.Address, "QuickConnect/Enabled", null, null, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return false; // very old servers have no Quick Connect
        }
    }

    public async Task<QuickConnectRequest> StartQuickConnectAsync(ServerInfo server, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<QuickConnectResultDto>(HttpMethod.Post, server.Address, "QuickConnect/Initiate", null, null, cancellationToken);
        if (result?.Code is not { Length: > 0 } code || result.Secret is not { Length: > 0 } secret)
            throw new ServerConnectionException("The server did not start Quick Connect.");
        return new QuickConnectRequest(code, secret);
    }

    public async Task<bool> IsQuickConnectAuthorizedAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<QuickConnectResultDto>(HttpMethod.Get, server.Address,
            $"QuickConnect/Connect?secret={Uri.EscapeDataString(request.Secret)}", null, null, cancellationToken);
        return result?.Authenticated == true;
    }

    public async Task<SessionInfo> AuthenticateWithQuickConnectAsync(ServerInfo server, QuickConnectRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<AuthenticationResultDto>(HttpMethod.Post, server.Address, "Users/AuthenticateWithQuickConnect", null,
            new { Secret = request.Secret }, cancellationToken);
        return ToSession(server, result);
    }

    public async Task<UserInfo?> ValidateSessionAsync(SessionInfo session, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await SendAsync<UserDto>(HttpMethod.Get, session.Server.Address, $"Users/{session.User.Id}", session.AccessToken, null, cancellationToken);
            return user?.ToModel();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SignOutAsync(SessionInfo session, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Post, session.Server.Address, "Sessions/Logout", session.AccessToken, null, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _log.Information("Server sign-out failed (signing out locally anyway): {Error}", ex.Message);
        }
    }

    // ---- Browsing --------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<MediaItem>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        // UserViews is the 10.9+ route; Users/{id}/Views is the older one.
        var result = await GetWithFallbackAsync<ItemsResultDto>(session,
            $"UserViews?userId={session.User.Id}",
            $"Users/{session.User.Id}/Views", cancellationToken);
        return ToModels(result);
    }

    public async Task<IReadOnlyList<MediaItem>> GetResumeItemsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var query = $"limit={limit}&mediaTypes=Video&fields={ItemFields}&enableImageTypes=Primary,Thumb,Backdrop&imageTypeLimit=1";
        var result = await GetWithFallbackAsync<ItemsResultDto>(session,
            $"UserItems/Resume?userId={session.User.Id}&{query}",
            $"Users/{session.User.Id}/Items/Resume?{query}", cancellationToken);
        return ToModels(result);
    }

    public async Task<IReadOnlyList<MediaItem>> GetNextUpAsync(int limit, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            $"Shows/NextUp?userId={session.User.Id}&limit={limit}&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1",
            session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<MediaItem?> GetNextEpisodeAsync(MediaItem episode, CancellationToken cancellationToken = default)
    {
        if (episode.Type != "Episode" || episode.SeriesId is not { } seriesId)
            return null;
        var session = _session.Require();
        // Same call as the web client: the episode list starting at this one; the second entry is the next episode.
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            $"Shows/{seriesId}/Episodes?userId={session.User.Id}&startItemId={episode.Id}&limit=2&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1",
            session.AccessToken, null, cancellationToken);
        return ToModels(result).SkipWhile(i => i.Id == episode.Id).FirstOrDefault();
    }

    public async Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, ItemListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            "Items?" + ChildrenQuery(parent, session.User.Id, options), session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<IReadOnlyList<string>> GetGenresAsync(MediaItem library, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            "Genres?" + GenresQuery(library, session.User.Id), session.AccessToken, null, cancellationToken);
        return result?.Items.Select(g => g.Name).OfType<string>().Where(n => n.Length > 0).ToList() ?? [];
    }

    public async Task<IReadOnlyList<MediaItem>> GetLatestAsync(MediaItem library, int limit, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        // Unlike the other lists, this answers with a bare array. Older servers have it under the user.
        var query = LatestQuery(library, limit);
        var items = await GetWithFallbackAsync<List<BaseItemDto>>(session,
            $"Items/Latest?userId={session.User.Id}&{query}",
            $"Users/{session.User.Id}/Items/Latest?{query}", cancellationToken);
        return items?.Select(i => i.ToModel()).ToList() ?? [];
    }

    public async Task<IReadOnlyList<MediaItem>> SearchAsync(string term, IReadOnlyList<string> itemTypes, int limit, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            "Items?" + SearchQuery(term, itemTypes, limit, session.User.Id), session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(IReadOnlyList<string> itemTypes, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            "Items?" + FavoritesQuery(itemTypes, session.User.Id), session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<ItemDetails> GetItemDetailsAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var item = await GetWithFallbackAsync<BaseItemDto>(session,
            $"Items/{itemId}?userId={session.User.Id}&fields={DetailsFields}",
            $"Users/{session.User.Id}/Items/{itemId}?fields={DetailsFields}", cancellationToken);
        return item?.ToDetails() ?? throw new HttpRequestException("The server returned no item.", null, HttpStatusCode.NotFound);
    }

    public async Task<IReadOnlyList<MediaItem>> GetSeasonsAsync(MediaItem series, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            $"Shows/{series.Id}/Seasons?userId={session.User.Id}&fields={ItemFields}&enableImageTypes=Primary&imageTypeLimit=1",
            session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(MediaItem series, MediaItem? season, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var seasonQuery = season is null ? "" : $"&seasonId={season.Id}";
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            $"Shows/{series.Id}/Episodes?userId={session.User.Id}{seasonQuery}&fields={EpisodeFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1",
            session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    public async Task<MediaItem?> GetSeriesNextUpAsync(MediaItem series, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            $"Shows/NextUp?userId={session.User.Id}&seriesId={series.Id}&limit=1&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1",
            session.AccessToken, null, cancellationToken);
        return ToModels(result).FirstOrDefault();
    }

    public Task SetPlayedAsync(string itemId, bool played, CancellationToken cancellationToken = default) =>
        SetUserFlagAsync("UserPlayedItems", "PlayedItems", itemId, played, cancellationToken);

    public Task SetFavoriteAsync(string itemId, bool favorite, CancellationToken cancellationToken = default) =>
        SetUserFlagAsync("UserFavoriteItems", "FavoriteItems", itemId, favorite, cancellationToken);

    // POST sets the flag, DELETE clears it. 10.9+ routes are "UserPlayedItems/{id}?userId="; older servers have
    // "Users/{userId}/PlayedItems/{id}".
    private async Task SetUserFlagAsync(string route, string legacyRoute, string itemId, bool value, CancellationToken cancellationToken)
    {
        var session = _session.Require();
        var method = value ? HttpMethod.Post : HttpMethod.Delete;
        try
        {
            await SendAsync<object>(method, session.Server.Address, $"{route}/{itemId}?userId={session.User.Id}", session.AccessToken, null, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            await SendAsync<object>(method, session.Server.Address, $"Users/{session.User.Id}/{legacyRoute}/{itemId}", session.AccessToken, null, cancellationToken);
        }
    }

    /// <summary>
    /// Library roots list their main item type recursively (like the web client), so folder layouts on disk do not matter;
    /// everything else lists its direct children in episode/track order, unless the options ask for another order.
    /// </summary>
    internal static string ChildrenQuery(MediaItem parent, string userId, ItemListOptions? options = null)
    {
        var query = $"userId={userId}&parentId={parent.Id}&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1";
        var recursiveType = RecursiveTypeOf(parent);
        if (recursiveType is not null)
            query += $"&recursive=true&includeItemTypes={recursiveType}";

        if (options is not null && (recursiveType is not null || !options.IsDefault))
            query += $"&sortBy={SortBy(options.Sort)}&sortOrder={(options.Descending ? "Descending" : "Ascending")}";
        else if (recursiveType is not null)
            query += "&sortBy=SortName&sortOrder=Ascending";
        else
            query += "&sortBy=ParentIndexNumber,IndexNumber,SortName&sortOrder=Ascending";

        if (options is not null)
            query += FilterQuery(options);
        return query;
    }

    private static string? RecursiveTypeOf(MediaItem parent) =>
        parent.Type == "CollectionFolder" || parent.Type == "UserView"
            ? parent.CollectionType switch
            {
                "movies" => "Movie",
                "tvshows" => "Series",
                "music" => "MusicAlbum",
                "musicvideos" => "MusicVideo",
                _ => null,
            }
            : null;

    // Ties (and items without the value) fall back to the name, like the web client.
    internal static string SortBy(ItemSort sort) => sort switch
    {
        ItemSort.DateAdded => "DateCreated,SortName",
        ItemSort.ReleaseDate => "PremiereDate,ProductionYear,SortName",
        ItemSort.CommunityRating => "CommunityRating,SortName",
        ItemSort.Runtime => "Runtime,SortName",
        _ => "SortName",
    };

    internal static string FilterQuery(ItemListOptions options)
    {
        var filters = new List<string>();
        if (options.Played == PlayedFilter.Played)
            filters.Add("IsPlayed");
        else if (options.Played == PlayedFilter.Unplayed)
            filters.Add("IsUnplayed");
        if (options.FavoritesOnly)
            filters.Add("IsFavorite");

        var query = filters.Count > 0 ? $"&filters={string.Join(',', filters)}" : "";
        // Genre names are separated by "|" (names can contain commas).
        if (options.Genres.Count > 0)
            query += $"&genres={Uri.EscapeDataString(string.Join('|', options.Genres))}";
        return query;
    }

    internal static string GenresQuery(MediaItem library, string userId)
    {
        var query = $"userId={userId}&parentId={library.Id}&sortBy=SortName&sortOrder=Ascending";
        return RecursiveTypeOf(library) is { } type ? $"{query}&includeItemTypes={type}" : query;
    }

    internal static string LatestQuery(MediaItem library, int limit) =>
        $"parentId={library.Id}&limit={limit}&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1";

    internal static string SearchQuery(string term, IReadOnlyList<string> itemTypes, int limit, string userId) =>
        $"userId={userId}&searchTerm={Uri.EscapeDataString(term)}&recursive=true&includeItemTypes={string.Join(',', itemTypes)}&limit={limit}" +
        $"&sortBy=SortName&sortOrder=Ascending&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1";

    internal static string FavoritesQuery(IReadOnlyList<string> itemTypes, string userId) =>
        $"userId={userId}&filters=IsFavorite&recursive=true&includeItemTypes={string.Join(',', itemTypes)}" +
        $"&sortBy=SortName&sortOrder=Ascending&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1";

    // ---- URLs ------------------------------------------------------------------------------------------------

    public Uri GetImageUri(string itemId, ImageKind kind, string? tag, int maxHeight)
    {
        var session = _session.Require();
        var tagQuery = string.IsNullOrEmpty(tag) ? "" : $"&tag={Uri.EscapeDataString(tag)}";
        return new Uri(session.Server.Address, $"Items/{itemId}/Images/{kind}?maxHeight={maxHeight}&quality=90{tagQuery}");
    }

    public Uri GetUserImageUri(ServerInfo server, UserInfo user, int maxHeight)
    {
        var tagQuery = string.IsNullOrEmpty(user.PrimaryImageTag) ? "" : $"&tag={Uri.EscapeDataString(user.PrimaryImageTag)}";
        return new Uri(server.Address, $"Users/{user.Id}/Images/Primary?maxHeight={maxHeight}&quality=90{tagQuery}");
    }

    public Uri GetStreamUri(MediaItem item, string? mediaSourceId = null)
    {
        var session = _session.Require();
        var kind = item.MediaType == "Audio" ? "Audio" : "Videos";
        // static=true: send the original file (direct play). The token goes in the query because the player
        // cannot send headers; LibVlcEngine redacts it from logs.
        return new Uri(session.Server.Address,
            $"{kind}/{item.Id}/stream?static=true&mediaSourceId={mediaSourceId ?? item.Id}&deviceId={Uri.EscapeDataString(_identity.DeviceId)}&api_key={Uri.EscapeDataString(session.AccessToken)}");
    }

    public Uri GetSubtitleUri(MediaItem item, string mediaSourceId, MediaStreamInfo stream)
    {
        var session = _session.Require();
        return new Uri(session.Server.Address,
            $"Videos/{item.Id}/{mediaSourceId}/Subtitles/{stream.Index}/0/Stream.{SubtitleFormat(stream.Codec)}?api_key={Uri.EscapeDataString(session.AccessToken)}");
    }

    /// <summary>Text formats VLC reads natively are served as is; everything else is converted to SubRip by the server.</summary>
    internal static string SubtitleFormat(string? codec) => codec?.ToLowerInvariant() switch
    {
        "ass" => "ass",
        "ssa" => "ssa",
        "vtt" or "webvtt" => "vtt",
        _ => "srt",
    };

    public async Task<PlaybackDetails> GetPlaybackDetailsAsync(MediaItem item, PlaybackRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new PlaybackRequest();
        var session = _session.Require();
        var info = await SendAsync<PlaybackInfoDto>(HttpMethod.Post, session.Server.Address,
            $"Items/{item.Id}/PlaybackInfo?userId={session.User.Id}", session.AccessToken,
            BuildPlaybackInfoBody(request, session.User.Id), cancellationToken);

        var chapters = request.IncludeChapters ? await GetChaptersAsync(item, session, cancellationToken) : [];
        return ToPlaybackDetails(item, session.Server.Address, info, chapters);
    }

    /// <summary>The body of the PlaybackInfo request, as jellyfin-web sends it: the limits and wishes plus a device profile.</summary>
    internal static Dictionary<string, object> BuildPlaybackInfoBody(PlaybackRequest request, string userId)
    {
        var bitrate = request.MaxBitrate ?? StreamingPolicy.UnlimitedBitrate;
        var body = new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["MaxStreamingBitrate"] = bitrate,
            ["EnableTranscoding"] = true,
            ["DeviceProfile"] = BuildDeviceProfile(bitrate),
        };
        if (!request.AllowDirectPlay)
        {
            body["EnableDirectPlay"] = false;
            body["EnableDirectStream"] = false;
        }
        // The server ignores the stream indexes unless it is told which file version they belong to.
        if (request.MediaSourceId is { Length: > 0 } sourceId)
            body["MediaSourceId"] = sourceId;
        if (request.AudioStreamIndex is { } audio)
            body["AudioStreamIndex"] = audio;
        if (request.SubtitleStreamIndex is { } subtitle)
            body["SubtitleStreamIndex"] = subtitle;
        return body;
    }

    /// <summary>
    /// What the player can take. Lumen plays anything the file holds directly (VLC decodes almost any format and draws
    /// embedded subtitles itself), so the direct play profile has no limits; the transcoding profile is the HLS the
    /// server falls back to: MPEG-TS segments of H.264/HEVC and AAC/AC-3, subtitles delivered as files where they
    /// are text (picture-based ones are burned in by the server).
    /// </summary>
    internal static object BuildDeviceProfile(int maxBitrate) => new
    {
        Name = "Lumen",
        MaxStreamingBitrate = maxBitrate,
        DirectPlayProfiles = new object[] { new { Type = "Video" }, new { Type = "Audio" } },
        TranscodingProfiles = new object[]
        {
            new
            {
                Type = "Video",
                Container = "ts",
                Protocol = "hls",
                Context = "Streaming",
                VideoCodec = "h264,hevc",
                AudioCodec = "aac,mp3,ac3,eac3",
                MaxAudioChannels = "6",
                MinSegments = 1,
                BreakOnNonKeyFrames = true,
            },
        },
        SubtitleProfiles = new[] { "srt", "ass", "ssa", "vtt" }.Select(f => new { Format = f, Method = "External" })
            .Concat(new[] { "srt", "subrip", "ass", "ssa", "vtt", "pgssub", "dvdsub", "dvbsub" }.Select(f => new { Format = f, Method = "Embed" }))
            .ToArray(),
    };

    /// <summary>Reads the server's answer: whether to play the file or a conversion, and the conversion's URL.</summary>
    internal static PlaybackDetails ToPlaybackDetails(MediaItem item, Uri serverAddress, PlaybackInfoDto? info, IReadOnlyList<ChapterInfo> chapters)
    {
        var source = info?.MediaSources.FirstOrDefault();
        if (source is null)
            return PlaybackDetails.Fallback(item) with { Chapters = chapters };

        var transcodeUri = ResolveTranscodingUrl(serverAddress, source.TranscodingUrl);
        var directPlay = source.SupportsDirectPlay ?? true;
        var directStream = source.SupportsDirectStream ?? false;
        return new PlaybackDetails(
            source.Id ?? item.Id,
            info!.PlaySessionId,
            source.MediaStreams.Select(s => s.ToModel()).ToList(),
            source.DefaultAudioStreamIndex,
            source.DefaultSubtitleStreamIndex,
            chapters)
        {
            Method = transcodeUri is not null ? PlayMethod.Transcode : directPlay ? PlayMethod.DirectPlay : PlayMethod.DirectStream,
            TranscodeUri = transcodeUri,
            TranscodeReasons = transcodeUri is null ? [] : QueryValue(transcodeUri, "TranscodeReasons")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            TranscodeAudioStreamIndex = transcodeUri is null ? null : IntQueryValue(transcodeUri, "AudioStreamIndex"),
            // The server burns the subtitle in when it says "Encode"; text subtitles are delivered as files instead.
            BurnedSubtitleStreamIndex = transcodeUri is not null && QueryValue(transcodeUri, "SubtitleMethod") == "Encode"
                ? IntQueryValue(transcodeUri, "SubtitleStreamIndex") : null,
            Playable = transcodeUri is not null || directPlay || directStream,
            TranscodingAllowed = source.SupportsTranscoding ?? true,
        };
    }

    /// <summary>The server sends a path ("/videos/{id}/master.m3u8?..."); the player needs it under the server's address.</summary>
    internal static Uri? ResolveTranscodingUrl(Uri serverAddress, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return absolute;
        // Not Uri(base, relative): a rooted path would drop a base path such as /jellyfin.
        return new Uri(serverAddress.AbsoluteUri.TrimEnd('/') + "/" + url.TrimStart('/'));
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return null;
    }

    private static int? IntQueryValue(Uri uri, string name) =>
        int.TryParse(QueryValue(uri, name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    public async Task<IReadOnlyList<Core.Playback.MediaSegment>> GetMediaSegmentsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        try
        {
            var result = await SendAsync<MediaSegmentsResultDto>(HttpMethod.Get, session.Server.Address,
                $"MediaSegments/{item.Id}", session.AccessToken, null, cancellationToken);
            return ParseSegments(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return []; // servers before 10.10 have no media segments API
        }
    }

    internal static IReadOnlyList<Core.Playback.MediaSegment> ParseSegments(MediaSegmentsResultDto? result) =>
        result?.Items
            .Where(s => s.EndTicks > s.StartTicks)
            .Select(s => new Core.Playback.MediaSegment(
                Enum.TryParse<Core.Playback.SegmentType>(s.Type, ignoreCase: true, out var type) ? type : Core.Playback.SegmentType.Unknown,
                TimeSpan.FromTicks(s.StartTicks),
                TimeSpan.FromTicks(s.EndTicks)))
            .OrderBy(s => s.Start)
            .ToList() ?? [];

    private async Task<IReadOnlyList<ChapterInfo>> GetChaptersAsync(MediaItem item, SessionInfo session, CancellationToken cancellationToken)
    {
        try
        {
            var details = await GetWithFallbackAsync<ItemChaptersDto>(session,
                $"Items/{item.Id}?userId={session.User.Id}&fields=Chapters",
                $"Users/{session.User.Id}/Items/{item.Id}?fields=Chapters", cancellationToken);
            return details?.Chapters?.Select((c, i) => new ChapterInfo(c.Name ?? $"Chapter {i + 1}", TimeSpan.FromTicks(c.StartPositionTicks))).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return []; // chapters are optional
        }
    }

    // ---- Playback reporting ----------------------------------------------------------------------------------

    public Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken = default) =>
        ReportAsync("Sessions/Playing", report, cancellationToken);

    public Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default) =>
        ReportAsync("Sessions/Playing/Progress", report, cancellationToken);

    public Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default) =>
        ReportAsync("Sessions/Playing/Stopped", report, cancellationToken);

    public async Task StopTranscodingAsync(string playSessionId, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        await SendAsync<object>(HttpMethod.Delete, session.Server.Address,
            $"Videos/ActiveEncodings?deviceId={Uri.EscapeDataString(_identity.DeviceId)}&playSessionId={Uri.EscapeDataString(playSessionId)}",
            session.AccessToken, null, cancellationToken);
    }

    private async Task ReportAsync(string path, PlaybackReport report, CancellationToken cancellationToken)
    {
        var session = _session.Require();
        await SendAsync<object>(HttpMethod.Post, session.Server.Address, path, session.AccessToken, new
        {
            ItemId = report.ItemId,
            MediaSourceId = report.MediaSourceId,
            PlaySessionId = report.PlaySessionId,
            PositionTicks = report.Position.Ticks,
            IsPaused = report.IsPaused,
            CanSeek = true,
            PlayMethod = report.Method.ToString(),
        }, cancellationToken);
    }

    // ---- Plumbing --------------------------------------------------------------------------------------------

    private async Task<T?> GetWithFallbackAsync<T>(SessionInfo session, string path, string fallbackPath, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<T>(HttpMethod.Get, session.Server.Address, path, session.AccessToken, null, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await SendAsync<T>(HttpMethod.Get, session.Server.Address, fallbackPath, session.AccessToken, null, cancellationToken);
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, Uri baseUri, string path, string? token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.TryAddWithoutValidation("Authorization", _identity.AuthorizationHeader(token));
        request.Headers.Accept.ParseAdd("application/json");
        // Buffered bodies with a Content-Length, like jellyfin-web: reverse proxies and tunnels in front of
        // Jellyfin do not all accept chunked request bodies. POSTs without a body still send "Content-Length: 0".
        if (body is not null)
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        else if (method == HttpMethod.Post)
            request.Content = new StringContent("");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} from {path.Split('?')[0]}", null, response.StatusCode);

        if (typeof(T) == typeof(object) || response.StatusCode == HttpStatusCode.NoContent)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(Json.Options, cancellationToken).ConfigureAwait(false);
    }

    private static SessionInfo ToSession(ServerInfo server, AuthenticationResultDto? result)
    {
        if (result?.User is null || string.IsNullOrEmpty(result.AccessToken))
            throw new AuthenticationException("The server did not return a session.");
        return new SessionInfo(server, result.User.ToModel(), result.AccessToken);
    }

    private static IReadOnlyList<MediaItem> ToModels(ItemsResultDto? result) =>
        result?.Items.Select(i => i.ToModel()).ToList() ?? [];

    internal static string DescribeConnectionError(string input, Exception? error)
    {
        var root = error;
        while (root?.InnerException is not null)
            root = root.InnerException;

        return root switch
        {
            System.Security.Authentication.AuthenticationException or System.Security.Cryptography.CryptographicException =>
                $"Could not connect to {input} securely: the server's HTTPS certificate is not trusted. Use http:// or a trusted certificate.",
            System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound } =>
                $"Could not find a server called {input}. Check the address.",
            _ when error is ServerConnectionException => $"Could not connect to {input}. {error.Message}",
            _ => $"Could not connect to a Jellyfin server at {input}. Check the address and that the server is running.",
        };
    }
}
