// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The growth (#3585, #3587): the kit's buildings are placed one by one on the best free frontage along a road — the
/// module turned so its door (or its <c>entrance</c> marker) faces the road, set back one to <see cref="_spacing"/> columns
/// with a footpath from the door to the road, a gap of one column to every other building. Central functions want the
/// plaza, houses the middle distance, farms the edge.
/// </summary>
internal sealed partial class SettlementPlanBuilder
{
    /// <summary>The marker that names the road side of a module without a door (#3585).</summary>
    public const string EntranceMarker = "entrance";

    /// <summary>One way a building can stand: its footprint and the door's column on its front wall, after turning.</summary>
    private readonly struct Shape
    {
        public Shape(int w, int l, int doorX, int doorZ, int turns)
        {
            W = w;
            L = l;
            DoorX = doorX;
            DoorZ = doorZ;
            Turns = turns;
        }

        public int W { get; }

        public int L { get; }

        public int DoorX { get; }

        public int DoorZ { get; }

        public int Turns { get; }
    }

    private readonly Dictionary<(string Key, int Front), Shape> _shapes = new();

    private void GrowAll(List<StructureTemplate> modules)
    {
        for (int i = 0; i < modules.Count; i++)
        {
            var m = modules[i];
            int ring = StructureRoles.Ring(m.FunctionOrRole);
            bool placed = false;
            for (int attempt = 0; attempt < 4 && !placed; attempt++)
            {
                placed = TryPlace(front => ShapeOf(m, front), ring, $"{i}", m.Key, m.FunctionOrRole, SettlementPlan.LotColumn);
                if (!placed && !_grid)
                {
                    GrowLane();
                }
            }
        }
    }

    /// <summary>A few small squares (a well, a garden) on free frontages near the plaza — the empty lots of a lived-in place.</summary>
    private void AddSquares(int count)
    {
        for (int i = 0; i < count; i++)
        {
            TryPlace(front => SquareShape(front), -1, "square" + i, string.Empty, "square", SettlementPlan.Green);
        }
    }

    private static Shape SquareShape(int front) => front switch
    {
        0 => new Shape(5, 5, 2, 0, 0),
        1 => new Shape(5, 5, 2, 4, 0),
        2 => new Shape(5, 5, 0, 2, 0),
        _ => new Shape(5, 5, 4, 2, 0),
    };

    /// <summary>
    /// Places one building on the best frontage. <paramref name="ring"/> steers the score (−1 = a square near the plaza);
    /// the chosen lot's columns become <paramref name="fill"/>, its footpath <see cref="SettlementPlan.Footpath"/>.
    /// </summary>
    private bool TryPlace(System.Func<int, Shape> shapeFor, int ring, string tag, string module, string function, byte fill)
    {
        double bestScore = double.MaxValue;
        (int Ox, int Oz, int X, int Z, int Dir, int S, Shape Shape) best = default;
        for (int x = 0; x < _e; x++)
        {
            for (int z = 0; z < _e; z++)
            {
                if (!SettlementPlan.IsRoad(Plan.KindAt(x, z)))
                {
                    continue;
                }

                for (int dir = 0; dir < 4; dir++)
                {
                    var (dx, dz) = Direction(dir);
                    if (Plan.KindAt(x + dx, z + dz) != SettlementPlan.Unclaimed || !In(x + dx, z + dz))
                    {
                        continue;
                    }

                    // At least two columns back: a door's lane (the doorway and two rows in front of it) then lies on the
                    // house's own footpath, never on a road that may climb a block right there.
                    int s = 2 + StableHashing.Index($"setback:{x}:{z}:{dir}", _r.Seed, System.Math.Max(1, _spacing - 1));
                    var shape = shapeFor(dir ^ 1);
                    var (ox, oz) = LotOrigin(x, z, dir, s, shape);
                    if (!Fits(ox, oz, shape.W, shape.L, x, z, dx, dz, s))
                    {
                        continue;
                    }

                    double score = Score(ox, oz, shape, ring, dir, $"{tag}:{x}:{z}:{dir}");
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (ox, oz, x, z, dir, s, shape);
                    }
                }
            }
        }

        if (bestScore == double.MaxValue)
        {
            return false;
        }

