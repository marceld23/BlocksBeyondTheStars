// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The beds of a ruin or a wreck (terrain generation 45, <see cref="WorldDescription.AbandonedBedsGeneration"/>). Nobody lives
/// there, so every bed the structure carries becomes the abandoned twin (<see cref="FurnitureShapes.AbandonedBedKey"/>): the
/// working bed's look, but a block no bed rule reads — no home spawn, no rest heal, no settler drawn to it, no sleeper (all of
/// those go by the <c>bed</c> block id). And it looks like today's two-cell bed:
/// <list type="bullet">
/// <item>a two-cell bed keeps both halves (a half whose partner the ruin took stays a half);</item>
/// <item>a one-cell bed — the legacy slab of a cramped room, or a plain cube a template placed — becomes the head half: joined
/// with a one-cell bed right beside it into one bed (along its own yaw axis; a cube, which carries no direction, in any
/// direction, +Z first), else given a foot on a free floor cell beside it where the caller allows one (the giant wrecks), else
/// a head alone.</item>
/// </list>
/// Pure and deterministic: the cells are visited in index order. Callers gate it by the world's generation
/// (<see cref="Applies"/>), so every older world builds its ruins exactly as before.
/// </summary>
public static class AbandonedBeds
{
    /// <summary>Whether a world of <paramref name="terrainGeneration"/> builds its ruins and wrecks with abandoned beds.</summary>
    public static bool Applies(int terrainGeneration) => terrainGeneration >= WorldDescription.AbandonedBedsGeneration;

    /// <summary>
    /// A ruined structure the settlement stamp writes (a fallen settlement, a ruin, a lab, a monument …) with abandoned beds —
    /// the structure itself when it holds no bed, else a copy (its markers, column map and modifiers carried over). Its one-cell
    /// beds never reach into a free cell: a cramped room kept the short bed because the long one did not fit.
    /// </summary>
    public static SettlementStructure Apply(SettlementStructure structure, GameContent content)
    {
        ushort bed = content.GetBlock(FurnitureShapes.BedKey)?.NumericId.Value ?? 0;
        if (bed == 0 || content.GetBlock(FurnitureShapes.AbandonedBedKey) is null || !structure.Holds(bed))
        {
            return structure;
        }

        var grid = structure.ToGrid();
        Apply(grid, content, footAllowed: null);
        return structure.WithCells(grid);
    }

    /// <summary>
    /// Turns every bed of <paramref name="grid"/> into the abandoned twin (see the class summary). <paramref name="footAllowed"/>
    /// (x, y, z): whether a lone bed may put its foot on that free floor cell — null never. Returns how many cells became the
    /// abandoned bed (0 when the content has no such block).
    /// </summary>
    internal static int Apply(VoxelGrid grid, GameContent content, Func<int, int, int, bool>? footAllowed)
    {
        ushort bed = content.GetBlock(FurnitureShapes.BedKey)?.NumericId.Value ?? 0;
        ushort abandoned = content.GetBlock(FurnitureShapes.AbandonedBedKey)?.NumericId.Value ?? 0;
        if (bed == 0 || abandoned == 0)
        {
            return 0;
        }

        var formed = new HashSet<int>();
        for (int i = 0; i < grid.Blocks.Length; i++)
        {
            if (grid.Blocks[i] != bed || formed.Contains(i))
            {
                continue;
            }

            int z = i % grid.L;
            int y = i / grid.L % grid.H;
            int x = i / (grid.L * grid.H);
            if (!FurnitureShapes.IsBedHalf(ShapeCode.ShapeOf(grid.ShapeAt(x, y, z))))
            {
                formed.Add(i);
                FormLoneBed(grid, content, bed, x, y, z, footAllowed, formed);
            }
        }

        int changed = 0;
        for (int i = 0; i < grid.Blocks.Length; i++)
        {
            if (grid.Blocks[i] == bed)
            {
                grid.Blocks[i] = abandoned;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// The foot rule of a giant wreck: never on a cell the ruin keeps (an entrance's clearance), and never in the column of a
    /// marker or right beside it — a chest, a sound, a doorway and the way in stay as they are, and no marker settles
    /// differently because a bed's foot now stands where it would have come to rest.
    /// </summary>
    internal static Func<int, int, int, bool> AwayFromMarkers(VoxelGrid grid, IEnumerable<SettlementMarker> markers)
    {
        var columns = new HashSet<(int X, int Z)>();
        foreach (var m in markers)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    columns.Add((m.LocalPos.X + dx, m.LocalPos.Z + dz));
                }
            }
        }

        return (x, y, z) => !grid.Keep[grid.Index(x, y, z)] && !columns.Contains((x, z));
    }

