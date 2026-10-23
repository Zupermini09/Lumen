using Lumen.Core;
using Lumen.Core.Jellyfin;
using Lumen.Jellyfin;
using Lumen.Platform;
using Lumen.Platform.Secrets;
using Serilog;

namespace Lumen.Tests;

public sealed class SessionAndStorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lumen-tests").FullName;
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void File_secret_store_round_trips_and_removes()
    {
        var store = new FileSecretStore(Path.Combine(_dir, "secrets"), protect: OperatingSystem.IsWindows());
        Assert.Null(store.Get("k"));
        store.Set("k", "s3cr3t");
        Assert.Equal("s3cr3t", store.Get("k"));
        store.Set("k", "changed");
        Assert.Equal("changed", store.Get("k"));
        store.Remove("k");
        Assert.Null(store.Get("k"));
    }

    [Fact]
    public void File_secret_store_does_not_write_the_secret_in_plain_text_on_windows_or_readable_by_others()
    {
        var store = new FileSecretStore(Path.Combine(_dir, "secrets"), protect: OperatingSystem.IsWindows());
        store.Set("k", "plain-token-value");
        var file = Directory.GetFiles(Path.Combine(_dir, "secrets")).Single();
        Assert.DoesNotContain("k", Path.GetFileName(file)); // key is hashed
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain("plain-token-value", File.ReadAllText(file));
        else
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public void Settings_survive_a_restart_and_a_corrupt_file_resets_to_defaults()
    {
        var first = new JsonSettingsStore(_dir, Log);
        first.Settings.LastServerId = "srv";
        first.Save();
        Assert.Equal("srv", new JsonSettingsStore(_dir, Log).Settings.LastServerId);

        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var recovered = new JsonSettingsStore(_dir, Log);
        Assert.Null(recovered.Settings.LastServerId);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.corrupt")));
    }

    [Fact]
    public async Task Sign_in_persists_the_session_and_sign_out_forgets_the_token()
    {
        var settings = new JsonSettingsStore(_dir, Log);
        var secrets = new MemorySecretStore();
        var client = new FakeClient();
        var holder = new SessionHolder();
        var sessions = new SessionService(client, holder, settings, secrets, Log);

        var server = new ServerInfo("srv", "Home", "10.11.0", new Uri("http://nas:8096/"));
        var session = new SessionInfo(server, new UserInfo("u1", "alice", null, true), "token-1");
        sessions.SignIn(session);

        Assert.Equal("token-1", secrets.Get(SessionService.TokenKey("srv", "u1")));
        Assert.Equal("alice", settings.Settings.Servers.Single().LastUserName);
        Assert.DoesNotContain("token-1", File.ReadAllText(Path.Combine(_dir, "settings.json"))); // token never in settings

        // Restart: a new service restores the session from settings + secret store.
        holder.Current = null;
        var restored = await new SessionService(client, holder, new JsonSettingsStore(_dir, Log), secrets, Log).TryRestoreAsync();
        Assert.NotNull(restored);
        Assert.Equal("token-1", restored.AccessToken);

        await sessions.SignOutAsync();
        Assert.Null(secrets.Get(SessionService.TokenKey("srv", "u1")));
        Assert.Null(holder.Current);
        Assert.True(client.SignedOut);
    }

    [Fact]
    public async Task Revoked_token_is_not_restored()
    {
        var settings = new JsonSettingsStore(_dir, Log);
        var secrets = new MemorySecretStore();
        var holder = new SessionHolder();
        var server = new ServerInfo("srv", "Home", "10.11.0", new Uri("http://nas:8096/"));
        new SessionService(new FakeClient(), holder, settings, secrets, Log)
            .SignIn(new SessionInfo(server, new UserInfo("u1", "alice", null, true), "old"));

        var restored = await new SessionService(new FakeClient { TokenValid = false }, new SessionHolder(), settings, secrets, Log).TryRestoreAsync();
        Assert.Null(restored);
        Assert.Null(secrets.Get(SessionService.TokenKey("srv", "u1")));
    }

    [Fact]
    public void Tokens_are_redacted_from_player_logs() =>
        Assert.Equal("open http://nas/Videos/1/stream?static=true&api_key=*** failed",
            Lumen.Player.LibVlcEngine.Redact("open http://nas/Videos/1/stream?static=true&api_key=abcdef123 failed"));

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string secret) => _values[key] = secret;
        public void Remove(string key) => _values.Remove(key);
    }

    private sealed class FakeClient : StubJellyfinClient
    {
        public bool TokenValid { get; init; } = true;
        public bool SignedOut { get; private set; }

        public override Task<UserInfo?> ValidateSessionAsync(SessionInfo session, CancellationToken cancellationToken = default) =>
            Task.FromResult(TokenValid ? session.User : null);

        public override Task SignOutAsync(SessionInfo session, CancellationToken cancellationToken = default)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
