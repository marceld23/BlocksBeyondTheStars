// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One kind of hostile (#2971) from <c>data/enemies.json</c>: the space drone, the UFO, the cruiser, the Guardian machines on
/// the ground, the robbers and their raider ship. The server builds every such hostile from its tier, and the Codex's
/// "Enemies" chapter shows the same numbers — one source, so a rebalance can never leave the Codex behind. Hostile wildlife
/// is not here: it is generated per world and belongs to the scanner's Discoveries.
/// </summary>
public sealed class EnemyDefinition
{
    public string Key { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>A short "how to beat it" line for the Codex.</summary>
    public string TipKey { get; set; } = string.Empty;

    /// <summary>Where it is met: <see cref="EnemyWhere.Planet"/> (on foot) or <see cref="EnemyWhere.Space"/> (flying).</summary>
    public string Where { get; set; } = EnemyWhere.Planet;

    /// <summary>Its strengths by tier name — every enemy has <see cref="EnemyTiers.Normal"/>; some have a stronger
    /// <see cref="EnemyTiers.Elite"/> (the Guardian gauntlet) or a <see cref="EnemyTiers.Camp"/> variant (a camp guard).</summary>
    public Dictionary<string, EnemyTier> Tiers { get; set; } = new();

    /// <summary>The named tier, else the normal one, else an empty tier (never null).</summary>
    public EnemyTier Tier(string name)
        => Tiers.TryGetValue(name, out var tier) ? tier
            : Tiers.TryGetValue(EnemyTiers.Normal, out var normal) ? normal
            : new EnemyTier();
}

/// <summary>One strength of an <see cref="EnemyDefinition"/>.</summary>
public sealed class EnemyTier
{
    public float Hull { get; set; } = 30f;

    /// <summary>The most hull this tier can reach when it scales (a bigger raider ship carries more); 0 = always
    /// <see cref="Hull"/>.</summary>
    public float HullMax { get; set; }

    /// <summary>Damage per second while it is in reach and attacking.</summary>
    public float DamagePerSecond { get; set; }

    public List<ItemAmount> Loot { get; set; } = new();
}

/// <summary>The tier names of <see cref="EnemyDefinition.Tiers"/>.</summary>
public static class EnemyTiers
{
    public const string Normal = "normal";
    public const string Elite = "elite";
    public const string Camp = "camp";
}

/// <summary>The values of <see cref="EnemyDefinition.Where"/>.</summary>
public static class EnemyWhere
{
    public const string Planet = "planet";
    public const string Space = "space";
}

/// <summary>
/// The enemy keys and the numbers that shipped before <c>data/enemies.json</c> (#2971). A data folder without the file —
/// or without one of the keys — plays with these, so a minimal test content still fights with the old balance; a
/// content test holds the shipped file equal to them.
/// </summary>
public static class EnemyCatalog
{
    public const string SpaceDrone = "space_drone";
    public const string Ufo = "ufo";
    public const string Cruiser = "cruiser";
    public const string BanditShip = "bandit_ship";
    public const string ScanDrone = "scan_drone";
    public const string GuardianRobot = "guardian_robot";
    public const string Hunter = "hunter";
    public const string Bandit = "bandit";
    public const string BanditGunner = "bandit_gunner";

    /// <summary>Every enemy in the Codex's order: space first (the player meets drones on the first flight), then the
    /// ground.</summary>
    public static IReadOnlyList<EnemyDefinition> Defaults() => new[]
    {
        Make(SpaceDrone, EnemyWhere.Space, ("normal", 40f, 0f, 5f, Loot(("data_fragment", 1))), ("elite", 70f, 0f, 7f, Loot(("data_fragment", 2)))),
        Make(Ufo, EnemyWhere.Space, ("normal", 40f, 0f, 4f, Loot(("data_fragment", 3))), ("elite", 95f, 0f, 8f, Loot(("data_fragment", 4)))),
        Make(Cruiser, EnemyWhere.Space, ("normal", 260f, 0f, 10f, Loot(("data_fragment", 8)))),
        Make(BanditShip, EnemyWhere.Space, ("normal", 55f, 120f, 5f, Loot(("data_fragment", 2), ("titanium_plate", 2)))),
        Make(ScanDrone, EnemyWhere.Planet, ("normal", 25f, 0f, 2f, Loot(("carbon", 2)))),
        Make(GuardianRobot, EnemyWhere.Planet, ("normal", 30f, 0f, 4f, Loot(("carbon", 2)))),
        Make(Hunter, EnemyWhere.Planet, ("normal", 60f, 0f, 6f, Loot(("carbon", 2)))),
        Make(Bandit, EnemyWhere.Planet, ("normal", 35f, 0f, 5f, Loot(("iron_plate", 2))), ("camp", 35f, 0f, 5f, Loot(("iron_plate", 2), ("carbon", 2)))),
        Make(BanditGunner, EnemyWhere.Planet, ("normal", 35f, 0f, 3f, Loot(("iron_plate", 2))), ("camp", 35f, 0f, 3f, Loot(("iron_plate", 2), ("carbon", 2)))),
    };

    private static List<ItemAmount> Loot(params (string Item, int Count)[] loot)
    {
        var list = new List<ItemAmount>();
        foreach (var (item, count) in loot)
        {
            list.Add(new ItemAmount(item, count));
        }

        return list;
    }

    private static EnemyDefinition Make(string key, string where, params (string Name, float Hull, float HullMax, float Dps, List<ItemAmount> Loot)[] tiers)
    {
        var def = new EnemyDefinition
        {
            Key = key,
            NameKey = "enemy." + key + ".name",
            DescriptionKey = "enemy." + key + ".desc",
            TipKey = "enemy." + key + ".tip",
            Where = where,
        };
        foreach (var t in tiers)
        {
            def.Tiers[t.Name] = new EnemyTier { Hull = t.Hull, HullMax = t.HullMax, DamagePerSecond = t.Dps, Loot = t.Loot };
        }

        return def;
    }
}
