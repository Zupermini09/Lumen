namespace Lumen.Core.Caching;

/// <summary>
/// Thread-safe least-recently-used cache bounded by a total "cost" (e.g. bytes). Evicted values are passed to
/// <c>onEvict</c> so they can be disposed.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly long _capacity;
    private readonly Action<TValue>? _onEvict;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _lock = new();

    public LruCache(long capacity, Action<TValue>? onEvict = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _onEvict = onEvict;
    }

    public long Size { get; private set; }
    public int Count { get { lock (_lock) return _map.Count; } }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value, long cost)
    {
        var evicted = new List<TValue>();
        lock (_lock)
        {
            if (_map.Remove(key, out var existing))
            {
                _order.Remove(existing);
                Size -= existing.Value.Cost;
                evicted.Add(existing.Value.Value);
            }

            _map[key] = _order.AddFirst(new Entry(key, value, cost));
            Size += cost;

            // Always keep the newest entry, even if it alone exceeds the capacity.
            while (Size > _capacity && _order.Count > 1)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                Size -= last.Value.Cost;
                evicted.Add(last.Value.Value);
            }
        }

        if (_onEvict is not null)
            evicted.ForEach(_onEvict);
    }

    private sealed record Entry(TKey Key, TValue Value, long Cost);
}
