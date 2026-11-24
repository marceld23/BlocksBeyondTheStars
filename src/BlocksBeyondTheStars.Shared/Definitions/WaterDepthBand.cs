// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The water a swimmer lives in (#3975): a water species may name a depth band (<see cref="CreatureSpecies.MinWaterDepth"/>,
/// <see cref="CreatureSpecies.MaxWaterDepth"/>, 0 = no bound) — the whale the deep sea, the dolphin the coastal shallows. The
/// spawner seats it only in a water column whose depth lies in the band, and a swimmer with a lower bound never steps into
/// water shallower than that bound while it would get shallower — it may always step to equal or deeper water, so it is never
/// trapped. Every species without a band (every rolled species, every older record) keeps the classic rules exactly.
/// Pure and Unity-free.
/// </summary>
public static class WaterDepthBand
{
    /// <summary>The fewest water cells a column must hold to seat the species at spawn: water life needs room to swim
    /// (<see cref="CreatureBehaviour.MinSwimDepth"/>, or its own lower bound when deeper); an amphibian wades at the surface
    /// cell of any water.</summary>
    public static int MinSpawnDepth(CreatureSpecies sp)
        => sp.Habitat == CreatureHabitat.Water ? Math.Max(CreatureBehaviour.MinSwimDepth, sp.MinWaterDepth) : 1;

    /// <summary>The most water cells a column may hold to seat the species at spawn; unbounded without an upper bound.</summary>
    public static int MaxSpawnDepth(CreatureSpecies sp)
        => sp.Habitat == CreatureHabitat.Water && sp.MaxWaterDepth > 0 ? sp.MaxWaterDepth : int.MaxValue;

    /// <summary>Whether a water column <paramref name="depth"/> cells deep may seat the species at spawn.</summary>
    public static bool SeatsAt(CreatureSpecies sp, int depth) => depth >= MinSpawnDepth(sp) && depth <= MaxSpawnDepth(sp);

    /// <summary>Whether a swimmer's step from water <paramref name="currentDepth"/> deep into water <paramref name="nextDepth"/>
    /// deep leaves its band at the shallow end: only for a species with a lower bound, only while the step gets shallower.</summary>
    public static bool StepTooShallow(CreatureSpecies sp, int currentDepth, int nextDepth)
        => sp.Habitat == CreatureHabitat.Water && sp.MinWaterDepth > 0 && nextDepth < sp.MinWaterDepth && nextDepth < currentDepth;
}
