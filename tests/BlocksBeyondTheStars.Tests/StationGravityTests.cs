// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gravity volume of a player-built station (#1485): inside the stamped box + margin a boarder walks;
/// outside it the suit floats (the on-foot zero-g the client already knows from building above a planet's
/// atmosphere), with a one-shot hint; far beyond it he is pulled back to the pad instead of falling for ever.
/// </summary>
public sealed class StationGravityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationGravityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stgrav_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, IServerTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static void Edit(SvGameServer server, string playerId, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    /// <summary>Deploys a core and builds a sealed 5×5×5 iron shell around it (cells −2…2) with a slide door;
    /// then boards it. The box lands at world (8…12, 64…68, 8…12), the pad at (10.5, 65, 10.5).</summary>
    private static string BuildAndBoard(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    bool shell = Math.Abs(x) == 2 || Math.Abs(y) == 2 || Math.Abs(z) == 2;
                    bool doorway = x == 2 && y == -1 && z == 0;
                    if (shell && !doorway)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 2, -1, 0, "door_slide");
        Assert.True(server.StationIsBoardableForTest(id));

        var contact = server.SpaceEntitiesFor(playerId).First(e => e.Id == id);
        server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(playerId, id);
        Assert.True(server.InStation(playerId));
        return id;
    }

    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int ticks = 4)
    {
        for (int i = 0; i < ticks; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void Boarder_WestOfTheOrigin_KeepsHisPosition_AndIsNotPulledBack()
    {
        // Lyxette (v2026.9.1): "zu weit abgetrieben" 16–20 blocks from the pad, only ever on the WEST side.
        // The position handler wrapped X in every world — the station void world's circumference turned
        // x = −20 into x ≈ 5930, i.e. 72+ blocks beyond the box → pulled back to the pad each second (#1558).
        var transport = new RecordingTransport();
        var server = NewServer("westwing", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);

            server.MoveForTest("Owner", -20.5f, 65f, 10.5f);
            Assert.Equal(-20.5f, pilot.State.Position.X, 3); // stations keep their own coordinate space: no wrap
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.Equal(-20.5f, pilot.State.Position.X, 3); // …and the drift rescue never fired
            Assert.DoesNotContain(transport.Sent, m => m is ServerMessage sm && sm.Text.Contains("drifted_back"));
        }
    }

    [Fact]
    public void InteriorBuiltBeforeTheWriteBack_JoinsTheBox_OnTheNextBoarding()
    {
        // Lyxette (v2026.9.1): everything he built inside on v2026.8.26 existed as world edits only — the cell
        // grid (and with it the air reach and the gravity box) still described the 5³ seed hull, so a closed
        // iron room 20 blocks out warned "not airtight" and the suit floated in it (#1559).
        var iron = _content.GetBlock("iron_wall")!.NumericId;
        var farFloor = new Vector3i(30, 64, 10); // a wing floor tile 18 blocks past the seed hull's east face
        string id;
        {
            var s1 = NewServer("absorb", out var repo1, new RecordingTransport());
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildAndBoard(s1, pilot);
                s1.World.SetBlock(farFloor, iron); // a pre-#1481 interior edit: the world has it, the grid does not
                Assert.False(s1.StationCellsForTest(id).ContainsKey(new Vector3i(farFloor.X - 10, farFloor.Y - 66, farFloor.Z - 10)));
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("absorb", out var repo2, new RecordingTransport());
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            s2.EnterSpace("Owner");
            var contact = s2.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            s2.ShipMove("Owner", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            s2.BoardStation("Owner", id);
            Assert.True(s2.InStation("Owner"));

            // The boarding absorbed the wing: it is a cell of the build now, and standing on it is walking, not floating.
            Assert.True(s2.StationCellsForTest(id).ContainsKey(new Vector3i(farFloor.X - 10, farFloor.Y - 66, farFloor.Z - 10)),
                "the world's interior blocks must be folded into the cell grid on boarding (#1559)");
            TickAt(s2, pilot, new Vector3f(30.5f, 65f, 10.5f));
            Assert.False(s2.FloatingOutsideStationForTest("Owner"), "a wing the world holds is inside the gravity volume");
        }
    }

    [Fact]
    public void Boarder_Walks_InsideTheBox_AndFloats_Outside_WithAHint()
    {
        var transport = new RecordingTransport();
        var server = NewServer("gravity", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);

            var deck = new Vector3f(10.5f, 65f, 10.5f);
            TickAt(server, pilot, deck);
            Assert.False(server.FloatingOutsideStationForTest("Owner"), "on the deck you walk");

            // Twelve blocks beside the hull: past the box + its 8-block margin → the suit floats, and the
            // player is told once.
            var outside = new Vector3f(10.5f + 2 + 12, 65f, 10.5f);
            TickAt(server, pilot, outside);
            Assert.True(server.FloatingOutsideStationForTest("Owner"), "beyond the gravity volume you float");
            Assert.Single(transport.Sent.Where(m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g"));

            // Back onto the deck: walking again, no second hint.
            TickAt(server, pilot, deck);
            Assert.False(server.FloatingOutsideStationForTest("Owner"));
            TickAt(server, pilot, outside);
            Assert.Single(transport.Sent.Where(m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g"));
        }
    }

    [Fact]
    public void Boarder_DriftingFarBelow_IsPulledBackToThePad()
    {
        var transport = new RecordingTransport();
        var server = NewServer("drift", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);

            // A hundred blocks under the station: beyond box + margin + rescue distance → back on the pad.
            pilot.State.Position = new Vector3f(10.5f, 65f - 100f, 10.5f);
            server.RunVoidRescueForTest();

            Assert.True(pilot.State.Position.Y > 60f, $"expected the pad, got {pilot.State.Position}");
            Assert.False(server.FloatingOutsideStationForTest("Owner"));
            Assert.Contains(transport.Sent, m => m is RespawnNotice r && r.Reason == "@srv.station.drifted_back");
            Assert.True(server.InStation("Owner"), "the rescue keeps him aboard, it does not undock him");
        }
    }

    // ---- Zero-g construction mode (#1842): a per-player, session-only toggle for any boarder of a player station ----

    [Fact]
    public void ZeroGMode_On_FloatsOnThePad_AndOff_WalksAgain()
    {
        var transport = new RecordingTransport();
        var server = NewServer("zerog", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);
            var deck = new Vector3f(10.5f, 65f, 10.5f);
            TickAt(server, pilot, deck);
            Assert.False(server.FloatingOutsideStationForTest("Owner"));

            server.SetStationZeroGForTest("Owner", true);
            Assert.True(server.StationZeroGForTest("Owner"));
            Assert.True(server.FloatingOutsideStationForTest("Owner"), "the float applies at once, right on the pad");
            Assert.Contains(transport.Sent, m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g_on");
            Assert.Contains(transport.Sent, m => m is PlayerStateUpdate ps && ps.StationZeroG && ps.AboveAtmosphere);
            // The chosen float is not a drift over the edge: no "you have left the station's gravity" hint.
            Assert.DoesNotContain(transport.Sent, m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g");

            TickAt(server, pilot, deck);
            Assert.True(server.FloatingOutsideStationForTest("Owner"), "the tick keeps the chosen float");

            server.SetStationZeroGForTest("Owner", false);
            Assert.False(server.StationZeroGForTest("Owner"));
            Assert.False(server.FloatingOutsideStationForTest("Owner"), "gravity is back the moment the mode goes off");
            Assert.Contains(transport.Sent, m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g_off");
            Assert.Contains(transport.Sent, m => m is PlayerStateUpdate ps && !ps.StationZeroG && !ps.AboveAtmosphere);
        }
    }

    [Fact]
    public void ZeroGMode_IsIgnored_WhenNotOnAPlayerStation()
    {
        var transport = new RecordingTransport();
        var server = NewServer("zerog_planet", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Walker");
            Assert.False(server.InStation("Walker"));

            server.SetStationZeroGForTest("Walker", true);

            Assert.False(server.StationZeroGForTest("Walker"), "on a planet the toggle does nothing");
            Assert.False(pilot.State.AboveAtmosphere);
            Assert.DoesNotContain(transport.Sent, m => m is ServerMessage sm && sm.Text.StartsWith("@srv.station.zero_g_", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ZeroGMode_Clears_OnLeavingTheStation()
    {
        var transport = new RecordingTransport();
        var server = NewServer("zerog_leave", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);
            server.SetStationZeroGForTest("Owner", true);
            Assert.True(server.StationZeroGForTest("Owner"));

            server.LeaveStation("Owner");

            Assert.False(server.InStation("Owner"));
            Assert.False(server.StationZeroGForTest("Owner"), "the mode is per boarding — leaving drops it");
            Assert.False(pilot.State.AboveAtmosphere || server.FloatingOutsideStationForTest("Owner"));
        }
    }

    [Fact]
    public void ZeroGMode_DriftRescue_StillFires_Beyond64Blocks()
    {
        var transport = new RecordingTransport();
        var server = NewServer("zerog_drift", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);
            server.SetStationZeroGForTest("Owner", true);

            pilot.State.Position = new Vector3f(10.5f, 65f - 100f, 10.5f);
            server.RunVoidRescueForTest();

            Assert.True(pilot.State.Position.Y > 60f, $"expected the pad, got {pilot.State.Position}");
            Assert.Contains(transport.Sent, m => m is RespawnNotice r && r.Reason == "@srv.station.drifted_back");
            Assert.True(server.InStation("Owner"));
            Assert.True(server.StationZeroGForTest("Owner"), "the rescue moves him back, the chosen mode stays on");
        }
    }

    [Fact]
    public void ZeroGMode_FallDamage_IsWaived_ForThreeSecondsAfterSwitchingOff()
    {
        var transport = new RecordingTransport();
        var server = NewServer("zerog_fall", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);
            pilot.State.Health = 100f;

            // While hovering in the mode a reported impact is not a fall either.
            server.SetStationZeroGForTest("Owner", true);
            server.FallDamageForTest("Owner", 30f);
            Assert.Equal(100f, pilot.State.Health);

            // Switched off → the drop to the deck is the mode's doing: no damage within the grace window.
            server.SetStationZeroGForTest("Owner", false);
            server.TickForTest(0.5);
            server.FallDamageForTest("Owner", 30f);
            Assert.Equal(100f, pilot.State.Health);

            // Past the grace window a hard landing hurts as usual.
            for (int i = 0; i < 8; i++)
            {
                server.TickForTest(0.5);
            }

            server.FallDamageForTest("Owner", 30f);
            Assert.True(pilot.State.Health < 100f, "after the grace window a hard landing is a real fall again");
        }
    }

    [Fact]
    public void SetStationZeroGIntent_RoundTripsThroughNetCodec()
    {
        var on = NetCodec.Decode(NetCodec.Encode(new SetStationZeroGIntent { Enabled = true }));
        var off = NetCodec.Decode(NetCodec.Encode(new SetStationZeroGIntent { Enabled = false }));

        Assert.True(Assert.IsType<SetStationZeroGIntent>(on).Enabled);
        Assert.False(Assert.IsType<SetStationZeroGIntent>(off).Enabled);
    }
}
