// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The worm body plan (#2109, generation 18 — Marcel's finding on a sand sea: "the sandworms have legs"). Until this
/// generation every Land species had 2, 4 or 6 legs and a long-bodied one could still roll the <c>Slitherer</c> style, so a
/// "worm" walked. Now, on generation-18 worlds, whoever slithers is legless: a standard Land species may become a
/// <see cref="CreatureBodyPlan.Worm"/> — a head and a chain of links that runs a travelling wave (the client draws the chain as
/// the tail rig, whose beat already travels link by link) — and the legged slitherer roll is gone. Knee- to hip-high, on every
/// world with land fauna. Every number the generator and the client share lives here.
/// </summary>
public static class WormRules
{
    /// <summary>One standard Land species in about seven becomes a worm (one draw, rolled last).</summary>
    public const double WormChance = 0.15;

    /// <summary>Knee- to hip-high (the standard roll's size is a scale, ~0.5 blocks per unit).</summary>
    public const float MinSize = 0.5f;
    public const float MaxSize = 1.1f;

    /// <summary>Links behind the head; the wave needs at least six to read as a slither.</summary>
    public const int MinSegments = 6;
    public const int MaxSegments = 12;

    /// <summary>Slow: a worm never outruns the player.</summary>
    public const float MinSpeed = 1.2f;
    public const float MaxSpeed = 2.2f;

    public static bool IsWorm(CreatureSpecies sp) => sp.BodyPlan == CreatureBodyPlan.Worm;
}
