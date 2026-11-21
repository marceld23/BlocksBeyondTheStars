// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Space-combat numbers the server and the client must agree on (#2284). The server applies them — hostiles hurt a
/// ship only inside the engage range — and the client draws from them: the enemy shots it shows, the "attacking"
/// classification of the target lock and the HUD's threat ticks all read the same range, so the two sides cannot
/// drift apart again (the client used to keep its own copy of the server's constant).
/// </summary>
public static class SpaceCombatRules
{
    /// <summary>A hostile ship or drone fires on a pilot only once it is this close (units of the flight instance).
    /// Beyond it a distant hostile cannot plink a ship forever, and flying clear of a fight really stops the damage
    /// and lets the shield recharge. The starter ship's laser (<c>ship_laser_basic</c>, <c>weapon_range</c> 60 in
    /// <c>data/ship_modules.json</c>) reaches to within ten units of it, so a child shot at from the edge of the
    /// aura has to fly only a little closer to shoot back — not the 25 units of the old 45 (#2284).</summary>
    public const float EngageRange = 70f;

    /// <summary>#3420: the flown ship's collision radius against a rock — an asteroid, a wreckage fragment or the wreck.
    /// The flight view holds the ship's centre this far from the rock's blocks (it stops at the surface and slides along,
    /// like at a station hull, which uses the same radius), and the server's ram check counts a pilot as touching an
    /// asteroid from this distance on (plus a little slack), so a pose at the surface is always a touch.</summary>
    public const float ShipRockRadius = 2.5f;

    /// <summary>#3756: seconds a heavy shot (the cruiser's, the raider's) charges before it fires. The charge is the
    /// warning: the client lights it up and the pilot has this long to roll or fly clear.</summary>
    public const float HeavyChargeSeconds = 1.6f;

    /// <summary>#3756: a heavy shot still lands on a target this many times the engage range away when it fires — a
    /// ship that flew clear of that is missed.</summary>
    public const float HeavyReachFactor = 1.15f;

    /// <summary>#3756: the share of its flight speed a hostile circles its quarry with once it holds its stand-off ring.</summary>
    public const float OrbitSpeedShare = 0.55f;

    /// <summary>#3758: the id prefix of a derelict station (and of a giant wreck) — <c>DerelictPlacer.IdPrefix</c>.</summary>
    public const string DerelictIdPrefix = "derelict:";

    /// <summary>#3758: the story's one derelict that is not a generated one (it keeps its own rules).</summary>
    public const string StoryDerelictId = "derelict:longquiet";

    /// <summary>#3758: whether a station is behind a shield that takes every ship weapon's shot: every station but a
    /// generated derelict (a giant wreck among them), whose hull breaks instead. The server decides by this and the
    /// client draws by it.</summary>
    public static bool StationHasShield(string? stationId)
        => stationId == null || stationId == StoryDerelictId
           || !stationId.StartsWith(DerelictIdPrefix, System.StringComparison.Ordinal);

    /// <summary>#3900: <c>SpaceHeavyShot.Style</c> of the escort cruiser's mega laser — a long charge with an aiming line
    /// from its barrel to the target, then one thick beam. Empty = the classic heavy shot.</summary>
    public const string HeavyStyleMegaLaser = "mega_laser";

    /// <summary>#3900: a ship with more than this share of its full hull left always keeps a hull point after a capped
    /// heavy shot (<c>EnemyTier.HeavyShotMaxHullShare</c>) — from above a quarter of its hull no single shot ends the flight.</summary>
    public const float HeavyLastPointShare = 0.25f;

    /// <summary>#3900: how far to the side of a capped heavy shot's line an asteroid still stops it — fly behind a rock
    /// and the beam ends there (times the rock's scale).</summary>
    public const float HeavyCoverRadius = 4f;

    /// <summary>#3900: what a capped heavy shot takes off the hull. <paramref name="onHull"/> is what the shield let
    /// through; it is cut to <paramref name="maxHullShare"/> of the full hull, and a ship above
    /// <see cref="HeavyLastPointShare"/> of its hull keeps one point. A share of 0 or less means no cap.</summary>
    public static float HeavyHullDamage(float onHull, float hull, float hullMax, float maxHullShare)
    {
        if (onHull <= 0f || hull <= 0f)
        {
            return 0f;
        }

        if (maxHullShare <= 0f || hullMax <= 0f)
        {
            return onHull;
        }

        float damage = System.Math.Min(onHull, hullMax * maxHullShare);
        if (hull > hullMax * HeavyLastPointShare && damage > hull - 1f)
        {
            damage = System.Math.Max(0f, hull - 1f);
        }

        return damage;
    }

    /// <summary>#3756: the phases a <c>SpaceHeavyShot</c> message reports.</summary>
    public const byte HeavyPhaseCharge = 0;

    /// <summary>The shot fired and hit its target.</summary>
    public const byte HeavyPhaseHit = 1;

    /// <summary>The shot fired and missed: the target rolled, or had flown clear.</summary>
    public const byte HeavyPhaseMiss = 2;
}
