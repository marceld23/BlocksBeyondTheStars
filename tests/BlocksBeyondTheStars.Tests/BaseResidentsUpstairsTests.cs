// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Residents in a multi-storey base (Screelit's report, 2026-10-16: "they don't come up to that floor"):
/// <list type="bullet">
/// <item>#2677 — the first settler comes for a bed or three machines anywhere inside the base, not only in the 17³ cube;</item>
/// <item>#2678 — residents walk the usual player staircase (one-block steps, a three-high room, the ceiling hole open
/// above the upper steps only) and search ±16 up and down, so a bed three storeys up is reached on foot;</item>
/// <item>#2679 — placing a bed at the base says whether it counts, founded the base, is outside or finds the base full;</item>
/// <item>#2680 — the unseen fallback move happens when no player near can SEE the NPC or its goal, not only when nobody is
/// within 24 blocks.</item>
/// </list>
/// </summary>
public sealed class BaseResidentsUpstairsTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    /// <summary>Places the base core at the pad's centre — no machines.</summary>
    private static int CoreOnly(NpcLifeWorld w)
    {
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, Cx, w.Feet, Cz, "base_core");
        return w.Server.BaseSnapshots.Single().Id;
    }

    /// <summary>A closed stone house of <paramref name="storeys"/> storeys centred on (cx, cz): walls around an inner
    /// (2·half−1)² room per storey, each storey three high, a stone slab between storeys (the floor above) and a roof.
    /// No door, no window.</summary>
    private static void House(NpcLifeWorld w, int cx, int cz, int half, int storeys)
    {
        int roof = w.Feet + 4 * storeys - 1;
        for (int dx = -half; dx <= half; dx++)
            for (int dz = -half; dz <= half; dz++)
            {
                bool wall = Math.Abs(dx) == half || Math.Abs(dz) == half;
                for (int y = w.Feet; y <= roof; y++)
                {
                    if (wall || (y - w.Feet) % 4 == 3)
                    {
                        w.Set(cx + dx, y, cz + dz, "stone");
                    }
                }
            }
    }

    /// <summary>The usual player staircase on the storey whose feet level is <paramref name="floorFeet"/>: three one-block
    /// steps rising along +X from <paramref name="x0"/> (each topped with a stair-shaped block — stairs are full blocks to
    /// stand on), the landing on the slab above at x0 + 3, and the slab opened over the two upper steps only.</summary>
    private static void Staircase(NpcLifeWorld w, int x0, int z, int floorFeet)
    {
        int stairs = ShapeCode.Pack(BlockShape.Stairs, 1);
        for (int i = 0; i < 3; i++)
            for (int h = 0; h <= i; h++)
            {
                w.Set(x0 + i, floorFeet + h, z, "stone", h == i ? stairs : 0);
            }

        w.Server.RemoveBlockForTest(x0 + 1, floorFeet + 3, z);
        w.Server.RemoveBlockForTest(x0 + 2, floorFeet + 3, z);
    }

    private string Text(string key, int n = 0)
        => _content.CreateLocalizer(GameLocale.English).Get(key)
            .Replace("{n}", n.ToString(CultureInfo.InvariantCulture))
            .Replace("{max}", SvGameServer.BaseResidentCapForTest.ToString(CultureInfo.InvariantCulture));

    // ---------------------------------------------------------------- #2677: the founding counts the whole base

    [Fact]
    public void Machines_InTheOpenField_BringNobody_OnTheUpperFloorOfAClosedWing_TheFirstSettler()
    {
        using var w = new NpcLifeWorld(_content, "found_upstairs", padHalf: 24);
        int baseId = CoreOnly(w);

        // Three machines and a bed 16 blocks out in the open — beyond the core zone, in no room: nothing inside the base.
        for (int i = 0; i < 3; i++)
        {
            w.Set(Cx - 16, w.Feet, Cz - 2 + 2 * i, "detoxifier");
        }

        w.Bed(Cx - 18, Cz);
        w.Server.ScanBaseLifeForTest();
        Assert.Empty(w.Server.BaseResidentsForTest(baseId));

        // A closed two-storey wing 16 blocks the other way, three machines on its upper floor: the base is a home.
        House(w, Cx + 16, Cz, half: 4, storeys: 2);
        for (int i = -1; i <= 1; i++)
        {
            w.Set(Cx + 16 + i, w.Feet + 4, Cz, "detoxifier");
        }

        w.Server.ScanBaseLifeForTest();
        Assert.Equal(0, Assert.Single(w.Server.BaseResidentsForTest(baseId)).Slot);
    }

    // ---------------------------------------------------------------- #2678: up the stairs

    [Fact]
    public void AResident_WalksUpThreePlayerStaircases_ToItsBedTwelveBlocksAboveTheCore()
    {
        using var w = new NpcLifeWorld(_content, "stairs_tower", padHalf: 16);
        int baseId = CoreOnly(w);

        // A four-storey tower around the core (inner rooms 5 × 5, three high); on each of the lower three storeys a
        // one-wide staircase with the tight hole, alternating between the north and the south row.
        House(w, Cx, Cz, half: 3, storeys: 4);
        for (int storey = 0; storey < 3; storey++)
        {
            Staircase(w, Cx - 2, storey % 2 == 0 ? Cz - 2 : Cz + 2, w.Feet + 4 * storey);
        }

        // The bed on the top floor, 12 blocks above the core — outside the core zone, inside the closed tower.
        var bed = w.Bed(Cx + 1, Cz - 1, w.Feet + 12);
        w.Player.State.Position = new Vector3f(Cx - 6.5f, w.Feet, Cz + 0.5f); // outside the tower, on the pad
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        var sleeper = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Bed == bed);
        Assert.True(w.Server.NpcRoutineForTest(sleeper.NpcId).Pos.Y < w.Feet + 0.5f, "the sleeper should start downstairs");

        // The tower's walls make the base a walled one: its guard keeps the night shift and sleeps by day.
        double bedtime = sleeper.Job == "guard" ? 0.45 : 0.9;
        w.TickAt(bedtime, 150, until: () => w.Server.NpcRoutineForTest(sleeper.NpcId).Pose == 2);
        var lying = w.Server.NpcRoutineForTest(sleeper.NpcId);
        Assert.True(lying.Pose == 2, $"pose {lying.Pose} at {lying.Pos} ({lying.Activity}), walking {w.Server.NpcHasGoalForTest(sleeper.NpcId)}, "
            + $"unseen moves {w.Server.NpcUnseenMovesForTest}, bed {bed}");
        Assert.True(Math.Abs(lying.Pos.Y - (w.Feet + 12)) < 1f, $"lies at {lying.Pos}, not in the bed at {bed}");
        Assert.Equal(0, w.Server.NpcUnseenMovesForTest); // walked every step — never set down by the fallback
    }

    // ---------------------------------------------------------------- #2679: what a placed bed did

    [Fact]
    public void ABedPlacedAtTheBase_SaysWhetherItCounts_FoundsTheBase_IsOutside_OrTheBaseIsFull()
    {
        using var w = new NpcLifeWorld(_content, "bed_feedback");
        int baseId = CoreOnly(w);
        w.Player.State.Inventory.Add("bed", 8, 16);

        void PlaceBed(int x, int z)
        {
            w.Player.State.Position = new Vector3f(x - 1.5f, w.Feet, z + 0.5f);
            w.Server.PlaceBlock(NpcLifeWorld.Owner, x, w.Feet, z, "bed", yaw: 0);
            Assert.Equal("bed", _content.BlockById(w.Server.World.GetBlock(new Vector3i(x, w.Feet, z)))?.Key); // placed for real
        }

        // Out in the open field before anybody lives here.
        PlaceBed(Cx + 16, Cz);
        Assert.Contains(Text("srv.base.bed_outside_no_settler"), w.MessagesToPlayer());
        Assert.Empty(w.Server.BaseResidentsForTest(baseId));

        // The first bed inside founds the base: the founding settler and the bed's own resident.
        PlaceBed(Cx - 6, Cz + 2);
        Assert.Contains(Text("srv.base.bed_founds", 2), w.MessagesToPlayer());
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
        var core = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<BaseList>().Last()
            .Bases.Single(b => b.Id == baseId);
        Assert.Equal((2, 32), (core.Residents, core.ResidentCap)); // the core's readout: "Residents: 2/32" (#2775)

        PlaceBed(Cx - 3, Cz + 2);
        Assert.Contains(Text("srv.base.bed_counts", 3), w.MessagesToPlayer());

        // Out in the open again, the base founded now: only "outside".
        PlaceBed(Cx + 16, Cz - 4);
        Assert.Contains(Text("srv.base.bed_outside"), w.MessagesToPlayer());

        PlaceBed(Cx + 3, Cz + 2);
        Assert.Contains(Text("srv.base.bed_counts", 4), w.MessagesToPlayer());
        PlaceBed(Cx + 6, Cz + 2);
        Assert.Contains(Text("srv.base.bed_counts", 5), w.MessagesToPlayer());

        Assert.DoesNotContain(Text("srv.base.bed_full"), w.MessagesToPlayer()); // #2775: five is no longer full

        // Beds up to one short of the cap (two more rows inside the core zone), then the placed bed that fills the base:
        // the cap's number of people live here already.
        int cap = SvGameServer.BaseResidentCapForTest, beds = 4;
        foreach (int z in new[] { Cz + 5, Cz - 2 })
        {
            for (int x = Cx - 8; x <= Cx + 8 && beds < cap - 1; x++)
            {
                w.Bed(x, z);
                beds++;
            }
        }

        PlaceBed(Cx - 6, Cz - 5);
        Assert.Contains(Text("srv.base.bed_full"), w.MessagesToPlayer());
        Assert.Equal(cap, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(cap, w.Server.BaseResidentsForTest(baseId).Count);
    }

    // ---------------------------------------------------------------- #2680: the unseen fallback

    [Fact]
    public void TheSightTest_GlassHidesNothing_AStoneWallDoes()
    {
        using var w = new NpcLifeWorld(_content, "unseen_sight");
        var player = new Vector3f(Cx - 5.5f, w.Feet, Cz + 0.5f);
        var npc = new Vector3f(Cx + 5.5f, w.Feet, Cz + 0.5f);
        var goal = new Vector3f(Cx + 5.5f, w.Feet, Cz + 6.5f);
        Assert.True(w.Server.PlayerSeesUnseenMoveForTest(player, npc, goal));

        // A glass wall across the whole view: still seen.
        for (int z = -10; z <= 16; z++)
            for (int y = 0; y < 6; y++)
            {
                w.Set(Cx, w.Feet + y, Cz + z, "glass");
            }

        Assert.True(w.Server.PlayerSeesUnseenMoveForTest(player, npc, goal));

        // The same wall in stone: neither the NPC nor its goal can be seen.
        for (int z = -10; z <= 16; z++)
            for (int y = 0; y < 6; y++)
            {
                w.Set(Cx, w.Feet + y, Cz + z, "stone");
            }

        Assert.False(w.Server.PlayerSeesUnseenMoveForTest(player, npc, goal));
    }

    [Fact]
    public void TheUnseenFallback_WaitsWhileThePlayerSharesTheRoom_AndMovesTheResidentWhenThePlayerIsOnTheFloorAbove()
    {
        using var w = new NpcLifeWorld(_content, "unseen_floor");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;

        // Room A (closed, no door) holds the resident; its goal lies in the sealed room B — no route anywhere.
        int ax = Cx + 12, az = Cz;
        w.Hut(ax, az, half: 4, doorway: false);
        int bx = Cx + 12, bz = Cz + 14;
        w.Hut(bx, bz, half: 2, doorway: false);
        var goal = new Vector3f(bx + 0.5f, w.Feet, bz + 0.5f);
        w.Server.MoveNpcForTest(id, new Vector3f(ax - 1.5f, w.Feet, az + 0.5f));
        w.Server.SetNpcGoalForTest(id, goal);

        // The player in room A with a clear view of the resident: it never pops away in front of them.
        w.Player.State.Position = new Vector3f(ax + 2.5f, w.Feet, az + 0.5f);
        w.TickAt(0.45, 40);
        Assert.True(w.Server.NpcHasGoalForTest(id));
        Assert.Equal(0, w.Server.NpcUnseenMovesForTest);

        // The player up on room A's roof, right above the resident: within 24 blocks, but the ceiling hides it — moved.
        w.Player.State.Position = new Vector3f(ax + 0.5f, w.Feet + 4, az + 0.5f);
        w.TickAt(0.45, 60, until: () => !w.Server.NpcHasGoalForTest(id));
        Assert.False(w.Server.NpcHasGoalForTest(id));
        Assert.Equal(1, w.Server.NpcUnseenMovesForTest);
        var placed = w.Server.NpcSnapshots.Single(n => n.Id == id).Pos;
        Assert.True(Math.Abs(placed.X - goal.X) < 0.6f && Math.Abs(placed.Z - goal.Z) < 0.6f, $"set down at {placed}, not the goal");
    }
}
