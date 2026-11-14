// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The road skeletons of the village forms (#3587): street, green, round, cluster, crossroads and bank village.</summary>
internal sealed partial class SettlementPlanBuilder
{
    /// <summary>Lays the skeleton of <paramref name="form"/>.</summary>
    private void FormSkeleton(string form)
    {
        switch (form)
        {
            case "green": GreenVillage(); break;
            case "round": RoundVillage(); break;
            case "cluster": ClusterVillage(); break;
            case "cross": CrossVillage(); break;
            case "bank": BankVillage(); break;
            default: StreetVillage(); break;
        }
    }

    /// <summary>
    /// Walks a road from (<paramref name="x"/>, <paramref name="z"/>) along (<paramref name="dx"/>, <paramref name="dz"/>) for
    /// at most <paramref name="length"/> steps, <paramref name="width"/> wide, now and then stepping one column aside (a gentle
    /// bend, never twice within four steps). <paramref name="toEdge"/> runs it out of the box (a way out of the village);
    /// otherwise it stops two columns short of the edge. Returns the columns of its centre line.
    /// </summary>
    private List<(int X, int Z)> Walk(int x, int z, int dx, int dz, int length, double bend, int width, byte kind, bool toEdge)
    {
        var line = new List<(int X, int Z)>();
        int px = dz != 0 ? 1 : 0, pz = dx != 0 ? 1 : 0; // the side the extra width goes to
        int sinceBend = 0, margin = toEdge ? 0 : 2;
        for (int step = 0; step < length; step++)
        {
            if (x < margin || z < margin || x >= _e - margin || z >= _e - margin || BlockedWide(x, z, px, pz, width))
            {
                break; // the box's edge, or a building (a lane grown later stops before it)
            }

            PaintWide(x, z, px, pz, width, kind);
            line.Add((x, z));
            if (++sinceBend >= 4 && _rng.NextDouble() < bend)
            {
                int side = _rng.Next(2) == 0 ? -1 : 1;
                int nx = x + (px * side), nz = z + (pz * side);
                if (nx >= 3 && nz >= 3 && nx < _e - 3 - width && nz < _e - 3 - width && !BlockedWide(nx, nz, px, pz, width))
                {
                    x = nx;
                    z = nz;
                    PaintWide(x, z, px, pz, width, kind);
                    sinceBend = 0;
                }
            }

            x += dx;
            z += dz;
        }

        return line;
    }

    /// <summary>Whether a road column (or its width) would touch a building or a footpath.</summary>
    private bool BlockedWide(int x, int z, int px, int pz, int width)
    {
        for (int k = 0; k < width; k++)
        {
            int cx = x + (px * k), cz = z + (pz * k);
            foreach (var (nx, nz) in new[] { (cx, cz), (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                if (Plan.KindAt(nx, nz) is SettlementPlan.LotColumn or SettlementPlan.Footpath)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void PaintWide(int x, int z, int px, int pz, int width, byte kind)
    {
        for (int k = 0; k < width; k++)
        {
            Paint(x + (px * k), z + (pz * k), kind);
        }
    }

    /// <summary>A lane branching off a road at a right angle: from a random column of <paramref name="from"/> (away from the
    /// plaza), to a random side, a quarter to a third of the box long.</summary>
    private void Branch(List<(int X, int Z)> from, int dx, int dz, double bend)
    {
        if (from.Count < 8)
        {
            return;
        }

        for (int attempt = 0; attempt < 6; attempt++)
        {
            var (x, z) = from[_rng.Next(from.Count)];
            if (System.Math.Abs(x - _px) + System.Math.Abs(z - _pz) < PlazaSize + 3)
            {
                continue;
            }

            int side = _rng.Next(2) == 0 ? -1 : 1;
            int bx = dz != 0 ? side : 0, bz = dx != 0 ? side : 0;
            Walk(x + bx, z + bz, bx, bz, (_e / 4) + _rng.Next(_e / 10 + 1), bend, 1, SettlementPlan.Lane, toEdge: false);
            return;
        }
    }

    /// <summary>How many side lanes a form adds by tier.</summary>
    private int SideLanes => TierRank switch { 0 => 0, 1 => 1 + _rng.Next(2), 2 => 2 + _rng.Next(2), _ => 3 + _rng.Next(2) };

    /// <summary>The street village: one main road through the box with gentle bends, the plaza on it, lanes off it.</summary>
    private void StreetVillage()
    {
        bool alongX = _rng.Next(2) == 0; // drawn whatever the request says, so the stream stays the same
        alongX = _r.Axis == "x" || (_r.Axis != "z" && alongX);
        int c = _e / 2;
        double bend = _r.Straight ? 0.0 : 0.08;
        var main = alongX
            ? Walk(0, c, 1, 0, _e, bend, _mainWidth, SettlementPlan.MainRoad, toEdge: true)
            : Walk(c, 0, 0, 1, _e, bend, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        var (mx, mz) = Nearest(main, c, c);
        PaintPlaza(mx, mz, PlazaSize);
        for (int i = SideLanes; i > 0; i--)
        {
            Branch(main, alongX ? 1 : 0, alongX ? 0 : 1, 0.12);
        }
    }

    /// <summary>The crossroads village: two main roads crossing at the plaza.</summary>
    private void CrossVillage()
    {
        int c = _e / 2;
        var a = Walk(0, c, 1, 0, _e, 0.06, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        var b = Walk(c, 0, 0, 1, _e, 0.06, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        PaintPlaza(c, c, PlazaSize);
        for (int i = SideLanes / 2; i > 0; i--)
        {
            Branch(a, 1, 0, 0.12);
            Branch(b, 0, 1, 0.12);
        }
    }

    /// <summary>The bank village: a road along the side the fresh water lies beyond, a quarter in from it; lanes lead away
    /// from the water (the growth keeps the houses on the land side).</summary>
    private void BankVillage()
    {
        int side = _r.WaterSide, q = _e / 4;
        bool alongX = side is 0 or 1;
        int line = side is 0 or 2 ? q : _e - 1 - q;
        var road = alongX
            ? Walk(0, line, 1, 0, _e, 0.06, _mainWidth, SettlementPlan.MainRoad, toEdge: true)
            : Walk(line, 0, 0, 1, _e, 0.06, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        var (mx, mz) = Nearest(road, _e / 2, _e / 2);
        PaintPlaza(mx, mz, PlazaSize);
        int away = side is 0 or 2 ? 1 : -1;
        for (int i = SideLanes; i > 0; i--)
        {
            if (road.Count < 8)
            {
                break;
            }

            var (x, z) = road[_rng.Next(road.Count)];
            if (System.Math.Abs(x - _px) + System.Math.Abs(z - _pz) >= PlazaSize + 3)
            {
                Walk(x + (alongX ? 0 : away), z + (alongX ? away : 0), alongX ? 0 : away, alongX ? away : 0,
                    (_e / 3) + _rng.Next(_e / 8 + 1), 0.12, 1, SettlementPlan.Lane, toEdge: false);
            }
        }
    }

    /// <summary>The column of <paramref name="line"/> nearest to (<paramref name="x"/>, <paramref name="z"/>).</summary>
    private static (int X, int Z) Nearest(List<(int X, int Z)> line, int x, int z)
    {
        var best = line.Count > 0 ? line[0] : (x, z);
        int bestD = int.MaxValue;
        foreach (var p in line)
        {
            int d = System.Math.Abs(p.X - x) + System.Math.Abs(p.Z - z);
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }

        return best;
    }
}
