namespace Lumen.UI.ViewModels;

/// <summary>A heading with the item types listed under it (search results and favourites).</summary>
public sealed record ItemGroup(string Title, IReadOnlyList<string> Types);

public static class ItemGroups
{
    /// <summary>In display order. Artists, people, books and photos have no pages of their own yet.</summary>
    public static readonly IReadOnlyList<ItemGroup> All =
    [
        new("Movies", ["Movie"]),
        new("Shows", ["Series"]),
        new("Episodes", ["Episode"]),
        new("Collections", ["BoxSet"]),
        new("Playlists", ["Playlist"]),
        new("Albums", ["MusicAlbum"]),
        new("Songs", ["Audio"]),
        new("Videos", ["MusicVideo", "Video"]),
    ];

    public static readonly IReadOnlyList<string> AllTypes = All.SelectMany(g => g.Types).ToList();

    public static ItemGroup? Of(string type) => All.FirstOrDefault(g => g.Types.Contains(type));
}
