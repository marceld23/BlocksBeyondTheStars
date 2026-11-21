// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3914 — bare hands on sound. A player over there WITHOUT a Resonance Blaster could be walled in by a stranger's ring
/// of sound blocks one cell further out than the shaping rule reaches, and the way back is a rune gate, nothing else. So
/// the mine key (anything in hand but the blaster) works a sound block that touches the player's own body box, slowly:
/// the server keeps the time (two seconds of hits, the crack overlay told on every hit), a pause or another block starts
/// over, a block farther out stays — and nothing of the real world is ever mined over there. All through the real intent
/// path (<c>Dispatch</c> with its other-side gate).
/// </summary>
public sealed class BareHandSoundTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>The mine key's pace on the client: a hit every quarter second.</summary>
    private const double Hit = 0.25;

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 12);

    /// <summary>A builder (over there, an ordinary player) and a stranger over there with nothing in hand.</summary>
    private static (PlayerSession Builder, PlayerSession Caged) Pair(NpcLifeWorld w)
    {
        var builder = w.Player;
        builder.OtherSide = true;
        builder.State.Role = PlayerRole.Player;
        var caged = SoundLayerTests.Join(w, "Caged", 2.5f, overThere: true);
        return (builder, caged);
    }

    private static void Mine(NpcLifeWorld w, PlayerSession s, Vector3i cell)
        => w.Server.HandlePayloadForTest(s.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

    /// <summary>Hits a cell for <paramref name="seconds"/> at the mine key's pace (the first hit at once).</summary>
    private static void HoldMine(NpcLifeWorld w, PlayerSession s, Vector3i cell, double seconds)
    {
        for (double t = 0; t <= seconds + 1e-6; t += Hit)
        {
            Mine(w, s, cell);
            w.Server.AdvanceUptimeForTest(Hit);
        }
    }

    [Fact]
    public void WithoutABlaster_TheMineKey_DissolvesAStrangersBlockThatTouchesThePlayer_AfterTwoSecondsOfHits()
    {
        using var w = World("bare_hands");
        var (builder, caged) = Pair(w);
        var feet = caged.State.Position.ToBlock();
        var wall = new Vector3i(feet.X + 1, feet.Y + 1, feet.Z);
        w.Server.PutSoundCellForTest(wall, OtherSideRules.ToneGlow[0], builder.State.PlayerId);
        w.Transport.Sent.Clear();

        // Most of the two seconds: the block stands, and every hit tells the client how far it is.
        for (int i = 0; i < 8; i++)
        {
            Mine(w, caged, wall);
            w.Server.AdvanceUptimeForTest(Hit);
        }

        Assert.True(w.Server.IsSoundBlock(wall), "bare hands are slow");
        var progress = w.Transport.To<MiningProgress>(caged).ToList();
        Assert.Equal(8, progress.Count);
        Assert.All(progress, p => Assert.Equal((wall.X, wall.Y, wall.Z), (p.X, p.Y, p.Z)));
        Assert.True(progress.Last().Fraction > progress.First().Fraction, "the cracks grow");
        Assert.InRange(progress.Last().Fraction, 0.8f, 0.99f);

        // The ninth hit, two seconds after the first: it gives way, whoever shaped it.
        Mine(w, caged, wall);
        Assert.False(w.Server.IsSoundBlock(wall));
        Assert.Equal(0, w.Server.SoundBlocksOf(builder.State.PlayerId));
        Assert.True(w.Server.World.GetBlock(wall).IsAir, "the real cell was air and stays air");
        Assert.DoesNotContain(w.Transport.To<ActionRejected>(caged), r => r.Action == "mine");
        Assert.DoesNotContain(w.Transport.To<ServerMessage>(caged), m => m.Text == SvGameServer.OtherSideRefusedLine);
    }

    [Fact]
    public void APause_OrAnotherBlock_StartsTheWorkOver()
    {
        using var w = World("bare_hands_pause");
        var (builder, caged) = Pair(w);
        var feet = caged.State.Position.ToBlock();
        var east = new Vector3i(feet.X + 1, feet.Y, feet.Z);
        var west = new Vector3i(feet.X - 1, feet.Y, feet.Z);
        w.Server.PutSoundCellForTest(east, OtherSideRules.ToneGlow[0], builder.State.PlayerId);
        w.Server.PutSoundCellForTest(west, OtherSideRules.ToneGlow[0], builder.State.PlayerId);

        // 1.75 s on the east block, a pause of a second, 1.75 s more: never two seconds in one go.
        HoldMine(w, caged, east, 1.5);
        w.Server.AdvanceUptimeForTest(1.0);
        HoldMine(w, caged, east, 1.5);
        Assert.True(w.Server.IsSoundBlock(east), "a pause longer than the gap starts over");

        // Switching blocks starts over too: east, west, east … each a second.
        for (int round = 0; round < 3; round++)
        {
            HoldMine(w, caged, round % 2 == 0 ? west : east, 1.0);
        }

        Assert.True(w.Server.IsSoundBlock(east));
        Assert.True(w.Server.IsSoundBlock(west));

        // One unbroken hold does it.
        HoldMine(w, caged, west, OtherSideRules.BareHandDissolveSeconds);
        Assert.False(w.Server.IsSoundBlock(west));
    }

    [Fact]
    public void ABlockFartherOut_StaysQuietly_ItIsTheBlastersToReach()
    {
        using var w = World("bare_hands_far");
        var (builder, caged) = Pair(w);
        var feet = caged.State.Position.ToBlock();
        var ring = new Vector3i(feet.X + 2, feet.Y, feet.Z); // two cells out: the shaping rule allows it
        Assert.True(w.Server.TryShapeSound(builder, ring, 0, out string refusal), refusal);
        w.Transport.Sent.Clear();

        HoldMine(w, caged, ring, 3.0);

        Assert.True(w.Server.IsSoundBlock(ring));
        Assert.Empty(w.Transport.To<MiningProgress>(caged));
        Assert.DoesNotContain(w.Transport.To<ServerMessage>(caged), m => m.Text == SvGameServer.OtherSideRefusedLine);

        // One step toward it, and the same block touches the player's body box: it gives way.
        caged.State.Position = new Vector3f(caged.State.Position.X + 1f, caged.State.Position.Y, caged.State.Position.Z);
        HoldMine(w, caged, ring, OtherSideRules.BareHandDissolveSeconds);
        Assert.False(w.Server.IsSoundBlock(ring));
    }

    [Fact]
    public void OverThere_TheMineKey_NeverTouchesTheRealWorld_AndTheGateOpensOnlyOnSound()
    {
        using var w = World("bare_hands_real");
        var (_, caged) = Pair(w);
        var feet = caged.State.Position.ToBlock();
        var dirt = new Vector3i(feet.X + 1, feet.Y, feet.Z); // a block bare hands mine on our side
        w.Set(dirt.X, dirt.Y, dirt.Z, "dirt");
        var air = new Vector3i(feet.X - 1, feet.Y, feet.Z);

        Assert.False(w.Server.OtherSideAllowsForTest(caged, new MineBlockIntent { X = dirt.X, Y = dirt.Y, Z = dirt.Z }));
        Assert.False(w.Server.OtherSideAllowsForTest(caged, new MineBlockIntent { X = air.X, Y = air.Y, Z = air.Z }));
        w.Server.PutSoundCellForTest(air, OtherSideRules.ToneGlow[0], NpcLifeWorld.Owner);
        Assert.True(w.Server.OtherSideAllowsForTest(caged, new MineBlockIntent { X = air.X, Y = air.Y, Z = air.Z }));

        // Through the gate the real block is refused with the one line; past it (a test seam) it is still never mined.
        w.Server.ResetOtherSideRefusalPaceForTest(caged);
        w.Transport.Sent.Clear();
        HoldMine(w, caged, dirt, 3.0);
        for (int i = 0; i < 40; i++)
        {
            w.Server.MineBlockOnce(caged.State.PlayerId, dirt.X, dirt.Y, dirt.Z);
        }

        Assert.Equal(w.Block("dirt").Value, w.Server.World.GetBlock(dirt).Value);
        Assert.Contains(w.Transport.To<ServerMessage>(caged), m => m.Text == SvGameServer.OtherSideRefusedLine);
        Assert.Empty(w.Transport.To<MiningProgress>(caged));

        // On our side the mine key mines as always.
        caged.OtherSide = false;
        w.Server.MineBlock(caged.State.PlayerId, dirt.X, dirt.Y, dirt.Z);
        Assert.True(w.Server.World.GetBlock(dirt).IsAir);
    }

    [Fact]
    public void TheBodyBox_IsTheOneRuleOfServerAndClient()
    {
        var feet = new Vector3i(10, 20, 30);
        Assert.True(OtherSideRules.InBodyBox(feet, new Vector3i(11, 22, 29), OtherSideRules.BodyMargin, 6000)); // above the head, a corner
        Assert.True(OtherSideRules.InBodyBox(feet, new Vector3i(9, 19, 31), OtherSideRules.BodyMargin, 6000));  // below the feet
        Assert.False(OtherSideRules.InBodyBox(feet, new Vector3i(12, 20, 30), OtherSideRules.BodyMargin, 6000)); // two cells out
        Assert.False(OtherSideRules.InBodyBox(feet, new Vector3i(10, 23, 30), OtherSideRules.BodyMargin, 6000)); // two over the head
        Assert.True(OtherSideRules.InBodyBox(feet, new Vector3i(10, 21, 30), 0, 6000), "the head cell is in the body");
        Assert.False(OtherSideRules.InBodyBox(feet, new Vector3i(11, 20, 30), 0, 6000), "without a margin only the body itself");

        // The short way round the seam: x = 0 and x = circumference − 1 are neighbours.
        Assert.True(OtherSideRules.InBodyBox(new Vector3i(0, 20, 30), new Vector3i(5999, 20, 30), 1, 6000));
        Assert.Equal(OtherSideRules.BodyMargin, SvGameServer.SoundBodyMargin);
    }
}
