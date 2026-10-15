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
/// #2600 — villagers talk to each other: two people standing together with a player near hold a little conversation,
/// the lines alternating between them, each player reading it in their own language; they face each other; it never
/// happens at night, without a listener or during the place's rest; a player who speaks to one of them ends it; topics
/// come from the real world (weather, a bandit camp, the deed log, a known player). NO SOUND JUMBLE: a station (one
/// place) or a crowd holds one conversation at a time and rests longer.
/// </summary>
public sealed class VillageChatterTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Testville";
    private const double Day = 0.40;

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: configure);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        return w;
    }

    /// <summary>Two villagers two blocks apart, east of the player (who stands at Cx−1.5, Cz−1.5).</summary>
    private static (int A, int B) Pair(NpcLifeWorld w, string settlement = Village, float x = NpcLifeWorld.Cx + 2.5f, float z = NpcLifeWorld.Cz + 0.5f)
        => (w.Server.SpawnVillagerForTest(settlement, new Vector3f(x, w.Feet, z)),
            w.Server.SpawnVillagerForTest(settlement, new Vector3f(x + 2f, w.Feet, z)));

    private static List<NpcGreeting> ChatterTo(NpcLifeWorld w, PlayerSession player)
        => w.Transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.Kind == NpcGreetingKinds.Chatter).ToList();

    private static string NameOf(NpcLifeWorld w, int npcId) => w.Server.NpcRosterForTest().Single(n => n.Id == npcId).Name;

    /// <summary>Lets the running conversation say its next line (and close after the last).</summary>
    private static void NextLine(NpcLifeWorld w)
    {
        w.Server.AdvanceUptimeForTest(3.7);
        w.Server.TickVillageLifeForTest();
    }

    [Fact]
    public void APairWithAPlayerNear_TalksInTurns_AndEachPlayerReadsTheirOwnLanguage()
    {
        using var w = World("chat_two_languages");
        var german = w.Server.AddLocalPlayer("Zweite", "de");
        german.State.AboardShip = false;
        german.State.Position = new Vector3f(NpcLifeWorld.Cx + 3.5f, w.Feet, NpcLifeWorld.Cz - 4.5f);
        var (a, b) = Pair(w);
        w.Server.SetChatterTopicForTest("crates");

        w.Server.TickVillageLifeForTest();
        var chat = Assert.Single(w.Server.VillageChatsForTest);
        Assert.Equal("crates", chat.Topic);
        int first = chat.A, second = chat.B;
        Assert.Equal(new[] { a, b }.OrderBy(i => i), new[] { first, second }.OrderBy(i => i));

        // Line 1 by the first speaker — in English for one player, in German for the other, one bubble id for both.
        var en1 = Assert.Single(ChatterTo(w, w.Player));
        var de1 = Assert.Single(ChatterTo(w, german));
        Assert.Equal(first, en1.NpcId);
        Assert.Equal("Need help with the crates?", en1.Text);
        Assert.Equal("Brauchst du Hilfe mit den Kisten?", de1.Text);
        Assert.Equal(en1.GreetingId, de1.GreetingId);
        Assert.NotEqual(0, en1.GreetingId);
        Assert.Equal(NpcGestures.None, en1.Gesture);

        // Line 2 by the other speaker, addressing the first by name.
        NextLine(w);
        var en2 = ChatterTo(w, w.Player)[1];
        var de2 = ChatterTo(w, german)[1];
        Assert.Equal(second, en2.NpcId);
        Assert.Equal($"Yes please! Thanks, {NameOf(w, first)}!", en2.Text);
        Assert.Equal($"Ja, bitte! Danke, {NameOf(w, first)}!", de2.Text);
        Assert.NotEqual(en1.GreetingId, en2.GreetingId);

        // Then it is over: they stop facing each other, rest, and the village rests 25–45 s.
        NextLine(w);
        Assert.Empty(w.Server.VillageChatsForTest);
        Assert.Equal(0, w.Server.NpcTalkStateForTest(a)!.Value.TalkWith);
        Assert.InRange(w.Server.ChatterPlaceRestForTest("s:" + Village), 20.0, 45.0);
        Assert.Equal(2, ChatterTo(w, w.Player).Count);
    }

    [Fact]
    public void TheTwoSpeakers_FaceEachOther_WhileTheyTalk()
    {
        using var w = World("chat_facing");
        var (a, b) = Pair(w);
        w.Server.SetChatterTopicForTest("crates");
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest);

        // A few movement ticks: they would stroll or turn to the player; talking, they look at each other instead.
        for (int i = 0; i < 4; i++)
        {
            w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
            w.Server.TickForTest(0.1);
        }

        var sa = w.Server.NpcTalkStateForTest(a)!.Value;
        var sb = w.Server.NpcTalkStateForTest(b)!.Value;
        Assert.Equal(b, sa.TalkWith);
        Assert.Equal(a, sb.TalkWith);
        Assert.Equal(Math.PI / 2, sa.Facing, 2);  // b stands 2 blocks east (+X) of a
        Assert.Equal(-Math.PI / 2, sb.Facing, 2); // and a west of b
    }

    [Fact]
    public void NoConversation_WithoutAListener_AtNight_OrDuringTheRest()
    {
        using var w = World("chat_none");
        Pair(w);
        w.Server.SetChatterTopicForTest("pet");

        // Nobody within 16 m of both.
        var home = w.Player.State.Position;
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 20f, w.Feet, NpcLifeWorld.Cz - 20f);
        w.Server.TickVillageLifeForTest();
        Assert.Empty(w.Server.VillageChatsForTest);

        // At night the village sleeps.
        w.Player.State.Position = home;
        w.Server.SetLocalDayFractionForTest(0.9, NpcLifeWorld.Cx);
        w.Server.TickVillageLifeForTest();
        Assert.Empty(w.Server.VillageChatsForTest);

        // By day they talk — and right after, the village rests (and so do the two).
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest);
        NextLine(w);
        NextLine(w);
        Assert.Empty(w.Server.VillageChatsForTest);
        w.Server.SetChatterTopicForTest(null);
        w.Server.TickVillageLifeForTest();
        Assert.Empty(w.Server.VillageChatsForTest);
        Assert.Empty(ChatterTo(w, w.Player).Skip(2));
    }

    [Fact]
    public void AFourLineEveningTalk_AlternatesBetweenTheTwo()
    {
        using var w = World("chat_evening");
        Pair(w);
        w.Server.SetLocalDayFractionForTest(0.70, NpcLifeWorld.Cx); // evening, before bedtime
        w.Server.SetChatterTopicForTest("story");
        w.Server.TickVillageLifeForTest();
        var chat = Assert.Single(w.Server.VillageChatsForTest);
        for (int i = 0; i < 4; i++)
        {
            NextLine(w);
        }

        Assert.Empty(w.Server.VillageChatsForTest);
        var lines = ChatterTo(w, w.Player);
        Assert.Equal(new[] { chat.A, chat.B, chat.A, chat.B }, lines.Select(l => l.NpcId));
        Assert.Equal("Tell me a story about the stars!", lines[0].Text);
        Assert.Equal("It did — it followed the brightest star.", lines[3].Text);
    }

    [Fact]
    public void SpeakingToOneOfThem_EndsTheConversation()
    {
        using var w = World("chat_abort");
        var (a, _) = Pair(w);
        w.Server.SetChatterTopicForTest("dream"); // three lines — plenty left to interrupt
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest);

        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, a);
        Assert.Empty(w.Server.VillageChatsForTest);
        Assert.Equal(0, w.Server.NpcTalkStateForTest(a)!.Value.TalkWith);

        // No line follows the interruption.
        NextLine(w);
        Assert.Single(ChatterTo(w, w.Player));
    }

    [Fact]
    public void AStationIsOnePlace_InACrowd_OneConversation_AndALongerRest()
    {
        using var w = World("chat_station");
        // Station crew: no settlement — the whole deck is ONE place. Eight people within 15 m of the player: a crowd.
        for (int i = 0; i < 4; i++)
        {
            float z = NpcLifeWorld.Cz - 6f + i * 4f;
            Pair(w, settlement: string.Empty, x: NpcLifeWorld.Cx + 1.5f, z: z);
        }

        w.Server.SetChatterTopicForTest("crates");
        w.Server.TickVillageLifeForTest();
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest); // four pairs fit — one conversation

        NextLine(w);
        NextLine(w);
        Assert.Empty(w.Server.VillageChatsForTest);
        Assert.InRange(w.Server.ChatterPlaceRestForTest("w:" + w.Server.World.LocationId), 45.0, 75.0); // a crowd: not 25–45 s
    }

    [Fact]
    public void ABigVillage_NeverStartsTwoConversationsInEarshot()
    {
        using var w = World("chat_big");
        // Thirty people (a big village may hold two conversations at once) — but all of them near one player.
        for (int i = 0; i < 15; i++)
        {
            Pair(w, x: NpcLifeWorld.Cx + 1.5f + (i % 5) * 3f, z: NpcLifeWorld.Cz - 8f + (i / 5) * 6f);
        }

        w.Server.SetChatterTopicForTest("crates");
        w.Server.TickVillageLifeForTest();
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest);
    }

    [Fact]
    public void Topics_ComeFromTheRealWorld_Weather()
    {
        using var w = World("chat_weather");
        var (a, b) = Pair(w);
        string me = w.Player.State.PlayerId;

        w.Server.SetWeatherForTest("fog");
        var foggy = w.Server.ChatterTopicsForTest(a, b, me).Select(t => t.Topic).ToList();
        Assert.Contains("fog", foggy);
        Assert.DoesNotContain("rain", foggy);
        Assert.DoesNotContain("sunny", foggy);
        Assert.Contains("crates", foggy); // everyday topics always fit (by day)
        Assert.DoesNotContain("soup", foggy); // an evening topic

        w.Server.SetWeatherForTest("blizzard");
        var snowy = w.Server.ChatterTopicsForTest(a, b, me, evening: true).Select(t => t.Topic).ToList();
        Assert.Contains("snow", snowy);
        Assert.DoesNotContain("fog", snowy);
        Assert.Contains("soup", snowy);
        Assert.DoesNotContain("crates", snowy);
    }

    [Fact]
    public void Topics_ComeFromTheRealWorld_ABanditCamp_AKnownPlayer_AndTheDeedLog_HeroWeighsFiveTimes()
    {
        using var w = World("chat_facts", c => c.Rules.PlanetEnemies = AlienActivity.Off);
        var (a, b) = Pair(w);
        string me = w.Player.State.PlayerId;
        var other = w.Server.AddLocalPlayer("Sibling");
        other.State.AboardShip = false;
        other.State.Position = w.Player.State.Position;

        var before = w.Server.ChatterTopicsForTest(a, b, me).Select(t => t.Topic).ToList();
        Assert.DoesNotContain("camp_near", before);
        Assert.DoesNotContain("player_known", before);
        Assert.DoesNotContain("deed_camp", before);

        w.Server.SpawnBanditCampForTest(new Vector3f(NpcLifeWorld.Cx + 120f, w.Feet, NpcLifeWorld.Cz), guards: 1);
        w.Server.AddStandingForTest(me, w.Server.NpcKeyOfNpcForTest(me, a)!, 15); // the first speaker knows the player
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, me, Village));

        var mine = w.Server.ChatterTopicsForTest(a, b, me);
        Assert.Contains(mine, t => t.Topic == "camp_near");
        Assert.Contains(mine, t => t.Topic == "player_known");
        Assert.Equal(4 * 5, mine.Single(t => t.Topic == "deed_camp").Weight); // the hero listens: ×5

        var theirs = w.Server.ChatterTopicsForTest(a, b, other.State.PlayerId);
        Assert.Equal(4, theirs.Single(t => t.Topic == "deed_camp").Weight); // someone else listens
        Assert.DoesNotContain(theirs, t => t.Topic == "player_known");      // nobody knows the sibling yet

        // A deed of another village is not this village's talk; a world-wide one is.
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.Giant, me, "Elsewhere", "colossus"));
        Assert.DoesNotContain(w.Server.ChatterTopicsForTest(a, b, me), t => t.Topic == "deed_giant");
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.Giant, me, string.Empty, "colossus"));
        Assert.Contains(w.Server.ChatterTopicsForTest(a, b, me), t => t.Topic == "deed_giant");
    }

    [Fact]
    public void ADeedConversation_NamesTheHero_AndIsToldOncePerVillage()
    {
        using var w = World("chat_deed");
        Pair(w);
        string me = w.Player.State.PlayerId;
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, me, Village));
        w.Server.SetChatterTopicForTest("deed_camp");

        w.Server.TickVillageLifeForTest();
        Assert.Equal("Did you hear? " + NpcLifeWorld.Owner + " chased off the bandits!", Assert.Single(ChatterTo(w, w.Player)).Text);
        NextLine(w);
        NextLine(w);

        // The same deed is not told again in this village (this run).
        w.Server.ClearChatterRestForTest();
        w.Server.TickVillageLifeForTest();
        Assert.Empty(w.Server.VillageChatsForTest);
    }
}
