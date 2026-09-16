// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
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
/// #1924 ("I started several worlds but none had a space station"): the start system always has a real station.
/// The per-system roll leaves most systems without one and the old home guarantees only covered <c>sys0</c>, while the
/// start planet is the first planet of the start type anywhere in the galaxy.
/// </summary>
public sealed class StartSystemStationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_startstation_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldDescription NewWorld() => new ServerConfig().World;

    /// <summary>The server's start pick for a fresh default world: the first planet of the start type.</summary>
    private static CelestialBody? StartOf(Galaxy galaxy)
        => galaxy.AllBodies().FirstOrDefault(b => b.Kind == CelestialKind.Planet && b.PlanetType == new ServerConfig().StartPlanet);

    private static bool HasStation(Galaxy galaxy, string systemId)
        => galaxy.Systems.First(s => s.Id == systemId).Bodies.Any(b => b.Kind == CelestialKind.SpaceStation);

    [Fact]
    public void EveryStartSystem_EndsUpWithARealStation_AndOnlyStationlessOnesGainOne()
    {
        int added = 0, checkedSeeds = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var galaxy = new UniverseGenerator(seed, NewWorld(), _content).Generate();
            if (StartOf(galaxy) is not { } start)
            {
                continue;
            }

            checkedSeeds++;
            var system = galaxy.Systems.First(s => s.Id == start.SystemId);
            bool hadOne = HasStation(galaxy, system.Id);
            var station = UniverseGenerator.EnsureStartSystemStation(system, start);

            Assert.True(HasStation(galaxy, system.Id), $"seed {seed}: start system {system.Id} has no station");
            Assert.Equal(hadOne, station is null); // a rolled station is left alone
            Assert.Null(UniverseGenerator.EnsureStartSystemStation(system, start)); // idempotent
            if (station is not null)
            {
                added++;
                Assert.Equal(system.Id + "-st", station.Id);
                Assert.Equal(start.Name + " Station", station.Name);
            }
        }

        Assert.True(checkedSeeds >= 30, $"only {checkedSeeds} seeds had a start planet");
        Assert.True(added > 0, "no seed in the range had a station-less start system — the test guards nothing");
    }

    [Fact]
    public void TheAddedStation_IsDerivedFromTheSeedAlone()
    {
        long seed = StationlessStartSeed();
        CelestialBody Add()
        {
            var galaxy = new UniverseGenerator(seed, NewWorld(), _content).Generate();
            var start = StartOf(galaxy)!;
            return UniverseGenerator.EnsureStartSystemStation(galaxy.Systems.First(s => s.Id == start.SystemId), start)!;
        }

        var a = Add();
        var b = Add();
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.SystemX, b.SystemX);
        Assert.Equal(a.SystemZ, b.SystemZ);
    }

    [Fact]
    public void AFreshWorld_WhoseStartSystemRolledNoStation_ShowsOneInFlight()
    {
        long seed = StationlessStartSeed();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "start"));
        using (repo)
        {
            var config = new ServerConfig
            {
                WorldName = "start",
                Seed = seed,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
            };
            config.Rules.FreeSpaceFlight = true;
            var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();

            var start = server.Galaxy.FindBody(server.ActiveLocationId)!;
            Assert.True(HasStation(server.Galaxy, start.SystemId));

            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Kind == CombatEntityKind.SpaceStation && e.Id == start.SystemId + "-st");
        }
    }

    /// <summary>The first seed whose default galaxy starts outside sys0 in a system that rolled no station.</summary>
    private long StationlessStartSeed()
    {
        for (long seed = 1; seed <= 400; seed++)
        {
            var galaxy = new UniverseGenerator(seed, NewWorld(), _content).Generate();
            if (StartOf(galaxy) is { } start && start.SystemId != "sys0" && !HasStation(galaxy, start.SystemId))
            {
                return seed;
            }
        }

        throw new InvalidOperationException("no station-less start system in 400 seeds");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
