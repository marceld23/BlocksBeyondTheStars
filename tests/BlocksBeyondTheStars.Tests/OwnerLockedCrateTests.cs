// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Owner-locked crates (#3294): a container with an owner that still holds items — the finale's reward crate, a knocked-out
/// robot's parts. Only the owner takes from it; nobody puts anything in; nobody but an admin mines it and the terrain blaster
/// leaves it standing; emptied, it is a normal crate again. The owner rides in two new columns (SQLite and PostgreSQL) and
/// in the in-memory store's serialised object.
/// </summary>
public sealed class OwnerLockedCrateTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_ownercrate_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<IDisposable> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private static StoredContainer LockedCrate(string id, Vector3i at, string owner = "Ada") => new()
    {
        Id = id,
        Planet = "rocky",
        Kind = "crate",
        Position = at,
        Items = new List<ItemStack> { new("robot_core", 1) },
        Owner = owner,
        OwnerName = owner,
    };

    // ---------------- persistence ----------------

    [Fact]
    public void TheOwner_RoundTrips_InSqlite()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sqlite_rt"));
        _repos.Add(repo);
        repo.Initialize();
        repo.SaveContainer(LockedCrate("c1", new Vector3i(1, 2, 3)));
        repo.SaveContainer(new StoredContainer { Id = "c2", Planet = "rocky", Kind = "crate", Position = new Vector3i(4, 5, 6) });

        var listed = repo.ListContainers("rocky");

        var locked = listed.Single(c => c.Id == "c1");
        Assert.Equal("Ada", locked.Owner);
        Assert.Equal("Ada", locked.OwnerName);
        Assert.Equal(string.Empty, listed.Single(c => c.Id == "c2").Owner);
    }

    [Fact]
    public void TheOwner_RoundTrips_InTheMemoryStore_AcrossASnapshot()
    {
        var paths = new SaveGamePaths(_root, "memory_rt");
        byte[] blob;
        using (var repo = new MemoryWorldRepository(paths))
        {
            repo.Initialize();
            repo.SaveContainer(LockedCrate("c1", new Vector3i(1, 2, 3)));
            Assert.Equal("Ada", repo.ListContainers("rocky").Single().Owner);
            blob = repo.ExportSnapshotBlob(); // the browser build's save payload
        }

        using var reopened = new MemoryWorldRepository(paths);
        reopened.Initialize();
        reopened.ImportSnapshotBlob(blob);
        var crate = Assert.Single(reopened.ListContainers("rocky"));
        Assert.Equal("Ada", crate.Owner);
        Assert.Equal("Ada", crate.OwnerName);
    }

    [Fact]
    public void AnOldSqliteSave_WithoutTheOwnerColumns_GetsThem_AndKeepsItsCrates()
    {
        var paths = new SaveGamePaths(_root, "sqlite_old");
        using (var repo = new SqliteWorldRepository(paths))
        {
            repo.Initialize();
            repo.SaveContainer(new StoredContainer { Id = "old", Planet = "rocky", Kind = "crate", Position = new Vector3i(1, 1, 1) });
        }

        // Turn the table back into the shape a save had before #3294.
        using (var connection = new SqliteConnection($"Data Source={paths.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE container DROP COLUMN owner; ALTER TABLE container DROP COLUMN owner_name;";
            cmd.ExecuteNonQuery();
        }

        var reopened = new SqliteWorldRepository(paths);
        _repos.Add(reopened);
        reopened.Initialize();
        Assert.Equal(string.Empty, Assert.Single(reopened.ListContainers("rocky")).Owner); // the old crate: anybody's

        reopened.SaveContainer(LockedCrate("new", new Vector3i(2, 2, 2)));
        Assert.Equal("Ada", reopened.ListContainers("rocky").Single(c => c.Id == "new").Owner);
    }

    // ---------------- the lock rules on a running server ----------------

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner, PlayerSession Other) Started(string name)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var owner = server.AddLocalPlayer("Ada");
        var other = server.AddLocalPlayer("Ben");
        foreach (var p in new[] { owner, other })
        {
            p.State.AboardShip = false;
            p.State.Role = PlayerRole.Player; // the world's creator is its admin — these two are ordinary players
            p.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
            for (int i = 0; i < p.State.Inventory.SlotCount; i++)
            {
                p.State.Inventory.SetSlot(i, null);
            }
        }

        return (server, transport, owner, other);
    }

    /// <summary>A crate block two cells in front of the players, registered as Ada's locked crate.</summary>
    private Vector3i PlaceLockedCrate(SvGameServer server, string id = "reward")
    {
        var cell = new Vector3i(Centre.X, Centre.Y, Centre.Z + 2);
        server.World.SetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z), _content.GetBlock("stone")!.NumericId);
        server.World.SetBlock(cell, _content.GetBlock("crate")!.NumericId);
        server.AddContainerForTest(LockedCrate(id, cell));
        return cell;
    }

    private static List<string> Rejections(RecordingTransport t, PlayerSession who)
        => t.To<ActionRejected>(who).Select(r => r.Reason).ToList();

    [Fact]
    public void OnlyTheOwner_Takes_AndEmptiedTheCrateIsAnybodysAgain()
    {
        var (server, transport, owner, other) = Started("lock_take");
        PlaceLockedCrate(server);

        server.LootContainer("Ben", "reward");
        Assert.Equal(0, other.State.Inventory.CountOf("robot_core"));
        Assert.Contains("@srv.loot.owner_only:Ada", Rejections(transport, other));
        Assert.False(server.MoveContainerItem("Ben", "reward", "robot_core", toContainer: false, all: true));

        server.LootContainer("Ada", "reward");
        Assert.Equal(1, owner.State.Inventory.CountOf("robot_core"));
        var crate = server.Containers.Single(c => c.Id == "reward");
        Assert.Equal(string.Empty, crate.Owner); // emptied: a normal crate the owner keeps where it stands

        other.State.Inventory.Add("stone", 5, 1024);
        Assert.True(server.MoveContainerItem("Ben", "reward", "stone", toContainer: true, all: true)); // anybody's again
    }

    [Fact]
    public void Nobody_PutsAnythingIn_NorSortsIt_WhileItIsLocked()
    {
        var (server, transport, owner, other) = Started("lock_put");
        PlaceLockedCrate(server);
        owner.State.Inventory.Add("stone", 5, 1024);
        other.State.Inventory.Add("stone", 5, 1024);

        server.DepositToContainer("Ben", "reward");
        server.DepositToContainer("Ada", "reward");
        Assert.False(server.MoveContainerItem("Ada", "reward", "stone", toContainer: true, all: true));
        server.SetContainerFilterForTest(other, "reward", new[] { "stone" });

        var crate = server.Containers.Single(c => c.Id == "reward");
        Assert.Equal(new[] { "robot_core" }, crate.Items.Select(s => s.Item));
        Assert.Empty(crate.Filter);
        Assert.Contains("@srv.loot.owner_locked:Ada", Rejections(transport, owner));
        Assert.Contains("@srv.loot.owner_locked:Ada", Rejections(transport, other));
        Assert.Contains("@srv.loot.owner_only:Ada", Rejections(transport, other));
    }

    [Fact]
    public void Nobody_MinesIt_ButAnAdmin_AndTheBlasterLeavesItStanding()
    {
        var (server, transport, owner, other) = Started("lock_mine");
        var cell = PlaceLockedCrate(server);
        var crateId = _content.GetBlock("crate")!.NumericId;

        server.MineBlock("Ben", cell.X, cell.Y, cell.Z);
        server.MineBlock("Ada", cell.X, cell.Y, cell.Z);
        Assert.Equal(crateId, server.World.GetBlock(cell));
        Assert.Contains("@srv.loot.owner_locked_mine:Ada", Rejections(transport, other));
        Assert.Contains("@srv.loot.owner_locked_mine:Ada", Rejections(transport, owner));

        other.State.Inventory.Add("terrain_blaster", 1, 1);
        other.State.SuitEnergy = 100f;
        server.UseGadgetForTest("Ben", "terrain_blaster", new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
        Assert.Equal(crateId, server.World.GetBlock(cell));
        Assert.Single(server.Containers, c => c.Id == "reward" && c.Items.Count == 1);

        other.State.Role = PlayerRole.Admin;
        server.MineBlock("Ben", cell.X, cell.Y, cell.Z);
        Assert.True(server.World.GetBlock(cell).IsAir); // an admin may (it goes to the cheat log)
    }

    [Fact]
    public void TheContainerList_NamesTheOwner_OnlyWhileLocked()
    {
        var (server, transport, owner, _) = Started("lock_wire");
        PlaceLockedCrate(server);
        var locked = transport.Messages().OfType<ContainerList>().Last().Containers.Single(c => c.Id == "reward");
        Assert.Equal("Ada", locked.OwnerId);
        Assert.Equal("Ada", locked.OwnerName);

        transport.Sent.Clear();
        server.LootContainer("Ada", "reward"); // empties it — the next list carries no owner
        Assert.Equal(1, owner.State.Inventory.CountOf("robot_core"));
        var after = transport.Messages().OfType<ContainerList>().Last().Containers.Single(c => c.Id == "reward");
        Assert.Equal(string.Empty, after.OwnerId);
        Assert.Equal(string.Empty, after.OwnerName);
    }
}
