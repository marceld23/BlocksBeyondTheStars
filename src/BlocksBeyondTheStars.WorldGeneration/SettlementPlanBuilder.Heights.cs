// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The heights that follow the land (#3590). The roads take the ground of the site, smoothed so two neighbouring road
/// columns never differ by more than one block (the lower envelope of the ground along the road network), held level at
/// the plaza and within <see cref="SettlementPlanner.MaxRise"/> of it; every building takes the height of the road column in
/// front of its door, its footpath and yard with it. A kit marked flat, and a plan without ground, stays on one level.
/// </summary>
internal sealed partial class SettlementPlanBuilder
{
    private const int Far = 1 << 20;

    private void AssignHeights()
    {
        if (_r.Ground is not { } ground || _r.Kit.Flat)
        {
            return; // every height stays 0
        }

        int g0 = ground(_px, _pz);
        var h = RoadHeights(ground, g0);
        int min = int.MaxValue;
        foreach (int v in h)
        {
            if (v != Far && v < min)
            {
                min = v;
            }
        }

        if (min == int.MaxValue)
        {
            return; // no road at all — nothing to follow
        }

        Plan.GroundBase = g0 + min;

        for (int x = 0; x < _e; x++)
        {
            for (int z = 0; z < _e; z++)
            {
                int v = h[(x * _e) + z];
                if (v != Far)
                {
                    Plan.SetHeight(x, z, v - min);
                }
            }
        }

        for (int i = 0; i < Plan.Lots.Count; i++)
        {
            var lot = Plan.Lots[i];
            var (fx, fz) = _frontage[i];
            lot.Y = Plan.HeightAt(fx, fz);
            SetRect(lot.X, lot.Z, lot.W, lot.L, lot.Y);
            WalkFootpath(fx, fz, lot.Y);
        }
    }

    /// <summary>The height of every road column (relative, may be negative; <see cref="Far"/> for any other column).</summary>
    private int[] RoadHeights(System.Func<int, int, int> ground, int g0)
    {
        int n = _e * _e;
        var h = new int[n];
        var plazaDist = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            bool node = SettlementPlan.IsRoad(Plan.Kinds[i]) || _level[i];
            h[i] = node ? ground(i / _e, i % _e) - g0 : Far;
            plazaDist[i] = Far;
            if (node && _level[i])
            {
                plazaDist[i] = 0;
                queue.Enqueue(i);
            }
        }

        Spread(queue, plazaDist, h);
        Envelope(h);
        int half = SettlementPlanner.MaxRise / 2;
        for (int i = 0; i < n; i++)
        {
            if (h[i] == Far)
            {
                continue;
            }

            int d = plazaDist[i] == Far ? half : plazaDist[i];
            h[i] = _level[i] ? 0 : System.Math.Clamp(h[i], -System.Math.Min(d, half), System.Math.Min(d, half));
        }

        return h;
    }

    /// <summary>The road-network distance from the level columns (a breadth-first walk over the road columns).</summary>
    private void Spread(Queue<int> queue, int[] dist, int[] h)
    {
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int j in Neighbours(i))
            {
                if (h[j] != Far && dist[j] == Far)
                {
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
        }
    }

    /// <summary>Lowers every road column to at most one block above its lowest road neighbour, until nothing changes — the
    /// lower envelope, so the road never climbs or drops more than one block per column.</summary>
    private void Envelope(int[] h)
    {
        bool changed = true;
        for (int pass = 0; changed && pass < 4 * _e; pass++)
        {
            changed = false;
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i] == Far)
                {
                    continue;
                }

                foreach (int j in Neighbours(i))
                {
                    if (h[j] != Far && h[j] + 1 < h[i])
                    {
                        h[i] = h[j] + 1;
                        changed = true;
                    }
                }
            }
        }
    }

    private IEnumerable<int> Neighbours(int i)
    {
        int x = i / _e, z = i % _e;
        if (x > 0) yield return i - _e;
        if (x < _e - 1) yield return i + _e;
        if (z > 0) yield return i - 1;
        if (z < _e - 1) yield return i + 1;
    }

    private void SetRect(int ox, int oz, int w, int l, int y)
    {
        for (int x = ox; x < ox + w; x++)
        {
            for (int z = oz; z < oz + l; z++)
            {
                Plan.SetHeight(x, z, y);
            }
        }
    }

    /// <summary>The footpath off a road column takes the height of the lot it leads to.</summary>
    private void WalkFootpath(int fx, int fz, int y)
    {
        for (int dir = 0; dir < 4; dir++)
        {
            var (dx, dz) = Direction(dir);
            for (int k = 1; Plan.KindAt(fx + (dx * k), fz + (dz * k)) == SettlementPlan.Footpath; k++)
            {
                Plan.SetHeight(fx + (dx * k), fz + (dz * k), y);
            }
        }
    }

    /// <summary>A yard of one column round every building (claimed, level with it), where nothing else stands.</summary>
    private void ClaimYards()
    {
        foreach (var lot in Plan.Lots)
        {
            for (int x = lot.X - 1; x <= lot.X + lot.W; x++)
            {
                for (int z = lot.Z - 1; z <= lot.Z + lot.L; z++)
                {
                    if (Plan.InBox(x, z) && Plan.KindAt(x, z) == SettlementPlan.Unclaimed)
                    {
                        Plan.Set(x, z, SettlementPlan.Yard, lot.Y);
                    }
                }
            }
        }
    }
}
