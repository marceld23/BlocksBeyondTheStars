// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2688 / #2693: mission boards roll per world and per slot without a four-step echo, a settlement's board, mission ids
/// and NPC memory belong to its body (not to its name), and nothing a player already holds or knows is lost on the way —
/// held board jobs keep their old definition, old relationships move onto the new keys.
/// </summary>
public sealed class MissionBoardVarietyTests : IClassFixture<MissionBoardVarietyTests.BoardWorld>
{
    private const int GatherTemplates = 7; // GiverMissionTemplates.Length — the board's gather pool

    /// <summary>Every settlement name the generator can coin (roots × the four tiers' suffixes).</summary>
    private static readonly string[] SettlementNames =
        new[] { "Karth", "Vega", "Mira", "Dorn", "Ysel", "Tarn", "Olun", "Reth", "Sabik", "Cael" }
            .SelectMany(r => new[] { " City", " Metropolis", " Prime", " Central", " Town", " Colony", " Outpost", " Heights",
                " Hamlet", " Cross", " Camp", " Rest", " Village", " Hollow", " Glen", " Stead", " End" }.Select(s => r + s))
            .ToArray();

    private readonly BoardWorld _w;

    public MissionBoardVarietyTests(BoardWorld w) => _w = w;

    // ---------------- Pure: rolls and keys ----------------

    /// <summary>The share of boards whose gather pick of slot k+4 equals that of slot k (k = 0..3).</summary>
    private static double FourStepEcho(Func<string, int, System.Random> rngFor, IEnumerable<string> boards)
    {
        int pairs = 0, equal = 0;
        foreach (string board in boards)
        {
            var picks = Enumerable.Range(0, 8).Select(slot => rngFor(board, slot).Next(GatherTemplates)).ToArray();
            for (int k = 0; k < 4; k++)
            {
                pairs++;
                equal += picks[k] == picks[k + 4] ? 1 : 0;
            }
        }

        return equal / (double)pairs;
    }

    [Fact]
    public void ConsecutiveBoardSlots_AreNotPeriodic()
    {
        const long Seed = 4242;
        var boards = SettlementNames.Select(n => SvGameServer.SettlementLocationKeyFor("sys0-p1", n, Seed)).ToList();

        double legacy = FourStepEcho((b, slot) => SvGameServer.BoardRng(b, "mission", slot, legacy: true, Seed), SettlementNames);
        double mixed = FourStepEcho((b, slot) => SvGameServer.BoardRng(b, "mission", slot, legacy: false, Seed), boards);

        // The old seeding echoed after four slots far more often than chance (the bug); the new one is at chance (1/7).
        Assert.True(legacy > 0.5, $"the old rolls echo at {legacy:P0} — the test no longer measures the bug");
        Assert.InRange(mixed, 0.07, 0.22);
    }

    [Fact]
    public void LegacyRolls_StayBitIdentical_ToTheOldSeeding()
    {
        // A held board job is coined again from its id after every load — its old roll must not move by a single draw.
        foreach (string name in SettlementNames.Take(20))
        {
            for (int slot = 0; slot < 5; slot++)
            {
                var old = new System.Random(unchecked((int)WorldGenerator.StableHash($"{name}:mission:{slot}")));
                var now = SvGameServer.BoardRng(name, "mission", slot, legacy: true, worldSeed: 99);
                Assert.Equal(old.Next(), now.Next());
                Assert.Equal(old.Next(-2, 4), now.Next(-2, 4));
            }
        }
    }

    [Fact]
    public void TwoWorlds_GiveDifferentBoards_ForTheSameSettlementName()
    {
        int differing = 0;
        foreach (string name in SettlementNames)
        {
            int[] Board(long seed)
            {
                string key = SvGameServer.SettlementLocationKeyFor("sys0-p1", name, seed);
                return Enumerable.Range(0, 6).Select(slot => SvGameServer.BoardRng(key, "mission", slot, legacy: false, seed).Next(GatherTemplates)).ToArray();
            }

            Assert.NotEqual(SvGameServer.SettlementLocationKeyFor("sys0-p1", name, 1), SvGameServer.SettlementLocationKeyFor("sys0-p1", name, 2));
            differing += Board(1).SequenceEqual(Board(2)) ? 0 : 1;
        }

        // Six picks out of seven agree by chance in about 1 board of 117 000 — every board here must differ.
        Assert.Equal(SettlementNames.Length, differing);
    }

