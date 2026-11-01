// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3058: the procedural scorch / crack / crater atlas of the voxel-face decals.</summary>
public sealed class DecalPatternsTests
{
    private static readonly byte[] Atlas = DecalPatterns.BuildAtlas();

    private static (byte Soot, byte Ember, byte B, byte A) Pixel(int tile, int x, int y)
    {
        int ax = (tile & 1) * DecalPatterns.TileSize + x, ay = (tile >> 1) * DecalPatterns.TileSize + y;
        int i = (ay * DecalPatterns.AtlasSize + ax) * 4;
        return (Atlas[i], Atlas[i + 1], Atlas[i + 2], Atlas[i + 3]);
    }

    [Fact]
    public void TheAtlas_IsFourTilesOfRgba32()
    {
        Assert.Equal(DecalPatterns.AtlasSize * DecalPatterns.AtlasSize * 4, Atlas.Length);
        Assert.Equal((0f, 0f), (DecalPatterns.TileU(DecalPatterns.Scorch), DecalPatterns.TileV(DecalPatterns.Scorch)));
        Assert.Equal((0.5f, 0f), (DecalPatterns.TileU(DecalPatterns.Dent), DecalPatterns.TileV(DecalPatterns.Dent)));
        Assert.Equal((0f, 0.5f), (DecalPatterns.TileU(DecalPatterns.QuakeRing), DecalPatterns.TileV(DecalPatterns.QuakeRing)));
        Assert.Equal((0.5f, 0.5f), (DecalPatterns.TileU(DecalPatterns.BurnCrater), DecalPatterns.TileV(DecalPatterns.BurnCrater)));
        for (int i = 0; i < Atlas.Length; i += 4)
        {
            Assert.Equal(0, Atlas[i + 2]);
            Assert.Equal(255, Atlas[i + 3]);
        }
    }

    [Theory]
    [InlineData(DecalPatterns.Scorch)]
    [InlineData(DecalPatterns.Dent)]
    [InlineData(DecalPatterns.QuakeRing)]
    [InlineData(DecalPatterns.BurnCrater)]
    public void EveryTile_IsBlankAlongItsBorder(int tile)
    {
        // The clamp and the mip chain must never pull one mark into the next — and the quads reach past the mark, where
        // the shader samples the clamped edge.
        const int Rim = 2;
        int n = DecalPatterns.TileSize;
        for (int a = 0; a < n; a++)
        {
            for (int k = 0; k < Rim; k++)
            {
                foreach (var (x, y) in new[] { (a, k), (a, n - 1 - k), (k, a), (n - 1 - k, a) })
                {
                    var p = Pixel(tile, x, y);
                    Assert.True(p.Soot == 0 && p.Ember == 0, $"tile {tile} pixel ({x}, {y}) is ({p.Soot}, {p.Ember})");
                }
            }
        }
    }

    [Theory]
    [InlineData(DecalPatterns.Scorch, 150)]
    [InlineData(DecalPatterns.Dent, 200)]
    [InlineData(DecalPatterns.BurnCrater, 150)]
    public void AMark_IsDarkestAtItsCentre(int tile, int atLeast)
    {
        int c = DecalPatterns.TileSize / 2;
        Assert.True(Pixel(tile, c, c).Soot >= atLeast, $"centre soot {Pixel(tile, c, c).Soot}");
        Assert.True(Pixel(tile, c, c).Soot > Pixel(tile, c + 56, c).Soot);
    }

    [Fact]
    public void TheQuakeRing_IsDarkerOnItsRingThanInItsMiddle()
    {
        // Average around the full circle so a crack crossing one sample does not decide the test.
        static float Ring(float r)
        {
            float sum = 0f;
            for (int i = 0; i < 64; i++)
            {
                double a = i * Math.PI * 2 / 64;
                sum += DecalPatterns.Sample(DecalPatterns.QuakeRing, (float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)).Soot;
            }

            return sum / 64f;
        }

        Assert.True(Ring(0.6f) > Ring(0.05f) + 0.1f, $"ring {Ring(0.6f)} vs middle {Ring(0.05f)}");
    }

    [Fact]
    public void OnlyTheHotMarks_CarryEmbers()
    {
        int c = DecalPatterns.TileSize / 2;
        Assert.True(Pixel(DecalPatterns.Scorch, c, c).Ember > 60);
        Assert.Equal(0, MaxEmber(DecalPatterns.Dent));
        Assert.Equal(0, MaxEmber(DecalPatterns.QuakeRing));
        Assert.True(MaxEmber(DecalPatterns.BurnCrater) > 100);
    }

    [Fact]
    public void TheAtlas_IsDeterministic()
    {
        Assert.Equal(Atlas, DecalPatterns.BuildAtlas());
    }

    private static int MaxEmber(int tile)
    {
        int max = 0;
        for (int y = 0; y < DecalPatterns.TileSize; y++)
        {
            for (int x = 0; x < DecalPatterns.TileSize; x++)
            {
                max = Math.Max(max, Pixel(tile, x, y).Ember);
            }
        }

        return max;
    }
}
