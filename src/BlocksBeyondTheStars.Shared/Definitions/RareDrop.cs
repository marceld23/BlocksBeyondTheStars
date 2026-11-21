// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One rare drop of an <see cref="EnemyTier"/> (#3894, <c>rareLoot</c> in <c>data/enemies.json</c>): an item an enemy
/// carries only now and then, on top of its fixed <see cref="EnemyTier.Loot"/>. The server rolls each entry once, when
/// it builds the enemy — so the drop is part of the enemy's loot from then on and every loot path (the backpack, the
/// ground, a sentry's kill) hands it out like the rest.
/// </summary>
public sealed class RareDrop
{
    public string Item { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    /// <summary>The chance per enemy, 0 (never) .. 1 (always).</summary>
    public float Chance { get; set; }

    /// <summary>Whether the drop falls for <paramref name="roll"/>, a number in [0, 1).</summary>
    public bool Falls(double roll) => Chance > 0f && Count > 0 && roll < Chance;
}
