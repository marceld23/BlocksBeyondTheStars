// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// End-to-end gameplay loops driven by the real NetworkClient against the real in-process server:
/// mining a block (block-change + inventory reflected on the client), a successful craft, and a
/// rejected craft. Asserts what the client receives, validating the client's decode/apply path.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class MineAndCraftTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static int CountIn(InventoryUpdate? inv, string item)
    {
        if (inv == null)
        {
            return 0;
        }

        int n = 0;
        foreach (var s in inv.Personal)
        {
            if (s.Item == item)
            {
                n += s.Count;
            }
        }

        return n;
    }

    [Fact]
    public void Mine_ReportsBlockChange_AndGrowsTheClientInventory()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join("Digger");
        Assert.True(h.PumpUntil(() => h.Chunks.Count > 0, maxTicks: 60), "Chunks should stream after join.");

        // Ground truth: find a minable, dropping block in the player's own column (mirrors the server suite).
        var session = h.Server.Sessions[1];
        int px = (int)System.Math.Floor(session.State.Position.X);
        int pz = (int)System.Math.Floor(session.State.Position.Z);
        int topY = (int)System.Math.Ceiling(session.State.Position.Y);

        Vector3i target = default;
        string? dropItem = null;
        for (int y = topY; y > topY - 12; y--)
        {
            var pos = new Vector3i(px, y, pz);
            var b = h.Server.World.GetBlock(pos);
            if (!b.IsAir && h.Server.World.Definition(b) is { } def && def.Drops.Count > 0)
            {
                target = pos;
                dropItem = def.Drops[0].Item;
                break;
            }
        }

        Assert.NotNull(dropItem);

        // Make sure the target's chunk reached the client, so its ClientWorld view can update.
        var tc = Shared.World.WorldConstants.WorldToChunk(target);
        Assert.True(h.PumpUntil(() => h.Chunks.ContainsKey((tc.X, tc.Y, tc.Z)), maxTicks: 60),
            "The target block's chunk should have streamed to the client.");

        // Drive mining through the client until the server reports the block gone (bare-hand hits take a few).
        for (int hit = 0; hit < 15 && !h.Server.World.GetBlock(target).IsAir; hit++)
        {
            h.Client.SendMine(target.X, target.Y, target.Z);
            h.Tick(0.1);
        }

        Assert.True(h.Server.World.GetBlock(target).IsAir, "Server should have removed the mined block.");

        // The client observed the authoritative removal and the drop landed in its inventory view.
        Assert.Contains(h.BlockChanges, c => c.X == target.X && c.Y == target.Y && c.Z == target.Z && c.Block == 0);
        Assert.True(h.World.GetBlock(target.X, target.Y, target.Z).IsAir, "Client world view should show the block as air.");
        Assert.True(CountIn(h.LastInventory, dropItem!) >= 1, $"Client inventory should contain the dropped '{dropItem}'.");
    }

    [Fact]
    public void Craft_ConsumesInputs_AndTheClientSeesTheOutput()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join("Smith");

        // Stock the (authoritative) inventory with the workshop recipe inputs: 2 ore -> 1 ingot.
        var player = h.Server.Sessions[1].State;
        player.Inventory.Add("iron_ore", 4, 99);

        h.Client.SendCraft("iron_ingot", 2);
        Assert.True(h.PumpUntil(() => h.LastCraftResult != null, maxTicks: 30), "Expected a CraftResult on the client.");

        Assert.True(h.LastCraftResult!.Success);
        Assert.True(h.PumpUntil(() => CountIn(h.LastInventory, "iron_ingot") == 2, maxTicks: 10),
            "Client inventory view should show the crafted ingots.");
        Assert.Equal(0, CountIn(h.LastInventory, "iron_ore"));
    }

    [Fact]
    public void Craft_Rejected_WhenBlueprintLocked_ReachesTheClient()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join("Eng");

        var player = h.Server.Sessions[1].State;
        // All materials for the titanium drill, but the blueprint is NOT unlocked.
        player.Inventory.Add("titanium_plate", 6, 99);
        player.Inventory.Add("cable", 4, 99);
        player.Inventory.Add("energy_cell_1", 2, 99);

        h.Client.SendCraft("titanium_drill");
        Assert.True(h.PumpUntil(() => h.LastCraftResult != null, maxTicks: 30), "Expected a CraftResult on the client.");

        Assert.False(h.LastCraftResult!.Success);
        Assert.Equal(0, CountIn(h.LastInventory, "titanium_drill"));
    }
}
