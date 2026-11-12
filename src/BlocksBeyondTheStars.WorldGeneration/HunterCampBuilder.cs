// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The geometry of a hunter shuttle's camp on the ground (#3097; #3522 moved it out of <c>GameServerHunterSiteStamp</c>):
/// the hull box of <see cref="HunterShuttleHull"/> with its origin (−X/−Z corner) at (<c>ox</c>, <c>fy</c>, <c>oz</c>) —
/// hull cells, air everywhere else in the box and one row over the roof (the hill, the grass, a trunk) — the ground filled
/// under the deck where it dips (at most four deep, through air and fluids), the shuttle's own dock in the hold and one or two
/// more beside it, each on a steel plate on the ground of its own column. Every dock is a <see cref="DockMarker"/> spot, in the
/// order the docks are built; the number of outside docks is drawn from the camp's random stream.
/// </summary>
public static class HunterCampBuilder
{
    /// <summary>The marker of a robot dock (the cell the dock block stands in).</summary>
    public const string DockMarker = "dock";

    /// <summary>
    /// Emits one camp. <paramref name="groundSurface"/> is the planet's surface block key (the fill under the deck);
    /// <paramref name="groundFeet"/> (x, z) gives the feet row on the real ground of a column (the first cell above the ground
    /// that carries a body) for the outside docks, and <paramref name="dockFits"/> (x, z, feet) says whether a dock may stand
    /// there (dry, inside the build height).
    /// </summary>
    public static void Build(IStructureCellSink sink, GameContent content, string groundSurface, int ox, int fy, int oz, Random rng,
        Func<int, int, int> groundFeet, Func<int, int, int, bool> dockFits)
    {
        var bay = content.GetBlock(HunterShuttleHull.DockBlockKey);
        BuildHullBox(sink, content, bay, ox, fy, oz);
        FillUnderDeck(sink, content, groundSurface, ox, fy, oz);
        if (bay is not null)
        {
            sink.Mark(DockMarker, ox + HunterShuttleHull.Bay.X, fy + HunterShuttleHull.Bay.Y, oz + HunterShuttleHull.Bay.Z);
            int outside = 1 + rng.Next(HunterShuttleHull.OutsideDocks.Length); // 2–3 docks in all
            for (int i = 0; i < outside; i++)
            {
                BuildOutsideDock(sink, content, bay, ox, oz, HunterShuttleHull.OutsideDocks[i], groundFeet, dockFits);
            }
        }
    }

    /// <summary>The hull box: hull cells (dyed where the block takes a dye), the bay facing the hatch, air in every other cell
    /// of the box and of the row over the roof.</summary>
    private static void BuildHullBox(IStructureCellSink sink, GameContent content, BlockDefinition? bay, int ox, int fy, int oz)
    {
        var cells = new Dictionary<Vector3i, (BlockDefinition Def, int Tint, int Shape)>();
        foreach (var (cell, part) in HunterShuttleHull.Cells)
        {
            if (HunterShuttleHull.BlockOf(content, part) is { } def)
            {
                cells[cell] = (def, def.Tintable ? HunterShuttleHull.DyeOf(part) : 0, 0);
            }
        }

        if (bay is not null)
        {
            cells[HunterShuttleHull.Bay] = (bay, 0, CubeFacing.Pack(HunterShuttleHull.BayFront));
        }

        for (int x = 0; x < HunterShuttleHull.Width; x++)
            for (int z = 0; z < HunterShuttleHull.Length; z++)
                for (int y = 0; y <= HunterShuttleHull.Height; y++) // one row over the roof too: no grass tuft on top
                {
                    if (cells.TryGetValue(new Vector3i(x, y, z), out var c))
                    {
                        sink.Set(ox + x, fy + y, oz + z, c.Def.NumericId, c.Tint, 0, c.Shape);
                    }
                    else
                    {
                        sink.Put(ox + x, fy + y, oz + z, BlockId.Air);
                    }
                }
    }

    /// <summary>Fills the dips under the deck with the planet's ground (at most four cells deep per column, down through air
    /// and fluids until the first solid cell).</summary>
    private static void FillUnderDeck(IStructureCellSink sink, GameContent content, string groundSurface, int ox, int fy, int oz)
    {
        var ground = (content.GetBlock(groundSurface) ?? content.GetBlock("stone"))?.NumericId ?? BlockId.Air;
        if (ground.IsAir)
        {
            return;
        }

        ushort water = content.GetBlock("water")?.NumericId.Value ?? 0;
        ushort lava = content.GetBlock("lava")?.NumericId.Value ?? 0;
        foreach (var (cell, part) in HunterShuttleHull.Cells)
        {
            if (part != HunterShuttleHull.Part.Floor)
            {
                continue;
            }

            for (int d = 1; d <= 4; d++)
            {
                var id = sink.Get(ox + cell.X, fy - d, oz + cell.Z);
                bool fluid = id.Value != 0 && (id.Value == water || id.Value == lava);
                if (!id.IsAir && !fluid)
                {
                    break;
                }

                sink.Put(ox + cell.X, fy - d, oz + cell.Z, ground);
            }
        }
    }

    /// <summary>A dock beside the shuttle: on the ground of its own column, on a steel plate, its front facing out, the cell
    /// above it cleared — skipped where <paramref name="dockFits"/> refuses the column.</summary>
    private static void BuildOutsideDock(IStructureCellSink sink, GameContent content, BlockDefinition bay, int ox, int oz,
        (int X, int Z, int Front) spot, Func<int, int, int> groundFeet, Func<int, int, int, bool> dockFits)
    {
        int x = ox + spot.X, z = oz + spot.Z;
        int feet = groundFeet(x, z);
        if (!dockFits(x, z, feet))
        {
            return;
        }

        var plate = content.GetBlock("steel_floor") ?? content.GetBlock("metal_panel");
        if (plate is not null)
        {
            sink.Set(x, feet - 1, z, plate.NumericId, plate.Tintable ? HunterShuttleHull.FloorDye : 0, 0, 0);
        }

        sink.Set(x, feet, z, bay.NumericId, 0, 0, CubeFacing.Pack(spot.Front));
        sink.Put(x, feet + 1, z, BlockId.Air);
        sink.Mark(DockMarker, x, feet, z);
    }
}
