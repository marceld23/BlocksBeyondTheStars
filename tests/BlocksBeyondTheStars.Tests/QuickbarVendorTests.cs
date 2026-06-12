using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// B55 — per-vendor trade themes (multiple vendors at one place sell different goods) — and B58 — a customisable
/// quick-bar (rearranging the slot-based inventory: slots 0..8 are the quick-bar).
/// </summary>
public sealed class QuickbarVendorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public QuickbarVendorTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_qbv_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string tag, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---- B55: per-vendor themes -------------------------------------------------------------

    private static readonly string[] ValidThemes = { "miners", "traders", "researchers", "settlers" };

    [Fact]
    public void VendorTheme_FirstVendorKeepsBaseTheme()
    {
        Assert.Equal("traders", SvGameServer.VendorThemeForTest("StationAlpha", 0, "traders"));
        Assert.Equal("miners", SvGameServer.VendorThemeForTest("Karth Village", 0, "miners"));
    }

    [Fact]
    public void VendorTheme_AdditionalVendorsVary_AndAreValid()
    {
        var themes = new HashSet<string>();
        for (int i = 0; i <= 8; i++)
        {
            string t = SvGameServer.VendorThemeForTest("StationAlpha", i, "traders");
            Assert.Contains(t, ValidThemes);
            themes.Add(t);
        }

        Assert.True(themes.Count >= 2, "vendors beyond the first should not all share one theme");
    }

    [Fact]
    public void VendorTheme_IsDeterministic()
    {
        Assert.Equal(
            SvGameServer.VendorThemeForTest("StationAlpha", 3, "traders"),
            SvGameServer.VendorThemeForTest("StationAlpha", 3, "traders"));
    }

    // ---- B58: Inventory model ---------------------------------------------------------------

    [Fact]
    public void Inventory_Swap_ExchangesTwoSlots()
    {
        var inv = new Inventory(24);
        inv.SetSlot(2, new ItemStack("iron_plate", 5));
        inv.Swap(2, 7);
        Assert.Null(inv.Slots[2]);
        Assert.Equal("iron_plate", inv.Slots[7]!.Item);

        // Swapping two filled slots exchanges them; out-of-range / equal indices are no-ops.
        inv.SetSlot(2, new ItemStack("cable", 3));
        inv.Swap(2, 7);
        Assert.Equal("iron_plate", inv.Slots[2]!.Item);
        Assert.Equal("cable", inv.Slots[7]!.Item);
        inv.Swap(2, 2);
        inv.Swap(2, 999);
        Assert.Equal("iron_plate", inv.Slots[2]!.Item); // unchanged
    }

    [Fact]
    public void Inventory_FirstEmptySlot_RespectsStartIndex()
    {
        var inv = new Inventory(24);
        inv.SetSlot(9, new ItemStack("stone", 1));
        Assert.Equal(0, inv.FirstEmptySlot());        // from the start
        Assert.Equal(10, inv.FirstEmptySlot(9));      // first empty at/after the backpack start (9 is taken)
    }

    // ---- B58: server quick-bar move ---------------------------------------------------------

    [Fact]
    public void MoveItem_AssignsBackpackStackToQuickSlot()
    {
        var server = NewServer("assign", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            inv.SetSlot(12, new ItemStack("laser_cutter", 1));
            inv.SetSlot(3, null);

            server.MoveItemForTest(p.State.PlayerId, 12, 3);

            Assert.Equal("laser_cutter", inv.Slots[3]!.Item); // now on the quick-bar
            Assert.Null(inv.Slots[12]);                        // moved out of the backpack
        }
    }

    [Fact]
    public void MoveItem_StowOutOfQuickbar_GoesToBackpack()
    {
        var server = NewServer("stow", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            inv.SetSlot(2, new ItemStack("medkit", 2)); // a quick-bar slot
            for (int i = 9; i < inv.SlotCount; i++) inv.SetSlot(i, null); // ensure the backpack is empty

            server.MoveItemForTest(p.State.PlayerId, 2, -1); // stow

            Assert.Null(inv.Slots[2]);                       // off the quick-bar
            Assert.Equal("medkit", inv.Slots[9]!.Item);      // first free backpack slot
        }
    }

    [Fact]
    public void MoveItem_FromEmptySlot_IsNoOp()
    {
        var server = NewServer("noop", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var inv = p.State.Inventory;
            inv.SetSlot(5, null);
            inv.SetSlot(1, new ItemStack("iron_plate", 1));

            server.MoveItemForTest(p.State.PlayerId, 5, 1); // source empty → nothing happens

            Assert.Equal("iron_plate", inv.Slots[1]!.Item);
            Assert.Null(inv.Slots[5]);
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
