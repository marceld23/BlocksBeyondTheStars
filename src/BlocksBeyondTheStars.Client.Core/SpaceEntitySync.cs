// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// The flight view's entity bookkeeping, kept Unity-free so it is tested headless (#2708). The view keeps one model per
/// space entity that has one, keyed by entity id, and rebuilds the set of ids in every snapshot. A drop that was pulled
/// in, a rescued life pod or a destroyed hostile has no "gone" message of its own — it is simply missing from the next
/// snapshot, and its model must go with it.
/// </summary>
public static class SpaceEntitySync
{
    /// <summary>Fills <paramref name="stale"/> (cleared first) with every id that has a view but is not in the snapshot's
    /// <paramref name="seen"/> ids, and returns how many there are. Runs every frame without allocating (both enumerators
    /// are structs, the list is reused). It must not be skipped by comparing counts: asteroids, debris and hull-backed
    /// stations or wrecks are in the snapshot without a view, so "more views than snapshot ids" is no test for a stale
    /// view — that rule left collected drops on screen as ghosts.</summary>
    public static int CollectStale<TView>(Dictionary<string, TView> views, HashSet<string> seen, List<string> stale)
    {
        stale.Clear();
        foreach (var kv in views)
        {
            if (!seen.Contains(kv.Key))
            {
                stale.Add(kv.Key);
            }
        }

        return stale.Count;
    }
}
