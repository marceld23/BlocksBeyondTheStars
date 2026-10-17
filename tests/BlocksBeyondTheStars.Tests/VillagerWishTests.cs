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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2601 personal wishes on a small arena village (<see cref="NpcLifeWorld"/> + a registered settlement and spawned
/// villagers — fast, every step under the test's control): the "!" markers (at most one per lane, only on villagers
/// who may wish), the offer in conversation with the exact fact line, accepting (log yes, board no, tab no; with
/// everything already in the bag the answer says so and the next talk hands it over), the
/// "no hurry" reminder, the hand-over by talking (used up, present, +5, the next wish moves on), "show me" keeps the
/// items, "check on the animals" counts scans, "maybe later", a full bag, the tab turn-in next to the villager only,
/// the 1 Hz "?" check, two players, feasibility per world and the tier of a first wish. The real settlements (seed loop)
/// and a restart are covered by <see cref="VillagerWishRealVillageTests"/>.
/// </summary>
public sealed class VillagerWishTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    internal const string Village = "Wishville";

    /// <summary>An arena with the village registered over the whole pad (tier "village" = two lanes).</summary>
    internal static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            configure?.Invoke(c);
        });
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        EmptyBag(w.Player); // a new player's starter kit (berries among it) would answer a wish before the test does
        return w;
    }

    internal static void EmptyBag(PlayerSession player)
    {
        for (int i = 0; i < player.State.Inventory.SlotCount; i++)
        {
            player.State.Inventory.SetSlot(i, null);
        }
    }

    /// <summary>Four plain settlers within talking reach of the player (who stands at Cx−1.5, Cz−1.5).</summary>
    internal static int[] Settlers(NpcLifeWorld w)
    {
        float px = NpcLifeWorld.Cx - 1.5f, pz = NpcLifeWorld.Cz - 1.5f;
        return new[]
        {
            w.Server.SpawnVillagerForTest(Village, new Vector3f(px + 2.5f, w.Feet, pz)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(px - 2.5f, w.Feet, pz)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(px, w.Feet, pz + 2.5f)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(px, w.Feet, pz - 2.5f)),
        };
    }

    internal static List<object> Sent(NpcLifeWorld w, PlayerSession player)
        => w.Transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).ToList();

    internal static T? Last<T>(NpcLifeWorld w, PlayerSession player) where T : class
        => Sent(w, player).OfType<T>().LastOrDefault();

    internal static (string Settlement, int Lane, string MissionId, int GiverId, string GiverKey, string Template, int Count, bool Held) Offer(
        NpcLifeWorld w, PlayerSession player, int lane = 0)
        => w.Server.WishLanesForTest(player.State.PlayerId).Single(l => l.Settlement == Village && l.Lane == lane);

    internal static void Give(NpcLifeWorld w, PlayerSession player, string item, int count)
        => Assert.Equal(0, player.State.Inventory.Add(item, count, w.Content.GetItem(item)!.MaxStack));

    private static string NameOf(NpcLifeWorld w, int npcId) => w.Server.NpcRosterForTest().Single(n => n.Id == npcId).Name;

    private static string En(string key) => Content.Value.CreateLocalizer(GameLocale.English).Get(key);

    /// <summary>#2713: the toast when a held wish turns ready — it names the village ("… bring it to Wishville!").</summary>
    private static string ReadyToast(string giver)
        => En("srv.wish.ready_village").Replace("{name}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal);

    /// <summary>Whether a server message is a "you have everything" toast — the named one or the plain token.</summary>
    private static bool IsReadyToast(string text)
        => text.StartsWith("@srv.wish.ready", StringComparison.Ordinal)
           || text.StartsWith(En("srv.wish.ready_village").Split("{name}")[0], StringComparison.Ordinal);

    /// <summary>#2713: the tab's refusal away from the giver — it names the village.</summary>
    private static string ReturnRefusal(string giver)
        => En("srv.wish.return_village").Replace("{name}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal);

    /// <summary>Talks to the lane's villager and says "Sure, I'll help!".</summary>
    internal static (string Settlement, int Lane, string MissionId, int GiverId, string GiverKey, string Template, int Count, bool Held) Accept(
        NpcLifeWorld w, PlayerSession player, int lane = 0)
    {
        var offer = Offer(w, player, lane);
        w.Server.TalkToNpcForTest(player.State.PlayerId, offer.GiverId);
        Assert.Equal(("wish", 0), w.Server.ActiveDialogForTest(player.State.PlayerId));
        w.Server.ChooseDialogForTest(player.State.PlayerId, 0);
        Assert.Contains(offer.MissionId, w.Server.HeldWishesForTest(player.State.PlayerId));
        return offer;
    }

    [Fact]
    public void Markers_AtMostOnePerLane_OnlyOnVillagersWhoMayWish_AndTheStandingsCarryThem()
    {
        using var w = World("wish_marks");
        var settlers = Settlers(w);
        float px = NpcLifeWorld.Cx - 1.5f, pz = NpcLifeWorld.Cz - 1.5f;
        int vendor = w.Server.SpawnVillagerForTest(Village, new Vector3f(px + 3f, w.Feet, pz + 3f), role: "vendor", job: "vendor");
        int doctor = w.Server.SpawnVillagerForTest(Village, new Vector3f(px - 3f, w.Feet, pz + 3f), job: "doctor");
        int guard = w.Server.SpawnVillagerForTest(Village, new Vector3f(px + 3f, w.Feet, pz - 3f), role: "guardian");
        var pid = w.Player.State.PlayerId;

        var (offers, ready) = w.Server.WishMarksForTest(pid);
        Assert.Equal(2, offers.Length); // a village: two lanes, two different villagers
        Assert.Equal(2, offers.Distinct().Count());
        Assert.All(offers, id => Assert.Contains(id, settlers));
        Assert.DoesNotContain(vendor, offers);
        Assert.DoesNotContain(doctor, offers);
        Assert.DoesNotContain(guard, offers);
        Assert.Empty(ready);

        // The markers reach the client through the codec, on the per-player standings message.
        w.Server.SendNpcStandingsForTest(pid);
        var standings = Last<NpcStandingList>(w, w.Player);
        Assert.NotNull(standings);
        Assert.Equal(offers.OrderBy(i => i), standings!.WishNpcIds.OrderBy(i => i));
        Assert.Empty(standings.WishReadyNpcIds);

        // A hamlet has room for one wish at a time.
        w.Server.SetSettlementTierForTest(Village, "hamlet");
        Assert.Single(w.Server.WishMarksForTest(pid).Offers);
    }

    [Fact]
    public void Talking_OpensTheWish_WithTheCountTheItem_AndTheAuthoredFactLine()
    {
        using var w = World("wish_offer");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var offer = Offer(w, w.Player);
        Assert.Equal("berries", offer.Template);
        Assert.InRange(offer.Count, 4, 6); // 5 ± 1

        w.Server.TalkToNpcForTest(w.Player.State.PlayerId, offer.GiverId);
        Assert.Equal(("wish", 0), w.Server.ActiveDialogForTest(w.Player.State.PlayerId));
        var panel = Last<NpcDialogState>(w, w.Player)!;
        Assert.Equal(offer.GiverId, panel.NpcId);
        Assert.False(panel.End);
        Assert.Contains($"bring me {offer.Count} berries", panel.Text, StringComparison.Ordinal);
        Assert.EndsWith($"\n\nYou need: {offer.Count}× Berries", panel.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { "Sure, I'll help!", "Maybe later." }, panel.Choices);
    }

    [Fact]
    public void Accepting_PutsItInTheLog_KeepsItOffTheBoard_AndTheTabCanNeverAcceptAWish()
    {
        using var w = World("wish_accept");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);

        var row = Assert.Single(w.Player.State.Missions, m => m.MissionId == offer.MissionId);
        Assert.Equal(MissionStatus.Active, row.Status);
        Assert.StartsWith("wish_", offer.MissionId, StringComparison.Ordinal);
        Assert.Equal(giver, w.Server.MissionGiverName(offer.MissionId));
        var objective = w.Server.FirstObjectiveForTest(offer.MissionId)!;
        Assert.Equal(MissionObjectiveType.Deliver, objective.Type);
        Assert.Equal(offer.Count, objective.Required);

        // The response, the accept toast titled "A wish from …", the mission list: active yes, available no.
        Assert.Equal("Thank you! I'll wait right here.", Last<NpcDialogState>(w, w.Player)!.Text);
        var toast = Sent(w, w.Player).OfType<MissionResult>().Last(r => r.MissionId == offer.MissionId);
        Assert.True(toast.Success);
        Assert.True(toast.FreeText);
        Assert.Equal($"A wish from {giver}", toast.Title);
        var list = Last<MissionList>(w, w.Player)!;
        var active = Assert.Single(list.Active, m => m.Id == offer.MissionId);
        Assert.True(active.FreeText);
        Assert.Equal($"A wish from {giver}", active.Title);
        Assert.Contains($"You need: {offer.Count}× Berries", active.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(list.Available, m => m.Id.StartsWith("wish_", StringComparison.Ordinal));

        // The marker of that villager is gone (held, not ready); the other lane's "!" stays.
        var (offers, _) = w.Server.WishMarksForTest(pid);
        Assert.DoesNotContain(offer.GiverId, offers);
        Assert.Single(offers);

        // The tab cannot take the other lane's wish — wishes are asked in person.
        var other = Offer(w, w.Player, lane: 1);
        w.Server.AcceptMission(pid, other.MissionId);
        Assert.Equal("@srv.wish.talk", Last<MissionResult>(w, w.Player)!.Reason);
        Assert.DoesNotContain(other.MissionId, w.Server.HeldWishesForTest(pid));
    }

    [Fact]
    public void AcceptingWithEverythingAlreadyInTheBag_TheAnswerSaysSo_TheQuestionMarkShows_AndTheNextTalkHandsItOver()
    {
        // A new player starts with berries: the yes must not end in silence.
        using var w = World("wish_ready_on_accept");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        Give(w, w.Player, "berries", Offer(w, w.Player).Count);
        var offer = Accept(w, w.Player);

        var answer = Last<NpcDialogState>(w, w.Player)!;
        Assert.True(answer.End);
        Assert.Equal("Oh, you already have everything with you! Just talk to me again.", answer.Text);
        Assert.Contains(offer.GiverId, Last<NpcStandingList>(w, w.Player)!.WishReadyNpcIds); // the "?" at once

        // The answer said it — no "bring it over!" toast on top.
        w.Server.TickWishesForTest(pid);
        Assert.DoesNotContain(Sent(w, w.Player).OfType<ServerMessage>(), m => IsReadyToast(m.Text));

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.Equal(0, w.Player.State.Inventory.CountOf("berries"));
        Assert.StartsWith("Berries! They smell wonderful", Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIncompleteWish_RemindsOnce_ConsumesNothing_AndThenTheVillagerJustChats()
    {
        using var w = World("wish_remind");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        Give(w, w.Player, "berries", offer.Count - 2);

        int panelsBefore = Sent(w, w.Player).OfType<NpcDialogState>().Count();
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        var reminder = Last<NpcDialogState>(w, w.Player)!;
        Assert.True(reminder.End);
        Assert.Equal($"You have {offer.Count - 2} of {offer.Count} Berries so far — no hurry!", reminder.Text);
        Assert.Equal(offer.Count - 2, w.Player.State.Inventory.CountOf("berries"));
        Assert.Contains(offer.MissionId, w.Server.HeldWishesForTest(pid));

        // Pressing E again without new progress: no second reminder — the ordinary talk.
        int panelsAfterReminder = Sent(w, w.Player).OfType<NpcDialogState>().Count();
        Assert.Equal(panelsBefore + 1, panelsAfterReminder);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.DoesNotContain(Sent(w, w.Player).OfType<NpcDialogState>().Skip(panelsAfterReminder),
            s => s.Text.Contains("no hurry", StringComparison.Ordinal));

        // One more berry → new progress → a new reminder.
        Give(w, w.Player, "berries", 1);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.Equal($"You have {offer.Count - 1} of {offer.Count} Berries so far — no hurry!", Last<NpcDialogState>(w, w.Player)!.Text);
    }

    [Fact]
    public void ACompleteWish_IsHandedOverByTalking_UsedUp_ThePresentPaid_Plus5_AndTheNextWishMovesOn()
    {
        using var w = World("wish_handover");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);
        Give(w, w.Player, "berries", offer.Count + 1);
        int before = w.Server.RelationshipValueForTest(pid, offer.GiverKey);
        var otherLane = Offer(w, w.Player, lane: 1);

        w.Server.TalkToNpcForTest(pid, offer.GiverId);

        var thanks = Last<NpcDialogState>(w, w.Player)!;
        Assert.True(thanks.End);
        Assert.Equal($"Berries! They smell wonderful — thank you so much!\n\n{giver} gives you: 3× Berry Seed", thanks.Text);
        Assert.Equal(1, w.Player.State.Inventory.CountOf("berries"));
        Assert.Equal(3, w.Player.State.Inventory.CountOf("berry_seed"));
        Assert.Equal(before + SvGameServer.WishStandingBonus, w.Server.RelationshipValueForTest(pid, offer.GiverKey));
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.DoesNotContain(w.Player.State.Missions, m => m.MissionId == offer.MissionId); // the row is gone …
        Assert.Equal(string.Empty, w.Server.MissionGiverName(offer.MissionId));            // … and the definition too
        Assert.Contains(Sent(w, w.Player).OfType<MissionResult>(), r => r.MissionId == offer.MissionId && r.Outcome == MissionOutcomes.Completed);

        // The lane moved on: a new wish, by somebody else.
        var next = Offer(w, w.Player);
        Assert.NotEqual(offer.MissionId, next.MissionId);
        Assert.NotEqual(offer.GiverId, next.GiverId);
        var ledger = w.Player.State.Wishes[w.Server.SettlementLocationKeyForTest(Village)];
        Assert.Equal(1, ledger.Done);
        Assert.Equal(1, ledger.Lanes[0].N);
        Assert.Equal(offer.GiverKey, ledger.Lanes[0].LastGiver);

        // The other lane's open offer is untouched — what a villager asks for only changes once THEIR wish is fulfilled.
        Assert.Equal(otherLane, Offer(w, w.Player, lane: 1));
    }

    [Fact]
    public void ShowMe_KeepsTheItems_AndPaysTheKnowledge()
    {
        using var w = World("wish_show");
        Settlers(w);
        w.Server.SetWishTemplateForTest("show_crystal");
        var pid = w.Player.State.PlayerId;
        var offer = Offer(w, w.Player);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.EndsWith("Show me: 1× Crystal — you keep it!", Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
        w.Server.ChooseDialogForTest(pid, 0);
        Assert.Equal(MissionObjectiveType.Collect, w.Server.FirstObjectiveForTest(offer.MissionId)!.Type);

        Give(w, w.Player, "crystal", 1);
        int knowledge = w.Player.State.KnowledgePoints;
        w.Server.TalkToNpcForTest(pid, offer.GiverId);

        Assert.Equal(1, w.Player.State.Inventory.CountOf("crystal")); // shown, not given
        Assert.Equal(knowledge + 2, w.Player.State.KnowledgePoints);
        Assert.EndsWith("gives you: 2 knowledge points", Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
    }

    [Fact]
    public void CheckOnTheAnimals_CountsScans_AndIsHandedOverAfterwards()
    {
        using var w = World("wish_scan");
        Settlers(w);
        w.Server.SetWishTemplateForTest("grazer");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        Assert.Equal(MissionObjectiveType.Scan, w.Server.FirstObjectiveForTest(offer.MissionId)!.Type);

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.Equal("You have scanned 0 of 1 so far — no hurry!", Last<NpcDialogState>(w, w.Player)!.Text);

        w.Server.SimulateScanForTest(w.Player, "creature", "sp0", hostile: false, firstTime: true);
        int berries = w.Player.State.Inventory.CountOf("berries");
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.StartsWith("The animals are well?", Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
        Assert.Equal(berries + 4, w.Player.State.Inventory.CountOf("berries"));
        Assert.Empty(w.Server.HeldWishesForTest(pid));
    }

    [Fact]
    public void AtTheFestival_NoNewWishIsAsked_ButAHeldOneIsStillHandedOver()
    {
        using var w = World("wish_festival");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var held = Accept(w, w.Player);
        var open = Offer(w, w.Player, lane: 1);
        Give(w, w.Player, "berries", held.Count);

        w.Server.SetNpcCelebratingForTest(held.GiverId, true);
        w.Server.SetNpcCelebratingForTest(open.GiverId, true);
        var (offers, ready) = w.Server.WishMarksForTest(pid);
        Assert.DoesNotContain(open.GiverId, offers); // celebrating: the "!" waits …
        Assert.Contains(held.GiverId, ready);        // … the "?" of a held wish does not
        w.Server.TickWishesForTest(pid);             // the festival started where the player stands → markers re-sent
        Assert.DoesNotContain(open.GiverId, Last<NpcStandingList>(w, w.Player)!.WishNpcIds);
        w.Server.TalkToNpcForTest(pid, open.GiverId);
        Assert.NotEqual(("wish", 0), w.Server.ActiveDialogForTest(pid));

        w.Server.TalkToNpcForTest(pid, held.GiverId);
        Assert.Empty(w.Server.HeldWishesForTest(pid));

        // The party is over: the same villager asks again, and the 1 Hz check brings the "!" back by itself.
        w.Server.SetNpcCelebratingForTest(held.GiverId, false);
        w.Server.SetNpcCelebratingForTest(open.GiverId, false);
        Assert.Contains(open.GiverId, w.Server.WishMarksForTest(pid).Offers);
        int standings = Sent(w, w.Player).OfType<NpcStandingList>().Count();
        w.Server.TickWishesForTest(pid);
        Assert.Equal(standings + 1, Sent(w, w.Player).OfType<NpcStandingList>().Count());
        Assert.Contains(open.GiverId, Last<NpcStandingList>(w, w.Player)!.WishNpcIds);
    }

    [Fact]
    public void MaybeLater_LeavesTheWishOpen_TheVillagerChatsNormallyThisSession_AndTheMarkerStays()
    {
        using var w = World("wish_later");
        Settlers(w);
        var pid = w.Player.State.PlayerId;
        var offer = Offer(w, w.Player);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        w.Server.ChooseDialogForTest(pid, 1);
        Assert.Equal("No hurry — I'll still be here.", Last<NpcDialogState>(w, w.Player)!.Text);
        Assert.Empty(w.Server.HeldWishesForTest(pid));

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.NotEqual(("wish", 0), w.Server.ActiveDialogForTest(pid));
        Assert.Contains(offer.GiverId, w.Server.WishMarksForTest(pid).Offers);
        Assert.Equal(offer.MissionId, Offer(w, w.Player).MissionId); // the same wish, still waiting
    }

    [Fact]
    public void AFullBag_KeepsTheWishOpen_AndTakesNothing()
    {
        using var w = World("wish_fullbag");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        Give(w, w.Player, "berries", offer.Count);
        var inv = w.Player.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is null || inv.Slots[i]!.IsEmpty)
            {
                inv.SetSlot(i, new ItemStack("stone", w.Content.GetItem("stone")!.MaxStack));
            }
        }

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.Equal("Oh, your bag is full! Make a little room and come back.", Last<NpcDialogState>(w, w.Player)!.Text);
        Assert.Equal(offer.Count, inv.CountOf("berries"));
        Assert.Equal(0, inv.CountOf("berry_seed"));
        Assert.Contains(offer.MissionId, w.Server.HeldWishesForTest(pid));
    }

    [Fact]
    public void TheMissionsTab_TurnsAWishInOnlyNextToTheVillager_WhoThenSaysThankYou()
    {
        using var w = World("wish_tab");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);
        Give(w, w.Player, "berries", offer.Count);
        var home = w.Player.State.Position;

        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 15f, w.Feet, NpcLifeWorld.Cz + 15f);
        w.Server.TurnInMission(pid, offer.MissionId);
        Assert.Equal(ReturnRefusal(giver), Last<MissionResult>(w, w.Player)!.Reason); // #2713: names the village
        Assert.Contains(offer.MissionId, w.Server.HeldWishesForTest(pid));
        Assert.Equal(offer.Count, w.Player.State.Inventory.CountOf("berries"));

        w.Player.State.Position = home;
        w.Server.TurnInMission(pid, offer.MissionId);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.Equal(0, w.Player.State.Inventory.CountOf("berries"));
        var bubble = Last<NpcGreeting>(w, w.Player)!;
        Assert.Equal(offer.GiverId, bubble.NpcId);
        Assert.Equal("Berries! They smell wonderful — thank you so much!", bubble.Text);
        Assert.NotEqual(0, bubble.GreetingId);
    }

    [Fact]
    public void TheOncePerSecondCheck_MarksAReadyWish_AndSaysSoOnce()
    {
        using var w = World("wish_ready");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);

        w.Server.TickWishesForTest(pid);
        Assert.DoesNotContain(Sent(w, w.Player).OfType<ServerMessage>(), m => IsReadyToast(m.Text));

        Give(w, w.Player, "berries", offer.Count);
        w.Server.TickWishesForTest(pid);
        Assert.Single(Sent(w, w.Player).OfType<ServerMessage>(), m => m.Text == ReadyToast(giver));
        Assert.Contains(offer.GiverId, Last<NpcStandingList>(w, w.Player)!.WishReadyNpcIds);
        Assert.Contains(offer.GiverId, w.Server.WishMarksForTest(pid).Ready);

        w.Server.TickWishesForTest(pid);
        Assert.Single(Sent(w, w.Player).OfType<ServerMessage>(), m => m.Text == ReadyToast(giver));

        // Dropping the berries takes the "?" away again (re-sent once).
        w.Player.State.Inventory.Remove("berries", 1);
        w.Server.TickWishesForTest(pid);
        Assert.DoesNotContain(offer.GiverId, Last<NpcStandingList>(w, w.Player)!.WishReadyNpcIds);
    }

    [Fact]
    public void TwoPlayers_HaveWishesOfTheirOwn()
    {
        using var w = World("wish_two");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var sibling = w.Server.AddLocalPlayer("Sibling");
        sibling.State.AboardShip = false;
        sibling.State.Position = w.Player.State.Position;
        EmptyBag(sibling);

        var mine = Accept(w, w.Player);
        var theirs = Offer(w, sibling);
        Assert.NotEqual(mine.MissionId, theirs.MissionId);
        Assert.False(theirs.Held);
        Assert.Empty(w.Server.HeldWishesForTest(sibling.State.PlayerId));
        Assert.Equal(2, w.Server.WishMarksForTest(sibling.State.PlayerId).Offers.Length);

        Accept(w, sibling);
        Assert.Single(w.Server.HeldWishesForTest(sibling.State.PlayerId));
        Assert.Equal(new[] { mine.MissionId }, w.Server.HeldWishesForTest(w.Player.State.PlayerId));
    }

    [Fact]
    public void TheCoin_IsDeterministic_AndAFirstWishIsAlwaysAnEasyOne()
    {
        using var w = World("wish_coin");
        Settlers(w);
        var first = w.Server.WishLanesForTest("Homesteader");
        Assert.Equal(first, w.Server.WishLanesForTest("Homesteader"));

        var tiers = Content.Value.Wishes.ToDictionary(t => t.Key, t => t.Tier);
        var feasible = w.Server.WishFeasibleTemplatesForTest();
        for (int i = 0; i < 40; i++)
        {
            foreach (var lane in w.Server.WishLanesForTest("kid" + i))
            {
                Assert.Equal(1, tiers[lane.Template]); // glass needs a furnace — never a newcomer's first wish
                Assert.Contains(lane.Template, feasible);
            }
        }
    }

    [Fact]
    public void AWorldWithoutPlants_NeverWishesForThem_AndPoisonousPlantsBringNoBerries()
    {
        using var w = World("wish_feasible");
        Settlers(w);
        var normal = w.Server.WishFeasibleTemplatesForTest();
        Assert.Contains("garden_wall", normal);
        Assert.Contains("show_crystal", normal);

        w.Server.SetWishFloraForTest(noPlants: false, toxicPlants: true);
        var toxic = w.Server.WishFeasibleTemplatesForTest();
        Assert.DoesNotContain("berries", toxic);
        Assert.DoesNotContain("fruit", toxic);
        Assert.Equal(normal.Contains("fibre_basket"), toxic.Contains("fibre_basket")); // fibre is fibre

        w.Server.SetWishFloraForTest(noPlants: true, toxicPlants: false);
        var bare = w.Server.WishFeasibleTemplatesForTest();
        foreach (string key in new[] { "berries", "fibre_basket", "fruit", "firewood", "dark_path" })
        {
            Assert.DoesNotContain(key, bare);
        }

        for (int i = 0; i < 20; i++)
        {
            Assert.All(w.Server.WishLanesForTest("kid" + i), lane => Assert.Contains(lane.Template, bare));
        }
    }

    /// <summary>#2714: a granted wish carries the giver's village — its name, its body and its centre — and so the Missions
    /// tab names it and the player's map marks it (one marker per village, the player's own only, on that body only); handing
    /// the wish over clears it and takes the marker away.</summary>
    [Fact]
    public void AGrantedWish_CarriesTheGiversVillage_MarkedOnTheMap_UntilItIsHandedOver()
    {
        using var w = World("wish_village");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var sibling = w.Server.AddLocalPlayer("Sibling");
        sibling.State.AboardShip = false;
        sibling.State.Position = w.Player.State.Position;
        var pid = w.Player.State.PlayerId;
        Assert.Empty(w.Server.WishMarkersForTest(pid)); // an offer is no wish yet

        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);
        var (village, body, at) = w.Server.WishVillageForTest(pid, offer.MissionId);
        Assert.Equal(Village, village);
        Assert.Equal(w.Player.CurrentLocationId, body);
        Assert.Equal(w.Server.SettlementCenterForTest(Village), at);

        // The Missions tab says where — and points at the marker.
        var active = Last<MissionList>(w, w.Player)!.Active.Single(m => m.Id == offer.MissionId);
        Assert.Equal(Village, active.PlaceName);
        Assert.Equal("wish:" + w.Server.SettlementLocationKeyForTest(Village), active.PlaceMarkerId);
        Assert.Contains(En("wish.where").Replace("{npc}", giver, StringComparison.Ordinal).Replace("{village}", Village, StringComparison.Ordinal),
            active.Description, StringComparison.Ordinal);

        // The map: the marker went out with the acceptance — for this player, not for the sibling standing beside them.
        var marker = Assert.Single(Last<MarkerList>(w, w.Player)!.Markers, m => m.Wish);
        Assert.Equal(active.PlaceMarkerId, marker.Id);
        Assert.Equal((Village, at.X, at.Y, at.Z), (marker.Label, marker.X, marker.Y, marker.Z));
        Assert.Empty(w.Server.WishMarkersForTest(sibling.State.PlayerId));

        // A second wish from the same village keeps ONE marker.
        Accept(w, w.Player, lane: 1);
        Assert.Single(w.Server.WishMarkersForTest(pid));

        // On another body the marker is not shown (the tab still names the village).
        var lane0 = w.Player.State.Wishes.Values.Single().Lanes[0];
        var lane1 = w.Player.State.Wishes.Values.Single().Lanes[1];
        lane0.VillageLocationId = lane1.VillageLocationId = "some_other_moon";
        Assert.Empty(w.Server.WishMarkersForTest(pid));
        lane0.VillageLocationId = lane1.VillageLocationId = body;

        // Handed over: the village is forgotten with the wish; the other wish keeps the marker until it goes too.
        Give(w, w.Player, "berries", offer.Count);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.DoesNotContain(offer.MissionId, w.Server.HeldWishesForTest(pid));
        Assert.Equal(string.Empty, lane0.Village);
        Assert.Equal(string.Empty, lane0.VillageLocationId);
        Assert.Single(w.Server.WishMarkersForTest(pid));

        var other = Offer(w, w.Player, lane: 1);
        Give(w, w.Player, "berries", other.Count);
        w.Server.TalkToNpcForTest(pid, other.GiverId);
        Assert.Empty(w.Server.HeldWishesForTest(pid));
        Assert.Empty(w.Server.WishMarkersForTest(pid));
        Assert.DoesNotContain(Last<MarkerList>(w, w.Player)!.Markers, m => m.Wish);
    }

    /// <summary>#2714: the village is saved with the wish — after a restart the tab and the map still know it.</summary>
    [Fact]
    public void TheWishsVillage_SurvivesARestart()
    {
        using var w = World("wish_village_restart");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var offer = Accept(w, w.Player);
        var before = w.Server.WishVillageForTest(w.Player.State.PlayerId, offer.MissionId);

        w.Restart();
        Assert.Contains(offer.MissionId, w.Server.HeldWishesForTest(w.Player.State.PlayerId));
        Assert.Equal(before, w.Server.WishVillageForTest(w.Player.State.PlayerId, offer.MissionId));
        Assert.Equal(Village, Assert.Single(w.Server.WishMarkersForTest(w.Player.State.PlayerId)).Label);
    }

    /// <summary>#2714: a wish held from before the village was stored with it (Lyxette's) learns its village the next time the
    /// player is on that body — the 1 Hz check fills it in and re-sends the tab and the map.</summary>
    [Fact]
    public void AWishHeldFromBefore_LearnsItsVillage_OnTheNextCheck()
    {
        using var w = World("wish_village_learn");
        Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        var pid = w.Player.State.PlayerId;
        var offer = Accept(w, w.Player);
        string giver = NameOf(w, offer.GiverId);
        w.Server.ForgetWishVillagesForTest(pid);
        Assert.Equal(string.Empty, w.Server.WishVillageForTest(pid, offer.MissionId).Village);
        Assert.Empty(w.Server.WishMarkersForTest(pid));

        // Without a village the texts fall back to the plain ones.
        Give(w, w.Player, "berries", offer.Count);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 60f, w.Feet, NpcLifeWorld.Cz + 60f);
        w.Server.TurnInMission(pid, offer.MissionId);
        Assert.Equal("@srv.wish.return_giver:" + giver, Last<MissionResult>(w, w.Player)!.Reason);

        int mark = w.Transport.Sent.Count;
        w.Server.TickWishesForTest(pid);
        Assert.Equal(Village, w.Server.WishVillageForTest(pid, offer.MissionId).Village);
        var later = w.Transport.Sent.Skip(mark).Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).ToList();
        Assert.Contains(later.OfType<MarkerList>(), l => l.Markers.Any(m => m.Wish && m.Label == Village));
        Assert.Contains(later.OfType<MissionList>(), l => l.Active.Any(m => m.Id == offer.MissionId && m.PlaceName == Village));
    }
}
