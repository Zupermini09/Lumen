using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

/// <summary>One entry of the sort list.</summary>
public sealed record SortChoice(ItemSort Sort, string Label)
{
    public static readonly IReadOnlyList<SortChoice> All =
    [
        new(ItemSort.Name, "Name"),
        new(ItemSort.DateAdded, "Date added"),
        new(ItemSort.ReleaseDate, "Release date"),
        new(ItemSort.CommunityRating, "Community rating"),
        new(ItemSort.Runtime, "Runtime"),
    ];
}

/// <summary>One entry of the watched-state list.</summary>
public sealed record PlayedChoice(PlayedFilter Filter, string Label)
{
    public static readonly IReadOnlyList<PlayedChoice> All =
    [
        new(PlayedFilter.Any, "All"),
        new(PlayedFilter.Unplayed, "Unplayed"),
        new(PlayedFilter.Played, "Played"),
    ];
}

/// <summary>A genre in the filter list that can be ticked.</summary>
public sealed partial class GenreChoice(string name, bool isSelected) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}