    [Fact]
    public void SameName_OnTwoBodies_GetsTwoKeys_ThatNeverLookOld()
    {
        string a = SvGameServer.SettlementLocationKeyFor("sys0-p1", "Karth Village", 7);
        string b = SvGameServer.SettlementLocationKeyFor("sys0-p1-m0", "Karth Village", 7);

        Assert.NotEqual(a, b);
        Assert.Equal(a, SvGameServer.SettlementLocationKeyFor("sys0-p1", "Karth Village", 7)); // stable
        foreach (string key in new[] { a, b })
        {
            Assert.Matches("^settle_[1-9][0-9]{8}$", key);
            Assert.False(SvGameServer.IsLegacySettlementPlace(key));
        }

        Assert.True(SvGameServer.IsLegacySettlementPlace(SvGameServer.LegacySettlementPlaceKeyForTest("Karth Village")));
        Assert.Equal($"settle_{(uint)WorldGenerator.StableHash("Karth Village") % 100000u}", SvGameServer.LegacySettlementPlaceKeyForTest("Karth Village")); // the old formula, read for held jobs
        Assert.False(SvGameServer.IsLegacySettlementPlace("settle_123_bounty"));
        Assert.False(SvGameServer.IsLegacySettlementPlace("station_123"));
    }

    // ---------------- Server: held jobs, separate memory, migration ----------------

