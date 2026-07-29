// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Throwing unwanted loot away (#599) — the only path that destroys an item instead of storing it — plus the
/// overflow bookkeeping behind the "backpack full" warning (#600).
/// </summary>
public sealed class DiscardItemTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DiscardItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_discard_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string tag, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---- the discard itself -----------------------------------------------------------------

    [Fact]
    public void Discard_RemovesEveryStackOfThatItem_AndNothingElse()
    {
        var server = Started("all", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            inv.SetSlot(10, new ItemStack("dirt", 99));
            inv.SetSlot(11, new ItemStack("dirt", 40));
            inv.SetSlot(12, new ItemStack("iron_ore", 7));

            server.DiscardItemForTest(p.State.PlayerId, 10);

            // One confirmed click clears the item out completely — emptying 300 dirt a stack at a time is busywork.
            Assert.Equal(0, inv.CountOf("dirt"));
            Assert.Null(inv.Slots[10]);
            Assert.Null(inv.Slots[11]);
            Assert.Equal(7, inv.CountOf("iron_ore")); // untouched
        }
    }

    [Fact]
    public void Discard_StarterKit_IsRefused()
    {
        var server = Started("starter", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;

            // A fresh pilot carries the kit in slots 0..4. None of it may be thrown away, or a player could
            // strand themselves with no drill and no way to craft a replacement.
            for (int slot = 0; slot < StarterKit.Items.Length; slot++)
            {
                string item = inv.Slots[slot]!.Item;
                server.DiscardItemForTest(p.State.PlayerId, slot);
                Assert.Equal(item, inv.Slots[slot]?.Item);
            }
        }
    }

    [Fact]
    public void Discard_StarterFood_IsAllowed()
    {
        var server = Started("berries", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            int slot = -1;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                if (inv.Slots[i]?.Item == "berries") { slot = i; break; }
            }

            Assert.True(slot >= 0, "a fresh pilot starts with berries");

            // Food is re-gatherable and a toxic batch is exactly what you want to bin — only gear is protected.
            server.DiscardItemForTest(p.State.PlayerId, slot);
            Assert.Equal(0, inv.CountOf("berries"));
        }
    }

    [Fact]
    public void Discard_EmptyOrOutOfRangeSlot_IsNoOp()
    {
        var server = Started("noop", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            inv.SetSlot(15, null);
            inv.SetSlot(16, new ItemStack("stone", 5));

            server.DiscardItemForTest(p.State.PlayerId, 15);   // empty
            server.DiscardItemForTest(p.State.PlayerId, -1);   // below range
            server.DiscardItemForTest(p.State.PlayerId, 9999); // above range

            Assert.Equal(5, inv.CountOf("stone"));
        }
    }

    [Fact]
    public void Discard_FromCargo_NeedsToBeAboard()
    {
        var server = Started("hold", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            server.Ship.Cargo.SetSlot(0, new ItemStack("sand", 64));

            p.State.AboardShip = false;
            server.DiscardItemForTest(p.State.PlayerId, 0, fromCargo: true);
            Assert.Equal(64, server.Ship.Cargo.CountOf("sand")); // refused: the hold is out of reach

            p.State.AboardShip = true;
            server.DiscardItemForTest(p.State.PlayerId, 0, fromCargo: true);
            Assert.Equal(0, server.Ship.Cargo.CountOf("sand")); // the hold is where "stow all" piles junk up
        }
    }

    // ---- the starter-kit list itself --------------------------------------------------------

    [Fact]
    public void StarterKit_MatchesWhatAFreshPilotIsHanded()
    {
        var server = Started("pinned", out var repo);
        using (repo)
        {
            var inv = server.AddLocalPlayer("Pilot").State.Inventory;

            // Pins the protection list to reality: if CreatePlayer ever hands out different gear, this fails
            // rather than silently leaving the new item discardable (or protecting one nobody starts with).
            for (int i = 0; i < StarterKit.Items.Length; i++)
            {
                Assert.Equal(StarterKit.Items[i], inv.Slots[i]?.Item);
                Assert.True(StarterKit.IsProtected(StarterKit.Items[i]));
            }

            Assert.False(StarterKit.IsProtected("berries"));
            Assert.False(StarterKit.IsProtected("iron_ore"));
        }
    }

    [Fact]
    public void StarterKit_IsProtected_IgnoresColourAndShapeModifiers()
    {
        // A composite key ("stone#t3f6fb0") must resolve to its base item, so a dyed variant of protected gear
        // — should any ever exist — cannot slip past the guard.
        Assert.True(StarterKit.IsProtected(ItemKey.Compose("suit_lamp", 0x3f6fb0, 0, 0)));
        Assert.False(StarterKit.IsProtected(ItemKey.Compose("stone", 0x3f6fb0, 0, 0)));
    }

    // ---- #600: the overflow the drops used to vanish into -------------------------------------

    [Fact]
    public void MaterialPool_Overflow_CountsWhatFoundNoRoom()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Pilot", AboardShip = false };
        var ship = new ShipState();
        for (int i = 0; i < player.Inventory.SlotCount; i++)
        {
            player.Inventory.SetSlot(i, new ItemStack("stone", _content.MaxStackOf("stone")));
        }

        var pool = new MaterialPool(_content, player, ship);
        Assert.Equal(0, pool.Overflow);

        int lost = pool.Add("iron_ore", 5); // every slot is full of something else
        Assert.Equal(5, lost);
        Assert.Equal(5, pool.Overflow);     // ...and the pool remembers, so the caller can warn instead of

        pool.Add("iron_ore", 3);            // ...letting it disappear unannounced
        Assert.Equal(8, pool.Overflow);     // accumulates across a whole area-mining burst
    }

    [Fact]
    public void MaterialPool_Overflow_StaysZeroWhenEverythingFits()
    {
        var player = new PlayerState { PlayerId = "p2", Name = "Pilot", AboardShip = false };
        var pool = new MaterialPool(_content, player, new ShipState());

        Assert.Equal(0, pool.Add("iron_ore", 5));
        Assert.Equal(0, pool.Overflow);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort — a locked SQLite file must not fail the test run
        }
    }
}
