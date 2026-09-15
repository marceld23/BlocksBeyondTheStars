// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1901: a chair stood in a station cabin's doorway. One door-lane rule (<see cref="RoomFurnisher.DoorLaneAt"/>) now keeps
/// the doorway and two rows on each side of it walkable in every composer — kit stations, settlements, cities and the
/// editor — and ends a room at its doorway, so the cabins off one corridor are furnished as rooms of their own.
/// </summary>
public sealed class DoorLaneTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A little voxel grid for the rule's unit tests: floor at y = 0, everything else air until set.</summary>
    private sealed class Grid
    {
        private readonly ushort[,,] _b;

        public Grid(int w, int h, int l)
        {
            W = w;
            H = h;
            L = l;
            _b = new ushort[w, h, l];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < l; z++)
                {
                    _b[x, 0, z] = 1;
                }
        }

        public int W { get; }
        public int H { get; }
        public int L { get; }

        public ushort Get(int x, int y, int z) => _b[x, y, z];

        public void Set(int x, int y, int z, ushort b = 1) => _b[x, y, z] = b;

        /// <summary>A full-height wall row along X at <paramref name="z"/> (y 1 .. H−1), leaving the listed columns open to y 2.</summary>
        public void WallAlongX(int z, params int[] openColumns)
        {
            for (int x = 0; x < W; x++)
                for (int y = 1; y < H; y++)
                {
                    bool open = openColumns.Contains(x) && y <= 2;
                    _b[x, y, z] = open ? (ushort)0 : (ushort)1;
                }
        }

        /// <summary>Outer walls on the X faces (x = 0, x = W−1).</summary>
        public void SideWalls()
        {
            for (int z = 0; z < L; z++)
                for (int y = 1; y < H; y++)
                {
                    _b[0, y, z] = 1;
                    _b[W - 1, y, z] = 1;
                }
        }
    }

    [Fact]
    public void AnInteriorDoor_KeepsTwoRowsClearOnBothSides_AndItsGapEndsTheRoom()
    {
        var g = new Grid(7, 4, 11);
        g.SideWalls();
        g.WallAlongX(0);
        g.WallAlongX(10);
        g.WallAlongX(5, 3); // a one-wide door at x = 3 in the wall at z = 5

        var lane = RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 3, 1, 5);
        Assert.NotNull(lane);
        Assert.Equal(1, lane!.FootY);
        Assert.True(lane.WallAlongX);
        Assert.Equal((3, 3), (lane.GapMin, lane.GapMax));
        Assert.Equal(new[] { (3, 5) }, lane.Gap);
        Assert.Equal(new[] { (3, 3), (3, 4), (3, 5), (3, 6), (3, 7) }.OrderBy(c => c), lane.Clear.OrderBy(c => c));
        // The corners of the first row on each side: a body turns into the doorway there.
        Assert.Contains((2, 4), lane.Keep);
        Assert.Contains((4, 4), lane.Keep);
        Assert.Contains((2, 6), lane.Keep);
        Assert.Contains((4, 6), lane.Keep);
        Assert.DoesNotContain((2, 3), lane.Keep);

        // The gap splits the floor: a room marker on one side floods only its own side once the gap reads as a wall.
        ushort Sealed(int x, int y, int z) => y == lane.FootY && lane.Gap.Contains((x, z)) ? (ushort)1 : g.Get(x, y, z);
        var region = RoomFurnisher.FloodRoom(Sealed, g.W, g.H, g.L, 2, 1, 2, 256, out _);
        Assert.Equal(5 * 4, region.Count);
        Assert.All(region, c => Assert.True(c.Z < 5));
    }

    [Fact]
    public void ATwoDeepJointDoorway_CountsBothWalls_AndAMarkerSetAboveTheFloor_DropsToIt()
    {
        var g = new Grid(8, 5, 12);
        g.SideWalls();
        g.WallAlongX(0);
        g.WallAlongX(11);
        g.WallAlongX(5, 3, 4); // two port walls back to back, both opened over x = 3..4
        g.WallAlongX(6, 3, 4);

        var lane = RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 3, 2, 5); // the marker one cell above the floor
        Assert.NotNull(lane);
        Assert.Equal(1, lane!.FootY);
        Assert.Equal((3, 4), (lane.GapMin, lane.GapMax));
        Assert.Equal((5, 6), (lane.DoorwayMin, lane.DoorwayMax));
        Assert.Equal(4, lane.Gap.Count);
        foreach (int z in new[] { 3, 4, 5, 6, 7, 8 })
        {
            Assert.Contains((3, z), lane.Clear);
            Assert.Contains((4, z), lane.Clear);
        }

        Assert.DoesNotContain((3, 2), lane.Clear);
        Assert.DoesNotContain((3, 9), lane.Clear);
    }

    [Fact]
    public void TheLane_EndsAtTheGridEdge_AndAtTheFarWallOfAShallowRoom()
    {
        var g = new Grid(7, 4, 8);
        g.SideWalls();
        g.WallAlongX(0, 3); // an entrance at the grid's edge
        g.WallAlongX(3, 3); // an interior door into a closet one row deep
        g.WallAlongX(5);
        g.WallAlongX(7);

        var entrance = RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 3, 1, 0)!;
        Assert.Equal(new[] { (3, 0), (3, 1), (3, 2) }.OrderBy(c => c), entrance.Clear.OrderBy(c => c));

        var closet = RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 3, 1, 3)!;
        Assert.Contains((3, 4), closet.Clear);      // the one row of the closet
        Assert.DoesNotContain((3, 5), closet.Clear); // its back wall is not a blocked lane
        Assert.Empty(RoomFurnisher.BlockedDoorLanes(g.Get, (_, _, _) => 0, g.W, g.H, g.L, new[] { new Vector3i(3, 1, 3) }));

        // A solid marker cell or one outside the grid measures nothing.
        Assert.Null(RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 2, 1, 3));
        Assert.Null(RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 3, 1, 9));
    }

    [Fact]
    public void BlockedDoorLanes_FlagsAChairBehindTheDoor_ButNotAStairStepOrAWalkThroughHeadroom()
    {
        var g = new Grid(7, 4, 11);
        g.SideWalls();
        g.WallAlongX(0);
        g.WallAlongX(10);
        g.WallAlongX(5, 3);
        var shapes = new Dictionary<Vector3i, int>();
        int ShapeAt(int x, int y, int z) => shapes.TryGetValue(new Vector3i(x, y, z), out var s) ? s : 0;
        var doors = new[] { new Vector3i(3, 1, 5) };
        Assert.Empty(RoomFurnisher.BlockedDoorLanes(g.Get, ShapeAt, g.W, g.H, g.L, doors));

        g.Set(3, 1, 4); // the report: a chair right behind the doorway
        shapes[new Vector3i(3, 1, 4)] = ShapeCode.Pack(BlockShape.Chair, 0);
        Assert.Equal(new[] { new Vector3i(3, 1, 4) }, RoomFurnisher.BlockedDoorLanes(g.Get, ShapeAt, g.W, g.H, g.L, doors));

        g.Set(3, 1, 4, 0);
        g.Set(3, 1, 7); // the foot of a staircase two rows out: walkable
        shapes[new Vector3i(3, 1, 7)] = ShapeCode.Pack(BlockShape.Stairs, 0);
        Assert.Empty(RoomFurnisher.BlockedDoorLanes(g.Get, ShapeAt, g.W, g.H, g.L, doors));

        g.Set(3, 2, 6); // …but something at head height blocks, whatever its form
        shapes[new Vector3i(3, 2, 6)] = ShapeCode.Pack(BlockShape.Stairs, 0);
        Assert.Equal(new[] { new Vector3i(3, 2, 6) }, RoomFurnisher.BlockedDoorLanes(g.Get, ShapeAt, g.W, g.H, g.L, doors));
    }

    [Fact]
    public void FurnishingPieces_AreTheFurnishersOwn_NeverBedsLightsOrWalls()
    {
        var p = RoomFurnisher.PaletteFor(RoomFurnisher.Style.Station, Content);
        ushort B(string key) => Content.GetBlock(key)!.NumericId.Value;
        Assert.True(RoomFurnisher.IsFurnishingPiece(p, B("steel_floor"), ShapeCode.Pack(BlockShape.Chair, 2)));
        Assert.True(RoomFurnisher.IsFurnishingPiece(p, B("steel_floor"), ShapeCode.Pack(BlockShape.Table, 0)));
        Assert.True(RoomFurnisher.IsFurnishingPiece(p, B("crate"), 0));
        Assert.True(RoomFurnisher.IsFurnishingPiece(p, B("algae_tank"), 0));
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, B("steel_floor"), 0)); // a deck plate
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, B("bed"), ShapeCode.Pack(BlockShape.BedHead, 0)));
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, B("light_white"), 0));
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, B("iron_wall"), 0));
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, B("ladder"), 0));
        Assert.False(RoomFurnisher.IsFurnishingPiece(p, 0, 0));
    }

    [Fact]
    public void EveryShippedTemplate_KeepsItsDoorLanesFree()
    {
        var all = Content.StationTemplates.Concat(Content.SettlementTemplates).ToList();
        Assert.True(all.Count > 100, $"expected the shipped template pools, found {all.Count}");
        var failures = new List<string>();
        int doors = 0;
        foreach (var t in all)
        {
            doors += t.Cells.Count(c => c.Kind == "marker" && RoomFurnisher.IsDoorMarker(c.Id));
            var blocked = RoomFurnisher.BlockedDoorLanes(t);
            if (blocked.Count > 0)
            {
                failures.Add($"{t.Key}: {string.Join(" ", blocked.Take(6))}");
            }
        }

        Assert.True(doors > 100, $"expected the shipped door markers, found {doors}");
        Assert.True(failures.Count == 0, "authored blocks in door lanes:\n" + string.Join("\n", failures));
    }

    private static List<Vector3i> DoorsOf(IEnumerable<(string Type, Vector3i Pos)> markers)
        => markers.Where(m => RoomFurnisher.IsDoorMarker(m.Type)).Select(m => m.Pos).ToList();

    /// <summary>The first blocked cells with what stands there (block key and form), for a readable failure.</summary>
    private static string Describe(List<Vector3i> blocked, Func<int, int, int, ushort> get, Func<int, int, int, int> shapeAt)
        => string.Join(" ", blocked.Take(8).Select(c =>
            $"{c}={Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(get(c.X, c.Y, c.Z)))?.Key}/{(BlockShape)ShapeCode.ShapeOf(shapeAt(c.X, c.Y, c.Z))}"));

    [Fact]
    public void EveryShippedStationKit_KeepsEveryDoorLaneWalkable_AndEveryCabinIsReachableOnFoot()
    {
        var kits = Content.StructureKits.Where(k => k.Kind == StructureKit.KindStation).ToList();
        Assert.Equal(5, kits.Count);
        foreach (var kit in kits)
        {
            for (long seed = 1; seed <= 4; seed++)
            {
                var s = StationKitComposer.Compose(kit, key => Content.TemplateByKey(StructureKit.KindStation, key), seed, Content, out _, out var failure);
                Assert.True(s != null, $"kit '{kit.Key}' seed {seed}: {failure}");
                var doors = DoorsOf(s!.Markers.Select(m => (m.Type, m.LocalPos)));
                Assert.NotEmpty(doors);
                var blocked = RoomFurnisher.BlockedDoorLanes(s.Get, s.GetShape, s.Width, s.Height, s.Length, doors);
                Assert.True(blocked.Count == 0, $"kit '{kit.Key}' seed {seed}: door lanes blocked at {Describe(blocked, s.Get, s.GetShape)}");

                bool Solid(Vector3i c) => s.InBounds(c.X, c.Y, c.Z) && s.Get(c.X, c.Y, c.Z) != 0;
                bool Free(Vector3i c) => !Solid(c);
                bool Standable(Vector3i c) => s.InBounds(c.X, c.Y, c.Z) && c.Y > 0 && Solid(new Vector3i(c.X, c.Y - 1, c.Z)) && Free(c) && Free(new Vector3i(c.X, c.Y + 1, c.Z));
                var spawn = s.Markers.First(m => m.Type == "spawn").LocalPos;
                var limits = new NpcGridPath.Limits(StationKitComposer.DefaultMaxExtent, 32, 400000);
                foreach (var cabin in s.Markers.Where(m => m.Type == "cabin"))
                {
                    var path = NpcGridPath.Find(spawn, cabin.LocalPos, Standable, Free, null, limits, out _);
                    Assert.True(path != null, $"kit '{kit.Key}' seed {seed}: the cabin at {cabin.LocalPos} cannot be reached from the spawn at {spawn}");
                }
            }
        }
    }

    [Fact]
    public void TheCabinsOffOneCorridor_AreFurnishedAsRoomsOfTheirOwn_AndTheCorridorStaysEmpty()
    {
        var kit = Content.KitByKey("station_small_1")!;
        var palette = RoomFurnisher.PaletteFor(RoomFurnisher.Style.Station, Content);
        int cabinsChecked = 0;
        for (long seed = 1; seed <= 6; seed++)
        {
            var s = StationKitComposer.Compose(kit, key => Content.TemplateByKey(StructureKit.KindStation, key), seed, Content, out var comp, out var failure);
            Assert.True(s != null, failure);
            foreach (var placed in comp.Modules.Where(m => m.Key == "st_small_cabins4"))
            {
                var original = Content.TemplateByKey(StructureKit.KindStation, placed.Key)!;
                var t = TemplateTransform.RotateY(original, placed.Turns);
                var authored = t.Cells.Where(c => c.Kind == "block" && c.Y == 1).Select(c => (c.X, c.Z)).ToHashSet();
                var unrotated = UnrotatedCells(original, placed.Turns);
                // Template-local (unrotated) boxes: cabins x 1..3 / 8..10, z 1..4 / 6..9; the corridor x 5..6.
                var pieces = new Dictionary<string, int>();
                for (int x = 0; x < t.Width; x++)
                    for (int z = 0; z < t.Length; z++)
                    {
                        int wx = placed.X + x, wy = placed.Y + 1, wz = placed.Z + z;
                        if (authored.Contains((x, z)) || !RoomFurnisher.IsFurnishingPiece(palette, s!.Get(wx, wy, wz), s.GetShape(wx, wy, wz)))
                        {
                            continue; // air, a wall, or the author's own crate
                        }

                        var (ux, uz) = unrotated[(x, z)];
                        Assert.False(ux is 5 or 6, $"seed {seed}: furniture in the corridor at template ({ux},{uz})");
                        string cabin = $"{(ux < 5 ? "L" : "R")}{(uz < 5 ? 0 : 1)}";
                        pieces[cabin] = pieces.TryGetValue(cabin, out int n) ? n + 1 : 1;
                    }

                // Every cabin is furnished on its own: each gets pieces, none gets the four cabins' worth.
                Assert.Equal(4, pieces.Count);
                Assert.All(pieces.Values, n => Assert.InRange(n, 1, 4));
                cabinsChecked += 4;
            }
        }

        Assert.True(cabinsChecked >= 4, "the small kit always carries a cabins4 module");
    }

    /// <summary>For every cell of the template rotated by <paramref name="turns"/> quarter turns, the unrotated cell it
    /// came from (found by rotating a probe template whose cells carry their own coordinates).</summary>
    private static Dictionary<(int X, int Z), (int X, int Z)> UnrotatedCells(StructureTemplate original, int turns)
    {
        var probe = new StructureTemplate { Width = original.Width, Height = 1, Length = original.Length };
        for (int x = 0; x < original.Width; x++)
            for (int z = 0; z < original.Length; z++)
            {
                probe.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = FormattableString.Invariant($"{x},{z}") });
            }

        var map = new Dictionary<(int X, int Z), (int X, int Z)>();
        foreach (var c in TemplateTransform.RotateY(probe, turns).Cells)
        {
            var parts = c.Id.Split(',');
            map[(c.X, c.Z)] = (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        return map;
    }

    private static ModuleMaterials MaterialsFor(StructureTemplate t)
        => t.Tier == StructureRoles.MetropolisTier ? ModuleMaterials.ForCity(Content) : ModuleMaterials.ForSettlement(t.Tier, "grass", t.IsAlienStyle, Content);

    [Fact]
    public void EverySettlementModule_KeepsItsDoorLanesWalkable_OnceFurnished()
    {
        var templates = Content.SettlementTemplates.Where(t => t.IsModule || t.Key.EndsWith("_home", StringComparison.Ordinal)).ToList();
        Assert.True(templates.Count >= 60);
        var failures = new List<string>();
        foreach (var t in templates)
        {
            var s = SettlementGenerator.FromTemplate(t, Content, t.IsModule ? MaterialsFor(t) : null);
            var blocked = RoomFurnisher.BlockedDoorLanes(s.Get, s.GetShape, s.Width, s.Height, s.Length, DoorsOf(s.Markers.Select(m => (m.Type, m.LocalPos))));
            if (blocked.Count > 0)
            {
                failures.Add($"{t.Key}: {Describe(blocked, s.Get, s.GetShape)}");
            }
        }

        Assert.True(failures.Count == 0, "furnished door lanes blocked:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("village")]
    [InlineData("town")]
    [InlineData("city")]
    public void ModularKitSettlements_KeepEveryDoorLaneWalkable(string tier)
    {
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var failures = new List<string>();
        foreach (var kit in Content.KitsFor(StructureKit.KindSettlement, tier, null, null))
        {
            for (long seed = 1; seed <= 3; seed++)
            {
                var layout = SettlementLayoutSpec.FromKit(kit, tier, new Random((int)seed));
                var s = SettlementGenerator.Generate(tier, false, seed * 7919, "grass", Content, null, 0, new List<string>(), null, layout, kit, pool);
                var doors = DoorsOf(s.Markers.Select(m => (m.Type, m.LocalPos)));
                Assert.NotEmpty(doors);
                var blocked = RoomFurnisher.BlockedDoorLanes(s.Get, s.GetShape, s.Width, s.Height, s.Length, doors);
                if (blocked.Count > 0)
                {
                    failures.Add($"{kit.Key} seed {seed} ({s.Width}×{s.Length}): {Describe(blocked, s.Get, s.GetShape)}");
                }
            }
        }

        Assert.True(failures.Count == 0, "door lanes blocked:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheGdsModularCity_KeepsEveryDoorLaneWalkable()
    {
        // (The legacy procedural houses of the Off option keep their own one-row door reservation — a 4 × 4 hut has no room
        // for two rows and its bed — so this invariant is held for the module-built places.)
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var gds = Assert.Single(Content.KitsFor(StructureKit.KindCity, null, null, "gds_desert"));
        var s = CityGenerator.Generate(20260915, Content, Array.Empty<CityGenerator.OpenZone>(), null, 0, new List<string>(), null, CityLayoutSpec.FromKit(gds), gds, pool);
        var doors = DoorsOf(s.Markers.Select(m => (m.Type, m.LocalPos)));
        Assert.True(doors.Count > 20, $"expected the districts' doors, found {doors.Count}");
        var blocked = RoomFurnisher.BlockedDoorLanes(s.Get, s.GetShape, s.Width, s.Height, s.Length, doors);
        Assert.True(blocked.Count == 0, $"door lanes blocked at {Describe(blocked, s.Get, s.GetShape)}");
    }

    [Fact]
    public void AModulesLampPost_StandsBesideItsRealDoor_NeverInTheLane()
    {
        // A 6 × 6 house whose doorway is off the procedural spot: x = 3..4 on the −Z wall. The fixed offset (mid + 1 = 4)
        // put the post right in front of the doorway's second column.
        var t = new StructureTemplate { Key = "offset_door", Name = "offset_door", Tier = "village", Kind = "settlement", Role = StructureRoles.House, Width = 6, Height = 5, Length = 6 };
        for (int x = 0; x < 6; x++)
            for (int z = 0; z < 6; z++)
                for (int y = 0; y < 5; y++)
                {
                    bool shell = y == 0 || y == 4 || x == 0 || x == 5 || z == 0 || z == 5;
                    bool door = z == 0 && (x == 3 || x == 4) && y >= 1 && y <= 3;
                    if (shell && !door)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = "stone" });
                    }
                }

        t.Cells.Add(new TemplateCell { X = 3, Y = 1, Z = 0, Kind = "marker", Id = "door_hinge" });
        t.Cells.Add(new TemplateCell { X = 2, Y = 1, Z = 4, Kind = "marker", Id = "npc" });
        t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 2, Kind = "marker", Id = SettlementGenerator.RoomMarker });

        ushort lamp = Content.GetBlock("data_cache")!.NumericId.Value;
        var modules = new[] { t };
        int modulePlots = 0, lampsBeside = 0;
        for (long seed = 1; seed <= 12; seed++)
        {
            var composition = new List<string>();
            var s = SettlementGenerator.Generate("village", false, seed, "grass", Content, modules, 1.0, composition);
            int rows = (s.Length - 1) / SettlementGenerator.Plot;
            for (int plot = 0; plot < composition.Count; plot++)
            {
                if (composition[plot] != t.Key)
                {
                    continue;
                }

                modulePlots++;
                int ox = plot / rows * SettlementGenerator.Plot + 1, oz = plot % rows * SettlementGenerator.Plot + 1;
                Assert.Contains(s.Markers, m => m.Type == "door_hinge" && m.LocalPos == new Vector3i(ox + 3, 1, oz));
                foreach (int x in new[] { ox + 3, ox + 4 })
                {
                    Assert.NotEqual(lamp, s.Get(x, 1, oz - 1)); // the first row out of the doorway
                    Assert.NotEqual(lamp, s.Get(x, 2, oz - 1));
                }

                if (s.Get(ox + 5, 1, oz - 1) == lamp && s.Get(ox + 5, 2, oz - 1) == lamp)
                {
                    lampsBeside++;
                }
            }
        }

        Assert.True(modulePlots > 0, "the module fills the house plots");
        Assert.True(lampsBeside > 0, "the lamp post stands beside the module's doorway, one step past its gap");
    }
}
