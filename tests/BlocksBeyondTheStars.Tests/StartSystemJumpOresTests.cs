// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// #2988 (Lyxette 2026-10-28: "Blei? Woher, wenn ich es nicht abbauen kann?"): the jump generator is the way out of the start
/// system, its reactor fuel needs lead and uranium, and only a handful of planet types grow them. From generation 31 the start
/// system always carries both on some body; older galaxies keep every type.
/// </summary>
public sealed class StartSystemJumpOresTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_jumpores_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static CelestialBody? StartOf(Galaxy galaxy)
        => galaxy.AllBodies().FirstOrDefault(b => b.Kind == CelestialKind.Planet && b.PlanetType == new ServerConfig().StartPlanet);

    private bool Grows(StarSystem system, string ore)
        => system.Bodies.Any(b => b.PlanetType is { } t && _content.GetPlanet(t) is { } p && p.Ores.Exists(o => o.Block == ore));

    [Fact]
    public void EveryNewStartSystem_CarriesLeadAndUranium_AndTheStartStaysPut()
    {
        int retyped = 0, checkedSeeds = 0;
        for (long seed = 1; seed <= 80; seed++)
        {
            var desc = new ServerConfig().World;
            Assert.Equal(WorldDescription.CurrentTerrainGeneration, desc.TerrainGeneration);
            var galaxy = new UniverseGenerator(seed, desc, _content, new ServerConfig().StartPlanet).Generate();
            if (StartOf(galaxy) is not { } start)
            {
                continue;
            }

            checkedSeeds++;
            var system = galaxy.Systems.Single(s => s.Id == start.SystemId);
            string startType = start.PlanetType!;
            var changed = UniverseGenerator.EnsureStartSystemJumpOres(system, start, seed, desc, _content);
            retyped += changed.Count;

            foreach (var ore in UniverseGenerator.JumpOres)
            {
                Assert.True(Grows(system, ore), $"seed {seed}: start system {system.Id} grows no {ore} — "
                    + string.Join(", ", system.Bodies.Select(b => $"{b.Id} {b.Kind} {b.PlanetType} {b.Status}")));
            }

            Assert.Equal(startType, start.PlanetType);
            Assert.Same(start, StartOf(galaxy)); // the server's start pick on the next load is the same body
            Assert.All(changed, b => Assert.NotEqual(startType, b.PlanetType));
            Assert.Empty(UniverseGenerator.EnsureStartSystemJumpOres(system, start, seed, desc, _content)); // idempotent
        }

        Assert.True(checkedSeeds >= 60, $"only {checkedSeeds} seeds had a start planet");
        Assert.True(retyped > 0, "no seed lacked a jump ore — the test guards nothing");
    }

    [Fact]
    public void AnOlderGeneration_KeepsEveryType()
    {
        for (long seed = 1; seed <= 30; seed++)
        {
            var desc = new ServerConfig().World;
            desc.TerrainGeneration = WorldDescription.StartSystemJumpOresGeneration - 1;
            var galaxy = new UniverseGenerator(seed, desc, _content, new ServerConfig().StartPlanet).Generate();
            if (StartOf(galaxy) is not { } start)
            {
                continue;
            }

            var system = galaxy.Systems.Single(s => s.Id == start.SystemId);
            var before = system.Bodies.Select(b => b.PlanetType).ToList();
            Assert.Empty(UniverseGenerator.EnsureStartSystemJumpOres(system, start, seed, desc, _content));
            Assert.Equal(before, system.Bodies.Select(b => b.PlanetType).ToList());
        }
    }

    /// <summary>A start system of only the start planet and asteroid fields has no other planet to retype: from generation 31
    /// the metallic asteroids carry a lead vein too (gated per vein, so an older world's asteroids stay as they were).</summary>
    [Fact]
    public void MetallicAsteroids_GrowLead_FromGeneration31Only()
    {
        var metallic = _content.Planets["asteroid_metallic"];
        bool Surveys(int generation)
        {
            var gen = new WorldGenerator(42, _content);
            gen.SetTerrainGeneration(generation);
            return gen.SurveyResources(metallic, "sys0-a0", 1.0, cratered: false).Veins.Any(v => v.Block == "lead_ore");
        }

        Assert.True(Surveys(WorldDescription.StartSystemJumpOresGeneration));
        Assert.False(Surveys(WorldDescription.StartSystemJumpOresGeneration - 1));
        Assert.Equal("lead_ore", metallic.Ores[^1].Block); // a gated vein goes last, so every older vein keeps its seed
    }

    [Fact]
    public void AFreshServer_PinsAStartSystemWithBothOres()
    {
        long seed = SeedLackingAJumpOre();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "jumpores"));
        using (repo)
        {
            var config = new ServerConfig { WorldName = "jumpores", Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
            var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();

            var galaxy = server.Galaxy;
            var start = galaxy.FindBody(server.Metadata.ActiveLocationId)!;
            var system = galaxy.Systems.Single(s => s.Id == start.SystemId);
            foreach (var ore in UniverseGenerator.JumpOres)
            {
                Assert.True(Grows(system, ore), $"seed {seed}: the live start system grows no {ore}");
            }

            Assert.All(system.Bodies.Where(b => !string.IsNullOrEmpty(b.PlanetType)),
                b => Assert.Equal(b.PlanetType, server.Metadata.BodyPlanetTypes[b.Id])); // pinned as retyped
        }
    }

    /// <summary>A seed whose classic start system lacks lead or uranium, so the server test exercises the retype.</summary>
    private long SeedLackingAJumpOre()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var galaxy = new UniverseGenerator(seed, new ServerConfig().World, _content, new ServerConfig().StartPlanet).Generate();
            if (StartOf(galaxy) is { } start
                && UniverseGenerator.JumpOres.Any(ore => !Grows(galaxy.Systems.Single(s => s.Id == start.SystemId), ore)))
            {
                return seed;
            }
        }

        throw new InvalidOperationException("no seed in 1..200 lacks a jump ore in its start system");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
