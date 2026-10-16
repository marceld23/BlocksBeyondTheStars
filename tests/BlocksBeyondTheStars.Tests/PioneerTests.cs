// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The pioneer (#2682): one player-shaped resident per outpost with a pixel skin (the look key and a seed on the wire, no
/// pixels), clearly an NPC (the "Pioneer" nameplate, a lodge person, talk but no trade), at work on the lodge job loop round
/// the outpost, away about a third of the in-game days by a deterministic schedule (gone unseen, walking off when watched,
/// home for the evening), and their talk — who they are, the outpost, a present from the crates for a friend only.
/// </summary>
public sealed class PioneerTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static string En(string key) => Content.CreateLocalizer(GameLocale.English).Get(key);

    private static string Outpost(NpcLifeWorld w, long seed = 3, string work = "hunter")
        => w.Server.SpawnPioneerOutpostForTest(NpcLifeWorld.Cx - 7, w.PadY, NpcLifeWorld.Cz - 7, seed, "stone", hidden: false, chests: 3, work: work);

    private static (int Id, string Name, string Role, string Job, string NameKey, string Look, int SkinSeed, string PersonKey, string Trip, bool Away) Pioneer(NpcLifeWorld w, string key)
        => w.Server.PioneerForTest(key)!.Value;

    /// <summary>Evening at the outpost — never an away hour — so the pioneer is home and awake.</summary>
    private static void Evening(NpcLifeWorld w, string key)
    {
        var center = w.Server.OutpostsForTest.Single(o => o.Key == key).Center;
        w.Server.SetLocalDayFractionForTest(0.7, center.X);
        w.Server.StepPioneersForTest();
    }

    private static void StandBeside(NpcLifeWorld w, int npcId)
    {
        var at = w.Server.NpcStateForTest(npcId)!.Value.Pos;
        w.Player.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);
    }

    private static NpcDialogState LastDialog(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();

    // ---- who they are --------------------------------------------------------------------------------------------

    [Fact]
    public void ThePioneer_IsAPlayerShapedNpc_WithAPixelSkinSeed_ThePioneerNameplate_AndAPersonKey()
    {
        using var w = new NpcLifeWorld(Content, "pioneer_who");
        string key = Outpost(w);
        Evening(w, key);
        var p = Pioneer(w, key);
        Assert.Equal(("settler", SvGameServer.PioneerJob, SvGameServer.PioneerNameKey, SvGameServer.PixelLook), (p.Role, p.Job, p.NameKey, p.Look));
        Assert.NotEqual(0, p.SkinSeed);
        Assert.Equal($"lodge:{w.Player.CurrentLocationId}:{key}#0", p.PersonKey); // a person like a hut's resident
        Assert.Equal("Pioneer", En(p.NameKey));
        Assert.False(string.IsNullOrWhiteSpace(p.Name));

        // The client gets the look key and the seed — never a pixel.
        StandBeside(w, p.Id);
        int mark = w.Transport.Sent.Count;
        w.TickAt(0.7, 1.0);
        var net = w.Transport.Sent.Skip(mark).Select(s => s.Msg).OfType<NpcList>().Last().Npcs.Single(n => n.Id == p.Id);
        Assert.Equal((SvGameServer.PixelLook, p.SkinSeed, "settler"), (net.Look, net.FaceVariant, net.Role));
        Assert.Equal(1f, net.Size); // a player's size

        // One per outpost, the same person every time the world loads.
        Assert.Single(w.Server.LodgeResidentsForTest(key));
        using var twin = new NpcLifeWorld(Content, "pioneer_who");
        string twinKey = Outpost(twin);
        var t = Pioneer(twin, twinKey);
        Assert.Equal((p.Name, p.SkinSeed), (t.Name, t.SkinSeed));
    }

    [Fact]
    public void ThePioneer_WorksOnTheLodgeLoop_FromTheOutpostsDoor_WithTheToolOfTheirWork_AndIsNoTrader()
    {
        using var w = new NpcLifeWorld(Content, "pioneer_work");
        string key = Outpost(w, work: "miner");
        var r = Assert.Single(w.Server.LodgeResidentsForTest(key));
        var yard = w.Server.OutpostMarkersForTest(key).Single(m => m.Type == "yard").Pos;
        Assert.True(Math.Abs(r.Work.X - yard.X) < 0.01f && Math.Abs(r.Work.Z - yard.Z) < 0.01f, "the work anchor is in front of the door");
        Assert.Equal(Content.LodgeProfession("miner")!.Held, r.Held);
        Assert.NotNull(r.Bed);
        Assert.NotNull(r.Seat);
        Assert.Contains(w.Server.NpcJobsForTest, n => n.Job == SvGameServer.PioneerJob && n.Role == "settler"); // talk, no trade
    }

    // ---- away ----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheAwaySchedule_IsDeterministic_AboutAThirdOfTheDays_AndOnlyInTheWorkingHours()
    {
        foreach (long seed in new[] { 1L, 42L, -7L, 123456789L })
        {
            int away = 0;
            for (long day = 0; day < 600; day++)
            {
                bool a = SvGameServer.PioneerAwayDay(seed, day);
                Assert.Equal(a, SvGameServer.PioneerAwayDay(seed, day)); // the same answer every time
                away += a ? 1 : 0;
                Assert.False(SvGameServer.PioneerAwayAt(seed, day, 0.1)); // never at night …
                Assert.False(SvGameServer.PioneerAwayAt(seed, day, 0.72)); // … nor in the evening
                Assert.Equal(a, SvGameServer.PioneerAwayAt(seed, day, 0.45));
            }

            Assert.InRange(away, 150, 250); // about a third of 600
        }
    }

    [Fact]
    public void OnADayOut_ThePioneerIsGoneUnseen_WalksOffWhenWatched_AndIsHomeForTheEvening()
    {
        using var w = new NpcLifeWorld(Content, "pioneer_away", padHalf: 58); // ground on the 40–55 m rings it walks in from
        string key = Outpost(w);
        Evening(w, key);
        long seed = w.Server.OutpostSeedForTest(key);
        for (int i = 0; i < 40 && !SvGameServer.PioneerAwayDay(seed, w.Server.MarketDayForTest); i++)
        {
            w.Server.AdvanceSystemDaysForTest(1.0);
        }

        Assert.True(SvGameServer.PioneerAwayDay(seed, w.Server.MarketDayForTest), "an away day within forty days");
        var center = w.Server.OutpostsForTest.Single(o => o.Key == key).Center;
        var p = Pioneer(w, key);

        // Nobody near: at work time the pioneer is simply gone — not sent, not to be talked to.
        w.Player.State.Position = new Vector3f(center.X + 90f, w.Feet, center.Z);
        w.Server.SetLocalDayFractionForTest(0.45, center.X);
        Assert.True(w.Server.PioneerAwayNowForTest(key));
        w.Server.StepPioneersForTest();
        Assert.Equal(("Away", true), (Pioneer(w, key).Trip, Pioneer(w, key).Away));
        w.Player.State.Position = new Vector3f(center.X, w.Feet, center.Z + 9f);
        Assert.DoesNotContain(p.Id, w.Server.NpcIdsSentToForTest(w.Player));

        // The evening: home again.
        w.Server.SetLocalDayFractionForTest(0.7, center.X);
        w.Server.StepPioneersForTest();
        Assert.Equal(("Returning", false), (Pioneer(w, key).Trip, Pioneer(w, key).Away)); // a player is near: walking in
        Assert.Contains(p.Id, w.Server.NpcIdsSentToForTest(w.Player));

        // Watched when the day out begins: it walks off rather than vanishing in front of the player.
        using var watched = new NpcLifeWorld(Content, "pioneer_away_watched");
        string wKey = Outpost(watched);
        Evening(watched, wKey);
        long wSeed = watched.Server.OutpostSeedForTest(wKey);
        for (int i = 0; i < 40 && !SvGameServer.PioneerAwayDay(wSeed, watched.Server.MarketDayForTest); i++)
        {
            watched.Server.AdvanceSystemDaysForTest(1.0);
        }

        var wp = Pioneer(watched, wKey);
        StandBeside(watched, wp.Id);
        var wCenter = watched.Server.OutpostsForTest.Single(o => o.Key == wKey).Center;
        watched.Server.SetLocalDayFractionForTest(0.45, wCenter.X);
        watched.Server.StepPioneersForTest();
        Assert.Equal(("Leaving", false), (Pioneer(watched, wKey).Trip, Pioneer(watched, wKey).Away));
        Assert.Equal(SvGameServer.PioneerExploringKey, watched.Server.NpcStateForTest(wp.Id)!.Value.ActivityKey);
    }

    // ---- talk ----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheirOwnTalk_WhoTheyAreAndTheOutpost_AndOnlyAFriendGetsAPresent_FromTheCrates_NeverTheCellar()
    {
        var mine = Content.Dialogs.Where(d => d.Job == SvGameServer.PioneerJob).ToList();
        Assert.Equal(new[] { "pioneer_friend", "pioneer" }, mine.Select(d => d.Key)); // the friend's talk is matched first
        Assert.All(mine, d => Assert.Equal(DialogDefinition.HomeLodge, d.Home));
        Assert.All(mine, d => Assert.True(d.AiPrompt, d.Key + " may be re-worded on hosted worlds"));
        Assert.All(mine, d => Assert.All(d.Nodes, n => Assert.InRange(n.Choices.Count, 1, 3)));
        Assert.Equal("trusted", mine[0].MinStage);
        Assert.Contains(mine[0].Nodes[0].Choices, c => c.Consequence == SvGameServer.LodgeGiftConsequence);
        Assert.DoesNotContain(mine[1].Nodes.SelectMany(n => n.Choices), c => c.Consequence == SvGameServer.LodgeGiftConsequence);
        var en = Content.CreateLocalizer(GameLocale.English);
        var de = Content.CreateLocalizer(GameLocale.German);
        foreach (string k in mine.SelectMany(d => d.Nodes).SelectMany(n => new[] { n.PromptKey }.Concat(n.Choices.SelectMany(c => new[] { c.TextKey, c.ResponseKey }))).Distinct())
        {
            Assert.NotEqual(k, en.Get(k));
            Assert.NotEqual(k, de.Get(k));
        }

        using var w = new NpcLifeWorld(Content, "pioneer_talk");
        string key = Outpost(w);
        Evening(w, key);
        var p = Pioneer(w, key);
        var o = w.Server.OutpostsForTest.Single(x => x.Key == key);
        var cellar = w.Server.Containers.Single(c => c.Id == o.CellarStoreId).Items.Select(s => (s.Item, s.Count)).ToList();

        // A stranger: who they are (a little standing), never a present.
        StandBeside(w, p.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, p.Id);
        Assert.Equal("pioneer", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0);
        Assert.StartsWith(En("dlg.pioneer.r_who"), LastDialog(w).Text, StringComparison.Ordinal);

        // A friend: the present comes out of the room's crates.
        w.Player.State.NpcMemory[p.PersonKey] = new NpcRelationship { Name = p.Name, Role = "settler", Value = 60 };
        int before = w.Player.State.Inventory.Slots.Where(s => s is { IsEmpty: false }).Sum(s => s!.Count);
        StandBeside(w, p.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, p.Id);
        Assert.Equal("pioneer_friend", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Got anything to spare?"
        Assert.StartsWith(En("dlg.pioneer.r_gift"), LastDialog(w).Text, StringComparison.Ordinal);
        int after = w.Player.State.Inventory.Slots.Where(s => s is { IsEmpty: false }).Sum(s => s!.Count);
        Assert.InRange(after - before, 1, SvGameServer.LodgeGiftMax);
        Assert.Equal(cellar, w.Server.Containers.Single(c => c.Id == o.CellarStoreId).Items.Select(s => (s.Item, s.Count)));
    }
}
