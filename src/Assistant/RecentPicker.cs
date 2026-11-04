using System;
using System.Collections.Generic;
using System.Linq;

namespace Webi.Assistant;

/// <summary>Picks random items while avoiding the last few picked (by key), so lines don't repeat back to back.</summary>
public sealed class RecentPicker
{
    readonly Queue<string> _recent = new();
    readonly int _memory;
    readonly Random _rng;

    /// <param name="memory">How many recent picks to avoid.</param>
    public RecentPicker(int memory, Random? rng = null)
    {
        _memory = memory;
        _rng = rng ?? Random.Shared;
    }

    /// <summary>A random item not picked recently (any item once all were); <paramref name="key"/> identifies an item.</summary>
    public T Pick<T>(IReadOnlyList<T> items, Func<T, string> key)
    {
        if (items.Count == 0) throw new ArgumentException("Nothing to pick from.", nameof(items));
        var fresh = items.Where(i => !_recent.Contains(key(i))).ToList();
        IReadOnlyList<T> pool = fresh.Count > 0 ? fresh : items;
        var item = pool[_rng.Next(pool.Count)];
        _recent.Enqueue(key(item));
        while (_recent.Count > _memory) _recent.Dequeue();
        return item;
    }

    public string Pick(IReadOnlyList<string> lines) => Pick(lines, l => l);
}
