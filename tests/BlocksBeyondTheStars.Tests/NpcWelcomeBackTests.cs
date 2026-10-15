// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2595 — an NPC who knows the player (or the player's own resident) and has not seen them for 18 hours or more greets
/// them with "welcome back" instead of a hello: in passing, or as a bubble alongside the dialogue when the player talks
/// first. The "last seen" stamp is read BEFORE it is renewed; an empty stamp (a memory from before the stamp existed)
/// only stamps, so the update brings no flood of welcomes. The stamp is saved with the player.
/// </summary>
public sealed class NpcWelcomeBackTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly Localizer En = Content.CreateLocalizer(GameLocale.English);
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static (NpcLifeWorld W, int NpcId, string Key) ResidentWorld(string name)
    {
        var w = new NpcLifeWorld(Content, name);
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int npcId = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        var spot = new Vector3f(NpcLifeWorld.Cx + 5.5f, w.Feet, NpcLifeWorld.Cz + 5.5f);
        w.Server.ConfigureNpcForTest(npcId, spot);
        w.Player.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
        w.Server.SetWallClockForTest(T0);
        return (w, npcId, w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, npcId)!);
    }

    private static List<NpcGreeting> Greetings(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId && s.Msg is NpcGreeting).Select(s => (NpcGreeting)s.Msg).ToList();

    private static string[] Pool(string prefix, int count)
        => Enumerable.Range(1, count).Select(i => En.Get(prefix + i).Replace("{0}", NpcLifeWorld.Owner)).ToArray();

    private static string Iso(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

    [Fact]
    public void AnEmptyStamp_OnlyStamps_NoWelcomeFlood()
    {
        var (w, _, key) = ResidentWorld("welcome_empty");
        using (w)
        {
            Assert.Equal(string.Empty, w.Player.State.NpcMemory[key].LastSeenUtc); // seeded at move-in, never seen yet

            w.Server.TickPassingGreetingsForTest();

            Assert.Equal(NpcGreetingKinds.Passing, Assert.Single(Greetings(w)).Kind);
            Assert.Equal(Iso(T0), w.Player.State.NpcMemory[key].LastSeenUtc);
        }
    }

    [Fact]
    public void NineteenHoursLater_TheResidentSaysWelcomeBack_AndTheStampMovesOn()
    {
        var (w, _, key) = ResidentWorld("welcome_19h");
        using (w)
        {
            w.Server.TickPassingGreetingsForTest(); // stamps T0

            var later = T0.AddHours(19);
            w.Server.SetWallClockForTest(later);
            w.Server.AdvanceSystemDaysForTest(1);
            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();

            var welcome = Greetings(w)[1];
            Assert.Equal(NpcGreetingKinds.Welcome, welcome.Kind);
            Assert.Equal(NpcGestures.Wave, welcome.Gesture);
            Assert.Contains(welcome.Text, Pool("npc.welcome.resident.", 2));
            Assert.Equal(Iso(later), w.Player.State.NpcMemory[key].LastSeenUtc);

            // The next day — only an hour later — it is a plain hello again.
            w.Server.SetWallClockForTest(later.AddHours(1));
            w.Server.AdvanceSystemDaysForTest(1);
            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(NpcGreetingKinds.Passing, Greetings(w)[2].Kind);
        }
    }

    [Fact]
    public void ABriefAbsence_IsNoWelcome()
    {
        var (w, _, key) = ResidentWorld("welcome_brief");
        using (w)
        {
            w.Player.State.NpcMemory[key].LastSeenUtc = Iso(T0.AddHours(-17));
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(NpcGreetingKinds.Passing, Assert.Single(Greetings(w)).Kind);
        }
    }

    [Fact]
    public void TalkingFirst_TheWelcomeBubbleComesAlongsideTheDialogue()
    {
        var (w, npcId, key) = ResidentWorld("welcome_talk");
        using (w)
        {
            w.Player.State.NpcMemory[key].LastSeenUtc = Iso(T0.AddDays(-2));
            w.Server.TalkToNpcForTest(w.Player.State.PlayerId, npcId);

            Assert.Equal(NpcGreetingKinds.Welcome, Assert.Single(Greetings(w)).Kind);
            Assert.NotNull(w.Server.ActiveDialogForTest(w.Player.State.PlayerId)); // the conversation opened as usual
            Assert.Equal(Iso(T0), w.Player.State.NpcMemory[key].LastSeenUtc);
        }
    }

    [Fact]
    public void AKnownVillager_WelcomesBack_AStrangerNever()
    {
        var (w, npcId, _) = ResidentWorld("welcome_known");
        using (w)
        {
            w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 40f, w.Feet, NpcLifeWorld.Cz);
            var spot = w.Server.NpcSnapshots.Single(n => n.Id == npcId).Pos;
            var visitor = w.Server.AddLocalPlayer("Visitor");
            visitor.State.AboardShip = false;
            visitor.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
            string key = w.Server.NpcKeyOfNpcForTest(visitor.State.PlayerId, npcId)!;

            // A stranger with an old stamp: not an acquaintance — a plain hello.
            visitor.State.NpcMemory[key] = new NpcRelationship { Name = "x", Role = "settler", Value = 2, LastSeenUtc = Iso(T0.AddDays(-3)) };
            w.Server.TickPassingGreetingsForTest();
            var toVisitor = w.Transport.Sent.Where(s => s.Conn == visitor.ConnectionId && s.Msg is NpcGreeting).Select(s => (NpcGreeting)s.Msg).ToList();
            Assert.Equal(NpcGreetingKinds.Passing, Assert.Single(toVisitor).Kind);

            // Known, three days away: welcome back, by name.
            visitor.State.NpcMemory[key].Value = 20;
            visitor.State.NpcMemory[key].LastSeenUtc = Iso(T0.AddDays(-3));
            w.Server.AdvanceSystemDaysForTest(1);
            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();
            toVisitor = w.Transport.Sent.Where(s => s.Conn == visitor.ConnectionId && s.Msg is NpcGreeting).Select(s => (NpcGreeting)s.Msg).ToList();
            Assert.Equal(NpcGreetingKinds.Welcome, toVisitor[1].Kind);
            Assert.Contains(toVisitor[1].Text, Enumerable.Range(1, 2).Select(i => En.Get("npc.welcome.known." + i).Replace("{0}", "Visitor")));
        }
    }

    [Fact]
    public void TheStamp_SurvivesSavingAndRejoining()
    {
        var (w, _, key) = ResidentWorld("welcome_persist");
        using (w)
        {
            w.Server.TickPassingGreetingsForTest();
            string id = w.Player.State.PlayerId;
            w.Server.DisconnectLocalPlayerForTest(id);

            var back = w.Server.AddLocalPlayer(NpcLifeWorld.Owner);
            Assert.Equal(Iso(T0), back.State.NpcMemory[key].LastSeenUtc);
        }
    }
}
