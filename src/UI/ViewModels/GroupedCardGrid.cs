using System.Collections.ObjectModel;
using Lumen.Core.Jellyfin;

namespace Lumen.UI.ViewModels;

/// <summary>The heading of a group of cards.</summary>
public sealed record GroupHeaderRow(string Title, int Count) : IGridRow;

/// <summary>A titled set of cards, in one shape so its rows line up.</summary>
public sealed record CardGroup(string Title, IReadOnlyList<MediaCardViewModel> Cards);

/// <summary>
/// Groups of cards as a flat list of rows (a heading, then rows of as many cards as fit), for a virtualized list: only the
/// rows on screen get views, so a long list of favourites stays light.
/// </summary>
public sealed class GroupedCardGrid : IScrollMemory
{
    private IReadOnlyList<CardGroup> _groups = [];
    private double _width;

    /// <summary>Heading rows and rows of cards; empty until the width is known.</summary>
    public ObservableCollection<IGridRow> Rows { get; } = [];

    public double ScrollOffset { get; set; }

    public bool IsEmpty => CardCount == 0;

    public int CardCount => _groups.Sum(g => g.Cards.Count);

    /// <summary>How many cards of the given width fit in a row of the given width.</summary>
    public static int ColumnsFor(double width, double cardWidth) =>
        Math.Max(1, (int)((width + ItemsViewModel.CardSpacing) / (cardWidth + ItemsViewModel.CardSpacing)));

    /// <summary>Called by the view when its width changes.</summary>
    public void SetWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _width) < 0.5)
            return;
        _width = width;
        Rebuild();
    }

    public void SetGroups(IReadOnlyList<CardGroup> groups)
    {
        _groups = groups;
        Rebuild();
    }

    /// <summary>Makes the groups of items shown under their titles, each in its most common card shape.</summary>
    public static IReadOnlyList<CardGroup> Group(IEnumerable<MediaItem> items, IJellyfinClient client)
    {
        var byGroup = items.Where(i => ItemGroups.Of(i.Type) is not null).GroupBy(i => ItemGroups.Of(i.Type)!);
        return ItemGroups.All
            .Select(g => (Group: g, Items: byGroup.FirstOrDefault(b => b.Key == g)?.ToList() ?? []))
            .Where(g => g.Items.Count > 0)
            .Select(g => Cards(g.Group.Title, g.Items, client))
            .ToList();
    }

    public static CardGroup Cards(string title, IReadOnlyList<MediaItem> items, IJellyfinClient client)
    {
        var shape = items.GroupBy(MediaCardViewModel.ShapeFor).OrderByDescending(g => g.Count()).First().Key;
        return new CardGroup(title, items.Select(i => MediaCardViewModel.Create(i, client, shape)).ToList());
    }

    private void Rebuild()
    {
        Rows.Clear();
        if (_width <= 0)
            return;
        foreach (var group in _groups.Where(g => g.Cards.Count > 0))
        {
            Rows.Add(new GroupHeaderRow(group.Title, group.Cards.Count));
            var columns = ColumnsFor(_width, group.Cards[0].Width);
            foreach (var chunk in group.Cards.Chunk(columns))
                Rows.Add(new CardRow(chunk));
        }
    }
}
