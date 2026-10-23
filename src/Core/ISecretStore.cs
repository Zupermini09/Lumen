namespace Lumen.Core;

/// <summary>Stores small secrets (access tokens) in the OS credential store where possible.</summary>
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string secret);
    void Remove(string key);
}
