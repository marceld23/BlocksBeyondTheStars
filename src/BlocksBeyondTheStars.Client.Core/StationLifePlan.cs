// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// The lively places: where the flight view puts the signs of life on a lived-in station's hull — worked out once per hull
/// from the cells the client already holds (no protocol change), so the per-frame part only blinks and turns:
/// <list type="bullet">
/// <item><see cref="NavLights"/> — up to four position lights on the hull's outermost cells: red on the −X side, green on
/// the +X side (port and starboard, as on a ship), white fore and aft (−Z / +Z). Each sits on the highest cell of its
/// side's outermost column, so it stands free.</item>
/// <item><see cref="DishCell"/> — the highest cell over the hull's middle, where a small dish turns.</item>
/// </list>
/// Deterministic from the cells alone, so every client shows the same picture. A hull too small to tell its sides apart
/// (under <see cref="MinSpan"/> blocks) gets nothing.
/// </summary>
public sealed class StationLifePlan
{
    /// <summary>The smallest hull (in blocks along X and along Z) that carries lights.</summary>
    public const int MinSpan = 6;

    /// <summary>How far (in blocks, along X and Z) from the box's centre column a cell still counts as "over the middle".</summary>
    private const double MiddleReach = 5.5;

    /// <summary>The sides, in the order of <see cref="NavLights"/>: 0 −X (red), 1 +X (green), 2 −Z (white), 3 +Z (white).</summary>
    public const int Port = 0, Starboard = 1, Fore = 2, Aft = 3;

    private StationLifePlan(List<(Vector3i Cell, int Side)> lights, Vector3i? dish)
    {
        NavLights = lights;
        DishCell = dish;
    }

    public IReadOnlyList<(Vector3i Cell, int Side)> NavLights { get; }

    public Vector3i? DishCell { get; }

    public static StationLifePlan Build(IReadOnlyDictionary<Vector3i, BlockId> cells)
    {
        var lights = new List<(Vector3i, int)>();
        var (minX, maxX, minZ, maxZ) = Footprint(cells);
        if (cells.Count == 0 || maxX - minX + 1 < MinSpan || maxZ - minZ + 1 < MinSpan)
        {
            return new StationLifePlan(lights, null);
        }

        double midX = (minX + maxX) / 2.0, midZ = (minZ + maxZ) / 2.0;
        var best = new Vector3i?[5];   // the four sides, then the dish
        var from = new double[5];
        foreach (var c in cells.Keys)
        {
            double offX = System.Math.Abs(c.X - midX), offZ = System.Math.Abs(c.Z - midZ);
            if (c.X == minX)
            {
                Consider(ref best[Port], ref from[Port], c, offZ);
            }

            if (c.X == maxX)
            {
                Consider(ref best[Starboard], ref from[Starboard], c, offZ);
            }

            if (c.Z == minZ)
            {
                Consider(ref best[Fore], ref from[Fore], c, offX);
            }

            if (c.Z == maxZ)
            {
                Consider(ref best[Aft], ref from[Aft], c, offX);
            }

            if (offX <= MiddleReach && offZ <= MiddleReach)
            {
                Consider(ref best[4], ref from[4], c, (offX * offX) + (offZ * offZ));
            }
        }

        for (int side = 0; side < 4; side++)
        {
            if (best[side] is { } cell)
            {
                lights.Add((cell, side));
            }
        }

        return new StationLifePlan(lights, best[4]);
    }

    /// <summary>The hull's extent in X and Z (an empty hull: an extent of nothing).</summary>
    private static (int MinX, int MaxX, int MinZ, int MaxZ) Footprint(IReadOnlyDictionary<Vector3i, BlockId> cells)
    {
        if (cells.Count == 0)
        {
            return (0, -1, 0, -1);
        }

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in cells.Keys)
        {
            minX = System.Math.Min(minX, c.X);
            maxX = System.Math.Max(maxX, c.X);
            minZ = System.Math.Min(minZ, c.Z);
            maxZ = System.Math.Max(maxZ, c.Z);
        }

        return (minX, maxX, minZ, maxZ);
    }

    /// <summary>Keeps the higher cell; at the same height the one nearer the middle (<paramref name="fromMiddle"/>), then the
    /// one with the smaller coordinates — never the dictionary's order.</summary>
    private static void Consider(ref Vector3i? current, ref double currentFrom, Vector3i c, double fromMiddle)
    {
        if (current is { } had)
        {
            if (c.Y < had.Y)
            {
                return;
            }

            bool smaller = c.X < had.X || (c.X == had.X && c.Z < had.Z);
            if (c.Y == had.Y && (fromMiddle > currentFrom || (fromMiddle == currentFrom && !smaller)))
            {
                return;
            }
        }

        current = c;
        currentFrom = fromMiddle;
    }
}
