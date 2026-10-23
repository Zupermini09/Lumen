using Lumen.Core.Jellyfin;

namespace Lumen.Jellyfin;

/// <summary>Shared by the client (which needs the token) and the session service (which sets it).</summary>
public sealed class SessionHolder
{
    public SessionInfo? Current { get; set; }

    public SessionInfo Require() =>
        Current ?? throw new InvalidOperationException("Not signed in to a Jellyfin server.");
}
