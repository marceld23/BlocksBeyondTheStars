// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Terrain extremes + landform variety (#576–#580): drama tail, archetype pool, landmark
/// landforms (table mountains / massifs / rifts), the new styled types and the deepened build band.</summary>
public class TerrainExtremesTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static double InvokeDrama(long seed)
    {
        var m = typeof(WorldGenerator).GetMethod("DramaFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (double)m.Invoke(null, new object[] { seed })!;
    }

    [Fact]
    public void DramaFor_HasExtremeTail_AtRoughlySixPercent()
    {
        // #576: most bodies roll the classic 0.9–1.5, a small tail rolls 1.9–2.6 — never anything else.
        int extreme = 0, normal = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            double d = InvokeDrama(i * 981_759_113L + 17);
            if (d >= 1.9 - 1e-9 && d <= 2.6 + 1e-9) extreme++;
            else if (d >= 0.9 - 1e-9 && d <= 1.5 + 1e-9) normal++;
            else Assert.Fail($"drama {d} outside both bands");
        }

        double frac = extreme / (double)n;
        Assert.InRange(frac, 0.04, 0.09);
        Assert.Equal(n, extreme + normal);
    }

    [Fact]
    public void SurfaceHeight_NeverExceedsTheAtmosphereSafeCap()
    {
        // #577/#578: no natural column — archetype, style, drama or landmark — may poke a player "into
        // space" on foot. The cap sits safely under the ~Y 320 atmosphere line.
        var content = Content();
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        foreach (var planet in content.Planets.Values)
        {
            foreach (long seed in new long[] { 7, 4711 })
            {
                var gen = new WorldGenerator(seed, content);
                for (int z = -period / 2; z < period / 2; z += 96)
                    for (int x = 0; x < WorldConstants.Circumference; x += 96)
                    {
                        int h = gen.SurfaceHeight(planet, x, z);
                        Assert.True(h <= 288, $"{planet.Key} seed {seed} at ({x},{z}): Y {h} > 288");
                    }
            }
        }
    }

    [Theory]
    [InlineData("savanna")]    // buttes + massifs + rifts (no fixed style)
    [InlineData("tablelands")] // new styled type with butte landmarks
    [InlineData("badlands")]   // new styled type
    [InlineData("karst")]      // new styled type
    [InlineData("jungle")]     // massifs + rifts on a blend world
    public void SurfaceHeight_WrapsSeamFree_OnLandmarkAndNewStyleWorlds(string key)
    {
        var content = Content();
        var planet = content.GetPlanet(key);
        Assert.NotNull(planet);
        var gen = new WorldGenerator(99, content);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);

        for (int z = -period / 2; z < period / 2; z += 61)
            for (int x = 0; x < circ; x += 173)
            {
                Assert.Equal(gen.SurfaceHeight(planet!, x, z), gen.SurfaceHeight(planet!, x + circ, z));
                Assert.Equal(gen.SurfaceHeight(planet!, x, z), gen.SurfaceHeight(planet!, x, z + period));
            }
    }

    [Fact]
    public void TableMountains_AreFlatToppedAndSteepWalled()
    {
        // #577: locate a butte via its (private) offset field and verify the landmark shape — a dead-flat
        // cap and a wall that gains most of the height over the outer third of the radius.
        var content = Content();
        var planet = content.GetPlanet("savanna")!;
        var gen = new WorldGenerator(7, content);
        long seed = (long)typeof(WorldGenerator)
            .GetMethod("PlanetSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(gen, new object[] { planet })!;
        var mOff = typeof(WorldGenerator)
            .GetMethod("TableMountainOffset", BindingFlags.NonPublic | BindingFlags.Instance)!;
        double Off(int x, int z) => (double)mOff.Invoke(gen, new object[] { seed, x, z })!;

        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        int foundX = int.MinValue, foundZ = 0;
        double best = 0;
        for (int z = -period / 2; z < period / 2 && best < 25; z += 16)
            for (int x = 0; x < WorldConstants.Circumference; x += 16)
            {
                double o = Off(x, z);
                if (o > best)
                {
                    best = o;
                    foundX = x;
                    foundZ = z;
                }
            }

        Assert.True(best > 25, $"no table mountain found on the scan grid (best offset {best})");

        // The scan hit SOME cap column — possibly right at the cap's edge. Find the cap's centroid (all
        // columns within 3 blocks of the cap height in a window around the hit) and test flatness there.
        long sumX = 0, sumZ = 0;
        int capCols = 0;
        for (int dz = -140; dz <= 140; dz += 8)
            for (int dx = -140; dx <= 140; dx += 8)
            {
                if (Off(foundX + dx, foundZ + dz) > best - 3.0)
                {
                    sumX += foundX + dx;
                    sumZ += foundZ + dz;
                    capCols++;
                }
            }

        int cX = (int)(sumX / capCols), cZ = (int)(sumZ / capCols);

        // Flat cap: the 17×17 neighbourhood around the cap centroid stays within a ~±2.5-block roll.
        double min = double.MaxValue, max = double.MinValue;
        for (int dz = -8; dz <= 8; dz += 4)
            for (int dx = -8; dx <= 8; dx += 4)
            {
                double o = Off(cX + dx, cZ + dz);
                min = System.Math.Min(min, o);
                max = System.Math.Max(max, o);
            }

        Assert.True(max - min <= 5.0, $"butte top is not flat: roll {max - min}");

        // Steep wall: within 130 blocks (the max radius + margin) the offset falls back to ~0.
        bool fellOff = false;
        for (int d = 8; d <= 150 && !fellOff; d += 8)
        {
            fellOff = Off(foundX + d, foundZ) < 2.0;
        }

        Assert.True(fellOff, "butte never fell back to ground level within 150 blocks");
    }

    [Fact]
    public void NewPlanetTypes_AreSelectable_AndGenerateChunks()
    {
        // #579: the three new types exist, are part of the selectable pool, and produce terrain.
        var content = Content();
        foreach (var key in new[] { "tablelands", "badlands", "karst" })
        {
            var planet = content.GetPlanet(key);
            Assert.NotNull(planet);
            Assert.True(planet!.Selectable, $"{key} must be selectable");
            Assert.Equal(key == "karst", planet.Exotic); // karst is the only exotic of the three

            var gen = new WorldGenerator(12345, content);
            var chunk = gen.Generate(planet, new ChunkCoord(1, 3, 2));
            Assert.NotNull(chunk);
        }
    }

    [Fact]
    public void BuildBand_CoversTheDeepestWorldFloor()
    {
        // #580: the deepest foundation roll (surface − 2048 from the lowest BaseHeight 48 → ~Y −2000)
        // must sit inside the vertical build band, so "dig to the bedrock" works on every world.
        var minBuildY = (int)typeof(BlocksBeyondTheStars.GameServer.GameServer)
            .GetField("MinBuildY", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        Assert.True(minBuildY <= 48 - 2048, $"MinBuildY {minBuildY} walls off the deepest world floors");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void DeepCaves_BelowTheLavaTable_AreNotAllLava()
    {
        // #580: below the lava table SOME caverns stay open (the molten-pocket split) — digging deep must
        // stay explorable, not a uniform lava bath. Scan well below the deepest possible table (128).
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var gen = new WorldGenerator(7, content);
        var lavaId = content.GetBlock("lava")!.NumericId;

        int air = 0, lava = 0;
        for (int cx = 0; cx < 10 && (air == 0 || lava == 0); cx++)
            for (int cz = 0; cz < 10 && (air == 0 || lava == 0); cz++)
            {
                // Surface ≈ Y 64 → chunk Y −8..−11 is depth ≈ 190–250: below every possible lava table.
                for (int cy = -11; cy <= -8; cy++)
                {
                    var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                    for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                        for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
                            for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
                            {
                                var b = chunk.Get(lx, ly, lz);
                                if (b.IsAir) air++;
                                else if (b == lavaId) lava++;
                            }
                }
            }

        Assert.True(air > 0, "no open cave cells found below the lava table — the deep is a solid bath");
        Assert.True(lava > 0, "no molten cells found below the lava table — the danger half is missing");
    }
}
