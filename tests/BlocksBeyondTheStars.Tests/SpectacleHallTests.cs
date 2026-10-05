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
/// Cave portals, daylight halls (#2340) and the giant overhangs (#2339): a hall has open sky over its centre and
/// a solid roof around it, its floor is the ground under the skylight (flora grows there), its lake fills the
/// bowl, the placer refuses it; a portal is a mouth you fly into; a wave rock's curl hangs over air on one side
/// only; an abri undercuts a table's wall. Nothing below terrain generation 21.
/// </summary>
public sealed class SpectacleHallTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static BlockId BlockAt(WorldGenerator gen, PlanetType planet, int x, int y, int z)
    {
        var chunk = gen.Generate(planet, new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z)));
        return chunk.Get(WorldConstants.WorldToLocal(x), WorldConstants.WorldToLocal(y), WorldConstants.WorldToLocal(z));
    }

    private static (WorldGenerator Gen, PlanetType Planet, (int X, int Z, double Rx, double Rz, double Ry, int Cy, int LakeY) Hall) FindHall(Func<(int X, int Z, double Rx, double Rz, double Ry, int Cy, int LakeY), bool>? pick = null)
    {
        var planet = Content.Planets["karst"];
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 19, WorldDescription.SpectacleGeneration);
            foreach (var hall in gen.DaylightHallsForTest(planet))
            {
                // A hall whose centre column is really open to the sky (the terrain over it may be a hill the
                // ellipsoid does not reach).
                if (pick is not null && !pick(hall))
                {
                    continue;
                }

                if (gen.ColumnHasVoidBelow(planet, hall.X, hall.Z, 2))
                {
                    return (gen, planet, hall);
                }
            }
        }

        Assert.Fail("no daylight hall with an open centre on 60 karst worlds");
        return default;
    }

    [Fact]
    public void ADaylightHall_IsOpenOverItsCentre_RoofedAtItsRim_AndItsFloorIsTheGround()
    {
        var (gen, planet, hall) = FindHall();
        int floor = hall.Cy - (int)hall.Ry - 1;
        int surface = gen.SurfaceHeight(planet, hall.X, hall.Z);
        Assert.True(surface > floor + 20, "the terrain surface stands far above the floor");

        // Open sky: from the floor up to the old surface the column is air or lake water.
        var water = Content.GetBlock("water")!.NumericId;
        Assert.False(BlockAt(gen, planet, hall.X, floor, hall.Z).IsAir, "the floor is solid");
        for (int y = floor + 1; y <= surface + 2; y += 5)
        {
            var b = BlockAt(gen, planet, hall.X, y, hall.Z);
            Assert.True(b.IsAir || b == water, $"y={y} is {b} — the skylight column must be open");
        }

        // Toward the rim the roof closes: somewhere between 80 % and 100 % of the long half-axis the ground is solid at
        // the surface while the hall is still air under it (where the ground around the centre lies low, the skylight
        // reaches far out and the roofed rim is narrow — a sinkhole with overhanging edges).
        bool roofed = false;
        for (int dx = (int)(hall.Rx * 0.8); dx <= (int)hall.Rx && !roofed; dx++)
        {
            int rx = hall.X + dx, rz = hall.Z;
            int rimSurface = gen.SurfaceHeight(planet, rx, rz);
            roofed = !BlockAt(gen, planet, rx, rimSurface, rz).IsAir && gen.ColumnHasVoidBelow(planet, rx, rz, rimSurface - hall.Cy + 2);
        }

        Assert.True(roofed, "the hall keeps a roofed rim");

        // The placer never seats anything over it.
        Assert.False(gen.FootprintClear(planet, hall.X - 5, hall.Z - 5, 11, 11, 10));
    }

    [Fact]
    public void ADaylightHall_GrowsFloraOnItsFloor_AndHoldsItsLake()
    {
        var (gen, planet, hall) = FindHall(h => h.LakeY != int.MinValue);
        var water = Content.GetBlock("water")!.NumericId;
        int floorCentre = hall.Cy - (int)hall.Ry - 1;
        Assert.Equal(water, BlockAt(gen, planet, hall.X, floorCentre + 1, hall.Z)); // the lake over the bowl's bottom
        Assert.True(BlockAt(gen, planet, hall.X, hall.LakeY + 1, hall.Z).IsAir, "air over the lake");

        // Somewhere on the dry floor under the skylight a plant stands on the ground: walk the hall on a grid, and on
        // every column whose first solid cell under the old surface lies deep (the floor, not a roof) look above it.
        int plants = 0, floorColumns = 0;
        for (int dx = -(int)hall.Rx; dx <= (int)hall.Rx && plants == 0; dx += 3)
            for (int dz = -(int)hall.Rz; dz <= (int)hall.Rz && plants == 0; dz += 3)
            {
                int x = hall.X + dx, z = hall.Z + dz;
                int surface = gen.SurfaceHeight(planet, x, z);
                for (int y = surface; y > hall.Cy - (int)hall.Ry - 2; y--)
                {
                    var b = BlockAt(gen, planet, x, y, z);
                    if (b.IsAir || b == water)
                    {
                        continue;
                    }

                    if (y >= surface - 3)
                    {
                        break; // a roof, or plain ground outside the skylight
                    }

                    // The first cell that is neither air nor water is the floor — or the plant standing on it.
                    floorColumns++;
                    if (Content.Blocks.Values.FirstOrDefault(d => d.NumericId == b)?.Key.StartsWith("flora_", StringComparison.Ordinal) == true)
                    {
                        plants++;
                    }

                    break;
                }
            }

        Assert.True(floorColumns > 0, "the skylight exposes floor columns");
        Assert.True(plants > 0, $"the floor under the skylight carries flora ({floorColumns} floor columns seen)");
    }

    [Fact]
    public void APortal_IsAMouthYouFlyInto_AtTheMassifsFoot()
    {
        var planet = Content.Planets["karst"];
        Assert.Contains("portals", WorldGenerator.TunnelFamilyOrderForTest());
        Assert.Contains("abris", WorldGenerator.TunnelFamilyOrderForTest());
        Assert.Contains("hall-portals", WorldGenerator.TunnelFamilyOrderForTest());

        // A massif world of generation 21 with the karst tag carries portals; find a column where a wide carve
        // (radius ≥ 7 → a span ≥ 13 tall) reaches the surface: the mouth.
        bool found = false;
        Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
        for (long s = 1; s <= 40 && !found; s++)
        {
            var gen = Gen(s * 6151 + 23, WorldDescription.SpectacleGeneration);
            if (!gen.WonderGatesForTest(planet)["mountainHalls"])
            {
                continue;
            }

            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int x = 0; x < circ && !found; x += 7)
                for (int z = -period / 2; z < period / 2 && !found; z += 7)
                {
                    int n = gen.TunnelSpans(planet, x, z, spans);
                    int surface = gen.SurfaceHeight(planet, x, z);
                    for (int i = 0; i < n && !found; i++)
                    {
                        found = spans[i].Hi - spans[i].Lo >= 13 && spans[i].Hi >= surface - 1 && spans[i].Lo < surface;
                    }
                }
        }

        Assert.True(found, "a portal mouth — a carve at least 13 tall breaking the surface — exists on a generation-21 karst world");
    }

    [Fact]
    public void AWaveRock_CurlsOverAirOnOneSideOnly()
    {
        var planet = Content.Planets["desert"];
        var dirs = new (double X, double Z)[] { (1, 0), (0.7071, 0.7071), (0, 1), (-0.7071, 0.7071), (-1, 0), (-0.7071, -0.7071), (0, -1), (0.7071, -0.7071) };
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        bool checkedOne = false;
        for (long s = 1; s <= 40 && !checkedOne; s++)
        {
            var gen = Gen(s * 6151 + 29, WorldDescription.SpectacleGeneration);
            foreach (var wave in gen.WaveRocksForTest(planet))
            {
                if (gen.LandmarkOffsetForTest("wave-rock", planet, wave.X, wave.Z) < wave.Rise * 0.9
                    || gen.SurfaceHeight(planet, wave.X, wave.Z) != gen.RawSurfaceHeightForTest(planet, wave.X, wave.Z) + (int)Math.Round(gen.LandmarkOffsetForTest("wave-rock", planet, wave.X, wave.Z)))
                {
                    continue; // another row owns the crest
                }

                var d = dirs[wave.Dir];
                // "Across" is positive on the curl side: the left-hand normal of the bearing.
                double nx = -d.Z, nz = d.X;
                int cx = wave.X + (int)Math.Round(nx * (wave.HalfWidth + 3)), cz = wave.Z + (int)Math.Round(nz * (wave.HalfWidth + 3));
                int bx = wave.X - (int)Math.Round(nx * (wave.HalfWidth + 3)), bz = wave.Z - (int)Math.Round(nz * (wave.HalfWidth + 3));
                int n = gen.GetExtraBands(planet, cx, cz, bands);
                bool curl = false;
                for (int i = 0; i < n; i++)
                {
                    if (bands[i].Kind == WorldGenerator.BandKind.Rock)
                    {
                        curl = true;
                        Assert.True(bands[i].Bottom > gen.SurfaceHeight(planet, cx, cz) + 4, "air under the curl");
                        // The curl rides the crest's height over ITS column's raw ground, drooping at most one cell this close to the wall.
                        int expected = gen.RawSurfaceHeightForTest(planet, cx, cz) + (int)Math.Round(wave.Rise);
                        Assert.InRange(bands[i].Top, expected - 2, expected + 1);
                    }
                }

                Assert.True(curl, "the curl side carries the band");
                int m = gen.GetExtraBands(planet, bx, bz, bands);
                for (int i = 0; i < m; i++)
                {
                    Assert.NotEqual(WorldGenerator.BandKind.Rock, bands[i].Kind);
                }

                checkedOne = true;
                break;
            }
        }

        Assert.True(checkedOne, "a wave rock whose crest the row owns exists on 40 desert worlds");
    }

    [Fact]
    public void NothingBelowGenerationTwentyOne()
    {
        var karst = Content.Planets["karst"];
        var g20 = Gen(6166, WorldDescription.SpectacleGeneration - 1);
        Assert.Empty(g20.DaylightHallsForTest(karst));
        Assert.Empty(g20.WaveRocksForTest(Content.Planets["desert"]));
        Assert.False(g20.WonderGatesForTest(karst).ContainsKey("portals") && g20.WonderGatesForTest(karst)["portals"]);

        var g21 = Gen(6166, WorldDescription.SpectacleGeneration);
        Assert.Contains("wave-rock", g21.LandmarkOrderForTest(Content.Planets["desert"]));
        Assert.DoesNotContain("wave-rock", g21.LandmarkOrderForTest(karst));
    }
}
