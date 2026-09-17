// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Furniture is no floor, a rug is no wall (#1895, Marcel 2026-09-14). The rules on their own, the NPC predicates on a
/// stone pad, and three walks: across a row of furniture, boxed in by it, and over a rug.
/// </summary>
public sealed class NpcFootingTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static int Form(BlockShape shape, int yaw = 0, int upFace = ShapeCode.UpPlusY) => ShapeCode.Pack((int)shape, yaw, upFace);

    [Theory]
    [InlineData(BlockShape.Table)]
    [InlineData(BlockShape.Chair)]
    [InlineData(BlockShape.Bench)]
    [InlineData(BlockShape.BedHead)]
    [InlineData(BlockShape.BedFoot)]
    [InlineData(BlockShape.Fence)]
    [InlineData(BlockShape.Pot)]
    public void FurnitureForms_AreNoFloor_OnAnyMaterial(BlockShape shape)
    {
        Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of("stone", Form(shape)));
        Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of("wood_log", Form(shape, yaw: 3)));
    }

    [Theory]
    [InlineData("crate")]
    [InlineData("wood_crate")]
    [InlineData("workbench")]
    [InlineData("forge")]
    [InlineData("data_cache")]
    [InlineData("flower_pot")]
    [InlineData("campfire")]
    public void FurnitureAndDeviceBlocks_AreNoFloor_WhateverTheirForm(string key)
    {
        Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of(key, 0));
        Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of(key, Form(BlockShape.Slab)));
    }

    [Fact]
    public void EveryProfessionPost_IsNoFloor()
    {
        // #1940: the eight posts of 2026-09 were missing from the list, so an NPC could path onto the counter
        // it was standing at. The list is derived from the profession table now — this guards that wiring.
        foreach (var profession in BlocksBeyondTheStars.Shared.Definitions.NpcProfessions.All)
        {
            Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of(profession.PostBlock, 0));
        }
    }

    [Fact]
    public void TheCrewBunk_IsNoFloor()
        => Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of("crew_bunk", 0));

    [Fact]
    public void TheLegacyOneCellBed_IsNoFloor()
        => Assert.Equal(NpcFooting.NoFloor, NpcFootings.Of("bed", ShapeCode.Pack(PropShapes.BedSingleCell, 1)));

    [Fact]
    public void StepsAndFloorBlocks_StayFloors()
    {
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("stone", 0));
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("steel_floor", Form(BlockShape.Slab)));
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("stairs", Form(BlockShape.Stairs, yaw: 2)));
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("stone", Form(BlockShape.Ramp)));
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("hydro_tray", 0));   // a greenhouse's deck plate
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("factory_pipe", 0)); // stepped over
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of("base_core", 0));
        Assert.Equal(NpcFooting.Floor, NpcFootings.Of(null, 0));
    }

    [Fact]
    public void Plates_LyingFlat_AreWalkedThrough_AndOnlyTheFloorOneCarries()
    {
        var rug = NpcFootings.Of("rug", Form(BlockShape.Sheet));
        var floorPanel = NpcFootings.Of("steel_floor", Form(BlockShape.Panel, yaw: 1));
        var ceilingPanel = NpcFootings.Of("steel_floor", Form(BlockShape.Panel, upFace: 1));
        var wallSheet = NpcFootings.Of("wood_log", Form(BlockShape.Sheet, upFace: 3));

        Assert.Equal(NpcFooting.FloorPlate, rug);
        Assert.Equal(NpcFooting.FloorPlate, floorPanel);
        Assert.Equal(NpcFooting.CeilingPlate, ceilingPanel);
        Assert.Equal(NpcFooting.NoFloor, wallSheet);
        Assert.True(NpcFootings.PassesBody(rug) && NpcFootings.PassesBody(ceilingPanel));
        Assert.False(NpcFootings.PassesBody(wallSheet) || NpcFootings.PassesBody(NpcFooting.Floor));
    }

    [Fact]
    public void EveryNoFloorBlock_IsARealBlock()
    {
        var missing = NpcFootings.NoFloorBlocks.Where(k => _content.GetBlock(k) is null).ToList();
        Assert.True(missing.Count == 0, "unknown block keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThePredicates_OnAPad_NeverATabletop_AlwaysThroughARug()
    {
        using var w = new NpcLifeWorld(_content, "footing_predicates");
        int f = w.Feet, z = NpcLifeWorld.Cz + 12;
        int x = NpcLifeWorld.Cx - 20;

        (string Key, int Form)[] noFloor =
        {
            ("stone", Form(BlockShape.Table)), ("stone", Form(BlockShape.Chair)), ("stone", Form(BlockShape.Bench)),
            ("bed", Form(BlockShape.BedHead)), ("stone", Form(BlockShape.Fence)), ("flower_pot", Form(BlockShape.Pot)),
            ("crate", 0), ("workbench", 0), ("steel_floor", Form(BlockShape.Panel, upFace: 2)),
        };
        foreach (var (key, form) in noFloor)
        {
            w.Set(x, f, z, key, form);
            Assert.False(w.Server.NpcStandableAtForTest(x, f + 1, z), $"stands on {key} form {form}");
            Assert.Null(w.Server.NpcStandableSpotForTest(x, f + 1, z));
            Assert.False(w.Server.NpcStandableAtForTest(x, f, z), $"stands inside {key} form {form}");
            x += 2;
        }

        // Floors stay floors: a cube, a slab, the hydroponics tray.
        foreach (var (key, form) in new[] { ("stone", 0), ("steel_floor", Form(BlockShape.Slab)), ("hydro_tray", 0) })
        {
            w.Set(x, f, z, key, form);
            Assert.True(w.Server.NpcStandableAtForTest(x, f + 1, z), $"cannot stand on {key}");
            Assert.NotNull(w.Server.NpcStandableSpotForTest(x, f + 1, z));
            x += 2;
        }

        // A rug: the feet stand in its cell, on the floor — never a block above it.
        w.Set(x, f, z, "rug", Form(BlockShape.Sheet));
        Assert.True(w.Server.NpcStandableAtForTest(x, f, z));
        Assert.Equal(f, w.Server.NpcStandableSpotForTest(x, f, z)!.Value.Y);
        Assert.False(w.Server.NpcStandableAtForTest(x, f + 1, z));
        x += 2;

        // A ceiling panel at head height is walked under; a floor panel two above the pad carries a catwalk.
        w.Set(x, f + 1, z, "steel_floor", Form(BlockShape.Panel, upFace: 1));
        Assert.True(w.Server.NpcStandableAtForTest(x, f, z));
        x += 2;
        w.Set(x, f + 2, z, "steel_floor", Form(BlockShape.Panel));
        Assert.True(w.Server.NpcStandableAtForTest(x, f + 2, z));
        Assert.NotNull(w.Server.NpcStandableSpotForTest(x, f + 2, z));
    }

    [Fact]
    public void AWalker_GoesAroundARowOfFurniture_NeverOverIt()
    {
        using var w = new NpcLifeWorld(_content, "footing_row");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        int f = w.Feet, rx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz + 16;

        // Seven pieces across the straight line (the bench sits right on it); the ends stay open.
        (string Key, int Form)[] row =
        {
            ("stone", Form(BlockShape.Table)), ("stone", Form(BlockShape.Chair)), ("crate", 0), ("stone", Form(BlockShape.Bench)),
            ("stone", Form(BlockShape.Table)), ("stone", Form(BlockShape.Fence)), ("workbench", 0),
        };
        for (int i = 0; i < row.Length; i++)
        {
            w.Set(rx, f, cz - 3 + i, row[i].Key, row[i].Form);
        }

        w.Server.MoveNpcForTest(id, new Vector3f(rx - 3.5f, f, cz + 0.5f));
        w.Server.SetNpcGoalForTest(id, new Vector3f(rx + 3.5f, f, cz + 0.5f));
        w.Player.State.Position = new Vector3f(rx - 6.5f, f, cz + 0.5f); // watched: no set-down at the goal

        float maxY = f;
        w.TickAt(0.45, 90, until: () => !w.Server.NpcHasGoalForTest(id),
            every: () => maxY = Math.Max(maxY, w.Server.NpcSnapshots.Single(n => n.Id == id).Pos.Y));

        Assert.False(w.Server.NpcHasGoalForTest(id), "never got past the row");
        Assert.True(maxY < f + 0.5f, $"climbed onto the furniture (feet at {maxY}, floor {f})");
        Assert.True(w.Server.NpcSnapshots.Single(n => n.Id == id).Pos.X > rx + 2f);
    }

    [Fact]
    public void AStroller_BoxedInByFurniture_NeverClimbsOne()
    {
        using var w = new NpcLifeWorld(_content, "footing_boxed");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        int f = w.Feet, bx = NpcLifeWorld.Cx - 14, bz = NpcLifeWorld.Cz + 14;

        var ring = new Queue<(string Key, int Form)>(new[]
        {
            ("stone", Form(BlockShape.Table)), ("stone", Form(BlockShape.Chair, yaw: 1)), ("stone", Form(BlockShape.Bench)),
            ("crate", 0), ("workbench", 0), ("stone", Form(BlockShape.Fence)), ("flower_pot", Form(BlockShape.Pot)),
            ("bed", ShapeCode.Pack(PropShapes.BedSingleCell, 0)),
        });
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx != 0 || dz != 0)
                {
                    var (key, form) = ring.Dequeue();
                    w.Set(bx + dx, f, bz + dz, key, form);
                }
            }

        var centre = new Vector3f(bx + 0.5f, f, bz + 0.5f);
        w.Server.MoveNpcForTest(id, centre);
        w.Server.SetNpcGoalForTest(id, centre); // arrives at once and strolls on the wander leash around it
        w.Player.State.Position = new Vector3f(bx + 4.5f, f, bz + 0.5f);

        float maxY = f;
        w.TickAt(0.45, 60, every: () => maxY = Math.Max(maxY, w.Server.NpcSnapshots.Single(n => n.Id == id).Pos.Y));

        Assert.True(maxY < f + 0.5f, $"climbed out over the furniture (feet at {maxY}, floor {f})");
    }

    [Fact]
    public void AWalker_CrossesARug_AtFloorHeight()
    {
        using var w = new NpcLifeWorld(_content, "footing_rug");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        int f = w.Feet, rx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz + 16;

        for (int dz = -6; dz <= 6; dz++)
        {
            w.Set(rx, f, cz + dz, "rug", Form(BlockShape.Sheet));
        }

        w.Server.MoveNpcForTest(id, new Vector3f(rx - 3.5f, f, cz + 0.5f));
        w.Server.SetNpcGoalForTest(id, new Vector3f(rx + 3.5f, f, cz + 0.5f));
        w.Player.State.Position = new Vector3f(rx - 6.5f, f, cz + 0.5f);

        float maxY = f;
        w.TickAt(0.45, 60, until: () => !w.Server.NpcHasGoalForTest(id),
            every: () => maxY = Math.Max(maxY, w.Server.NpcSnapshots.Single(n => n.Id == id).Pos.Y));

        Assert.False(w.Server.NpcHasGoalForTest(id), "never crossed the rug");
        Assert.True(maxY < f + 0.5f, $"walked a block above the rug (feet at {maxY}, floor {f})");
    }

    [Fact]
    public void AChairBoxedInByItsTableAndCrates_IsNeverReachedOverThem()
    {
        using var w = new NpcLifeWorld(_content, "footing_seat");
        int baseId = w.FoundBase();
        int cx = NpcLifeWorld.Cx - 1, cz = NpcLifeWorld.Cz + 5, f = w.Feet;
        var chair = w.Chair(cx, cz);                                // backrest toward +Z: its front is −Z
        w.Set(cx, f, cz - 1, "stone", Form(BlockShape.Table));      // the table in front
        w.Set(cx - 1, f, cz, "crate");
        w.Set(cx + 1, f, cz, "crate");
        w.Set(cx, f, cz + 1, "crate");
        w.Server.ScanBaseLifeForTest();
        var settler = w.Server.BaseResidentsForTest(baseId).Single(r => r.Slot == 0);
        Assert.Equal(chair, settler.Seat);

        float maxStandingY = f;
        w.TickAt(0.72, 60, until: () => w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1, every: () =>
        {
            var r = w.Server.NpcRoutineForTest(settler.NpcId);
            if (r.Pose == 0)
            {
                maxStandingY = Math.Max(maxStandingY, r.Pos.Y);
            }
        });

        Assert.Equal(1, w.Server.NpcRoutineForTest(settler.NpcId).Pose);
        Assert.True(maxStandingY < f + 0.5f, $"climbed onto the table or a crate to sit (feet at {maxStandingY}, floor {f})");
    }
}
