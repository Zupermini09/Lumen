using System.Diagnostics;
using Lumen.Core;

namespace Lumen.Platform.Secrets;

/// <summary>
/// The freedesktop Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> command from libsecret.
/// The secret is passed on stdin, never on the command line. Throws when secret-tool or a keyring is unavailable,
/// so <see cref="FallbackSecretStore"/> switches to the file store.
/// </summary>
internal sealed class LibSecretStore : ISecretStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string? Get(string key)
    {
        var (exitCode, output) = Run(["lookup", "service", "lumen", "key", key], stdin: null);
        // secret-tool exits 1 with no output when nothing is stored under the attributes.
        return exitCode == 0 && output.Length > 0 ? output : null;
    }

    public void Set(string key, string secret)
    {
        var (exitCode, _) = Run(["store", "--label=Lumen access token", "service", "lumen", "key", key], stdin: secret);
        if (exitCode != 0)
            throw new InvalidOperationException($"secret-tool store failed with exit code {exitCode}.");
    }

    public void Remove(string key) => Run(["clear", "service", "lumen", "key", key], stdin: null);

    private static (int ExitCode, string Output) Run(string[] args, string? stdin)
    {
        var info = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("secret-tool could not be started.");
        if (stdin is not null)
            process.StandardInput.Write(stdin);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill();
            throw new TimeoutException("secret-tool did not respond.");
        }

        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0 && error.Contains("Cannot autolaunch D-Bus", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No Secret Service available (no D-Bus session).");
        return (process.ExitCode, output.Result);
    }
}
