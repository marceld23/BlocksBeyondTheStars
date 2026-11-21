// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places, the foundation (terrain generation 46): the generation and the plan revision it switches, the kits'
/// generation bounds, the mirror of a module, and the two messages that carry a place and a gesture to the client.
/// </summary>
public sealed class LivelyPlacesFoundationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly List<StructureTemplate> Pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();

    [Fact]
    public void NewWorlds_AreCreatedAtGeneration46_AndPlanWithRevision2()
    {
        Assert.Equal(46, WorldDescription.LivelyPlacesGeneration);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= WorldDescription.LivelyPlacesGeneration); // 47: the sky-lights (#3893)
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, new ServerConfig().World.TerrainGeneration);
        Assert.Equal(SettlementPlan.LivelyRevision, SettlementPlan.RevisionFor(WorldDescription.CurrentTerrainGeneration));
        Assert.Equal(SettlementPlan.LivelyRevision, SettlementPlan.CurrentRevision);
        Assert.Equal(SettlementPlan.LivelyRevision, SettlementPlan.RevisionFor(WorldDescription.LivelyPlacesGeneration));
        Assert.Equal(SettlementPlan.ClassicRevision, SettlementPlan.RevisionFor(WorldDescription.AbandonedBedsGeneration));
        Assert.Equal(SettlementPlan.ClassicRevision, SettlementPlan.RevisionFor(WorldDescription.GrownSettlementsGeneration));
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

    [Theory]
    [InlineData("village_grown_1", "village")]
    [InlineData("town_grid_1", "town")]
    public void AClassicPlan_KeepsItsEightFieldLots_AndNeverMirrors(string kit, string tier)
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var plan = PlanFor(kit, tier, seed, SettlementPlan.ClassicRevision);
            Assert.False(plan.IsLively);
            Assert.DoesNotContain(plan.Lots, l => l.Mirrored);
            string text = plan.Serialize();
            Assert.StartsWith("plan|1|", text);
            Assert.All(text.Split('|')[8].Split(';'), lot => Assert.Equal(8, lot.Split(',').Length)); // the pinned format of generations 41 … 45
            Assert.True(SettlementPlan.TryParse(text, out var back));
            Assert.Equal(text, back.Serialize());
        }
    }

    [Theory]
    [InlineData("village_grown_1", "village")]
    [InlineData("town_grid_1", "town")]
    public void ALivelyPlan_CarriesTheFlags_MirrorsSomeLots_AndPinsLosslessly(string kit, string tier)
    {
        int mirrored = 0, lots = 0;
        for (long seed = 1; seed <= 12; seed++)
        {
            var plan = PlanFor(kit, tier, seed, SettlementPlan.LivelyRevision);
            Assert.True(plan.IsLively);
            string text = plan.Serialize();
            Assert.StartsWith("plan|2|", text);
            Assert.All(text.Split('|')[8].Split(';'), lot => Assert.Equal(9, lot.Split(',').Length));
            Assert.True(SettlementPlan.TryParse(text, out var back));
            Assert.Equal(text, back.Serialize());
            Assert.Equal(plan.Lots.Select(l => l.Mirrored), back.Lots.Select(l => l.Mirrored));
            mirrored += plan.Lots.Count(l => l.Mirrored);
            lots += plan.Lots.Count(l => l.Module.Length > 0);
        }

        Assert.InRange(mirrored, lots / 5, lots * 4 / 5); // about every other lot — never none, never all
    }

    [Fact]
    public void AKitThatForbidsIt_IsNeverMirrored()
    {
        var kit = Content.KitByKey("village_grown_1")!;
        bool had = kit.NoMirror;
        try
        {
            kit.NoMirror = true;
            for (long seed = 1; seed <= 6; seed++)
            {
                Assert.DoesNotContain(PlanFor(kit.Key, "village", seed, SettlementPlan.LivelyRevision).Lots, l => l.Mirrored);
            }
        }
        finally
        {
            kit.NoMirror = had;
        }
    }

    [Fact]
    public void MirroringAModuleTwice_GivesItBack_AndKeepsTheDoorOnItsWall()
    {
        int checkedModules = 0, changed = 0;
        foreach (var module in Pool.Where(TemplateTransform.CanMirror))
        {
            var once = TemplateTransform.MirrorX(module);
            var twice = TemplateTransform.MirrorX(once);
            Assert.Equal(module.Width, once.Width);
            Assert.Equal(module.Length, once.Length);
            Assert.Equal(Cells(module), Cells(twice));
            changed += Cells(module) == Cells(once) ? 0 : 1; // a tower that is its own mirror image stays as it is

            var (side, _, z) = SettlementPlanBuilder.FrontDoor(module);
            var (mirroredSide, _, mirroredZ) = SettlementPlanBuilder.FrontDoor(once);
            if (side is 0 or 1)
            {
                Assert.Equal((side, z), (mirroredSide, mirroredZ)); // the front (−Z) stays the front
            }

            checkedModules++;
        }

        Assert.True(checkedModules > 50, $"only {checkedModules} of the shipped modules can be mirrored");
        Assert.True(changed > checkedModules * 3 / 4, $"only {changed} of {checkedModules} modules have a left and a right");
    }

    private static string Cells(StructureTemplate t)
        => string.Join(";", t.Cells.Select(c => $"{c.X},{c.Y},{c.Z},{c.Kind},{c.Id},{c.Shape},{c.Tint},{c.Glow},{c.Config}").OrderBy(s => s, System.StringComparer.Ordinal));

    [Fact]
    public void AMirroredShape_PointsTheOtherWayAlongX_AndStaysAlongZ()
    {
        int stairsWest = ShapeCode.Pack(BlockShape.Stairs, ShapeCode.YawToward(-1, 0));
        int stairsEast = ShapeCode.Pack(BlockShape.Stairs, ShapeCode.YawToward(1, 0));
        int stairsNorth = ShapeCode.Pack(BlockShape.Stairs, ShapeCode.YawToward(0, -1));
        Assert.Equal(stairsEast, TemplateTransform.MirrorShape(stairsWest));
        Assert.Equal(stairsWest, TemplateTransform.MirrorShape(stairsEast));
        Assert.Equal(stairsNorth, TemplateTransform.MirrorShape(stairsNorth));

        int frontPlusX = ShapeCode.Pack(BlockShape.Cube, 0, ShapeCode.FaceFromDirection(1, 0, 0)); // a cube's front rides in the up-face
        int frontMinusX = ShapeCode.Pack(BlockShape.Cube, 0, ShapeCode.FaceFromDirection(-1, 0, 0));
        Assert.Equal(frontMinusX, TemplateTransform.MirrorShape(frontPlusX));

        // A bed pair stays a pair: the head's partner offset flips with the cells.
        int head = ShapeCode.Pack(BlockShape.BedHead, ShapeCode.YawToward(1, 0));
        Assert.True(FurnitureShapes.TryBedPartnerOffset(TemplateTransform.MirrorShape(head), out int dx, out int dz));
        Assert.Equal((-1, 0), (dx, dz));
    }

    [Fact]
    public void WhatHasNoMirrorImage_IsRefused()
    {
        Assert.False(TemplateTransform.CanMirrorShape(ShapeCode.Pack(BlockShape.QuarterCube, 1)));          // a corner has none in the descriptor
        Assert.False(TemplateTransform.CanMirrorShape(ShapeCode.Pack(ShapeCode.FirstCustom, 0)));           // a player-designed form
        Assert.False(TemplateTransform.CanMirrorShape(ShapeCode.WithDesign(ShapeCode.Pack(BlockShape.Slab, 0), 7))); // a painted block
        Assert.False(TemplateTransform.CanMirrorShape(ShapeCode.Pack(BlockShape.Stairs, 1, ShapeCode.FaceFromDirection(0, -1, 0)))); // stairs on the ceiling
        Assert.True(TemplateTransform.CanMirrorShape(ShapeCode.Pack(BlockShape.Panel, 0, ShapeCode.FaceFromDirection(1, 0, 0))));    // a ladder on a wall

        var docked = new StructureTemplate { Width = 3, Height = 3, Length = 3 };
        docked.Cells.Add(new TemplateCell { X = 0, Y = 1, Z = 1, Id = "iron_wall", Port = "door" });
        Assert.False(TemplateTransform.CanMirror(docked)); // a station module docks by its ports' faces
    }

    [Fact]
    public void AKit_IsDrawnOnlyInsideItsGenerations()
    {
        var kit = new StructureKit { MinTerrainGeneration = 41, MaxTerrainGeneration = 45 };
        Assert.False(kit.DrawnAt(40));
        Assert.True(kit.DrawnAt(41));
        Assert.True(kit.DrawnAt(45));
        Assert.False(kit.DrawnAt(46));
        Assert.True(new StructureKit().DrawnAt(0));
        Assert.True(new StructureKit().DrawnAt(WorldDescription.CurrentTerrainGeneration));
        Assert.True(new StructureKit { MinTerrainGeneration = 46 }.DrawnAt(999));

        // The editors' lists ask for every kit whatever its bounds say.
        var all = Content.KitsFor(StructureKit.KindSettlement, "village", null, null);
        Assert.Contains(all, k => k.MinTerrainGeneration > 0);
        foreach (int generation in new[] { 0, WorldDescription.GeographyGeneration, WorldDescription.GrownSettlementsGeneration, WorldDescription.CurrentTerrainGeneration })
        {
            Assert.All(Content.KitsFor(StructureKit.KindSettlement, "village", null, null, generation), k => Assert.Contains(k, all));
        }
    }

    [Fact]
    public void ThePlaceList_AndTheEmote_TravelThroughTheCodec()
    {
        var list = new PlaceList
        {
            Places = new[]
            {
                new NetPlace
                {
                    Key = "settle_12345",
                    Kind = PlaceKinds.Settlement,
                    Name = "Mira Mill",
                    Tier = "village",
                    MinX = 100, MinZ = -40, MaxX = 155, MaxZ = 15, Y = 71, Color = 0xB8442C,
                    PlazaX = 128, PlazaY = 72, PlazaZ = -12,
                    Trades = new[] { StructureRoles.Market, StructureRoles.Tavern },
                    Signs = new[] { new NetPlaceSign { X = 120, Y = 72, Z = -20, Side = 3, Fn = StructureRoles.Tavern } },
                    Points = new[] { new NetPlacePoint { X = 130, Y = 72, Z = -10, Kind = PlacePointKinds.Fire } },
                    State = PlaceStates.Market,
                },
            },
        };

        var back = Assert.IsType<PlaceList>(NetCodec.Decode(NetCodec.Encode(list)));
        var place = Assert.Single(back.Places);
        Assert.Equal("Mira Mill", place.Name);
        Assert.Equal((100, -40, 155, 15, 71), (place.MinX, place.MinZ, place.MaxX, place.MaxZ, place.Y));
        Assert.Equal(0xB8442C, place.Color);
        Assert.Equal(new[] { StructureRoles.Market, StructureRoles.Tavern }, place.Trades);
        Assert.Equal((byte)3, Assert.Single(place.Signs).Side);
        Assert.Equal(PlacePointKinds.Fire, Assert.Single(place.Points).Kind);
        Assert.Equal(PlaceStates.Market, place.State);

        var emote = Assert.IsType<NpcEmote>(NetCodec.Decode(NetCodec.Encode(new NpcEmote { NpcId = 42, Gesture = NpcGestures.Dance })));
        Assert.Equal((42, NpcGestures.Dance), (emote.NpcId, emote.Gesture));
    }
}
