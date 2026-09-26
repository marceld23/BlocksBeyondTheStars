// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 13 (#2024, the toxic worlds) — the rare-ore outcrops (#2030), partial of <see cref="WorldGenerator"/>.
/// A toxic world that rolled <see cref="WorldTraits.OreOutcrops"/> shows small clumps of its rare-tier veins on the
/// surface. The gate is false below generation 13 and on every world without the trait; the row sits at the end of the
/// prop table, so every older row keeps its precedence. Kept in its own file (#1740: the big methods stay small).
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The outcrop row's gate: solid ground on a world that rolled the trait.</summary>
    private static bool PropOreOutcrops(WonderProfile w, PlanetType p)
        => w.Generation >= WorldDescription.ToxicWorldsGeneration && w.OreOutcrops && PropSolidGround(w, p) && !p.Cratered;

    /// <summary>A clump of rare ore lying on the ground: one to four cells, each side cell seated on its own column.</summary>
    private static void StampOreOutcrop(PropStamp s)
    {
        var ore = s.Generator.OutcropOre(s.Planet, s.ShapeHash, s.Material);
        s.Set(s.Wx, s.Sy + 1, s.Wz, ore);

        int dx = (s.ShapeHash & 1) == 0 ? 1 : -1;
        int dz = (s.ShapeHash & 2) == 0 ? 1 : -1;
        s.Set(s.Wx + dx, s.Generator.SurfaceHeight(s.Planet, s.Wx + dx, s.Wz) + 1, s.Wz, ore);
        if ((s.ShapeHash & 4) == 0)
        {
            s.Set(s.Wx, s.Generator.SurfaceHeight(s.Planet, s.Wx, s.Wz + dz) + 1, s.Wz + dz, ore);
        }

        if ((s.ShapeHash & 24) == 0)
        {
            s.Set(s.Wx, s.Sy + 2, s.Wz, ore); // now and then a lump sits on top
        }
    }

    /// <summary>The rare-tier vein a clump is made of: one of the type's <see cref="OreVein.RareTier"/> veins, picked by the
    /// column's shape hash (the fallback when the type has none).</summary>
    private BlockId OutcropOre(PlanetType planet, int shapeHash, BlockId fallback)
    {
        int rare = 0;
        foreach (var vein in planet.Ores)
        {
            if (vein.RareTier)
            {
                rare++;
            }
        }

        if (rare == 0)
        {
            return fallback;
        }

        int pick = (shapeHash >> 5) % rare;
        foreach (var vein in planet.Ores)
        {
            if (vein.RareTier && pick-- == 0)
            {
                return _content.GetBlock(vein.Block)?.NumericId ?? fallback;
            }
        }

        return fallback;
    }
}
