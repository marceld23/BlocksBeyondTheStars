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

    /// <summary>#3096: the damage of one burst at its centre, for an enemy that attacks by bursting (the tank spider) —
    /// it falls off toward the edge (<see cref="TankSpiderRules.DamageAt"/>). 0 = it does not burst.</summary>
    public float BurstDamage { get; set; }

    /// <summary>#3756: the damage of this tier's heavy shot in space — it charges visibly
    /// (<see cref="SpaceCombatRules.HeavyChargeSeconds"/>) and a barrel roll at the right moment makes it miss.
    /// 0 = it has none.</summary>
    public float HeavyShotDamage { get; set; }

    /// <summary>#3756: seconds from one heavy shot to the start of the next charge.</summary>
    public float HeavyShotInterval { get; set; }

    /// <summary>#3758: this tier's shield in space — a ship weapon's damage goes on it first, then on the hull. It does
    /// not recharge, and an ion pulse drops it at once. 0 = none.</summary>
    public float Shield { get; set; }

    /// <summary>#3900: seconds this tier's heavy shot charges; 0 = <see cref="SpaceCombatRules.HeavyChargeSeconds"/>.</summary>
    public float HeavyShotCharge { get; set; }

    /// <summary>#3900: how close the hunted pilot must be for the charge to start (the shot still lands up to
    /// <see cref="SpaceCombatRules.HeavyReachFactor"/> times this far); 0 = <see cref="SpaceCombatRules.EngageRange"/>.</summary>
    public float HeavyShotRange { get; set; }

    /// <summary>#3900: the most of the target ship's full hull one heavy shot of this tier may take (0.4 = 40 %), and a
    /// ship above <see cref="SpaceCombatRules.HeavyLastPointShare"/> of its hull always keeps a hull point — the shot is
    /// never deadly in one hit. 0 = no cap (the classic heavy shot).</summary>
    public float HeavyShotMaxHullShare { get; set; }

    /// <summary>#3900: what the client draws for the heavy shot — empty = the classic glow,
    /// <see cref="SpaceCombatRules.HeavyStyleMegaLaser"/> = the escort cruiser's mega laser with its aiming line.</summary>
    public string HeavyShotStyle { get; set; } = string.Empty;

    /// <summary>#3900: a boss (the escort cruiser) — it has a boss bar, a second phase and the loot rule of the bosses
    /// (every pilot who fought gets a share). The four values below only matter for a boss.</summary>
    public bool Boss { get; set; }

    /// <summary>#3900: the hull share at which a boss enrages (0.5 = half hull); 0 = it never does.</summary>
    public float EnrageAtHull { get; set; }

    /// <summary>#3900: the factor on a boss's flight speed once enraged (0 = unchanged).</summary>
    public float EnrageSpeed { get; set; }

    /// <summary>#3900: seconds between two heavy shots once enraged (0 = unchanged).</summary>
    public float EnrageHeavyShotInterval { get; set; }

    /// <summary>#3900: how many space drones a boss calls once, the moment it enrages.</summary>
    public int Reinforcements { get; set; }

    public List<ItemAmount> Loot { get; set; } = new();

    /// <summary>#3894: what this tier carries only now and then, on top of <see cref="Loot"/> (<c>rareLoot</c>) — each
    /// entry is rolled once when the server builds the enemy.</summary>
    public List<RareDrop> RareLoot { get; set; } = new();
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
    public const string EscortCruiser = EscortCruiserRules.EnemyKey; // #3900: the boss variant of the cruiser
    public const string BanditShip = "bandit_ship";
    public const string HunterShuttle = "hunter_shuttle"; // #3097
    public const string ScanDrone = "scan_drone";
    public const string GuardianRobot = "guardian_robot";
    public const string Hunter = "hunter";
    public const string TankSpider = "tank_spider"; // #3096
    public const string GuardianGiant = GuardianGiantRules.EnemyKey; // #3676: the boss — its hull and the base of each loot crate
    public const string Bandit = "bandit";
    public const string BanditGunner = "bandit_gunner";

    /// <summary>#3290: the robot metal the Guardian's machines drop — the guardian set is made of it.</summary>
    public const string GuardianPlate = "guardian_plate";

    /// <summary>#3291: the dark cloth robbers drop — the robber disguise is sewn from it.</summary>
    public const string RobberCloth = "robber_cloth";

    /// <summary>#3894: the key item for the Service's old machines (a guide tower's relay console, a capsule tank) — a
    /// hunter carries one now and then (<see cref="HunterChipChance"/>), and the workshop forges it.</summary>
    public const string HackingChip = "hacking_chip";

    /// <summary>#3894: the chance that a hunter carries a hacking chip — about one hunter in twelve.</summary>
    public const float HunterChipChance = 0.08f;

    /// <summary>Every enemy in the Codex's order: space first (the player meets drones on the first flight), then the
    /// ground.</summary>
    public static IReadOnlyList<EnemyDefinition> Defaults() => new[]
    {
        Make(SpaceDrone, EnemyWhere.Space, ("normal", 40f, 0f, 5f, Loot(("data_fragment", 1))), ("elite", 70f, 0f, 7f, Loot(("data_fragment", 2)))),
        Shielded(Make(Ufo, EnemyWhere.Space, ("normal", 40f, 0f, 4f, Loot(("data_fragment", 3))), ("elite", 95f, 0f, 8f, Loot(("data_fragment", 4)))), "elite", 30f), // #3758
        // #3756: the cruiser and the raider trade part of their steady fire for a heavy shot a barrel roll can dodge —
        // about the same damage over time for a pilot who never rolls (7 + 24/8 and 3.5 + 13/9 per second).
        Shielded(Heavy(Make(Cruiser, EnemyWhere.Space, ("normal", 260f, 0f, 7f, Loot(("data_fragment", 8)))), 24f, 8f), "normal", 60f),
        MakeEscortCruiser(), // #3900: the boss — its mega laser is the heavy shot made bigger, slower and capped
        Shielded(Heavy(Make(BanditShip, EnemyWhere.Space, ("normal", 55f, 120f, 3.5f, Loot(("data_fragment", 2), ("titanium_plate", 2)))), 13f, 9f), "normal", 25f),
        Make(HunterShuttle, EnemyWhere.Space, ("normal", 35f, 0f, 2f, Loot(("data_fragment", 2), ("circuit_board", 1)))), // #3097
        Make(ScanDrone, EnemyWhere.Planet, ("normal", 25f, 0f, 2f, Loot(("carbon", 2), (GuardianPlate, 1)))), // #3290: robot metal
        Make(GuardianRobot, EnemyWhere.Planet, ("normal", 30f, 0f, 4f, Loot(("carbon", 2), (GuardianPlate, 2)))),
        Rare(Make(Hunter, EnemyWhere.Planet, ("normal", 60f, 0f, 6f, Loot(("carbon", 2), (GuardianPlate, 2)))), HackingChip, HunterChipChance), // #3894
        Bursting(Make(TankSpider, EnemyWhere.Planet, ("normal", 20f, 0f, 0f, Loot(("carbon", 2), (GuardianPlate, 1)))), 30f), // #3096
        Make(GuardianGiant, EnemyWhere.Planet, ("normal", GuardianGiantRules.DefaultHull, 0f, 0f, Loot(("carbon", 6), (GuardianPlate, 14)))), // #3676
        Make(Bandit, EnemyWhere.Planet, ("normal", 35f, 0f, 5f, Loot(("iron_plate", 2), (RobberCloth, 1))), ("camp", 35f, 0f, 5f, Loot(("iron_plate", 2), ("carbon", 2), (RobberCloth, 2)))), // #3291
        Make(BanditGunner, EnemyWhere.Planet, ("normal", 35f, 0f, 3f, Loot(("iron_plate", 2), (RobberCloth, 1))), ("camp", 35f, 0f, 3f, Loot(("iron_plate", 2), ("carbon", 2), (RobberCloth, 2)))),
    };

    /// <summary>#3894: gives every tier of <paramref name="def"/> one rare drop.</summary>
    private static EnemyDefinition Rare(EnemyDefinition def, string item, float chance)
    {
        foreach (var tier in def.Tiers.Values)
        {
            tier.RareLoot.Add(new RareDrop { Item = item, Count = 1, Chance = chance });
        }

        return def;
    }

    /// <summary>#3096: gives every tier of <paramref name="def"/> the burst damage of a bursting enemy.</summary>
    private static EnemyDefinition Bursting(EnemyDefinition def, float burstDamage)
    {
        foreach (var tier in def.Tiers.Values)
        {
            tier.BurstDamage = burstDamage;
        }

        return def;
    }

    /// <summary>#3756: gives every tier of <paramref name="def"/> a heavy shot.</summary>
    private static EnemyDefinition Heavy(EnemyDefinition def, float damage, float interval)
    {
        foreach (var tier in def.Tiers.Values)
        {
            tier.HeavyShotDamage = damage;
            tier.HeavyShotInterval = interval;
        }

        return def;
    }

    /// <summary>#3900: the escort cruiser — the cruiser as a boss. Hull and shield about 2.7 times the cruiser's, the same
    /// steady fire, and the mega laser: 60 damage after a 2.8 s charge from up to 110 units, every 14 s (9 s once enraged
    /// at half hull, when it also flies half as fast again and calls two drones). One hit takes at most 40 % of the
    /// target's full hull and never the last hull point of a ship above a quarter of its hull.</summary>
    private static EnemyDefinition MakeEscortCruiser()
    {
        var def = Shielded(Heavy(Make(EscortCruiserRules.EnemyKey, EnemyWhere.Space,
            ("normal", 700f, 0f, 7f, Loot(("data_fragment", 16), (GuardianPlate, 10)))), 60f, 14f), "normal", 150f);
        var tier = def.Tiers["normal"];
        tier.HeavyShotCharge = 2.8f;
        tier.HeavyShotRange = 110f;
        tier.HeavyShotMaxHullShare = 0.4f;
        tier.HeavyShotStyle = SpaceCombatRules.HeavyStyleMegaLaser;
        tier.Boss = true;
        tier.EnrageAtHull = 0.5f;
        tier.EnrageSpeed = 1.5f;
        tier.EnrageHeavyShotInterval = 9f;
        tier.Reinforcements = 2;
        return def;
    }

    /// <summary>#3758: gives one tier of <paramref name="def"/> a shield.</summary>
    private static EnemyDefinition Shielded(EnemyDefinition def, string tier, float shield)
    {
        def.Tiers[tier].Shield = shield;
        return def;
    }

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
