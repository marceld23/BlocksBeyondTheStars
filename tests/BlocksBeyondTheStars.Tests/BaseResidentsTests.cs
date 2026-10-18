// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Base residents (#1865, Marcel 2026-09-13): every bed inside a base brings one more resident — 1 + beds, at most
/// the cap (five at first, the city tier's 32 since #2775); a bed counts in the core zone, in a sealed room, inside
/// the walled yard or in a closed room (walls, roof, door), never in the open field; the founding settler comes for a bed or a few machines inside (#2677). A trading post and a mission board
/// inside the base are staffed by a resident — barter and board jobs at home — and a post nobody can staff says why.
/// Residents are distinct people on the roster, keyed by base and slot; the founding settler keeps its old key.
/// </summary>
public sealed class BaseResidentsTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void NoBed_OneSettler_EveryBedOneMore_UpToTheCap()
    {
        using var w = new NpcLifeWorld(_content, "residents_count");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        Assert.Single(w.Server.BaseResidentsForTest(baseId));

        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Bed(NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        var three = w.Server.BaseResidentsForTest(baseId);
        Assert.Equal(3, three.Count);
        Assert.Equal(2, w.Server.BaseFurnitureForTest(baseId).Beds); // a two-cell bed is ONE bed, its foot never counts

        // #2775: up to the cap (the city tier, 32 — it was five) and not one more: two rows of beds across the core zone,
        // one bed more than the cap — still the cap's number of people.
        int cap = SvGameServer.BaseResidentCapForTest;
        Assert.Equal(32, cap);
        int beds = 2;
        foreach (int z in new[] { NpcLifeWorld.Cz + 1, NpcLifeWorld.Cz + 4 })
        {
            for (int x = NpcLifeWorld.Cx - 8; x <= NpcLifeWorld.Cx + 8 && beds < cap + 1; x++)
            {
                if (z == NpcLifeWorld.Cz + 1 && (x == NpcLifeWorld.Cx - 5 || x == NpcLifeWorld.Cx - 3))
                {
                    continue; // the two beds above
                }

                w.Bed(x, z);
                beds++;
            }
        }

        w.Server.ScanBaseLifeForTest();
        Assert.Equal(cap + 1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(cap, w.Server.BaseResidentsForTest(baseId).Count);
        Assert.Equal(Enumerable.Range(0, cap), w.Server.BaseResidentsForTest(baseId).Select(r => r.Slot).OrderBy(s => s));

        // Each resident is a different person (seeded per slot), each on the owner's roster under its own key; the
        // founding settler keeps the key it always had.
        var roster = w.Player.State.NpcMemory.Where(kv => kv.Key.StartsWith($"base_{baseId}", StringComparison.Ordinal)).ToList();
        Assert.Equal(cap, roster.Count);
        Assert.Equal(5, roster.Take(5).Select(kv => kv.Value.Name).Distinct().Count());
        Assert.Contains($"base_{baseId}:settler", w.Player.State.NpcMemory.Keys);
        Assert.Contains($"base_{baseId}#1:settler", w.Player.State.NpcMemory.Keys);
    }

    [Fact]
    public void BedsGo_AndTheirSleepersMoveOn()
    {
        using var w = new NpcLifeWorld(_content, "residents_leave");
        int baseId = w.FoundBase();
        var bed = w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
        string sleeper = w.Server.BaseResidentNamesForTest(baseId).Single(r => r.Slot == 1).Name;
        Assert.DoesNotContain(w.MessagesToPlayer(), m => m.StartsWith("@srv.base.resident_left", StringComparison.Ordinal));

        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z);
        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z + 1);
        w.Server.ScanBaseLifeForTest();
        var left = Assert.Single(w.Server.BaseResidentsForTest(baseId));
        Assert.Equal(0, left.Slot); // the founding settler stays

        // #2715: the owner hears who moved out and why — it used to happen silently.
        Assert.Single(w.MessagesToPlayer(), m => m == "@srv.base.resident_left:" + sleeper);
    }

    [Fact]
    public void WhoLivesHere_ListsTheFounderAndTheBedResident_WithJobBedAndWhereabouts()
    {
        using var w = new NpcLifeWorld(_content, "residents_list");
        int baseId = w.FoundBase();
        Assert.Equal(SvGameServer.BaseResidentCapForTest, w.Server.BaseResidentListForTest(NpcLifeWorld.Owner).Cap);

        var bed = w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "workbench");
        w.Server.ScanBaseLifeForTest();

        // #2717: the founding settler first, then the resident the bed brought — names as on the nameplates.
        var list = w.Server.BaseResidentListForTest(NpcLifeWorld.Owner);
        Assert.Equal(1, list.Beds);
        Assert.Equal(2, list.People.Length);
        var names = w.Server.BaseResidentNamesForTest(baseId);
        Assert.True(list.People[0].Founder);
        Assert.Equal(names.Single(r => r.Slot == 0).Name, list.People[0].Name);
        Assert.False(list.People[1].Founder);
        Assert.Equal(names.Single(r => r.Slot == 1).Name, list.People[1].Name);

        // One took the workbench (a craftsman), one sleeps in the bed — the other has none yet.
        Assert.Single(list.People, p => p.RoleKey == "npc.role.craftsman");
        Assert.Single(list.People, p => p.HasBed);
        Assert.All(list.People, p =>
        {
            Assert.True(p.Here);
            Assert.False(string.IsNullOrEmpty(p.ActivityKey));
            Assert.True(p.DirectionKey.Length == 0 ? p.Distance < 3 : p.DirectionKey.StartsWith("dir.", StringComparison.Ordinal));
        });

        // At night the bed's sleeper is listed asleep.
        var sleeper = w.Server.BaseResidentsForTest(baseId).Single(r => r.Bed == bed);
        w.TickAt(0.9, 60, until: () => w.Server.NpcRoutineForTest(sleeper.NpcId).Pose == 2);
        Assert.Contains(w.Server.BaseResidentListForTest(NpcLifeWorld.Owner).People, p => p.HasBed && p.ActivityKey == "npc.activity.sleeping");

        // A resident whose NPC is not on the world right now is "not here right now" — still listed, by the name the owner knows.
        string founder = list.People[0].Name;
        w.Server.RemoveNpcForTest(w.Server.BaseSettlerForTest(baseId)!.Value);
        var away = w.Server.BaseResidentListForTest(NpcLifeWorld.Owner).People[0];
        Assert.False(away.Here);
        Assert.Equal(founder, away.Name);
    }

    [Fact]
    public void ABedInTheOpenField_BringsNobody_TheSameBedInAClosedHut_Does()
    {
        using var w = new NpcLifeWorld(_content, "residents_hut");
        int baseId = w.FoundBase();

        // 16 blocks out: beyond the core zone, no walls, no roof.
        var bed = w.Bed(NpcLifeWorld.Cx + 16, NpcLifeWorld.Cz);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(0, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Single(w.Server.BaseResidentsForTest(baseId));

        // Build a closed hut around the bed (walls, roof, no gap): now it is a home.
        w.Hut(NpcLifeWorld.Cx + 16, NpcLifeWorld.Cz, half: 3, doorway: false);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Bed == bed);
    }

    [Fact]
    public void ABedInsideTheWalledYard_Counts()
    {
        using var w = new NpcLifeWorld(_content, "residents_yard", padHalf: 24);
        int baseId = w.FoundBase();
        w.WallRing(14);
        w.Bed(NpcLifeWorld.Cx + 11, NpcLifeWorld.Cz - 2); // outside the core zone, inside the ring
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
    }

    [Fact]
    public void OneBedInside_FoundsTheBase_WithoutAnyMachine_AndTheFounderStaysWhenItGoes()
    {
        using var w = new NpcLifeWorld(_content, "residents_nomachines");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core");
        int baseId = w.Server.BaseSnapshots.Single().Id;
        w.Server.ScanBaseLifeForTest();
        Assert.Empty(w.Server.BaseResidentsForTest(baseId)); // a bare claim attracts nobody

        // #2677: one bed inside the base is enough for the first settler — and, like every bed, it brings one more.
        var bed = w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(new[] { 0, 1 }, w.Server.BaseResidentsForTest(baseId).Select(r => r.Slot).OrderBy(s => s));

        // A founded base stays founded: the bed goes, its sleeper moves on, the founding settler stays.
        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z);
        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(0, Assert.Single(w.Server.BaseResidentsForTest(baseId)).Slot);
    }

    [Fact]
    public void ATradingPostAtHome_IsStaffed_AndBarterWorksBesideIt()
    {
        using var w = new NpcLifeWorld(_content, "residents_vendor");
        int baseId = w.FoundBase();
        var post = new Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "station_vendor");
        w.Server.ScanBaseLifeForTest();

        var vendor = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "vendor");
        Assert.Equal("vendor", vendor.Role);

        w.Player.State.Position = new Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        Assert.True(w.Server.MarketAvailableForTest(w.Player.State), "barter at the base's trading post");
        w.Player.State.Position = new Vector3f(post.X - 12.5f, w.Feet, post.Z + 0.5f);
        Assert.False(w.Server.MarketAvailableForTest(w.Player.State), "no barter across the yard");

        // The post goes: the trader goes back to being a settler, barter ends.
        w.Server.RemoveBlockForTest(post.X, post.Y, post.Z);
        w.Server.ScanBaseLifeForTest();
        Assert.DoesNotContain(w.Server.BaseResidentsForTest(baseId), r => r.Job == "vendor");
        w.Player.State.Position = new Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        Assert.False(w.Server.MarketAvailableForTest(w.Player.State));
    }

    [Fact]
    public void AMissionBoardAtHome_OffersJobs_TakenOnlyAtTheBoard()
    {
        using var w = new NpcLifeWorld(_content, "residents_board");
        int baseId = w.FoundBase();
        var board = new Vector3i(NpcLifeWorld.Cx - 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(board.X, board.Y, board.Z, "mission_board");
        w.Server.ScanBaseLifeForTest();
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Job == "quartermaster");

        string mission = w.Server.BaseBoardMissionIdsForTest(baseId).First(id => id.StartsWith("home_", StringComparison.Ordinal));

        w.Player.State.Position = new Vector3f(board.X + 12.5f, w.Feet, board.Z + 0.5f);
        w.Server.AcceptMission(NpcLifeWorld.Owner, mission);
        Assert.DoesNotContain(w.Player.State.Missions, m => m.MissionId == mission);

        w.Player.State.Position = new Vector3f(board.X + 1.5f, w.Feet, board.Z + 0.5f);
        w.Server.AcceptMission(NpcLifeWorld.Owner, mission);
        Assert.Contains(w.Player.State.Missions, m => m.MissionId == mission);
    }

    [Fact]
    public void APostOutsideTheBase_OrWithNobodyToStaffIt_SaysWhy()
    {
        using var w = new NpcLifeWorld(_content, "residents_posthint");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core");

        // No resident yet: placed inside the zone.
        w.Player.State.Inventory.Add("station_vendor", 2, 16);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 2.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx + 3, w.Feet, NpcLifeWorld.Cz + 4, "station_vendor");
        Assert.Contains("@srv.base.post_no_resident", w.MessagesToPlayer());

        // In the open, 16 blocks out.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 15.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx + 17, w.Feet, NpcLifeWorld.Cz, "station_vendor");
        Assert.Contains("@srv.base.post_outside", w.MessagesToPlayer());
    }

    [Fact]
    public void TheResidentList_IsAnsweredOverTheWire_AlsoWhileTheWorldIsHeld()
    {
        using var w = new NpcLifeWorld(_content, "residents_list_wire");
        w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        string body = w.Server.ActiveLocationId;
        BaseResidentList? Answer()
            => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<BaseResidentList>().LastOrDefault();

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new RequestBaseResidentsIntent { BodyId = body }));
        var list = Answer();
        Assert.NotNull(list);
        Assert.Equal(body, list!.BodyId);
        Assert.Single(list.People);

        // A singleplayer world held by a menu still answers the read (the paused-dispatch whitelist).
        w.Server.PauseForTest(w.Player, true);
        Assert.True(w.Server.IsPaused);
        w.Transport.Sent.Clear();
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new RequestBaseResidentsIntent { BodyId = body }));
        Assert.Single(Answer()!.People);

        // No base of theirs on that body: an empty list.
        w.Transport.Sent.Clear();
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new RequestBaseResidentsIntent { BodyId = "nowhere" }));
        Assert.Equal(0, Answer()!.Cap);
    }

    [Fact]
    public void AnOlderCoreGoes_AndAfterARestart_TheOtherBasesResidents_AreStillTheSamePeople()
    {
        using var w = new NpcLifeWorld(_content, "residents_pinned");

        // A neighbour's base first (the older one, the lower load-order id), then the homesteader's with a bed.
        var neighbour = w.Server.AddLocalPlayer("Neighbour");
        neighbour.State.AboardShip = false;
        var oldCore = new Vector3i(NpcLifeWorld.Cx + 20, w.Feet, NpcLifeWorld.Cz);
        neighbour.State.Position = new Vector3f(oldCore.X - 1.2f, w.Feet, oldCore.Z + 0.5f); // within mining reach
        w.Server.PlaceBaseForTest(neighbour, oldCore); // the seam founds only the entity
        w.Server.World.SetBlock(oldCore, w.Block("base_core"), 0, 0, 0, "Neighbour");
        int older = w.Server.BaseSnapshots.Single(b => b.OwnerId == neighbour.State.PlayerId).Id;
        int baseId = w.FoundBase();
        Assert.True(older < baseId);
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();

        var names = w.Server.BaseResidentNamesForTest(baseId).OrderBy(r => r.Slot).ToList();
        var faces = w.Server.BaseResidentFacesForTest(baseId).OrderBy(r => r.Slot).ToList();
        Assert.Equal(2, names.Count);
        var keys = w.Server.BaseResidentsForTest(baseId).OrderBy(r => r.Slot)
            .Select(r => w.Server.NpcKeyOfNpcForTest(NpcLifeWorld.Owner, r.NpcId)).ToList();
        var roster = w.Player.State.NpcMemory.Keys.Where(k => k.StartsWith("base_", StringComparison.Ordinal)).OrderBy(k => k).ToList();
        Assert.Equal(keys.OrderBy(k => k), roster);

        // The neighbour knows the homesteader's founding settler (they traded there, say).
        neighbour.State.NpcMemory[keys[0]!] = new NpcRelationship { Name = names[0].Name, Role = "settler", Value = 20 };

        // The neighbour mines their core: the older base is gone, and with the restart the homesteader's base id shifts.
        neighbour.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        w.Server.MineBlock("Neighbour", oldCore.X, oldCore.Y, oldCore.Z);
        Assert.Single(w.Server.BaseSnapshots);
        w.Restart();
        int now = w.Server.BaseSnapshots.Single().Id;
        Assert.NotEqual(baseId, now); // the trigger of #2716: ids are load-order numbers

        w.Transport.Sent.Clear();
        w.Server.ScanBaseLifeForTest();

        // #2716: the same people — names, faces and the memory keys the owner knows them by — and no move-in news.
        Assert.Equal(names, w.Server.BaseResidentNamesForTest(now).OrderBy(r => r.Slot).ToList());
        Assert.Equal(faces, w.Server.BaseResidentFacesForTest(now).OrderBy(r => r.Slot).ToList());
        Assert.Equal(keys, w.Server.BaseResidentsForTest(now).OrderBy(r => r.Slot)
            .Select(r => w.Server.NpcKeyOfNpcForTest(NpcLifeWorld.Owner, r.NpcId)).ToList());
        Assert.Equal(roster, w.Player.State.NpcMemory.Keys.Where(k => k.StartsWith("base_", StringComparison.Ordinal)).OrderBy(k => k).ToList());
        Assert.DoesNotContain(w.Transport.Sent, s => s.Msg is ShipAiLine l
            && l.LineKey.StartsWith("vega.news.settler", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Transport.Sent, s => s.Msg is ChatMessage { IsNpcCall: true });

        // A newer base gets the id the homesteader's base had before the restart — its settler never takes over the key
        // (and so the memories) the homesteader's founding settler is pinned with.
        neighbour = w.Server.AddLocalPlayer("Neighbour");
        neighbour.State.AboardShip = false;
        Assert.Equal("settler", neighbour.State.NpcMemory[keys[0]!].Role);
        w.Server.PlaceBaseForTest(neighbour, oldCore);
        int newer = w.Server.BaseSnapshots.Single(b => b.OwnerId == neighbour.State.PlayerId).Id;
        Assert.Equal(baseId, newer);
        for (int i = 0; i < 3; i++)
        {
            w.Server.World.SetBlock(new Vector3i(oldCore.X + 1 + i, oldCore.Y, oldCore.Z - 4), w.Block("detoxifier"), 0, 0, 0, "Neighbour");
        }

        // A trading post: the founding settler becomes its trader in the very scan they move in.
        w.Server.World.SetBlock(new Vector3i(oldCore.X - 3, oldCore.Y, oldCore.Z + 3), w.Block("station_vendor"), 0, 0, 0, "Neighbour");

        w.Server.ScanBaseLifeForTest();
        var settler = Assert.Single(w.Server.BaseResidentsForTest(newer));
        Assert.Equal("vendor", settler.Job);
        string newKey = w.Server.NpcKeyOfNpcForTest("Neighbour", settler.NpcId)!;
        Assert.DoesNotContain(newKey, keys);
        Assert.StartsWith("base_", newKey, StringComparison.Ordinal);

        // The first scan already read the new settler's pinned key: the job went onto their memory entry, not onto the one the
        // neighbour keeps of the homesteader's founding settler (the key the new base's id would have minted).
        Assert.Equal("vendor", neighbour.State.NpcMemory[newKey].Role);
        Assert.Equal("settler", neighbour.State.NpcMemory[keys[0]!].Role);
    }

    [Fact]
    public void ACorePutBackOnItsCell_BringsItsPeopleBack_ABaseElsewhereWithTheSameIdGetsSomeoneNew()
    {
        using var w = new NpcLifeWorld(_content, "residents_retired");
        int first = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        var founder = Assert.Single(w.Server.BaseResidentNamesForTest(first));
        string key = w.Server.NpcKeyOfNpcForTest(NpcLifeWorld.Owner, w.Server.BaseSettlerForTest(first)!.Value)!;
        var core = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);

        void MineTheCore()
        {
            w.Player.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
            w.Server.MineBlock(NpcLifeWorld.Owner, core.X, core.Y, core.Z);
            Assert.Empty(w.Server.BaseSnapshots);
            w.Server.ScanBaseLifeForTest(); // the sweep: the base's people leave with it
        }

        bool SettlerNews() => w.Transport.Sent.Any(s => s.Msg is ShipAiLine l && l.LineKey.StartsWith("vega.news.settler", StringComparison.Ordinal));

        // #2716: the core mined and put back on its cell — the same founding settler comes back, no news.
        MineTheCore();
        w.Transport.Sent.Clear();
        int again = w.FoundBase();
        Assert.NotEqual(first, again);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(founder.Name, Assert.Single(w.Server.BaseResidentNamesForTest(again)).Name);
        Assert.Equal(key, w.Server.NpcKeyOfNpcForTest(NpcLifeWorld.Owner, w.Server.BaseSettlerForTest(again)!.Value));
        Assert.False(SettlerNews());

        // Mined again and founded elsewhere after a restart: the ids start over, so the new base has the first one's id — but
        // its settler is somebody new, never the one who left with the old base.
        MineTheCore();
        w.Restart();
        w.Transport.Sent.Clear();
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 2.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx + 3, w.Feet, NpcLifeWorld.Cz - 2, "base_core"); // the machines stay in its zone
        int elsewhere = w.Server.BaseSnapshots.Single().Id;
        Assert.Equal(first, elsewhere);
        w.Server.ScanBaseLifeForTest();
        string newKey = w.Server.NpcKeyOfNpcForTest(NpcLifeWorld.Owner, w.Server.BaseSettlerForTest(elsewhere)!.Value)!;
        Assert.NotEqual(key, newKey);
        Assert.True(SettlerNews());
    }
}
