// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A bounded memo of what one hotspot cell rolled (terrain generation 21, #2332). A spectacle family's cluster — the
/// stems and balconies of a pillar island, the arcs of an arch field, the tables of a bridge mesa — is a pure
/// function of the cell hash, yet every column of the cell would re-derive it (the #712 lesson of the worm cache:
/// dozens of rolls per column, half the map). Static like the worm cache (the client bakes previews with fresh
/// generators, tests spin up hundreds), one lock, a soft cap that drops everything instead of evicting — a cell's
/// rolls are a few hundred bytes. The builder runs outside the lock: it is deterministic, so two racing builders of
/// one key produce equal values and the second write is harmless.
/// </summary>
/// <typeparam name="T">The cell's rolled geometry (an immutable array or record).</typeparam>
internal sealed class CellCache<T> where T : class
{
    private readonly Dictionary<(long Seed, ulong Hash), T> _entries = new();
    private readonly object _lock = new();
    private readonly int _cap;

    public CellCache(int cap = 512)
    {
        _cap = cap;
    }

    /// <summary>The cell's value, built on the first ask. <paramref name="state"/> travels into the builder so a
    /// static lambda can be used and no closure is allocated per column.</summary>
    public T GetOrAdd<TState>(long seed, ulong hash, TState state, Func<TState, ulong, T> build)
    {
        var key = (seed, hash);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var value = build(state, hash);
        lock (_lock)
        {
            if (_entries.Count >= _cap)
            {
                _entries.Clear();
            }

            _entries[key] = value;
        }

        return value;
    }
}
