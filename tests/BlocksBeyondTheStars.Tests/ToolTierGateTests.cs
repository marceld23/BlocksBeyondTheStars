// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1686: a tool-tier gate used to turn a swing away with "your current tool cannot mine this block" and
/// never say what would work — a Machine Housing lying in the open world simply refused a Basic Drill.
/// These pin the rule itself (one shared implementation, not two hand-copied twins), the tool the message
/// names, and the locale keys the client needs to render it.
/// </summary>
public sealed class ToolTierGateTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ToolTierGateTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_tiergate_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, IServerTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tiergate"));
        var config = new ServerConfig { WorldName = "tiergate", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    // --- The shared rule -------------------------------------------------------------------------

    [Fact]
    public void Rule_GatesOnKindFirst_ThenTier()
    {
        var machineHousing = _content.GetBlock("machine_block")!;
        var basic = _content.GetItem("basic_drill")!.Tool!;
        var titanium = _content.GetItem("titanium_drill")!.Tool!;
        var machete = _content.GetItem("machete")!.Tool!;

        Assert.Equal(ToolKind.Drill, machineHousing.RequiredTool);
        Assert.Equal(2, machineHousing.MinToolTier);
        Assert.False(MiningRules.ToolCanMine(basic, machineHousing));    // right kind, tier too low
        Assert.False(MiningRules.ToolCanMine(machete, machineHousing));  // wrong kind entirely
        Assert.True(MiningRules.ToolCanMine(titanium, machineHousing));
    }

    [Fact]
    public void Rule_OnlyTheRightKindTooWeak_IsATierGate()
    {
        // #2525: "not strong enough" (and VEGA's tier-gate tip) only for the right kind of tool that is too weak.
        var machineHousing = _content.GetBlock("machine_block")!;
        var stone = _content.GetBlock("stone")!;
        var basic = _content.GetItem("basic_drill")!.Tool!;
        var machete = _content.GetItem("machete")!.Tool!;
        var hands = new ToolProperties { Kind = ToolKind.None, Tier = 0 };

        Assert.True(MiningRules.IsTierGate(basic, machineHousing));   // a drill, one tier short
        Assert.False(MiningRules.IsTierGate(machete, machineHousing)); // the wrong kind entirely
        Assert.False(MiningRules.IsTierGate(hands, machineHousing));   // nothing (or a block) in hand
        Assert.False(MiningRules.IsTierGate(hands, stone));
        Assert.False(MiningRules.IsTierGate(basic, stone));            // no refusal at all
    }

    [Fact]
    public void Rule_UngatedBlock_TakesBareHands()
    {
        var dirt = _content.GetBlock("dirt")!;
        var hands = new ToolProperties { Kind = ToolKind.None, Tier = 0 };
        Assert.True(MiningRules.ToolCanMine(hands, dirt));
    }

    [Fact]
    public void CheapestTool_NamesTheTitaniumDrill_NotAMaxTierLaser()
    {
        // The whole point of the hint is that it is actionable: the cheapest rung that clears the gate,
        // never whichever tier-3 tool happens to sort first in the item table.
        var wanted = MiningRules.CheapestToolFor(_content, _content.GetBlock("machine_block")!);
        Assert.NotNull(wanted);
        Assert.Equal("titanium_drill", wanted!.Key);
    }

    [Fact]
    public void CheapestTool_ClearsEveryGatedBlockInTheShippedData()
    {
        // Every gated block must have SOMETHING that opens it — a gate with no key is a dead end, and the
        // hint would have nothing to name. Fails loudly if a future data edit adds one.
        foreach (var block in _content.Blocks.Values.Where(b => b.Mineable && b.MinToolTier > 0))
        {
            var wanted = MiningRules.CheapestToolFor(_content, block);
            Assert.True(wanted is not null, "No tool in the content set can mine " + block.Key + ".");
            Assert.True(MiningRules.ToolCanMine(wanted!.Tool!, block),
                "CheapestToolFor picked " + wanted.Key + " for " + block.Key + ", which cannot actually mine it.");
        }
    }

    // --- The message the player reads ------------------------------------------------------------

    [Fact]
    public void MiningAboveTier_RejectsWithTheToolNamed()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner"); // starter loadout: basic_drill in slot 0
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("machine_block")!.NumericId);

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.False(server.World.GetBlock(pos).IsAir, "A tier-1 drill must not break a tier-2 block.");
            string? reason = transport.Sent.Select(s => s.Msg).OfType<ActionRejected>()
                .Where(m => m.Action == "mine").Select(m => m.Reason).LastOrDefault();
            Assert.NotNull(reason);
            Assert.StartsWith("@srv.mine.wrong_tool_named:", reason);
            // The tail is the tool's name in the session's language, ready for the client's {name} slot.
            Assert.Equal("Titanium Drill", reason!.Substring("@srv.mine.wrong_tool_named:".Length));

            // A real tier gate arms VEGA's tip and the tutorial's tier-gate moment (#2525 keeps exactly this case).
            Assert.Equal("Titanium Drill", p.VegaTierGateTool);
            Assert.False(double.IsNegativeInfinity(p.TierGateRefusedAt), "A real tier gate must arm world:tier_gate.");
        }
    }

    // --- #2525: the wrong kind of tool, or none, is not "not strong enough" -----------------------

    /// <summary>Puts plain stone under a fresh starter player (Basic Drill in quick-bar slot 0), lets
    /// <paramref name="arrange"/> change the hand or the inventory, swings once, and returns the mine rejection.</summary>
    private string? SwingAtStone(Action<PlayerSession> arrange, out PlayerSession player)
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            player = server.AddLocalPlayer("Miner");
            player.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            Assert.Equal("basic_drill", player.State.Inventory.Slots[0]?.Item);
            arrange(player);

            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);
            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.False(server.World.GetBlock(pos).IsAir, "Stone must not break without a drill in hand.");
            return transport.Sent.Select(s => s.Msg).OfType<ActionRejected>()
                .Where(m => m.Action == "mine").Select(m => m.Reason).LastOrDefault();
        }
    }

    [Fact]
    public void WrongKind_WithTheDrillInTheQuickBar_SaysSelectIt_AndArmsNoTierGate()
    {
        // The reported case: a block in hand (the server's saved slot), the drill in quick-bar slot 0.
        string? reason = SwingAtStone(p =>
        {
            p.State.Inventory.SetSlot(4, new ItemStack("stone", 5));
            p.State.SelectedHotbarSlot = 4;
        }, out var player);

        Assert.Equal("@srv.mine.need_tool_in_hand_quickbar:Basic Drill", reason);
        Assert.Equal(string.Empty, player.VegaTierGateBlock);
        Assert.Equal(string.Empty, player.VegaTierGateTool);
        Assert.True(double.IsNegativeInfinity(player.TierGateRefusedAt), "A kind mismatch must not arm world:tier_gate.");
    }

    [Fact]
    public void EmptyHand_WithTheDrillInTheBackpack_SaysPutItInTheQuickBar()
    {
        string? reason = SwingAtStone(p =>
        {
            p.State.Inventory.SetSlot(20, p.State.Inventory.Slots[0]);
            p.State.Inventory.SetSlot(0, null);
        }, out var player);

        Assert.Equal("@srv.mine.need_tool_in_hand_backpack:Basic Drill", reason);
        Assert.True(double.IsNegativeInfinity(player.TierGateRefusedAt));
    }

    [Fact]
    public void WrongKind_WithoutASuitableTool_NamesTheCheapestOne()
    {
        // The machete is a tool, but the wrong kind for stone; no drill anywhere in the inventory.
        string? reason = SwingAtStone(p =>
        {
            p.State.Inventory.SetSlot(0, null);
            int machete = Enumerable.Range(0, p.State.Inventory.SlotCount).First(i => p.State.Inventory.Slots[i]?.Item == "machete");
            p.State.SelectedHotbarSlot = machete;
        }, out var player);

        // #2536: the cheapest tool for stone is the wooden pickaxe now — and it is the one the player can make by hand,
        // anywhere (the basic drill has no recipe).
        Assert.Equal("@srv.mine.need_tool_in_hand:Wooden Pickaxe", reason);
        Assert.True(double.IsNegativeInfinity(player.TierGateRefusedAt));
    }

    [Fact]
    public void MiningWithinTier_IsNotRejected()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("mud")!.NumericId);

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.DoesNotContain(transport.Sent.Select(s => s.Msg).OfType<ActionRejected>(), m => m.Action == "mine");
        }
    }

    // --- The locale keys the client renders ------------------------------------------------------

    [Fact]
    public void LocaleKeys_ExistAndCarryTheirPlaceholders()
    {
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = _content.CreateLocalizer(locale);
            string named = loc.Get("srv.mine.wrong_tool_named");
            Assert.DoesNotContain("srv.mine.wrong_tool_named", named); // resolved, not echoed back
            Assert.Contains("{name}", named);

            // #2525: the wrong kind or an empty hand, plain and with where the carried tool is.
            foreach (string key in new[] { "srv.mine.need_tool_in_hand", "srv.mine.need_tool_in_hand_quickbar", "srv.mine.need_tool_in_hand_backpack" })
            {
                string text = loc.Get(key);
                Assert.DoesNotContain(key, text);
                Assert.Contains("{name}", text);
            }

            string tip = loc.Get("vega.hint.tier_gate");
            Assert.DoesNotContain("vega.hint.tier_gate", tip);
            Assert.Contains("{0}", tip); // the block
            Assert.Contains("{1}", tip); // the tool

            Assert.DoesNotContain("ui.scan.tool", loc.Get("ui.scan.tool"));
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
