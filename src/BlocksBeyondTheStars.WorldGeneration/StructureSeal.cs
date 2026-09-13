// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The airtight check for station modules (#1873): the air the module USES — every marker cell and the cell
/// behind every port — must not reach the outside. A flood fill through 6-connected air from those seeds that
/// arrives at any air cell on the bounding box's faces has found a leak; those boundary cells are returned so
/// the editor can blink them red. Every block seals (glass, force fields and doors' wall cells included);
/// air outside a sealed hull (between antennae, say) is never visited, so greebles are fine.
/// </summary>
public static class StructureSeal
{
    /// <summary>The leak cells, empty when the module is airtight (or has nothing to seal: no markers, no ports).</summary>
    public static List<Vector3i> FindLeaks(StructureTemplate t)
    {
        var leaks = new List<Vector3i>();
        if (t.Width <= 0 || t.Height <= 0 || t.Length <= 0)
        {
            return leaks;
        }

        var solid = new HashSet<Vector3i>();
        var seeds = new List<Vector3i>();
        foreach (var c in t.Cells)
        {
            var p = new Vector3i(c.X, c.Y, c.Z);
            if (c.Kind == "block")
            {
                solid.Add(p);
            }
            else
            {
                seeds.Add(p);
            }
        }

        foreach (var port in StructurePorts.Collect(t))
        {
            var inward = port.Outward * -1;
            foreach (var c in port.Cells)
            {
                seeds.Add(c + inward);
            }
        }

        bool Inside(Vector3i p) => p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < t.Width && p.Y < t.Height && p.Z < t.Length;
        bool Boundary(Vector3i p) => p.X == 0 || p.Y == 0 || p.Z == 0 || p.X == t.Width - 1 || p.Y == t.Height - 1 || p.Z == t.Length - 1;

        var seen = new HashSet<Vector3i>();
        var queue = new Queue<Vector3i>();
        foreach (var s in seeds)
        {
            if (Inside(s) && !solid.Contains(s) && seen.Add(s))
            {
                queue.Enqueue(s);
            }
        }

        var leakSet = new HashSet<Vector3i>();
        var steps = new[]
        {
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0),
            new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1),
        };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (Boundary(p))
            {
                leakSet.Add(p);
                continue; // report the boundary cell; no need to walk out into the void
            }

            foreach (var d in steps)
            {
                var n = p + d;
                if (Inside(n) && !solid.Contains(n) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        leaks.AddRange(leakSet);
        leaks.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
        return leaks;
    }
}
