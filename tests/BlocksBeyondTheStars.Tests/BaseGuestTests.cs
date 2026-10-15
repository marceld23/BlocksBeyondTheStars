// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2602 — friendly visitors at the player's base: a neighbour (the REAL villager, hidden from the village while it
/// visits), a travelling merchant (the market opens at the base) or a lost traveller walks up to a base whose owner is at
/// home by day, greets with a wave, waits, hands a present over once per player and leaves where nobody sees it vanish.
/// No visit while the owner is away, aboard or at night, or with the world option off; one guest per base; nothing appears
/// inside the walls — an unreachable owner hears "{name} is waiting outside your door!"; the owner leaving sends the
/// guest away at once. Guests are never combat entities and come with planet enemies off. AI: the greeting and the offer
/// are prefetched at spawn and the cached lines are used.
/// </summary>
public sealed class BaseGuestTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Testville";

    /// <summary>A peaceful arena with a founded base, the sun at late morning over the base.</summary>
    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, padHalf: 48, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off; // guests are not tied to any enemy setting
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
            configure?.Invoke(c);
        });
        w.FoundBase();
        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        return w;
    }

    /// <summary>A village 36 blocks east of the core with one villager at its far side (out of the owner's sight).</summary>
    private static int VillageWithVillager(NpcLifeWorld w)
    {
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx + 28, w.PadY, NpcLifeWorld.Cz - 6), new Vector3i(NpcLifeWorld.Cx + 44, w.PadY + 10, NpcLifeWorld.Cz + 6));
        return w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 40.5f, w.Feet, NpcLifeWorld.Cz + 0.5f));
    }

    private static (int Id, string Name, string Role) Npc(NpcLifeWorld w, int id)
    {
        var n = w.Server.NpcRosterForTest().Single(x => x.Id == id);
        return (n.Id, n.Name, n.Role);
    }

    private static Vector3f PosOf(NpcLifeWorld w, int id) => w.Server.NpcSnapshots.Single(n => n.Id == id).Pos;

    /// <summary>Puts the owner right next to the guest (the walk-in is covered by its own test).</summary>
    private static void StandBeside(NpcLifeWorld w, int guestId)
    {
        var p = PosOf(w, guestId);
        w.Player.State.Position = new Vector3f(p.X + 1.5f, p.Y, p.Z);
    }

    /// <summary>Steps the guests' script second by second (the uptime moves on between the steps, like the real 1 Hz).</summary>
    private static void StepSeconds(NpcLifeWorld w, int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
            w.Server.AdvanceUptimeForTest(1.0);
            w.Server.TickBaseGuestsForTest();
        }
    }

    private static List<NpcGreeting> Greetings(NpcLifeWorld w, int npcId)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.NpcId == npcId).ToList();

    private static NpcDialogState LastDialog(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();

    private static string En(string key) => Content.Value.CreateLocalizer(GameLocale.English).Get(key);

    [Fact]
    public void ANeighbour_IsTheRealVillager_WalksOver_GreetsWithAWave_AndGoesHomeAgain()
    {
        using var w = World("guest_neighbour");
        int villager = VillageWithVillager(w);
        var real = Npc(w, villager);

        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        Assert.Equal(real.Name, Npc(w, guest).Name);
        Assert.Equal(w.Server.NpcPersonKeyForTest(villager), w.Server.NpcPersonKeyForTest(guest)); // the same person
        Assert.Equal(w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, villager), w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, guest));
        Assert.True(w.Server.NpcVisitingAwayForTest(villager));
        Assert.DoesNotContain(villager, w.Server.NpcIdsSentToForTest(w.Player)); // absent from the village meanwhile
        Assert.Contains(guest, w.Server.NpcIdsSentToForTest(w.Player));
        Assert.Contains("@srv.guest.arriving:" + real.Name, w.MessagesToPlayer());
        Assert.Empty(w.Server.Bandits); // never a combat entity

        // It spawned on the village's side, outside the base, and walks in with the NPC pathfinding.
        var spawn = PosOf(w, guest);
        Assert.True(spawn.X > NpcLifeWorld.Cx + 20, $"spawned at {spawn.X} — not on the village's side");
        w.TickAt(0.4, 150, until: () => w.Server.BaseGuestsForTest.Single().Greeted);
        var g = w.Server.BaseGuestsForTest.Single();
        Assert.True(g.Greeted, "the neighbour never reached the owner");
        Assert.False(g.Outside);

        var hello = Assert.Single(Greetings(w, guest));
        Assert.Equal(NpcGreetingKinds.Guest, hello.Kind);
        Assert.Equal(NpcGestures.Wave, hello.Gesture);
        Assert.True(hello.GreetingId > 0);
        Assert.Contains(Village, hello.Text);
        Assert.Contains(w.Player.State.Name, hello.Text);

        // Time to go home: it walks back out and vanishes where nobody sees it — the villager is back in the village.
        w.Server.EndBaseGuestWaitsForTest();
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 40f, w.Feet, NpcLifeWorld.Cz - 40f);
        w.TickAt(0.4, 200, until: () => w.Server.BaseGuestsForTest.Count == 0);
        Assert.Empty(w.Server.BaseGuestsForTest);
        Assert.DoesNotContain(w.Server.NpcRosterForTest(), n => n.Id == guest);
        Assert.False(w.Server.NpcVisitingAwayForTest(villager));
        Assert.Contains(villager, w.Server.NpcIdsSentToForTest(w.Player));
    }

    [Fact]
    public void ThePresent_IsGivenOncePerPlayer_ThenTheGuestHeadsHome()
    {
        using var w = World("guest_gift");
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "traveler"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        Assert.True(w.Server.BaseGuestsForTest.Single().Greeted);
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);

        string pid = w.Player.State.PlayerId;
        int before = w.Player.State.Inventory.CountOf("torch");
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(("guest_traveler", 0), w.Server.ActiveDialogForTest(pid));
        w.Server.ChooseDialogForTest(pid, 0); // "Yes — it's that way!" → torches
        Assert.Equal(before + 4, w.Player.State.Inventory.CountOf("torch"));
        Assert.StartsWith(En("dlg.guest_traveler.r_way"), LastDialog(w).Text);

        w.Server.TalkToNpcForTest(pid, guest);
        w.Server.ChooseDialogForTest(pid, 0); // asked again: nothing more to give
        Assert.Equal(before + 4, w.Player.State.Inventory.CountOf("torch"));
        Assert.Equal(En("dlg.guest.gift_given"), LastDialog(w).Text);

        // A stranger is nobody to remember: no "guest:" entry in the save.
        Assert.DoesNotContain(w.Player.State.NpcMemory.Keys, k => k.StartsWith("guest:", StringComparison.Ordinal));

        // The present given, the traveller is on its way soon — even with the player standing right beside it.
        StepSeconds(w, 32);
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
    }

    [Fact]
    public void NoVisit_WhileTheOwnerIsAway_Aboard_OrItIsNight_AndOnlyOneGuestPerBase()
    {
        using var w = World("guest_gates");
        var home = w.Player.State.Position;

        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 30f, w.Feet, NpcLifeWorld.Cz);
        Assert.Equal(0, w.Server.TryBaseGuestVisitForTest()); // away from the base

        w.Player.State.Position = home;
        w.Player.State.AboardShip = true;
        Assert.Equal(0, w.Server.TryBaseGuestVisitForTest()); // aboard
        w.Player.State.AboardShip = false;

        w.Server.SetLocalDayFractionForTest(0.9, NpcLifeWorld.Cx);
        Assert.Equal(0, w.Server.TryBaseGuestVisitForTest()); // night
        w.Server.SetLocalDayFractionForTest(0.7, NpcLifeWorld.Cx);
        Assert.Equal(0, w.Server.TryBaseGuestVisitForTest()); // evening: a guest would arrive at bedtime

        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.Equal(1, w.Server.TryBaseGuestVisitForTest()); // home, on foot, by day — someone comes
        Assert.Equal(1, w.Server.TryBaseGuestVisitForTest()); // and nobody else while they are here
        Assert.Null(w.Server.SpawnBaseGuestForTest(w.BaseId, "merchant"));
        Assert.Empty(w.Server.Bandits);
    }

    [Fact]
    public void TheWorldOption_IsOnByDefault_InEveryPreset_AndTheAdminSwitchesItOff()
    {
        Assert.True(new GameRules().FriendlyVisitors);
        foreach (string preset in ServerPresets.Names)
        {
            Assert.True(ServerPresets.Get(preset)!.FriendlyVisitors, $"preset '{preset}' has no friendly visitors");
        }

        // An old save's rules know nothing of the option: it reads ON.
        Assert.True(JsonSerializer.Deserialize<GameRules>("{\"BaseVisitors\":true}")!.FriendlyVisitors);
        Assert.Equal(string.Empty, new ServerRules().FriendlyVisitors); // an older server: the client hides the row

        using var w = World("guest_option");
        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { FriendlyVisitors = "Off" }));
        var rules = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ServerRules>().Last();
        Assert.Equal("Off", rules.FriendlyVisitors);
        Assert.Equal(0, w.Server.TryBaseGuestVisitForTest());
        Assert.Null(w.Server.SpawnBaseGuestForTest(w.BaseId, "traveler"));

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { FriendlyVisitors = "On" }));
        Assert.Equal("On", w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ServerRules>().Last().FriendlyVisitors);
        Assert.Equal(1, w.Server.TryBaseGuestVisitForTest());

        // Switching it off sends the guest already there home.
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { FriendlyVisitors = "Off" }));
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
    }

    [Fact]
    public void NothingAppearsInsideTheWalls_AndAnUnreachableOwner_HearsTheGuestWaitsOutside()
    {
        using var w = World("guest_walls");
        const int ring = 12;
        w.WallRing(ring); // a closed yard, no door
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "traveler"));
        string name = Npc(w, guest).Name;

        bool insideEver = false;
        w.TickAt(0.4, 90, until: () => w.Server.BaseGuestsForTest.Single().Outside, every: () =>
        {
            var p = PosOf(w, guest);
            insideEver |= Math.Max(Math.Abs(p.X - NpcLifeWorld.Cx), Math.Abs(p.Z - NpcLifeWorld.Cz)) <= ring;
        });

        Assert.True(w.Server.BaseGuestsForTest.Single().Outside, "the guest never gave up on the walled-in owner");
        Assert.Contains("@srv.guest.waiting:" + name, w.MessagesToPlayer());
        w.TickAt(0.4, 30, every: () =>
        {
            var p = PosOf(w, guest);
            insideEver |= Math.Max(Math.Abs(p.X - NpcLifeWorld.Cx), Math.Abs(p.Z - NpcLifeWorld.Cz)) <= ring;
        });
        Assert.False(insideEver, "the guest got inside the walls");
    }

    [Fact]
    public void TheOwnerGoingAboard_SendsTheGuestAwayAtOnce_AndTheVillagerIsHomeAgain()
    {
        using var w = World("guest_owner_leaves");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        Assert.True(w.Server.NpcVisitingAwayForTest(villager));

        w.Player.State.AboardShip = true;
        w.Server.TickBaseGuestsForTest();

        Assert.Empty(w.Server.BaseGuestsForTest);
        Assert.DoesNotContain(w.Server.NpcRosterForTest(), n => n.Id == guest);
        Assert.False(w.Server.NpcVisitingAwayForTest(villager));
    }

    [Fact]
    public void TheTravellingMerchant_OpensTheMarketAtTheBase_WithItsOwnGoods()
    {
        using var w = World("guest_merchant");
        int merchant = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "merchant"));
        Assert.Equal("vendor", Npc(w, merchant).Role); // the client's "Trade or talk?" works as at any stall
        Assert.StartsWith("guest:", w.Server.NpcKeyOfNpcForTest(w.Player.State.PlayerId, merchant));

        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 20f, w.Feet, NpcLifeWorld.Cz - 20f);
        Assert.False(w.Server.MarketAvailableForTest(w.Player.State));

        StandBeside(w, merchant);
        Assert.True(w.Server.NearBaseGuestMerchantForTest(w.Player.State));
        Assert.True(w.Server.MarketAvailableForTest(w.Player.State));
        Assert.Contains(w.Server.VendorThemeAtForTest(w.Player.State), new[] { "miners", "traders", "researchers", "settlers" });

        // Its stall stays open longer than an ordinary guest waits.
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 20f, w.Feet, NpcLifeWorld.Cz - 20f);
        StepSeconds(w, 150);
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);
        StepSeconds(w, 40);
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
    }

    [Fact]
    public void ANeighbourWithAWish_BringsItAlong_AndItLandsInTheMissionLog()
    {
        using var w = World("guest_wish");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();

        string pid = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(("guest_neighbour", 0), w.Server.ActiveDialogForTest(pid));
        w.Server.ChooseDialogForTest(pid, 1); // "Can I help you with anything?"

        var wish = Assert.Single(w.Server.HeldWishesForTest(pid));
        Assert.True(BlocksBeyondTheStars.GameServer.GameServer.IsWishMissionForTest(wish));
        Assert.StartsWith(En("dlg.guest_neighbour.r_wish"), LastDialog(w).Text);
        Assert.Contains(w.Server.WishLanesForTest(pid), l => l.Held && l.GiverId == villager);

        // Asked again, the neighbour has nothing more to wish for right now.
        w.Server.TalkToNpcForTest(pid, guest);
        w.Server.ChooseDialogForTest(pid, 1);
        Assert.Equal(En("dlg.guest_neighbour.r_nowish"), LastDialog(w).Text);
        Assert.Single(w.Server.HeldWishesForTest(pid));
    }

    [Fact]
    public void ANeighboursWish_AlreadyInTheBag_GetsTheBringItOverToast_ItIsHandedOverInTheVillage()
    {
        using var w = World("guest_wish_ready");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        Assert.Equal(0, w.Player.State.Inventory.Add("crystal", 1, w.Content.GetItem("crystal")!.MaxStack));

        string pid = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(pid, guest);
        w.Server.ChooseDialogForTest(pid, 1); // "Can I help you with anything?"
        Assert.Single(w.Server.HeldWishesForTest(pid));
        Assert.StartsWith(En("dlg.guest_neighbour.r_wish"), LastDialog(w).Text); // the neighbour's own answer stays

        w.Server.TickWishesForTest(pid);
        Assert.Contains("@srv.wish.ready:" + Npc(w, villager).Name, w.MessagesToPlayer());
    }

    [Fact]
    public void AVillagerCelebratingAtTheFestival_IsNotBorrowedForAVisit()
    {
        using var w = World("guest_festival");
        int villager = VillageWithVillager(w);
        w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 36.5f, w.Feet, NpcLifeWorld.Cz - 3.5f), "quartermaster", "quartermaster");

        // The player finished the village's chain: the festival starts in the late afternoon and the villager joins the ring.
        string place = w.Server.SettlementLocationKeyForTest(Village);
        foreach (var id in new[] { "chain_needs_1", "chain_needs_2", "chain_needs_3", "chain_needs_4b_travel" })
        {
            w.Player.State.Missions.Add(new MissionProgress { MissionId = id, ChainId = "settlement_needs", AcceptedFrom = place, Status = MissionStatus.TurnedIn });
        }

        w.Server.ChainStepTurnedInForTest(w.Player, w.Server.MissionDefForTest("chain_needs_4b_travel")!, w.Player.State.Missions.Last());
        w.Server.SetLocalDayFractionForTest(0.65, NpcLifeWorld.Cx);
        w.Server.TickVillageFestivalsForTest();
        Assert.True(w.Server.FestivalForTest(Village)!.Value.Running);
        Assert.NotNull(w.Server.FestivalPlaceForTest(villager));
        w.TickAt(0.65, 40, until: () => !w.Server.NpcHasGoalForTest(villager)); // standing at its spot in the ring
        Assert.False(w.Server.NpcHasGoalForTest(villager));

        // By day at the base: nobody from the party comes over.
        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.Null(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        Assert.False(w.Server.NpcVisitingAwayForTest(villager));

        // Once the party is over, the same villager may visit again.
        w.Server.AdvanceUptimeForTest(200.0);
        w.Server.TickVillageFestivalsForTest();
        Assert.False(w.Server.FestivalForTest(Village)!.Value.Running);
        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.NotNull(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        Assert.True(w.Server.NpcVisitingAwayForTest(villager));
    }

    /// <summary>A scripted backend: answers per purpose, records every request.</summary>
    private sealed class FakeLines : IAiMissionProvider
    {
        private readonly Func<NpcLinesRequest, string[]?> _answer;
        private int _calls;

        public FakeLines(Func<NpcLinesRequest, string[]?> answer) => _answer = answer;

        public int Calls => Volatile.Read(ref _calls);

        public List<NpcLinesRequest> Requests { get; } = new();

        public MissionPlan? Generate(string context) => null;

        public string? GenerateNpcLine(NpcLineRequest request) => null;

        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;

        public string[]? GenerateNpcLines(NpcLinesRequest request)
        {
            Interlocked.Increment(ref _calls);
            lock (Requests)
            {
                Requests.Add(request);
            }

            return _answer(request);
        }
    }

    private static string[]? GuestAnswers(NpcLinesRequest r) => r.Purpose switch
    {
        NpcLinePurposes.GuestGreet => new[] { "Hey {player}, what a nice place!" },
        NpcLinePurposes.GuestOffer => new[] { "Look what I brought along for you!" },
        _ => null,
    };

    private static NpcLifeWorld AiWorld(string name, AiLevel level, FakeLines ai)
    {
        var w = World(name, c => c.AiLevel = level);
        w.Server.SetAiProviderForTest(ai);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0);
        return w;
    }

    [Fact]
    public void AiOn_TheGreetingAndTheOffer_ArePrefetchedAtSpawn_AndTheCachedLinesAreUsed()
    {
        var ai = new FakeLines(GuestAnswers);
        using var w = AiWorld("guest_ai", AiLevel.TextOnly, ai);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "traveler"));

        // Both lines were asked for the moment the guest appeared — the walk-in covers the latency.
        Assert.Contains(ai.Requests, r => r.Purpose == NpcLinePurposes.GuestGreet && r.Language == "en");
        Assert.Contains(ai.Requests, r => r.Purpose == NpcLinePurposes.GuestOffer);
        Assert.DoesNotContain(ai.Requests, r => (r.Place + r.Situation + string.Join("|", r.Authored)).Contains(w.Player.State.Name, StringComparison.Ordinal));

        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Hey " + w.Player.State.Name + ", what a nice place!", Assert.Single(Greetings(w, guest)).Text);

        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, guest);
        Assert.Equal("Look what I brought along for you!", LastDialog(w).Text);
    }

    [Fact]
    public void AiOff_AsksNothing_AndTheAuthoredLinesAreUsed()
    {
        var ai = new FakeLines(GuestAnswers);
        using var w = AiWorld("guest_ai_off", AiLevel.Off, ai);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "merchant"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, guest);

        Assert.Equal(0, ai.Calls);
        Assert.Equal(En("guest.greet.merchant").Replace("{player}", w.Player.State.Name, StringComparison.Ordinal),
            Assert.Single(Greetings(w, guest)).Text);
        Assert.Equal(En("dlg.guest_merchant.prompt"), LastDialog(w).Text);
    }
}
