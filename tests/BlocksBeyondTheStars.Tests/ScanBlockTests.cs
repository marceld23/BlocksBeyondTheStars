// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The once-per-cell scan event (#3007): <c>scan:block</c> carries the block key and fires once per cell per player
/// (persisted), never without a cell (an older client), and the hand scanner is throttled to two scans a second.</summary>
public sealed class ScanBlockTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg, int Amount)> Events = new();

        public void OnGameEvent(PlayerSession? actor, string key, string arg, int amount) => Events.Add((key, arg, amount));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    [Fact]
    public void ScanBlock_FiresOncePerCell_WithTheBlockKey_AndNeverWithoutACell()
    {
        using var w = World("scan_block_once");
        w.Player.State.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));
        w.Set(NpcLifeWorld.Cx + 2, w.PadY + 1, NpcLifeWorld.Cz, "pine_needles");
        w.Set(NpcLifeWorld.Cx + 3, w.PadY + 1, NpcLifeWorld.Cz, "pine_needles");
        var rec = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.ScanBlock, rec);

        var a = new Vector3i(NpcLifeWorld.Cx + 2, w.PadY + 1, NpcLifeWorld.Cz);
        var b = new Vector3i(NpcLifeWorld.Cx + 3, w.PadY + 1, NpcLifeWorld.Cz);
        w.Server.ScanSubject(w.Player.State.PlayerId, "block", "pine_needles", null, a);
        w.Server.ScanSubject(w.Player.State.PlayerId, "block", "pine_needles", null, a); // the same tree again: nothing
        w.Server.ScanSubject(w.Player.State.PlayerId, "block", "pine_needles", null, b); // a second tree: counts
        w.Server.ScanSubject(w.Player.State.PlayerId, "block", "pine_needles"); // an older client names no cell: nothing

        Assert.Equal(2, rec.Events.Count);
        Assert.All(rec.Events, e => Assert.Equal((GameEventKeys.ScanBlock, "pine_needles", 1), e));
        Assert.Equal(2, w.Player.State.ScannedCells.Count);

        // The set survives a reload: the same tree still counts once.
        w.Restart();
        w.Player.State.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));
        var rec2 = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.ScanBlock, rec2);
        w.Server.ScanSubject(w.Player.State.PlayerId, "block", "pine_needles", null, a);
        Assert.Empty(rec2.Events);
        Assert.Equal(2, w.Player.State.ScannedCells.Count);
    }

    [Fact]
    public void TheHandScanner_IsThrottled_ToTwoScansASecond()
    {
        using var w = World("scan_block_throttle");
        w.Player.State.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));
        w.Set(NpcLifeWorld.Cx + 2, w.PadY + 1, NpcLifeWorld.Cz, "stone");
        int before = w.Transport.Sent.Select(s => s.Msg).OfType<ScanResult>().Count();

        var intent = new ScanIntent { SubjectType = "block", SubjectKey = "stone", HasCell = true, CellX = NpcLifeWorld.Cx + 2, CellY = w.PadY + 1, CellZ = NpcLifeWorld.Cz };
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(intent));
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(intent)); // within the half second: dropped
        Assert.Equal(before + 1, w.Transport.Sent.Select(s => s.Msg).OfType<ScanResult>().Count());

        w.TickAt(0.4, seconds: 1.0); // time passes
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(intent));
        Assert.Equal(before + 2, w.Transport.Sent.Select(s => s.Msg).OfType<ScanResult>().Count());
    }

    [Fact]
    public void TheVocabulary_KnowsTheNewEvent()
    {
        Assert.Contains(GameEventKeys.ScanBlock, GameEventKeys.All);
        Assert.True(GameEventKeys.Matches("scan:*", GameEventKeys.ScanBlock));
    }
}
