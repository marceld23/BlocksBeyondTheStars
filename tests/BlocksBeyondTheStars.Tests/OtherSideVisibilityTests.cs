// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Who sees whom across the threshold (#3905): a player over there is withheld from everything our side is told about
/// them — the server sends nothing, it does not ask a client not to draw — players over there see each other and
/// everything of our side, and the animals and figures of the other side reach only the players who are over there.
/// </summary>
public sealed class OtherSideVisibilityTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlayerSession Friend(OtherSideStage w, string name = "Friend", float dx = 6f)
        => w.Player(name, new Vector3f(OtherSideStage.Cx + 0.5f + dx, w.PadY + 1, OtherSideStage.Cz + 0.5f));

    private static bool ToldAbout(OtherSideStage w, PlayerSession viewer, PlayerSession subject)
        => w.Transport.To<PlayerPresence>(viewer).Any(p => p.PlayerId == subject.State.PlayerId);

    [Fact]
    public void OurSide_GetsNoPresenceOfAPlayerOverThere_AndGetsItBackOnTheReturn()
    {
        using var w = new OtherSideStage(Content, "presence");
        var a = w.Player("Kid", w.AtGateA);
        var b = Friend(w);
        OtherSideStage.Dress(a);
        w.Tick(1.0);
        Assert.True(ToldAbout(w, b, a), "before the crossing the friend sees the kid");

        w.Transport.Sent.Clear();
        w.Use(a, w.GateA[1]);
        Assert.True(a.OtherSide);
        Assert.Contains(w.Transport.To<PlayerLeft>(b), l => l.PlayerId == a.State.PlayerId); // the avatar goes at once

        w.Transport.Sent.Clear();
        a.State.Position = new Vector3f(a.State.Position.X + 2f, a.State.Position.Y, a.State.Position.Z); // walking about
        w.Tick(3.0);
        Assert.False(ToldAbout(w, b, a), "nothing about a player over there reaches our side");
        Assert.True(ToldAbout(w, a, b), "the player over there still sees our side");

        a.State.Position = w.AtGateA;
        w.Use(a, w.GateA[1]);
        Assert.False(a.OtherSide);
        w.Transport.Sent.Clear();
        w.Tick(1.0);
        Assert.True(ToldAbout(w, b, a), "back on our side the friend sees the kid again");
    }

    [Fact]
    public void TwoPlayersOverThere_SeeEachOther()
    {
        using var w = new OtherSideStage(Content, "together");
        var a = w.Crossed("Kid");
        var c = Friend(w, "Watcher", dx: -7f);
        var b = Friend(w, "Friend", dx: 3f);
        OtherSideStage.Dress(b);
        b.State.FacePixels = new string('3', 32 * 32);
        a.State.FacePixels = new string('5', 32 * 32);

        w.Transport.Sent.Clear();
        w.Use(b, w.GateA[2]);
        Assert.True(b.OtherSide);
        Assert.Contains(w.Transport.To<PlayerFace>(b), f => f.PlayerId == a.State.PlayerId); // the look that was withheld until now

        w.Transport.Sent.Clear();
        w.Tick(2.0);
        Assert.True(ToldAbout(w, a, b));
        Assert.True(ToldAbout(w, b, a));
        Assert.False(ToldAbout(w, c, a));
        Assert.False(ToldAbout(w, c, b));
        Assert.True(ToldAbout(w, a, c));

        // One comes back: they drop the avatar of the one who stays, and our side gets their look again.
        w.Transport.Sent.Clear();
        w.Use(b, w.GateA[2]);
        Assert.False(b.OtherSide);
        Assert.Contains(w.Transport.To<PlayerLeft>(b), l => l.PlayerId == a.State.PlayerId);
        Assert.Contains(w.Transport.To<PlayerFace>(c), f => f.PlayerId == b.State.PlayerId);
        Assert.DoesNotContain(w.Transport.To<PlayerFace>(c), f => f.PlayerId == a.State.PlayerId);
    }

    [Fact]
    public void LooksGesturesAndTheStarMap_AreWithheldToo()
    {
        using var w = new OtherSideStage(Content, "looks");
        var a = w.Crossed("Kid");
        var b = Friend(w);
        var c = Friend(w, "Other", dx: -5f);
        OtherSideStage.Dress(c);
        w.Use(c, w.GateA[0]);
        Assert.True(c.OtherSide);
        w.Transport.Sent.Clear();

        // A new face: saved, shown to who is over there, withheld from our side.
        w.Send(a, new SetFaceIntent { Pixels = new string('7', 32 * 32) });
        Assert.Contains(w.Transport.To<PlayerFace>(c), f => f.PlayerId == a.State.PlayerId);
        Assert.DoesNotContain(w.Transport.To<PlayerFace>(b), f => f.PlayerId == a.State.PlayerId);

        // A wave.
        w.Send(a, new PlayerEmoteIntent { Emote = PlayerEmotes.Wave });
        Assert.Contains(w.Transport.To<PlayerEmote>(c), e => e.PlayerId == a.State.PlayerId);
        Assert.DoesNotContain(w.Transport.To<PlayerEmote>(b), e => e.PlayerId == a.State.PlayerId);

        // A tone on the melody bar.
        w.Send(a, new MelodyNoteIntent { Tone = 3 });
        Assert.NotEmpty(w.Transport.To<NoteSequence>(c));
        Assert.Empty(w.Transport.To<NoteSequence>(b));

        // The star map's player list.
        Assert.DoesNotContain("Kid", w.Server.StarMapPlayerNamesForTest(b));
        Assert.Contains("Friend", w.Server.StarMapPlayerNamesForTest(b));
        Assert.Contains("Kid", w.Server.StarMapPlayerNamesForTest(c));
        Assert.Contains("Kid", w.Server.StarMapPlayerNamesForTest(a));
        Assert.Contains("Friend", w.Server.StarMapPlayerNamesForTest(a)); // our side stays visible from over there

        // A newcomer on our side learns nothing either.
        w.Transport.Sent.Clear();
        var late = Friend(w, "Late", dx: 9f);
        w.Tick(1.0);
        Assert.False(ToldAbout(w, late, a));
        Assert.DoesNotContain(w.Transport.To<PlayerFace>(late), f => f.PlayerId == a.State.PlayerId);
        Assert.True(ToldAbout(w, late, b));

        // Chat stays visible to everybody.
        Assert.True(SvGameServer.OtherSideMayHandleForTest(new ChatIntent()));
    }

    [Fact]
    public void TheWorldsOwnSenses_SkipAPlayerOverThere()
    {
        using var w = new OtherSideStage(Content, "senses");
        var a = w.Player("Kid", w.AtGateA);
        var b = Friend(w);
        OtherSideStage.Dress(a);
        Assert.Contains(a.State.Position, w.Server.DoorOpenersForTest());
        Assert.Contains(a.State.PlayerId, w.Server.CrystalPresencePlayersForTest());
        Assert.Contains("Kid", w.Server.BumpNearbyPlayerNamesForTest(b));

        w.Use(a, w.GateA[1]);
        Assert.True(a.OtherSide);

        Assert.DoesNotContain(a.State.Position, w.Server.DoorOpenersForTest()); // no slide door opens for them
        Assert.DoesNotContain(a.State.PlayerId, w.Server.CrystalPresencePlayersForTest()); // no sensor counts them
        Assert.DoesNotContain("Kid", w.Server.BumpNearbyPlayerNamesForTest(b)); // no report of our side lists them
        Assert.Contains("Friend", w.Server.BumpNearbyPlayerNamesForTest(a));

        // A drop on the ground stays where it lies.
        var cell = new Vector3i(OtherSideStage.Cx + 3, w.PadY + 1, OtherSideStage.Cz - 3);
        w.Server.SpillToGroundForTest(cell, "stone", 3);
        a.State.Position = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
        w.Tick(2.0);
        Assert.Equal(0, a.State.Inventory.CountOf("stone"));
    }

    [Fact]
    public void TheQualis_AndTheSoundersAtTheGate_ReachOnlyWhoIsOverThere()
    {
        using var w = new OtherSideStage(Content, "folk");
        var a = w.Crossed("Kid");
        var b = Friend(w);
        w.Transport.Sent.Clear();

        w.Tick(8.0);

        var qualis = w.Server.OtherSideQualiIdsForTest();
        Assert.InRange(qualis.Count, SvGameServer.QualiMinNear, SvGameServer.QualiMaxNear);
        Assert.All(qualis, id => Assert.Equal("Quali", w.Server.NetCreatureForTest(id).BodyPlan));
        Assert.All(qualis, id => Assert.Contains(id, w.Server.CreatureIdsSentToForTest(a)));
        Assert.All(qualis, id => Assert.DoesNotContain(id, w.Server.CreatureIdsSentToForTest(b)));
        Assert.DoesNotContain(w.Server.SpeciesRoster, s => s.Id == SvGameServer.OtherSideQualiSpeciesId); // never spawned on our side

        // On the wire: the lists our side got never name one, the lists over there do; the motion stream likewise.
        var toA = w.Transport.To<CreatureList>(a);
        var toB = w.Transport.To<CreatureList>(b);
        Assert.Contains(toA, l => l.Creatures.Any(c => qualis.Contains(c.Id)));
        Assert.NotEmpty(toB);
        Assert.DoesNotContain(toB, l => l.Creatures.Any(c => qualis.Contains(c.Id)));
        Assert.DoesNotContain(w.Transport.To<CreatureMotionFrame>(b), f => f.Ids.Any(qualis.Contains));

        // They drift close, awake at any hour, and nobody can hit one.
        foreach (string id in qualis)
        {
            var q = w.Server.Creatures.First(c => c.Id == id);
            Assert.True(Distance(q.Position, a.State.Position) <= SvGameServer.QualiLeaveRange);
            Assert.False(w.Server.NetCreatureForTest(id).Asleep);
            float hull = q.Hull;
            w.Server.AttackEntity(b.State.PlayerId, id, 1f, 0f, 0f);
            Assert.Equal(hull, q.Hull);
        }

        // The Sounders who crossed over stand at the gate — for the one who is over there.
        var folk = w.Server.OtherSideFolkNamesForTest();
        Assert.InRange(folk.Count, 1, 3);
        Assert.All(folk, name => Assert.Contains(name, SvGameServer.OtherSideFolkNames));
        Assert.Equal(folk.Count, folk.Distinct().Count());
        var ids = w.Server.OtherSideNpcIdsForTest();
        Assert.Equal(folk.Count, ids.Count);
        Assert.All(ids, id => Assert.Contains(id, w.Server.NpcIdsSentToForTest(a)));
        Assert.All(ids, id => Assert.DoesNotContain(id, w.Server.NpcIdsSentToForTest(b)));
        Assert.All(ids, id => Assert.Equal(SoundersIds.Echo, w.Server.NetNpcForTest(id).Look));
        Assert.DoesNotContain(w.Transport.To<NpcList>(b), l => l.Npcs.Any(n => ids.Contains(n.Id)));

        // Nobody over there any more: the other side of this world is empty again.
        w.Server.ForceReturnForTest(a);
        w.Tick(2.0);
        Assert.Empty(w.Server.OtherSideQualiIdsForTest());
        Assert.Empty(w.Server.OtherSideNpcIdsForTest());
        Assert.DoesNotContain(w.Server.CreatureIdsSentToForTest(a), id => qualis.Contains(id));
    }

    [Fact]
    public void TheSoundersAtAGate_AreTheSameEveryTime_AndTalk()
    {
        using var w = new OtherSideStage(Content, "talk");
        var a = w.Crossed("Kid");
        var b = Friend(w);
        w.Tick(5.0);
        var first = w.Server.OtherSideFolkNamesForTest().ToList();
        int npc = w.Server.OtherSideNpcIdsForTest()[0];
        Assert.NotEmpty(first);

        // Small talk: the dialog of the folk over there opens, in the player's language.
        a.State.Position = At(w.Server.NetNpcForTest(npc), 1.5f);
        w.Transport.Sent.Clear();
        w.Send(a, new TalkToNpcIntent { NpcId = npc });
        var panel = w.Transport.To<NpcDialogState>(a).Single();
        Assert.Equal(TestLocales.Load("en")["dlg.snd_over_folk.prompt"], panel.Text);
        Assert.Equal(3, panel.Choices.Length);
        w.Send(a, new NpcDialogChoiceIntent { NpcId = npc, ChoiceIndex = 1 });
        Assert.Equal(TestLocales.Load("en")["dlg.snd_over_folk.r_lights"], w.Transport.To<NpcDialogState>(a).Last().Text);

        // On our side nobody stands there.
        b.State.Position = At(w.Server.NetNpcForTest(npc), 1.5f);
        w.Send(b, new TalkToNpcIntent { NpcId = npc });
        Assert.Empty(w.Transport.To<NpcDialogState>(b));
        Assert.Contains("@srv.npc.talk_gone", w.Transport.TextsTo(b));

        // Gone and back: the same gate has the same folk.
        a.State.Position = w.AtGateA;
        w.Server.ForceReturnForTest(a);
        w.Tick(2.0);
        Assert.Empty(w.Server.OtherSideFolkNamesForTest());
        w.Use(a, w.GateA[1]);
        w.Tick(5.0);
        Assert.Equal(first, w.Server.OtherSideFolkNamesForTest());
    }

    [Fact]
    public void AStoryEcho_PlacedOverThere_IsSeenAndHeardOnlyOverThere()
    {
        using var w = new OtherSideStage(Content, "story_echo");
        var a = w.Crossed("Kid");
        var b = Friend(w);
        var at = new Vector3f(OtherSideStage.Cx + 0.5f, w.PadY + 1, OtherSideStage.Cz - 4.5f);

        int elora = w.Server.SpawnOtherSideEchoForTest("elora", at);

        var net = w.Server.NetNpcForTest(elora);
        Assert.Equal("Elora", net.Name);
        Assert.Equal(SoundersIds.Echo, net.Look);
        Assert.Contains(elora, w.Server.NpcIdsSentToForTest(a));
        Assert.DoesNotContain(elora, w.Server.NpcIdsSentToForTest(b));
        Assert.True(w.Server.OtherSideAllowsForTest(a, new TalkToNpcIntent { NpcId = elora }));
        Assert.True(w.Server.RemoveSounderNpcForTest(elora)); // the story takes its Echo away again
        Assert.DoesNotContain(elora, w.Server.NpcIdsSentToForTest(a));
    }

    private static Vector3f At(NetNpc npc, float away) => new(npc.X + away, npc.Y, npc.Z);

    private static float Distance(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return System.MathF.Sqrt((dx * dx) + (dz * dz));
    }
}
