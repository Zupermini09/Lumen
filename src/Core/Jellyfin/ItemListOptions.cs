using System.Text.Json.Serialization;

namespace Lumen.Core.Jellyfin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemSort
{
    Name,
    DateAdded,
    ReleaseDate,
    CommunityRating,
    Runtime,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayedFilter
{
    Any,
    Unplayed,
    Played,
}

/// <summary>How a library listing is sorted and filtered (the sort and filter bar of the library page).</summary>
public sealed record ItemListOptions
{
    public ItemSort Sort { get; init; } = ItemSort.Name;
    public bool Descending { get; init; }
    public PlayedFilter Played { get; init; } = PlayedFilter.Any;
    public bool FavoritesOnly { get; init; }

    /// <summary>Only items with at least one of these genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>True when no filter is set (sorting does not count).</summary>
    [JsonIgnore]
    public bool HasFilters => Played != PlayedFilter.Any || FavoritesOnly || Genres.Count > 0;

    /// <summary>The listing a library gets when nothing was chosen: by name, everything.</summary>
    [JsonIgnore]
    public bool IsDefault => Sort == ItemSort.Name && !Descending && !HasFilters;
}
