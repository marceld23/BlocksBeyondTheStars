// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
/// Doors are bulkheads (#2653): a leak empties only the damaged room — the rooms behind its doors, the core zone and the
/// rest of the base keep their air, and only the players in that room hear about it (@base_air_lost only when every room
/// is gone). Plus the engine's guarantees at server level (#2650): a fill in progress keeps the old result, an unloaded
/// wing keeps its air, the emergency brake never takes air away. Rocky (toxic atmosphere) makes oxygen matter.
/// </summary>
public sealed class BaseAirBulkheadTests : IDisposable
{
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;

    private static readonly Vector3f InRoomA = new(10.5f, CoreY, 0.5f);
    private static readonly Vector3f InRoomB = new(20.5f, CoreY, 0.5f);
    private static readonly Vector3f InTheCoreZone = new(CoreX - 2.5f, CoreY, CoreZ + 0.5f);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_baseairbulkhead_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Records every server send with its connection, so a test can tell who heard what.</summary>
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

        public List<string> LinesTo(PlayerSession p)
            => Sent.Where(x => x.Conn == p.ConnectionId && x.Msg is ServerMessage).Select(x => ((ServerMessage)x.Msg).Text).ToList();
    }

    /// <summary>Keeps the server's warnings, so a test can count how often the brake was logged.</summary>
    private sealed class RecordingLog : IGameLogger
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Warnings = new();

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Enqueue(message);
        public void Error(string message) { }
    }

    private SvGameServer Start(string name, RecordingTransport transport, out SqliteWorldRepository repo, IGameLogger? log = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // toxic atmosphere → oxygen drains outside a life-support field
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, transport, repo, log);
        server.Start();
        return server;
    }

    /// <summary>Places the core for the player, standing beside it.</summary>
    private static int FoundCore(SvGameServer server, PlayerSession p)
    {
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
        return server.BaseSnapshots.Last().Id;
    }

    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>A stone shell around the interior [x0..x1]×[y0..y1]×[z0..z1].</summary>
    private void Shell(SvGameServer server, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = x0 - 1; x <= x1 + 1; x++)
            for (int y = y0 - 1; y <= y1 + 1; y++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    bool interior = x >= x0 && x <= x1 && y >= y0 && y <= y1 && z >= z0 && z <= z1;
                    server.World.SetBlock(new Vector3i(x, y, z), interior ? BlockId.Air : stone);
                }
    }

    /// <summary>A door in the wall at x, z (floor at <see cref="CoreY"/>), placed from <paramref name="standAt"/>.</summary>
    private static void Door(SvGameServer server, PlayerSession p, int x, int z, string kind, Vector3f standAt)
    {
        for (int y = CoreY; y <= CoreY + 2; y++)
        {
            server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
        }

        p.State.Inventory.Add(kind, 2, 16);
        p.State.Position = standAt;
        server.PlaceBlock(p.State.PlayerId, x, CoreY, z, kind);
    }

    /// <summary>
    /// The core, room A (x 6..14) behind an energy door at x 5 facing the core, and room B (x 16..24) behind a door of
    /// <paramref name="innerDoor"/> in the shared wall x 15 — both doors in the base's reach, so both rooms are seeds.
    /// </summary>
    private (SvGameServer Server, PlayerSession Player, int BaseId) Base(string name, RecordingTransport t,
        out SqliteWorldRepository repo, string innerDoor = "door_hinge", IGameLogger? log = null)
    {
        var server = Start(name, t, out repo, log);
        var p = OnFoot(server, "Builder");
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
        int baseId = server.BaseSnapshots.Last().Id;
        Shell(server, 6, 14, CoreY, CoreY + 2, -2, 2);
        Shell(server, 16, 24, CoreY, CoreY + 2, -2, 2);
        Door(server, p, 5, 0, "door_energy", new Vector3f(4.5f, CoreY, 0.5f));
        Door(server, p, 15, 0, innerDoor, new Vector3f(13.5f, CoreY, 0.5f));
        return (server, p, baseId);
    }

    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    private static Vector3i Cell(Vector3f v) => new((int)Math.Floor(v.X), (int)Math.Floor(v.Y), (int)Math.Floor(v.Z));

    [Fact]
    public void ALeakBehindADoor_EmptiesOnlyThatRoom()
    {
        var t = new RecordingTransport();
        var (server, p, baseId) = Base("bulkhead", t, out var repo);
        using (repo)
        {
            TickAt(server, p, InRoomA, 6);
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);

            server.World.SetBlock(new Vector3i(20, CoreY + 3, 0), BlockId.Air); // a hole in room B's roof
            TickAt(server, p, InRoomA, 8);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomB)), "room B leaks");
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomA)), "room A behind the door keeps its air");
            Assert.Equal(3, p.State.LifeSupportSource); // the builder in A still breathes base air
        }
    }

    [Fact]
    public void ALeakInTheFirstRoom_NeverBreaksTheChain_TheRoomBehindKeepsItsAir()
    {
        // base → A (leak) → B: B's door is in reach, so B is its own seed, not only a link of A's chain.
        var t = new RecordingTransport();
        var (server, p, baseId) = Base("chain", t, out var repo);
        using (repo)
        {
            TickAt(server, p, InRoomB, 6);
            server.World.SetBlock(new Vector3i(10, CoreY + 3, 0), BlockId.Air); // a hole in room A's roof
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, InRoomB, 8);
            Assert.True(p.State.Oxygen > before, $"room B keeps its air (oxygen {p.State.Oxygen})");
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomA)));
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
        }
    }

    [Fact]
    public void AnOpenEnergyDoor_IsStillABulkhead()
    {
        var t = new RecordingTransport();
        var (server, p, baseId) = Base("opencurtain", t, out var repo, innerDoor: "door_energy");
        using (repo)
        {
            var byTheDoor = new Vector3f(14.5f, CoreY, 0.5f); // in room A, right at the A|B curtain: it opens for the player
            TickAt(server, p, byTheDoor, 6);
            Assert.Contains(server.DoorSnapshots, d => d.Kind == "energy" && d.Open && Math.Abs(d.Pos.X - 15.5f) < 0.6f);

            server.World.SetBlock(new Vector3i(20, CoreY + 3, 0), BlockId.Air); // room B leaks
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, byTheDoor, 8);
            Assert.True(server.DoorSnapshots.Any(d => d.Kind == "energy" && d.Open), "the curtain is still open");
            Assert.True(p.State.Oxygen > before, $"room A keeps its air behind the open curtain (oxygen {p.State.Oxygen})");
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomB)));
        }
    }

    [Fact]
    public void TheLeakWarning_ReachesOnlyThePlayersInTheDamagedRoom_AndAirLostOnlyWhenEveryRoomFails()
    {
        var t = new RecordingTransport();
        var (server, ann, _) = Base("warnroom", t, out var repo);
        using (repo)
        {
            var ben = OnFoot(server, "Ben");
            void Tick(int halfSeconds)
            {
                for (int i = 0; i < halfSeconds; i++)
                {
                    ann.State.Position = InRoomA;
                    ben.State.Position = InRoomB;
                    server.TickForTest(0.5);
                }
            }

            Tick(6);
            t.Sent.Clear();

            server.World.SetBlock(new Vector3i(20, CoreY + 3, 0), BlockId.Air); // room B leaks
            Tick(8);
            var toBen = Assert.Single(t.LinesTo(ben), l => l.Contains("this room leaks", StringComparison.Ordinal));
            Assert.Contains("at 20/123/0, 3 blocks above you", toBen, StringComparison.Ordinal);
            Assert.DoesNotContain(t.LinesTo(ann), l => l.Contains("leak", StringComparison.Ordinal) || l == "@base_air_lost");
            Assert.DoesNotContain(t.LinesTo(ben), l => l == "@base_air_lost");

            t.Sent.Clear();
            server.World.SetBlock(new Vector3i(10, CoreY + 3, 0), BlockId.Air); // room A leaks too: no room is left
            Tick(8);
            Assert.Contains(t.LinesTo(ann), l => l.Contains("this room leaks", StringComparison.Ordinal));
            Assert.Contains("@base_air_lost", t.LinesTo(ann));
            Assert.Contains("@base_air_lost", t.LinesTo(ben));
        }
    }

    [Fact]
    public void AFillInProgress_KeepsTheOldResult_UntilItIsDone()
    {
        var t = new RecordingTransport();
        var server = Start("pending", t, out var repo);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
            int baseId = server.BaseSnapshots.Last().Id;
            Shell(server, 6, 65, CoreY, CoreY + 14, -30, 29); // a 60 × 15 × 60 hall: 3 600 spans
            Door(server, p, 5, 0, "door_slide", new Vector3f(4.5f, CoreY, 0.5f));
            var far = new Vector3i(60, CoreY, 20);
            var inHall = new Vector3f(20.5f, CoreY, -20.5f); // the player's oxygen tick asks about the hall every tick
            TickAt(server, p, inHall, 6);
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, far));

            // From now on only the tick driver's minimum moves a fill: 64 spans a tick — the refill takes dozens of ticks.
            server.AirTickBudgetMsForTest = 0;
            server.World.SetBlock(new Vector3i(60, CoreY + 15, 20), BlockId.Air); // a hole in the roof
            int pendingTicks = 0;
            for (int i = 0; i < 400; i++)
            {
                TickAt(server, p, inHall, 1);
                if (server.BaseAirFillPendingForTest(baseId))
                {
                    pendingTicks++;
                    Assert.True(server.BaseAirSuppliesNowForTest(baseId, far), "nobody loses air while the fill runs");
                }
                else if (pendingTicks > 0)
                {
                    break; // the refill is done
                }
            }

            Assert.True(pendingTicks > 5, $"the refill ran over several ticks ({pendingTicks})");
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, far), "once done, the hole counts");
        }
    }

    [Fact]
    public void AnUnloadedWing_NeverMakesTheRoomLeak()
    {
        var t = new RecordingTransport();
        var server = Start("unloaded", t, out var repo);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
            int baseId = server.BaseSnapshots.Last().Id;
            Shell(server, 6, 160, CoreY, CoreY + 2, -2, 2); // a long gallery
            Door(server, p, 5, 0, "door_slide", new Vector3f(4.5f, CoreY, 0.5f));
            var near = new Vector3i(20, CoreY, 0);
            var wing = new Vector3i(150, CoreY, 0);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, wing));

            // The far wing's chunks unload; a block set near the door makes the base refill. The unloaded cells bound the
            // gallery like walls: the part anyone can stand in keeps breathing, nobody is warned. (The unloaded cells
            // themselves are simply unknown now — nobody can stand there; the room found again is never carried, #2649 review.)
            var anchor = WorldConstants.WorldToChunk(new Vector3i(10, CoreY, 0));
            server.World.UnloadFarChunks(new[] { anchor }, 3);
            Assert.False(server.World.IsChunkLoaded(WorldConstants.WorldToChunk(wing)));
            t.Sent.Clear();
            server.World.SetBlock(new Vector3i(8, CoreY + 2, 0), _content.GetBlock("stone")!.NumericId);
            server.AdvanceUptimeForTest(2);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);

            Assert.True(server.BaseAirSuppliesNowForTest(baseId, near), "the loaded part of the gallery breathes");
            Assert.Equal(SvGameServer.BaseAirStatus.Supplied, server.DiagnoseBaseAirForTest(near.X, near.Y, near.Z).Status);
            Assert.DoesNotContain(t.Sent, x => x.Msg is ServerMessage m && (m.Text == "@base_air_lost" || m.Text.Contains("leak", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void TheEmergencyBrake_NeverTakesAirAway_AndIsLoggedOnce()
    {
        var t = new RecordingTransport();
        var log = new RecordingLog();
        var (server, p, baseId) = Base("brake", t, out var repo, log: log);
        using (repo)
        {
            // Room B gets a long annex behind its far wall; a per-room brake of 100 spans fits each room, not B with the annex.
            Shell(server, 26, 50, CoreY, CoreY + 2, -2, 2);
            server.SetAirBrakeForTest(maxCells: 10_000_000, maxSpans: 100);
            TickAt(server, p, InRoomB, 6);
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);
            Assert.False(server.BaseAirBrakedForTest(baseId));

            for (int y = CoreY; y <= CoreY + 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    server.World.SetBlock(new Vector3i(25, y, z), BlockId.Air); // the wall between B and the annex goes
                }

            float before = p.State.Oxygen = 50f;
            TickAt(server, p, InRoomB, 8);
            Assert.True(server.BaseAirBrakedForTest(baseId), "the bigger room hits the brake");
            Assert.True(p.State.Oxygen > before, $"… and its last known air keeps room B breathing (oxygen {p.State.Oxygen})");
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);

            server.World.SetBlock(new Vector3i(10, CoreY + 2, 2), _content.GetBlock("stone")!.NumericId); // another refill
            server.AdvanceUptimeForTest(2);
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);
            Assert.Single(log.Warnings, w => w.StartsWith("Base air:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ARoomPastTheBrakeOnTheFirstFill_LeavesTheOtherRoomsTheirAir()
    {
        // #2649 review: the brake used to count all rooms of a fill together, so a base whose first fill met one huge room
        // had no room at all. Now only that room is unknown; A and B breathe.
        var t = new RecordingTransport();
        var log = new RecordingLog();
        var server = Start("firstbrake", t, out var repo, log);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            server.SetAirBrakeForTest(maxCells: 10_000_000, maxSpans: 100);
            Shell(server, 6, 14, CoreY, CoreY + 2, -2, 2);   // A: 45 spans
            Shell(server, 16, 24, CoreY, CoreY + 2, -2, 2);  // B: 45 spans
            Shell(server, 26, 50, CoreY, CoreY + 2, -2, 2);  // C: 125 spans — past the brake
            Door(server, p, 5, 0, "door_energy", new Vector3f(4.5f, CoreY, 0.5f));
            Door(server, p, 15, 0, "door_hinge", new Vector3f(13.5f, CoreY, 0.5f));
            Door(server, p, 25, 0, "door_hinge", new Vector3f(23.5f, CoreY, 0.5f));
            int baseId = FoundCore(server, p);

            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomA)));
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, Cell(InRoomB)));
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, new Vector3i(40, CoreY, 0)), "the braked room is unknown — no air never known");
            Assert.True(server.BaseAirBrakedForTest(baseId));
            Assert.Single(log.Warnings, w => w.StartsWith("Base air:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ARoomChainedPastTheReachCap_KeepsItsAir_WhenTheRoomBeforeItLeaks()
    {
        // #2653 / review: core → corridor A (its door in reach) → door D2 past the 192-block reach cap → room B. B is found
        // only through A's door at first; once B breathed, D2 is a seed of its own, so a leak in A leaves B its air.
        var t = new RecordingTransport();
        var server = Start("chaincap", t, out var repo);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = FoundCore(server, p);
            var stone = _content.GetBlock("stone")!.NumericId;
            Shell(server, 6, 199, CoreY, CoreY + 2, 0, 0); // corridor A, its end wall at x 200
            for (int x = 200; x <= 210; x++)
                for (int y = CoreY - 1; y <= CoreY + 3; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        bool shell = x == 200 || x == 210 || y == CoreY - 1 || y == CoreY + 3 || Math.Abs(z) == 3;
                        server.World.SetBlock(new Vector3i(x, y, z), shell ? stone : BlockId.Air); // room B
                    }

            Door(server, p, 5, 0, "door_slide", new Vector3f(4.5f, CoreY, 0.5f));
            Door(server, p, 200, 0, "door_hinge", new Vector3f(199.5f, CoreY, 0.5f));
            var inB = new Vector3i(205, CoreY, 0);
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, inB));

            server.World.SetBlock(new Vector3i(100, CoreY + 3, 0), BlockId.Air); // a hole in A's roof
            server.AdvanceUptimeForTest(2);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.False(server.BaseAirSuppliesNowForTest(baseId, new Vector3i(50, CoreY, 0)), "A leaks");
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, inB), "B behind the bulkhead keeps its air");

            server.AdvanceUptimeForTest(9); // the backstop: B stays a seed of its own
            server.World.SetBlock(new Vector3i(207, CoreY + 2, 2), stone);
            server.AdvanceUptimeForTest(2);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, inB));
        }
    }

    [Fact]
    public void AGroupOnTheWorldBeingPlayed_IsKept_WhileThePlayerIdlesInTheCoreZone()
    {
        // #2649 review: a group nobody asked about for 120 s was dropped — and a player standing in the core zone never asks —
        // so the next question re-ran its first fill to the end every two minutes.
        var t = new RecordingTransport();
        var (server, p, baseId) = Base("idlekeep", t, out var repo);
        using (repo)
        {
            TickAt(server, p, InTheCoreZone, 4);
            Assert.True(server.BaseAirGroupKnownForTest(baseId));
            server.AdvanceUptimeForTest(130);
            TickAt(server, p, InTheCoreZone, 1);
            Assert.True(server.BaseAirGroupKnownForTest(baseId), "a group on the world being played is kept, asked or not");
        }
    }

    [Fact]
    public void AHugeBasesFirstFill_GoesOnSliced_InsteadOfStallingTheTick()
    {
        var t = new RecordingTransport();
        var server = Start("firstslice", t, out var repo);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            server.AirFirstFillBudgetMsForTest = 0; // the first step does a token amount of work …
            server.AirTickBudgetMsForTest = 0;      // … and only the tick driver's minimum moves it on
            Shell(server, 6, 65, CoreY, CoreY + 14, -30, 29); // 3 600 spans
            Door(server, p, 5, 0, "door_slide", new Vector3f(4.5f, CoreY, 0.5f));
            int baseId = FoundCore(server, p);
            Assert.True(server.BaseAirFillPendingForTest(baseId), "the first fill of a huge base is not run to the end at once");

            var inHall = new Vector3f(20.5f, CoreY, -20.5f);
            for (int i = 0; i < 400 && server.BaseAirFillPendingForTest(baseId); i++)
            {
                TickAt(server, p, inHall, 1);
            }

            Assert.False(server.BaseAirFillPendingForTest(baseId));
            Assert.True(server.BaseAirSuppliesNowForTest(baseId, new Vector3i(60, CoreY, 20)));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
