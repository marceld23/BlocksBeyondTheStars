// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The geometry of the finale's buried Guardian-core chamber (#3522 moved it out of <c>GameServerStoryFinale.CoreChamber</c>):
/// an 11×11 iron shell (9×9 inside, six air high) with a plated floor, red glow strips, the dormant red core on its pedestal
/// between four metal pillars and glass windows — and Route A, a 3×3 aperture shaft from the surface down into the chamber,
/// offset toward +Z (<see cref="ShaftOffsetZ"/>) so you drop onto open floor, ringed at its mouth by two-high plating that
/// stands on the ground of each column. The core's cell is a <see cref="CoreMarker"/> spot.
/// </summary>
public static class GuardianCoreBuilder
{
    /// <summary>How far the surface lies above the core column's floor cell (the chamber's floor lies one deeper).</summary>
    public const int ShaftDepth = 23;

    /// <summary>How far the aperture shaft sits from the core column towards +Z.</summary>
    public const int ShaftOffsetZ = 3;

    /// <summary>The marker on the core column's lowest cell (the terminal you breach).</summary>
    public const string CoreMarker = "guardian_core";

    /// <summary>The chamber's floor row for a surface measured at <paramref name="surfaceY"/> over the chamber's centre.</summary>
    public static int FloorY(int surfaceY) => surfaceY - (ShaftDepth + 1);

    /// <summary>The chamber's shell block: iron wall, else deepslate, else stone.</summary>
    public static BlockId Shell(GameContent content)
        => (content.GetBlock("iron_wall") ?? content.GetBlock("deepslate") ?? content.GetBlock("stone"))!.NumericId;

    /// <summary>Emits the chamber around the core column (<paramref name="ax"/>, <paramref name="az"/>) on
    /// <paramref name="floorY"/>, then the aperture up to <paramref name="surfaceY"/> with its ring on
    /// <paramref name="ground"/> (x, z → the surface row).</summary>
    public static void Build(IStructureCellSink sink, GameContent content, int ax, int az, int floorY, int surfaceY, Func<int, int, int> ground)
    {
        var shell = Shell(content);
        BuildChamber(sink, content, ax, az, floorY, shell);
        BuildAperture(sink, ax, az, floorY, surfaceY, shell, ground);
        sink.Mark(CoreMarker, ax, floorY + 1, az);
    }

    /// <summary>The chamber itself: the shell with its plated floor, the glow strips, the pedestal, the core, the pillars and
    /// the windows.</summary>
    private static void BuildChamber(IStructureCellSink sink, GameContent content, int ax, int az, int floorY, BlockId shell)
    {
        var core = content.GetBlock("light_red")?.NumericId ?? BlockId.Air;
        var floorBlk = content.GetBlock("steel_floor")?.NumericId ?? shell;
        var frame = content.GetBlock("metal_panel")?.NumericId ?? shell;
        var window = content.GetBlock("glass")?.NumericId ?? BlockId.Air;
        const int R = 5;
        for (int dx = -R; dx <= R; dx++)
            for (int dz = -R; dz <= R; dz++)
                for (int dy = -1; dy <= 6; dy++)
                {
                    bool wall = dx == -R || dx == R || dz == -R || dz == R;
                    sink.Put(ax + dx, floorY + dy, az + dz, dy == -1 ? floorBlk : dy == 6 || wall ? shell : BlockId.Air); // plated walkable floor
                }

        // Glow strips: red lights set into the middle of each wall at eye level.
        if (!core.IsAir)
        {
            foreach (var (wx, wz) in new[] { (-R, 0), (R, 0), (0, -R), (0, R) })
            {
                sink.Put(ax + wx, floorY + 2, az + wz, core);
            }
        }

        // The dormant core: a red heart on a plated pedestal, four metal pillars, glass windows — the terminal to breach.
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                sink.Put(ax + dx, floorY, az + dz, frame);
            }

        if (!core.IsAir)
        {
            for (int dy = 1; dy <= 4; dy++)
            {
                sink.Put(ax, floorY + dy, az, core);
            }
        }

        foreach (var (cx, cz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            for (int dy = 1; dy <= 5; dy++)
            {
                sink.Put(ax + cx, floorY + dy, az + cz, frame);
            }
        }

        if (!window.IsAir)
        {
            foreach (var (ex, ez) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                for (int dy = 2; dy <= 3; dy++)
                {
                    sink.Put(ax + ex, floorY + dy, az + ez, window);
                }
            }
        }
    }

    /// <summary>Route A — the aperture: the 3×3 open shaft from the surface down into the chamber and the low ring of plating
    /// around its mouth, so the opening reads as the core's maw from the surface.</summary>
    private static void BuildAperture(IStructureCellSink sink, int ax, int az, int floorY, int surfaceY, BlockId shell, Func<int, int, int> ground)
    {
        int shaftZ = az + ShaftOffsetZ;
        for (int dy = floorY + 6; dy <= surfaceY + 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    sink.Put(ax + dx, dy, shaftZ + dz, BlockId.Air);
                }

        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (Math.Abs(dx) != 2 && Math.Abs(dz) != 2)
                {
                    continue;
                }

                int ry = ground(ax + dx, shaftZ + dz);
                sink.Put(ax + dx, ry + 1, shaftZ + dz, shell);
                sink.Put(ax + dx, ry + 2, shaftZ + dz, shell);
            }
    }
}
