// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The people of a lodge (#2641): one per bed with a person key of their own, a day round the hut (work, the bench by the
/// fire, the bed), their own trade, their own talk — a tip that puts a find on the map, the gatherer's poison warning, a
/// friend's present from the crate once a day and never anything toxic — and the villagers' hint that names them. The
/// lodges are built on the <see cref="NpcLifeWorld"/> arena (<c>SpawnLodgeForTest</c>).
/// </summary>
public sealed class LodgeResidentTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static string En(string key) => Content.CreateLocalizer(GameLocale.English).Get(key);

    private static string Lodge(NpcLifeWorld w, string profession, int beds = 1, int dx = 4, int dz = -6)
        => w.Server.SpawnLodgeForTest(NpcLifeWorld.Cx + dx, w.PadY, NpcLifeWorld.Cz + dz, profession, beds);

    private static (int Id, string Name, string Job, string PersonKey, Vector3i? Bed, Vector3i? Seat, Vector3f Work, string Held) Resident(NpcLifeWorld w, string key, int slot = 0)
        => w.Server.LodgeResidentsForTest(key)[slot];

    private static void StandBeside(NpcLifeWorld w, int npcId)
    {
        var at = w.Server.NpcStateForTest(npcId)!.Value.Pos;
        w.Player.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);
    }

    private static NpcDialogState LastDialog(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();

    private static void Befriend(NpcLifeWorld w, string personKey, string name)
        => w.Player.State.NpcMemory[personKey] = new NpcRelationship { Name = name, Role = "vendor", Value = 60 };

    // ---- who lives there -----------------------------------------------------------------------------------------

    [Fact]
    public void OneResidentPerBed_EachAPersonWithABed_AnEveningSeat_AndTheYardAsWork()
    {
        using var w = new NpcLifeWorld(Content, "lodge_people");
        string key = Lodge(w, "gatherer", beds: 2);
        var markers = w.Server.LodgeMarkersForTest(key);
        var beds = markers.Where(m => m.Type == LodgeGenerator.MarkerBed).Select(m => m.Pos.ToBlock()).ToList();
        var seats = markers.Where(m => m.Type == LodgeGenerator.MarkerSeat).Select(m => m.Pos.ToBlock()).ToList();
        var yard = markers.Single(m => m.Type == LodgeGenerator.MarkerYard).Pos;
        var people = w.Server.LodgeResidentsForTest(key);
        Assert.Equal(2, people.Count);
        Assert.Equal(2, beds.Count);

        string world = w.Player.CurrentLocationId;
        for (int slot = 0; slot < people.Count; slot++)
        {
            var r = people[slot];
            Assert.Equal($"lodge:{world}:{key}#{slot}", r.PersonKey);
            Assert.Equal(beds[slot], r.Bed);
            Assert.Contains(r.Seat!.Value, seats);
            Assert.True(Math.Abs(r.Work.X - yard.X) < 0.01f && Math.Abs(r.Work.Z - yard.Z) < 0.01f, "the yard is the work anchor");
            Assert.Equal(Math.Floor(yard.Y), r.Work.Y);
            var prof = Content.LodgeProfession(r.Job)!;
            Assert.Equal(prof.Held, r.Held);
            Assert.Contains(w.Server.NpcJobsForTest, n => n.Job == r.Job && n.Role == "vendor" && n.Theme == prof.Theme && n.NameKey == "npc.role." + r.Job);
        }

        Assert.Equal("gatherer", people[0].Job); // the first resident works the lodge's own yard
        Assert.Contains(people[1].Job, new[] { "gatherer", "hunter" }); // the second the partner's trade, when the world has game
        Assert.NotEqual(people[0].Seat, people[1].Seat);

        // Spawned again from the same world, seed and spot: the same people.
        using var twin = new NpcLifeWorld(Content, "lodge_people");
        string twinKey = Lodge(twin, "gatherer", beds: 2);
        Assert.Equal(people.Select(p => (p.Name, p.Job)), twin.Server.LodgeResidentsForTest(twinKey).Select(p => (p.Name, p.Job)));
    }

    [Fact]
    public void ADayRoundTheHut_TheBenchInTheEvening_TheBedAtNight_AndAVisitorFindsThemAsleep()
    {
        using var w = new NpcLifeWorld(Content, "lodge_day");
        string key = Lodge(w, "miner");
        var r = Resident(w, key);

        w.TickAt(0.45, 10);
        Assert.Equal(0, w.Server.NpcRoutineForTest(r.Id).Pose);

        w.TickAt(0.72, 90, until: () => w.Server.NpcRoutineForTest(r.Id).Pose == 1);
        var sitting = w.Server.NpcRoutineForTest(r.Id);
        Assert.Equal(1, sitting.Pose);
        Assert.True(Math.Abs(sitting.Pos.X - (r.Seat!.Value.X + 0.5f)) < 0.01f && Math.Abs(sitting.Pos.Z - (r.Seat.Value.Z + 0.5f)) < 0.01f,
            $"sits at {sitting.Pos}, not on the bench at {r.Seat}");

        w.TickAt(0.9, 90, until: () => w.Server.NpcRoutineForTest(r.Id).Pose == 2);
        var sleeping = w.Server.NpcRoutineForTest(r.Id);
        Assert.Equal(2, sleeping.Pose);
        Assert.Equal("npc.activity.sleeping", sleeping.Activity);
        Assert.True(Math.Abs(sleeping.Pos.X - (r.Bed!.Value.X + 0.5f)) < 0.6f && Math.Abs(sleeping.Pos.Z - (r.Bed.Value.Z + 0.5f)) < 1.1f,
            $"lies at {sleeping.Pos}, not in the bed at {r.Bed}");

        StandBeside(w, r.Id);
        int mark = w.Transport.Sent.Count;
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, r.Id);
        var bubble = w.Transport.Sent.Skip(mark).Select(s => s.Msg).OfType<NpcGreeting>().Last();
        Assert.Equal(En("npc.greet.sleepy"), bubble.Text);
    }

    // ---- trade ---------------------------------------------------------------------------------------------------

    private static string Craft(NpcLifeWorld w, string recipe)
    {
        int mark = w.Transport.Sent.Count;
        w.Server.Craft(w.Player.State.PlayerId, recipe);
        var res = w.Transport.Sent.Skip(mark).Select(x => x.Msg).OfType<CraftResult>().LastOrDefault();
        return res is null ? "(no result)" : res.Success ? "ok" : res.Reason ?? string.Empty;
    }

    [Fact]
    public void EachResident_TradesTheirOwnGoods_NearThem_AndNowhereElse()
    {
        using var w = new NpcLifeWorld(Content, "lodge_trade");
        var spots = new[] { (-22, -22), (2, -22), (-22, 2), (2, 2) };
        var lodges = Content.Lodges.Select((l, i) => (Profession: l, Key: Lodge(w, l.Key, beds: 1, dx: spots[i].Item1, dz: spots[i].Item2))).ToList();
        foreach (var (item, n) in new[] { ("iron_ore", 200), ("copper_ore", 200), ("silicate", 200), ("carbon", 200) })
        {
            w.Player.State.Inventory.Add(item, n, 999);
        }

        long day = w.Server.MarketDayForTest;
        var recipes = Content.Recipes.Values.ToList();
        foreach (var (prof, key) in lodges)
        {
            var r = Resident(w, key);
            StandBeside(w, r.Id);
            var own = MarketStock.ListFor(recipes, prof.Theme, day).Where(x => x.MarketTheme == prof.Theme).ToList();
            Assert.NotEmpty(own);
            foreach (var recipe in own.Where(x => x.Inputs.All(i => w.Player.State.Inventory.CountOf(i.Item) >= i.Count)).Take(2))
            {
                Assert.Equal("ok", Craft(w, recipe.Key));
            }

            var foreign = recipes.First(x => MarketStock.IsTrade(x) && x.MarketTheme.Length > 0 && x.MarketTheme != prof.Theme
                && Content.Lodges.Any(l => l.Theme == x.MarketTheme) && x.OfferedOnDay(day));
            Assert.Equal("@srv.craft.wrong_vendor", Craft(w, foreign.Key));
        }

        // Far from every resident: no lodge goods at all.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 10.5f, w.Feet, NpcLifeWorld.Cz - 10.5f);
        var gatherers = recipes.First(x => x.MarketTheme == "gatherers");
        Assert.NotEqual("ok", Craft(w, gatherers.Key));
    }

    // ---- talk ----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheJobDialogues_AreTheirOwn_ShortEnoughForThePanel_AndAFriendIsOfferedAPresent()
    {
        var lodgeDialogs = Content.Dialogs.Where(d => d.Key.StartsWith("lodge_", StringComparison.Ordinal)).ToList();
        foreach (var l in Content.Lodges)
        {
            var mine = lodgeDialogs.Where(d => d.Job == l.Key).ToList();
            Assert.Equal(new[] { "lodge_" + l.Key + "_friend", "lodge_" + l.Key }, mine.Select(d => d.Key)); // the friend's talk wins first
            Assert.All(mine, d => Assert.True(d.AiPrompt, d.Key + " may be re-worded on hosted worlds"));
            Assert.All(mine, d => Assert.Equal(DialogDefinition.HomeLodge, d.Home)); // a lodge resident's talk, nobody else's of that job
            Assert.All(mine, d => Assert.All(d.Nodes, n => Assert.InRange(n.Choices.Count, 1, 3))); // the client shows three replies
            Assert.Equal("trusted", mine[0].MinStage);
            Assert.Contains(mine[0].Nodes[0].Choices, c => c.Consequence == SvGameServer.LodgeGiftConsequence);
            Assert.DoesNotContain(mine[1].Nodes.SelectMany(n => n.Choices), c => c.Consequence == SvGameServer.LodgeGiftConsequence);
            Assert.Contains(mine[1].Nodes.SelectMany(n => n.Choices), c => c.Consequence == SvGameServer.LodgeTipConsequence);
        }

        Assert.Contains(lodgeDialogs.Where(d => d.Job == "gatherer").SelectMany(d => d.Nodes).SelectMany(n => n.Choices),
            c => c.Consequence == SvGameServer.LodgePoisonConsequence);

        using var w = new NpcLifeWorld(Content, "lodge_talk");
        string gatherer = Lodge(w, "gatherer");
        string miner = Lodge(w, "miner", dx: -22);
        var g = Resident(w, gatherer);
        var m = Resident(w, miner);

        StandBeside(w, g.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, g.Id);
        Assert.Equal("lodge_gatherer", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
        StandBeside(w, m.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, m.Id);
        Assert.Equal("lodge_miner", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);

        Befriend(w, g.PersonKey, g.Name);
        StandBeside(w, g.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, g.Id);
        Assert.Equal("lodge_gatherer_friend", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
    }

    [Fact]
    public void TheTip_PutsTheNearestHiddenFindOnTheMap_AndThenThereIsNothingNew()
    {
        using var w = new NpcLifeWorld(Content, "lodge_tip", configure: c => c.PlaceChests = false); // only the test's own cache
        string key = Lodge(w, "hunter");
        var r = Resident(w, key);
        string world = w.Player.CurrentLocationId;
        var chest = new Vector3i(NpcLifeWorld.Cx - 20, w.Feet, NpcLifeWorld.Cz + 20);
        w.Server.AddContainerForTest(new StoredContainer
        {
            Id = "loot_chest_lodge_test",
            Planet = world,
            Kind = "salvage",
            Position = chest,
            Items = new List<ItemStack> { new("iron_ore", 3) },
        });
        Assert.DoesNotContain(w.Server.PlanetPoisForTest(w.Player.State.PlayerId), p => p.Type == "treasure");

        StandBeside(w, r.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, r.Id);
        Assert.Equal("lodge_hunter", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 1); // "Seen anything interesting around here?"
        var said = LastDialog(w);
        Assert.True(said.End);
        Assert.StartsWith("Between us: there's a hidden cache about", said.Text, StringComparison.Ordinal);
        Assert.Contains(w.Server.PlanetPoisForTest(w.Player.State.PlayerId), p => p.Type == "treasure");

        w.Server.SkipDialogRewardCooldownForTest(NpcLifeWorld.Owner);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, r.Id);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 1);
        Assert.Equal(En("dlg.lodge.r_tip_none"), LastDialog(w).Text); // no wreck, no other cache, no camp, no village here
    }

    [Fact]
    public void AFriendsPresent_ComesFromTheCrate_OncePerInGameDay_AndIsNeverToxic()
    {
        using var w = new NpcLifeWorld(Content, "lodge_gift");
        string key = Lodge(w, "gatherer");
        var r = Resident(w, key);
        string store = w.Server.LodgesForTest.Single(l => l.Key == key).StoreId;
        w.Server.DepositIntoLodgeStoreForTest(key, "toxic_berries", 5);
        w.Server.DepositIntoLodgeStoreForTest(key, "berries", 6);
        Befriend(w, r.PersonKey, r.Name);
        var p = w.Player.State;
        int before = p.Inventory.CountOf("berries");

        string Ask()
        {
            StandBeside(w, r.Id);
            w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, r.Id);
            Assert.Equal("lodge_gatherer_friend", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
            w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Got anything to spare?"
            return LastDialog(w).Text;
        }

        string first = Ask();
        Assert.StartsWith(En("dlg.lodge_gatherer.r_gift"), first, StringComparison.Ordinal);
        Assert.EndsWith("+" + SvGameServer.LodgeGiftMax + " " + En("item.berries.name"), first, StringComparison.Ordinal);
        Assert.Equal(before + SvGameServer.LodgeGiftMax, p.Inventory.CountOf("berries"));
        Assert.Equal(0, p.Inventory.CountOf("toxic_berries"));
        var crate = w.Server.Containers.Single(c => c.Id == store).Items;
        Assert.Equal(6 - SvGameServer.LodgeGiftMax, crate.Single(s => s.Item == "berries").Count); // really out of the crate
        Assert.Equal(5, crate.Single(s => s.Item == "toxic_berries").Count);

        Assert.Equal(En("dlg.lodge.r_gift_again"), Ask()); // once per in-game day
        Assert.Equal(before + SvGameServer.LodgeGiftMax, p.Inventory.CountOf("berries"));

        w.Server.AdvanceSystemDaysForTest(1.0);
        Ask(); // the rest of the berries
        Assert.Equal(before + 6, p.Inventory.CountOf("berries"));
        Assert.Single(p.Milestones, m => m.StartsWith(SvGameServer.LodgeGiftMilestonePrefix, StringComparison.Ordinal)); // only the latest day

        w.Server.AdvanceSystemDaysForTest(1.0);
        Assert.Equal(En("dlg.lodge.r_gift_empty"), Ask()); // only the toxic berries are left — never given away
        Assert.Equal(0, p.Inventory.CountOf("toxic_berries"));
    }

    [Fact]
    public void TheGatherer_WarnsAboutThisWorldsPoisonousPlants()
    {
        using var w = new NpcLifeWorld(Content, "lodge_poison");
        string key = Lodge(w, "gatherer");
        var r = Resident(w, key);
        StandBeside(w, r.Id);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, r.Id);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "What do you do out here?" — and on to the plants
        Assert.Equal(1, w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Node);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Which plants should I stay away from?"
        string said = LastDialog(w).Text;
        string warning = En("dlg.lodge_gatherer.r_poison");
        string prefix = warning.Substring(0, warning.IndexOf("{0}", StringComparison.Ordinal));
        Assert.True(said == En("dlg.lodge_gatherer.r_poison_none") || (said.StartsWith(prefix, StringComparison.Ordinal) && !said.Contains("{0}")), said);
    }

    // ---- villagers know them -------------------------------------------------------------------------------------

    [Fact]
    public void AVillagersHint_NamesTheResident_AndPutsTheHutOnTheMap()
    {
        using var w = new NpcLifeWorld(Content, "lodge_hint");
        string key = Lodge(w, "woodcutter");
        var r = Resident(w, key);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 40f, w.Feet, NpcLifeWorld.Cz);

        string hint = w.Server.LodgeHintForTest(NpcLifeWorld.Owner);
        Assert.StartsWith(r.Name + " the " + En("npc.role.woodcutter") + " lives in a hut", hint, StringComparison.Ordinal);
        Assert.Contains("m to the east", hint, StringComparison.Ordinal);
        Assert.Contains(w.Server.PlanetPoisForTest(w.Player.State.PlayerId), p => p.Type == "lodge");
        Assert.Equal(string.Empty, w.Server.LodgeHintForTest(NpcLifeWorld.Owner)); // already on the map

        Assert.Equal(new[] { "{a}", "{b}", "{place}", "{npc}" }, VillageChatterNeeds.TokensFor(VillageChatterNeeds.LodgeNear));
        Assert.Contains(Content.VillageLife.Chatter, t => t.Needs == VillageChatterNeeds.LodgeNear && t.Lines.Count >= 2);
    }
}
