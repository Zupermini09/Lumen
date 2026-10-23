using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using Lumen.Core.Jellyfin;
using Serilog;
using AuthenticationException = Lumen.Core.Jellyfin.AuthenticationException;

namespace Lumen.Jellyfin;

public sealed class JellyfinClient : IJellyfinClient
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);
    private const string ItemFields = "PrimaryImageAspectRatio,MediaSourceCount";

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

    public async Task<IReadOnlyList<MediaItem>> GetChildrenAsync(MediaItem parent, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        var result = await SendAsync<ItemsResultDto>(HttpMethod.Get, session.Server.Address,
            "Items?" + ChildrenQuery(parent, session.User.Id), session.AccessToken, null, cancellationToken);
        return ToModels(result);
    }

    /// <summary>
    /// Library roots list their main item type recursively (like the web client), so folder layouts on disk do not matter;
    /// everything else lists its direct children in episode/track order.
    /// </summary>
    internal static string ChildrenQuery(MediaItem parent, string userId)
    {
        var query = $"userId={userId}&parentId={parent.Id}&fields={ItemFields}&enableImageTypes=Primary,Thumb&imageTypeLimit=1";
        var recursiveType = parent.Type == "CollectionFolder" || parent.Type == "UserView"
            ? parent.CollectionType switch
            {
                "movies" => "Movie",
                "tvshows" => "Series",
                "music" => "MusicAlbum",
                "musicvideos" => "MusicVideo",
                _ => null,
            }
            : null;

        return recursiveType is not null
            ? $"{query}&recursive=true&includeItemTypes={recursiveType}&sortBy=SortName&sortOrder=Ascending"
            : $"{query}&sortBy=ParentIndexNumber,IndexNumber,SortName&sortOrder=Ascending";
    }

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

    public async Task<PlaybackDetails> GetPlaybackDetailsAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        var session = _session.Require();
        // A permissive profile: Lumen plays everything directly (VLC decodes almost any format), so the server only
        // has to tell us the streams and the user's default track choices.
        var info = await SendAsync<PlaybackInfoDto>(HttpMethod.Post, session.Server.Address,
            $"Items/{item.Id}/PlaybackInfo?userId={session.User.Id}", session.AccessToken, new
            {
                DeviceProfile = new
                {
                    Name = "Lumen",
                    DirectPlayProfiles = new object[] { new { Type = "Video" }, new { Type = "Audio" } },
                },
            }, cancellationToken);

        var source = info?.MediaSources.FirstOrDefault();
        var chapters = await GetChaptersAsync(item, session, cancellationToken);
        if (source is null)
            return PlaybackDetails.Fallback(item) with { Chapters = chapters };

        return new PlaybackDetails(
            source.Id ?? item.Id,
            info!.PlaySessionId,
            source.MediaStreams.Select(s => s.ToModel()).ToList(),
            source.DefaultAudioStreamIndex,
            source.DefaultSubtitleStreamIndex,
            chapters);
    }

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
            PlayMethod = "DirectPlay",
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
