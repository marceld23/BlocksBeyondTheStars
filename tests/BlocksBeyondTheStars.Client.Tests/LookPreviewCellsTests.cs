// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3940: the engine-free half of the texture editor's block wall — the per-cell mip chains of the preview
/// atlas, the still tint mode, the cell of a UV, the ground variants that follow the tile being painted, and the example
/// world colours.
/// </summary>
public sealed class LookPreviewCellsTests
{
    private const int Tile = LookPreviewCells.TileSize;

    [Fact]
    public void AMipChain_EndsInOneTexel_ThatIsTheTilesMean()
    {
        var tile = new byte[Tile * Tile * 4];
        for (int i = 0; i < Tile * Tile; i++)
        {
            bool dark = ((i % Tile) + (i / Tile)) % 2 == 0; // a checkerboard of 0 and 200
            tile[i * 4] = dark ? (byte)0 : (byte)200;
            tile[(i * 4) + 1] = 100;
            tile[(i * 4) + 2] = 50;
            tile[(i * 4) + 3] = 255;
        }

        var chain = LookPreviewCells.MipChain(tile, Tile);
        Assert.Equal(LookPreviewCells.Log2(Tile) + 1, chain.Count); // 64, 32, … 1: seven levels
        Assert.Equal(6, LookPreviewCells.Log2(Tile));
        Assert.Equal(4, chain[chain.Count - 1].Length);
        Assert.Equal(100, chain[chain.Count - 1][0]); // the mean of 0 and 200
        Assert.Equal(100, chain[chain.Count - 1][1]);
        Assert.Equal(255, chain[chain.Count - 1][3]);
        Assert.Equal(32 * 32 * 4, chain[1].Length);
    }

    [Fact]
    public void AHeightChain_HalvesOneBytePerTexel()
    {
        var height = new byte[] { 0, 100, 200, 100 };
        var half = LookPreviewCells.HalveR8(height, 2);
        Assert.Single(half);
        Assert.Equal(100, half[0]);
        Assert.Equal(3, LookPreviewCells.MipChainR8(new byte[16], 4).Count);
    }

    [Fact]
    public void TheStillTintMode_DropsTheAnimationButKeepsTheMode()
    {
        float animated = 1f + (16f * 3f) + (256f * 2f) + (1024f * 900f); // flora mode, 3 frames, speed 2, strip at 900
        Assert.Equal(1f, LookPreviewCells.StillTintMode(animated));
        Assert.Equal(4f, LookPreviewCells.StillTintMode(4f));
        Assert.Equal(0f, LookPreviewCells.StillTintMode(0f));
    }

    [Fact]
    public void AUv_LiesInTheSlotOfItsCell()
    {
        Assert.Equal((7 * 32) + 5, LookPreviewCells.SlotOf(5.5f / 32f, 7.5f / 32f));
        Assert.Equal(0, LookPreviewCells.SlotOf(0.001f, 0.001f));
        Assert.Equal(1023, LookPreviewCells.SlotOf(0.999f, 0.999f));
        Assert.Equal(1023, LookPreviewCells.SlotOf(1.2f, 1.5f)); // clamped like the atlas edge
    }

    [Fact]
    public void ACalmVariant_IsTheTileMirroredAndAToneStepAway()
    {
        var tile = Ramp();
        var left = LookPreviewCells.GroundVariant(tile, 3, 0, calm: true, tone: 0.1f);
        var top = LookPreviewCells.GroundVariant(tile, 3, 1, calm: true, tone: 0.1f);

        // Variant 0 mirrors left-right and is darker: texel (0, 5) shows (63, 5) at 0.9.
        Assert.Equal((byte)Math.Round(tile[Index(Tile - 1, 5)] * 0.9f), left[Index(0, 5)]);
        // Variant 1 mirrors top-bottom and is lighter: texel (5, 0) shows (5, 63) at 1.1.
        Assert.Equal((byte)Math.Min(255, Math.Round(tile[Index(5, Tile - 1) + 1] * 1.1f)), top[Index(5, 0) + 1]);
        Assert.Equal(255, left[Index(0, 5) + 3]); // alpha stays
    }

    [Fact]
    public void APaintedVariant_KeepsThePaintingWhereItIs_AndOnlyStepsItsTone()
    {
        // #3939: a tile a player painted is neither mirrored nor cracked, whatever the slot was before — a painted arrow
        // stays readable on every variant.
        var tile = Ramp();
        foreach (bool calm in new[] { false, true })
        {
            var dark = LookPreviewCells.GroundVariant(tile, 3, 0, calm, tone: 0.1f, painted: true);
            var light = LookPreviewCells.GroundVariant(tile, 3, 1, calm, tone: 0.1f, painted: true);
            Assert.Equal((byte)Math.Round(tile[Index(0, 5)] * 0.9f), dark[Index(0, 5)]);
            Assert.Equal((byte)Math.Min(255, Math.Round(tile[Index(5, 0) + 1] * 1.1f)), light[Index(5, 0) + 1]);
            Assert.Equal(255, dark[Index(0, 5) + 3]);
        }
    }

