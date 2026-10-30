// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// The yield store of one base core or one player station (#2999, <see cref="WorldMetadata.BaseYields"/>): what it has
/// gathered and not yet been collected, and when it was last read. Filled lazily by
/// <see cref="Definitions.BaseRules.Accrue"/> from the wall clock — nothing ticks while nobody plays.
/// </summary>
public sealed class BaseYieldStore
{
    /// <summary>UTC unix seconds of the last read (0 = never read: the next read starts the clock).</summary>
    public long LastUnix { get; set; }

    /// <summary>The share of the next item already gathered (0..1).</summary>
    public double Progress { get; set; }

    /// <summary>How many items the store ever made — the index of the next pick, so the picks are deterministic.</summary>
    public long Produced { get; set; }

    /// <summary>What lies ready: item key → count.</summary>
    public Dictionary<string, int> Items { get; set; } = new();

    /// <summary>How many items lie ready (a method, not a property: the store is saved as JSON).</summary>
    public int CountReady() => Items.Values.Sum();
}
