// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
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
/// #2697: a body's gravity. The classic roll read the low 16 bits of <c>h·31+c</c> of the body id, so the planets of a
/// system came out within a few ten-thousandths of a g of each other; from terrain generation 26 it is the mixed hash of
/// the id salted with the world seed. A world of generation 25 or older keeps its gravity bit for bit (the pinned values
/// below were taken from the code before the change), and the ship scanner shows the gravity the world uses.
/// </summary>
public sealed class BodyGravityTests : IDisposable
{
    private const int Gen = WorldDescription.HashVarietyGeneration;
    private const int OldGen = Gen - 1;

    private static readonly WorldConstants.WorldSizeClass[] Classes =
        { WorldConstants.WorldSizeClass.Asteroid, WorldConstants.WorldSizeClass.Moon, WorldConstants.WorldSizeClass.Planet };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_gravity_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>The gravity roll exactly as <c>GameServerWeather.GravityFor</c> computed it before #2697.</summary>
    private static float PreChangeGravity(string id, WorldConstants.WorldSizeClass cls, long seed)
    {
        uint h = unchecked((uint)(AtmosphereTints.StableHash(id) ^ (int)seed));
        (float lo, float hi) = cls switch
        {
            WorldConstants.WorldSizeClass.Asteroid => (0.35f, 0.55f),
            WorldConstants.WorldSizeClass.Moon => (0.55f, 0.85f),
            _ => (0.80f, 1.60f),
        };
        float t = ((h ^ 0x9E3779B9u) & 0xFFFFu) / 65535f;
        return lo + t * (hi - lo);
    }

    [Fact]
    public void TheGate_IsTheHashVarietyGeneration()
    {
        Assert.False(BodyGravity.HasSeededGravity(OldGen));
        Assert.True(BodyGravity.HasSeededGravity(Gen));
        Assert.True(BodyGravity.HasSeededGravity(WorldDescription.CurrentTerrainGeneration));
    }

    [Fact]
    public void OldGeneration_KeepsEveryGravity_BitForBit()
    {
        // Pinned from the code before #2697 — the bug is visible here: three consecutive planets, one gravity.
        Assert.Equal(1.1313771f, BodyGravity.FactorFor("sys0-p0", WorldConstants.WorldSizeClass.Planet, 42, OldGen));
        Assert.Equal(1.131365f, BodyGravity.FactorFor("sys0-p1", WorldConstants.WorldSizeClass.Planet, 42, OldGen));
        Assert.Equal(1.1313527f, BodyGravity.FactorFor("sys0-p2", WorldConstants.WorldSizeClass.Planet, 42, OldGen));
        Assert.Equal(0.8318082f, BodyGravity.FactorFor("sys3-p1-m0", WorldConstants.WorldSizeClass.Moon, -7, OldGen));
        Assert.Equal(0.45253757f, BodyGravity.FactorFor("sys2-a1", WorldConstants.WorldSizeClass.Asteroid, 1, OldGen));
        Assert.Equal(1.4974837f, BodyGravity.FactorFor("sys5-p3", WorldConstants.WorldSizeClass.Planet, long.MaxValue, OldGen));

        // Every generation below 26 is the old formula, whatever the seed, id or class.
        foreach (int generation in new[] { 0, 1, 9, 21, OldGen })
            foreach (long seed in new[] { 0L, 1L, 42L, -7L, long.MaxValue, long.MinValue, 0x1234_5678_9ABC_DEF0L })
                foreach (var cls in Classes)
                    for (int i = 0; i < 16; i++)
                    {
                        string id = $"sys{i}-p{i % 5}" + (i % 3 == 0 ? "-m0" : string.Empty);
                        Assert.Equal(PreChangeGravity(id, cls, seed), BodyGravity.FactorFor(id, cls, seed, generation));
                    }
    }

    [Fact]
    public void NewGeneration_PlanetsOfASystem_SpreadAcrossTheBand()
    {
        // The classic roll put every planet of a system within a hundredth of a g (100 % of the systems); seeded by the
        // world, the planets of a system differ like independent draws and fill the whole band.
        var galaxy = new UniverseGenerator(42, new WorldDescription { StarSystemCount = 80, TerrainGeneration = Gen }, _content).Generate();
        int systems = 0, classicFlat = 0, mixedFlat = 0;
        var all = new List<float>();
        foreach (var system in galaxy.Systems)
        {
            var planets = system.Bodies.Where(b => b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType)).ToList();
            if (planets.Count < 3)
            {
                continue;
            }

            systems++;
            var mixed = planets.Select(b => BodyGravity.FactorFor(b.Id, WorldConstants.SizeClassFor(b.Kind, b.PlanetType!), 42, Gen)).ToList();
            var classic = planets.Select(b => BodyGravity.FactorFor(b.Id, WorldConstants.SizeClassFor(b.Kind, b.PlanetType!), 42, OldGen)).ToList();
            classicFlat += classic.Max() - classic.Min() < 0.01f ? 1 : 0;
            mixedFlat += mixed.Max() - mixed.Min() < 0.01f ? 1 : 0;
            Assert.Equal(mixed.Count, mixed.Distinct().Count()); // no two planets of a system share their gravity
            all.AddRange(mixed.Where((_, i) => WorldConstants.SizeClassFor(planets[i].Kind, planets[i].PlanetType!) == WorldConstants.WorldSizeClass.Planet));
        }

