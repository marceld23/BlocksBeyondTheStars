// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Justus' black screen (2026-11-15, "pressed E on a station in space and was simply gone"): he had boarded the start
/// system's giant wreck (by all signs docked from the helm), left it on a spacewalk and pressed E at a hull. The client
/// never sent the board intent (a docking flag left from the helm's docking — <c>Client.Core/DockHandoff</c>, tested in
/// <c>DockHandoffTests</c>) and held its fade black. This pins the server half of that chain, which the client's hand-over
/// relies on: after the fall the player is on the spacewalk exactly as the report's snapshot showed (in the flight, on an
/// EVA, aboard the ship's record); a docking out of reach is refused with its reason and changes nothing — the suit and
/// the parked ship stay where they float, so the client's give-up can hand the spacewalk back there; and E at the hull
/// boards the wreck again.
/// </summary>
public sealed class GiantWreckSpacewalkDockingTests : IDisposable
{
    private const string Sample = "sample_hulk";

    private readonly string _root;
    private readonly GameContent _content;

    public GiantWreckSpacewalkDockingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_gw_eva_dock_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, ChunkGenWorkers = 2 };
        config.Rules.FreeSpaceFlight = true;
        config.World.GiantWrecks = Frequency.Normal;
        var server = new SvGameServer(config, _content, transport, repo) { GiantWreckDesignForTest = Sample };
        server.Start();
        return server;
    }

    [Fact]
    public void FallenOutOfAGiantWreck_ARefusedDockingKeepsTheSpacewalk_AndOneAtTheHullBoardsAgain()
    {
        var transport = new RecordingTransport();
        var server = NewServer("gw_eva_dock", transport, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = GiantWreckPlacer.BodyId(server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId, 0);

            // Docked from the helm, in front of the wreck.
            server.EnterSpace("Justus");
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            var half = server.GiantWreckForTest(id)!.Value.Half;
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z + half.Z + 8f);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"), "the giant wreck is boarded from the helm");

            // Fell out through the hull: the report's state — in the flight, on the spacewalk, the ship's record aboard.
            var arrival = p.State.Position;
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 60f, arrival.Z);
            for (int i = 0; i < 5 && !server.InSpace("Justus"); i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(server.InSpace("Justus") && !server.InStation("Justus"), "falling out opens the flight");
            Assert.True(p.State.InEva && p.State.AboardShip, "…on a spacewalk, as the report's snapshot showed");
            var ship = server.ShipFloatForTest("Justus");
            Assert.NotNull(ship);

            // E with the suit drifted 100 blocks off the hull: refused with its reason, and nothing moves.
            var far = new Vector3f(contact.Position.X, contact.Position.Y, contact.Position.Z + half.Z + 100f);
            server.ShipMove("Justus", far.X, far.Y, far.Z);
            int boardedBefore = transport.To<StationBoarded>(p).Count;
            server.BoardStation("Justus", id);
            Assert.Contains(transport.To<ActionRejected>(p), r => r.Action == "station" && r.Reason == "@srv.station.closer");
            Assert.True(server.InSpace("Justus") && !server.InStation("Justus"), "a refused docking keeps the flight");
            Assert.True(p.State.InEva, "…and the spacewalk");
            Assert.Equal(far, server.SuitPoseForTest("Justus"));
            Assert.Equal(ship, server.ShipFloatForTest("Justus"));
            Assert.Equal(boardedBefore, transport.To<StationBoarded>(p).Count);

            // E at the way in: aboard the wreck again, the spacewalk over.
            var wreck = server.GiantWreckForTest(id)!.Value;
            var at = server.DerelictCellToFlightForTest(id, wreck.Arrival);
            server.ShipMove("Justus", at.X, at.Y + 1.5f, at.Z);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus") && !server.InSpace("Justus"), "the suit boards the wreck through its way in");
            Assert.False(p.State.InEva);
            Assert.Contains(transport.To<StationBoarded>(p), b => b.StationId == id);
            server.Stop();
        }
    }
}