    [Fact]
    public void BoardJob_TakenBeforeTheChange_KeepsItsDefinition_AndCanBeTurnedIn()
    {
        var server = _w.Server;
        var p = _w.PlayerAtBoard("Holder");
        string legacyPrefix = SvGameServer.LegacySettlementPlaceKeyForTest(_w.Name) + "_";
        string gather = legacyPrefix + "1", build = legacyPrefix + "b0", order = legacyPrefix + "c0";
        foreach (string id in new[] { gather, build, order })
        {
            p.State.Missions.Add(new MissionProgress { MissionId = id, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });
        }

        // The window run is exactly what a reload does: no definition in memory, coined again from the id.
        var visible = server.VisibleMissionIdsForTest("Holder");

        var def = server.MissionDefForTest(gather);
        Assert.NotNull(def);
        var (need, required) = LegacyGather(_w.Name, 1);
        Assert.Equal(need, def!.Objectives[0].Target);
        Assert.Equal(required, def.Objectives[0].Required);
        Assert.NotNull(server.MissionDefForTest(build));
        Assert.Equal(_w.Name + ":bigorder:0", server.MissionDefForTest(order)!.ChainId); // the old chain, both steps
        Assert.NotNull(server.MissionDefForTest(legacyPrefix + "c1"));

        // The board itself offers the new ids only — the old stream offers nothing new.
        string newPrefix = server.SettlementLocationKeyForTest(_w.Name) + "_";
        Assert.Contains(visible, id => id.StartsWith(newPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(visible, id => id.StartsWith(legacyPrefix, StringComparison.Ordinal));

        p.State.Inventory.Add(need, required + 5, 99);
        server.TurnInMission("Holder", gather);
        Assert.Equal(MissionStatus.TurnedIn, p.State.Missions.First(m => m.MissionId == gather).Status);
    }

    [Fact]
    public void SameNameOnAnotherBody_HasItsOwnMemory()
    {
        var server = _w.Server;
        var p = _w.PlayerAtBoard("Twin");
        int qm = server.PersonKeysForTest.First(n => n.Role == "quartermaster" && n.Settlement == _w.Name).Id;
        string here = server.SettlementLocationKeyForTest(_w.Name);
        string elsewhere = SvGameServer.SettlementLocationKeyFor("sys9-p9", _w.Name, _w.Seed);
        Assert.NotEqual(here, elsewhere);

        // A friend at the namesake on another body is a stranger here …
        p.State.NpcMemory[elsewhere + ":quartermaster"] = new NpcRelationship { Name = "Elsewhere", Role = "quartermaster", Value = 60 };
        Assert.Equal(string.Empty, server.NpcStageForTest("Twin", qm));

        // … and the quartermaster of THIS settlement reads this settlement's entry.
        p.State.NpcMemory[here + ":quartermaster"] = new NpcRelationship { Name = "Here", Role = "quartermaster", Value = 60 };
        Assert.NotEqual(string.Empty, server.NpcStageForTest("Twin", qm));
    }

    [Fact]
    public void OldNameOnlyKeys_AreFound_AndMovedOntoThisBody()
    {
        var server = _w.Server;
        var p = _w.PlayerAtBoard("Returner");
        string legacy = SvGameServer.LegacySettlementPlaceKeyForTest(_w.Name);
        string place = server.SettlementLocationKeyForTest(_w.Name);
        string person = "#bed@1.2.3:settler";
        string stranger = SvGameServer.LegacySettlementPlaceKeyForTest("Nowhere Glen") + ":quartermaster";
        string wishId = "wish_" + legacy.Substring("settle_".Length) + "_0badcafe_0_0";

        p.State.NpcMemory[legacy + ":quartermaster"] = new NpcRelationship { Name = "Old Friend", Role = "quartermaster", Value = 40 };
        p.State.NpcMemory[legacy + person] = new NpcRelationship { Name = "Tomas", Role = "settler", Value = 20 };
        // The old LocationKeyOfMission cut a camp bounty's id at its LAST underscore: a stray quartermaster entry.
        string stray = legacy + "_bounty_camp:quartermaster";
        p.State.NpcMemory[stray] = new NpcRelationship
        {
            Name = "Old Friend",
            Role = "quartermaster",
            Value = 6,
            Log = { new NpcInteraction { Kind = NpcInteractionKind.MissionAccepted }, new NpcInteraction { Kind = NpcInteractionKind.MissionAccepted } },
        };
        p.State.NpcMemory[stranger] = new NpcRelationship { Name = "Far Away", Role = "quartermaster", Value = 30 };
        p.State.Milestones.Add("npcmig:" + legacy);
        p.State.Wishes[legacy] = new WishLedger { Lanes = { new WishLane { N = 2, Giver = legacy + person, LastGiver = legacy + "#bed@4.5.6:settler" } } };
        p.State.Missions.Add(new MissionProgress { MissionId = "chain_needs_2", ChainId = "settlement_needs", AcceptedFrom = legacy, Status = MissionStatus.TurnedIn });
        p.State.Missions.Add(new MissionProgress { MissionId = wishId, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });
        server.AddMissionDefForTest(new MissionDefinition { Id = wishId, Source = MissionSource.System, Active = true });

        server.MigrateLegacySettlementKeysForTest("Returner");

        Assert.Equal(46, server.RelationshipValueForTest("Returner", place + ":quartermaster")); // 40 + the stray bounties' 6
        Assert.Equal(2, p.State.NpcMemory[place + ":quartermaster"].Log.Count);
        Assert.False(p.State.NpcMemory.ContainsKey(stray));
        Assert.Equal(20, server.RelationshipValueForTest("Returner", place + person));
        Assert.DoesNotContain(p.State.NpcMemory.Keys, k => k.StartsWith(legacy, StringComparison.Ordinal));
        Assert.True(p.State.NpcMemory.ContainsKey(stranger)); // a name that is not on this body waits for its own body
        Assert.Contains("npcmig:" + place, p.State.Milestones);
        Assert.DoesNotContain("npcmig:" + legacy, p.State.Milestones);

        var ledger = Assert.Contains(place, (IDictionary<string, WishLedger>)p.State.Wishes);
        Assert.False(p.State.Wishes.ContainsKey(legacy));
        Assert.Equal(place + person, ledger.Lanes[0].Giver);
        Assert.Equal(place + "#bed@4.5.6:settler", ledger.Lanes[0].LastGiver);

        Assert.Equal(place, p.State.Missions.First(m => m.MissionId == "chain_needs_2").AcceptedFrom);
        string movedWish = "wish_" + place.Substring("settle_".Length) + "_0badcafe_0_0";
        Assert.Contains(p.State.Missions, m => m.MissionId == movedWish);
        Assert.Equal(movedWish, server.MissionDefForTest(movedWish)?.Id);
        Assert.Null(server.MissionDefForTest(wishId));

        // Once moved, a second world entry has nothing left to do.
        var before = p.State.NpcMemory.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        server.MigrateLegacySettlementKeysForTest("Returner");
        Assert.Equal(before, p.State.NpcMemory.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void HeldOldSurvey_CountsAwayFromTheBoard_AlsoAfterARestart()
    {
        var server = _w.Server;
        var p = server.AddLocalPlayer("Surveyor");
        p.State.Position = new Shared.Geometry.Vector3f(_w.BoardPos.X + 1500f, _w.BoardPos.Y, _w.BoardPos.Z + 1500f); // far from any board
        string survey = SvGameServer.LegacySettlementPlaceKeyForTest(_w.Name) + "_s0";
        string newGather = server.SettlementLocationKeyForTest(_w.Name) + "_7"; // a held new job beyond the stocked window
        p.State.Missions.Add(new MissionProgress { MissionId = survey, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });
        p.State.Missions.Add(new MissionProgress { MissionId = newGather, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });

        for (int restart = 0; restart < 2; restart++)
        {
            // A restart forgets every coined board job; the world entry gives the held ones back, wherever the player stands.
            server.ForgetMissionDefForTest(survey);
            server.ForgetMissionDefForTest(newGather);
            server.MigrateNpcMemoryForTest("Surveyor");
            var def = server.MissionDefForTest(survey);
            Assert.NotNull(def);
            Assert.NotNull(server.MissionDefForTest(newGather));

            var obj = def!.Objectives[0];
            string target = obj.Target;
            int colon = target.IndexOf(':');
            server.SimulateScanForTest(p, colon > 0 ? target[..colon] : target, "anything", hostile: target.EndsWith(":hostile", StringComparison.Ordinal), firstTime: true);
            Assert.Equal(Math.Min(restart + 1, obj.Required), p.State.Missions.First(m => m.MissionId == survey).ObjectiveProgress[0]);
        }
    }

    [Fact]
    public void WishLedgers_Merge_WhenTheNewPlaceHasOneAlready()
    {
        var server = _w.Server;
        var p = _w.PlayerAtBoard("Merger");
        string legacy = SvGameServer.LegacySettlementPlaceKeyForTest(_w.Name);
        string place = server.SettlementLocationKeyForTest(_w.Name);
        string Wish(string key, int lane, int n) => "wish_" + key.Substring("settle_".Length) + "_0decade1_" + lane + "_" + n;

        p.State.Wishes[place] = new WishLedger { Done = 1, Lanes = { new WishLane { N = 3, LastGiver = place + "#bed@9.9.9:settler" } } };
        p.State.Wishes[legacy] = new WishLedger
        {
            Done = 2,
            Lanes = { new WishLane { N = 1, Giver = legacy + "#bed@1.1.1:settler" }, new WishLane { N = 4, Giver = legacy + "#bed@2.2.2:settler" } },
        };
        foreach (string id in new[] { Wish(legacy, 0, 1), Wish(legacy, 1, 4) })
        {
            p.State.Missions.Add(new MissionProgress { MissionId = id, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });
            server.AddMissionDefForTest(new MissionDefinition { Id = id, Source = MissionSource.System, Active = true });
        }

        server.MigrateLegacySettlementKeysForTest("Merger");

        Assert.False(p.State.Wishes.ContainsKey(legacy));
        var ledger = p.State.Wishes[place];
        Assert.Equal(3, ledger.Done);
        Assert.Equal(2, ledger.Lanes.Count);
        Assert.Equal(place + "#bed@1.1.1:settler", ledger.Lanes[0].Giver); // the held wish comes over …
        Assert.Equal(3, ledger.Lanes[0].N);                                  // … on the higher counter (no id minted twice)
        Assert.Equal(place + "#bed@2.2.2:settler", ledger.Lanes[1].Giver);
        foreach (string id in new[] { Wish(place, 0, 3), Wish(place, 1, 4) })
        {
            Assert.Contains(p.State.Missions, m => m.MissionId == id);
            Assert.Equal(id, server.MissionDefForTest(id)?.Id);
        }

        Assert.DoesNotContain(p.State.Missions, m => m.MissionId.StartsWith("wish_" + legacy.Substring("settle_".Length) + "_", StringComparison.Ordinal));
    }

    [Fact]
    public void WishLedgers_Merge_KeepsTheNewHeldWish_WhenBothLanesHoldOne()
    {
        var server = _w.Server;
        var p = _w.PlayerAtBoard("Dropper");
        string legacy = SvGameServer.LegacySettlementPlaceKeyForTest(_w.Name);
        string place = server.SettlementLocationKeyForTest(_w.Name);
        string oldWish = "wish_" + legacy.Substring("settle_".Length) + "_0beefed2_0_1";
        string newWish = "wish_" + place.Substring("settle_".Length) + "_0beefed2_0_5";

        p.State.Wishes[place] = new WishLedger { Lanes = { new WishLane { N = 5, Giver = place + "#bed@3.3.3:settler" } } };
        p.State.Wishes[legacy] = new WishLedger { Lanes = { new WishLane { N = 1, Giver = legacy + "#bed@1.1.1:settler" } } };
        foreach (string id in new[] { oldWish, newWish })
        {
            p.State.Missions.Add(new MissionProgress { MissionId = id, Status = MissionStatus.Active, ObjectiveProgress = { 0 } });
            server.AddMissionDefForTest(new MissionDefinition { Id = id, Source = MissionSource.System, Active = true });
        }

        server.MigrateLegacySettlementKeysForTest("Dropper");

        Assert.False(p.State.Wishes.ContainsKey(legacy)); // no orphaned ledger keeps the migration busy forever
        Assert.Equal(place + "#bed@3.3.3:settler", p.State.Wishes[place].Lanes[0].Giver);
        Assert.Contains(p.State.Missions, m => m.MissionId == newWish);
        Assert.DoesNotContain(p.State.Missions, m => m.MissionId == oldWish);
        Assert.Null(server.MissionDefForTest(oldWish));
    }

    [Fact]
    public void OldNews_MovesOntoThePlacesBodyAwareKey()
    {
        var server = _w.Server;
        _w.PlayerAtBoard("Reader");
        int reporter = server.SpawnVillagerForTest(_w.Name, _w.BoardPos, job: "reporter");
        server.AddNewsArticleForTest("s:" + _w.Name, "Oldie", "A story from before the update.", at: 0);

        Assert.Contains("• Oldie: A story from before the update.", server.NewsDigestForTest("Reader", reporter));
        Assert.Contains(server.NewsForTest, a => a.Place == "s:" + server.SettlementLocationKeyForTest(_w.Name) && a.PlayerName == "Oldie");
        Assert.DoesNotContain(server.NewsForTest, a => a.Place == "s:" + _w.Name);
    }

    [Fact]
    public void ChainGiver_IsTheSettlementsQuestGiver_InANewWorld()
    {
        // #2698: in a world of generation 26 (this fixture) a chain giver nobody named yet is the settlement's own quest
        // giver, coined from its place — the board's quartermaster, whose memory entry the chain shares. A world of
        // generation 25 or older keeps the name the old name-only key coined (SettlementTradeAndGiverTests).
        var server = _w.Server;
        string place = server.SettlementLocationKeyForTest(_w.Name);
        string giver = SvGameServer.SettlementGiverNameFor(_w.Name, place, BlocksBeyondTheStars.Shared.World.WorldDescription.CurrentTerrainGeneration);
        Assert.Equal(giver, server.SettlementTradeAndGiverForTest(_w.Name).Giver);
        Assert.Equal(giver, server.ChainGiverFallbackNameForTest(place));
    }

    /// <summary>
    /// Known answers: the place key and the new board rolls are SAVE DATA (memory keys, mission ids, wish ledgers; a held
    /// job is coined again from its id after every load). Any change here renames people and morphs held jobs in every
    /// save — it must be a deliberate migration, never an accident.
    /// </summary>
    [Fact]
    public void PlaceKeysAndBoardRolls_ArePinned()
    {
        Assert.Equal("settle_473276369", SvGameServer.SettlementLocationKeyFor("sys0-p1", "Karth Village", 4242));
        Assert.Equal("settle_135404181", SvGameServer.SettlementLocationKeyFor("sys3-p2-m0", "Vega Town", -7));
        Assert.Equal("settle_102956756", SvGameServer.SettlementLocationKeyFor("sys0-p0", "Mira Hamlet", 1));

        string picks = string.Join(",", Enumerable.Range(0, 8).Select(slot =>
            SvGameServer.BoardRng("settle_512345678", "mission", slot, legacy: false, 4242).Next(GatherTemplates)));
        Assert.Equal("5,1,4,1,5,1,2,5", picks);
        Assert.Equal(2045631227, SvGameServer.BoardRng("sys0-st", "scanmission", 3, legacy: false, 4242).Next());
        Assert.Equal(1625932830, SvGameServer.BoardRng("settle_512345678", "bounty", "1:2", legacy: false, 4242).Next());
    }

    /// <summary>The pre-#2688 gather roll, restated from the old code: template = Next(7), required = max(3, target + Next(-2, 4)).</summary>
    private static (string Need, int Required) LegacyGather(string boardKey, int slot)
    {
        (string Need, int Target)[] templates =
        {
            ("iron_ore", 10), ("carbon", 8), ("silicate", 8), ("copper_ore", 10), ("crystal", 5), ("titanium_ore", 6), ("data_fragment", 3),
        };
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"{boardKey}:mission:{slot}")));
        var tpl = templates[rng.Next(templates.Length)];
        return (tpl.Need, Math.Max(3, tpl.Target + rng.Next(-2, 4)));
    }

    /// <summary>One settlement-only world with an inhabited settlement and its board, shared by the server tests (one boot).</summary>
    public sealed class BoardWorld : IDisposable
    {
        private readonly SettlementTestWorlds _worlds = new();
        private readonly SqliteWorldRepository _repo;

        public BoardWorld()
        {
            var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
            Server = _worlds.Start(content, "jungle", Seed, out _repo);
            Assert.True(Server.InhabitedSettlementCount > 0, $"jungle seed {Seed} should carry an inhabited settlement");
            BoardPos = Server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;

            var probe = Server.AddLocalPlayer("Probe");
            probe.State.Position = BoardPos;
            string here = Server.CurrentPlaceKeyForTest("Probe");
            Name = Server.SettlementNamesForTest.First(n => Server.SettlementLocationKeyForTest(n) == here);
        }

        public long Seed => 1;

        public SvGameServer Server { get; }

        public Shared.Geometry.Vector3f BoardPos { get; }

        /// <summary>The settlement whose board <see cref="BoardPos"/> is.</summary>
        public string Name { get; }

        public BlocksBeyondTheStars.GameServer.PlayerSession PlayerAtBoard(string name)
        {
            var p = Server.AddLocalPlayer(name);
            p.State.Position = BoardPos;
            return p;
        }

        public void Dispose()
        {
            _repo.Dispose();
            _worlds.Dispose();
        }
    }
}