        Assert.True(systems >= 20, $"need systems with three planets or more, got {systems}");
        Assert.True(classicFlat > systems * 9 / 10, $"the classic roll is flat in only {classicFlat} of {systems} systems — the test no longer measures the bug");
        Assert.True(mixedFlat <= systems / 20, $"{mixedFlat} of {systems} systems still have all planets within 0.01 g");

        // Spread across the planet band 0.8–1.6 g: every quarter of it is used, and it is used about evenly.
        Assert.All(all, g => Assert.InRange(g, 0.8f, 1.6f));
        for (int q = 0; q < 4; q++)
        {
            float lo = 0.8f + q * 0.2f, hi = lo + 0.2f;
            int n = all.Count(g => g >= lo && g < hi);
            Assert.True(n >= all.Count / 8, $"only {n} of {all.Count} planets weigh {lo:0.0}–{hi:0.0} g");
        }
    }

    [Fact]
    public void NewGeneration_DiffersBetweenWorlds_AndStaysInTheClassBand()
    {
        foreach (var cls in Classes)
        {
            (float lo, float hi) = BodyGravity.BandFor(cls);
            var bySeed = new List<float>();
            for (long seed = 0; seed < 200; seed++)
            {
                float g = BodyGravity.FactorFor("sys1-p2", cls, seed, Gen);
                Assert.InRange(g, lo, hi);
                bySeed.Add(g);
            }

            // The same body id is a different world in every save.
            Assert.True(bySeed.Distinct().Count() >= 195, $"{cls}: only {bySeed.Distinct().Count()} distinct gravities over 200 seeds");
            Assert.True(bySeed.Min() < lo + (hi - lo) * 0.1f && bySeed.Max() > hi - (hi - lo) * 0.1f, $"{cls}: the seeds do not reach both ends of the band");
        }
    }

    [Fact]
    public void NewGeneration_Gravity_IsBitFrozen()
    {
        // Known answers: a generation-26 save's gravity (jump height, fall damage, the colossus gate) must never move.
        Assert.Equal(0.8225184f, BodyGravity.FactorFor("sys0-p0", WorldConstants.WorldSizeClass.Planet, 42, Gen));
        Assert.Equal(0.92277837f, BodyGravity.FactorFor("sys0-p1", WorldConstants.WorldSizeClass.Planet, 42, Gen));
        Assert.Equal(1.1434228f, BodyGravity.FactorFor("sys0-p2", WorldConstants.WorldSizeClass.Planet, 42, Gen));
        Assert.Equal(0.8395443f, BodyGravity.FactorFor("sys3-p1-m0", WorldConstants.WorldSizeClass.Moon, -7, Gen));
        Assert.Equal(0.49555913f, BodyGravity.FactorFor("sys2-a1", WorldConstants.WorldSizeClass.Asteroid, 1, Gen));
        Assert.Equal(1.1122723f, BodyGravity.FactorFor("sys5-p3", WorldConstants.WorldSizeClass.Planet, long.MaxValue, Gen));
    }

    // ---------- the server: one helper for the world and the scanner ----------

    private SvGameServer NewServer(string name, int generation, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = false };
        config.World.TerrainGeneration = generation;
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Theory]
    [InlineData(OldGen)]
    [InlineData(Gen)]
    public void TheScanner_ShowsTheGravityTheWorldUses(int generation)
    {
        var server = NewServer("scan_g" + generation, generation, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.Ship.Modules.Add("planet_scanner"); // tier 2: the card prints the value

            var here = server.Galaxy.FindBody(server.ActiveLocationId)!;
            var cls = WorldConstants.SizeClassFor(here.Kind, here.PlanetType!);
            Assert.Equal(BodyGravity.FactorFor(here.Id, cls, 11, generation), server.GravityFactor); // the loaded world

            var card = server.PlanetScanForTest("Pilot", string.Empty, out _)!;
            Assert.Equal(Printed(server.GravityFactor), card.Rows.Single(r => r.Topic == "gravity").Extra);

            // Another body of the system: no world is loaded for it, and the card shows the gravity it will have.
            foreach (var other in server.Galaxy.AllBodies().Where(b => b.SystemId == here.SystemId && b.Id != here.Id
                         && !string.IsNullOrEmpty(b.PlanetType) && b.Kind is CelestialKind.Planet or CelestialKind.Moon))
            {
                var report = server.PlanetScanForTest("Pilot", other.Id, out _)!;
                float expected = BodyGravity.FactorFor(other.Id, WorldConstants.SizeClassFor(other.Kind, other.PlanetType!), 11, generation);
                Assert.Equal(Printed(expected), report.Rows.Single(r => r.Topic == "gravity").Extra);
            }
        }
    }

    private static string Printed(float gravity) => gravity.ToString("0.0", CultureInfo.InvariantCulture) + " g";
}
