// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3289: a monument's runes glow in the colour of the biome they stand in. The rune stone and the ancient brick wear the
/// palette of their column (<see cref="BiomePalettes"/>, the client mesher picks the tile), so the light they cast takes
/// the same palette's glow instead of the monument's random one. Decided when the voxels are stamped — once per world, so
/// existing worlds keep their glow — and without touching <see cref="WorldGeneration.MonumentGenerator"/>: its random
/// draws, hence every monument's layout and its re-derived bounds, stay exactly as they were.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The glow a monument cell of <paramref name="block"/> at column (<paramref name="worldX"/>,
    /// <paramref name="worldZ"/>) is stamped with: the column's palette glow for a glowing biome-look block, else the
    /// generator's <paramref name="glow"/> unchanged (an unlit cell, another block, a column whose biome has no palette).</summary>
    private int MonumentGlowAt(ushort block, int glow, int worldX, int worldZ)
    {
        if (glow == 0 || _content.BlockById(new BlockId(block)) is not { BiomeLooks: true })
        {
            return glow;
        }

        int palette = BiomePalettes.OfSurface(_content, _generator.BiomeSurfaceKeyAt(_world.Planet, worldX, worldZ));
        return palette < 0 ? glow : BiomePalettes.GlowOf(palette);
    }
}
