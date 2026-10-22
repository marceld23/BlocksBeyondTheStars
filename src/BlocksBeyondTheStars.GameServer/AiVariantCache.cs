// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2590/#2591: a bounded, thread-safe cache of up to <see cref="MaxVariants"/> variants per key. Keys are evicted
/// first-in-first-out once more than <see cref="Capacity"/> are held, so the cache can never grow without bound on a
/// long-running world. Variants are picked at random so an NPC doesn't repeat one sentence forever.
/// </summary>
internal sealed class AiVariantCache<T>
    where T : class
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly Func<T, T, bool> _same;
    private readonly Random _rng;

    public AiVariantCache(int capacity, int maxVariants, Func<T, T, bool> same, int seed = 0x5EED)
    {
        Capacity = Math.Max(1, capacity);
        MaxVariants = Math.Max(1, maxVariants);
        _same = same;
        _rng = new Random(seed);
    }

    /// <summary>Most keys held at once.</summary>
    public int Capacity { get; }

    /// <summary>Most variants per key.</summary>
    public int MaxVariants { get; }

    /// <summary>Keys currently held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>How many variants a key holds (0 = none).</summary>
    public int VariantCount(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) ? e.Variants.Count : 0;
        }
    }

    /// <summary>A random variant of the key, or null.</summary>
    public T? Pick(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) && e.Variants.Count > 0 ? e.Variants[_rng.Next(e.Variants.Count)] : null;
        }
    }

    /// <summary>A deterministic variant of the key (<paramref name="seed"/> modulo the variant count), or null.</summary>
    public T? PickAt(string key, int seed)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) && e.Variants.Count > 0
                ? e.Variants[(seed & int.MaxValue) % e.Variants.Count]
                : null;
        }
    }

    /// <summary>All variants of the key (a copy).</summary>
    public IReadOnlyList<T> Variants(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) ? e.Variants.ToArray() : Array.Empty<T>();
        }
    }

    /// <summary>Adds a variant. False when it equals one already held or the key is full. Evicts the oldest key when
    /// the cache is over capacity.</summary>
    public bool Add(string key, T value)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var e))
            {
                e = new Entry(_order.AddLast(key));
                _map[key] = e;
                while (_map.Count > Capacity && _order.First is { } oldest)
                {
                    _order.RemoveFirst();
                    _map.Remove(oldest.Value);
                }
            }

            if (e.Variants.Count >= MaxVariants)
            {
                return false;
            }

            foreach (var v in e.Variants)
            {
                if (_same(v, value))
                {
                    return false;
                }
            }

            e.Variants.Add(value);
            return true;
        }
    }

    /// <summary>Drops every key matching <paramref name="match"/>; returns how many went.</summary>
    public int RemoveWhere(Func<string, bool> match)
    {
        lock (_gate)
        {
            var gone = _map.Keys.Where(match).ToList();
            foreach (var key in gone)
            {
                _order.Remove(_map[key].Node);
                _map.Remove(key);
            }

            return gone.Count;
        }
    }

    private sealed class Entry
    {
        public Entry(LinkedListNode<string> node) => Node = node;

        public LinkedListNode<string> Node { get; }

        public List<T> Variants { get; } = new();
    }
}