        Claim(best.Ox, best.Oz, best.Shape, best.X, best.Z, best.Dir, best.S, fill);
        Plan.Lots.Add(new PlanLot
        {
            Module = module,
            Function = function,
            X = best.Ox,
            Z = best.Oz,
            Turns = best.Shape.Turns,
            W = best.Shape.W,
            L = best.Shape.L,
        });
        _frontage.Add((best.X, best.Z));
        return true;
    }

    /// <summary>The lot's west / north corner for a road column, a side and a setback: its front wall stands
    /// <paramref name="s"/> + 1 columns from the road, its door in line with the road column.</summary>
    private static (int Ox, int Oz) LotOrigin(int x, int z, int dir, int s, Shape shape) => dir switch
    {
        1 => (x - shape.DoorX, z + s + 1),
        0 => (x - shape.DoorX, z - s - shape.L),
        3 => (x + s + 1, z - shape.DoorZ),
        _ => (x - s - shape.W, z - shape.DoorZ),
    };

    /// <summary>Whether a lot fits: inside the box with a column to spare, its footprint and a ring of one column around it
    /// unclaimed, its footpath free.</summary>
    private bool Fits(int ox, int oz, int w, int l, int x, int z, int dx, int dz, int s)
    {
        if (ox < 1 || oz < 1 || ox + w > _e - 1 || oz + l > _e - 1)
        {
            return false;
        }

        for (int k = 1; k <= s; k++)
        {
            int fx = x + (dx * k), fz = z + (dz * k);
            if (Plan.KindAt(fx, fz) != SettlementPlan.Unclaimed
                || SettlementPlan.IsWalkable(Plan.KindAt(fx + dz, fz + dx)) || SettlementPlan.IsWalkable(Plan.KindAt(fx - dz, fz - dx))
                || (k > 1 && SettlementPlan.IsWalkable(Plan.KindAt(fx + dx, fz + dz))))
            {
                return false; // a footpath touches no other way: it takes its lot's height, which may not be that way's
            }
        }

        for (int cx = ox - 1; cx <= ox + w; cx++)
        {
            for (int cz = oz - 1; cz <= oz + l; cz++)
            {
                if (Plan.KindAt(cx, cz) != SettlementPlan.Unclaimed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void Claim(int ox, int oz, Shape shape, int x, int z, int dir, int s, byte fill)
    {
        for (int cx = ox; cx < ox + shape.W; cx++)
        {
            for (int cz = oz; cz < oz + shape.L; cz++)
            {
                Plan.SetKind(cx, cz, fill);
            }
        }

        var (dx, dz) = Direction(dir);
        for (int k = 1; k <= s; k++)
        {
            Plan.SetKind(x + (dx * k), z + (dz * k), SettlementPlan.Footpath);
        }
    }

    /// <summary>How well a lot suits its function — lower is better: the plaza's functions as close as they can, the farms
    /// toward the edge, houses at a middle distance; a hash of the candidate keeps two equal spots apart. The grown grid
    /// keeps its edge thin; the bank village keeps its houses off the water side.</summary>
    private double Score(int ox, int oz, Shape shape, int ring, int dir, string key)
    {
        double cx = ox + (shape.W / 2.0), cz = oz + (shape.L / 2.0);
        double r = System.Math.Sqrt(((cx - _px) * (cx - _px)) + ((cz - _pz) * (cz - _pz))) / (_e / 2.0);
        double score = ring switch
        {
            0 => r * 3.0,
            2 => (1.0 - r) * 2.5,
            -1 => System.Math.Abs(r - 0.25) * 2.0,
            _ => System.Math.Abs(r - (_grid ? 0.4 : 0.35)) * 2.0,
        };

        score += StableHashing.Unit("lot:" + key, _r.Seed) * 0.4;
        int edge = System.Math.Min(System.Math.Min(ox, oz), System.Math.Min(_e - (ox + shape.W), _e - (oz + shape.L)));
        if (_grid && ring == 1 && edge < 6)
        {
            score += 0.6;
        }

        if (Plan.Form == "bank" && dir == _r.WaterSide)
        {
            score += 1.5;
        }

        return score;
    }

    /// <summary>A new lane when the frontages are full (a grown village grows a way out to new plots): from a random road
    /// column with free land beside it, outward, a fifth of the box long.</summary>
    private void GrowLane()
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int x = 2 + _rng.Next(_e - 4), z = 2 + _rng.Next(_e - 4), dir = _rng.Next(4);
            var (dx, dz) = Direction(dir);
            if (!SettlementPlan.IsRoad(Plan.KindAt(x, z)) || Plan.KindAt(x + dx, z + dz) != SettlementPlan.Unclaimed
                || Plan.KindAt(x + (2 * dx), z + (2 * dz)) != SettlementPlan.Unclaimed)
            {
                continue;
            }

            Walk(x + dx, z + dz, dx, dz, (_e / 5) + _rng.Next(_e / 8 + 1), 0.15, 1, SettlementPlan.Lane, toEdge: false);
            return;
        }
    }
}
