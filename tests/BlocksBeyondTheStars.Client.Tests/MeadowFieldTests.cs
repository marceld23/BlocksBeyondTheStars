// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3056: the High preset's meadow — where flowers grow, which ones, and how the season thins them.</summary>
public sealed class MeadowFieldTests
{
    [Fact]
    public void AboutOneGrassTopInNine_GrowsAFlower()
    {
        int flowers = 0, cells = 0;
        for (int x = -60; x < 60; x++)
        {
            for (int z = -60; z < 60; z++)
            {
                cells++;
                if (MeadowField.IsFlowerCell(x, 64, z))
                {
                    flowers++;
                }
            }
        }

        double share = flowers / (double)cells;
        Assert.InRange(share, 1.0 / 9.0 * 0.85, 1.0 / 9.0 * 1.15);
    }

    [Fact]
    public void Picks_AreAPureFunctionOfTheCell()
    {
        var a = MeadowField.Flower(1234, 70, -98765);
        var b = MeadowField.Flower(1234, 70, -98765);
        Assert.Equal(a.Species, b.Species);
        Assert.Equal(a.Rank, b.Rank);
        Assert.Equal(a.Yaw, b.Yaw);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(MeadowField.IsFlowerCell(5, 6, 7), MeadowField.IsFlowerCell(5, 6, 7));
        Assert.Equal(MeadowField.CellSeed(-3, 40, 9), MeadowField.CellSeed(-3, 40, 9));

        // A terrace above the same column is a different cell.
        Assert.NotEqual(MeadowField.CellSeed(10, 40, 10), MeadowField.CellSeed(10, 41, 10));
    }

    [Fact]
    public void Flowers_StayInTheirRanges_AndUseEverySpecies()
    {
        var counts = new int[MeadowField.FlowerSpeciesKeys.Length];
        for (int x = 0; x < 80; x++)
        {
            for (int z = 0; z < 80; z++)
            {
                var f = MeadowField.Flower(x, 12, z);
                counts[f.Species]++;
                Assert.InRange(f.Rank, 0f, 1f);
                Assert.InRange(f.Yaw, 0f, 360f);
                Assert.InRange(f.Height, MeadowField.MinFlowerHeight, MeadowField.MaxFlowerHeight);
                Assert.InRange(f.OffsetX, -MeadowField.MaxFlowerOffset, MeadowField.MaxFlowerOffset);
                Assert.InRange(f.OffsetZ, -MeadowField.MaxFlowerOffset, MeadowField.MaxFlowerOffset);
                Assert.InRange(MeadowField.CellSeed(x, 12, z), 0f, 1f);
            }
        }

        // Four species in roughly equal shares (6400 draws → 1600 each).
        Assert.All(counts, c => Assert.InRange(c, 1350, 1850));
    }

    [Fact]
    public void FlowerSpecies_AreRealFloralBlocks()
    {
        var content = BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        Assert.Equal(4, MeadowField.FlowerSpeciesKeys.Length);
        Assert.All(MeadowField.FlowerSpeciesKeys, key => Assert.True(content.Blocks.ContainsKey(key), key));
    }

    [Fact]
    public void TopVariant_MirrorsTheMeshersFormula()
    {
        // ChunkMesher: hash = wx*73856093 ^ wy*19349663 ^ wz*83492791; pick = hash % (variants + 1); turn = (hash >> 8) & 3.
        for (int x = -20; x < 20; x += 3)
        {
            for (int z = -20; z < 20; z += 7)
            {
                int hash = unchecked((x * 73856093) ^ (31 * 19349663) ^ (z * 83492791));
                var (pick, turn) = MeadowField.TopVariant(x, 31, z, 2);
                Assert.Equal((int)((uint)hash % 3u), pick);
                Assert.Equal((hash >> 8) & 3, turn);
                Assert.InRange(turn, 0, 3);
            }
        }

        Assert.Equal(0, MeadowField.TopVariant(7, 8, 9, 0).Pick); // no variants: always the block's own tile
    }

