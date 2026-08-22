// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Remnant Protocol (#1206): defeating the Guardian no longer empties the galaxy. Planet machines refill as a
/// thinned remnant (half cap, drones only, slower), peaceful/family rules still mean zero, and bandit ships keep
/// only genuine pirate havens — so the raider bounty stays earnable after the ending.
/// </summary>
public sealed class RemnantProtocolTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RemnantProtocolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_remnant_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string name, Action<GameRules>? rules, out SqliteWorldRepository repo, long seed = 4242)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static void TickSurface(SvGameServer server, PlayerSession p, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.Tick(6.0);
            p.State.Health = 100f;
        }
    }

    [Fact]
    public void AfterTheWin_TwoPlayersShareAHalvedCap_OfDronesOnly()
    {
        var server = Started("remnant_two", null, out var repo); // Survival + PlanetEnemies Normal (2 per player)
        using (repo)
        {
            var a = server.AddLocalPlayer("Ada");
            var b = server.AddLocalPlayer("Ben");
            foreach (var p in new[] { a, b })
            {
                p.State.AboardShip = false;
                p.State.Position = new Vector3f(0, 64, 0);
            }

            // Before the win the world fills to the full cap (2 × 2 players = 4).
            for (int i = 0; i < 40 && server.PlanetEnemies.Count < 4; i++)
            {
                server.Tick(6.0);
                a.State.Health = 100f;
                b.State.Health = 100f;
            }

            Assert.Equal(4, server.PlanetEnemies.Count);

            server.MarkGuardianDefeatedForTest();
            Assert.Empty(server.PlanetEnemies);

            for (int i = 0; i < 40; i++)
            {
                server.Tick(6.0);
                a.State.Health = 100f;
                b.State.Health = 100f;
            }

            Assert.Equal(2, server.PlanetEnemies.Count); // max(1, 4 / 2)
            Assert.All(server.PlanetEnemies, e => Assert.Equal(CombatEntityKind.ScanDrone, e.Kind));
        }
    }

    [Fact]
    public void AfterTheWin_PeacefulRulesStillMeanNoMachines()
    {
        var server = Started("remnant_peaceful", r => r.PlanetEnemies = AlienActivity.Off, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.MarkGuardianDefeatedForTest();
            TickSurface(server, p, 20);

            Assert.Empty(server.PlanetEnemies);
        }
    }

    [Fact]
    public void FamilyPresets_KeepPlanetMachinesOff()
    {
        Assert.Equal(AlienActivity.Off, ServerPresets.Get("family")!.PlanetEnemies);
        Assert.Equal(AlienActivity.Off, ServerPresets.Get("peaceful-creative")!.PlanetEnemies);
    }

    [Fact]
    public void AfterTheWin_BanditShipsKeepOnlyPirateHavens()
    {
        // Scan a handful of start systems: before the win any pirate-space system harbours raiders, after it
        // only a genuine PirateHaven does — and a haven keeps them, so the raider bounty stays earnable.
        bool sawNonHavenPirateSpace = false, sawHaven = false;
        for (long seed = 1; seed <= 40 && !(sawNonHavenPirateSpace && sawHaven); seed++)
        {
            var server = Started("remnant_bandit" + seed, r =>
            {
                r.PlanetEnemies = AlienActivity.Off;
                r.Bandits = AlienActivity.Normal;
                r.FreeSpaceFlight = true;
                r.SpaceCombat = SpaceCombatMode.PvE;
                r.ShipWeapons = ShipWeaponMode.NpcsOnly;
            }, out var repo, seed);
            using (repo)
            {
                var pilot = server.AddLocalPlayer("Pilot");
                pilot.State.AboardShip = true;
                server.Ship.CurrentLocationId = pilot.CurrentLocationId;
                server.EnterSpace("Pilot");
                string systemId = server.SpaceSystemIdForTest(pilot.State.PlayerId);
                if (systemId.Length == 0 || !server.BanditSystemForTest(systemId))
                {
                    continue;
                }

                bool haven = server.SystemArchetypeForTest(systemId) == SystemArchetype.PirateHaven;
                Assert.True(server.BanditShipsAllowedInForTest(systemId), "pirate space harbours raiders before the win");

                server.MarkGuardianDefeatedForTest();
                Assert.Equal(haven, server.BanditShipsAllowedInForTest(systemId));
                sawHaven |= haven;
                sawNonHavenPirateSpace |= !haven;
            }
        }

        Assert.True(sawNonHavenPirateSpace, "expected at least one non-haven pirate-space start system in 40 seeds");
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
        catch
        {
            // best-effort temp cleanup
        }
    }
}
