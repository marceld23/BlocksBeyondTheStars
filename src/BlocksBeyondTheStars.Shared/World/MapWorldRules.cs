// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The one generation rule of the map worlds (#3981). A map world (a type with a <see cref="PlanetType.TerrainMap"/>: Sol's
/// Earth, Moon, Mars, Venus, Mercury) joins saves of every terrain generation as a newcomer — Sol appends itself to old
/// galaxies too — and its given places and listed pads need the structure pipeline of the newer generations: the footprint
/// registry (generation 38), the landmark overlays, protection and span seats (39), the reserved and graded ground and the
/// river geography (40). So on a map world those stages run at least at <see cref="WorldDescription.GeographyGeneration"/>,
/// whatever generation the save was created with. No world that existed before the maps changes: only map worlds read it.
/// Every other gate (which kinds roll, from which generation) still reads the save's generation.
/// </summary>
public static class MapWorldRules
{
    /// <summary>The generation the geography stages and the structure pipeline of a world of <paramref name="planet"/> run at:
    /// <paramref name="saveGeneration"/>, raised to <see cref="WorldDescription.GeographyGeneration"/> on a map world.</summary>
    public static int PipelineGeneration(int saveGeneration, PlanetType? planet)
        => planet is { HasTerrainMap: true } && saveGeneration < WorldDescription.GeographyGeneration
            ? WorldDescription.GeographyGeneration
            : saveGeneration;
}
