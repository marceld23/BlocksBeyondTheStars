// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The rules that read a planned settlement's column map instead of its box (#3592, #3594): the protected volume and "above
/// the floor" follow each claimed column's own floor (an unclaimed corner of the box is free ground), the wild animals' town
/// zone is the claimed columns grown by its margin, the festival gathers on the plaza marker and the intercity station docks
/// where a road leaves the box. A settlement without a map (every box-built one) answers exactly as before.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The world height of the floor under a cell of a settlement's box: the column's own floor on a planned
    /// settlement, <see cref="int.MaxValue"/> for a column it does not claim, the ground row everywhere else.</summary>
    private int SettlementFloorY(SettlementInstance s, Vector3i pos)
    {
        if (s.Layout is not { HasColumnMap: true } layout)
        {
            return s.GroundY;
        }

        int lx = WorldConstants.WrapDeltaX(pos.X - s.Min.X, _world.Circumference);
        int f = layout.FloorAt(lx, pos.Z - s.Min.Z);
        return f < 0 ? int.MaxValue : s.GroundY + f;
    }

    /// <summary>The town-zone depth of a planned settlement at a feet cell (#3592): 0 outside its claimed columns grown by
    /// <see cref="TownWildMargin"/>, inside it 1 + the columns to the zone's nearest edge.</summary>
    private int PlannedZoneDepth(SettlementInstance s, int x, int feetY, int z, int circ)
    {
        int m = TownWildMargin;
        var layout = s.Layout!;
        if (feetY < s.GroundY - m || feetY > s.Max.Y + m)
        {
            return 0;
        }

        int w = layout.Width + (2 * m), l = layout.Length + (2 * m);
        int lx = WorldConstants.WrapDeltaX(x - (s.Min.X - m), circ), lz = z - (s.Min.Z - m);
        if (lx < 0 || lx >= w || lz < 0 || lz >= l)
        {
            return 0;
        }

        s.TownDepth ??= TownDepthMap(layout, m);
        return s.TownDepth[(lx * l) + lz];
    }

    /// <summary>The zone depth over the box grown by <paramref name="m"/>: the claimed columns grown by <paramref name="m"/>
    /// (any column within <paramref name="m"/> of a claimed one), each 1 + its four-way distance to the zone's outside.</summary>
    internal static byte[] TownDepthMap(SettlementStructure layout, int m)
    {
        int w = layout.Width + (2 * m), l = layout.Length + (2 * m);
        var depth = new byte[w * l];
        var queue = new Queue<int>();
        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < l; z++)
            {
                if (!NearClaimed(layout, x - m, z - m, m))
                {
                    continue;
                }

                bool rim = x == 0 || z == 0 || x == w - 1 || z == l - 1
                    || !NearClaimed(layout, x - m - 1, z - m, m) || !NearClaimed(layout, x - m + 1, z - m, m)
                    || !NearClaimed(layout, x - m, z - m - 1, m) || !NearClaimed(layout, x - m, z - m + 1, m);
                depth[(x * l) + z] = rim ? (byte)1 : byte.MaxValue;
                if (rim)
                {
                    queue.Enqueue((x * l) + z);
                }
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i / l, z = i % l;
            foreach (int j in new[] { x > 0 ? i - l : -1, x < w - 1 ? i + l : -1, z > 0 ? i - 1 : -1, z < l - 1 ? i + 1 : -1 })
            {
                if (j >= 0 && depth[j] == byte.MaxValue)
                {
                    depth[j] = (byte)System.Math.Min(250, depth[i] + 1);
                    queue.Enqueue(j);
                }
            }
        }

        return depth;
    }

    /// <summary>A settlement's plaza marker (#3594), feet level, or null when it has none.</summary>
    private static Vector3f? PlazaMarkerOf(SettlementInstance s)
    {
        foreach (var (type, pos) in s.Markers)
        {
            if (type == SettlementPlanBaker.PlazaMarker)
            {
                return new Vector3f(pos.X, (float)System.Math.Floor(pos.Y), pos.Z);
            }
        }

        return null;
    }

    /// <summary>The road exit on the side of the box a station of <paramref name="heading"/> stands on (#3594), the one
    /// nearest the partner town: its world column and its floor height; null when the settlement has none there.</summary>
    private (int X, int Z, int FloorY)? RoadExitFor(SettlementInstance s, int heading, double partnerX, double partnerZ)
    {
        string side = "side=" + heading switch { 0 => 3, 1 => 2, 2 => 1, _ => 0 };
        (int X, int Z, int FloorY)? best = null;
        double bestD = double.MaxValue;
        foreach (var (type, pos, data) in s.DataMarkers)
        {
            if (type != SettlementPlanBaker.RoadExitMarker || data != side)
            {
                continue;
            }

            double d = System.Math.Abs(pos.X - partnerX) + System.Math.Abs(pos.Z - partnerZ);
            if (d < bestD)
            {
                bestD = d;
                best = ((int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z), (int)System.Math.Floor(pos.Y) - 1);
            }
        }

        return best;
    }
}
