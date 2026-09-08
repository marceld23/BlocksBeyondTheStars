// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
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
        }
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
