// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A body's species beyond what one call site rolls: the one roster formula every caller shares.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The roster body <paramref name="bodyId"/> of type <paramref name="planet"/> rolls in this save — THE formula the
    /// live world (<see cref="InitCreatures"/>) and the ship's overview scan of another body both read, so the scan names the
    /// animals the landing meets.</summary>
    private IReadOnlyList<CreatureSpecies> RosterFor(PlanetType planet, string bodyId)
        => CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(_meta.Seed, bodyId),
            _meta.Description.TerrainGeneration, _content.AuthoredCreaturesFor(planet));
}
