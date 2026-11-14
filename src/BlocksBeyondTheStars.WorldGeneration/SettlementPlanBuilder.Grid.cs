// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The grown grid (#3586): an ordered settlement that looks lived in. Lanes along one axis with uneven strides (rows of
/// houses on both sides), cross lanes further apart, one wider main lane through the middle that leaves the box at both
/// ends, the plaza where it meets the middle cross lane. The growth then fills the frontages from the plaza outward, so the
/// edge stays thin.
/// </summary>
internal sealed partial class SettlementPlanBuilder
{
    private void GridSkeleton()
    {
        int c = _e / 2;
        bool alongX = _rng.Next(2) == 0;
        var rows = Strides(c, 18, 23);
        var cols = Strides(c, 22, 31);
        int lo = rows[0], hi = rows[rows.Count - 1];
        foreach (int r in rows)
        {
            bool main = r == c;
            int from = main ? 0 : 2, to = main ? _e - 1 : _e - 3;
            for (int a = from; a <= to; a++)
            {
                for (int k = 0; k < (main ? _mainWidth : 1); k++)
                {
                    PaintAxis(alongX, a, r + k, main ? SettlementPlan.MainRoad : SettlementPlan.Lane);
                }
            }
        }

        foreach (int col in cols)
        {
            bool middle = col == c;
            int from = middle ? 0 : lo, to = middle ? _e - 1 : hi;
            for (int b = from; b <= to; b++)
            {
                PaintAxis(alongX, col, b, SettlementPlan.Lane);
            }
        }

        PaintPlaza(c, c, PlazaSize);
    }

    /// <summary>Paints the column at <paramref name="along"/> on the grid's main axis and <paramref name="across"/> on the other.</summary>
    private void PaintAxis(bool alongX, int along, int across, byte kind)
        => Paint(alongX ? along : across, alongX ? across : along, kind);

    /// <summary>Lane positions from the centre outward, each stride drawn from [<paramref name="min"/>, <paramref name="max"/>],
    /// kept three columns inside the box; ascending.</summary>
    private List<int> Strides(int centre, int min, int max)
    {
        var list = new List<int> { centre };
        for (int p = centre - min - _rng.Next(max - min + 1); p >= 3; p -= min + _rng.Next(max - min + 1))
        {
            list.Add(p);
        }

        for (int p = centre + min + _rng.Next(max - min + 1); p <= _e - 4; p += min + _rng.Next(max - min + 1))
        {
            list.Add(p);
        }

        list.Sort();
        return list;
    }
}
