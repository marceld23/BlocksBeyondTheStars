// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// TEMPORARY STUB (#2639 package, wave 1) — two helpers of the hunter core (#2645, agent C) that the lodges (#2640/#2641)
/// call, so this branch compiles on its own. DELETE THIS FILE when merging agent C's branch: the real ones live in
/// <c>GameServerNpcHunting.cs</c> with the same signatures.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Stub of the hunter core's threat: the real one makes skittish animals within <paramref name="radius"/>
    /// flee from <paramref name="pos"/> for <paramref name="seconds"/>.</summary>
    private void ScareCreaturesFrom(Vector3f pos, float radius, double seconds)
    {
        _ = pos;
        _ = radius;
        _ = seconds;
    }

    /// <summary>Stub of the hunter core's roster test: whether this world has a species a hunter may hunt (here: any
    /// passive or skittish land species — the real core is stricter).</summary>
    private bool WorldHasHuntableSpecies()
        => _speciesRoster.Any(sp => sp.Habitat == CreatureHabitat.Land
            && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish);
}
