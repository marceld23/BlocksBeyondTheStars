// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2676: the flight view's broken-look extras of a derelict are planned from the hull cells the client holds —
/// the flickering beacon on the snapped mast's dead lamp, sparks at ragged edges, a few tumbling pieces in free space
/// beside the hull — the same on every client.</summary>
public sealed class DerelictDecorPlanTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    /// <summary>A beta wreck's cells as the flight view gets them (the design's non-air cells).</summary>
    private static Dictionary<Vector3i, BlockId> Hull(long seed)
    {
        var l = DerelictGenerator.Generate(seed, Content.Value);
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int x = 0; x < l.Width; x++)
            for (int y = 0; y < l.Height; y++)
                for (int z = 0; z < l.Length; z++)
                {
                    if (l.Get(x, y, z) != 0)
                    {
                        cells[new Vector3i(x, y, z)] = new BlockId(l.Get(x, y, z));
                    }
                }

        return cells;
    }

    [Theory]
    [InlineData(11)]
    [InlineData(4242)]
    [InlineData(-90210)]
    public void ABetaWreck_FlickersOnItsDeadBeacon_SparksAtRaggedEdges_AndTumblesPiecesInFreeSpace(long seed)
    {
        var cells = Hull(seed);
        ushort deadLamp = Content.Value.GetBlock("light_white_off")!.NumericId.Value;
        var plan = DerelictDecorPlan.Build(cells, "derelict:sys0-0", b => b.Value == deadLamp);

        Assert.NotNull(plan.BeaconCell);
        Assert.Equal(deadLamp, cells[plan.BeaconCell!.Value].Value); // the snapped mast's dead beacon

        Assert.InRange(plan.SparkCells.Count, 1, DerelictDecorPlan.MaxSparkCells);
        foreach (var c in plan.SparkCells)
        {
            Assert.True(cells.ContainsKey(c), $"spark point {c} is no hull cell");
            Assert.True(OpenFaces(cells, c) >= DerelictDecorPlan.RaggedOpenFaces, $"spark point {c} is no ragged edge");
        }

        Assert.InRange(plan.DebrisHomes.Count, 3, 6);
        foreach (var d in plan.DebrisHomes)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Assert.False(cells.ContainsKey(new Vector3i(d.X + dx, d.Y + dy, d.Z + dz)), $"debris home {d} touches the hull");
                    }

            Assert.Contains(cells.Keys, c => Math.Abs(c.X - d.X) <= 4 && Math.Abs(c.Y - d.Y) <= 4 && Math.Abs(c.Z - d.Z) <= 4); // beside it
        }
    }

    [Fact]
    public void ThePlan_IsTheSameOnEveryClient_WhateverOrderTheDesignArrivedIn()
    {
        var cells = Hull(77);
        var reversed = cells.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value);
        var a = DerelictDecorPlan.Build(cells, "derelict:sys3-1");
        var b = DerelictDecorPlan.Build(reversed, "derelict:sys3-1");
        Assert.Equal(a.BeaconCell, b.BeaconCell);
        Assert.Equal(a.SparkCells, b.SparkCells);
        Assert.Equal(a.DebrisHomes, b.DebrisHomes);
    }

    [Fact]
    public void WithoutABeaconBlock_TheHighestCellOverTheMiddle_Flickers_AndAnEmptyHullPlansNothing()
    {
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int x = 0; x < 15; x++)
            for (int z = 0; z < 15; z++)
            {
                cells[new Vector3i(x, 0, z)] = new BlockId(1);
            }

        cells[new Vector3i(7, 1, 7)] = new BlockId(1);
        cells[new Vector3i(7, 2, 7)] = new BlockId(1); // an antenna in the middle
        cells[new Vector3i(0, 3, 0)] = new BlockId(1); // higher, but off the middle
        var plan = DerelictDecorPlan.Build(cells, "derelict:old");
        Assert.Equal(new Vector3i(7, 2, 7), plan.BeaconCell);

        var none = DerelictDecorPlan.Build(new Dictionary<Vector3i, BlockId>(), "derelict:none");
        Assert.Null(none.BeaconCell);
        Assert.Empty(none.SparkCells);
        Assert.Empty(none.DebrisHomes);
    }

    private static int OpenFaces(Dictionary<Vector3i, BlockId> cells, Vector3i c)
    {
        int open = 0;
        foreach (var n in new[]
        {
            new Vector3i(c.X + 1, c.Y, c.Z), new Vector3i(c.X - 1, c.Y, c.Z), new Vector3i(c.X, c.Y + 1, c.Z),
            new Vector3i(c.X, c.Y - 1, c.Z), new Vector3i(c.X, c.Y, c.Z + 1), new Vector3i(c.X, c.Y, c.Z - 1),
        })
        {
            open += cells.ContainsKey(n) ? 0 : 1;
        }

        return open;
    }
}
