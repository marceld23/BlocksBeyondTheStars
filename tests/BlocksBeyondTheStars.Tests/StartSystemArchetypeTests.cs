// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// #3436: a world's start system is home — never Desolate or a Pirate Haven for the things that live there, even when the
/// start body lies outside <c>sys0</c> (Justus' world started in sys6, a Pirate Haven: three drones at home from day one, and
/// they stayed after the win). The layout keeps the rolled archetype.
/// </summary>
public sealed class StartSystemArchetypeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StartSystemArchetypeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_startsys_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static bool Lawless(SystemArchetype a) => a is SystemArchetype.PirateHaven or SystemArchetype.Desolate;

    [Fact]
    public void ForStartSystem_IsNeverLawless_AndKeepsEverySafeRoll()
    {
        var desc = new WorldDescription { SystemVariance = true };
        int rerolled = 0;
        for (long seed = 1; seed <= 400; seed++)
        {
            for (int i = 0; i < 16; i++)
            {
                string id = "sys" + i;
                var rolled = SystemArchetypes.For(seed, id, desc);
                var start = SystemArchetypes.ForStartSystem(seed, id, desc);
                Assert.False(Lawless(start), $"seed {seed} {id}: the start system lives as {start}");
                if (Lawless(rolled))
                {
                    rerolled++;
                }
                else
                {
                    Assert.Equal(rolled, start); // a safe roll stays exactly as it is
                }
            }

            Assert.Equal(SystemArchetypes.ForIndex(seed, 0), SystemArchetypes.ForStartSystem(seed, "sys0", desc));
        }

        Assert.True(rerolled > 0, "expected lawless rolls outside sys0 across 400 seeds");
        Assert.Equal(SystemArchetype.Standard, SystemArchetypes.ForStartSystem(7, "sys3", new WorldDescription()));
        Assert.Equal(SystemArchetype.Standard, SystemArchetypes.ForStartSystem(7, "ornemis", desc));
    }

    private SvGameServer Started(string name, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = StartType,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        config.Rules.AlienUfos = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The start planet type of the world under test (Justus' world started on "varied").</summary>
    private const string StartType = "varied";

    /// <summary>The first world whose start system ROLLED a Pirate Haven or Desolate. Today seed 1 already is one (it starts
    /// in sys4, a rolled Pirate Haven that lives as a Hub), so the scan usually stops at once; it walks on if generation
    /// ever moves that start.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo)? Scan()
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            var server = Started("startsys" + seed, seed, out var repo);
            if (Lawless(server.StartSystemArchetypesForTest().Rolled))
            {
                return (server, repo);
            }

            repo.Dispose();
        }

        return null;
    }

    [Fact]
    public void LawlessStartSystem_LivesAsHome_FromTheFirstLaunch_AndIsCalmAfterTheWin()
    {
        var found = Scan();
        Assert.True(found.HasValue, "expected a world whose start system rolled a Pirate Haven or Desolate");
        var (server, repo) = found!.Value;
        using (repo)
        {
            var (effective, rolled) = server.StartSystemArchetypesForTest();
            Assert.True(Lawless(rolled));
            Assert.False(Lawless(effective), $"the start system ({server.StartSystemIdForTest}) rolled {rolled} but lives as {effective}");

            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            server.Ship.CurrentLocationId = pilot.CurrentLocationId;
            server.EnterSpace("Pilot");
            Assert.Equal(server.StartSystemIdForTest, server.SpaceSystemIdForTest(pilot.State.PlayerId));
            Assert.Equal(2, server.AmbientHostilesForTest(pilot.State.PlayerId)); // a home's wave at Normal: two drones, no bonus

            server.MarkGuardianDefeatedForTest();
            Assert.Equal(0, server.AmbientHostilesForTest(pilot.State.PlayerId)); // the win calms the live flight at once
            Assert.False(server.BanditShipsAllowedInForTest(server.StartSystemIdForTest!), "no raiders at home after the win");
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