    [Fact]
    public void Density_FollowsTheSeasons()
    {
        static float At(string season, float progress)
        {
            var (autumn, winter, bloom) = SeasonLook.Target(season, progress);
            return MeadowField.FlowerDensity(autumn, winter, bloom);
        }

        Assert.Equal(MeadowField.SummerDensity, MeadowField.FlowerDensity(0f, 0f, 0f), 3); // no seasons
        Assert.Equal(1f, At(PlanetSeasons.Spring, 1f), 3);                                  // full bloom
        Assert.Equal(MeadowField.SummerDensity, At(PlanetSeasons.Summer, 1f), 3);
        Assert.Equal(MeadowField.SummerDensity * MeadowField.AutumnShare, At(PlanetSeasons.Autumn, 1f), 3);
        Assert.Equal(0f, At(PlanetSeasons.Winter, 0.5f), 3);                                // none in winter
        Assert.Equal(0f, At(PlanetSeasons.Winter, 1f), 3);
    }

    [Fact]
    public void Density_NeverRebloomsWhenWinterStarts_AndRisesThroughSpring()
    {
        static float At(string season, float progress)
        {
            var (autumn, winter, bloom) = SeasonLook.Target(season, progress);
            return MeadowField.FlowerDensity(autumn, winter, bloom);
        }

        // Winter starts with a share of the autumn look left: the field must keep thinning, never come back.
        float previous = At(PlanetSeasons.Autumn, 1f);
        foreach (float p in Enumerable.Range(0, 21).Select(i => i / 20f))
        {
            float d = At(PlanetSeasons.Winter, p);
            Assert.True(d <= previous + 1e-4f, $"winter {p}: {d} > {previous}");
            previous = d;
        }

        previous = At(PlanetSeasons.Winter, 1f);
        foreach (float p in Enumerable.Range(0, 21).Select(i => i / 20f))
        {
            float d = At(PlanetSeasons.Spring, p);
            Assert.True(d >= previous - 1e-4f, $"spring {p}: {d} < {previous}");
            previous = d;
        }
    }

    [Fact]
    public void Density_IsAShare_ForAnyInput()
    {
        foreach (float a in new[] { -1f, 0f, 0.3f, 1f, 2f })
        {
            foreach (float w in new[] { -1f, 0f, 0.5f, 1f, 2f })
            {
                foreach (float b in new[] { -1f, 0f, 1f, 2f })
                {
                    float d = MeadowField.FlowerDensity(a, w, b);
                    Assert.False(float.IsNaN(d));
                    Assert.InRange(d, 0f, 1f);
                }
            }
        }
    }

    [Fact]
    public void Sprite_HasFivePetals_ACentre_AndAStem()
    {
        byte[] px = FlowerSprite.Paint();
        Assert.Equal(FlowerSprite.Width * FlowerSprite.Height * 4, px.Length);
        (byte Shade, byte Petal, byte Stem, byte Alpha) At(int x, int y)
        {
            int i = ((y * FlowerSprite.Width) + x) * 4;
            return (px[i], px[i + 1], px[i + 2], px[i + 3]);
        }

        var centre = At(32, 32);
        Assert.Equal(255, centre.Alpha);
        Assert.Equal(0, centre.Petal);
        Assert.Equal(0, At(0, 0).Alpha);       // the blossom's corner is cut out
        Assert.Equal(0, At(127, 63).Alpha);    // and so is the stem half's

        // Walk a circle through the petals: five opaque arcs (counted as the opaque → cut-out steps around the loop).
        const int Steps = 720;
        bool Opaque(int step)
        {
            double t = step % Steps / (double)Steps * 2.0 * Math.PI;
            int x = (int)Math.Floor(32 + (Math.Cos(t) * 0.75 * FlowerSprite.BlossomRadius));
            int y = (int)Math.Floor(32 + (Math.Sin(t) * 0.75 * FlowerSprite.BlossomRadius));
            return At(x, y).Alpha > 127;
        }

        int arcs = Enumerable.Range(0, Steps).Count(s => Opaque(s) && !Opaque(s + 1));
        Assert.Equal(FlowerSprite.Petals, arcs);
        Assert.Equal(255, At(32 + 22, 32).Petal); // along a petal axis

        var stem = At(64 + 32, 50);
        Assert.Equal(255, stem.Alpha);
        Assert.Equal(255, stem.Stem);
    }
}
