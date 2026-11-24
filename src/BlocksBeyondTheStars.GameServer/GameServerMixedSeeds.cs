// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #4001: a mixed seed (<see cref="ItemDefinition.PlacesAnyOf"/> — the flower seeds) plants one of several flora blocks, picked
/// by the cell, partial of <see cref="GameServer"/>. Each meadow flower is its own species, so every world gives each its own
/// form and colours: a bed sown from one bag comes out mixed. The pick is a pure function of the world seed and the cell (the
/// same cell always gives the same flower) among the flowers that root on the ground there; where none does, the item's face
/// stays and the flora rule refuses it as usual.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The block a mixed seed plants at <paramref name="pos"/>: one of the pool that roots on the ground there, picked
    /// by the cell, or <paramref name="face"/> when none of them does.</summary>
    private BlockDefinition MixedSeedBlock(ItemDefinition item, BlockDefinition face, Vector3i pos)
    {
        var rooting = new List<BlockDefinition>();
        foreach (string key in item.PlacesAnyOf!)
        {
            if (_content.GetBlock(key) is { } def && !def.NumericId.IsAir && IsValidFloraHost(def.NumericId.Value, pos))
            {
                rooting.Add(def);
            }
        }

        return rooting.Count == 0 ? face : rooting[MixedSeedIndex(_meta.Seed, pos, rooting.Count)];
    }

    /// <summary>A stable index in [0, <paramref name="count"/>) for a cell of a world (SplitMix64 of seed and cell).</summary>
    internal static int MixedSeedIndex(long worldSeed, Vector3i pos, int count)
    {
        ulong z = unchecked((ulong)worldSeed ^ ((ulong)(uint)pos.X * 0x9E3779B97F4A7C15UL)
            ^ ((ulong)(uint)pos.Y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)pos.Z * 0x165667B19E3779F9UL));
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;
        return (int)(z % (ulong)count);
    }
}
