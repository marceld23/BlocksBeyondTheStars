// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The village forms with a centre of their own (#3587): the green village, the round village and the cluster.</summary>
internal sealed partial class SettlementPlanBuilder
{
    /// <summary>
    /// The green village: the road comes in from one side, splits around an elongated green in the middle and joins again on
    /// the far side. The green (grass, trees, a pond) is the village's meeting place, level with itself; the houses face it
    /// across the two roads.
    /// </summary>
    private void GreenVillage()
    {
        int c = _e / 2;
        int half = System.Math.Max(6, _e / 6) + _rng.Next(3);          // half the green's length
        int gw = 3 + _rng.Next(2) + (TierRank >= 2 ? 1 : 0);            // half the green's width
        int x0 = c - half, x1 = c + half, zN = c - gw - 1, zS = c + gw + 1;
        Walk(0, c, 1, 0, x0 + 1, 0.05, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        Walk(x1, c, 1, 0, _e, 0.05, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        for (int x = x0; x <= x1; x++)
        {
            Paint(x, zN, SettlementPlan.Lane);
            Paint(x, zS, SettlementPlan.Lane);
        }

        for (int z = zN; z <= zS; z++)
        {
            Paint(x0, z, SettlementPlan.Lane);
            Paint(x1, z, SettlementPlan.Lane);
        }

        for (int x = x0 + 1; x < x1; x++)
        {
            for (int z = zN + 1; z < zS; z++)
            {
                Plan.SetKind(x, z, SettlementPlan.Green);
                _level[(x * _e) + z] = true;
            }
        }

        _px = c;
        _pz = c;
        for (int i = SideLanes / 2; i > 0; i--)
        {
            int bx = x0 + 2 + _rng.Next(System.Math.Max(1, x1 - x0 - 3));
            bool north = _rng.Next(2) == 0;
            Walk(bx, north ? zN - 1 : zS + 1, 0, north ? -1 : 1, (_e / 4) + _rng.Next(_e / 10 + 1), 0.12, 1, SettlementPlan.Lane, toEdge: false);
        }
    }

    /// <summary>
    /// The round village: one way in from the edge, a round plaza in the middle and a ring lane around it; the houses stand
    /// round the ring like the spokes of a wheel, their doors toward the middle. Larger tiers add a few lanes outward.
    /// </summary>
    private void RoundVillage()
    {
        int c = _e / 2;
        int r = TierRank switch { 0 => 3, 1 => 3, 2 => 4, _ => 5 };
        for (int x = c - r - 3; x <= c + r + 3; x++)
        {
            for (int z = c - r - 3; z <= c + r + 3; z++)
            {
                double d = System.Math.Sqrt(((x - c) * (x - c)) + ((z - c) * (z - c)));
                if (d <= r + 0.3)
                {
                    Plan.SetKind(x, z, SettlementPlan.Plaza);
                    _level[(x * _e) + z] = true;
                }
                else if (d <= r + 1.8)
                {
                    Paint(x, z, SettlementPlan.Lane);
                }
            }
        }

        _px = c;
        _pz = c;
        int side = _rng.Next(4);
        var (dx, dz) = Direction(side);
        Walk(c + (dx * (r + 2)), c + (dz * (r + 2)), dx, dz, _e, 0.08, _mainWidth, SettlementPlan.MainRoad, toEdge: true);
        for (int i = TierRank - 1; i > 0; i--)
        {
            var (ox, oz) = Direction((side + i) % 4);
            Walk(c + (ox * (r + 2)), c + (oz * (r + 2)), ox, oz, (_e / 3) + _rng.Next(_e / 8 + 1), 0.15, 1, SettlementPlan.Lane, toEdge: false);
        }
    }

    /// <summary>
    /// The cluster: lanes wander off the plaza in several directions and fork, the one way out runs to the edge — a village
    /// that grew house by house without a plan.
    /// </summary>
    private void ClusterVillage()
    {
        int c = _e / 2;
        PaintPlaza(c, c, PlazaSize);
        int lanes = TierRank switch { 0 => 2, 1 => 3, 2 => 4, _ => 5 };
        int first = _rng.Next(4);
        for (int i = 0; i < lanes; i++)
        {
            var (dx, dz) = Direction((first + i) % 4);
            int start = (PlazaSize / 2) + 1;
            bool main = i == 0;
            var line = Walk(c + (dx * start), c + (dz * start), dx, dz, main ? _e : (_e / 4) + _rng.Next(_e / 6 + 1), 0.25,
                main ? _mainWidth : 1, main ? SettlementPlan.MainRoad : SettlementPlan.Lane, toEdge: main);
            if (_rng.NextDouble() < 0.6)
            {
                Branch(line, dx, dz, 0.25);
            }
        }
    }

    /// <summary>The unit step toward a box side: 0 −Z, 1 +Z, 2 −X, 3 +X.</summary>
    private static (int Dx, int Dz) Direction(int side) => side switch
    {
        0 => (0, -1),
        1 => (0, 1),
        2 => (-1, 0),
        _ => (1, 0),
    };
}
