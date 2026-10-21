// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2593 — NPCs greet and wave when the player walks past: the nearest awake, idle NPC within 6 m with a clear line of
/// sight says a short line by friendship stage (kind Passing, a wave, a fresh bubble id) and pauses to look at the player;
/// once per NPC and player per in-game day, at most one greeting every 4 s per player (8 s in a crowd). Never asleep,
/// walking, a G.D.S. guardian, the streamer, out of range or behind a wall; no hints or story threads consumed, no
/// standing for strangers. #2594: the player's own residents say "welcome home"; their talk is the resident dialogue.
/// </summary>
public sealed class NpcPassingGreetingTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A base with its founding resident standing idle five blocks out, the owner three blocks from them.</summary>
    private static (NpcLifeWorld W, int NpcId, Vector3f Spot) ResidentWorld(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content, name, configure: configure);
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int npcId = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        var spot = new Vector3f(NpcLifeWorld.Cx + 5.5f, w.Feet, NpcLifeWorld.Cz + 5.5f);
        w.Server.ConfigureNpcForTest(npcId, spot);
        w.Player.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
        return (w, npcId, spot);
    }

    private static List<NpcGreeting> GreetingsTo(NpcLifeWorld w, PlayerSession p)
        => w.Transport.Sent.Where(s => s.Conn == p.ConnectionId && s.Msg is NpcGreeting).Select(s => (NpcGreeting)s.Msg).ToList();

    private static string[] Lines(string prefix, int count, string name = "")
        => Enumerable.Range(1, count).Select(i => Content.CreateLocalizer(Shared.Localization.GameLocale.English)
            .Get(prefix + i).Replace("{0}", name)).ToArray();

    [Fact]
    public void AnIdleResident_WavesAtItsOwner_SaysWelcomeHome_AndPausesToLook()
    {
        var (w, npcId, _) = ResidentWorld("pass_resident");
        using (w)
        {
            w.Server.TickPassingGreetingsForTest();

            var g = Assert.Single(GreetingsTo(w, w.Player));
            Assert.Equal(npcId, g.NpcId);
            Assert.Equal(NpcGreetingKinds.Passing, g.Kind);
            Assert.Equal(NpcGestures.Wave, g.Gesture);
            Assert.True(g.GreetingId > 0);
            Assert.Contains(NpcLifeWorld.Owner, g.Text, StringComparison.Ordinal); // a resident knows the owner by name
            Assert.True(w.Server.NpcAttendingForTest(npcId), "the NPC does not pause to look at the player");
        }
    }

    [Fact]
    public void AStranger_HearsAPlainHello_WithoutTheirName_AndGainsNothing()
    {
        var (w, npcId, spot) = ResidentWorld("pass_stranger");
        using (w)
        {
            w.Player.State.Position = new Vector3f(spot.X + 30f, w.Feet, spot.Z); // the owner is far away
            var visitor = w.Server.AddLocalPlayer("Visitor");
            visitor.State.AboardShip = false;
            visitor.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);

            w.Server.TickPassingGreetingsForTest();

            var g = Assert.Single(GreetingsTo(w, visitor));
            Assert.Contains(g.Text, Lines("npc.hello.stranger.", 3));
            Assert.DoesNotContain("Visitor", g.Text, StringComparison.Ordinal);
            Assert.Empty(visitor.State.NpcMemory); // no standing, no acquaintance from a hello
            Assert.Empty(GreetingsTo(w, w.Player));
        }
    }

    [Fact]
    public void AKnownVisitor_HearsTheirName()
    {
        var (w, npcId, spot) = ResidentWorld("pass_known");
        using (w)
        {
            w.Player.State.Position = new Vector3f(spot.X + 30f, w.Feet, spot.Z);
            var visitor = w.Server.AddLocalPlayer("Visitor");
            visitor.State.AboardShip = false;
            visitor.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
            string key = w.Server.NpcKeyOfNpcForTest(visitor.State.PlayerId, npcId)!;
            visitor.State.NpcMemory[key] = new NpcRelationship { Name = "x", Role = "settler", Value = 20 };

            w.Server.TickPassingGreetingsForTest();

            var g = Assert.Single(GreetingsTo(w, visitor));
            Assert.Contains(g.Text, Lines("npc.hello.known.", 3, "Visitor"));
        }
    }

    [Fact]
    public void OncePerInGameDay_ThenAgainTheNextDay()
    {
        var (w, _, _) = ResidentWorld("pass_daily");
        using (w)
        {
            w.Server.TickPassingGreetingsForTest();
            Assert.Single(GreetingsTo(w, w.Player));

            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();
            Assert.Single(GreetingsTo(w, w.Player)); // same day: nothing more

            w.Server.AdvanceSystemDaysForTest(1);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(2, GreetingsTo(w, w.Player).Count);
            Assert.True(GreetingsTo(w, w.Player)[1].GreetingId > GreetingsTo(w, w.Player)[0].GreetingId);
        }
    }

    [Fact]
    public void Nobody_Greets_WhenAsleep_Walking_AGuardian_TheStreamer_OutOfRange_OrBehindAWall()
    {
        var (w, npcId, spot) = ResidentWorld("pass_never");
        using (w)
        {
            var near = w.Player.State.Position;
            void Expect(string why)
            {
                w.Server.TickPassingGreetingsForTest();
                Assert.True(GreetingsTo(w, w.Player).Count == 0, why);
                w.Server.ConfigureNpcForTest(npcId, spot, job: string.Empty, role: "settler"); // back to idle
                w.Player.State.Position = near;
            }

            w.Server.ConfigureNpcForTest(npcId, spot, pose: 2);
            Expect("an NPC asleep in bed greeted");

            w.Server.SetNpcGoalForTest(npcId, new Vector3f(spot.X + 8f, w.Feet, spot.Z));
            Expect("an NPC walking a route greeted");

            w.Server.ConfigureNpcForTest(npcId, spot, role: "guardian");
            Expect("a G.D.S. guardian greeted");

            w.Server.ConfigureNpcForTest(npcId, spot, job: "streamer");
            Expect("the streamer greeted (it has its own call-out)");

            w.Player.State.Position = new Vector3f(spot.X - 8f, w.Feet, spot.Z);
            Expect("an NPC 8 m away greeted");

            for (int dz = -3; dz <= 3; dz++)
            {
                for (int y = 0; y < 3; y++)
                {
                    w.Set(NpcLifeWorld.Cx + 4, w.Feet + y, NpcLifeWorld.Cz + 5 + dz, "stone");
                }
            }

            Expect("an NPC behind a wall greeted");

            for (int dz = -3; dz <= 3; dz++)
            {
                for (int y = 0; y < 3; y++)
                {
                    w.Server.RemoveBlockForTest(NpcLifeWorld.Cx + 4, w.Feet + y, NpcLifeWorld.Cz + 5 + dz);
                }
            }

            w.Server.TickPassingGreetingsForTest();
            Assert.Single(GreetingsTo(w, w.Player)); // all clear: now they do
        }
    }

    [Fact]
    public void ATalk_IsTodaysHello_NoPassingGreetingRightAfterIt()
    {
        var (w, npcId, _) = ResidentWorld("pass_talked");
        using (w)
        {
            w.Server.TalkToNpcForTest(w.Player.State.PlayerId, npcId);
            int before = GreetingsTo(w, w.Player).Count;
            w.Server.ChooseDialogForTest(w.Player.State.PlayerId, 1); // end the talk
            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(before, GreetingsTo(w, w.Player).Count);
        }
    }

    [Fact]
    public void InAConversation_NobodyCallsOut_ButWalkingAwayFromItEndsThat()
    {
        var (w, npcId, spot) = ResidentWorld("pass_dialog_open");
        using (w)
        {
            w.Server.TalkToNpcForTest(w.Player.State.PlayerId, npcId); // the panel opens (and that is today's hello)
            int other = w.Server.AddNpcForTest(new Vector3f(spot.X - 3f, w.Feet, spot.Z + 3f));
            w.Server.TickPassingGreetingsForTest();
            Assert.DoesNotContain(GreetingsTo(w, w.Player), g => g.NpcId == other); // mid-conversation: quiet

            // The player walks off without answering — #2616: the talk is over by the one validity rule (even before the
            // 1 Hz sweep ends it), so the next villager may greet.
            w.Player.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z + 9f);
            w.Server.ConfigureNpcForTest(other, new Vector3f(spot.X - 3f, w.Feet, spot.Z + 12f));
            w.Server.TickPassingGreetingsForTest();
            Assert.Contains(GreetingsTo(w, w.Player), g => g.NpcId == other);
            w.Server.SweepNpcConversationsForTest();
            Assert.Null(w.Server.ActiveDialogForTest(w.Player.State.PlayerId));
        }
    }

    [Fact]
    public void AtMostOneGreetingEveryFourSeconds_EightInACrowd()
    {
        var (w, npcId, spot) = ResidentWorld("pass_gap");
        using (w)
        {
            int second = w.Server.AddNpcForTest(new Vector3f(spot.X - 3f, w.Feet, spot.Z + 3f));
            w.Server.TickPassingGreetingsForTest();
            Assert.Single(GreetingsTo(w, w.Player));

            w.Server.AdvanceUptimeForTest(2);
            w.Server.TickPassingGreetingsForTest();
            Assert.Single(GreetingsTo(w, w.Player)); // within 4 s: the other one waits

            w.Server.AdvanceUptimeForTest(2.5);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(2, GreetingsTo(w, w.Player).Count);
            Assert.Equal(new[] { npcId, second }.OrderBy(i => i), GreetingsTo(w, w.Player).Select(g => g.NpcId).OrderBy(i => i));

            // A crowd: seven more people within 10 m — after the next hello the gap is 8 s.
            var crowd = new List<int>();
            for (int i = 0; i < 7; i++)
            {
                crowd.Add(w.Server.AddNpcForTest(new Vector3f(spot.X - 3f + (i % 2 == 0 ? 2f : -2f), w.Feet, spot.Z - 4f + i)));
            }

            w.Server.AdvanceUptimeForTest(4.5);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(3, GreetingsTo(w, w.Player).Count);

            w.Server.AdvanceUptimeForTest(4.5);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(3, GreetingsTo(w, w.Player).Count); // 4.5 s is not enough in a crowd

            w.Server.AdvanceUptimeForTest(4);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal(4, GreetingsTo(w, w.Player).Count);
        }
    }

    [Fact]
    public void APassingGreeting_ConsumesNoHintNoThread_AndPaysNoStandingToAStranger()
    {
        var (w, npcId, spot) = ResidentWorld("pass_nosideeffects");
        using (w)
        {
            var before = w.Player.State.Milestones.ToHashSet();
            int value = w.Server.RelationshipValueForTest(w.Player.State.PlayerId, w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, npcId)!);

            w.Server.TickPassingGreetingsForTest();

            var g = Assert.Single(GreetingsTo(w, w.Player));
            var resident = Lines("npc.hello.resident.", 3, NpcLifeWorld.Owner)
                .Concat(new[] { "gardener", "craftsman", "guard", "vendor", "quartermaster" }
                    .Select(j => Content.CreateLocalizer(Shared.Localization.GameLocale.English).Get("npc.hello.resident." + j)
                        .Replace("{0}", NpcLifeWorld.Owner)));
            Assert.Contains(g.Text, resident); // a hello line — never a hint or a story thread
            Assert.True(before.SetEquals(w.Player.State.Milestones), "a passing greeting burned a milestone");

            // A base resident is no villager person key: the hello pays nothing (only villagers met before get +1).
            Assert.Equal(value, w.Server.RelationshipValueForTest(w.Player.State.PlayerId, w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, npcId)!));
        }
    }

    // ---------------- #2594: own residents ----------------

    [Fact]
    public void YourOwnResident_TalksAboutHome_AVisitorStillGetsTheVisitorsTalk()
    {
        var (w, npcId, spot) = ResidentWorld("pass_resident_dialog");
        using (w)
        {
            string id = w.Player.State.PlayerId;
            w.Player.State.Milestones.Add("dialog:settler_crystal_gift:done"); // the one-off gift went earlier
            w.Server.TalkToNpcForTest(id, npcId);
            Assert.Equal(("resident_home", 0), w.Server.ActiveDialogForTest(id));
            Assert.Contains(w.Transport.Sent, s => s.Conn == w.Player.ConnectionId && s.Msg is NpcDialogState d
                && d.Text == Content.CreateLocalizer(Shared.Localization.GameLocale.English).Get("dlg.resident_home.prompt"));

            var visitor = w.Server.AddLocalPlayer("Visitor");
            visitor.State.Position = new Vector3f(spot.X - 2f, w.Feet, spot.Z);
            w.Server.TalkToNpcForTest(visitor.State.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), w.Server.ActiveDialogForTest(visitor.State.PlayerId));
        }
    }

    [Fact]
    public void AResidentWithAJob_HasTheJobsTalk()
    {
        var (w, npcId, spot) = ResidentWorld("pass_resident_job");
        using (w)
        {
            string id = w.Player.State.PlayerId;
            w.Player.State.Milestones.Add("dialog:settler_crystal_gift:done");
            w.Server.ConfigureNpcForTest(npcId, spot, job: "gardener");
            w.Server.TalkToNpcForTest(id, npcId);
            Assert.Equal(("resident_gardener", 0), w.Server.ActiveDialogForTest(id));
        }
    }

    [Fact]
    public void ContentCheck_TheNewLines_ExistInEnglishAndGerman()
    {
        // Raw de.json (the localizer falls back to English, so Has() would hide a missing German line).
        var de = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(TestPaths.DataDir(), "locales", "de.json")));
        var en = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(TestPaths.DataDir(), "locales", "en.json")));
        var keys = new List<string>();
        keys.AddRange(Enumerable.Range(1, 3).SelectMany(i => new[] { "npc.hello.stranger." + i, "npc.hello.known." + i, "npc.hello.trusted." + i, "npc.hello.resident." + i }));
        keys.AddRange(new[] { "gardener", "craftsman", "guard", "vendor", "quartermaster" }.Select(j => "npc.hello.resident." + j));
        keys.AddRange(Enumerable.Range(1, 2).SelectMany(i => new[] { "npc.welcome.known." + i, "npc.welcome.trusted." + i, "npc.welcome.resident." + i }));
        keys.AddRange(new[] { "npc.greet.vendor.known", "npc.greet.vendor.trusted", "npc.greet.quartermaster.known", "npc.greet.quartermaster.trusted" });
        keys.AddRange(Content.Dialogs.Where(d => d.Key.StartsWith("resident_", StringComparison.Ordinal))
            .SelectMany(d => d.Nodes).SelectMany(n => new[] { n.PromptKey }.Concat(n.Choices.SelectMany(c => new[] { c.TextKey, c.ResponseKey }))));
        foreach (string key in keys)
        {
            Assert.True(en.RootElement.TryGetProperty(key, out _), $"en.json lacks '{key}'");
            Assert.True(de.RootElement.TryGetProperty(key, out _), $"de.json lacks '{key}'");
        }

        Assert.Equal(5, Content.Dialogs.Count(d => d.Key.StartsWith("resident_", StringComparison.Ordinal)));
        Assert.All(Content.Dialogs.Where(d => d.Key.StartsWith("resident_", StringComparison.Ordinal)),
            d => Assert.Equal(Shared.Definitions.DialogDefinition.HomeOwnBase, d.Home));
        Assert.Equal(Shared.Definitions.DialogDefinition.HomeAway, Content.Dialogs.Single(d => d.Key == "settler_neighbours").Home);
    }

    // ---------------- #2594: the E-greeting by stage ----------------

    [Fact]
    public void TheEGreeting_OfAKnownTrader_UsesTheirName_AStrangerStillGetsTheClientsLine()
    {
        using var w = new NpcLifeWorld(Content, "pass_stage_greeting");
        int baseId = w.FoundBase();
        var post = new Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "station_vendor");
        w.Server.ScanBaseLifeForTest();
        int vendorId = w.Server.BaseResidentsForTest(baseId).Single(r => r.Job == "vendor").NpcId;
        var at = new Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        w.Server.ConfigureNpcForTest(vendorId, at);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 40f, w.Feet, NpcLifeWorld.Cz);

        // Somebody else's base vendor: a stranger to the visitor → today's line (a story flavour line or "" for the
        // client's role line), never their name.
        var visitor = w.Server.AddLocalPlayer("Visitor");
        visitor.State.Position = new Vector3f(at.X - 1.5f, w.Feet, at.Z);
        Assert.True(w.Server.EmitGreetingForTest(visitor.State.PlayerId, "vendor"));
        Assert.DoesNotContain("Visitor", GreetingsTo(w, visitor).Last().Text, StringComparison.Ordinal);

        // Known → the stage line with the visitor's name. (A known NPC may first share a treasure hint or a story thread —
        // each only once — so greet a few times.)
        string key = w.Server.NpcKeyOfNpcForTest(visitor.State.PlayerId, vendorId)!;
        visitor.State.NpcMemory[key] = new NpcRelationship { Name = "x", Role = "vendor", Value = 15 };
        var en = Content.CreateLocalizer(Shared.Localization.GameLocale.English);
        Assert.True(GreetsWith(w, visitor, en.Get("npc.greet.vendor.known").Replace("{0}", "Visitor")));

        visitor.State.NpcMemory[key].Value = 45;
        Assert.True(GreetsWith(w, visitor, en.Get("npc.greet.vendor.trusted").Replace("{0}", "Visitor")));
    }

    /// <summary>E-greets the nearest vendor (past the 25 s cooldown each time) until it says <paramref name="line"/>.</summary>
    private static bool GreetsWith(NpcLifeWorld w, PlayerSession p, string line)
    {
        for (int i = 0; i < 12; i++)
        {
            w.Server.AdvanceUptimeForTest(30);
            Assert.True(w.Server.EmitGreetingForTest(p.State.PlayerId, "vendor"));
            if (GreetingsTo(w, p).Last().Text == line)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- AI (hosted worlds) ----------------

    [Fact]
    public void WithAi_AResidentSaysTheCachedLine_NextTime_AndAStrangerNeverAsksTheBackend()
    {
        var provider = new FakeNpcLines(r => r.Purpose == NpcLinePurposes.ResidentHello ? new[] { "Home sweet home, {player}!" } : null);
        var (w, npcId, spot) = ResidentWorld("pass_ai", c => c.AiLevel = AiLevel.TextOnly);
        using (w)
        {
            w.Server.SetAiProviderForTest(provider);
            w.Server.RunAiLinesSynchronouslyForTest();
            w.Server.SetAiVariantRollForTest(1.0); // no extra variants

            w.Server.TickPassingGreetingsForTest();
            Assert.DoesNotContain("Home sweet home", GreetingsTo(w, w.Player)[0].Text, StringComparison.Ordinal); // a miss: authored
            Assert.Contains(provider.Requests, r => r.Purpose == NpcLinePurposes.ResidentHello && r.Tokens.Contains(AiLineTokens.Player));

            w.Server.AdvanceSystemDaysForTest(1);
            w.Server.AdvanceUptimeForTest(30);
            w.Server.TickPassingGreetingsForTest();
            Assert.Equal("Home sweet home, Homesteader!", GreetingsTo(w, w.Player)[1].Text);

            // A stranger: the authored hello, and no request for them (E14).
            w.Player.State.Position = new Vector3f(spot.X + 30f, w.Feet, spot.Z);
            var visitor = w.Server.AddLocalPlayer("Visitor");
            visitor.State.AboardShip = false;
            visitor.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
            int asked = provider.Requests.Count;
            w.Server.TickPassingGreetingsForTest();
            Assert.Contains(Assert.Single(GreetingsTo(w, visitor)).Text, Lines("npc.hello.stranger.", 3));
            Assert.Equal(asked, provider.Requests.Count);
        }
    }
}
