// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Picks the halls of a listed rail line all at once (#4014). Each city of the line offers its halls
/// (<see cref="MetroRailHalls.Candidates"/>); the way between two cities depends on BOTH halls — where the one's street run
/// leaves its city and where the other's begins — so choosing city by city (each looking at its neighbour's centre) left
/// legs that ran back over a city. The chain is a shortest path through the cities' candidates (Viterbi): a hall's own cost
/// (its street runs) plus each leg's cost from one city's chosen hall to the next one's; a leg that cannot be routed costs
/// <see cref="double.PositiveInfinity"/> and is never taken. Pure and deterministic (ties keep the earlier candidate).
/// </summary>
public static class ListedRailHallChain
{
    /// <summary>The chosen candidate per city, or null when no chain of finite cost exists. <paramref name="counts"/> is the
    /// number of candidates per city, <paramref name="hallCost"/>(city, candidate) a hall's own cost and
    /// <paramref name="legCost"/>(city, from, to) the cost of the leg from candidate <c>from</c> of that city to candidate
    /// <c>to</c> of the next one.</summary>
    public static int[]? Choose(IReadOnlyList<int> counts, Func<int, int, double> hallCost, Func<int, int, int, double> legCost)
    {
        int n = counts.Count;
        if (n == 0)
        {
            return null;
        }

        var best = new double[n][];
        var from = new int[n][];
        for (int k = 0; k < n; k++)
        {
            best[k] = new double[counts[k]];
            from[k] = new int[counts[k]];
            for (int j = 0; j < counts[k]; j++)
            {
                double own = hallCost(k, j);
                best[k][j] = k == 0 ? own : double.PositiveInfinity;
                from[k][j] = -1;
                for (int i = 0; k > 0 && i < counts[k - 1]; i++)
                {
                    if (double.IsPositiveInfinity(best[k - 1][i]))
                    {
                        continue;
                    }

                    double cost = best[k - 1][i] + legCost(k - 1, i, j) + own;
                    if (cost < best[k][j])
                    {
                        best[k][j] = cost;
                        from[k][j] = i;
                    }
                }
            }
        }

        return Backtrack(best, from);
    }

    private static int[]? Backtrack(double[][] best, int[][] from)
    {
        int n = best.Length;
        int last = -1;
        for (int j = 0; j < best[n - 1].Length; j++)
        {
            if (!double.IsPositiveInfinity(best[n - 1][j]) && (last < 0 || best[n - 1][j] < best[n - 1][last]))
            {
                last = j;
            }
        }

        if (last < 0)
        {
            return null;
        }

        var pick = new int[n];
        pick[n - 1] = last;
        for (int k = n - 1; k > 0; k--)
        {
            pick[k - 1] = from[k][pick[k]];
        }

        return pick;
    }
}
