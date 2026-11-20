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
/// The lively places — the bake of a revision-2 plan: lanterns that cast light instead of two glowing blocks, the
/// settlement's own colour on its awnings, market stalls and benches on the plaza, a sign marker at the door of every
/// building that offers something — and nothing of it in a door's lane. A revision-1 plan bakes what it always baked.
/// </summary>
public sealed class LivelySettlementBakeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly List<StructureTemplate> Pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();

    public static IEnumerable<object[]> Kits()
    {
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            yield return new object[] { $"{tier}_grown_1", tier };
            yield return new object[] { $"{tier}_grid_1", tier };
        }
    }

    private static SettlementPlan PlanFor(string kit, string tier, long seed, int revision)
        => SettlementPlanner.Plan(new SettlementPlanRequest
        {
            Tier = tier,
            Seed = seed,
            Kit = Content.KitByKey(kit)!,
            Pool = Pool,
            Ground = SettlementPlannerTests.Hills,
            Revision = revision,
        });

    private static SettlementStructure Bake(SettlementPlan plan, string tier, long seed, bool ruined = false)
        => SettlementPlanBaker.Bake(plan, tier, ruined, seed, "grass", Content, Pool);

    private static string KeyAt(SettlementStructure s, Vector3i c)
        => s.InBounds(c.X, c.Y, c.Z) ? Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(s.Get(c.X, c.Y, c.Z)))?.Key ?? string.Empty : string.Empty;

    private static bool Free(SettlementStructure s, Vector3i c) => s.InBounds(c.X, c.Y, c.Z) && s.Get(c.X, c.Y, c.Z) == 0;

    private static IEnumerable<SettlementMarker> Of(SettlementStructure s, string type) => s.Markers.Where(m => m.Type == type);

    [Theory]
    [MemberData(nameof(Kits))]
    public void ALivelyPlace_HasLanternsThatCastLight_OnPosts(string kit, string tier)
    {
        int lamps = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            var s = Bake(PlanFor(kit, tier, seed, SettlementPlan.LivelyRevision), tier, seed);
            foreach (var lamp in Of(s, SettlementPlanBaker.LampMarker))
            {
                // The marker is the post's foot (or, at a town's column, the cell beside it): a lantern stands within two rows above.
                var light = Enumerable.Range(0, 4).SelectMany(dy => new[] { 0, -1 }.Select(dx => new Vector3i(lamp.LocalPos.X + dx, lamp.LocalPos.Y + dy, lamp.LocalPos.Z)))
                    .Select(c => KeyAt(s, c)).FirstOrDefault(k => k is "lantern" or "crystal");
                Assert.True(light is not null, $"{kit} seed {seed}: no lantern at the lamp marker {lamp.LocalPos}");
                Assert.True(Content.GetBlock(light!)!.LightColor is not null, $"'{light}' casts no light");
                lamps++;
            }

            // The classic lamp post (two glowing data caches on top of each other) is gone from the streets.
            for (int x = 0; x < s.Width; x++)
                for (int z = 0; z < s.Length; z++)
                    for (int y = 0; y + 1 < s.Height; y++)
                    {
                        bool stack = KeyAt(s, new Vector3i(x, y, z)) == "data_cache" && KeyAt(s, new Vector3i(x, y + 1, z)) == "data_cache";
                        Assert.False(stack, $"{kit} seed {seed}: a classic lamp post stands at ({x},{y},{z})");
                    }
        }

        Assert.True(lamps > 0, $"{kit}: eight settlements without a single lantern");
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void EveryBuildingThatOffersSomething_HasASignAtItsDoor_AndAnAwningInThePlacesColour(string kit, string tier)
    {
        for (long seed = 1; seed <= 8; seed++)
        {
            var plan = PlanFor(kit, tier, seed, SettlementPlan.LivelyRevision);
            var s = Bake(plan, tier, seed);
            Assert.Contains(plan.Colour, SettlementPalette.Colours);
            Assert.Equal(plan.Colour, s.Colour);
            var signs = Of(s, SettlementPlanBaker.SignMarker).ToList();
            foreach (var lot in plan.Lots.Where(l => l.Module.Length > 0))
            {
                var own = signs.Where(m => lot.Covers(m.LocalPos.X, m.LocalPos.Z)).ToList();
                bool offers = lot.Function is not (StructureRoles.House or StructureRoles.Field or StructureRoles.Garden or StructureRoles.Pen);
                if (!offers)
                {
                    Assert.Empty(own);
                    continue;
                }

                var sign = Assert.Single(own);
                var settings = MarkerSettings.Parse(sign.Data);
                Assert.Equal(lot.Function, settings.Get("fn"));
                int side = settings.GetInt("side", -1);
                Assert.InRange(side, 0, 3);
                Assert.True(Free(s, sign.LocalPos) && Free(s, new Vector3i(sign.LocalPos.X, sign.LocalPos.Y + 1, sign.LocalPos.Z)),
                    $"{kit} seed {seed}: the sign of {lot.Module} is not at a doorway");

                // The awning: a dyed slab one step out from the door, above the doorway.
                var (ox, oz) = side switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };
                var over = new Vector3i(sign.LocalPos.X + ox, sign.LocalPos.Y + 3, sign.LocalPos.Z + oz);
                if (s.InBounds(over.X, over.Y, over.Z))
                {
                    Assert.Equal(plan.Colour, s.GetModifier(over.X, over.Y, over.Z).Tint);
                    Assert.Equal((int)BlockShape.Slab, ShapeCode.ShapeOf(s.GetShape(over.X, over.Y, over.Z)));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void TheFurniture_StandsOnTheGround_AndKeepsEveryDoorLaneWalkable(string kit, string tier)
    {
        int stalls = 0, benches = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            var s = Bake(PlanFor(kit, tier, seed, SettlementPlan.LivelyRevision), tier, seed);
            var doors = s.Markers.Where(m => RoomFurnisher.IsDoorMarker(m.Type)).Select(m => m.LocalPos).ToList();
            var blocked = RoomFurnisher.BlockedDoorLanes((x, y, z) => s.InBounds(x, y, z) ? s.Get(x, y, z) : (ushort)0,
                (x, y, z) => s.InBounds(x, y, z) ? s.GetShape(x, y, z) : 0, s.Width, s.Height, s.Length, doors);
            Assert.True(blocked.Count == 0, $"{kit} seed {seed}: door lanes blocked at {string.Join(" ", blocked.Take(4))}");

            // Where somebody is meant to stand (in front of a stall, a bench, beside the well) is free, with ground under it.
            foreach (string type in new[] { SettlementPlanBaker.StallMarker, SettlementPlanBaker.BenchMarker, SettlementPlanBaker.WellMarker })
            {
                foreach (var m in Of(s, type))
                {
                    Assert.True(Free(s, m.LocalPos) && Free(s, new Vector3i(m.LocalPos.X, m.LocalPos.Y + 1, m.LocalPos.Z)),
                        $"{kit} seed {seed}: the {type} marker at {m.LocalPos} is not free");
                    // Its column's floor is the row below (a paved plaza is in the bake, a green's ground is the stamper's).
                    Assert.True(s.FloorAt(m.LocalPos.X, m.LocalPos.Z) == m.LocalPos.Y - 1,
                        $"{kit} seed {seed}: the {type} marker at {m.LocalPos} does not stand on its column's floor ({s.FloorAt(m.LocalPos.X, m.LocalPos.Z)})");
                }
            }

            stalls += Of(s, SettlementPlanBaker.StallMarker).Count();
            benches += Of(s, SettlementPlanBaker.BenchMarker).Count();
        }

        Assert.True(stalls > 0, $"{kit}: no market stall in eight settlements");
        Assert.True(benches > 0, $"{kit}: no bench in eight settlements");
    }

    /// <summary>#3872: a lively green village's pond — three by five blocks of water, a plank jetty from the east bank to
    /// the middle with a lantern on a post at its end, the water-drawing spot on the jetty. Where the green is not level
    /// the single water block of before stays; a classic green keeps it always.</summary>
    [Fact]
    public void ALivelyGreenVillage_HasAPondWithAJettyAndALantern()
    {
        int ponds = 0;
        for (long seed = 1; seed <= 12; seed++)
        {
            var request = new SettlementPlanRequest
            {
                Tier = "village",
                Seed = seed,
                Kit = Content.KitByKey("village_grown_1")!,
                Pool = Pool,
                Form = "green",
                Ground = (_, _) => 64,
                Revision = SettlementPlan.LivelyRevision,
            };
            var plan = SettlementPlanner.Plan(request);
            Assert.Equal("green", plan.Form);
            var s = Bake(plan, "village", seed);
            int cx = plan.PlazaX, cz = plan.PlazaZ, y = plan.HeightAt(cx, cz);
            if (KeyAt(s, new Vector3i(cx - 1, y, cz - 2)) != "water")
            {
                Assert.Equal("water", KeyAt(s, new Vector3i(cx, y, cz))); // no room for the pond: the single water block
                continue;
            }

            ponds++;
            for (int dx = -SettlementPlanBaker.PondHalfW; dx <= SettlementPlanBaker.PondHalfW; dx++)
            {
                for (int dz = -SettlementPlanBaker.PondHalfL; dz <= SettlementPlanBaker.PondHalfL; dz++)
                {
                    string expected = dz == 0 && dx >= 0 ? "wood_planks" : "water";
                    Assert.Equal(expected, KeyAt(s, new Vector3i(cx + dx, y, cz + dz)));
                    if (!(dz == 0 && dx == 0))
                    {
                        Assert.True(Free(s, new Vector3i(cx + dx, y + 1, cz + dz)), $"seed {seed}: something stands on the pond");
                    }
                }
            }

            Assert.Contains(KeyAt(s, new Vector3i(cx, y + 2, cz)), new[] { "lantern", "crystal" }); // on its post at the jetty's end (an alien place's lamp is a crystal)
            Assert.Contains(Of(s, SettlementPlanBaker.LampMarker), m => m.LocalPos == new Vector3i(cx, y + 1, cz));
            var well = Assert.Single(Of(s, SettlementPlanBaker.WellMarker));
            Assert.Equal(new Vector3i(cx + 1, y + 1, cz), well.LocalPos);

            // The same plan baked as a hamlet keeps its fire and no pond.
            var hamlet = Bake(SettlementPlanner.Plan(new SettlementPlanRequest
            {
                Tier = "hamlet",
                Seed = seed,
                Kit = Content.KitByKey("hamlet_grown_1")!,
                Pool = Pool,
                Form = "green",
                Ground = (_, _) => 64,
                Revision = SettlementPlan.LivelyRevision,
            }), "hamlet", seed);
            Assert.Empty(Of(hamlet, SettlementPlanBaker.WellMarker).Where(m => KeyAt(hamlet, m.LocalPos - new Vector3i(0, 1, 0)) == "wood_planks"));
        }

        Assert.True(ponds >= 6, $"only {ponds} of 12 flat green villages got their pond");
    }

    [Fact]
    public void AHamletGathersRoundAFire_AVillageHasItsWell()
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var hamlet = Bake(PlanFor("hamlet_grown_1", "hamlet", seed, SettlementPlan.LivelyRevision), "hamlet", seed);
            var fire = Assert.Single(Of(hamlet, SettlementPlanBaker.FireMarker));
            Assert.Equal("campfire", KeyAt(hamlet, fire.LocalPos));

            var village = Bake(PlanFor("village_grown_1", "village", seed, SettlementPlan.LivelyRevision), "village", seed);
            Assert.NotEmpty(Of(village, SettlementPlanBaker.WellMarker));
        }
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void TheBake_IsTheSameEveryTime_AndAKitWithoutDressing_GetsNoFurniture(string kit, string tier)
    {
        var plan = PlanFor(kit, tier, 5, SettlementPlan.LivelyRevision);
        Assert.Equal(Cells(Bake(plan, tier, 5)), Cells(Bake(plan, tier, 5)));
        Assert.True(SettlementPlan.TryParse(plan.Serialize(), out var pinned));
        Assert.Equal(Cells(Bake(plan, tier, 5)), Cells(Bake(pinned, tier, 5))); // the pinned plan replays block for block

        plan.Dressing = 1; // "none": the lanterns stay (a street is lit), the plaza's furniture and the fronts' props do not
        var bare = Bake(plan, tier, 5);
        Assert.Empty(Of(bare, SettlementPlanBaker.StallMarker));
        Assert.Empty(Of(bare, SettlementPlanBaker.BenchMarker).Where(m => plan.KindAt(m.LocalPos.X, m.LocalPos.Z) == SettlementPlan.Plaza));
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void AClassicPlan_BakesItsClassicDressing_WithoutColourFurnitureOrSigns(string kit, string tier)
    {
        for (long seed = 1; seed <= 4; seed++)
        {
            var plan = PlanFor(kit, tier, seed, SettlementPlan.ClassicRevision);
            var s = Bake(plan, tier, seed);
            Assert.Equal(0, plan.Colour);
            Assert.Equal(0, s.Colour);
            Assert.DoesNotContain(s.Markers, m => SettlementPlanBaker.FurnitureMarkers.Contains(m.Type));
            for (int x = 0; x < s.Width; x++)
                for (int z = 0; z < s.Length; z++)
                    for (int y = 0; y < s.Height; y++)
                    {
                        Assert.NotEqual("lantern", KeyAt(s, new Vector3i(x, y, z)));
                    }
        }
    }

    [Fact]
    public void ARuinOfALivelyPlace_HasNoFurnitureMarkers()
    {
        var plan = PlanFor("village_grown_1", "village", 3, SettlementPlan.LivelyRevision);
        var ruin = Bake(plan, "village", 3, ruined: true);
        Assert.Equal(0, ruin.Colour);
        Assert.DoesNotContain(ruin.Markers, m => SettlementPlanBaker.FurnitureMarkers.Contains(m.Type));
    }

    private static string Cells(SettlementStructure s)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b != 0)
                    {
                        sb.Append(x).Append(',').Append(y).Append(',').Append(z).Append(':').Append(b).Append('/').Append(s.GetShape(x, y, z))
                            .Append('/').Append(s.GetModifier(x, y, z).Tint).Append(';');
                    }
                }

        foreach (var m in s.Markers)
        {
            sb.Append(m.Type).Append('@').Append(m.LocalPos).Append('=').Append(m.Data).Append(';');
        }

        return sb.ToString();
    }
}
