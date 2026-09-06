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
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The boat (#1215): the water <c>Kind</c> of the speeder system. Launching needs a water column ahead and sets
/// the boat onto the waterline; it never drains a cell and cannot be refuelled; a driver who keeps reporting
/// the boat ashore is set back onto the last water pose; packing up returns the <c>boat</c> item; the kind
/// survives a reload; and a water-world start hands out a boat.
/// </summary>
public sealed class BoatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    // The stage: the pilot stands at (10.5, 80, 10.5) facing +Z. A stone pad fills the launch scan window
    // (2–5 blocks ahead, ±3 sideways, 6 down / 3 up), and the water tests flood a 5×4 pool in front of it.
    private static readonly Vector3f Stand = new Vector3f(10.5f, 80f, 10.5f);

    public BoatTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_boat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string startPlanet = "varied", IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "boat"));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "boat", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, StartPlanet = startPlanet };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Pilot(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Pilot");
        p.State.Position = Stand;
        p.State.Yaw = 0f; // facing +Z
        p.State.Inventory.Add("boat", 1, 1);
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

    /// <summary>Dry land everywhere the launch scan can look (and under the stand): stone to y=79, air above.</summary>
    private void StonePad(SvGameServer server)
    {
        Fill(server, "stone", 4, 16, 73, 79, 6, 18);
        Fill(server, "air", 4, 16, 80, 84, 6, 18);
    }

    /// <summary>A two-deep pool right in front of the pilot: water at y=78..79 → waterline at y=80.</summary>
    private void Pool(SvGameServer server) => Fill(server, "water", 8, 12, 78, 79, 12, 15);

    /// <summary>Records every decoded outbound message so tests can assert on reject reasons.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private static IEnumerable<string> RejectionsTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason);

    [Fact]
    public void Content_BoatIsAWaterVehicle_WithAWorkshopRecipeAndNoBlueprint()
    {
        var boat = _content.GetItem("boat");
        Assert.NotNull(boat);
        Assert.NotNull(boat!.Vehicle);
        Assert.Equal("boat", boat.Vehicle!.Kind);
        Assert.Equal("water", boat.Vehicle.Medium);
        Assert.False(boat.Vehicle.Fuel);
        Assert.Equal("gadget", boat.Tool?.Kind.ToString().ToLowerInvariant()); // deploys through the gadget path

        var speeder = _content.GetItem("speeder")!.Vehicle;
        Assert.NotNull(speeder);
        Assert.Equal("speeder", speeder!.Kind);
        Assert.True(speeder.Fuel);

        var recipe = _content.GetRecipe("boat");
        Assert.NotNull(recipe);
        Assert.True(string.IsNullOrEmpty(recipe!.RequiredBlueprint)); // early-game: no blueprint gate
        Assert.Contains(recipe.Inputs, i => i.Item == "wood_log");
    }

    [Fact]
    public void Launch_OnDryLand_IsRefused_AndKeepsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);

            string id = server.DeployVehicleForTest("Pilot", "boat");

            Assert.Equal(string.Empty, id);
            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
        }
    }

    [Fact]
    public void Launch_OntoWater_SetsTheBoatOnTheWaterline_WithNoCell()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);

            string id = server.DeployVehicleForTest("Pilot", "boat");

            Assert.NotEqual(string.Empty, id);
            var s = server.SpeederSnapshots.Single();
            Assert.Equal("boat", s.Kind);
            Assert.Equal(80.3f, s.Pos.Y, 2);            // waterline (top of the y=79 water cell) + 0.3
            Assert.InRange(s.Pos.Z, 12f, 16f);           // in the pool ahead, not at the pilot's feet
            Assert.Equal(0f, s.FuelMax);                 // no cell
            Assert.Equal(0, p.State.Inventory.CountOf("boat"));
            Assert.Equal("boat", p.State.DeployedSpeeders.Single().Kind);
        }
    }

    [Fact]
    public void Boat_NeverDrains_AndCannotBeRefuelled()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);
            Assert.Equal(id, p.State.InSpeeder);

            // Cruise up and down the pool: the (non-existent) cell never moves.
            for (int i = 0; i < 10; i++)
            {
                server.DriveSpeederStepForTest("Pilot", new Vector3f(pos.X + (i % 2 == 0 ? 1.5f : -1.5f), pos.Y, pos.Z + (i % 3)));
            }

            var s = server.SpeederSnapshots.Single();
            Assert.Equal(0f, s.Fuel);
            Assert.Equal(0f, s.FuelMax);

            p.State.Inventory.Add("energy_cell_1", 1, 99);
            server.RefuelSpeederForTest("Pilot", id);
            Assert.Equal(1, p.State.Inventory.CountOf("energy_cell_1")); // refused — nothing to fill
            Assert.Equal(0f, server.SpeederSnapshots.Single().Fuel);
        }
    }

    [Fact]
    public void Exit_MidLake_PutsTheDriverInTheWaterBesideTheHull_NotOnTheSeat()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            // A wide lake: stone to y=79 everywhere, water at y=78..79 from z=12 on, banks ≥ 8 cells from the middle.
            Fill(server, "stone", 0, 24, 73, 79, 0, 30);
            Fill(server, "air", 0, 24, 80, 84, 0, 30);
            Fill(server, "water", 2, 22, 78, 79, 12, 28);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var water = server.SpeederSnapshots.Single().Pos;
            p.State.Position = water;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", water);
            server.DriveSpeederStepForTest("Pilot", new Vector3f(12.5f, 80.3f, 15.5f));
            var mid = new Vector3f(12.5f, 80.3f, 20.5f);
            server.DriveSpeederStepForTest("Pilot", mid);

            server.ExitSpeederForTest("Pilot");

            // Beside the hull, in the water, a block under the waterline — not on the seat inside the hull (#1671).
            var feet = p.State.Position;
            float planar = MathF.Sqrt((feet.X - mid.X) * (feet.X - mid.X) + (feet.Z - mid.Z) * (feet.Z - mid.Z));
            Assert.InRange(planar, 1.5f, 3.5f);
            Assert.Equal(79f, feet.Y, 2);
            var cell = new Vector3i((int)Math.Floor(feet.X), 79, (int)Math.Floor(feet.Z));
            Assert.Equal(_content.GetBlock("water")!.NumericId, server.World.GetBlock(cell));
            Assert.Equal(mid.X, server.SpeederSnapshots.Single().Pos.X, 2); // the boat stays where the driver got out
            Assert.Equal(string.Empty, p.State.InSpeeder);
        }
    }

    [Fact]
    public void DrivingAshore_ForLongEnough_SnapsBackToTheLastWaterPose()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var water = server.SpeederSnapshots.Single().Pos;
            p.State.Position = water;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", water); // registers the wet pose

            // Drive onto the stone pad behind the pilot's stand (no water within two cells under the hull).
            var land = new Vector3f(10.5f, 80.3f, 8.5f);
            for (int i = 0; i < 29; i++)
            {
                server.DriveSpeederStepForTest("Pilot", land);
            }

            Assert.Equal(land.Z, p.State.Position.Z, 2); // lenient: 29 dry reports are still the driver's business
            p.AwaitingSpawnAdopt = false;                 // whatever the join left behind — the snap must arm it itself

            server.DriveSpeederStepForTest("Pilot", land);   // the 30th trips the rule
            Assert.Equal(water.X, p.State.Position.X, 2);
            Assert.Equal(water.Z, p.State.Position.Z, 2);    // back on the water, where the boat last floated
            Assert.Equal(water.Z, server.SpeederSnapshots.Single().Pos.Z, 2);
            Assert.Equal(id, p.State.InSpeeder);             // still aboard — nothing was destroyed or ejected
            Assert.True(p.AwaitingSpawnAdopt);               // #865/#1301: the client's stale ashore stream is dropped until it snaps
        }
    }

    [Fact]
    public void DrivingIntoAnUnloadedChunk_KeepsThePreviousWaterPose_AndSnapsBackToIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var water = server.SpeederSnapshots.Single().Pos;
            p.State.Position = water;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", water); // judged wet → remembered
            Assert.True(server.BoatWaterPosForTest(id).Known);

            // Sweep the whole cache (an anchor nobody is near keeps nothing): the stage is now "not loaded".
            var stage = WorldConstants.WorldToChunk(new Vector3i(10, 80, 8));
            var nowhere = new ChunkCoord(stage.X + 20, stage.Y, stage.Z + 20);
            server.World.UnloadFarChunks(new[] { nowhere }, 0);
            Assert.False(server.World.IsChunkLoaded(stage));

            // A pose the server cannot judge must be neither "ashore" nor the new snap-back target (#1301).
            var unjudged = new Vector3f(14.5f, 80.3f, 8.5f);
            server.DriveSpeederStepForTest("Pilot", unjudged);
            var (known, last) = server.BoatWaterPosForTest(id);
            Assert.True(known);
            Assert.Equal(water.X, last.X, 2);
            Assert.Equal(water.Z, last.Z, 2);

            // Bring the stage back (edits are persisted, so the pool is still there) and beach the boat for real.
            server.World.GetOrLoadChunk(stage);
            server.World.GetOrLoadChunk(WorldConstants.WorldToChunk(new Vector3i(10, 78, 12)));
            Assert.Equal(_content.GetBlock("water")!.NumericId, server.World.GetBlock(new Vector3i(10, 79, 13)));
            var land = new Vector3f(10.5f, 80.3f, 8.5f);
            for (int i = 0; i < 30; i++)
            {
                server.DriveSpeederStepForTest("Pilot", land);
            }

            Assert.Equal(water.X, p.State.Position.X, 2); // back where it last floated — not at the unjudged pose
            Assert.Equal(water.Z, p.State.Position.Z, 2);
        }
    }

    [Fact]
    public void DrivingIntoAnUnloadedChunk_WithNoWaterPoseKnown_LearnsNothing()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            p.State.Position = server.SpeederSnapshots.Single().Pos;
            server.EnterSpeederForTest("Pilot", id);
            Assert.False(server.BoatWaterPosForTest(id).Known); // boarding alone judges nothing

            var stage = WorldConstants.WorldToChunk(new Vector3i(10, 80, 8));
            server.World.UnloadFarChunks(new[] { new ChunkCoord(stage.X + 20, stage.Y, stage.Z + 20) }, 0);

            server.DriveSpeederStepForTest("Pilot", new Vector3f(14.5f, 80.3f, 8.5f));

            Assert.False(server.BoatWaterPosForTest(id).Known); // still nothing sane to snap to
        }
    }

    [Fact]
    public void Launch_OntoLava_IsRefused_LikeDryLand()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, transport: t);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Fill(server, "lava", 8, 12, 78, 79, 12, 15); // the pool, but molten

            string id = server.DeployVehicleForTest("Pilot", "boat");

            Assert.Equal(string.Empty, id);
            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
            Assert.Contains("@srv.boat.need_water", RejectionsTo(t, p));
        }
    }

    [Fact]
    public void SomeoneElsesVehicle_IsRejected_InTheVehiclesOwnWording()
    {
        var t = new RecordingTransport();
        var server = NewServer(out var repo, transport: t);
        using (repo)
        {
            var owner = Pilot(server);
            owner.State.Inventory.Add("speeder", 1, 1);
            StonePad(server);
            Pool(server);
            string boat = server.DeployVehicleForTest("Pilot", "boat");
            owner.State.Yaw = 180f; // the speeder unfolds on the stone behind — never into the pool (#1660)
            string speeder = server.DeployVehicleForTest("Pilot", "speeder");
            Assert.NotEqual(string.Empty, boat);
            Assert.NotEqual(string.Empty, speeder);

            var thief = server.AddLocalPlayer("Thief");
            thief.State.Position = server.SpeederSnapshots.Single(s => s.Id == boat).Pos; // in reach — ownership is the only bar
            thief.State.Inventory.Add("energy_cell_1", 1, 1);

            server.EnterSpeederForTest("Thief", boat);
            server.StowSpeederForTest("Thief", boat);
            server.RefuelSpeederForTest("Thief", boat);
            server.EnterSpeederForTest("Thief", speeder);

            var rejects = RejectionsTo(t, thief).ToList();
            Assert.Equal(3, rejects.Count(r => r == "@srv.boat.not_yours"));      // board, pack up, refuel — all in boat wording (#1301)
            Assert.Equal(1, rejects.Count(r => r == "@srv.speeder.not_yours"));   // the speeder keeps its own
            Assert.Equal(string.Empty, thief.State.InSpeeder);
            Assert.Equal(2, server.SpeederCount);
        }
    }

    [Fact]
    public void Stow_ReturnsTheBoatItem_NotASpeeder()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            p.State.Position = server.SpeederSnapshots.Single().Pos; // within reach

            server.StowSpeederForTest("Pilot", id);

            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
            Assert.Equal(0, p.State.Inventory.CountOf("speeder"));
        }
    }

    [Fact]
    public void DeployedBoat_SurvivesAReload_AsABoat()
    {
        string id;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            Pilot(server);
            StonePad(server);
            Pool(server);
            id = server.DeployVehicleForTest("Pilot", "boat");
            Assert.NotEqual(string.Empty, id);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            server2.ReconcileSpeedersForTest("Pilot");
            Assert.Equal(1, server2.SpeederCount);
            Assert.Equal("boat", server2.VehicleKindForTest(id));
            Assert.Equal(0f, server2.SpeederSnapshots.Single().FuelMax);
        }
    }

    [Fact]
    public void WaterWorldStart_HandsOutABoat_OtherStartsDoNot()
    {
        var ocean = NewServer(out var repo1, startPlanet: "ocean");
        using (repo1)
        {
            Assert.True(ocean.StartBodyIsWaterWorldForTest);
            var p = ocean.AddLocalPlayer("Pilot");
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        System.IO.Directory.Delete(_root, recursive: true);

        var varied = NewServer(out var repo2);
        using (repo2)
        {
            Assert.False(varied.StartBodyIsWaterWorldForTest);
            var p = varied.AddLocalPlayer("Pilot");
            Assert.Equal(0, p.State.Inventory.CountOf("boat"));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
