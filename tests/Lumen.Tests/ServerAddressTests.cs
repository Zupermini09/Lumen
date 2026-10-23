using Lumen.Core.Jellyfin;

namespace Lumen.Tests;

public class ServerAddressTests
{
    private static string[] Candidates(string input) => ServerAddress.Candidates(input).Select(u => u.ToString()).ToArray();

    [Theory]
    [InlineData("http://192.168.1.10:8096", "http://192.168.1.10:8096/")]
    [InlineData("https://media.example.org", "https://media.example.org/")]
    [InlineData("https://media.example.org/jellyfin/", "https://media.example.org/jellyfin/")]
    [InlineData("  HTTP://Example.org:8096  ", "http://example.org:8096/")]
    public void Explicit_scheme_is_used_as_is(string input, string expected) =>
        Assert.Equal([expected], Candidates(input));

    [Fact]
    public void Host_without_port_tries_https_then_default_http_port_then_http() =>
        Assert.Equal(["https://media.example.org/", "http://media.example.org:8096/", "http://media.example.org/"],
            Candidates("media.example.org"));

    [Fact]
    public void Default_http_port_tries_http_first() =>
        Assert.Equal(["http://192.168.1.10:8096/", "https://192.168.1.10:8096/"], Candidates("192.168.1.10:8096"));

    [Fact]
    public void Other_port_tries_https_first() =>
        Assert.Equal(["https://nas.local:8920/", "http://nas.local:8920/"], Candidates("nas.local:8920"));

    [Fact]
    public void Port_80_is_kept_when_typed() =>
        Assert.Equal(["https://nas.local:80/", "http://nas.local/"], Candidates("nas.local:80"));

    [Fact]
    public void Base_path_is_kept() =>
        Assert.Equal(["https://example.org/jellyfin/", "http://example.org:8096/jellyfin/", "http://example.org/jellyfin/"],
            Candidates("example.org/jellyfin"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a host")]
    [InlineData("ftp://example.org")]
    [InlineData("http://")]
    public void Invalid_input_gives_no_candidates(string input) => Assert.Empty(Candidates(input));
}
