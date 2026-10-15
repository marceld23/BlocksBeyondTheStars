// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2599 — the world remembers what players did: clearing a bandit camp (by weapon: the attacker; by fire: the nearest
/// player within 64 m), defeating a giant, a first landing on an inhabited world go into the deed log (16 per world); the
/// villagers who know the hero are glad about a cleared camp; and the reporter's and the innkeeper's news merge the
/// place's interviews and deeds, newest four first.
/// </summary>
public sealed class DeedNewsTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Testville";

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off; // no machine wanders into the test
            configure?.Invoke(c);
        });
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        return w;
    }

    private static string Me(NpcLifeWorld w) => w.Player.State.PlayerId;

    [Fact]
    public void ClearingACamp_ByWeapon_RecordsTheKillersDeed_AndTheVillagersWhoKnowThemAreGlad()
    {
        using var w = World("deed_camp_weapon");
        int friend = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 6.5f, w.Feet, NpcLifeWorld.Cz + 6.5f));
        int stranger = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 8.5f, w.Feet, NpcLifeWorld.Cz + 6.5f));
        string friendKey = w.Server.NpcKeyOfNpcForTest(Me(w), friend)!;
        string strangerKey = w.Server.NpcKeyOfNpcForTest(Me(w), stranger)!;
        w.Server.AddStandingForTest(Me(w), friendKey, 10);
        int known = w.Server.RelationshipValueForTest(Me(w), friendKey); // 10 + the first meeting

        string camp = w.Server.SpawnBanditCampForTest(new Vector3f(NpcLifeWorld.Cx + 10.5f, w.Feet, NpcLifeWorld.Cz - 8.5f), guards: 1);
        var guard = Assert.Single(w.Server.Bandits);
        guard.Hull = 1f;
        for (int i = 0; i < 10 && w.Server.Bandits.Count > 0; i++)
        {
            w.Player.State.Position = guard.Position;
            w.Server.AttackEntity(Me(w), guard.Id);
            if (w.Server.Bandits.Count > 0)
            {
                w.Server.TickForTest(MeleeRules.FistCooldownSeconds);
            }
        }

        Assert.True(w.Server.BanditCampClearedForTest(camp));
        var deed = Assert.Single(w.Server.DeedsForTest);
        Assert.Equal(DeedKinds.CampCleared, deed.Kind);
        Assert.Equal(NpcLifeWorld.Owner, deed.PlayerName);
        Assert.Equal(Village, deed.Settlement);
        Assert.Equal(camp, deed.Arg);

        Assert.Equal(known + 2, w.Server.RelationshipValueForTest(Me(w), friendKey)); // knew the hero: +2
        Assert.Equal(0, w.Server.RelationshipValueForTest(Me(w), strangerKey));    // a stranger stays a stranger
    }

    [Fact]
    public void ACampBurnedOut_CreditsTheNearestPlayerWithin64m_AndNobodyFarAway()
    {
        using var w = World("deed_camp_fire");
        var lava = w.Block("lava");
        var center = new Vector3f(NpcLifeWorld.Cx + 10.5f, w.Feet, NpcLifeWorld.Cz - 8.5f);
        w.Server.SpawnBanditCampForTest(center, guards: 1);
        var guard = Assert.Single(w.Server.Bandits);
        var cell = guard.Position.ToBlock();
        w.Server.World.SetBlock(new Vector3i(cell.X, cell.Y, cell.Z), lava);
        var at = guard.Position;

        for (int i = 0; i < 60 && w.Server.Bandits.Count > 0; i++)
        {
            guard.Position = at; // it does not get to walk out of the fire in this test
            w.Server.TickBurningForTest(0.5f);
        }

        Assert.Empty(w.Server.Bandits);
        var deed = Assert.Single(w.Server.DeedsForTest);
        Assert.Equal(DeedKinds.CampCleared, deed.Kind);
        Assert.Equal(NpcLifeWorld.Owner, deed.PlayerName); // the player standing 20 m away saw it burn
    }

    [Fact]
    public void ACampBurnedOut_WithNobodyNear_IsNoOnesDeed()
    {
        using var w = World("deed_camp_fire_alone");
        var center = new Vector3f(NpcLifeWorld.Cx + 10.5f, w.Feet, NpcLifeWorld.Cz - 8.5f);
        w.Server.SpawnBanditCampForTest(center, guards: 1);
        var guard = Assert.Single(w.Server.Bandits);
        var cell = guard.Position.ToBlock();
        w.Server.World.SetBlock(new Vector3i(cell.X, cell.Y, cell.Z), w.Block("lava"));
        var at = guard.Position;
        w.Player.State.Position = new Vector3f(at.X + 100f, at.Y, at.Z); // 100 m away

        for (int i = 0; i < 60 && w.Server.Bandits.Count > 0; i++)
        {
            guard.Position = at;
            w.Server.TickBurningForTest(0.5f);
        }

        Assert.Empty(w.Server.Bandits);
        Assert.Empty(w.Server.DeedsForTest);
    }

    [Fact]
    public void AGiantDefeated_IsADeed_AndTheNewsNamesTheGiant()
    {
        using var w = World("deed_giant");
        int reporter = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), job: "reporter");

        // Far out on the plain (no village within 400 m) the whole world hears of it; next to the village, the village.
        w.Server.DefeatGiantForTest("colossus", Me(w), new Vector3f(NpcLifeWorld.Cx + 900f, w.Feet, NpcLifeWorld.Cz));
        var far = Assert.Single(w.Server.DeedsForTest);
        Assert.Equal(DeedKinds.Giant, far.Kind);
        Assert.Equal("colossus", far.Arg);
        Assert.Equal(NpcLifeWorld.Owner, far.PlayerName);
        Assert.Equal(string.Empty, far.Settlement);
        Assert.Equal(1, w.Player.State.AchievementCounters.GetValueOrDefault("defeat:colossus"));

        w.Server.DefeatGiantForTest("sandworm", Me(w), new Vector3f(NpcLifeWorld.Cx + 60f, w.Feet, NpcLifeWorld.Cz));
        Assert.Equal(Village, w.Server.DeedsForTest[^1].Settlement);

        var news = w.Server.NewsDigestForTest(Me(w), reporter);
        Assert.Equal(2, news.Count);
        Assert.Equal("• " + NpcLifeWorld.Owner + " defeated a sandworm near " + Village + "!", news[0]);
        Assert.StartsWith("• " + NpcLifeWorld.Owner + " defeated a colossus near ", news[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheNews_MergesInterviewsAndDeeds_NewestFourFirst_OnlyThisPlacesAndTheWorlds()
    {
        using var w = World("deed_news_merge");
        int reporter = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), job: "reporter");
        string place = "s:" + Village;

        w.Server.AddNewsArticleForTest(place, "Oldie", "An old story from before the deed log.", at: 0);
        w.Server.AddNewsArticleForTest(place, "Oldest", "An even older story.", at: 0);
        w.Server.SetPlaytimeForTest(50);
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, Me(w), Village, "1:1"));
        w.Server.AddNewsArticleForTest(place, "Writer", "I found a cave full of crystals!", at: 100);
        w.Server.SetPlaytimeForTest(150);
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, Me(w), "Elsewhere", "2:2")); // another village's news
        w.Server.SetPlaytimeForTest(200);
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.Landing, Me(w), string.Empty));           // the whole world's

        var news = w.Server.NewsDigestForTest(Me(w), reporter);
        Assert.Equal(4, news.Count);
        Assert.StartsWith("• Welcome! " + NpcLifeWorld.Owner + " landed on ", news[0], StringComparison.Ordinal);
        Assert.Equal("• Writer: I found a cave full of crystals!", news[1]);
        Assert.Equal("• " + NpcLifeWorld.Owner + " chased the bandits out of their camp near " + Village + "!", news[2]);
        Assert.Equal("• Oldest: An even older story.", news[3]); // old articles last, newest of them first
        Assert.DoesNotContain(news, n => n.Contains("Elsewhere", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDeedLog_KeepsTheLatest16PerWorld()
    {
        using var w = World("deed_cap");
        for (int i = 0; i < 20; i++)
        {
            w.Server.SetPlaytimeForTest(10 + i);
            Assert.True(w.Server.RecordDeedForTest(DeedKinds.Giant, Me(w), string.Empty, "sandworm"));
        }

        var log = w.Server.DeedsForTest;
        Assert.Equal(DeedRecord.MaxPerWorld, log.Count);
        Assert.Equal(5, log[0].Id);
        Assert.Equal(20, log[^1].Id);
    }

    [Fact]
    public void ManyFirstLandings_MakeRoomAmongThemselves_NotForTheCamp()
    {
        using var w = World("deed_landings");
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, Me(w), Village, "1:1"));
        for (int i = 0; i < 6; i++)
        {
            Assert.True(w.Server.RecordDeedForTest(DeedKinds.Landing, Me(w)));
        }

        var log = w.Server.DeedsForTest;
        Assert.Equal(4, log.Count(d => d.Kind == DeedKinds.Landing));
        Assert.Contains(log, d => d.Kind == DeedKinds.CampCleared);
        Assert.Equal(7, log[^1].Id);
    }

    [Fact]
    public void TheInnkeeper_TellsTheNewsToo()
    {
        using var w = World("deed_innkeeper");
        int inn = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 1.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), job: "innkeeper");

        // #2601: the village's only villager has a personal wish for the player, and a wish comes before every other
        // dialogue — "Maybe later" sets it aside for the session, after which the innkeeper's own talk opens.
        w.Server.TalkToNpcForTest(Me(w), inn);
        Assert.Equal(("wish", 0), w.Server.ActiveDialogForTest(Me(w)));
        w.Server.ChooseDialogForTest(Me(w), 1);

        // Nothing happened yet: the innkeeper says so in their own words.
        w.Server.TalkToNpcForTest(Me(w), inn);
        Assert.Equal(("innkeeper_news", 0), w.Server.ActiveDialogForTest(Me(w)));
        w.Server.ChooseDialogForTest(Me(w), 0);
        var quiet = LastDialog(w);
        Assert.Contains("Hmm, it's been quiet lately.", quiet.Text, StringComparison.Ordinal);

        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, Me(w), Village, "3:3"));
        w.Server.TalkToNpcForTest(Me(w), inn);
        w.Server.ChooseDialogForTest(Me(w), 0); // "What's the news?"
        var told = LastDialog(w);
        Assert.True(told.End);
        Assert.Contains("Oh, folks talk about all sorts of things in here:", told.Text, StringComparison.Ordinal);
        Assert.Contains("• " + NpcLifeWorld.Owner + " chased the bandits out of their camp near " + Village + "!", told.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFirstLandingOnAnInhabitedWorld_IsADeed_ButNotTheSpawnWorld()
    {
        using var w = World("deed_landing");
        var p = w.Player.State;
        string here = w.Server.World.LocationId;

        // Already "landed" here at the join (the spawn world records nothing).
        Assert.Empty(w.Server.DeedsForTest);

        // As if the player arrived from another world for the first time.
        p.LandedBodies.Clear();
        p.LandedBodies.Add("somewhere-else");
        p.Scanned.Remove("place:" + here);
        w.Server.MarkArrivedOnBodyForTest(w.Player, here);

        var deed = Assert.Single(w.Server.DeedsForTest);
        Assert.Equal(DeedKinds.Landing, deed.Kind);
        Assert.Equal(string.Empty, deed.Settlement);
        Assert.Equal(NpcLifeWorld.Owner, deed.PlayerName);
    }

    private static NpcDialogState LastDialog(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
}
