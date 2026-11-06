// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3274: the procedural smoke-puff atlas the smoke particles pick their shape from.</summary>
public sealed class SmokePuffsTests
{
    private static readonly byte[] Atlas = SmokePuffs.BuildAtlas();

    private static (byte Erosion, byte Lumps, byte B, byte Density) Pixel(int tile, int x, int y)
    {
        int ax = (tile & 1) * SmokePuffs.TileSize + x, ay = (tile >> 1) * SmokePuffs.TileSize + y;
        int i = (ay * SmokePuffs.AtlasSize + ax) * 4;
        return (Atlas[i], Atlas[i + 1], Atlas[i + 2], Atlas[i + 3]);
    }

    [Fact]
    public void TheAtlas_IsFourTilesOfRgba32_AndDeterministic()
    {
        Assert.Equal(SmokePuffs.AtlasSize * SmokePuffs.AtlasSize * 4, Atlas.Length);
        Assert.Equal(Atlas, SmokePuffs.BuildAtlas());
        for (int i = 2; i < Atlas.Length; i += 4)
        {
            Assert.Equal(0, Atlas[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryTile_IsEmptyAlongItsBorder_AndDenseInItsCore(int tile)
    {
        int n = SmokePuffs.TileSize;
        for (int a = 0; a < n; a++)
        {
            for (int k = 0; k < 2; k++)
            {
                foreach (var (x, y) in new[] { (a, k), (a, n - 1 - k), (k, a), (n - 1 - k, a) })
                {
                    Assert.True(Pixel(tile, x, y).Density == 0, $"tile {tile} pixel ({x}, {y}) has density {Pixel(tile, x, y).Density}");
                }
            }
        }

        int c = n / 2;
        Assert.True(Pixel(tile, c, c).Density >= 180, $"tile {tile} core density {Pixel(tile, c, c).Density}");
    }

    [Fact]
    public void TheTiles_AreDifferentPuffs()
    {
        int n = SmokePuffs.TileSize;
        for (int a = 0; a < 4; a++)
        {
            for (int b = a + 1; b < 4; b++)
            {
                int differ = 0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        if (Math.Abs(Pixel(a, x, y).Density - Pixel(b, x, y).Density) > 40)
                        {
                            differ++;
                        }
                    }
                }

                Assert.True(differ > n * n / 20, $"tiles {a} and {b} differ in only {differ} pixels");
            }
        }
    }

    [Fact]
    public void ThePuff_HasStructure_ToErodeAndToLight()
    {
        // Inside the puff the erosion noise and the billows must spread widely, or the erosion would fade the puff evenly
        // and the light would fall on a flat disc.
        int n = SmokePuffs.TileSize;
        var erosion = new List<int>();
        var lumps = new List<int>();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var p = Pixel(1, x, y);
                if (p.Density > 120)
                {
                    erosion.Add(p.Erosion);
                    lumps.Add(p.Lumps);
                }
            }
        }

        Assert.True(erosion.Count > n * n / 8, "the puff covers a good share of its tile");
        Assert.True(erosion.Max() - erosion.Min() > 110, $"erosion spread {erosion.Min()}..{erosion.Max()}");
        Assert.True(lumps.Max() - lumps.Min() > 70, $"billow spread {lumps.Min()}..{lumps.Max()}");
    }

    [Fact]
    public void TheDensity_FallsOffTowardTheEdge()
    {
        static float Ring(int tile, float r)
        {
            float sum = 0f;
            for (int i = 0; i < 32; i++)
            {
                double a = i * Math.PI * 2 / 32;
                sum += SmokePuffs.Sample(tile, (float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)).Density;
            }

            return sum / 32f;
        }

        for (int tile = 0; tile < 4; tile++)
        {
            Assert.True(Ring(tile, 0.1f) > 0.8f, $"tile {tile}: core {Ring(tile, 0.1f)}");
            Assert.True(Ring(tile, 0.1f) > Ring(tile, 0.6f), $"tile {tile}");
            Assert.True(Ring(tile, 0.6f) >= Ring(tile, 0.85f), $"tile {tile}");
            Assert.True(Ring(tile, 0.85f) < 0.1f, $"tile {tile}: edge {Ring(tile, 0.85f)}");
        }
    }
}
