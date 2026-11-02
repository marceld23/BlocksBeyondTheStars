// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The rules of the tank spider (#3096, Justus: "a mechanical spider with a tank instead of a hump — when you get too close
/// its tank inflates and it explodes, without destroying blocks, only with damage"). A Guardian machine that does not bite:
/// it skitters up to a player, stops, its tank swells with a hiss and a glow for <see cref="InflateSeconds"/> — the warning
/// to run — and then bursts. The burst hurts players in <see cref="BurstRadius"/> (less toward the edge), pushes them away
/// and never changes a block; a wall between the tank and a player stops it. Shot before it bursts, it only pops. Pure and
/// Unity-free so the server (when it arms and bursts), the client (how the tank swells, who is pushed) and the tests read
/// the same numbers. The burst's full damage is <c>data/enemies.json</c>'s <see cref="EnemyTier.BurstDamage"/>.
/// </summary>
public static class TankSpiderRules
{
    /// <summary>How close (blocks, across the ground) a player must come before the spider stops and its tank swells.</summary>
    public const float TriggerRange = 3f;

    /// <summary>How far above or below the spider a player may be and still set it off — one on a roof over it does not.</summary>
    public const float TriggerHeight = 2.5f;

    /// <summary>The warning: the tank swells, hisses and glows this long before it bursts — time enough to run clear.</summary>
    public const float InflateSeconds = 1.5f;

    /// <summary>The burst's reach (blocks): a sphere around the tank.</summary>
    public const float BurstRadius = 3.5f;

    /// <summary>The share of the full burst damage that is left at the very edge of <see cref="BurstRadius"/>.</summary>
    public const float EdgeShare = 0.4f;

    /// <summary>The push away from the tank (blocks/s) a player at its centre gets; it falls off like the damage.</summary>
    public const float KnockSpeed = 8f;

    /// <summary>The upward part of the push (blocks/s) at the centre.</summary>
    public const float KnockLift = 5f;

    /// <summary>The share of the planet-machine refills (never the first machines of a visit) that come as a tank spider.</summary>
    public const double RefillShare = 0.35;

    /// <summary>The server's <c>WorldFx</c> kind of the burst: the client's flash, smoke, bang and push.</summary>
    public const string BurstFx = "tank_burst";

    /// <summary>The sound of the swelling tank (a rising hiss), played when it starts.</summary>
    public const string InflateCue = "tank_spider_inflate";

    /// <summary>The sound of the burst — a pop with a gush of air (also, quieter, when it is shot and only pops).</summary>
    public const string BurstCue = "tank_spider_burst";

    /// <summary>Whether a player at (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>) from the spider sets it
    /// off: within <see cref="TriggerRange"/> across the ground and <see cref="TriggerHeight"/> up or down.</summary>
    public static bool InTriggerReach(float dx, float dy, float dz)
        => (dx * dx) + (dz * dz) <= TriggerRange * TriggerRange && Math.Abs(dy) <= TriggerHeight;

    /// <summary>The share (0..1) of the full burst that reaches a player <paramref name="distance"/> blocks from the tank: 1 at
    /// the centre, falling evenly to <see cref="EdgeShare"/> at the edge of <see cref="BurstRadius"/>, 0 beyond it.</summary>
    public static float FalloffAt(float distance)
    {
        if (distance > BurstRadius || float.IsNaN(distance))
        {
            return 0f;
        }

        float t = Math.Clamp(distance / BurstRadius, 0f, 1f);
        return 1f - ((1f - EdgeShare) * t);
    }

    /// <summary>The damage a burst of <paramref name="burstDamage"/> deals <paramref name="distance"/> blocks from the tank.</summary>
    public static float DamageAt(float burstDamage, float distance) => burstDamage <= 0f ? 0f : burstDamage * FalloffAt(distance);

    /// <summary>How far the tank has swollen (0..1) <paramref name="elapsed"/> seconds after it began.</summary>
    public static float InflateProgress(float elapsed) => Math.Clamp(elapsed / InflateSeconds, 0f, 1f);

    /// <summary>Whether a refill spawn with the stable random <paramref name="roll"/> (0..1) comes as a tank spider.</summary>
    public static bool PicksSpider(double roll) => roll < RefillShare;
}
