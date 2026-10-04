// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The rules of a push (#2278, the shock gloves) — one place for the numbers the server applies and the tests pin.
/// A push moves a creature, a machine or a bandit straight away from the attacker by the tool's
/// <see cref="ToolProperties.Knockback"/> times the target's mass factor, swept through the target's own collision rules
/// in <see cref="SweepStep"/> steps so it stops at the first wall, hull, fence, cliff edge or (for land animals) water or
/// lava — a push is never a way to throw anything off a cliff. Players, companions and giants are never pushed. A hit
/// with <see cref="ToolProperties.StaggerSeconds"/> also dazes the target; after the daze it cannot be dazed again for
/// <see cref="StaggerImmuneSeconds"/>, so a fast glove never holds an animal down for good.
/// </summary>
public static class KnockbackRules
{
    /// <summary>Length of one sweep step in blocks: the push is tested every half block, so it cannot skip a
    /// one-block wall.</summary>
    public const float SweepStep = 0.5f;

    /// <summary>Seconds after a daze ends during which the target cannot be dazed again (it can still be pushed).</summary>
    public const float StaggerImmuneSeconds = 1.5f;

    /// <summary>The little hop a pushed walker or crawler makes, in blocks (the client draws the arc).</summary>
    public const float HopHeight = 0.6f;

    /// <summary>Mass factor of a bandit — a grown-up in a suit.</summary>
    public const float BanditMass = 0.8f;

    /// <summary>Mass factor of the heavier Guardian hunter robot (the walking scan drone and the plain robot are 1).</summary>
    public const float ToughMachineMass = 0.6f;

    /// <summary>Lightest mass factor a creature can have: even a titan moves about a block.</summary>
    public const float MinCreatureMass = 0.15f;

    /// <summary>The mass factor of a creature of body size <paramref name="size"/>: up to size 2 it flies the full distance,
    /// size 4 half of it, the biggest titans about a block. A giant (the colossus, a sandworm, the leviathan, the sky giant)
    /// is 0 — far too big to push.</summary>
    public static float CreatureMass(float size, bool giant)
    {
        if (giant)
        {
            return 0f;
        }

        float s = float.IsNaN(size) || size < 1f ? 1f : size;
        float m = 2f / s;
        return m < MinCreatureMass ? MinCreatureMass : m > 1f ? 1f : m;
    }

    /// <summary>The number of sweep steps for a push of <paramref name="distance"/> blocks (0 for none).</summary>
    public static int Steps(float distance)
        => distance > 0f && !float.IsInfinity(distance) ? (int)System.Math.Ceiling(distance / SweepStep) : 0;

    /// <summary>The length of sweep step <paramref name="index"/> (1-based) of a push of <paramref name="distance"/>
    /// blocks: a full <see cref="SweepStep"/>, the last one the remainder.</summary>
    public static float StepLength(float distance, int index)
    {
        float left = distance - ((index - 1) * SweepStep);
        return left < SweepStep ? (left > 0f ? left : 0f) : SweepStep;
    }

    /// <summary>True when a daze may start at <paramref name="now"/>: the last one plus its immunity window is over.</summary>
    public static bool CanStagger(double now, double immuneUntil) => now >= immuneUntil;
}
