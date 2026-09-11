// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 3, part 8 — the three planet types where the new families are dense (coral_sea, icecap,
/// river_lowlands): data-only, gated to generation-3 galaxies through <c>minTerrainGeneration</c>, named in
/// every locale, and each activating the families it was made for.
/// </summary>
public sealed class LandscapeGen3WorldsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] Gen3Types = { "coral_sea", "icecap", "river_lowlands" };

    [Fact]
    public void Gen3Types_AreDataComplete_AndGatedToGenerationThree()
    {
        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (var key in Gen3Types)
        {
            var p = Content.GetPlanet(key);
            Assert.NotNull(p);
            Assert.True(p!.Selectable);
            Assert.Equal(3, p.MinTerrainGeneration);
            Assert.InRange(p.SpawnWeight, 3, 8);
            Assert.True(p.TerrainStyles.Count >= 2, $"{key} has no style pool");
            Assert.Contains($"\"planet.{key}.name\"", en);
            Assert.Contains($"\"planet.{key}.name\"", de);
            Assert.Contains($"\"planet.{key}.desc\"", en);
            Assert.Contains($"\"planet.{key}.desc\"", de);
        }
    }

    [Fact]
    public void Gen3Types_EnterOnlyGenerationThreeGalaxies()
    {
        var gen1 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = 1 };
        var gen3 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = 3 };
        int onGen3 = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var body in new UniverseGenerator(seed, gen1, Content).Generate().Systems.SelectMany(s => s.Bodies))
            {
                Assert.DoesNotContain(body.PlanetType, Gen3Types);
            }

            onGen3 += new UniverseGenerator(seed, gen3, Content).Generate().Systems.SelectMany(s => s.Bodies).Count(b => Gen3Types.Contains(b.PlanetType));
        }

        Assert.True(onGen3 > 0, "no generation-3 galaxy rolled a new planet type in 40 seeds");
    }

    [Fact]
    public void Gen3Types_ActivateTheirFamilies_AndGenerateChunks()
    {
        var gen = new WorldGenerator(77, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(3);

        var coral = gen.WonderGatesForTest(Content.Planets["coral_sea"]);
        Assert.True(coral["reefRings"] && coral["reefFields"] && coral["blueHoles"] && coral["causewayIslands"] && coral["riverMorphology"], "the coral sea lacks a reef family");
        var icecap = gen.WonderGatesForTest(Content.Planets["icecap"]);
        Assert.True(icecap["glaciers"] && icecap["iceSheets"] && icecap["icebergs"] && icecap["iceCaves"] && icecap["frostPolygons"], "the icecap lacks an ice family");
        var lowlands = gen.WonderGatesForTest(Content.Planets["river_lowlands"]);
        Assert.True(lowlands["riverMorphology"] && lowlands["peatBogs"] && lowlands["floatingMats"] && lowlands["rias"], "the river lowlands lack a wet family");

        foreach (var key in Gen3Types)
        {
            var planet = Content.Planets[key];
            int h = gen.SurfaceHeight(planet, 100, 37);
            Assert.InRange(h, -400, 288);
            Assert.NotNull(gen.Generate(planet, new ChunkCoord(3, WorldConstants.WorldToChunk(h), 1)));
            string name = NameGenerator.PlanetProper(new DeterministicRandom(12345 + key.Length), key);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    // ---------- School club wave 3 (#1756): the four generation-5 planet types ----------

    private static readonly string[] Gen5Types = { "rainbow_sea", "flower_fields", "scrapyard", "gamer_hills" };

    [Fact]
    public void Gen5Types_AreDataComplete_AndGatedToGenerationFive()
    {
        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (var key in Gen5Types)
        {
            var p = Content.GetPlanet(key);
            Assert.NotNull(p);
            Assert.True(p!.Selectable);
            Assert.Equal(WorldDescription.AuthoredContentGeneration, p.MinTerrainGeneration);
            Assert.InRange(p.SpawnWeight, 3, 8);
            Assert.True(p.TerrainStyles.Count >= 2, $"{key} has no style pool");
            Assert.Contains($"\"planet.{key}.name\"", en);
            Assert.Contains($"\"planet.{key}.name\"", de);
            Assert.Contains($"\"planet.{key}.desc\"", en);
            Assert.Contains($"\"planet.{key}.desc\"", de);
        }

        // The data fields each child asked for.
        var rainbow = Content.Planets["rainbow_sea"];
        Assert.True(rainbow.FloatingIslands && rainbow.UnderwaterForests && rainbow.SeabedBlock == "sand" && rainbow.WaterTint == "rainbow" && rainbow.Exotic);
        var flowers = Content.Planets["flower_fields"];
        Assert.Equal(0.0, flowers.TreeDensity);
        Assert.Equal("authored", flowers.CreatureAbundance);
        Assert.Contains("flowerling", flowers.AuthoredCreatures);
        var scrap = Content.Planets["scrapyard"];
        Assert.True(scrap.HasTag(TerrainTag.Scrap) && scrap.FloraDensity == 0.0 && scrap.CreatureAbundance == "none" && !scrap.IsAirless);
        Assert.True(scrap.RuinsBias > 1.0 && scrap.FactoriesBias > 1.0);
        var gamer = Content.Planets["gamer_hills"];
        Assert.True(gamer.HasTag(TerrainTag.Gaming) && gamer.HasTag(TerrainTag.Karst) && gamer.Atmosphere == "breathable");
    }

    [Fact]
    public void Gen5Types_EnterOnlyGenerationFiveGalaxies()
    {
        var gen4 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = 4 };
        var gen5 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = 5 };
        int onGen5 = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var body in new UniverseGenerator(seed, gen4, Content).Generate().Systems.SelectMany(s => s.Bodies))
            {
                Assert.DoesNotContain(body.PlanetType, Gen5Types);
            }

            onGen5 += new UniverseGenerator(seed, gen5, Content).Generate().Systems.SelectMany(s => s.Bodies).Count(b => Gen5Types.Contains(b.PlanetType));
        }

        Assert.True(onGen5 > 0, "no generation-5 galaxy rolled a new planet type in 40 seeds");
    }

    [Fact]
    public void Gen5Types_ActivateTheirFamilies_AndGenerateChunks()
    {
        var gen = new WorldGenerator(77, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(5);

        Assert.True(gen.WonderGatesForTest(Content.Planets["gamer_hills"])["gamingLandmarks"], "the gaming planet lacks its landmarks");
        Assert.False(gen.WonderGatesForTest(Content.Planets["meadowlands"])["gamingLandmarks"], "a meadow grew gaming landmarks");

        var gen4 = new WorldGenerator(77, Content);
        gen4.SetLavaCoreVolcanoes(true);
        gen4.SetTerrainGeneration(4);
        Assert.False(gen4.WonderGatesForTest(Content.Planets["gamer_hills"])["gamingLandmarks"], "generation 4 must not grow the generation-5 landmarks");

        foreach (var key in Gen5Types)
        {
            var planet = Content.Planets[key];
            int h = gen.SurfaceHeight(planet, 100, 37);
            Assert.InRange(h, -400, 288);
            Assert.NotNull(gen.Generate(planet, new ChunkCoord(3, WorldConstants.WorldToChunk(h), 1)));
            string name = NameGenerator.PlanetProper(new DeterministicRandom(12345 + key.Length), key);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
