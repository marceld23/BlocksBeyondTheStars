// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1631: volcanoes on every world with a lava core, and sea-mount cones that rise out of the sea.</summary>
public class VolcanoLavaCoreTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static (WorldGenerator Gen, PlanetType Planet) NewWorld(string planetKey, long seed, string body = "probe")
    {
        var gen = new WorldGenerator(seed, Content);
        var planet = Content.GetPlanet(planetKey)!;
        int circ = WorldConstants.CircumferenceFor(body, WorldConstants.WorldSizeClass.Planet, 0);
        gen.SetWorldMode(circ, cratered: false, landingPads: null, body);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        return (gen, planet);
    }

    [Fact]
    public void LegacySaves_KeepTheOldVolcanoRule()
    {
        // A save without the LavaCoreVolcanoes flag (every world created before #1631) keeps the #477 rule:
        // no cones on a desert world, and a submerged ocean cone stays at its rolled height.
        var gen = new WorldGenerator(3, Content);
        int circ = WorldConstants.CircumferenceFor("old", WorldConstants.WorldSizeClass.Planet, 0);
        gen.SetWorldMode(circ, cratered: false, landingPads: null, "old");
        gen.SetContinentsEnabled(true);
        Assert.Empty(gen.VolcanoesForTest(Content.GetPlanet("desert")!));

        var ocean = Content.GetPlanet("ocean")!;
        foreach (var v in gen.VolcanoesForTest(ocean))
        {
            Assert.InRange(v.Height, 24.0, 46.0);
            Assert.InRange(v.Radius, 34.0, 60.0);
        }
    }

    [Fact]
    public void OceanWorld_SubmergedConesRiseOutOfTheSea()
    {
        // A cone whose centre lies under the sea is lifted until its crater rim clears the water (12–36 blocks
        // nominal at the centre column; the rim column's own seabed undulation allows a few blocks of slack).
        int seaMounts = 0;
        for (long seed = 1; seed <= 12; seed++)
        {
            var (gen, planet) = NewWorld("ocean", seed);
            int sea = gen.SeaLevel(planet);
            Assert.NotEqual(int.MinValue, sea);
            foreach (var v in gen.VolcanoesForTest(planet))
            {
                if (!v.SeaMount)
                {
                    continue;
                }

                seaMounts++;
                Assert.True(v.RimY >= sea + 6, $"seed {seed}: sea-mount at {v.X},{v.Z} rim y={v.RimY} sits under sea {sea} (height {v.Height:F0}, radius {v.Radius:F0})");
                Assert.True(v.Radius <= 60.0 + 24.0, "the base never grows past the placement margin");
            }
        }

        Assert.True(seaMounts > 0, "twelve ocean worlds are expected to hold at least one submerged hotspot cone");
    }

    [Fact]
    public void LavaCoreWorlds_GrowVolcanoes_CrateredBodiesDoNot()
    {
        // Desert (no water at all) and lava worlds now qualify — they have a lava core like every non-cratered
        // body; a cratered asteroid stays dead.
        int desert = 0, lava = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            desert += NewWorld("desert", seed).Gen.VolcanoesForTest(Content.GetPlanet("desert")!).Count;
            lava += NewWorld("lava", seed).Gen.VolcanoesForTest(Content.GetPlanet("lava")!).Count;
        }

        Assert.True(desert > 0, "desert worlds should grow volcanoes now");
        Assert.True(lava > 0, "lava worlds should grow volcanoes now");

        var gen = new WorldGenerator(5, Content);
        var asteroid = Content.GetPlanet("asteroid")!;
        int circ = WorldConstants.CircumferenceFor("rock", WorldConstants.WorldSizeClass.Asteroid, 0);
        gen.SetWorldMode(circ, cratered: true, landingPads: null, "rock");
        gen.SetLavaCoreVolcanoes(true);
        Assert.Empty(gen.VolcanoesForTest(asteroid));
    }

    [Fact]
    public void SeaMountLift_IsDeterministic_AndIndependentOfQueryOrder()
    {
        // The calibration samples SurfaceHeight before the sea level exists (cones un-lifted); those memoised
        // columns must be dropped, so a generator that asks for a rim height FIRST agrees with one that
        // resolved the sea level first, and with a third generator built from the same seed.
        (int X, int Z, double Radius, double Height, int RimY, bool SeaMount)? mount = null;
        long seed = 0;
        for (long s = 1; s <= 12 && mount is null; s++)
        {
            var (g, p) = NewWorld("ocean", s);
            mount = g.VolcanoesForTest(p).FirstOrDefault(v => v.SeaMount) is { SeaMount: true } m ? m : null;
            seed = s;
        }

        Assert.NotNull(mount);
        var rim = (mount!.Value.X + (int)System.Math.Round(System.Math.Max(4.0, mount.Value.Radius * 0.16)), mount.Value.Z);

        var (first, planetA) = NewWorld("ocean", seed);
        int viaSurfaceFirst = first.SurfaceHeight(planetA, rim.Item1, rim.Item2);

        var (second, planetB) = NewWorld("ocean", seed);
        _ = second.SeaLevel(planetB);
        int viaSeaFirst = second.SurfaceHeight(planetB, rim.Item1, rim.Item2);

        Assert.Equal(viaSeaFirst, viaSurfaceFirst);
        Assert.Equal(mount.Value.RimY, viaSurfaceFirst);
        Assert.Equal(first.VolcanoesForTest(planetA), second.VolcanoesForTest(planetB));
    }
}
