using Lumen.Core.Caching;

namespace Lumen.Tests;

public class LruCacheTests
{
    [Fact]
    public void Evicts_least_recently_used_entries_when_over_capacity()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(10, evicted.Add);
        cache.Add("a", "A", 4);
        cache.Add("b", "B", 4);
        Assert.True(cache.TryGet("a", out _)); // a is now most recent
        cache.Add("c", "C", 4);                // over capacity: b goes

        Assert.Equal(["B"], evicted);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(8, cache.Size);
    }

    [Fact]
    public void Replacing_a_key_evicts_the_old_value_and_updates_size()
    {
        var evicted = new List<string>();
        var cache = new LruCache<string, string>(100, evicted.Add);
        cache.Add("a", "old", 10);
        cache.Add("a", "new", 20);
        Assert.Equal(["old"], evicted);
        Assert.Equal(20, cache.Size);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal("new", value);
    }

    [Fact]
    public void Keeps_a_single_entry_larger_than_capacity()
    {
        var cache = new LruCache<string, string>(10);
        cache.Add("big", "x", 50);
        Assert.Equal(1, cache.Count);
    }
}
