// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2604 — the village festival: finishing a settlement's "settlement needs" chain plans a festival that is persisted and
/// waits (without a deadline) for its hero in the late afternoon; then the villagers gather in a ring around the square
/// (the routine's festival phase), cheer one at a time, a bell tune plays as positional sound notes, the quartermaster
/// hands over the present once (a full bag: at the next talk), every attendee likes the hero more, and afterwards the
/// routine takes everyone back. Held once per settlement — a second helper gets the thanks and the present only. With AI
/// on, the cheers and the thanks are asked for when the festival is planned, in the hero's language.
/// </summary>
public sealed class VillageFestivalTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Testville";
    private const double Day = 0.40;
    private const double LateAfternoon = 0.65;

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            configure?.Invoke(c);
        });
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        return w;
    }

    /// <summary>The quartermaster (east of the player) and four villagers around the square.</summary>
    private static (int Quartermaster, int[] Villagers) Village5(NpcLifeWorld w)
    {
        int qm = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 3.5f, w.Feet, NpcLifeWorld.Cz - 2.5f), "quartermaster", "quartermaster");
        var villagers = new[]
        {
            w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 8.5f, w.Feet, NpcLifeWorld.Cz + 6.5f)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx - 7.5f, w.Feet, NpcLifeWorld.Cz + 9.5f)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx - 9.5f, w.Feet, NpcLifeWorld.Cz - 8.5f)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 1.5f, w.Feet, NpcLifeWorld.Cz + 12.5f)),
        };
        return (qm, villagers);
    }

    /// <summary>The player turned in all four steps of the settlement's chain — the last turn-in's bookkeeping runs.</summary>
    private static void FinishChain(SvGameServer server, PlayerSession hero)
    {
        string place = server.SettlementLocationKeyForTest(Village);
        foreach (var id in new[] { "chain_needs_1", "chain_needs_2", "chain_needs_3", "chain_needs_4b_travel" })
        {
            hero.State.Missions.Add(new MissionProgress
            {
                MissionId = id,
                ChainId = "settlement_needs",
                AcceptedFrom = place,
                Status = MissionStatus.TurnedIn,
            });
        }

        server.ChainStepTurnedInForTest(hero, server.MissionDefForTest("chain_needs_4b_travel")!, hero.State.Missions.Last());
    }

    /// <summary>The late afternoon at the pad, then one festival pass.</summary>
    private static void Evening(NpcLifeWorld w)
    {
        w.Server.SetLocalDayFractionForTest(LateAfternoon, NpcLifeWorld.Cx);
        w.Server.TickVillageFestivalsForTest();
    }

    private static List<NpcGreeting> Bubbles(NpcLifeWorld w, PlayerSession player, byte kind)
        => w.Transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.Kind == kind).ToList();

    private static List<ShipAiLine> Vega(NpcLifeWorld w, string key)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
            .Where(l => l.LineKey == key).ToList();

    private static double YawTo(Vector3f from, Vector3f to) => Math.Atan2(to.X - from.X, to.Z - from.Z);

    private static double AngleGap(double a, double b)
    {
        double d = Math.Abs(a - b) % (2 * Math.PI);
        return d > Math.PI ? 2 * Math.PI - d : d;
    }

    // ---------------- Planned ----------------

    [Fact]
    public void FinishingTheChain_PlansAFestival_TheQuartermasterAndVegaSaySo_AndADeedIsLogged()
    {
        using var w = World("fest_planned");
        var (qm, _) = Village5(w);

        FinishChain(w.Server, w.Player);

        Assert.Equal(("pending", w.Player.State.PlayerId, NpcLifeWorld.Owner, false), w.Server.FestivalForTest(Village));
        Assert.Contains(w.Server.DeedsForTest, d => d.Kind == DeedKinds.SettlementHelped && d.PlayerName == NpcLifeWorld.Owner && d.Settlement == Village);

        // The quartermaster answers the turn-in in person (an addressed line with the cheer gesture) …
        var planned = Assert.Single(Bubbles(w, w.Player, NpcGreetingKinds.Addressed), g => g.NpcId == qm);
        Assert.Equal($"You did it, {NpcLifeWorld.Owner}! When evening comes, we'll throw a party for you!", planned.Text);
        Assert.Equal(NpcGestures.Cheer, planned.Gesture);
        Assert.NotEqual(0, planned.GreetingId);

        // … VEGA repeats it, and the villagers' "festival_pending" chatter names the hero while it waits.
        var vega = Assert.Single(Vega(w, "vega.festival.planned"));
        Assert.Equal(Village, vega.LineArg);
        int a = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx - 4.5f, w.Feet, NpcLifeWorld.Cz - 4.5f));
        int b = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx - 2.5f, w.Feet, NpcLifeWorld.Cz - 4.5f));
        Assert.Contains(w.Server.ChatterTopicsForTest(a, b, w.Player.State.PlayerId), t => t.Topic == "festival_pending");
    }

    [Fact]
    public void APlannedFestival_SurvivesARestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_festival_" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfig
        {
            WorldName = "fest_restart",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
        };
        try
        {
            string heroId;
            using (var repo = new SqliteWorldRepository(new SaveGamePaths(root, "fest_restart")))
            {
                var server = new SvGameServer(config, Content.Value, new RecordingTransport(), repo);
                server.Start();
                var hero = server.AddLocalPlayer("Hero");
                hero.State.AboardShip = false;
                // The village lies far from the hero, so the festival cannot start on the spot.
                var at = hero.State.Position.ToBlock();
                server.RegisterTestSettlementForTest(Village, new Vector3i(at.X + 400, at.Y, at.Z + 400), new Vector3i(at.X + 440, at.Y + 10, at.Z + 440));
                FinishChain(server, hero);
                heroId = hero.State.PlayerId;
                Assert.Equal(("pending", heroId, "Hero", false), server.FestivalForTest(Village));
                server.Stop();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using (var repo = new SqliteWorldRepository(new SaveGamePaths(root, "fest_restart")))
            {
                var reloaded = new SvGameServer(config, Content.Value, new RecordingTransport(), repo);
                reloaded.Start();
                Assert.Equal(("pending", heroId, "Hero", false), reloaded.FestivalForTest(Village));
                reloaded.Stop();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    // ---------------- Waits for the hero and the late afternoon ----------------

    [Fact]
    public void TheFestival_StartsOnlyWithItsHeroThere_InTheLateAfternoon()
    {
        using var w = World("fest_waits");
        Village5(w);
        FinishChain(w.Server, w.Player);
        var home = w.Player.State.Position;

        // The hero far away in the late afternoon: it waits.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 90f, w.Feet, NpcLifeWorld.Cz);
        Evening(w);
        Assert.Equal("pending", w.Server.FestivalForTest(Village)!.Value.State);

        // There, but at midday: it waits (no deadline — hours of world time change nothing).
        w.Player.State.Position = home;
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        w.Server.AdvanceUptimeForTest(3600);
        w.Server.TickVillageFestivalsForTest();
        Assert.Equal("pending", w.Server.FestivalForTest(Village)!.Value.State);

        // Someone else standing there does not count.
        var other = w.Server.AddLocalPlayer("Neighbour");
        other.State.AboardShip = false;
        other.State.Position = home;
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 90f, w.Feet, NpcLifeWorld.Cz);
        Evening(w);
        Assert.Equal("pending", w.Server.FestivalForTest(Village)!.Value.State);

        // The hero there in the late afternoon: the party starts, and is held from now on.
        w.Player.State.Position = home;
        Evening(w);
        Assert.Equal(("held", w.Player.State.PlayerId, NpcLifeWorld.Owner, true), w.Server.FestivalForTest(Village));
        Assert.Contains(w.Server.DeedsForTest, d => d.Kind == DeedKinds.Festival && d.PlayerName == NpcLifeWorld.Owner);
        Assert.Single(Vega(w, "vega.festival.planned"));
    }

    [Fact]
    public void AHeroAlreadyThereInTheLateAfternoon_GetsThePartyRightAway()
    {
        using var w = World("fest_right_away");
        Village5(w);
        w.Server.SetLocalDayFractionForTest(LateAfternoon, NpcLifeWorld.Cx);

        FinishChain(w.Server, w.Player);

        Assert.Equal(("held", w.Player.State.PlayerId, NpcLifeWorld.Owner, true), w.Server.FestivalForTest(Village));
        Assert.Empty(Vega(w, "vega.festival.planned")); // no "it starts this evening" when it already does
    }

    // ---------------- The ring ----------------

    [Fact]
    public void Attendees_TakeTheFestivalPhase_WalkIntoTheRing_AndFaceTheSquare()
    {
        using var w = World("fest_ring");
        var (qm, villagers) = Village5(w);
        int farAway = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 80.5f, w.Feet, NpcLifeWorld.Cz + 0.5f));
        int stranger = w.Server.SpawnVillagerForTest("Elsewhere", new Vector3f(NpcLifeWorld.Cx + 5.5f, w.Feet, NpcLifeWorld.Cz + 5.5f));
        FinishChain(w.Server, w.Player);
        Evening(w);

        var attendees = villagers.Append(qm).ToArray();
        var anchor = w.Server.FestivalPlaceForTest(qm)!.Value.Anchor;
        Assert.Equal(NpcLifeWorld.Cx + 0.5f, anchor.X, 2);
        Assert.Equal(NpcLifeWorld.Cz + 0.5f, anchor.Z, 2);
        Assert.Null(w.Server.FestivalPlaceForTest(farAway)); // beyond 64 m
        Assert.Null(w.Server.FestivalPlaceForTest(stranger)); // another settlement's
        var spots = new HashSet<(float, float)>();
        foreach (int id in attendees)
        {
            Assert.True(w.Server.NpcInFestivalPhaseForTest(id));
            var spot = w.Server.FestivalPlaceForTest(id)!.Value.Spot;
            double r = Math.Sqrt(Math.Pow(spot.X - anchor.X, 2) + Math.Pow(spot.Z - anchor.Z, 2));
            Assert.InRange(r, 2.0, 4.0); // five people: radius clamp(1.5 + 1.25, 3, 6) = 3
            Assert.True(spots.Add((spot.X, spot.Z)), "two attendees on one spot");
        }

        // They walk there and stand looking at the middle, "celebrating" on the nameplate.
        w.TickAt(LateAfternoon, 30, every: () => w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, w.Feet, NpcLifeWorld.Cz - 1.5f));
        foreach (int id in attendees)
        {
            var spot = w.Server.FestivalPlaceForTest(id)!.Value.Spot;
            var (_, activity, pos, _) = w.Server.NpcRoutineForTest(id);
            Assert.Equal("npc.activity.celebrating", activity);
            Assert.True(Math.Abs(pos.X - spot.X) < 0.7f && Math.Abs(pos.Z - spot.Z) < 0.7f, $"NPC {id} at {pos}, its spot {spot}");
            double facing = w.Server.NpcTalkStateForTest(id)!.Value.Facing;
            Assert.True(AngleGap(facing, YawTo(pos, anchor)) < 0.2, $"NPC {id} looks at {facing}, the square is at {YawTo(pos, anchor)}");
        }

        Assert.False(w.Server.NpcInFestivalPhaseForTest(stranger));
    }

    [Fact]
    public void AfterTheParty_EveryoneReturnsToTheirRoutine()
    {
        using var w = World("fest_after");
        var (qm, villagers) = Village5(w);
        FinishChain(w.Server, w.Player);
        Evening(w);
        w.TickAt(LateAfternoon, 10);
        Assert.True(w.Server.FestivalForTest(Village)!.Value.Running);

        w.Server.AdvanceUptimeForTest(181);
        w.Server.TickVillageFestivalsForTest();
        Assert.Equal(("held", w.Player.State.PlayerId, NpcLifeWorld.Owner, false), w.Server.FestivalForTest(Village));

        w.TickAt(LateAfternoon, 5);
        foreach (int id in villagers.Append(qm))
        {
            Assert.False(w.Server.NpcInFestivalPhaseForTest(id));
            Assert.Null(w.Server.FestivalPlaceForTest(id));
            Assert.NotEqual("npc.activity.celebrating", w.Server.NpcRoutineForTest(id).Activity);
        }

        // Held once: the next late afternoon brings no second party.
        w.Server.AdvanceUptimeForTest(600);
        Evening(w);
        Assert.False(w.Server.FestivalForTest(Village)!.Value.Running);
    }

    // ---------------- Cheers and the tune ----------------

    [Fact]
    public void Cheers_ComeOneAtATime_WithTheCheerGesture_AndNameTheHero()
    {
        using var w = World("fest_cheers");
        var (qm, villagers) = Village5(w);
        FinishChain(w.Server, w.Player);
        Evening(w);
        Assert.Empty(Bubbles(w, w.Player, NpcGreetingKinds.Cheer)); // the quartermaster speaks first

        for (int i = 0; i < 30; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        var cheers = Bubbles(w, w.Player, NpcGreetingKinds.Cheer);
        Assert.InRange(cheers.Count, 5, 7); // one every 5 s — NO SOUND JUMBLE
        Assert.All(cheers, c => Assert.Equal(NpcGestures.Cheer, c.Gesture));
        Assert.All(cheers, c => Assert.Contains(c.NpcId, villagers.Append(qm)));
        Assert.Equal($"Hooray for {NpcLifeWorld.Owner}!", cheers[0].Text);
        Assert.Equal($"Three cheers for {NpcLifeWorld.Owner}!", cheers[1].Text);
        Assert.Equal(cheers.Count, cheers.Select(c => c.GreetingId).Distinct().Count());
        for (int i = 1; i < cheers.Count; i++)
        {
            Assert.NotEqual(cheers[i - 1].NpcId, cheers[i].NpcId); // never the same voice twice in a row
        }
    }

    [Fact]
    public void TheTune_PlaysAsBellNotesAtTheSquare_AtTheStartAndAgainLater()
    {
        using var w = World("fest_tune");
        var (qm, _) = Village5(w);
        FinishChain(w.Server, w.Player);
        Evening(w);
        var anchor = w.Server.FestivalPlaceForTest(qm)!.Value.Anchor;

        List<SoundFx> Notes() => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<SoundFx>()
            .Where(f => f.SoundId.StartsWith("note_", StringComparison.Ordinal)).ToList();

        for (int i = 0; i < 4; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        var tune = Notes();
        Assert.Equal(new[] { "note_1_0", "note_1_2", "note_1_4", "note_1_7", "note_1_4", "note_1_2", "note_1_4", "note_1_7" },
            tune.Select(n => n.SoundId));
        Assert.All(tune, n => Assert.True(Math.Abs(n.X - anchor.X) < 0.01f && Math.Abs(n.Z - anchor.Z) < 0.01f));

        // Every 45 s again.
        for (int i = 0; i < 46; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        Assert.Equal(16, Notes().Count);
    }

    // ---------------- The present and the friendship ----------------

    [Fact]
    public void TheQuartermaster_HandsOverThePresentOnce_AndEveryAttendeeLikesTheHeroMore()
    {
        using var w = World("fest_gift");
        var (qm, villagers) = Village5(w);
        var inv = w.Player.State.Inventory;
        int lanterns = inv.CountOf("lantern"), pots = inv.CountOf("flower_pot"), gold = inv.CountOf("gold_ingot");
        string id = w.Player.State.PlayerId;
        var keys = villagers.Append(qm).Select(n => w.Server.NpcKeyOfNpcForTest(id, n)!).ToList();
        var before = keys.Select(k => w.Server.RelationshipValueForTest(id, k)).ToList();

        FinishChain(w.Server, w.Player);
        Evening(w);

        Assert.Equal(lanterns + 6, inv.CountOf("lantern"));
        Assert.Equal(pots + 2, inv.CountOf("flower_pot"));
        Assert.Equal(gold + 1, inv.CountOf("gold_ingot"));
        var thanks = Assert.Single(Bubbles(w, w.Player, NpcGreetingKinds.Welcome), g => g.NpcId == qm);
        Assert.StartsWith($"Thank you for everything, {NpcLifeWorld.Owner}! A little gift from all of us:\n6× ", thanks.Text, StringComparison.Ordinal);
        Assert.Contains("1× ", thanks.Text, StringComparison.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            Assert.True(w.Server.RelationshipValueForTest(id, keys[i]) >= before[i] + 6, $"{keys[i]}: {before[i]} → {w.Server.RelationshipValueForTest(id, keys[i])}");
        }

        // VEGA's line follows a moment later (the quartermaster is heard first).
        Assert.Empty(Vega(w, "vega.festival.started"));
        for (int i = 0; i < 7; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        Assert.Equal(Village, Assert.Single(Vega(w, "vega.festival.started")).LineArg);

        // Once: the chain's bookkeeping again (a replay) brings neither a present nor a festival.
        FinishChain(w.Server, w.Player);
        Assert.Equal(lanterns + 6, inv.CountOf("lantern"));
        Assert.Equal(1, w.Server.FestivalCountForTest);
    }

    [Fact]
    public void AFullBag_KeepsThePresent_UntilTheNextTalkToTheQuartermaster()
    {
        using var w = World("fest_full_bag");
        var (qm, _) = Village5(w);
        var inv = w.Player.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, new ItemStack("stone", 1024)); // a bag that fits nothing more
        }

        FinishChain(w.Server, w.Player);
        Evening(w);
        Assert.Equal(0, inv.CountOf("lantern"));
        var full = Assert.Single(Bubbles(w, w.Player, NpcGreetingKinds.Welcome), g => g.NpcId == qm);
        Assert.StartsWith($"Your bag is full, {NpcLifeWorld.Owner}!", full.Text, StringComparison.Ordinal);

        // Still full: a talk is an ordinary talk, the present keeps waiting.
        var qmPos = w.Server.NpcRoutineForTest(qm).Pos;
        w.Player.State.Position = new Vector3f(qmPos.X + 1f, qmPos.Y, qmPos.Z);
        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, qm);
        Assert.Equal(0, inv.CountOf("lantern"));

        // Room again → the next talk hands it over, as a direct reply.
        inv.SetSlot(0, null);
        inv.SetSlot(1, null);
        inv.SetSlot(2, null);
        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, qm);
        Assert.Equal(6, inv.CountOf("lantern"));
        Assert.Equal(1, inv.CountOf("gold_ingot"));
        Assert.Contains(Bubbles(w, w.Player, NpcGreetingKinds.Addressed),
            g => g.NpcId == qm && g.Text.StartsWith($"There you are, {NpcLifeWorld.Owner}! Here is your festival gift:\n", StringComparison.Ordinal));

        // And never twice.
        inv.SetSlot(3, null);
        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, qm);
        Assert.Equal(6, inv.CountOf("lantern"));
    }

    [Fact]
    public void ASecondHero_GetsTheThanksAndThePresent_ButNoSecondFestival()
    {
        using var w = World("fest_second_hero");
        var (qm, _) = Village5(w);
        FinishChain(w.Server, w.Player);

        var second = w.Server.AddLocalPlayer("Zweite", "de");
        second.State.AboardShip = false;
        second.State.Position = new Vector3f(NpcLifeWorld.Cx + 1.5f, w.Feet, NpcLifeWorld.Cz - 3.5f);
        int lanterns = second.State.Inventory.CountOf("lantern");
        FinishChain(w.Server, second);

        Assert.Equal(lanterns + 6, second.State.Inventory.CountOf("lantern"));
        Assert.Contains(Bubbles(w, second, NpcGreetingKinds.Addressed),
            g => g.NpcId == qm && g.Text.StartsWith("Du hast uns auch geholfen, Zweite! Danke — das ist für dich:\n", StringComparison.Ordinal));
        Assert.Equal(1, w.Server.FestivalCountForTest);
        Assert.Equal(("pending", w.Player.State.PlayerId, NpcLifeWorld.Owner, false), w.Server.FestivalForTest(Village));
        Assert.Equal(2, w.Server.DeedsForTest.Count(d => d.Kind == DeedKinds.SettlementHelped)); // both helped

        // The party is still the first hero's — and the second one already has their present.
        Evening(w);
        Assert.True(w.Server.FestivalForTest(Village)!.Value.Running);
        Assert.Equal(lanterns + 6, second.State.Inventory.CountOf("lantern"));
    }

    // ---------------- AI (hosted worlds) ----------------

    private static string[]? FestivalAnswers(NpcLinesRequest r) => r.Purpose switch
    {
        NpcLinePurposes.Cheer => new[] { "Yay, {player}!", "Go, {player}, go!", "Party time!", "Woohoo!", "Hip hip hooray!", "Dance with us, {player}!" },
        NpcLinePurposes.FestivalThanks => new[] { "You saved our village, {player}! This is from everyone:" },
        _ => null,
    };

    private static NpcLifeWorld AiWorld(string name, AiLevel level, FakeNpcLines ai)
    {
        var w = World(name, c => c.AiLevel = level);
        w.Server.SetAiProviderForTest(ai);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0);
        return w;
    }

    [Fact]
    public void WithAi_PlanningAsksForTheCheersAndTheThanks_InTheHerosLanguage_AndThePartyUsesThem()
    {
        var ai = new FakeNpcLines(FestivalAnswers);
        using var w = AiWorld("fest_ai", AiLevel.TextOnly, ai);
        var (qm, _) = Village5(w);

        FinishChain(w.Server, w.Player);

        NpcLinesRequest cheer, thanks;
        lock (ai.Requests)
        {
            cheer = Assert.Single(ai.Requests, r => r.Purpose == NpcLinePurposes.Cheer);
            thanks = Assert.Single(ai.Requests, r => r.Purpose == NpcLinePurposes.FestivalThanks);
        }

        Assert.Equal("en", cheer.Language);
        Assert.Equal(6, cheer.LineCount);
        Assert.Equal(6, cheer.Authored.Count);
        Assert.Contains("{player}", cheer.Tokens);
        Assert.Contains("{player} brought the village iron ore", cheer.Facts);
        Assert.Contains("{player} built new lights for the village", cheer.Facts);
        Assert.Contains("{player} studied the animals around the village", cheer.Facts);
        Assert.Contains("{player} explored another world for the village", cheer.Facts);
        Assert.DoesNotContain(cheer.Facts, f => f.Contains(NpcLifeWorld.Owner, StringComparison.Ordinal)); // names travel as tokens
        Assert.Equal(1, thanks.LineCount);
        Assert.Equal("quartermaster", Assert.Single(thanks.Speakers).Role);
        var (cheerKey, thanksKey) = w.Server.FestivalCacheKeysForTest(w.Player.State.PlayerId, Village, "en");
        Assert.DoesNotContain(NpcLifeWorld.Owner, cheerKey, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Player.State.PlayerId, cheerKey, StringComparison.Ordinal);
        Assert.Single(w.Server.AiLinesCacheForTest(cheerKey));
        Assert.Single(w.Server.AiLinesCacheForTest(thanksKey));

        Evening(w);
        var words = Assert.Single(Bubbles(w, w.Player, NpcGreetingKinds.Welcome), g => g.NpcId == qm);
        Assert.StartsWith($"You saved our village, {NpcLifeWorld.Owner}! This is from everyone:\n6× ", words.Text, StringComparison.Ordinal);
        for (int i = 0; i < 11; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        var cheers = Bubbles(w, w.Player, NpcGreetingKinds.Cheer);
        Assert.Equal($"Yay, {NpcLifeWorld.Owner}!", cheers[0].Text);
        Assert.Equal($"Go, {NpcLifeWorld.Owner}, go!", cheers[1].Text);
    }

    [Fact]
    public void WithAiOff_NothingIsAsked_AndTheLinesAreAuthored()
    {
        var ai = new FakeNpcLines(FestivalAnswers);
        using var w = AiWorld("fest_ai_off", AiLevel.Off, ai);
        var (qm, _) = Village5(w);

        FinishChain(w.Server, w.Player);
        Evening(w);
        for (int i = 0; i < 6; i++)
        {
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickVillageFestivalsForTest();
        }

        Assert.Equal(0, ai.Calls);
        Assert.StartsWith($"Thank you for everything, {NpcLifeWorld.Owner}!",
            Assert.Single(Bubbles(w, w.Player, NpcGreetingKinds.Welcome), g => g.NpcId == qm).Text, StringComparison.Ordinal);
        Assert.Equal($"Hooray for {NpcLifeWorld.Owner}!", Bubbles(w, w.Player, NpcGreetingKinds.Cheer)[0].Text);
    }

    // ---------------- Content ----------------

    [Fact]
    public void FestivalData_ThePresentIsReal_AndEveryLineResolvesInEnglishAndGerman()
    {
        var festival = Content.Value.VillageLife.Festival;
        Assert.Equal(new[] { ("lantern", 6), ("flower_pot", 2), ("gold_ingot", 1) }, festival.Gift.Select(g => (g.Item, g.Count)));
        Assert.All(festival.Gift, g => Assert.NotNull(Content.Value.GetItem(g.Item)));
        Assert.Equal(6, festival.Cheers.Count);

        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
        {
            var cheerTokens = new List<string>();
            foreach (var key in festival.Cheers)
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                Assert.True(text!.Length <= 60, $"{lang} '{key}' is {text.Length} characters: {text}");
                Assert.All(Placeholder.Matches(text).Select(m => m.Value), t => Assert.Equal("{player}", t));
                Assert.DoesNotMatch(@"\d", Placeholder.Replace(text, string.Empty)); // numbers would be facts
                cheerTokens.AddRange(Placeholder.Matches(text).Select(m => m.Value));
            }

            Assert.Contains("{player}", cheerTokens); // the hero is cheered by name

            foreach (var key in new[] { "village.festival.planned", "village.festival.thanks", "village.festival.thanks_again",
                         "village.festival.bag_full", "village.festival.gift_later" })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                Assert.Equal(new[] { "{player}" }, Placeholder.Matches(text!).Select(m => m.Value));
            }

            foreach (var key in new[] { "vega.festival.planned", "vega.festival.started" })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                Assert.Equal(new[] { "{0}" }, Placeholder.Matches(text!).Select(m => m.Value));
            }

            Assert.True(table.TryGetValue("npc.activity.celebrating", out var activity) && activity.Length > 0, $"{lang}: missing activity");
        }
    }

    [Fact]
    public void ContentValidation_CatchesABrokenFestival()
    {
        var bad = new VillageLifeDefinition
        {
            Festival = new VillageFestivalDefinition
            {
                Gift = { new ItemAmount("unobtainium", 1), new ItemAmount("lantern", 0) },
                Cheers = { "village.festival.cheer.1", "village.festival.cheer.1", " " },
            },
        };
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetVillageLife(bad);
        var ex = Assert.Throws<ContentValidationException>(() => content.Validate());
        Assert.Contains("Village life festival gift references unknown item 'unobtainium'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("festival gift 'lantern' has a count below 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("festival cheer 'village.festival.cheer.1' is listed twice", ex.Message, StringComparison.Ordinal);
        Assert.Contains("an empty festival cheer key", ex.Message, StringComparison.Ordinal);
    }
}