    /// <summary>A one-cell bed at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) becomes a head half: with
    /// the lone bed beside it as its foot, else with a foot on an allowed free floor cell, else alone.</summary>
    private static void FormLoneBed(VoxelGrid grid, GameContent content, ushort bed, int x, int y, int z,
        Func<int, int, int, bool>? footAllowed, HashSet<int> formed)
    {
        int descriptor = grid.ShapeAt(x, y, z);
        bool oriented = ShapeCode.ShapeOf(descriptor) != (int)BlockShape.Cube;
        int yaw0 = oriented ? ShapeCode.OrientationOf(descriptor) & 3 : 0;
        int[] order = { yaw0, (yaw0 + 2) & 3, (yaw0 + 1) & 3, (yaw0 + 3) & 3 };
        int tries = oriented ? 2 : 4; // an oriented short bed lies along its yaw axis; a cube says nothing

        for (int k = 0; k < tries; k++)
        {
            var (dx, dz) = ShapeCode.YawDirection(order[k]);
            int nx = x + dx, nz = z + dz;
            if (grid.In(nx, y, nz) && grid.Blocks[grid.Index(nx, y, nz)] == bed && !formed.Contains(grid.Index(nx, y, nz))
                && !FurnitureShapes.IsBedHalf(ShapeCode.ShapeOf(grid.ShapeAt(nx, y, nz))))
            {
                formed.Add(grid.Index(nx, y, nz));
                PutPair(grid, x, y, z, nx, nz, order[k]);
                return;
            }
        }

        for (int k = 0; footAllowed != null && k < tries; k++)
        {
            var (dx, dz) = ShapeCode.YawDirection(order[k]);
            int nx = x + dx, nz = z + dz;
            if (FreeFloor(grid, content, nx, y, nz) && footAllowed(nx, y, nz))
            {
                grid.Blocks[grid.Index(nx, y, nz)] = bed;
                grid.Cavity[grid.Index(nx, y, nz)] = false;
                grid.Air[grid.Index(nx, y, nz)] = false;
                PutPair(grid, x, y, z, nx, nz, order[k]);
                return;
            }
        }

        grid.Shapes[grid.Index(x, y, z)] = ShapeCode.Pack(BlockShape.BedHead, yaw0, ShapeCode.UpPlusY);
    }

    /// <summary>The head at (x, y, z) facing <paramref name="yaw"/> (its foot lies that way) and the foot at (fx, y, fz).</summary>
    private static void PutPair(VoxelGrid grid, int x, int y, int z, int fx, int fz, int yaw)
    {
        int head = ShapeCode.Pack(BlockShape.BedHead, yaw, ShapeCode.UpPlusY);
        grid.Shapes[grid.Index(x, y, z)] = head;
        grid.Shapes[grid.Index(fx, y, fz)] = FurnitureShapes.BedPartnerDescriptor(head);
    }

    /// <summary>An empty cell inside the grid that stands on a solid block.</summary>
    private static bool FreeFloor(VoxelGrid grid, GameContent content, int x, int y, int z)
        => grid.In(x, y, z) && grid.Blocks[grid.Index(x, y, z)] == 0 && y > 0
           && grid.Blocks[grid.Index(x, y - 1, z)] is var below && below != 0
           && content.BlockById(new Shared.Primitives.BlockId(below))?.Solid == true;
}
