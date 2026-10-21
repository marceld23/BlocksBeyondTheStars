// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2858: every glowing plant casts light in its colour on this world. The classic glowers declare a fixed light colour
/// in blocks.json (the glow vine always light green) while the plant wears this world's colour of its species — their
/// light now takes that colour, at the brightness and the reach the declared light gives. Authored plants keep their own
/// colour and light; generation 11's light species keep their own resolver path.
/// </summary>
public sealed class PlantGlowLightTintTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static IEnumerable<(FloraCatalog.Species Species, BlockDefinition Block)> ClassicGlowers()
    {
        foreach (var sp in FloraCatalog.All)
        {
            if (Content.GetBlock(sp.Key) is { } block && PlantGlowLight.TakesWorldColour(sp, block))
            {
                yield return (sp, block);
            }
        }
    }

    [Fact]
    public void TheClassicGlowers_AreTheWildGlowSpeciesWithADeclaredLight()
    {
        var keys = ClassicGlowers().Select(g => g.Block.Key).ToHashSet();
        foreach (string key in new[] { "flora_glowvine", "flora_glowcap", "flora_emberbloom", "flora_cinderbush", "flora_crystal", "flora_shardbloom" })
        {
            Assert.Contains(key, keys);
        }

        // Authored plants keep their colour, so they keep their light; the frost flower is no glower (#2858).
        Assert.DoesNotContain(FloraCatalog.LanternFruitKey, keys);
        Assert.DoesNotContain("flora_frostflower", keys);
        // Generation 11's light species (cell resolver, rainbow class included) are not this path.
        Assert.DoesNotContain(keys, k => FloraCatalog.LightOf(k) > 0f);
    }

    [Fact]
    public void AClassicGlower_CastsItsWorldColour_AtTheDeclaredBrightnessAndReach()
    {
        foreach (var (_, block) in ClassicGlowers())
        {
            for (int w = 0; w < 6; w++)
            {
                string location = $"Sol · Glow {w}";
                int packed = PlantGlowLight.PackedFor(4242 + w, location, block);
                int rgb = BlockLight.ColorFrom(packed);
                int declared = block.LightColor!.Value;

                // The reach and the surface-only flag are the declared ones.
                Assert.Equal(BlockLight.RadiusOf(block), BlockLight.RadiusFrom(packed));
                Assert.Equal(block.LightSurfaceOnly, BlockLight.SurfaceOnlyFrom(packed));

                // The brightest channel is as bright as the declared light's brightest channel (the flood's reach).
                Assert.InRange(Peak(rgb) - Peak(declared), -1, 1);

                // The colour is the species' colour on this world: the channels in the tint's proportions.
                var tint = FloraTints.For(4242 + w, location, block.Key);
                float scale = Peak(rgb) / 255f / System.Math.Max(tint.R, System.Math.Max(tint.G, tint.B));
                Assert.InRange(((rgb >> 16) & 0xFF) - (tint.R * scale * 255f), -1.5f, 1.5f);
                Assert.InRange(((rgb >> 8) & 0xFF) - (tint.G * scale * 255f), -1.5f, 1.5f);
                Assert.InRange((rgb & 0xFF) - (tint.B * scale * 255f), -1.5f, 1.5f);
            }
        }
    }

    [Fact]
    public void TheGlowVine_NoLongerLightsGreenOnEveryWorld()
    {
        var vine = Content.GetBlock("flora_glowvine")!;
        var colours = new HashSet<int>();
        for (int w = 0; w < 12; w++)
        {
            colours.Add(BlockLight.ColorFrom(PlantGlowLight.PackedFor(w * 977, $"world-{w}", vine)));
        }

        Assert.True(colours.Count >= 10, $"the glow vine's light should follow the world's colour (got {colours.Count}/12 colours)");
        Assert.Equal(PlantGlowLight.PackedFor(5, "same", vine), PlantGlowLight.PackedFor(5, "same", vine));
    }

    [Fact]
    public void ABlockWithoutADeclaredLight_KeepsItsPlainLight()
    {
        var fern = Content.GetBlock("flora_fern")!;
        Assert.Equal(BlockLight.PackedLightOf(fern), PlantGlowLight.PackedFor(1, "x", fern));
    }

    private static int Peak(int rgb) => System.Math.Max((rgb >> 16) & 0xFF, System.Math.Max((rgb >> 8) & 0xFF, rgb & 0xFF));
}