    [Fact]
    public void TheClassicVariants_AreDeterministic_DarkerAndLighter()
    {
        var tile = new byte[Tile * Tile * 4];
        for (int i = 0; i < tile.Length; i++)
        {
            tile[i] = 128;
        }

        var dark = LookPreviewCells.GroundVariant(tile, 17, 0, calm: false, tone: 0f);
        var light = LookPreviewCells.GroundVariant(tile, 17, 1, calm: false, tone: 0f);
        Assert.Equal(dark, LookPreviewCells.GroundVariant(tile, 17, 0, calm: false, tone: 0f));
        Assert.True(Mean(dark) < 128f);
        Assert.True(Mean(light) > 128f);
        Assert.Contains(dark, b => b == (byte)Math.Round(128 * 0.96f * 0.70f)); // the cracks
        Assert.Equal(128, dark[3]); // alpha untouched
    }

    [Fact]
    public void TheExampleWorlds_DifferInHue_AndBarkStaysDarkerThanLeaves()
    {
        for (int a = 0; a < LookPreviewCells.ExampleCount; a++)
        {
            for (int b = a + 1; b < LookPreviewCells.ExampleCount; b++)
            {
                Assert.True(HueGap(LookPreviewCells.ExampleTint(a, "flora_fern"), LookPreviewCells.ExampleTint(b, "flora_fern")) > 60f);
                Assert.True(HueGap(LookPreviewCells.ExampleTint(a, TreeWood.LogKey), LookPreviewCells.ExampleTint(b, TreeWood.LogKey)) > 60f);
            }

            Assert.True(Max(LookPreviewCells.ExampleTint(a, TreeWood.LogKey)) < Max(LookPreviewCells.ExampleTint(a, TreeFoliage.LeavesKey)));
            Assert.Equal(FloraTints.Mahogany, LookPreviewCells.ExampleTint(a, TreeWood.GiantLogKey));
        }

        Assert.Equal(LookPreviewCells.ExampleTint(0, "flora_fern"), LookPreviewCells.ExampleTint(3, "flora_fern")); // wraps
    }

    [Fact]
    public void WorldTint_IsForWildPlantsCrownsAndTrunks()
    {
        Assert.True(LookPreviewCells.TakesWorldTint("flora_fern"));
        Assert.True(LookPreviewCells.TakesWorldTint(TreeFoliage.LeavesKey));
        Assert.True(LookPreviewCells.TakesWorldTint(TreeWood.LogKey));
        Assert.True(LookPreviewCells.TakesWorldTint("saguaro"));
        Assert.False(LookPreviewCells.TakesWorldTint("stone"));
        Assert.False(LookPreviewCells.TakesWorldTint(null));
    }

    private static byte[] Ramp()
    {
        var tile = new byte[Tile * Tile * 4];
        for (int y = 0; y < Tile; y++)
        {
            for (int x = 0; x < Tile; x++)
            {
                int i = Index(x, y);
                tile[i] = (byte)(x * 3);
                tile[i + 1] = (byte)(y * 3);
                tile[i + 2] = 40;
                tile[i + 3] = 255;
            }
        }

        return tile;
    }

    private static int Index(int x, int y) => ((y * Tile) + x) * 4;

    private static float Mean(byte[] rgba)
    {
        long sum = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            sum += rgba[i];
        }

        return sum / (float)(rgba.Length / 4);
    }

    private static float Max((float R, float G, float B) c) => Math.Max(c.R, Math.Max(c.G, c.B));

    private static float HueGap((float R, float G, float B) a, (float R, float G, float B) b)
    {
        float d = Math.Abs(Hue(a) - Hue(b));
        return Math.Min(d, 360f - d);
    }

    private static float Hue((float R, float G, float B) c)
    {
        float max = Max(c), min = Math.Min(c.R, Math.Min(c.G, c.B));
        float d = max - min;
        if (d < 1e-5f)
        {
            return 0f;
        }

        float h = max == c.R ? ((c.G - c.B) / d) % 6f : max == c.G ? ((c.B - c.R) / d) + 2f : ((c.R - c.G) / d) + 4f;
        h *= 60f;
        return h < 0f ? h + 360f : h;
    }
}
