// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The two-cell bed and the bench (#1846): a placed bed is a head half plus a foot half on the cell its yaw
/// points to (server-stamped, refused when that cell is not free), both halves go with whichever one is
/// mined and drop ONE bed, the legacy one-cell Slab bed keeps working, and the bench joins the chair as a
/// seat. The bed halves and the bench live at the top of the 6-bit shape field so saved custom-form ids
/// (persisted by index from 19 upward) stay untouched — see <c>CustomShapeTests</c> for that range.
/// </summary>
public sealed class BedPairTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BedPairTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bedpair_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, RecordingTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bedpair"));
        IServerTransport st = transport is not null ? transport : new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "bedpair", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at the origin with a few beds in the pack; the cells around y=64 are air.</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession Sleeper(SvGameServer server, int beds = 3)
    {
        var p = server.AddLocalPlayer("Sleeper");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 64, 0.5f);
        p.State.Yaw = 0f; // facing +Z
        if (beds > 0)
        {
            p.State.Inventory.Add("bed", beds, 64);
        }

        return p;
    }

    private ushort BedId => _content.GetBlock("bed")!.NumericId.Value;

    private static int ShapeAt(SvGameServer server, int x, int y, int z) => ShapeCode.ShapeOf(server.World.GetShape(new Vector3i(x, y, z)));

    private static int YawAt(SvGameServer server, int x, int y, int z) => ShapeCode.OrientationOf(server.World.GetShape(new Vector3i(x, y, z)));

    /// <summary>Records every decoded outbound message so tests can assert on reject reasons and broadcasts.</summary>
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

    // ── the shared model ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BedPartner_FollowsTheClientYawRotation()
    {
        // The head's foot side is its local +Z; ShapeCode.YawDirection is the rotation the client mesher applies
        // (0 = +Z, 1 = −X, 2 = −Z, 3 = +X), so the foot cell is exactly where the drawn headboard is not.
        var expected = new[] { (0, 1), (-1, 0), (0, -1), (1, 0) };
        for (int yaw = 0; yaw < 4; yaw++)
        {
            Assert.Equal(expected[yaw], ShapeCode.YawDirection(yaw));
            Assert.Equal(yaw, ShapeCode.YawToward(expected[yaw].Item1, expected[yaw].Item2));

            int head = ShapeCode.Pack(BlockShape.BedHead, yaw);
            Assert.True(FurnitureShapes.TryBedPartnerOffset(head, out int dx, out int dz));
            Assert.Equal(expected[yaw], (dx, dz));

            // The foot points back at the head, and the partner descriptor swaps the half but keeps the yaw.
            int foot = FurnitureShapes.BedPartnerDescriptor(head);
            Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(foot));
            Assert.Equal(yaw, ShapeCode.OrientationOf(foot));
            Assert.True(FurnitureShapes.TryBedPartnerOffset(foot, out int bx, out int bz));
            Assert.Equal((-dx, -dz), (bx, bz));
            Assert.Equal(head, FurnitureShapes.BedPartnerDescriptor(foot));
        }

        // The heading → geometry conversion the auto placement uses: +Z/−Z keep their index, ±X swap.
        Assert.Equal(0, ShapeCode.YawFacingForward(0));
        Assert.Equal(3, ShapeCode.YawFacingForward(1));
        Assert.Equal(2, ShapeCode.YawFacingForward(2));
        Assert.Equal(1, ShapeCode.YawFacingForward(3));

        // The legacy one-cell bed and every non-bed form have no partner.
        Assert.False(FurnitureShapes.TryBedPartnerOffset(ShapeCode.Pack(BlockShape.Slab, 1), out _, out _));
        Assert.False(FurnitureShapes.TryBedPartnerOffset(ShapeCode.Pack(BlockShape.Chair, 1), out _, out _));
        Assert.Equal(0, FurnitureShapes.BedPartnerDescriptor(ShapeCode.Pack(BlockShape.Slab, 1)));
    }

    [Fact]
    public void Bench_IsASeat_LikeTheChair_AndOtherFurnitureIsNot()
    {
        Assert.True(FurnitureShapes.IsSeat((int)BlockShape.Chair));
        Assert.True(FurnitureShapes.IsSeat((int)BlockShape.Bench));
        Assert.False(FurnitureShapes.IsSeat((int)BlockShape.Table));
        Assert.False(FurnitureShapes.IsSeat((int)BlockShape.BedHead));
        Assert.False(FurnitureShapes.IsSeat(0));

        Assert.True(FurnitureShapes.IsBedHalf((int)BlockShape.BedHead));
        Assert.True(FurnitureShapes.IsBedHalf((int)BlockShape.BedFoot));
        Assert.False(FurnitureShapes.IsBedHalf((int)BlockShape.Slab));
        Assert.False(FurnitureShapes.IsBedHalf((int)BlockShape.Bench));
    }

    [Fact]
    public void Bench_IsCraftedWithTheShapeAction_AndPlacesAsABench()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Sleeper(server);
            p.State.Inventory.Add("mud", 2, 99); // shapeable and mineable by hand

            // The Shape action accepts the high built-in index like any low one (no registry needed).
            server.ShapeCraft(p.State.PlayerId, "mud", (int)BlockShape.Bench);
            string bench = ItemKey.Compose("mud", 0, 0, (int)BlockShape.Bench);
            Assert.Equal("mud#s3f", bench);
            Assert.Equal(1, p.State.Inventory.CountOf(bench));

            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, bench, yaw: 1);
            Assert.Equal((int)BlockShape.Bench, ShapeAt(server, 2, 64, 0));
            Assert.Equal(1, YawAt(server, 2, 64, 0));

            // Mining hands the bench-shaped mud back (a player's own form is NOT stripped, unlike a prop stamp).
            server.MineBlock(p.State.PlayerId, 2, 64, 0);
            Assert.Equal(1, p.State.Inventory.CountOf(bench));
        }
    }

    // ── placing ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlaceBed_WritesHeadAndFoot_WithTheSameYaw_AndBroadcastsBoth()
    {
        var t = new RecordingTransport();
        var server = Started(out var repo, t);
        using (repo)
        {
            var p = Sleeper(server, beds: 1);

            // Yaw 3 → the foot lies at +X of the head (ShapeCode.YawDirection).
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 3);

            Assert.Equal(BedId, server.World.GetBlock(new Vector3i(2, 64, 0)).Value);
            Assert.Equal(BedId, server.World.GetBlock(new Vector3i(3, 64, 0)).Value);
            Assert.Equal((int)BlockShape.BedHead, ShapeAt(server, 2, 64, 0));
            Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, 3, 64, 0));
            Assert.Equal(3, YawAt(server, 2, 64, 0));
            Assert.Equal(3, YawAt(server, 3, 64, 0));
            Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(server.World.GetShape(new Vector3i(3, 64, 0))));
            Assert.Equal(0, p.State.Inventory.CountOf("bed")); // ONE bed consumed for two cells

            // Both cells went out as block changes carrying their forms — the client meshes the foot at once.
            var changes = t.Sent.Select(s => s.Msg).OfType<BlockChanged>().Where(b => b.Block == BedId).ToList();
            Assert.Contains(changes, b => b.X == 2 && b.Z == 0 && ShapeCode.ShapeOf(b.Shape) == (int)BlockShape.BedHead);
            Assert.Contains(changes, b => b.X == 3 && b.Z == 0 && ShapeCode.ShapeOf(b.Shape) == (int)BlockShape.BedFoot);
            Assert.Empty(RejectionsTo(t, p));
        }
    }

    [Fact]
    public void PlaceBed_FollowsThePlayersFacing_WhenNoYawIsSent()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Sleeper(server, beds: 4);

            // Without a rotate-key yaw the foot ALWAYS lands in the cell the player is facing — on every
            // heading, even though the raw heading index and the geometry yaw disagree for ±X (the server
            // converts through ShapeCode.YawFacingForward). Heading: Unity euler, 0 = +Z, 90 = +X.
            var cases = new[]
            {
                (Heading: 0f, HeadX: 2, HeadZ: 2, FootX: 2, FootZ: 3, Yaw: 0),   // north → foot at +Z
                (Heading: 90f, HeadX: 2, HeadZ: -2, FootX: 3, FootZ: -2, Yaw: 3), // east → foot at +X
                (Heading: 180f, HeadX: -2, HeadZ: 2, FootX: -2, FootZ: 1, Yaw: 2), // south → foot at −Z
                (Heading: 270f, HeadX: -2, HeadZ: -2, FootX: -3, FootZ: -2, Yaw: 1), // west → foot at −X
            };
            foreach (var c in cases)
            {
                p.State.Yaw = c.Heading;
                server.PlaceBlock(p.State.PlayerId, c.HeadX, 64, c.HeadZ, "bed");
                Assert.Equal((int)BlockShape.BedHead, ShapeAt(server, c.HeadX, 64, c.HeadZ));
                Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, c.FootX, 64, c.FootZ));
                Assert.Equal(c.Yaw, YawAt(server, c.HeadX, 64, c.HeadZ));
                Assert.Equal(c.Yaw, YawAt(server, c.FootX, 64, c.FootZ));
            }

            Assert.Equal(0, p.State.Inventory.CountOf("bed"));
        }
    }

    [Fact]
    public void PlaceBed_RefusesABlockedFootCell_AndKeepsTheItem()
    {
        var t = new RecordingTransport();
        var server = Started(out var repo, t);
        using (repo)
        {
            var p = Sleeper(server, beds: 1);
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(2, 64, 1), stone); // right where yaw 0 wants the foot

            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 0);

            Assert.Contains("@srv.place.bed_room", RejectionsTo(t, p));
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 0)).IsAir, "no lone head half may be left behind");
            Assert.Equal(stone.Value, server.World.GetBlock(new Vector3i(2, 64, 1)).Value);
            Assert.Equal(1, p.State.Inventory.CountOf("bed")); // nothing consumed

            // Turned away from the wall the same bed goes down fine.
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 2);
            Assert.Equal((int)BlockShape.BedHead, ShapeAt(server, 2, 64, 0));
            Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, 2, 64, -1));
            Assert.Equal(0, p.State.Inventory.CountOf("bed"));
        }
    }

    [Fact]
    public void PlaceBed_RefusesAFootCellOutOfReach_OrOnThePlayersHead()
    {
        var t = new RecordingTransport();
        var server = Started(out var repo, t);
        using (repo)
        {
            var p = Sleeper(server, beds: 2);

            // Reach is 8 m + 1 m slack to the block's near face: a head at z=9 (8.5 m) is the last cell in reach,
            // its foot at z=10 (9.5 m) is not → the whole bed is refused, nothing consumed.
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.PlaceBlock(p.State.PlayerId, 0, 64, 9, "bed", yaw: 0);
            Assert.Contains("@srv.place.bed_room", RejectionsTo(t, p));
            Assert.True(server.World.GetBlock(new Vector3i(0, 64, 9)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(0, 64, 10)).IsAir);
            Assert.Equal(2, p.State.Inventory.CountOf("bed"));

            // Turned the other way the foot comes closer, and the same head cell is fine.
            server.PlaceBlock(p.State.PlayerId, 0, 64, 9, "bed", yaw: 2);
            Assert.Equal((int)BlockShape.BedHead, ShapeAt(server, 0, 64, 9));
            Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, 0, 64, 8));

            // The foot may not land in the player's own head cell either: head at (0,65,-1) with yaw 0 puts the
            // foot at (0,65,0) = right above the player's feet cell.
            t.Sent.Clear();
            server.PlaceBlock(p.State.PlayerId, 0, 65, -1, "bed", yaw: 0);
            Assert.Contains("@srv.place.bed_room", RejectionsTo(t, p));
            Assert.True(server.World.GetBlock(new Vector3i(0, 65, -1)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(0, 65, 0)).IsAir);
        }
    }

    // ── mining ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MineEitherHalf_ClearsBoth_AndDropsOneBed()
    {
        var t = new RecordingTransport();
        var server = Started(out var repo, t);
        using (repo)
        {
            var p = Sleeper(server, beds: 1);
            p.State.Inventory.Add("diamond_drill", 1, 1);

            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 0); // head (2,0), foot (2,1)
            Assert.Equal(0, p.State.Inventory.CountOf("bed"));

            // Mining the HEAD takes the foot with it and yields exactly one plain bed (no "#s3e" suffix).
            t.Sent.Clear();
            server.MineBlock(p.State.PlayerId, 2, 64, 0);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 0)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 1)).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("bed"));
            Assert.Equal(0, p.State.Inventory.CountOf(ItemKey.Compose("bed", 0, 0, (int)BlockShape.BedHead)));
            Assert.Equal(0, p.State.Inventory.CountOf(ItemKey.Compose("bed", 0, 0, (int)BlockShape.BedFoot)));
            var cleared = t.Sent.Select(s => s.Msg).OfType<BlockChanged>().Where(b => b.Block == 0).ToList();
            Assert.Contains(cleared, b => b.X == 2 && b.Y == 64 && b.Z == 0);
            Assert.Contains(cleared, b => b.X == 2 && b.Y == 64 && b.Z == 1);

            // Mining the FOOT of a fresh bed does the same.
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 0);
            Assert.Equal(0, p.State.Inventory.CountOf("bed"));
            server.MineBlock(p.State.PlayerId, 2, 64, 1);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 0)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 1)).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("bed"));
        }
    }

    [Fact]
    public void MiningAHalf_TakesOnlyItsTruePartner()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Sleeper(server, beds: 2);
            p.State.Inventory.Add("diamond_drill", 1, 1);

            // Two beds foot to foot: A head (2,0) foot (2,1) with yaw 0; B head (2,3) foot (2,2) with yaw 2.
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 0);
            server.PlaceBlock(p.State.PlayerId, 2, 64, 3, "bed", yaw: 2);
            Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, 2, 64, 1));
            Assert.Equal((int)BlockShape.BedFoot, ShapeAt(server, 2, 64, 2));

            // Mining A's foot clears A only: its partner is the head at (2,0), not the foot next door.
            server.MineBlock(p.State.PlayerId, 2, 64, 1);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 0)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 1)).IsAir);
            Assert.Equal(BedId, server.World.GetBlock(new Vector3i(2, 64, 2)).Value);
            Assert.Equal(BedId, server.World.GetBlock(new Vector3i(2, 64, 3)).Value);
            Assert.Equal(1, p.State.Inventory.CountOf("bed"));

            // A world edit that turned B's foot the wrong way leaves an unmatched pair: mining B's head clears
            // only the head — a bed half with the wrong yaw is not this bed's partner (and never drops twice).
            server.World.SetBlock(new Vector3i(2, 64, 2), _content.GetBlock("bed")!.NumericId, 0, 0, ShapeCode.Pack(BlockShape.BedFoot, 1));
            server.MineBlock(p.State.PlayerId, 2, 64, 3);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 3)).IsAir);
            Assert.Equal(BedId, server.World.GetBlock(new Vector3i(2, 64, 2)).Value);
            Assert.Equal(2, p.State.Inventory.CountOf("bed"));

            // The stray half on its own still drops a plain bed (stamped forms are stripped).
            server.MineBlock(p.State.PlayerId, 2, 64, 2);
            Assert.True(server.World.GetBlock(new Vector3i(2, 64, 2)).IsAir);
            Assert.Equal(3, p.State.Inventory.CountOf("bed"));
        }
    }

    // ── the legacy one-cell bed ────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacySlabBed_StillMinesToAPlainBed_AndArmsTheHomeSpawn()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Sleeper(server, beds: 0);
            p.State.Inventory.Add("diamond_drill", 1, 1);

            // A bed from before #1846 (old save, ship layout, station template, cramped procedural room).
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("bed")!.NumericId, 0, 0, ShapeCode.Pack(PropShapes.BedSingleCell, 1));
            Assert.Equal((int)BlockShape.Slab, ShapeAt(server, 1, 64, 0));

            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "E on a legacy bed still arms the home spawn");

            server.MineBlock(p.State.PlayerId, 1, 64, 0);
            Assert.True(server.World.GetBlock(new Vector3i(1, 64, 0)).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("bed")); // plain key — the Slab is a stamped form, stripped
            Assert.True(server.World.GetBlock(new Vector3i(1, 64, 1)).IsAir); // nothing else touched
            Assert.True(server.World.GetBlock(new Vector3i(0, 64, 0)).IsAir);
        }
    }

    [Fact]
    public void HomeSpawn_ArmsOnEitherHalf_OfATwoCellBed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Sleeper(server, beds: 1);
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, "bed", yaw: 0); // head (2,0), foot (2,1)

            server.SetSpawnPoint(p.State.PlayerId, 2, 64, 1); // E on the FOOT half
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "the foot half is a bed for the home spawn too");

            p.State.CustomSpawnBodyId = string.Empty;
            server.SetSpawnPoint(p.State.PlayerId, 2, 64, 0); // … and on the head half
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId));
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
