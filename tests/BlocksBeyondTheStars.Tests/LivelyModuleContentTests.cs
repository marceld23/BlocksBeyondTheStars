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
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — the shipped shells, landmarks and kits (tools/gen_lively_settlement_modules.py): every function has
/// a shell of its own (no two functions of a style share one), every module is whole (a door or an entrance, its room, its
/// post, a free door lane, a mirror image), the kits of the lively places are drawn only from terrain generation 46 and the
/// kits from before only up to 45, and a settlement planned from them has at most one landmark and is far less uniform
/// than the classic one.
/// </summary>
public sealed class LivelyModuleContentTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly List<StructureTemplate> Pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
    private static readonly List<StructureTemplate> Lively = Pool.Where(t => t.Kit == "settlement_lively").ToList();
    private readonly ITestOutputHelper _out;

    public LivelyModuleContentTests(ITestOutputHelper output) => _out = output;

    private static string Geometry(StructureTemplate t)
        => string.Join(";", t.Cells.Where(c => c.Kind == "block").Select(c => $"{c.X},{c.Y},{c.Z},{c.Id},{c.Shape}").OrderBy(s => s, StringComparer.Ordinal));

    [Fact]
    public void TheShippedModules_ArriveExpanded_AndEveryBlockIsKnown()
    {
        Assert.True(Lively.Count >= 120, $"only {Lively.Count} lively modules");
        foreach (var t in Lively)
        {
            Assert.Empty(t.Rows); // the compact body was expanded at load: every reader walks the cells
            Assert.Empty(t.Palette);
            Assert.Equal(string.Empty, t.Role); // never a slot of the classic plot grid
            Assert.Equal(WorldDescription.LivelyPlacesGeneration, t.MinTerrainGeneration);
            Assert.True(t.Cells.Count(c => c.Kind == "block") > 20, $"{t.Key} is nearly empty");
            foreach (var c in t.Cells.Where(c => c.Kind == "block"))
            {
                Assert.True(MaterialTokens.IsToken(c.Id) || Content.GetBlock(c.Id) is not null, $"{t.Key}: unknown block '{c.Id}'");
                Assert.True(c.X >= 0 && c.Y >= 0 && c.Z >= 0 && c.X < t.Width && c.Y < t.Height && c.Z < t.Length, $"{t.Key}: a cell leaves the box");
            }

            Assert.InRange(t.Width, 5, 11);
            Assert.InRange(t.Length, 5, 11);
        }
    }

    [Fact]
    public void EveryFunction_HasAShellOfItsOwn()
    {
        foreach (var style in Lively.Where(t => !StructureRoles.IsLandmark(t.Function)).GroupBy(t => (t.Tier, t.IsAlienStyle)))
        {
            var shells = style.GroupBy(Geometry).ToList();
            Assert.Equal(style.Count(), shells.Count); // no two modules of a style are the same building
            _out.WriteLine($"{style.Key.Tier} {(style.Key.IsAlienStyle ? "alien" : "human")}: {style.Count()} modules, {shells.Count} shells");
        }

        // Against the classic set: 13 village functions shared one shell.
        var classicVillage = Pool.Where(t => t.Kit == "settlement_modular" && t.Tier == "village" && !t.IsAlienStyle).ToList();
        Assert.True(classicVillage.GroupBy(Geometry).Count() < classicVillage.Count / 2);
    }

    [Fact]
    public void EveryBuilding_HasItsDoorItsRoomAndItsPost_AndAFreeDoorLane()
    {
        foreach (var t in Lively)
        {
            bool landmark = StructureRoles.IsLandmark(t.Function);
            var markers = t.Cells.Where(c => c.Kind == "marker").ToList();
            bool doorOnFront = markers.Any(m => RoomFurnisher.IsDoorMarker(m.Id) && m.Z == 0);
            bool entrance = markers.Any(m => m.Id == "entrance" && m.Z == 0);
            Assert.True(doorOnFront || entrance, $"{t.Key}: neither a door nor an entrance on its road side");
            Assert.True(TemplateTransform.CanMirror(t), $"{t.Key} has no mirror image");
            if (landmark)
            {
                continue;
            }

            Assert.Contains(markers, m => RoomFurnisher.IsDoorMarker(m.Id));
            Assert.Contains(markers, m => m.Id == "room");
            string? post = t.Function switch
            {
                StructureRoles.House => "npc",
                StructureRoles.Market => "vendor",
                StructureRoles.Board => "mission_board",
                StructureRoles.Tavern => SettlementGenerator.TavernMarker,
                StructureRoles.Workshop => SettlementGenerator.WorkshopMarker,
                _ => NpcProfessions.ByFunction(t.Function)?.Marker,
            };
            Assert.True(post is not null && markers.Any(m => m.Id == post), $"{t.Key}: no '{post}' marker");
            var blocked = RoomFurnisher.BlockedDoorLanes(t);
            Assert.True(blocked.Count == 0, $"{t.Key}: its door lane is blocked at {string.Join(" ", blocked.Take(3))}");
        }
    }

    [Fact]
    public void TheFamilyHouse_HasAChildsBed()
    {
        var family = Lively.Where(t => t.Cells.Any(c => c.Kind == "marker" && c.Id == SettlementPlanBaker.ChildMarker)).ToList();
        Assert.NotEmpty(family);
        foreach (var t in family)
        {
            var child = t.Cells.Single(c => c.Kind == "marker" && c.Id == SettlementPlanBaker.ChildMarker);
            Assert.Contains(t.Cells, c => c.Kind == "block" && c.Id == FurnitureShapes.BedKey && ShapeCode.ShapeOf(c.Shape) == (int)BlockShape.BedHead
                && Math.Abs(c.X - child.X) <= 1 && Math.Abs(c.Z - child.Z) <= 1 && c.Y == child.Y);
        }
    }

    [Fact]
    public void TheKitsOfTheLivelyPlaces_AreDrawnFromGeneration46_AndTheOlderOnesOnlyBefore()
    {
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            var now = Content.KitsFor(StructureKit.KindSettlement, tier, null, "meadowlands", WorldDescription.LivelyPlacesGeneration);
            Assert.Equal(new[] { $"{tier}_grown_2", $"{tier}_grid_2" }, now.Select(k => k.Key).OrderByDescending(k => k, StringComparer.Ordinal));
            Assert.All(now, k => Assert.True(k.IsPlanned));

            var before = Content.KitsFor(StructureKit.KindSettlement, tier, null, "meadowlands", WorldDescription.AbandonedBedsGeneration);
            Assert.NotEmpty(before);
            Assert.DoesNotContain(before, k => k.Key.EndsWith("_2", StringComparison.Ordinal) && k.MinTerrainGeneration == WorldDescription.LivelyPlacesGeneration);
            Assert.Contains(before, k => k.Key == $"{tier}_grown_1");
        }
    }

    public static IEnumerable<object[]> Kits()
    {
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            yield return new object[] { $"{tier}_grown_2", tier };
            yield return new object[] { $"{tier}_grid_2", tier };
        }
    }

    private static SettlementPlan PlanFor(string kit, string tier, long seed)
        => SettlementPlanner.Plan(new SettlementPlanRequest
        {
            Tier = tier,
            Seed = seed,
            Kit = Content.KitByKey(kit)!,
            Pool = Pool,
            Ground = SettlementPlannerTests.Hills,
        });

    [Theory]
    [MemberData(nameof(Kits))]
    public void APlannedLivelySettlement_HasItsServices_AtMostOneLandmark_AndWalkableDoors(string kit, string tier)
    {
        int landmarks = 0;
        for (long seed = 1; seed <= 10; seed++)
        {
            var plan = PlanFor(kit, tier, seed);
            var functions = plan.Lots.Select(l => l.Function).ToList();
            Assert.Contains(StructureRoles.Market, functions);
            Assert.Contains(StructureRoles.Board, functions);
            int own = functions.Count(StructureRoles.IsLandmark);
            Assert.True(own <= 1, $"{kit} seed {seed}: {own} landmarks");
            landmarks += own;

            var s = SettlementPlanBaker.Bake(plan, tier, false, seed, "grass", Content, Pool);
            var doors = s.Markers.Where(m => RoomFurnisher.IsDoorMarker(m.Type)).Select(m => m.LocalPos).ToList();
            var blocked = RoomFurnisher.BlockedDoorLanes((x, y, z) => s.InBounds(x, y, z) ? s.Get(x, y, z) : (ushort)0,
                (x, y, z) => s.InBounds(x, y, z) ? s.GetShape(x, y, z) : 0, s.Width, s.Height, s.Length, doors);
            Assert.True(blocked.Count == 0, $"{kit} seed {seed}: door lanes blocked at {string.Join(" ", blocked.Take(4))}");
            Assert.NotEmpty(s.Markers.Where(m => m.Type == SettlementPlanBaker.SignMarker));
        }

        Assert.InRange(landmarks, 4, 10); // about three in four settlements have one
    }

    [Fact]
    public void ALivelyVillage_IsFarLessUniformThanAClassicOne()
    {
        double Sameness(string kit, int revision)
        {
            double sum = 0;
            int n = 0;
            for (long seed = 1; seed <= 40; seed++)
            {
                var plan = SettlementPlanner.Plan(new SettlementPlanRequest
                {
                    Tier = "village",
                    Seed = seed,
                    Kit = Content.KitByKey(kit)!,
                    Pool = Pool,
                    Ground = SettlementPlannerTests.Hills,
                    Revision = revision,
                });
                var roofed = plan.Lots.Where(l => l.Module.Length > 0 && l.Function is not (StructureRoles.Field or StructureRoles.Garden or StructureRoles.Pen))
                    .Select(l => Geometry(Pool.First(m => m.Key == l.Module)) + (l.Mirrored ? "|m" : string.Empty)).ToList();
                if (roofed.Count == 0)
                {
                    continue;
                }

                sum += roofed.GroupBy(g => g).Max(g => g.Count()) / (double)roofed.Count;
                n++;
            }

            return sum / n;
        }

        double classic = Sameness("village_grown_1", SettlementPlan.ClassicRevision);
        double lively = Sameness("village_grown_2", SettlementPlan.LivelyRevision);
        _out.WriteLine($"share of a village's buildings that are the most common one: classic {classic:P0}, lively {lively:P0}");
        Assert.True(classic > 0.5, $"the classic village was expected to be uniform, got {classic:P0}");
        Assert.True(lively < 0.35, $"the lively village is still uniform: {lively:P0}");
    }

    [Fact]
    public void AWholeTemplate_CanStandAtTheHeartOfAPlannedVillage()
    {
        var centre = Content.SettlementTemplates.First(t => t.Key == "river_hamlet_home");
        var pool = Pool.Append(centre).ToList();
        int placed = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            var plan = SettlementPlanner.Plan(new SettlementPlanRequest
            {
                Tier = "village",
                Seed = seed,
                Kit = Content.KitByKey("village_grown_2")!,
                Pool = Pool,
                Ground = SettlementPlannerTests.Hills,
                Centres = new[] { centre },
                Centre = centre.Key,
            });
            Assert.DoesNotContain(plan.Lots, l => StructureRoles.IsLandmark(l.Function)); // the template is the landmark
            var lot = plan.Lots.FirstOrDefault(l => l.Function == SettlementPlanBaker.CentreFunction);
            if (lot is null)
            {
                continue;
            }

            placed++;
            Assert.Equal(centre.Key, lot.Module);
            Assert.True(SettlementPlan.TryParse(plan.Serialize(), out var pinned));
            var s = SettlementPlanBaker.Bake(pinned, "village", false, seed, "grass", Content, pool);
            ushort bed = Content.GetBlock(FurnitureShapes.BedKey)!.NumericId.Value;
            int beds = 0;
            for (int x = lot.X; x < lot.X + lot.W; x++)
                for (int z = lot.Z; z < lot.Z + lot.L; z++)
                    for (int y = 0; y < s.Height; y++)
                    {
                        beds += s.Get(x, y, z) == bed ? 1 : 0;
                    }

            Assert.True(beds > 0, $"seed {seed}: the template at the heart was not stamped");
            Assert.True(plan.Lots.Count(l => l.Module.Length > 0) >= 5, "the village round the template is missing");
        }

        Assert.True(placed >= 6, $"the template found a place in only {placed} of 8 villages");
    }
}
