// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// #2436: the crate screen's server half — one kind of item moves between the backpack and a crate by hand, in either
/// direction, any category (Justus: "you can only put blocks into the crate, no items!"); the crate's filter, a wood
/// box's eight kinds and the loot reach still rule; the one-key sweep (H) keeps to loose materials.
/// </summary>
public sealed class CrateScreenTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrateScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cratescreen_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "crate"));
        var config = new ServerConfig { WorldName = "crate", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, string crateBlock = "crate")
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cell is empty
        p.State.Inventory.Add(crateBlock, 1, 99);
        server.PlaceBlock("Builder", 1, 200, 0, crateBlock);
        return p;
    }

    [Fact]
    public void AnyCategory_GoesInByHand_AndComesBackOut()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("berries", 7, 99);      // food (consumable) — on top of the starter kit's berries
            p.State.Inventory.Add("medpack", 2, 20);      // consumable
            p.State.Inventory.Add("machete", 1, 1);       // a tool
            int berries = p.State.Inventory.CountOf("berries");
            int medpacks = p.State.Inventory.CountOf("medpack");
            int machetes = p.State.Inventory.CountOf("machete");
            Assert.True(berries >= 7 && machetes >= 1);
            var crate = server.Containers.First(c => c.Kind == "crate");

            Assert.True(server.MoveContainerItem("Builder", crate.Id, "berries", toContainer: true, all: true));
            Assert.True(server.MoveContainerItem("Builder", crate.Id, "medpack", toContainer: true, all: true));
            Assert.True(server.MoveContainerItem("Builder", crate.Id, "machete", toContainer: true, all: false));
            Assert.Equal(0, p.State.Inventory.CountOf("berries"));
            Assert.Equal(0, p.State.Inventory.CountOf("medpack"));
            Assert.Equal(machetes - 1, p.State.Inventory.CountOf("machete"));
            var stored = server.Containers.First(c => c.Id == crate.Id).Items;
            Assert.Contains(stored, s => s.Item == "berries" && s.Count == berries);
            Assert.Contains(stored, s => s.Item == "medpack" && s.Count == medpacks);
            Assert.Contains(stored, s => s.Item == "machete" && s.Count == 1);

            Assert.True(server.MoveContainerItem("Builder", crate.Id, "machete", toContainer: false, all: false));
            Assert.Equal(machetes, p.State.Inventory.CountOf("machete"));
            Assert.DoesNotContain(server.Containers.First(c => c.Id == crate.Id).Items, s => s.Item == "machete");
        }
    }

    [Fact]
    public void OneStack_OrEveryStack_OfAKind()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("stone", 150, 99); // two stacks: 99 + 51
            var crate = server.Containers.First(c => c.Kind == "crate");

            Assert.True(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: true, all: false));
            int inCrate = server.Containers.First(c => c.Id == crate.Id).Items.First(s => s.Item == "stone").Count;
            Assert.InRange(inCrate, 1, 99);                        // one stack went in
            Assert.Equal(150 - inCrate, p.State.Inventory.CountOf("stone"));

            Assert.True(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: true, all: true));
            Assert.Equal(0, p.State.Inventory.CountOf("stone"));
            Assert.Equal(150, server.Containers.First(c => c.Id == crate.Id).Items.First(s => s.Item == "stone").Count);

            // Out: one stack = up to the item's real stack size (stone stacks to 1024, so that is all 150 at once).
            int max = _content.GetItem("stone")!.MaxStack;
            Assert.True(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: false, all: false));
            Assert.Equal(Math.Min(150, max), p.State.Inventory.CountOf("stone"));
            if (p.State.Inventory.CountOf("stone") < 150)
            {
                Assert.True(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: false, all: true));
            }

            Assert.Equal(150, p.State.Inventory.CountOf("stone"));
            Assert.DoesNotContain(server.Containers.First(c => c.Id == crate.Id).Items, s => s.Item == "stone");
            Assert.False(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: false, all: true)); // nothing left
        }
    }

    [Fact]
    public void TheFilter_StillRules_AndMayNameFoodOrTools()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("stone", 5, 99);
            p.State.Inventory.Add("berries", 5, 99);
            var crate = server.Containers.First(c => c.Kind == "crate");

            server.SetContainerFilterForTest(p, crate.Id, new[] { "berries" }); // a food crate (#1032 used to drop food keys)
            Assert.Contains("berries", server.Containers.First(c => c.Id == crate.Id).Filter);

            Assert.False(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: true, all: false));
            Assert.Equal(5, p.State.Inventory.CountOf("stone"));
            Assert.True(server.MoveContainerItem("Builder", crate.Id, "berries", toContainer: true, all: false));
            Assert.Equal(0, p.State.Inventory.CountOf("berries"));
        }
    }

    [Fact]
    public void AWoodBox_HoldsEightKinds_TheNinthIsRefused()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server, "wood_crate");
            string[] kinds = { "stone", "dirt", "sand", "grass", "wood_log", "iron_ore", "copper_ore", "carbon", "silicate" };
            foreach (var k in kinds)
            {
                p.State.Inventory.Add(k, 3, 99);
            }

            var box = server.Containers.First(c => c.Kind == "crate");
            for (int i = 0; i < 8; i++)
            {
                Assert.True(server.MoveContainerItem("Builder", box.Id, kinds[i], toContainer: true, all: false), kinds[i]);
            }

            Assert.False(server.MoveContainerItem("Builder", box.Id, kinds[8], toContainer: true, all: false));
            Assert.Equal(3, p.State.Inventory.CountOf(kinds[8]));
            Assert.Equal(8, server.Containers.First(c => c.Id == box.Id).Items.Count);

            // More of a kind the box already holds still fits.
            p.State.Inventory.Add("stone", 2, 99);
            Assert.True(server.MoveContainerItem("Builder", box.Id, "stone", toContainer: true, all: true));
            Assert.Equal(5, server.Containers.First(c => c.Id == box.Id).Items.First(s => s.Item == "stone").Count);
        }
    }

    [Fact]
    public void OutOfReach_MovesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("stone", 5, 99);
            var crate = server.Containers.First(c => c.Kind == "crate");
            p.State.Position = new Vector3f(40, 200, 40);

            Assert.False(server.MoveContainerItem("Builder", crate.Id, "stone", toContainer: true, all: false));
            Assert.Equal(5, p.State.Inventory.CountOf("stone"));
            Assert.Empty(server.Containers.First(c => c.Id == crate.Id).Items);
        }
    }

    [Fact]
    public void TheOneKeySweep_StillLeavesToolsAndFoodWithThePlayer()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("stone", 5, 99);
            p.State.Inventory.Add("berries", 5, 99);
            int berries = p.State.Inventory.CountOf("berries"); // the starter kit carries some too
            int machetes = p.State.Inventory.CountOf("machete");
            var crate = server.Containers.First(c => c.Kind == "crate");

            server.DepositToContainer("Builder", crate.Id); // H
            Assert.Equal(0, p.State.Inventory.CountOf("stone"));
            Assert.Equal(berries, p.State.Inventory.CountOf("berries"));
            Assert.Equal(machetes, p.State.Inventory.CountOf("machete"));
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
