// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The four spectacle planet types (#2342) and the galaxy rule that makes them findable (#2343): data complete and
/// gated to generation 21, each activating the families it was made for, every generation-21 galaxy carrying each
/// of them at least once, an older galaxy never, and landing pads found on each.
/// </summary>
public sealed class SpectacleWorldsTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_spectacle_" + Guid.NewGuid().ToString("N"));

    private static readonly string[] Types = { "arch_lands", "pillar_world", "hollow_world", "bone_desert" };

    [Fact]
    public void SpectacleTypes_AreDataComplete_GuaranteedOnce_AndGatedToGenerationTwentyOne()
    {
        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (var key in Types)
        {
            var p = Content.GetPlanet(key);
            Assert.NotNull(p);
            Assert.True(p!.Selectable);
            Assert.Equal(WorldDescription.SpectacleGeneration, p.MinTerrainGeneration);
            Assert.True(p.Exotic);
            Assert.True(p.GuaranteedOnce);
            Assert.Equal(4.0, p.SpectacleDensity);
            Assert.InRange(p.SpawnWeight, 3, 8);
            Assert.True(p.TerrainStyles.Count >= 2, $"{key} has no style pool");
            Assert.Equal("breathable", p.Atmosphere);
            Assert.Contains($"\"planet.{key}.name\"", en);
            Assert.Contains($"\"planet.{key}.name\"", de);
            Assert.Contains($"\"planet.{key}.desc\"", en);
            Assert.Contains($"\"planet.{key}.desc\"", de);
        }
    }

    [Fact]
    public void EveryType_ActivatesTheFamiliesItWasMadeFor()
    {
        var gen = new WorldGenerator(20261005, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.SpectacleGeneration);

        var arch = gen.LandmarkOrderForTest(Content.Planets["arch_lands"]);
        Assert.Contains("arch-cluster", arch);
        Assert.Contains("mesa-cluster", arch);
        Assert.Contains("wave-rock", arch);
        Assert.Contains("table-mountain", arch);
        Assert.True(gen.WonderGatesForTest(Content.Planets["arch_lands"])["tableVariants"]);
        Assert.True(gen.WonderGatesForTest(Content.Planets["arch_lands"])["abris"]);

        var pillar = gen.LandmarkOrderForTest(Content.Planets["pillar_world"]);
        Assert.Contains("pillar-island", pillar);
        Assert.True(gen.WonderGatesForTest(Content.Planets["pillar_world"])["daylightHalls"]); // karst country has halls too

        var hollow = gen.WonderGatesForTest(Content.Planets["hollow_world"]);
        Assert.True(hollow["portals"]);
        Assert.True(hollow["daylightHalls"]);
        Assert.True(hollow["mountainHalls"]);

        var bone = gen.LandmarkOrderForTest(Content.Planets["bone_desert"]);
        Assert.Contains("fossil-ridge", bone);
        Assert.True(gen.WonderGatesForTest(Content.Planets["bone_desert"])["boneStrata"]);

        // Dense: the pillar world rolls many more islands than a karst world that carries them as a find.
        int dense = gen.PillarIslandsForTest(Content.Planets["pillar_world"]).Count;
        int find = gen.PillarIslandsForTest(Content.Planets["karst"]).Count;
        Assert.True(dense > find * 3, $"pillar world {dense} islands vs karst {find}");
    }

    [Fact]
    public void EveryGenerationTwentyOneGalaxy_CarriesEachType_AndAnOlderOneNone()
    {
        var gen20 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.SpectacleGeneration - 1 };
        var gen21 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.SpectacleGeneration };
        for (long seed = 1; seed <= 40; seed++)
        {
            var oldBodies = new UniverseGenerator(seed, gen20, Content).Generate().Systems.SelectMany(s => s.Bodies).ToList();
            Assert.DoesNotContain(oldBodies, b => b.PlanetType is { } t && Types.Contains(t));

            var galaxy = new UniverseGenerator(seed, gen21, Content).Generate();
            var planets = galaxy.Systems.SelectMany(s => s.Bodies).Where(b => b.Kind == CelestialKind.Planet).ToList();
            foreach (var key in Types)
            {
                Assert.True(planets.Any(b => b.PlanetType == key), $"seed {seed}: no {key} in a generation-21 galaxy");
            }

            // Never the start system's bodies.
            Assert.DoesNotContain(galaxy.Systems[0].Bodies, b => b.PlanetType is { } t && Types.Contains(t));
        }
    }

    [Fact]
    public void AGrownGalaxy_KeepsItsGuaranteedBodies()
    {
        var desc = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.SpectacleGeneration };
        var a = new UniverseGenerator(7, desc, Content).Generate(12);
        var b = new UniverseGenerator(7, desc, Content).Generate(15);
        for (int si = 0; si < 12; si++)
        {
            for (int bi = 0; bi < a.Systems[si].Bodies.Count; bi++)
            {
                Assert.Equal(a.Systems[si].Bodies[bi].PlanetType, b.Systems[si].Bodies[bi].PlanetType);
            }
        }
    }

    [Theory]
    [InlineData("arch_lands")]
    [InlineData("pillar_world")]
    [InlineData("hollow_world")]
    [InlineData("bone_desert")]
    public void LandingPads_AreFound_OnEveryType(string type)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pads_" + type));
        var config = new ServerConfig
        {
            WorldName = "pads_" + type, Seed = 20261005, StartPlanet = type, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = false, PlaceRuins = false, PlaceRailRuins = false, PlaceChests = false, PlaceWrecks = false,
            PlaceVaults = false, PlaceDataCubes = false, PlaceBanditCamps = false,
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer("Pilot");
        Assert.True(server.LandingPadCenters.Count >= 4, $"{type}: {server.LandingPadCenters.Count} pads");
        for (int i = 0; i < server.LandingPadCenters.Count; i++)
        {
            var pad = server.LandingPadInfoForTest(i);
            Assert.False(pad.Wet && !pad.Islet, $"{type}: pad {i} is wet");
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
