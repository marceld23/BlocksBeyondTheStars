// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #2858: the light a classic glowing plant casts on a world. The classic glowers (the glow vine, the glow cap, the ember
/// bloom …) declare a fixed <see cref="BlockDefinition.LightColor"/> in <c>data/blocks.json</c>, but the plant itself wears
/// this world's colour of its species (<see cref="FloraTints.For"/>) — so the glow vine flooded light green around a violet
/// plant. Generation 11's light species already cast their world colour; this gives every classic glower the same rule,
/// keeping the brightness and the reach its declared light gives today. Authored plants (the Fifi blossom, the lantern
/// fruit) and the farmed crops keep their own colour, so they keep their declared light too. Pure function of (world seed,
/// location key, block), so every client agrees without any traffic.
/// </summary>
public static class PlantGlowLight
{
    /// <summary>True for a classic glowing wild species: tagged <see cref="FloraTag.Glow"/>, its block declares a light
    /// colour, it takes the world's colour (neither a crop nor an authored plant), and it is no generation-11 light species
    /// (<see cref="FloraCatalog.Species.Light"/>, whose resolver already handles it — the rainbow class included).</summary>
    public static bool TakesWorldColour(FloraCatalog.Species species, BlockDefinition? block)
        => block?.LightColor is int declared && (declared & 0xFFFFFF) != 0
            && (species.Tags & FloraTag.Glow) != 0
            && !species.Cultivated && !species.Authored
            && species.Light <= 0f && !species.Rainbow;

    /// <summary>The light the block casts on this world, packed for the client's light index
    /// (<see cref="BlockLight.Pack"/>): the species' world colour, scaled so its brightest channel is as bright as the
    /// declared light's brightest channel (the flood fill's reach follows the channel levels), with the declared reach and
    /// surface-only flag. A block without a declared light casts its plain packed light (0 when none).</summary>
    public static int PackedFor(long worldSeed, string locationKey, BlockDefinition block)
    {
        if (block.LightColor is not int declared || (declared & 0xFFFFFF) == 0)
        {
            return BlockLight.PackedLightOf(block);
        }

        int peak = System.Math.Max((declared >> 16) & 0xFF, System.Math.Max((declared >> 8) & 0xFF, declared & 0xFF));
        var tint = FloraTints.For(worldSeed, locationKey, block.Key);
        float tintPeak = System.Math.Max(tint.R, System.Math.Max(tint.G, tint.B));
        if (tintPeak <= 0.001f)
        {
            return BlockLight.PackedLightOf(block); // a black tint cannot carry a colour — keep the declared light
        }

        int rgb = FloraTints.ToRgb24(tint, peak / 255f / tintPeak);
        return BlockLight.Pack(rgb, BlockLight.RadiusOf(block), block.LightSurfaceOnly);
    }
}
