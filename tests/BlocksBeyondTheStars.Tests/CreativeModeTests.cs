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

/// <summary>Singleplayer "Creative" world options: unlock-all blueprints, own all ships, a curated starter kit —
/// persisted per world (reapplied on every load) while survival mechanics stay on. All-off = "Explorer".</summary>
public sealed class CreativeModeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreativeModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_creative_" + System.Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string name, out SqliteWorldRepository repo, bool creative, bool placeShip = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = placeShip,
            CreativeUnlockAllBlueprints = creative,
            CreativeStartAllShips = creative,
            CreativeStarterKit = creative,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void CreativeWorld_UnlocksAllBlueprints_OwnsAllShips_AndGrantsKit()
    {
        var server = Start("creative", out var repo, creative: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");

            // Every blueprint is unlocked.
            Assert.Equal(_content.Blueprints.Count, p.State.UnlockedBlueprints.Count);
            Assert.True(_content.Blueprints.Count > 0);

            // Every ship type is owned (starter + the three unlockables).
            var types = server.OwnedShips.Values.Select(s => s.ShipType).ToHashSet();
            Assert.Contains("starter", types);
            Assert.Contains("hauler", types);
            Assert.Contains("scout", types);
            Assert.Contains("corvette", types);

            // The curated kit was granted: material stacks land in the ship's cargo hold, NOT the backpack —
            // a backpack stuffed full of kit stacks refused every on-foot mine ("inventory full", #677).
            var starter = server.OwnedShips.Values.First(s => s.ShipType == "starter");
            Assert.True(starter.Cargo.CountOf("iron_ore") > 0, "the creative kit materials should reach the cargo hold");
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));

            // The kit's tools DO go to the backpack, and the backpack keeps room for mining drops.
            Assert.Equal(1, p.State.Inventory.CountOf("titanium_drill"));
            int freeSlots = p.State.Inventory.Slots.Count(s => s is null || s.IsEmpty);
            Assert.True(freeSlots >= 5, $"the backpack must keep free slots for mining drops, had {freeSlots}");
        }
    }

    [Fact]
    public void CreativeKit_LeavesRoomToMine_FreshPlayerMinesTerrainOnFoot()
    {
        // Regression for #677: the forced Sandbox kit used to fill all 24 backpack slots, and a full
        // backpack refuses every break since #600 — a fresh Sandbox player could not mine anything.
        var server = Start("sandboxmine", out var repo, creative: true, placeShip: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");
            p.State.AboardShip = false; // on foot: drops must fit the backpack alone (cargo doesn't count)
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f); // basic_drill is starter slot 0
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("mud")!.NumericId); // soft: one basic-drill hit

            server.MineBlockOnce("Host", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "a fresh creative/sandbox player must be able to mine terrain (#677)");
            Assert.True(p.State.Inventory.CountOf("mud") > 0, "the mined drop should land in the backpack");
        }
    }

    [Fact]
    public void ExplorerWorld_GrantsNothingExtra()
    {
        var server = Start("explorer", out var repo, creative: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");

            Assert.Empty(p.State.UnlockedBlueprints);                                  // nothing unlocked
            var types = server.OwnedShips.Values.Select(s => s.ShipType).ToHashSet();
            Assert.DoesNotContain("hauler", types);                                    // only the starter
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));                    // no creative kit
        }
    }

    [Fact]
    public void CreativeOptions_PersistAcrossRestart_AndKitIsGrantedOnce()
    {
        // Create the world Creative, note the kit amount, then reopen the SAME save with config flags OFF —
        // the persisted world options still apply (unlock-all + all-ships), and the one-time kit isn't refilled.
        int ironAfterFirst;
        {
            var s1 = Start("persist", out var repo1, creative: true);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Host");
                ironAfterFirst = s1.OwnedShips.Values.First(s => s.ShipType == "starter").Cargo.CountOf("iron_ore");
                Assert.True(ironAfterFirst > 0);
                repo1.Flush();
            }
        }

        // Fresh server, same save dir, but config says NOT creative — the world's saved options win.
        var s2 = Start("persist", out var repo2, creative: false);
        using (repo2)
        {
            var p = s2.AddLocalPlayer("Host");
            Assert.Equal(_content.Blueprints.Count, p.State.UnlockedBlueprints.Count); // still all unlocked
            Assert.Contains("corvette", s2.OwnedShips.Values.Select(s => s.ShipType)); // still owns all ships
            int ironAfterReload = s2.OwnedShips.Values.First(s => s.ShipType == "starter").Cargo.CountOf("iron_ore");
            Assert.Equal(ironAfterFirst, ironAfterReload);                             // kit NOT granted again
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
