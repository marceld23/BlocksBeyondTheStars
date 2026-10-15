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
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2616 — an NPC conversation lives on the player's session only while the client shows reply choices, under ONE
/// validity rule (same world, that very NPC object, not away visiting, within talk reach + 2 m the short way round the
/// seams). It ends on the client's leave (<see cref="NpcDialogChoiceIntent.Leave"/>, served while the world is held
/// paused), an end node, a new talk, walking away, a world change, the NPC leaving and a disconnect; the 1 Hz sweep tells
/// the client with <see cref="NpcDialogState.ActionClose"/>. NPC ids restart per world, so a stale talk must never apply
/// a choice to another NPC with the same id. While talking the NPC stands still facing the player; its walk waits at most
/// a minute. #2613 (server part): the talk reach wraps across both seams.
/// </summary>
public sealed class NpcConversationLifecycleTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>The arena with the player at (Cx−1.5, Cz−1.5) and a plain settler two blocks east of them.</summary>
    private static NpcLifeWorld World(string name, out int npcId)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        var p = w.Player.State.Position;
        npcId = w.Server.AddNpcForTest(new Vector3f(p.X + 2f, w.Feet, p.Z));
        return w;
    }

    private static string Pid(NpcLifeWorld w) => w.Player.State.PlayerId;

    private static List<NpcDialogState> Dialogs(NpcLifeWorld w, PlayerSession player)
        => w.Transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().ToList();

    private static List<NpcDialogState> Closes(NpcLifeWorld w, PlayerSession player)
        => Dialogs(w, player).Where(d => d.Action == NpcDialogState.ActionClose).ToList();

    /// <summary>Talks to the NPC — the settler's smalltalk opens with two replies.</summary>
    private static void Open(NpcLifeWorld w, int npcId)
    {
        w.Server.TalkToNpcForTest(Pid(w), npcId);
        Assert.Equal(("settler_neighbours", 0), w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Equal(npcId, w.Server.ConversationNpcForTest(Pid(w)));
    }

    private static bool Answered(NpcLifeWorld w, int npcId)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg)
            .Any(m => (m is NpcDialogState d && d.NpcId == npcId) || (m is NpcGreeting g && g.NpcId == npcId));

    // ---------------- Ending a conversation ----------------

    [Fact]
    public void TheLeave_EndsTheConversation_AndTheNpcGoesBackToItsDay()
    {
        using var w = World("talk_leave", out int npc);
        Open(w, npc);
        Assert.True(w.Server.NpcTalkHeldForTest(npc), "the NPC does not stand still for the talk");

        w.Server.LeaveDialogForTest(Pid(w), npc);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.False(w.Server.NpcTalkHeldForTest(npc));
        Assert.Empty(Closes(w, w.Player)); // the client closed the panel itself — nothing to send back

        // A reply after the leave changes nothing.
        w.Server.ChooseDialogForTest(Pid(w), 0, npc);
        Assert.DoesNotContain(w.Player.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEndNode_EndsTheConversation_WithItsClosingLine_NotAClose()
    {
        using var w = World("talk_end", out int npc);
        Open(w, npc);

        w.Server.ChooseDialogForTest(Pid(w), 0, npc); // "How is life here?" — the answer ends the talk
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Contains("dialogflag:settler_neighbours:0:0", w.Player.State.Milestones);
        var last = Dialogs(w, w.Player).Last();
        Assert.True(last.End);
        Assert.NotEqual(NpcDialogState.ActionClose, last.Action);
        Assert.Empty(Closes(w, w.Player));
        Assert.False(w.Server.NpcTalkHeldForTest(npc));
    }

    [Fact]
    public void WalkingAway_EndsItOnTheSweep_AndTheServerClosesThePanel()
    {
        using var w = World("talk_walk", out int npc);
        Open(w, npc);
        var p = w.Player.State.Position;

        w.Player.State.Position = new Vector3f(p.X - 4f, p.Y, p.Z); // 6 blocks: a step back — still talking (reach + 2)
        w.Server.SweepNpcConversationsForTest();
        Assert.NotNull(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Empty(Closes(w, w.Player));

        w.Player.State.Position = new Vector3f(p.X - 8f, p.Y, p.Z); // 10 blocks: walked away
        w.Server.SweepNpcConversationsForTest();
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        var close = Assert.Single(Closes(w, w.Player));
        Assert.Equal(npc, close.NpcId);
        Assert.True(close.End);
        Assert.Empty(close.Choices);
        Assert.False(w.Server.NpcTalkHeldForTest(npc));
    }

    [Fact]
    public void AnOlderClient_GetsNoClose_ItsPanelWouldPopOpenAgain()
    {
        using var w = World("talk_old_client", out int npc);
        w.Player.ClientFeatures.Remove(ClientFeatures.DialogClose1); // a build before #2616 announces no dialogclose1
        Open(w, npc);
        var p = w.Player.State.Position;

        w.Player.State.Position = new Vector3f(p.X - 8f, p.Y, p.Z);
        w.Server.SweepNpcConversationsForTest();
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w))); // the talk is over all the same
        Assert.Empty(Closes(w, w.Player));
        Assert.False(w.Server.NpcTalkHeldForTest(npc));
    }

    [Fact]
    public void AReplyAfterWalkingAway_AppliesNothing_AndClosesThePanel()
    {
        using var w = World("talk_walk_choose", out int npc);
        Open(w, npc);
        var p = w.Player.State.Position;
        w.Player.State.Position = new Vector3f(p.X - 8f, p.Y, p.Z); // before the sweep noticed

        w.Server.ChooseDialogForTest(Pid(w), 0, npc);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.DoesNotContain(w.Player.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));
        Assert.Empty(w.Player.State.NpcMemory); // no Dialog interaction, no standing
        Assert.Single(Closes(w, w.Player));
    }

    [Fact]
    public void ANewTalk_EndsTheOldOne_EvenWhenItOnlyGreets()
    {
        using var w = World("talk_greet_only", out int npc);
        var p = w.Player.State.Position;
        int sleeper = w.Server.AddNpcForTest(new Vector3f(p.X, w.Feet, p.Z + 2f));
        w.Server.ConfigureNpcForTest(sleeper, new Vector3f(p.X, w.Feet, p.Z + 2f), pose: 2); // asleep: only a mumble
        Open(w, npc);

        w.Server.TalkToNpcForTest(Pid(w), sleeper);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Equal(npc, Assert.Single(Closes(w, w.Player)).NpcId); // the old panel closes — its replies are dead
        Assert.False(w.Server.NpcTalkHeldForTest(npc));

        // …and a stale reply for the first NPC does nothing.
        w.Server.ChooseDialogForTest(Pid(w), 0, npc);
        Assert.DoesNotContain(w.Player.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));
    }

    [Fact]
    public void ALateLeave_ForAnEarlierNpc_DoesNotEndTheTalkWithTheNextOne()
    {
        using var w = World("talk_late_leave", out int first);
        var p = w.Player.State.Position;
        int second = w.Server.AddNpcForTest(new Vector3f(p.X, w.Feet, p.Z + 2f));
        Open(w, first);
        Open(w, second); // E at the next settler while the first panel was still open

        w.Server.LeaveDialogForTest(Pid(w), first); // the first panel's leave arrives late
        Assert.Equal(second, w.Server.ConversationNpcForTest(Pid(w)));
        Assert.True(w.Server.NpcTalkHeldForTest(second));

        w.Server.ChooseDialogForTest(Pid(w), 0, first); // a late reply for the first one too
        Assert.Equal(("settler_neighbours", 0), w.Server.ActiveDialogForTest(Pid(w)));

        w.Server.LeaveDialogForTest(Pid(w), second);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
    }

    [Fact]
    public void TheNpcLeavingTheWorld_EndsTheConversation()
    {
        using var w = World("talk_despawn", out int npc);
        Open(w, npc);

        w.Server.RemoveNpcForTest(npc);
        w.Server.SweepNpcConversationsForTest();
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Equal(npc, Assert.Single(Closes(w, w.Player)).NpcId);
    }

    [Fact]
    public void ADisconnect_EndsTheConversation_AndTheRejoinedPlayerIsInNone()
    {
        using var w = World("talk_rejoin", out int npc);
        Open(w, npc);

        w.Server.DisconnectLocalPlayerForTest(Pid(w));
        Assert.False(w.Server.NpcTalkHeldForTest(npc)); // the NPC goes back to its day at once

        var back = w.Server.AddLocalPlayer(NpcLifeWorld.Owner);
        back.State.Position = w.Player.State.Position;
        Assert.Null(w.Server.ActiveDialogForTest(back.State.PlayerId));
        w.Server.ChooseDialogForTest(back.State.PlayerId, 0, npc);
        Assert.DoesNotContain(back.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));
    }

    [Fact]
    public void AWorldChange_EndsTheConversation_AndAReusedIdOnTheNewWorld_IsNeverTalkedTo()
    {
        using var w = World("talk_world_switch", out int npc);
        Open(w, npc);
        string home = w.Player.CurrentLocationId;

        w.Server.SetInstantTravelForTest(true);
        string other = OtherPlanet(w).Id;
        Assert.True(w.Server.QuickTravelForTest(Pid(w), other));
        Assert.True(w.Server.ActivateWorldForTest(other));

        // NPC ids restart at 1 on every world: the first person here carries the id of the one talked to at home.
        var here = w.Player.State.Position;
        int twin = w.Server.AddNpcForTest(new Vector3f(here.X + 2f, here.Y, here.Z));
        Assert.Equal(npc, twin);

        // A reply for the old panel reaches the new world — it must not be applied to this other person.
        w.Server.ChooseDialogForTest(Pid(w), 0, npc);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.DoesNotContain(w.Player.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));
        Assert.Empty(w.Player.State.NpcMemory);
        Assert.Single(Closes(w, w.Player));
        Assert.NotEqual(home, w.Player.CurrentLocationId);
    }

    [Fact]
    public void AWorldChange_IsSweptAway_EvenWhenTheOldWorldNoLongerTicks()
    {
        using var w = World("talk_world_sweep", out int npc);
        Open(w, npc);

        w.Server.SetInstantTravelForTest(true);
        string other = OtherPlanet(w).Id;
        Assert.True(w.Server.QuickTravelForTest(Pid(w), other));
        Assert.True(w.Server.ActivateWorldForTest(other));

        w.Server.SweepNpcConversationsForTest(); // the sweep of the world the player is on now
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Equal(npc, Assert.Single(Closes(w, w.Player)).NpcId);
    }

    /// <summary>A second landable planet in the SAME system (quick travel without a jump generator).</summary>
    private static CelestialBody OtherPlanet(NpcLifeWorld w)
    {
        string sys = w.Server.Galaxy.FindBody(w.Server.ActiveLocationId)!.SystemId;
        return w.Server.Galaxy.AllBodies().First(b =>
            b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
            && b.SystemId == sys
            && !string.IsNullOrEmpty(b.PlanetType)
            && w.Content.GetPlanet(b.PlanetType!) is not null
            && b.Id != w.Server.ActiveLocationId);
    }

    // ---------------- The paused world ----------------

    [Fact]
    public void WhileTheWorldIsHeld_TheLeaveGetsThrough_ButAReplyWaits()
    {
        using var w = World("talk_paused", out int npc);
        Open(w, npc);
        w.Server.PauseForTest(w.Player, true);
        Assert.True(w.Server.IsPaused);

        // A real reply would move the frozen world (standing, consequences) — it is dropped like any gameplay intent.
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new NpcDialogChoiceIntent { ChoiceIndex = 0, NpcId = npc }));
        Assert.Equal(("settler_neighbours", 0), w.Server.ActiveDialogForTest(Pid(w)));
        Assert.DoesNotContain(w.Player.State.Milestones, m => m.StartsWith("dialogflag:", StringComparison.Ordinal));

        // Esc closed the panel as the pause menu opened: the leave must not be swallowed by the hold.
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(
            new NpcDialogChoiceIntent { ChoiceIndex = NpcDialogChoiceIntent.Leave, NpcId = npc }));
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.False(w.Server.NpcTalkHeldForTest(npc));
    }

    [Fact]
    public void TheLeave_RidesTheChoiceIntent_WithItsNpcId()
    {
        var leave = NetCodec.Decode(NetCodec.Encode(new NpcDialogChoiceIntent { ChoiceIndex = NpcDialogChoiceIntent.Leave, NpcId = 7 }));
        var intent = Assert.IsType<NpcDialogChoiceIntent>(leave);
        Assert.Equal(-1, intent.ChoiceIndex);
        Assert.Equal(7, intent.NpcId);

        var close = Assert.IsType<NpcDialogState>(NetCodec.Decode(NetCodec.Encode(new NpcDialogState { NpcId = 7, End = true, Action = NpcDialogState.ActionClose })));
        Assert.Equal("close", close.Action);
    }

    // ---------------- The NPC while talking ----------------

    [Fact]
    public void WhileTalking_TheNpcStandsStillFacingThePlayer_ItsWalkWaitsAtMostAMinute()
    {
        using var w = World("talk_hold", out int npc);
        var p = w.Player.State.Position;
        var start = w.Server.NpcSnapshots.Single(n => n.Id == npc).Pos;
        Open(w, npc);
        w.Server.SetNpcGoalForTest(npc, new Vector3f(start.X + 10f, start.Y, start.Z)); // its day wants it elsewhere

        w.TickAt(0.4, 10);
        var still = w.Server.NpcSnapshots.Single(n => n.Id == npc).Pos;
        Assert.True(Math.Abs(still.X - start.X) < 0.05f && Math.Abs(still.Z - start.Z) < 0.05f, $"the NPC walked off mid-talk to {still}");
        Assert.True(w.Server.NpcHasGoalForTest(npc)); // the walk waits — it is not dropped
        float facing = w.Server.NpcTalkStateForTest(npc)!.Value.Facing;
        float toPlayer = (float)Math.Atan2(p.X - start.X, p.Z - start.Z);
        Assert.True(Math.Abs(Math.IEEERemainder(facing - toPlayer, 2 * Math.PI)) < 0.1, $"facing {facing}, the player is at {toPlayer}");
        Assert.NotNull(w.Server.ActiveDialogForTest(Pid(w)));

        // A minute after the talk began the walk goes on — and walking out of reach ends the talk.
        w.TickAt(0.4, 80, until: () => w.Server.ActiveDialogForTest(Pid(w)) is null);
        Assert.Null(w.Server.ActiveDialogForTest(Pid(w)));
        Assert.Equal(npc, Assert.Single(Closes(w, w.Player)).NpcId);
        Assert.True(w.Server.NpcSnapshots.Single(n => n.Id == npc).Pos.X > start.X + 4f, "the NPC never went on with its walk");
    }

    // ---------------- #2613: the talk reach wraps ----------------

    [Fact]
    public void TalkingAcrossTheEastWestSeam_IsAnswered()
    {
        using var w = new NpcLifeWorld(Content.Value, "talk_seam_x");
        int c = w.Server.World.Circumference;
        float z = NpcLifeWorld.Cz + 0.5f;
        int npc = w.Server.AddNpcForTest(new Vector3f(c - 1.5f, w.Feet, z)); // canonical X just below the circumference
        w.Player.State.Position = new Vector3f(0.5f, w.Feet, z);             // 2 blocks east of it, across X = 0

        w.Server.TalkToNpcForTest(Pid(w), npc);
        Assert.True(Answered(w, npc), "an NPC two blocks away across the seam did not answer");
        Assert.Equal(npc, w.Server.ConversationNpcForTest(Pid(w)));

        // The kept conversation wraps too: a step back across the seam keeps it.
        w.Player.State.Position = new Vector3f(2.5f, w.Feet, z);
        w.Server.SweepNpcConversationsForTest();
        Assert.Equal(npc, w.Server.ConversationNpcForTest(Pid(w)));

        // Really far is still too far.
        w.Player.State.Position = new Vector3f(12.5f, w.Feet, z);
        int far = w.Server.AddNpcForTest(new Vector3f(c - 1.5f, w.Feet, z + 0.5f));
        w.Server.TalkToNpcForTest(Pid(w), far);
        Assert.False(Answered(w, far));
    }

    [Fact]
    public void TalkingAcrossTheNorthSouthSeam_IsAnswered()
    {
        using var w = new NpcLifeWorld(Content.Value, "talk_seam_z");
        int period = WorldConstants.LatitudePeriodFor(w.Server.World.Circumference);
        float x = NpcLifeWorld.Cx + 0.5f;
        int npc = w.Server.AddNpcForTest(new Vector3f(x, w.Feet, (period / 2) - 1.5f)); // the northern edge of the band
        w.Player.State.Position = new Vector3f(x, w.Feet, (-period / 2) + 0.5f);        // just across the latitude seam

        w.Server.TalkToNpcForTest(Pid(w), npc);
        Assert.True(Answered(w, npc), "an NPC two blocks away across the latitude seam did not answer");
    }
}
