// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A deployed vehicle never becomes unretrievable (#1660, #1661, #1662): the speeder unfolds on dry, level
/// ground or not at all; a driver who sinks it is set back onto dry ground; the seat is released on death;
/// dismounting steps the driver off the seat; and the landed ship can call a stranded speeder or boat back.
/// </summary>
public sealed class VehicleRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    // The same stage as the boat tests: the pilot stands at (10.5, 80, 10.5) on a stone slab facing +Z.
    private static readonly Vector3f Stand = new Vector3f(10.5f, 80f, 10.5f);

    public VehicleRecoveryTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_vrec_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, bool ship = false, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vrec"));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "vrec", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = ship };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Pilot(SvGameServer server, string item = "speeder")
    {
        var p = server.AddLocalPlayer("Pilot");
        p.State.Position = Stand;
        p.State.Yaw = 0f;
        p.State.AboardShip = false;
        p.State.Inventory.Add(item, 1, 1);
        p.State.SuitEnergy = 100f;
        return p;
    }

    private void Fill(SvGameServer server, string block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var id = block == "air" ? BlockId.Air : _content.GetBlock(block)!.NumericId;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), id);
                }
    }

    /// <summary>Stone to y=79, air above, all around the stand.</summary>
    private void StonePad(SvGameServer server)
    {
        Fill(server, "stone", 0, 24, 73, 79, 0, 24);
        Fill(server, "air", 0, 24, 80, 86, 0, 24);
    }

    /// <summary>A two-deep pool right in front of the pilot: water at y=78..79 → surface at 80.</summary>
    private void Pool(SvGameServer server) => Fill(server, "water", 8, 12, 78, 79, 12, 16);

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<(int Conn, object Msg)> Sent = new();
        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m)); }
        public void Broadcast(byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m)); }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private static IEnumerable<string> RejectionsTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason);

    private static IEnumerable<string> MessagesTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static void AssertStandable(SvGameServer server, Vector3f feet)
    {
        var c = new Vector3i((int)Math.Floor(feet.X), (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z));
        Assert.True(server.World.GetBlock(c).IsAir, $"feet cell {c} is not air");
        Assert.True(server.World.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).IsAir, $"head cell over {c} is not air");
        Assert.False(server.World.GetBlock(new Vector3i(c.X, c.Y - 1, c.Z)).IsAir, $"no floor under {c}");
    }

    // ---------------- #1660: deploy on dry ground, snap back out of the water ----------------

    [Fact]
    public void Deploy_SnapsTheSpeederOntoTheGround_EvenWhenThePlayerIsInTheAir()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            p.State.Position = new Vector3f(Stand.X, Stand.Y + 2f, Stand.Z); // mid-jump

            string id = server.DeploySpeederForTest("Pilot");

            Assert.NotEqual(string.Empty, id);
            var s = server.SpeederSnapshots.Single();
            Assert.Equal(80f, s.Pos.Y); // on the slab, not two blocks up in the air
            AssertStandable(server, s.Pos);
        }
    }

    [Fact]
    public void Deploy_RefusesOverWater_AndKeepsTheItem()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, transport: t);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server); // 2..5 blocks ahead is water

            string id = server.DeploySpeederForTest("Pilot");

            Assert.Equal(string.Empty, id);
            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("speeder"));
            Assert.Contains("@srv.speeder.need_land", RejectionsTo(t, p));
        }
    }

    [Fact]
    public void Driving_IntoTheSea_SetsTheSpeederBackOntoDryGround()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, transport: t);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Fill(server, "water", 8, 12, 78, 79, 16, 22); // a pool further out, off the deploy spot
            string id = server.DeploySpeederForTest("Pilot");
            var dry = server.SpeederSnapshots.Single().Pos;
            p.State.Position = dry;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", dry); // judged dry → remembered

            var wet = new Vector3f(10.5f, 80f, 18.5f); // over the pool: water two cells under the feet
            for (int i = 0; i < 29; i++)
            {
                server.DriveSpeederStepForTest("Pilot", wet);
            }

            Assert.Equal(wet.Z, p.State.Position.Z); // lenient: not yet
            server.DriveSpeederStepForTest("Pilot", wet);

            Assert.Equal(dry.Z, p.State.Position.Z, 2); // the 30th wet report snaps back
            Assert.Equal(dry.Z, server.SpeederSnapshots.Single().Pos.Z, 2);
            Assert.Contains("@srv.speeder.in_water", MessagesTo(t, p));
        }
    }

    // ---------------- #1661: the seat is released on death, the ship recalls a stranded vehicle ----------------

    [Fact]
    public void Death_ReleasesTheSeat_SoTheOwnerCanBoardAgain()
    {
        var server = NewServer(out var repo, ship: true);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            string id = server.DeploySpeederForTest("Pilot");
            p.State.Position = server.SpeederSnapshots.Single().Pos;
            server.EnterSpeederForTest("Pilot", id);
            Assert.Equal("Pilot", server.SpeederSnapshots.Single().DriverId);

            p.State.Health = 0f;
            server.Tick(0.1); // death → respawn at the ship

            Assert.Equal(string.Empty, p.State.InSpeeder);
            Assert.Equal(string.Empty, server.SpeederSnapshots.Single().DriverId);

            // Walk back and board it again — the bond does not linger.
            p.State.Position = server.SpeederSnapshots.Single().Pos;
            p.State.AboardShip = false;
            server.EnterSpeederForTest("Pilot", id);
            Assert.Equal("Pilot", server.SpeederSnapshots.Single().DriverId);
        }
    }

    /// <summary>No slot left: one stack of stone in every free slot (#1668's park-beside branch).</summary>
    private static void FillInventory(BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        while (p.State.Inventory.Add("stone", 64, 64) == 0)
        {
        }
    }

    /// <summary>A level stone apron around the pad, outside the reserved pad volume (the ship stays untouched):
    /// every recall-ring cell standable, so "nearest the cockpit" is decided by geometry alone.</summary>
    private void Apron(SvGameServer server, int padX, int padY, int padZ)
    {
        for (int dx = -20; dx <= 20; dx++)
            for (int dz = -20; dz <= 20; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) < 9)
                {
                    continue;
                }

                Fill(server, "stone", padX + dx, padX + dx, padY - 2, padY - 1, padZ + dz, padZ + dz);
                Fill(server, "air", padX + dx, padX + dx, padY, padY + 3, padZ + dz, padZ + dz);
            }
    }

    private static float Planar(Vector3f a, Vector3f b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void Recall_PacksTheSpeederIntoTheInventory_OnlyFromTheCockpit_AndNeverWhileDriven()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, ship: true, transport: t);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            string id = server.DeploySpeederForTest("Pilot");
            var rec = p.State.DeployedSpeeders.Single();

            // Far from the cockpit: refused, nothing moves, nothing packed.
            p.State.Position = Stand;
            server.RecallVehicleForTest("Pilot", id);
            Assert.Contains("@srv.station.too_far", RejectionsTo(t, p));
            Assert.Equal(Stand.Z + 2.5f, rec.Z, 1);
            Assert.Equal(0, p.State.Inventory.CountOf("speeder"));

            // Driven: refused.
            var cockpit = server.StationPosition("cockpit")!.Value;
            p.State.Position = new Vector3f(rec.X, rec.Y, rec.Z);
            server.EnterSpeederForTest("Pilot", id);
            p.State.Position = cockpit;
            p.State.AboardShip = true;
            server.RecallVehicleForTest("Pilot", id);
            Assert.Contains("@srv.speeder.driven", RejectionsTo(t, p));
            Assert.Single(server.SpeederSnapshots);

            // Parked, at the cockpit, a slot free: the recall IS a pack-up (#1668) — the item is back in the
            // inventory, the record and the live vehicle are gone, and the message says so.
            p.State.Position = new Vector3f(rec.X, rec.Y, rec.Z);
            p.State.AboardShip = false;
            server.ExitSpeederForTest("Pilot");
            p.State.Position = cockpit;
            p.State.AboardShip = true;

            server.RecallVehicleForTest("Pilot", id);

            Assert.Contains("@srv.speeder.recalled_packed", MessagesTo(t, p));
            Assert.Equal(1, p.State.Inventory.CountOf("speeder"));
            Assert.Empty(p.State.DeployedSpeeders);
            Assert.Empty(server.SpeederSnapshots);
        }
    }

    [Fact]
    public void Recall_WithAFullInventory_ParksTheSpeederNearestTheCockpit_AndPingsTheSpot()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, ship: true, transport: t);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            string id = server.DeploySpeederForTest("Pilot");
            var rec = p.State.DeployedSpeeders.Single();
            var cockpit = server.StationPosition("cockpit")!.Value;
            var pad = server.LandingPadInfoForTest(p.State.LandingPadIndex);
            Apron(server, pad.X, pad.Y, pad.Z);
            FillInventory(p);

            p.State.Position = cockpit;
            p.State.AboardShip = true;
            server.RecallVehicleForTest("Pilot", id);

            // Parked on the recall rings (pad radius 8 + 2..10 cells), standable, still out (no slot for it).
            string msg = MessagesTo(t, p).Single(m => m.StartsWith("@srv.speeder.recalled_parked:", StringComparison.Ordinal));
            var parked = server.SpeederSnapshots.Single();
            float dx = Math.Abs(parked.Pos.X - pad.X), dz = Math.Abs(parked.Pos.Z - pad.Z);
            Assert.True(Math.Max(dx, dz) >= 9.5f && Math.Max(dx, dz) <= 18.5f, $"parked at {parked.Pos}, pad ({pad.X}, {pad.Z})");
            AssertStandable(server, parked.Pos);
            Assert.Equal(parked.Pos.X, rec.X);
            Assert.Equal(0, p.State.Inventory.CountOf("speeder"));

            // Nearest the cockpit — not the first ring's (−r, −r) corner, where the old scan order always put it.
            float toParked = Planar(cockpit, parked.Pos);
            float toCorner = Planar(cockpit, new Vector3f(pad.X - 9.5f, parked.Pos.Y, pad.Z - 9.5f));
            Assert.True(toParked < toCorner - 1f, $"parked {toParked:F1} m from the cockpit, the old corner is {toCorner:F1} m");

            // The message carries the distance, and the spot is pinged for the owner.
            int metres = int.Parse(msg.Substring(msg.IndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
            float d3 = MathF.Sqrt(toParked * toParked + (cockpit.Y - parked.Pos.Y) * (cockpit.Y - parked.Pos.Y));
            Assert.InRange(metres, (int)MathF.Floor(d3) - 1, (int)MathF.Ceiling(d3) + 1);
            Assert.Contains(server.VisibleMarkersForTest("Pilot"), m => m.Ping);
        }
    }

    [Fact]
    public void Recall_PacksTheBoat_AndWithAFullInventory_FloatsItOnWaterNearThePad_OrSaysThereIsNone()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, ship: true, transport: t);
        using (repo)
        {
            var p = Pilot(server, "boat");
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            Assert.NotEqual(string.Empty, id);

            var cockpit = server.StationPosition("cockpit")!.Value;
            p.State.Position = cockpit;
            p.State.AboardShip = true;
            var pad = server.LandingPadInfoForTest(p.State.LandingPadIndex);

            // A slot free: packed (#1668).
            server.RecallVehicleForTest("Pilot", id);
            Assert.Contains("@srv.boat.recalled_packed", MessagesTo(t, p));
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
            Assert.Empty(p.State.DeployedSpeeders);
            Assert.Empty(server.SpeederSnapshots);

            // Out again, and no slot free this time: either the starter world has water within reach of the
            // pad, or the recall says there is none — never a silent no-op, never a boat dumped onto dry ground.
            p.State.Position = Stand;
            p.State.AboardShip = false;
            id = server.DeployVehicleForTest("Pilot", "boat");
            Assert.NotEqual(string.Empty, id);
            var rec = p.State.DeployedSpeeders.Single();
            FillInventory(p);
            p.State.Position = cockpit;
            p.State.AboardShip = true;

            server.RecallVehicleForTest("Pilot", id);
            bool none = RejectionsTo(t, p).Contains("@srv.boat.recall_no_water");
            Assert.True(none || MessagesTo(t, p).Any(m => m.StartsWith("@srv.boat.recalled_parked:", StringComparison.Ordinal)));
            Assert.Equal(0, p.State.Inventory.CountOf("boat"));

            // A pond dug just off the pad rim is always within reach: the recall floats the boat on water beside
            // the ship, on a real waterline, and pings the spot.
            int px = pad.X + 9, pz = pad.Z;
            int surface = pad.Y;
            Fill(server, "water", px, px + 2, surface - 2, surface - 1, pz - 1, pz + 1);
            Fill(server, "air", px, px + 2, surface, surface + 2, pz - 1, pz + 1);
            rec.X += 300f; // stranded far away

            server.RecallVehicleForTest("Pilot", id);

            Assert.Contains(MessagesTo(t, p), m => m.StartsWith("@srv.boat.recalled_parked:", StringComparison.Ordinal));
            Assert.True(Math.Max(Math.Abs(rec.X - pad.X), Math.Abs(rec.Z - pad.Z)) <= 14, $"boat at ({rec.X}, {rec.Z}), pad ({pad.X}, {pad.Z})");
            var hull = new Vector3i((int)Math.Floor(rec.X), (int)Math.Floor(rec.Y) - 1, (int)Math.Floor(rec.Z));
            Assert.Equal(_content.GetBlock("water")!.NumericId, server.World.GetBlock(hull)); // afloat, not beached
            Assert.Equal("boat", server.VehicleKindForTest(id));
            Assert.Contains(server.VisibleMarkersForTest("Pilot"), m => m.Ping);
        }
    }

    // ---------------- #1662: dismount steps the driver off the seat ----------------

    [Fact]
    public void Exit_StepsThePlayerOffTheSeat_OntoAStandableCellBesideTheHull()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            string id = server.DeploySpeederForTest("Pilot");
            var seat = server.SpeederSnapshots.Single().Pos;
            p.State.Position = seat;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", seat);

            server.ExitSpeederForTest("Pilot");

            var parked = server.SpeederSnapshots.Single();
            Assert.Equal(seat.X, parked.Pos.X, 2); // the vehicle stays where the driver got out
            var feet = p.State.Position;
            float planar = MathF.Sqrt((feet.X - seat.X) * (feet.X - seat.X) + (feet.Z - seat.Z) * (feet.Z - seat.Z));
            Assert.InRange(planar, 1.5f, 4f); // beside the 3×5 hull, not on the seat and not far away
            AssertStandable(server, feet);
        }
    }
}
