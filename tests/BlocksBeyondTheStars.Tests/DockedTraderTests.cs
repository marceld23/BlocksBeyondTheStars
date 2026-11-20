// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — a merchant's ship at a station: a docked NPC trader lies moored beside the hangar for as long as
/// its pilot trades aboard and casts off afterwards; a pilot who looks later sees it too; aboard, the merchant appears for
/// people who were already there and leaves with the ship; the station announces both.
/// </summary>
public sealed class DockedTraderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_dockedtrader_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, out RecordingTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static void Tick(SvGameServer server, int ticks = 4)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(0.25);
        }
    }

    [Fact]
    public void ADockedTrader_LiesMooredAtTheStation_ForEveryPilot_AndCastsOffWhenItsStayIsOver()
    {
        var server = NewServer("moored", out var repo, out _);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);

            // The merchant is aboard (docked before anybody looked): the flight scene shows its ship at the berth.
            server.DockTraderForTest(station.Id);
            Tick(server);
            var moored = Assert.Single(server.TradersInSceneForTest("Pilot"), t => t.Station == station.Id);
            Assert.Equal("Docked", moored.Phase);
            double away = Math.Sqrt(Math.Pow(moored.Pos.X - station.Position.X, 2) + Math.Pow(moored.Pos.Z - station.Position.Z, 2));
            Assert.InRange(away, 4.0, 160.0); // beside the station, not in its middle and not across the system

            // It stays put, and it does not come twice.
            Tick(server, 8);
            var still = Assert.Single(server.TradersInSceneForTest("Pilot"), t => t.Station == station.Id);
            Assert.Equal((moored.Id, "Docked"), (still.Id, still.Phase));
            Assert.Equal(moored.Pos, still.Pos);

            // The stay is over: the record goes and the ship flies out.
            server.ExpireVisitingTraderForTest(station.Id);
            Tick(server);
            Assert.Null(server.VisitingTraderSecondsLeftForTest(station.Id));
            var leaving = server.TradersInSceneForTest("Pilot").FirstOrDefault(t => t.Id == moored.Id);
            Assert.True(leaving.Id is null || leaving.Phase == "Departing", $"the ship is still {leaving.Phase}");
            server.Stop();
        }
    }

    [Fact]
    public void Aboard_TheMerchantComesForThoseAlreadyThere_AndLeavesWithItsShip_AndTheStationSaysSo()
    {
        var server = NewServer("aboard", out var repo, out var transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
            server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"));
            Assert.DoesNotContain(server.NpcCrewForTest(), n => n.Name == "Docked Trader");

            // A trader docks while the player is aboard: the station announces it and the merchant stands at the post.
            int before = transport.Sent.Count;
            server.DockTraderForTest(station.Id);
            server.TickPlaceEventsForTest();
            Assert.Contains(server.NpcCrewForTest(), n => n.Name == "Docked Trader" && n.Role == "vendor");
            var news = transport.Sent.Skip(before).Where(s => s.Conn == pilot.ConnectionId).Select(s => s.Msg).ToList();
            Assert.Contains(news.OfType<ServerMessage>(), m => m.Text == "@srv.station.announce.trader_docked:Docked Trader");
            Assert.Contains(news.OfType<SoundFx>(), f => f.SoundId == SvGameServer.StationGongSound);
            server.TickPlaceEventsForTest();
            Assert.Single(server.NpcCrewForTest(), n => n.Name == "Docked Trader"); // one merchant, not one per look

            // Its stay runs out: the merchant is gone from the deck and the record with it.
            server.ExpireVisitingTraderForTest(station.Id);
            server.TickPlaceEventsForTest();
            Assert.DoesNotContain(server.NpcCrewForTest(), n => n.Name == "Docked Trader");
            Assert.Null(server.VisitingTraderSecondsLeftForTest(station.Id));
            server.Stop();
        }
    }
}
