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
using BlocksBeyondTheStars.Shared.Definitions;
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

    /// <summary>"{name} is walking home to {village}." in English.</summary>
    private static string WalkingHome(string name)
        => En("srv.guest.walking_home").Replace("{name}", name, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal);

    /// <summary>"I'm heading home to {village} soon — come and see me there!" in English.</summary>
    private static string GoingHome => En("dlg.guest.going_home").Replace("{village}", Village, StringComparison.Ordinal);

    /// <summary>#2713 (Lyxette 2026-10-17: "where did they go?"): the owner going aboard used to remove the guest at once, in
    /// plain view of the ship's windows. Now it walks home — the owner reads where to — and vanishes only once nobody is
    /// near (or after the usual timeout); the villager is home again then.</summary>
    [Fact]
    public void TheOwnerGoingAboard_SendsTheGuestWalkingHome_AndItVanishesOnlyOutOfSight()
    {
        using var w = World("guest_owner_leaves");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        string name = Npc(w, guest).Name;
        w.TickAt(0.4, 150, until: () => w.Server.BaseGuestsForTest.Single().Greeted); // it walked up to the owner
        Assert.True(w.Server.BaseGuestsForTest.Single().Greeted, "the neighbour never reached the owner");

        w.Player.State.AboardShip = true;
        w.Server.TickBaseGuestsForTest();

        Assert.Equal("Leaving", Assert.Single(w.Server.BaseGuestsForTest).Phase);
        Assert.Contains(w.Server.NpcRosterForTest(), n => n.Id == guest); // not popped out of the yard
        Assert.True(w.Server.NpcVisitingAwayForTest(villager));
        Assert.Single(w.MessagesToPlayer(), m => m == WalkingHome(name));
        Assert.True(w.Server.KeepsNpcsMovingForTest(w.Player.State.PlayerId), "the owner aboard does not see the guest walk");

        // Watched from right there, it is still around a moment later — the walk is its way out.
        StepSeconds(w, 3);
        Assert.Contains(w.Server.NpcRosterForTest(), n => n.Id == guest);

        // Out of sight (the owner far off), it is gone at the end of its walk — and the villager is back in the village.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 40f, w.Feet, NpcLifeWorld.Cz - 40f);
        w.TickAt(0.4, 200, until: () => w.Server.BaseGuestsForTest.Count == 0);
        Assert.Empty(w.Server.BaseGuestsForTest);
        Assert.DoesNotContain(w.Server.NpcRosterForTest(), n => n.Id == guest);
        Assert.False(w.Server.NpcVisitingAwayForTest(villager));
        Assert.False(w.Server.KeepsNpcsMovingForTest(w.Player.State.PlayerId)); // aboard and nobody to watch: the NPCs rest again
        Assert.Single(w.MessagesToPlayer(), m => m == WalkingHome(name)); // said once
    }

    /// <summary>#2713: freeze-safe — a world nobody ticked for a while still ends the visit at once (no guest walks home in a
    /// world without players).</summary>
    [Fact]
    public void AWorldNotTickedForAWhile_StillEndsTheVisitAtOnce()
    {
        using var w = World("guest_frozen");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        w.Server.AdvanceUptimeForTest(30.0); // longer than the 20 s a guest's world may pause
        w.Server.TickBaseGuestsForTest();

        Assert.Empty(w.Server.BaseGuestsForTest);
        Assert.DoesNotContain(w.Server.NpcRosterForTest(), n => n.Id == guest);
        Assert.False(w.Server.NpcVisitingAwayForTest(villager));
    }

    /// <summary>#2713: a neighbour's present comes with where it is off to, and so does its leaving.</summary>
    [Fact]
    public void ANeighboursPresent_SaysWhereItIsGoing_AndItsLeaving_IsToldToTheOwner()
    {
        using var w = World("guest_neighbour_gift");
        VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        string name = Npc(w, guest).Name;
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();

        string pid = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(("guest_neighbour", 0), w.Server.ActiveDialogForTest(pid));
        w.Server.ChooseDialogForTest(pid, 0); // "Oh, thank you!" → the present
        string answer = LastDialog(w).Text;
        Assert.StartsWith(En("dlg.guest_neighbour.r_thanks"), answer);
        Assert.EndsWith(GoingHome, answer);
        Assert.Contains(Village, answer);

        Assert.DoesNotContain(WalkingHome(name), w.MessagesToPlayer()); // still here
        StepSeconds(w, 32); // half a minute after its present it heads home
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
        Assert.Single(w.MessagesToPlayer(), m => m == WalkingHome(name));
    }

    /// <summary>#2713: the traveller and the merchant are nobody's neighbours — no "walking home" toast for them.</summary>
    [Fact]
    public void OnlyANeighbour_IsSaidToWalkHome()
    {
        using var w = World("guest_traveller_leaves");
        VillageWithVillager(w);
        Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "traveler"));
        w.Player.State.AboardShip = true;
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
        Assert.DoesNotContain(w.MessagesToPlayer(), m => m.Contains(Village, StringComparison.Ordinal));
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
        Assert.Equal(MarketStock.MixedTheme, w.Server.VendorThemeAtForTest(w.Player.State)); // #2630: a bit of everything

        // Its stall stays open longer than an ordinary guest waits.
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 20f, w.Feet, NpcLifeWorld.Cz - 20f);
        StepSeconds(w, 150);
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);
        StepSeconds(w, 40);
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
    }

    /// <summary>#2630: "A bit of everything!" for real — the merchant's goods are one offer from each of several trades,
    /// new every day, and the server takes exactly the rows the client's vendor screen lists for it (both read
    /// <see cref="MarketStock"/>; the merchant's theme travels on the wire as <see cref="MarketStock.MixedTheme"/>).</summary>
    [Fact]
    public void TheTravellingMerchant_SellsABitOfEverything_ExactlyWhatTheScreenLists()
    {
        using var w = World("guest_merchant_goods");
        int merchant = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "merchant"));
        Assert.Equal(MarketStock.MixedTheme, w.Server.NpcJobsForTest.Single(n => n.Role == "vendor").Theme);
        StandBeside(w, merchant);

        string Craft(string recipe)
        {
            int mark = w.Transport.Sent.Count;
            w.Server.Craft(w.Player.State.PlayerId, recipe);
            var res = w.Transport.Sent.Skip(mark).Select(s => s.Msg).OfType<CraftResult>().LastOrDefault();
            return res is null ? "(no result)" : res.Success ? "ok" : res.Reason ?? string.Empty;
        }

        var recipes = Content.Value.Recipes.Values;
        long day = w.Server.MarketDayForTest;
        var rows = MarketStock.ListFor(recipes, MarketStock.MixedTheme, day);
        var goods = rows.Where(r => r.MarketTheme.Length > 0).ToList();
        Assert.True(goods.Select(r => r.MarketTheme).Distinct().Count() >= 3, "offers from at least three trades");
        foreach (var r in rows)
        {
            foreach (var input in r.Inputs)
            {
                w.Player.State.Inventory.Add(input.Item, input.Count, 999);
            }

            Assert.Equal("ok", Craft(r.Key)); // every listed row trades
        }

        var notToday = recipes.First(r => MarketStock.IsTrade(r) && r.MarketTheme.Length > 0 && r.OfferedOnDay(day) && !goods.Contains(r));
        Assert.Equal("@srv.craft.wrong_vendor", Craft(notToday.Key)); // and nothing it does not list

        // Tomorrow it has other goods.
        var tomorrow = MarketStock.MixedOffers(recipes, day + 1);
        Assert.NotEqual(goods.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal), tomorrow.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));
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

        // #2617: talked to again, the neighbour holds the wish like its villager would: how far along the player is first…
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(guest, LastDialog(w).NpcId);
        Assert.StartsWith(RemindPrefix(0, 1), LastDialog(w).Text);
        Assert.Null(w.Server.ActiveDialogForTest(pid));

        // …and with nothing new since, its own talk — where it has nothing more to wish for right now.
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(("guest_neighbour", 0), w.Server.ActiveDialogForTest(pid));
        w.Server.ChooseDialogForTest(pid, 1);
        Assert.Equal(En("dlg.guest_neighbour.r_nowish"), LastDialog(w).Text);
        Assert.Single(w.Server.HeldWishesForTest(pid));
    }

    private static MissionList LastMissions(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<MissionList>().Last();

    private static MarkerList LastMarkers(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<MarkerList>().Last();

    /// <summary>#2713/#2714 (Lyxette 2026-10-17: "I accept a few small tasks … after that the settlers have vanished"): the wish
    /// a neighbour brings along says where to take it — the neighbour's answer, the Missions tab, the ready toast and the
    /// tab's refusal name the village, and the village is marked on the player's map until the wish is handed over.</summary>
    [Fact]
    public void ANeighboursWish_NamesTheVillage_InTheTalk_TheMissionTheToastsAndOnTheMap()
    {
        using var w = World("guest_wish_where");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        string giver = Npc(w, villager).Name;
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();

        string pid = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(pid, guest);
        w.Server.ChooseDialogForTest(pid, 1); // "Can I help you with anything?"
        Assert.EndsWith(GoingHome, LastDialog(w).Text); // "I'm heading home to Testville soon — come and see me there!"

        // The Missions tab: the request, the fact line and where the villager lives — plus the place and its marker.
        string wish = Assert.Single(w.Server.HeldWishesForTest(pid));
        var mission = LastMissions(w).Active.Single(m => m.Id == wish);
        Assert.EndsWith(En("wish.where").Replace("{npc}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal),
            mission.Description);
        Assert.Equal(Village, mission.PlaceName);
        Assert.Equal("wish:" + w.Server.SettlementLocationKeyForTest(Village), mission.PlaceMarkerId);

        // The map: one marker at the village, the player's own, sent with the acceptance.
        var marker = Assert.Single(w.Server.WishMarkersForTest(pid));
        Assert.Equal(mission.PlaceMarkerId, marker.Id);
        Assert.Equal(Village, marker.Label);
        Assert.Equal(pid, marker.OwnerId);
        Assert.False(marker.Ping);
        var centre = w.Server.SettlementCenterForTest(Village);
        Assert.Equal((centre.X, centre.Z), (marker.X, marker.Z));
        Assert.Contains(LastMarkers(w).Markers, m => m.Wish && m.Id == marker.Id);

        // Ready: the toast says where to bring it.
        GiveCrystal(w);
        w.Server.TickWishesForTest(pid);
        Assert.Contains(En("srv.wish.ready_village").Replace("{name}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal),
            w.MessagesToPlayer());

        // The tab, away from the villager: the refusal names the village too.
        var beside = w.Player.State.Position;
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 30f, w.Feet, NpcLifeWorld.Cz - 30f);
        w.Server.TurnInMission(pid, wish);
        var refusal = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<MissionResult>().Last();
        Assert.False(refusal.Success);
        Assert.Equal(En("srv.wish.return_village").Replace("{name}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal),
            refusal.Reason);

        // Handed over to the neighbour still visiting: the marker and the stored village are gone with the wish.
        w.Player.State.Position = beside;
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.Empty(w.Server.WishMarkersForTest(pid));
        Assert.DoesNotContain(LastMarkers(w).Markers, m => m.Wish);
        Assert.All(w.Player.State.Wishes.Values.SelectMany(l => l.Lanes), l => Assert.Equal(string.Empty, l.Village));
    }

    /// <summary>The start of the "you have {have} of {count} … — no hurry!" reminder, up to the item's name.</summary>
    private static string RemindPrefix(int have, int count)
        => En("dlg.wish.remind").Split("{item}")[0].Replace("{have}", have.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>The start of "{npc} gives you: {gift}", up to the gift.</summary>
    private static string GiftPrefix(string npcName)
        => En("dlg.wish.gift").Split("{gift}")[0].Replace("{npc}", npcName, StringComparison.Ordinal);

    private static void GiveCrystal(NpcLifeWorld w)
        => Assert.Equal(0, w.Player.State.Inventory.Add("crystal", 1, w.Content.GetItem("crystal")!.MaxStack));

    /// <summary>In the village: talks to the villager and says "Sure, I'll help!", then walks back to the base.</summary>
    private static string TakeWishInTheVillage(NpcLifeWorld w, int villager)
    {
        string pid = w.Player.State.PlayerId;
        var home = w.Player.State.Position;
        var at = PosOf(w, villager);
        w.Player.State.Position = new Vector3f(at.X - 2f, at.Y, at.Z);
        w.Server.TalkToNpcForTest(pid, villager);
        Assert.Equal(("wish", 0), w.Server.ActiveDialogForTest(pid));
        w.Server.ChooseDialogForTest(pid, 0);
        w.Player.State.Position = home;
        return Assert.Single(w.Server.HeldWishesForTest(pid));
    }

    private static NpcStandingList LastStandings(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcStandingList>().Last();

    [Fact]
    public void ANeighboursWish_AlreadyInTheBag_IsSaidSo_AndHandedOverRightThere()
    {
        using var w = World("guest_wish_ready");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        GiveCrystal(w);

        string pid = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(pid, guest);
        w.Server.ChooseDialogForTest(pid, 1); // "Can I help you with anything?"
        Assert.Single(w.Server.HeldWishesForTest(pid));
        string answer = LastDialog(w).Text;
        Assert.StartsWith(En("dlg.guest_neighbour.r_wish"), answer); // the neighbour's own answer, then the wish…
        Assert.EndsWith(En("dlg.wish.r_yes_ready"), answer);         // …and #2617: "you already have everything with you!"

        // No "bring it over!" toast — it is right here; the "?" sits on the guest, never on the villager hidden at home.
        w.Server.TickWishesForTest(pid);
        Assert.DoesNotContain("@srv.wish.ready:" + Npc(w, villager).Name, w.MessagesToPlayer());
        var (offers, ready) = w.Server.WishMarksForTest(pid);
        Assert.Equal(new[] { guest }, ready);
        Assert.DoesNotContain(villager, offers.Concat(ready));

        // The neighbour waits for the hand-over instead of heading home half a minute later.
        StepSeconds(w, 32);
        Assert.Equal("Waiting", w.Server.BaseGuestsForTest.Single().Phase);

        // Talking again hands it over: the thanks, the present, +5 with the villager.
        string key = w.Server.NpcPersonKeyForTest(villager);
        int before = w.Player.State.NpcMemory[key].Value;
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        var thanks = LastDialog(w);
        Assert.Equal(guest, thanks.NpcId);
        Assert.True(thanks.End);
        Assert.Contains(GiftPrefix(Npc(w, villager).Name), thanks.Text);
        Assert.True(w.Player.State.NpcMemory[key].Value >= before + 5, "the hand-over brought no standing");
        Assert.Empty(w.Server.WishMarksForTest(pid).Ready);
    }

    [Fact]
    public void AWishTakenInTheVillage_IsHandedOver_WhenThatVillagerVisitsTheBase()
    {
        using var w = World("guest_wish_village");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        string pid = w.Player.State.PlayerId;
        TakeWishInTheVillage(w, villager);

        // Back home with the crystal — and the villager comes visiting.
        GiveCrystal(w);
        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        Assert.True(w.Server.NpcVisitingAwayForTest(villager));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();

        // The "?" moved onto the guest with the visit (the standings went out at the spawn).
        var standings = LastStandings(w);
        Assert.Contains(guest, standings.WishReadyNpcIds);
        Assert.DoesNotContain(villager, standings.WishReadyNpcIds.Concat(standings.WishNpcIds));

        string key = w.Server.NpcPersonKeyForTest(villager);
        int before = w.Player.State.NpcMemory[key].Value;
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.Equal(guest, LastDialog(w).NpcId);
        Assert.Contains(GiftPrefix(Npc(w, villager).Name), LastDialog(w).Text);
        Assert.True(w.Player.State.NpcMemory[key].Value >= before + 5, "the hand-over brought no standing");
    }

    [Fact]
    public void AnIncompleteWish_GetsTheReminder_FromTheVisitingNeighbour()
    {
        using var w = World("guest_wish_remind");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        string pid = w.Player.State.PlayerId;
        TakeWishInTheVillage(w, villager);

        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        Assert.Empty(w.Server.WishMarksForTest(pid).Ready); // nothing to hand over yet — no "?"

        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(guest, LastDialog(w).NpcId);
        Assert.StartsWith(RemindPrefix(0, 1), LastDialog(w).Text);
        Assert.Single(w.Server.HeldWishesForTest(pid));

        // A second press with nothing new talks normally — the guest's own dialogue.
        w.Server.TalkToNpcForTest(pid, guest);
        Assert.Equal(("guest_neighbour", 0), w.Server.ActiveDialogForTest(pid));
    }

    [Fact]
    public void WhileTheVillagerVisits_ItsMarkersSitOnTheGuest_AndGoHomeWithIt()
    {
        using var w = World("guest_wish_marks");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        string pid = w.Player.State.PlayerId;
        Assert.Contains(villager, w.Server.WishMarksForTest(pid).Offers); // an open offer: "!" over the villager

        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        var (offers, ready) = w.Server.WishMarksForTest(pid);
        Assert.Contains(guest, offers); // its "Can I help you with anything?" takes it
        Assert.DoesNotContain(villager, offers.Concat(ready)); // never the hidden id
        Assert.Contains(guest, LastStandings(w).WishNpcIds);

        // The owner going aboard sends the guest home (#2713: it walks, the "!" stays on it on its way) — once it is gone
        // the "!" goes back with the villager, and the standings say so.
        w.Player.State.AboardShip = true;
        w.Server.TickBaseGuestsForTest();
        Assert.Equal("Leaving", w.Server.BaseGuestsForTest.Single().Phase);
        Assert.Contains(guest, w.Server.WishMarksForTest(pid).Offers);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 40f, w.Feet, NpcLifeWorld.Cz - 40f); // nobody watches it go
        StepSeconds(w, 125); // at the latest the usual timeout ends the walk
        Assert.Empty(w.Server.BaseGuestsForTest);
        Assert.Contains(villager, w.Server.WishMarksForTest(pid).Offers);
        Assert.Contains(villager, LastStandings(w).WishNpcIds);
        Assert.DoesNotContain(guest, LastStandings(w).WishNpcIds);
    }

    [Fact]
    public void AtTheTab_AWishIsTurnedInNextToTheVisitingNeighbour_WhoSaysThankYou()
    {
        using var w = World("guest_wish_tab");
        w.Server.SetWishTemplateForTest("show_crystal");
        int villager = VillageWithVillager(w);
        string pid = w.Player.State.PlayerId;
        string wish = TakeWishInTheVillage(w, villager);
        GiveCrystal(w);

        int guest = Assert.IsType<int>(w.Server.SpawnBaseGuestForTest(w.BaseId, "neighbour"));
        StandBeside(w, guest);
        w.Server.TickBaseGuestsForTest();
        w.Server.TurnInMission(pid, wish); // the Missions tab, standing beside the guest

        Assert.Empty(w.Server.HeldWishesForTest(pid));
        var thanks = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.Kind == NpcGreetingKinds.Addressed).ToList();
        Assert.Contains(thanks, g => g.NpcId == guest); // the thanks come from the one standing there
        Assert.DoesNotContain(thanks, g => g.NpcId == villager);
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

    private static string[]? GuestAnswers(NpcLinesRequest r) => r.Purpose switch
    {
        NpcLinePurposes.GuestGreet => new[] { "Hey {player}, what a nice place!" },
        NpcLinePurposes.GuestOffer => new[] { "Look what I brought along for you!" },
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
    public void AiOn_TheGreetingAndTheOffer_ArePrefetchedAtSpawn_AndTheCachedLinesAreUsed()
    {
        var ai = new FakeNpcLines(GuestAnswers);
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
        var ai = new FakeNpcLines(GuestAnswers);
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
