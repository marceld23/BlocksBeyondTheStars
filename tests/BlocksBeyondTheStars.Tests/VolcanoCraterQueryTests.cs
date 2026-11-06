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

/// <summary>#3277/#3275: the read-only generator queries the client's world effects use — the volcano craters near a
/// point (the crater smoke) and whether a world's geyser vents are volcanic (ash) or water geysers (steam).</summary>
public class VolcanoCraterQueryTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static (WorldGenerator Gen, PlanetType Planet, int Circ) NewWorld(string planetKey, long seed, string body = "probe")
    {
        var gen = new WorldGenerator(seed, Content);
        var planet = Content.GetPlanet(planetKey)!;
        int circ = WorldConstants.CircumferenceFor(body, WorldConstants.WorldSizeClass.Planet, 0);
        gen.SetWorldMode(circ, cratered: false, landingPads: null, body);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        return (gen, planet, circ);
    }

    private static (WorldGenerator Gen, PlanetType Planet, int Circ, (int X, int Z, double Radius, double Height, int RimY, bool SeaMount) Volcano, long Seed) WorldWithVolcano(string planetKey)
    {
        for (long seed = 1; seed <= 16; seed++)
        {
            var (gen, planet, circ) = NewWorld(planetKey, seed);
            var all = gen.VolcanoesForTest(planet);
            if (all.Count > 0)
            {
                return (gen, planet, circ, all[0], seed);
            }
        }

        throw new Xunit.Sdk.XunitException($"no {planetKey} world with a volcano among 16 seeds");
    }

    private static List<VolcanoCrater> Near(WorldGenerator gen, PlanetType planet, int x, int z, int radius)
    {
        var list = new List<VolcanoCrater>();
        gen.VolcanoCratersNear(planet, x, z, radius, list);
        return list;
    }

    [Fact]
    public void ACrater_IsFoundFromItsFlank_WithTheGeneratorsOwnRimAndPool()
    {
        var (gen, planet, _, v, _) = WorldWithVolcano("lava");

        var hit = Assert.Single(Near(gen, planet, v.X + 150, v.Z - 80, 400), c => c.X == v.X && c.Z == v.Z);
        Assert.Equal(v.RimY, hit.RimY);
        Assert.Equal(System.Math.Max(4.0, v.Radius * 0.16), hit.Radius, 6);
        Assert.True(gen.TryGetVolcanoCrater(planet, v.X, v.Z, out int lavaTop));
        Assert.Equal(lavaTop, hit.LavaTopY);
        Assert.True(hit.RimY > hit.LavaTopY, "the pool lies inside the bowl, below the rim");
    }

    [Fact]
    public void TheRadius_IsRespected()
    {
        var (gen, planet, _, v, _) = WorldWithVolcano("lava");

        Assert.Contains(Near(gen, planet, v.X + 399, v.Z, 400), c => c.X == v.X && c.Z == v.Z);
        Assert.DoesNotContain(Near(gen, planet, v.X + 401, v.Z, 400), c => c.X == v.X && c.Z == v.Z);
        Assert.Empty(Near(gen, planet, v.X, v.Z, -1));
    }

    [Fact]
    public void Craters_AreReported_InTheQueryPointsFrame_AcrossTheSeam()
    {
        var (gen, planet, circ, v, _) = WorldWithVolcano("ocean");

        // A player one lap further east sees the same crater one lap further east — never on the far side of the world.
        var lap = Assert.Single(Near(gen, planet, v.X + circ + 20, v.Z, 100));
        Assert.Equal(v.X + circ, lap.X);
        Assert.Equal(v.Z, lap.Z);
        Assert.Equal(v.RimY, lap.RimY);
    }

    [Fact]
    public void TheQuery_IsDeterministic_AndEmptyOnAWorldWithoutVolcanoes()
    {
        var (a, planetA, _, v, seed) = WorldWithVolcano("desert");
        var (b, planetB, _) = NewWorld("desert", seed);
        Assert.Equal(
            Near(a, planetA, v.X, v.Z, 2000).Select(c => (c.X, c.Z, c.RimY, c.LavaTopY)),
            Near(b, planetB, v.X, v.Z, 2000).Select(c => (c.X, c.Z, c.RimY, c.LavaTopY)));

        // A cratered airless body is geologically dead: no volcano, no crater, no smoke.
        var dead = new WorldGenerator(5, Content);
        int circ = WorldConstants.CircumferenceFor("rock", WorldConstants.WorldSizeClass.Asteroid, 0);
        dead.SetWorldMode(circ, cratered: true, landingPads: null, "rock");
        dead.SetLavaCoreVolcanoes(true);
        Assert.Empty(Near(dead, Content.GetPlanet("asteroid")!, 0, 0, 5000));
    }

    [Theory]
    [InlineData("lava", true)]          // the volcanic tag
    [InlineData("ashen", true)]         // lava abundance
    [InlineData("desert", true)]        // dry: its vents come from its volcanoes alone
    [InlineData("ocean", false)]        // wet: water geysers
    [InlineData("meadowlands", false)]
    [InlineData("rocky", false)]        // no water set, but an atmosphere: counted wet
    public void VentLook_FollowsTheWorldsTraits(string planetKey, bool volcanic)
    {
        var (gen, planet, _) = NewWorld(planetKey, 3);
        Assert.Equal(volcanic, gen.VentsAreVolcanic(planet));
    }
}
