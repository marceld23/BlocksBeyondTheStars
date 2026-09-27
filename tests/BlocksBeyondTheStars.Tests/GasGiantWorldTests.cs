// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gas giant's world side (#2112, Justus' "where are the gases?", generation 18): the type's data, the gas sea over the
/// whole heightfield with the islands above it, the galaxy placement (the lone giant's planet, an outermost-orbit roll,
/// rings), the all-flying roster and the sky giant's rules. Nothing of it exists below generation 18.
/// </summary>
public sealed class GasGiantWorldTests
{
    private const string Key = "gas_giant";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation = WorldDescription.GasGiantGeneration)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    [Fact]
    public void TheType_IsAGasSeaWithIslands_ColdToxicAndStormy_PlacedOnlyByTheGalaxy()
    {
        var p = Content.GetPlanet(Key);
        Assert.NotNull(p);
        Assert.True(p!.IsGasWorld);
        Assert.Equal("gas", p.SeaFluid);
        Assert.True(p.FloatingIslands, "the islands are the only ground");
        Assert.Equal(WorldDescription.GasGiantGeneration, p.MinTerrainGeneration);
        Assert.Equal(-120.0, p.BaseTemperature);
        Assert.Equal("toxic", p.Atmosphere);
        Assert.Equal("stormy", p.Weather);
        Assert.Equal(0, p.SpawnWeight); // never the ordinary type roll — the galaxy places it (ApplyGasGiants)
        Assert.True(p.Exotic);
        Assert.Equal(0.0, p.WaterAbundance);
        Assert.True(string.IsNullOrEmpty(p.WaterTint));

        var gas = Content.GetBlock("gas");
        Assert.NotNull(gas);
        Assert.True(gas!.Liquid, "a still liquid, never the automaton");
        Assert.False(gas.Solid);
        Assert.False(gas.Mineable);
        Assert.Empty(gas.Drops); // nothing to pump

        // Every classic type keeps the no-op default.
        foreach (var other in Content.Planets.Values.Where(t => t.Key != Key))
        {
            Assert.False(other.IsGasWorld, other.Key);
        }

        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (string key in new[] { "planet.gas_giant.name", "planet.gas_giant.desc", "vega.hint.world.gas_giant", "srv.death.gas", "block.gas.name", "ui.hud.city_air", "achv.sky_giant.name", "achv.sky_giant.desc" })
        {
            Assert.Contains("\"" + key + "\"", en);
            Assert.Contains("\"" + key + "\"", de);
        }
    }

    [Fact]
    public void TheGasSea_FloodsTheWholeHeightfield_AndTheIslandsRiseAboveIt()
    {
        var planet = Content.GetPlanet(Key)!;
        var gen = Gen(20260927);
        Assert.True(gen.SeaIsGas(planet));
        Assert.False(gen.SeaIsWater(planet));
        int sea = gen.SeaLevel(planet);
        Assert.NotEqual(int.MinValue, sea);

        var gasId = Content.GetBlock("gas")!.NumericId;
        int islands = 0, columns = 0;
        for (int x = 16; x < 400; x += 23)
            for (int z = -160; z < 160; z += 19)
            {
                columns++;
                int ground = gen.SurfaceHeight(planet, x, z);
                Assert.True(ground + WorldGenerator.GasSeaRise <= sea, $"the ground at ({x},{z}) is {ground}, the gas stands at {sea} — nothing of the heightfield may show");

                // The column itself: gas at the sea level, air right above it (unless an island hangs there), rock under the gas.
                var atSea = gen.Generate(planet, WorldConstants.WorldToChunk(new Vector3i(x, sea, z)));
                var o = WorldConstants.ChunkOrigin(WorldConstants.WorldToChunk(new Vector3i(x, sea, z)));
                Assert.Equal(gasId, atSea.Get(x - o.X, sea - o.Y, z - o.Z));
                int top = gen.FloatingIslandTop(planet, x, z);
                if (top != int.MinValue)
                {
                    islands++;
                    Assert.True(top > sea, $"an island top at {top} lies in the gas ({sea})");
                }
            }

        Assert.True(islands > 0, $"no floating island over {columns} sampled columns");
        Assert.True(islands < columns, "the islands must leave open gas between them");
    }

    [Fact]
    public void TheGalaxy_PutsAGasGiantOnTheLoneGiant_AndSometimesOnTheOutermostOrbit_ButNeverBelowGenerationEighteen()
    {
        WorldDescription Desc(int generation) => new()
        {
            StarSystemCount = 60,
            PlanetsPerSystemMin = 2,
            PlanetsPerSystemMax = 6,
            MoonsPerPlanetMin = 0,
            MoonsPerPlanetMax = 2,
            SystemVariance = true,
            TerrainGeneration = generation,
        };

        var older = new UniverseGenerator(4242, Desc(WorldDescription.GasGiantGeneration - 1), Content).Generate();
        Assert.DoesNotContain(older.AllBodies(), b => b.PlanetType == Key);

        var galaxy = new UniverseGenerator(4242, Desc(WorldDescription.GasGiantGeneration), Content).Generate();
        int loneGiants = 0, loneGas = 0, outerGas = 0, ringed = 0;
        for (int si = 1; si < galaxy.Systems.Count; si++)
        {
            var system = galaxy.Systems[si];
            var planets = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            bool lone = SystemArchetypes.ForIndex(4242, si) == SystemArchetype.LoneGiant;
            if (lone)
            {
                loneGiants++;
                if (planets.Count > 0 && planets[0].PlanetType == Key)
                {
                    loneGas++;
                }
            }

            foreach (var planet in planets.Where(b => b.PlanetType == Key))
            {
                Assert.True(lone || ReferenceEquals(planet, planets[^1]), $"a gas giant off the outermost orbit in system {si}");
                if (!lone)
                {
                    outerGas++;
                }

                if (planet.RingSeed != 0)
                {
                    ringed++;
                }
            }

            // A gas giant's moons keep their own types — they are still landable worlds.
            foreach (var moon in system.Bodies.Where(b => b.Kind == CelestialKind.Moon))
            {
                Assert.NotEqual(Key, moon.PlanetType);
            }
        }

        Assert.True(loneGiants > 0, "the layout rolled no lone-giant system");
        Assert.True(loneGas >= loneGiants - 2, $"only {loneGas} of {loneGiants} lone giants are gas giants"); // the first breathable and a once-per-galaxy landmark are spared
        Assert.True(outerGas > 0, "no outermost-orbit gas giant in 60 systems");
        Assert.True(ringed > 0, "no gas giant wears rings");
        Assert.DoesNotContain(galaxy.Systems[0].Bodies, b => b.PlanetType == Key); // never the start system
    }

    [Fact]
    public void TheRoster_AllFlies_AndTheSkyGiant_IsThePassiveGiantOfEveryGasGiant()
    {
        var planet = Content.GetPlanet(Key)!;
        var roster = CreatureGenerator.GenerateRoster(planet, 77, WorldDescription.GasGiantGeneration);
        Assert.NotEmpty(roster);
        Assert.All(roster, sp => Assert.Equal(CreatureHabitat.Air, sp.Habitat));
        Assert.DoesNotContain(roster, sp => sp.IsGiant);

        Assert.True(GiantRules.HostsSkyGiant(planet, WorldDescription.GasGiantGeneration));
        Assert.False(GiantRules.HostsSkyGiant(planet, WorldDescription.GasGiantGeneration - 1));
        Assert.False(GiantRules.HostsSkyGiant(Content.GetPlanet("ocean")!, WorldDescription.GasGiantGeneration));
        Assert.False(GiantRules.HostsSkyGiant(Content.GetPlanet("skylands")!, WorldDescription.GasGiantGeneration)); // islands over ground, not over gas

        for (int i = 0; i < 30; i++)
        {
            var a = CreatureGenerator.GenerateSkyGiant(900 + i, "sys7-p1");
            var b = CreatureGenerator.GenerateSkyGiant(900 + i, "sys7-p1");
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.WormLength, b.WormLength);
            Assert.Equal(CreatureBodyPlan.SkyGiant, a.BodyPlan);
            Assert.Equal(CreatureHabitat.Air, a.Habitat);
            Assert.Equal(CreatureTemperament.Passive, a.Temperament); // a spectacle, never a threat
            Assert.True(a.IsGiant);
            Assert.False(a.IsBurrowingGiant);
            Assert.InRange(a.WormLength, 40f, 80f);
            Assert.InRange(a.WingPairs, 3, 5);
            Assert.Equal(0f, a.AttackDamage);
        }
    }
}
