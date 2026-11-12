// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The geometry of a buried vault (W-R3, #3522 moved it out of <c>GameServer.StampVault</c>): a 9×9 deepslate chamber
/// (7×7 inside, four high) sixteen below the surface, a 2×2 shaft from the surface into its ceiling corner, for the WELLHEAD
/// variant (#586) a 4×4 stone tube casing the shaft up through the water, a broken ring of granite pillars around the mouth
/// (the surface hint — each pillar stands on the ground of its own column) and three data caches in the chamber's corners;
/// then the loot spots of the chamber (<c>loot</c> ×2, <c>data_terminal</c>). The ring draws its gaps and heights from the
/// vault's shared random stream (the server's vault loop stream), so the caller hands it in and the loot draws follow.
/// <para>A crystal vault (#2260) adds its puzzle niche with <see cref="BuildCrystalPuzzle"/> after the loot.</para>
/// </summary>
public static class VaultBuilder
{
    /// <summary>How far below the surface the chamber's floor lies.</summary>
    public const int ChamberDepth = 16;

    /// <summary>The chamber's floor row for a vault whose surface (or seabed) is at <paramref name="surfaceY"/>.</summary>
    public static int FloorY(int surfaceY) => surfaceY - ChamberDepth;

    /// <summary>
    /// Emits one vault around the shaft column (<paramref name="ax"/>, <paramref name="az"/>): the chamber, the shaft, the well
    /// tube when <paramref name="waterTop"/> is set (the surface is then the seabed), the pillar ring (gaps and heights from
    /// <paramref name="rng"/>, each pillar on <paramref name="ground"/> of its column), the data caches and the loot spots.
    /// </summary>
    public static void Build(IStructureCellSink sink, GameContent content, int ax, int az, int surfaceY, int? waterTop, Random rng,
        Func<int, int, int> ground)
    {
        var shell = (content.GetBlock("deepslate") ?? content.GetBlock("stone"))!.NumericId;
        var pillar = (content.GetBlock("granite") ?? content.GetBlock("stone"))!.NumericId;
        var cache = content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;
        int floorY = FloorY(surfaceY);
        BuildChamberAndShaft(sink, ax, az, surfaceY, floorY, shell);
        if (waterTop is int top)
        {
            // Wellhead (#586): the shaft cased up through the water column as a 4×4 stone well tube with the 2×2 drop inside,
            // mouth one block above the water line — a diveable ancient well.
            for (int dy = surfaceY + 2; dy <= top + 1; dy++)
            {
                for (int dx = -2; dx <= 1; dx++)
                    for (int dz = -2; dz <= 1; dz++)
                    {
                        bool casing = dx is -2 or 1 || dz is -2 or 1;
                        sink.Put(ax + dx, dy, az + dz, casing ? shell : BlockId.Air);
                    }
            }
        }

        BuildPillarRing(sink, ax, az, pillar, rng, ground);

        // Treasure: data caches in the chamber corners + the loot spots (a salvage capsule ×2 and a data terminal).
        if (!cache.IsAir)
        {
            sink.Put(ax + 3, floorY, az + 3, cache);
            sink.Put(ax - 3, floorY, az + 3, cache);
            sink.Put(ax + 3, floorY, az - 3, cache);
        }

        sink.Mark("loot", ax - 2, floorY, az - 2);
        sink.Mark("loot", ax + 2, floorY, az - 3);
        sink.Mark("data_terminal", ax, floorY, az);
    }

