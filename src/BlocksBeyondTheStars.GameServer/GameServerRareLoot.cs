// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The rare drops of <c>data/enemies.json</c> (#3894, <see cref="EnemyTier.RareLoot"/>): an item an enemy carries only now
/// and then — the hunter's hacking chip. Rolled once, when the enemy is built (<see cref="NewEnemy"/>), and added to its
/// loot, so every loot path hands it out like the fixed loot. The roll is a hash of the world seed, the enemy's id, the
/// item and the server's uptime: nothing a player can farm by a pattern, and the same in every test run (a test steps
/// the uptime itself).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Adds the rare drops of <paramref name="tier"/> that fall for this enemy to its loot.</summary>
    private void AddRareLoot(CombatEntity entity, EnemyTier tier)
    {
        foreach (var rare in tier.RareLoot)
        {
            if (rare.Falls(RareLootRoll(entity.Id, rare.Item)))
            {
                entity.Loot.Add(new ItemAmount(rare.Item, rare.Count));
            }
        }
    }

    /// <summary>The roll of one rare drop, in [0, 1).</summary>
    private double RareLootRoll(string entityId, string item)
    {
        long tenths = (long)(_uptime * 10.0);
        long hash = WorldGenerator.StableHash(string.Concat(
            "rare_loot:", _meta.Seed.ToString(CultureInfo.InvariantCulture), ":", entityId, ":", item, ":",
            tenths.ToString(CultureInfo.InvariantCulture)));
        return ((ulong)hash >> 40) / (double)0x1000000; // the top 24 bits: FNV's low bits mix poorly
    }

    /// <summary>Test seam: the roll <see cref="NewEnemy"/> would make for this enemy id and item right now.</summary>
    internal double RareLootRollForTest(string entityId, string item) => RareLootRoll(entityId, item);
}
