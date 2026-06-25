// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// Item 37 — placeable radio beacons. A beacon is a real voxel block (so the mesher draws it) plus a tracked
/// entity that carries the player-typed label + owner: it shows on the map/compass, only the owner can rename it,
/// mining it returns the item + forgets the marker, and both the block and its label survive a reload.
/// </summary>
public sealed class BeaconTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BeaconTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_beacon_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "beacon"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "beacon", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, string name = "Builder")
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cell is empty + within reach
        p.State.Inventory.Add("radio_beacon", 1, 99);
        return p;
    }

    [Fact]
    public void PlaceBeacon_IsASolidBlock_WithATrackedLabelAndOwner()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server);

            server.PlaceBlock("Builder", 1, 200, 0, "radio_beacon", "Iron Lake");

            Assert.Equal(1, server.BeaconCount);
            Assert.False(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // a real solid block (unlike a door)
            var b = server.BeaconSnapshots.Single();
            Assert.Equal("Iron Lake", b.Label);
            Assert.Equal("Builder", b.OwnerId);
            Assert.Equal(0, p.State.Inventory.CountOf("radio_beacon"));         // the block was consumed
            Assert.Single(repo.ListBeacons(server.ActiveLocationId));           // persisted by its cell
        }
    }

    [Fact]
    public void MiningABeacon_RemovesTheMarker_AndReturnsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "radio_beacon", "Base 1");
            Assert.Equal(1, server.BeaconCount);

            server.MineBlock("Builder", 1, 200, 0); // mine it fully (loops until the cell is air)

            Assert.Equal(0, server.BeaconCount);
            Assert.True(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("radio_beacon")); // dropped back to the miner
            Assert.Empty(repo.ListBeacons(server.ActiveLocationId));    // and forgotten from the save
        }
    }

    [Fact]
    public void OnlyTheOwnerCanRename_AndTheLabelPersists()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var owner = Builder(server);
            var other = server.AddLocalPlayer("Other");
            other.State.Position = new Vector3f(0, 200, 0); // in reach, so only ownership gates the rename

            server.PlaceBlock("Builder", 1, 200, 0, "radio_beacon", "A");
            int id = server.BeaconSnapshots.Single().Id;

            server.SetBeaconLabelForTest(other, id, "Hijacked"); // not the owner → refused
            Assert.Equal("A", server.BeaconSnapshots.Single().Label);

            server.SetBeaconLabelForTest(owner, id, "Eisensee"); // the owner → applied
            Assert.Equal("Eisensee", server.BeaconSnapshots.Single().Label);
            Assert.Equal("Eisensee", repo.ListBeacons(server.ActiveLocationId).Single().Label); // persisted
        }
    }

    [Fact]
    public void Beacon_BlockAndLabel_SurviveAReload()
    {
        string loc;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "radio_beacon", "Iron Lake");
            loc = server.ActiveLocationId;
            Assert.Equal(1, server.BeaconCount);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            Assert.Equal(loc, server2.ActiveLocationId);
            var b = server2.BeaconSnapshots.Single();
            Assert.Equal("Iron Lake", b.Label);
            Assert.Equal("Builder", b.OwnerId);
            Assert.False(server2.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // the block edit came back too
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