    /// <summary>The chamber (a 9×9 outer shell, 7×7 inside, four air high) and the 2×2 drop from the surface into it.</summary>
    private static void BuildChamberAndShaft(IStructureCellSink sink, int ax, int az, int surfaceY, int floorY, BlockId shell)
    {
        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
                for (int dy = -1; dy <= 4; dy++)
                {
                    bool isShell = dx is -4 or 4 || dz is -4 or 4 || dy is -1 or 4;
                    sink.Put(ax + dx, floorY + dy, az + dz, isShell ? shell : BlockId.Air);
                }

        // The way in — bring a jetpack or dig steps back out.
        for (int dy = floorY + 4; dy <= surfaceY + 1; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    sink.Put(ax + dx, dy, az + dz, BlockId.Air);
                }
        }
    }

    /// <summary>The surface hint: a broken ring of weathered pillars (1–2 tall, some missing) around the shaft mouth.</summary>
    private static void BuildPillarRing(IStructureCellSink sink, int ax, int az, BlockId pillar, Random rng, Func<int, int, int> ground)
    {
        (int X, int Z)[] ring = { (3, 0), (2, 2), (0, 3), (-2, 2), (-3, 0), (-2, -2), (0, -3), (2, -2) };
        foreach (var (rx, rz) in ring)
        {
            if (rng.NextDouble() < 0.3)
            {
                continue; // a collapsed pillar — the ring reads ancient, not freshly built
            }

            int py = ground(ax + rx, az + rz);
            int h = 1 + rng.Next(2);
            for (int dy = 1; dy <= h; dy++)
            {
                sink.Put(ax + rx, py + dy, az + rz, pillar);
            }
        }
    }

    /// <summary>The cell of a crystal vault's niche floor (behind the north wall) — where the niche's loot cache stands.</summary>
    public static (int X, int Y, int Z) CrystalNiche(int ax, int az, int floorY) => (ax, floorY, az + 5);

    /// <summary>
    /// A crystal vault's puzzle (#2260) for a vault around (<paramref name="ax"/>, <paramref name="az"/>) with its floor at
    /// <paramref name="floorY"/>: the walled niche behind the north wall, the wire lamps, then the world-circuit cells — the
    /// two-high phase door, the AND gate above it (pointing down), the wires along the top of the north wall and down the side
    /// walls' corners to the two crystal switches — each cell followed by its device marker
    /// (<see cref="TemplateDevices.Marker"/>, data = its device settings). Last the niche's <c>loot</c> spot.
    /// </summary>
    public static void BuildCrystalPuzzle(IStructureCellSink sink, GameContent content, int ax, int az, int floorY)
    {
        var shell = (content.GetBlock("deepslate") ?? content.GetBlock("stone"))!.NumericId;
        var lamp = content.GetBlock("light_white")?.NumericId ?? BlockId.Air;
        int top = floorY + 3, north = az + 4;

        // The niche: two cells of air behind the north wall, walled in.
        for (int dy = 0; dy <= 1; dy++)
        {
            sink.Put(ax, floorY + dy, az + 5, BlockId.Air);
            sink.Put(ax - 1, floorY + dy, az + 5, shell);
            sink.Put(ax + 1, floorY + dy, az + 5, shell);
            sink.Put(ax, floorY + dy, az + 6, shell);
        }

        sink.Put(ax, floorY - 1, az + 5, shell);
        sink.Put(ax, floorY + 2, az + 5, shell);
        foreach (int side in new[] { -1, 1 })
        {
            sink.Put(ax + (side * 2), top - 1, north, lamp); // each wire's ON lamp (written even where the content lacks it)
        }

        foreach (var (x, y, z, block, data) in CrystalPuzzleDevices(content, ax, floorY, north))
        {
            if (!block.IsAir)
            {
                sink.Put(x, y, z, block);
                sink.Mark(TemplateDevices.Marker, x, y, z, data);
            }
        }

        var niche = CrystalNiche(ax, az, floorY);
        sink.Mark("loot", niche.X, niche.Y, niche.Z);
    }

    /// <summary>The puzzle's world-circuit cells in their stamping order: the door, the gate above it (pointing down: yaw 5
    /// rides in its settings), the wire between them, then per side (west, east) a wire along the top of the north wall, down
    /// the side wall's corner to a switch. A block the content lacks is air (the cell is left out).</summary>
    private static List<(int X, int Y, int Z, BlockId Block, string Data)> CrystalPuzzleDevices(GameContent content, int ax, int floorY, int north)
    {
        BlockId B(string key) => content.GetBlock(key)?.NumericId ?? BlockId.Air;
        var conduit = B("crystal_conduit");
        var phase = B("phase_block");
        var sw = B("crystal_switch");
        var gate = B("logic_block");
        int top = floorY + 3;
        string plain = TemplateDevices.Encode(0, string.Empty, string.Empty);
        var cells = new List<(int X, int Y, int Z, BlockId Block, string Data)>
        {
            (ax, floorY, north, phase, plain),
            (ax, floorY + 1, north, phase, plain),
            (ax, floorY + 2, north, conduit, plain),
            (ax, top, north, gate, TemplateDevices.Encode((int)LogicMode.And, "yaw=" + CrystalNetRules.YawDown, string.Empty)),
        };

        foreach (int side in new[] { -1, 1 })
        {
            for (int dx = 1; dx <= 4; dx++)
            {
                cells.Add((ax + (side * dx), top, north, conduit, plain));
            }

            cells.Add((ax + (side * 4), top, north - 1, conduit, plain));
            cells.Add((ax + (side * 4), top - 1, north - 1, conduit, plain));
            cells.Add((ax + (side * 4), floorY + 1, north - 1, sw, plain));
        }

        return cells;
    }
}
