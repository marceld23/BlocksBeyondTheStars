using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Beam blocks (teleporter pads). A beam block is a real voxel block (the mesher draws it) plus a tracked entity
/// carrying the player-typed name + owner: it shows on the map, only the owner can rename it, mining it returns the
/// item + forgets the marker, the block and its name survive a reload, and stepping onto one beams the player to any
/// of their own or an allied player's pads on the same world — for suit energy, on a cooldown, scope-gated.
/// </summary>
public sealed class BeamTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BeamTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_beam_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "beam"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "beam", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Player(SvGameServer server, string name, Vector3f at)
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = at; // up in the air → the target cell is empty + within reach
        p.State.Inventory.Add("beam_block", 1, 16);
        p.State.SuitEnergy = 100f;
        return p;
    }

    [Fact]
    public void PlaceBeam_IsASolidBlock_WithATrackedNameAndOwner()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0));

            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "Mine");

            Assert.Equal(1, server.BeamCount);
            Assert.False(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // a real solid block (unlike a door)
            var b = server.BeamSnapshots.Single();
            Assert.Equal("Mine", b.Name);
            Assert.Equal("Builder", b.OwnerId);
            Assert.Equal(0, p.State.Inventory.CountOf("beam_block")); // the block was consumed
            Assert.Single(repo.ListBeams(server.ActiveLocationId));    // persisted by its cell
        }
    }

    [Fact]
    public void MiningABeam_RemovesTheMarker_AndReturnsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0));
            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "Mine");
            Assert.Equal(1, server.BeamCount);

            server.MineBlock("Builder", 1, 200, 0);

            Assert.Equal(0, server.BeamCount);
            Assert.True(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("beam_block")); // dropped back to the miner
            Assert.Empty(repo.ListBeams(server.ActiveLocationId));     // and forgotten from the save
        }
    }

    [Fact]
    public void OnlyTheOwnerCanRename_AndTheNamePersists()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var owner = Player(server, "Builder", new Vector3f(0, 200, 0));
            var other = server.AddLocalPlayer("Other");
            other.State.Position = new Vector3f(0, 200, 0); // in reach, so only ownership gates the rename

            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "A");
            int id = server.BeamSnapshots.Single().Id;

            server.SetBeamNameForTest(other, id, "Hijacked"); // not the owner → refused
            Assert.Equal("A", server.BeamSnapshots.Single().Name);

            server.SetBeamNameForTest(owner, id, "Nordlager"); // the owner → applied
            Assert.Equal("Nordlager", server.BeamSnapshots.Single().Name);
            Assert.Equal("Nordlager", repo.ListBeams(server.ActiveLocationId).Single().Name); // persisted
        }
    }

    [Fact]
    public void Beam_BlockAndName_SurviveAReload()
    {
        string loc;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            Player(server, "Builder", new Vector3f(0, 200, 0));
            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "Home");
            loc = server.ActiveLocationId;
            Assert.Equal(1, server.BeamCount);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            Assert.Equal(loc, server2.ActiveLocationId);
            var b = server2.BeamSnapshots.Single();
            Assert.Equal("Home", b.Name);
            Assert.Equal("Builder", b.OwnerId);
            Assert.False(server2.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // the block edit came back too
        }
    }

    [Fact]
    public void Teleport_MovesThePlayerOntoTheTargetPad_AndSpendsEnergy()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0));
            p.State.Inventory.Add("beam_block", 1, 16); // a second pad to place

            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "Source");
            server.PlaceBlock("Builder", 5, 200, 0, "beam_block", "Dest");
            var snaps = server.BeamSnapshots.OrderBy(b => b.Cell.X).ToList();
            int srcId = snaps[0].Id, dstId = snaps[1].Id;

            float energyBefore = p.State.SuitEnergy;
            server.BeamTeleportForTest(p, srcId, dstId);

            Assert.Equal(5.5f, p.State.Position.X, 3); // standing on top of the destination pad cell
            Assert.Equal(201f, p.State.Position.Y, 3);
            Assert.Equal(0.5f, p.State.Position.Z, 3);
            Assert.True(p.State.SuitEnergy < energyBefore); // the jump cost suit energy
        }
    }

    [Fact]
    public void Teleport_IsRefused_WithoutEnoughEnergy()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0));
            p.State.Inventory.Add("beam_block", 1, 16);

            server.PlaceBlock("Builder", 1, 200, 0, "beam_block", "Source");
            server.PlaceBlock("Builder", 5, 200, 0, "beam_block", "Dest");
            var snaps = server.BeamSnapshots.OrderBy(b => b.Cell.X).ToList();

            p.State.SuitEnergy = 1f; // below the per-jump cost
            server.BeamTeleportForTest(p, snaps[0].Id, snaps[1].Id);

            Assert.Equal(0f, p.State.Position.X, 3); // unmoved
            Assert.Equal(200f, p.State.Position.Y, 3);
        }
    }

    [Fact]
    public void Teleport_ToAStrangersPad_IsRefused_UntilAllied()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Host"); // the first player becomes world admin (who bypasses scope); keep Alice/Bob regular
            var alice = Player(server, "Alice", new Vector3f(0, 200, 0));
            var bob = Player(server, "Bob", new Vector3f(4, 200, 0));
            Assert.False(alice.State.IsAdmin); // the scope gate only matters for non-admins

            server.PlaceBlock("Alice", 1, 200, 0, "beam_block", "Alice Pad");
            server.PlaceBlock("Bob", 5, 200, 0, "beam_block", "Bob Pad");
            var alicePad = server.BeamSnapshots.Single(b => b.OwnerId == "Alice");
            var bobPad = server.BeamSnapshots.Single(b => b.OwnerId == "Bob");

            // Not allied → Alice can't beam to Bob's pad.
            server.BeamTeleportForTest(alice, alicePad.Id, bobPad.Id);
            Assert.Equal(0f, alice.State.Position.X, 3); // unmoved

            // Form the alliance, then it works.
            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);
            Assert.True(server.AreAllied("Alice", "Bob"));

            server.BeamTeleportForTest(alice, alicePad.Id, bobPad.Id);
            Assert.Equal(5.5f, alice.State.Position.X, 3); // beamed onto Bob's pad
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
