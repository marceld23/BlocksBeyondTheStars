// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 40 (#3567, #3568, #3572): a reserved site is level at its floor in the generator itself, the land
/// around it is graded within the site's limits, the water rules hold, pads win, and no tree, prop or cave stands in it.
/// </summary>
public sealed class SiteGradeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Gen40 = WorldDescription.GeographyGeneration;

    private static WorldGenerator Gen(long seed, IReadOnlyList<SiteFlat>? sites, int generation = Gen40,
        IReadOnlyList<LandingPadFlatten>? pads = null)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        gen.SetWorldMode(WorldConstants.Circumference, cratered: false, pads, "sys0-p3", siteFlats: sites);
        return gen;
    }

    /// <summary>A dry, hilly column of a meadow world to put a test site on (the first one from a fixed scan).</summary>
    private static (int X, int Z, int Y) DrySpot(WorldGenerator gen, PlanetType planet, int minSpread)
    {
        int sea = gen.SeaLevel(planet);
        for (int x = 300; x < 6000; x += 37)
        {
            int z = (x * 7 % 1200) - 600;
            int h = gen.SurfaceHeight(planet, x, z);
            int spread = Enumerable.Range(0, 5).Select(i => gen.SurfaceHeight(planet, x + (i * 6), z + (i * 5))).Max()
                         - Enumerable.Range(0, 5).Select(i => gen.SurfaceHeight(planet, x + (i * 6), z + (i * 5))).Min();
            if (h > sea + 4 && spread >= minSpread && !gen.IsSurfaceWater(planet, x, z))
            {
                return (x, z, h);
            }
        }

        throw new InvalidOperationException("no dry spot found");
    }

    [Fact]
    public void TheBox_IsLevelAtItsFloor_AndTheGradeMeetsTheLand()
    {
        var planet = Content.Planets["highland"];
        var probe = Gen(5, null);
        var (x, z, y) = DrySpot(probe, planet, 6);
        var site = new SiteFlat(x, z, 24, 18, y, 16, 24, 24);
        var gen = Gen(5, new[] { site });
        for (int dx = 0; dx < site.Width; dx++)
            for (int dz = 0; dz < site.Length; dz++)
            {
                int natural = probe.SurfaceHeight(planet, x + dx, z + dz);
                int expected = Math.Clamp(site.FloorY, natural - site.MaxCut, natural + site.MaxFill);
                Assert.Equal(expected, gen.SurfaceHeight(planet, x + dx, z + dz));
            }

        // Past the radius the ground is the land's own; inside it every column stays within the limits.
        for (int dx = -24; dx < site.Width + 24; dx++)
        {
            int wx = x + dx, wz = z - 20;
            int before = probe.SurfaceHeight(planet, wx, wz), after = gen.SurfaceHeight(planet, wx, wz);
            Assert.InRange(after, before - site.MaxCut, before + site.MaxFill);
        }

        Assert.Equal(probe.SurfaceHeight(planet, x - 40, z - 40), gen.SurfaceHeight(planet, x - 40, z - 40));
    }

    [Fact]
    public void TheGrade_HasNoStepLargerThanTheLandsOwn()
    {
        var planet = Content.Planets["highland"];
        var probe = Gen(5, null);
        var (x, z, y) = DrySpot(probe, planet, 6);
        var site = new SiteFlat(x, z, 20, 20, y, 16, 24, 24);
        var gen = Gen(5, new[] { site });
        for (int dz = -20; dz < 40; dz += 3)
        {
            for (int dx = -20; dx < 40; dx++)
            {
                int a = gen.SurfaceHeight(planet, x + dx, z + dz), b = gen.SurfaceHeight(planet, x + dx + 1, z + dz);
                int na = probe.SurfaceHeight(planet, x + dx, z + dz), nb = probe.SurfaceHeight(planet, x + dx + 1, z + dz);
                Assert.True(Math.Abs(a - b) <= Math.Abs(na - nb) + 4,
                    $"a step of {Math.Abs(a - b)} at ({x + dx},{z + dz}) where the land steps {Math.Abs(na - nb)}");
            }
        }
    }

    [Fact]
    public void ASite_AcrossTheXSeam_GradesTheSameOnBothSides()
    {
        var planet = Content.Planets["meadowlands"];
        var probe = Gen(9, null);
        int sea = probe.SeaLevel(planet);
        int zs = Enumerable.Range(0, 200).Select(i => (i * 7) - 700).First(z0 => Enumerable.Range(-12, 24).All(
            dx => probe.SurfaceHeight(planet, dx, z0 + 5) > sea + 3 && !probe.IsSurfaceWater(planet, dx, z0 + 5)));
        int y = probe.SurfaceHeight(planet, 0, zs + 5);
        var site = new SiteFlat(-10, zs - 5, 20, 16, y, 12, 16, 16);
        var gen = Gen(9, new[] { site });
        int circ = WorldConstants.Circumference;
        for (int dx = -24; dx <= 24; dx++)
        {
            Assert.Equal(gen.SurfaceHeight(planet, dx, zs + 3), gen.SurfaceHeight(planet, dx + circ, zs + 3));
        }

        Assert.Equal(Math.Clamp(y, probe.SurfaceHeight(planet, -5, zs + 5) - 16, probe.SurfaceHeight(planet, -5, zs + 5) + 16),
            gen.SurfaceHeight(planet, circ - 5, zs + 5));
    }

    [Fact]
    public void APad_WinsOverAGrade_AndAGrade_NeverRaisesTheSea()
    {
        var planet = Content.Planets["archipelago"];
        var probe = Gen(3, null);
        int sea = probe.SeaLevel(planet);
        Assert.NotEqual(int.MinValue, sea);
        // A site box over open sea: the sea columns keep their seabed, whatever the floor asks.
        var wet = Enumerable.Range(0, 400).Select(i => (X: 200 + (i * 13), Z: (i * 31 % 900) - 450))
            .First(c => probe.SurfaceHeight(planet, c.X, c.Z) < sea - 3);
        var site = new SiteFlat(wet.X - 4, wet.Z - 4, 8, 8, sea + 6, 10, 32, 32);
        var gen = Gen(3, new[] { site });
        Assert.Equal(probe.SurfaceHeight(planet, wet.X, wet.Z), gen.SurfaceHeight(planet, wet.X, wet.Z));

        // A pad inside a site: the pad's levelling owns its columns.
        var land = DrySpot(probe, planet, 0);
        var pad = new LandingPadFlatten(land.X, land.Z, land.Y, 8);
        var padSite = new SiteFlat(land.X - 12, land.Z - 12, 24, 24, land.Y + 5, 10, 16, 16);
        var withPad = Gen(3, new[] { padSite }, pads: new[] { pad });
        var padOnly = Gen(3, null, pads: new[] { pad });
        Assert.Equal(padOnly.SurfaceHeight(planet, land.X, land.Z), withPad.SurfaceHeight(planet, land.X, land.Z));
    }

    [Fact]
    public void NoTreePropOrCave_StandsInTheBox()
    {
        var planet = Content.Planets["boreal"];
        var probe = Gen(11, null);
        var (x, z, y) = DrySpot(probe, planet, 0);
        var site = new SiteFlat(x, z, 32, 32, y, 16, 24, 24);
        var gen = Gen(11, new[] { site });
        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        BlockId Cell(int wx, int wy, int wz)
        {
            var c = new ChunkCoord(WorldConstants.WorldToChunk(wx), WorldConstants.WorldToChunk(wy), WorldConstants.WorldToChunk(wz));
            if (!chunks.TryGetValue(c, out var chunk))
            {
                chunks[c] = chunk = gen.Generate(planet, c);
            }

            const int cs = WorldConstants.ChunkSize;
            return chunk.Get(((wx % cs) + cs) % cs, ((wy % cs) + cs) % cs, ((wz % cs) + cs) % cs);
        }

        var log = Content.GetBlock("wood_log")!.NumericId;
        for (int dx = 0; dx < site.Width; dx++)
            for (int dz = 0; dz < site.Length; dz++)
            {
                int floor = gen.SurfaceHeight(planet, x + dx, z + dz);
                if (floor != site.FloorY)
                {
                    continue; // a limit held the grade: the stamper's cut and skirt own that column
                }

                for (int wy = floor - 8; wy <= floor; wy++)
                {
                    Assert.False(Cell(x + dx, wy, z + dz).IsAir, $"a hole at ({x + dx},{wy},{z + dz}) under the floor {floor}");
                }

                for (int wy = floor + 1; wy <= floor + 12; wy++)
                {
                    Assert.NotEqual(log, Cell(x + dx, wy, z + dz));
                }
            }
    }

    [Fact]
    public void AnOlderWorld_IgnoresSites()
    {
        var planet = Content.Planets["highland"];
        var probe = Gen(5, null, WorldDescription.SchoolClubLandmarksGeneration);
        var (x, z, y) = DrySpot(probe, planet, 6);
        var old = Gen(5, new[] { new SiteFlat(x, z, 20, 20, y + 9, 16, 24, 24) }, WorldDescription.SchoolClubLandmarksGeneration);
        Assert.Equal(probe.SurfaceHeight(planet, x + 5, z + 5), old.SurfaceHeight(planet, x + 5, z + 5));
    }

    [Fact]
    public void TheSiteText_RoundTrips_AndSkipsABrokenEntry()
    {
        var sites = new[] { new SiteFlat(-12, 40, 30, 22, 71, 16, 24, 24), new SiteFlat(900, -300, 12, 12, 64, 10, 12, 12) };
        string text = SiteFlat.Format(sites);
        Assert.True(SiteFlat.SameList(sites, SiteFlat.Parse(text, out int bad)));
        Assert.Equal(0, bad);
        Assert.True(SiteFlat.SameList(sites, SiteFlat.Unpack(SiteFlat.Pack(sites))));
        var broken = SiteFlat.Parse(text + ";1,2,x,4,5,6,7,8;1,2", out bad);
        Assert.Equal(2, bad);
        Assert.Equal(2, broken.Count);
    }
}
