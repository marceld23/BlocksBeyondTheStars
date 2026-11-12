// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Revision 2 of the pass (the school club's landmarks, terrain generation 39): the lattice BREAKS. The collapse balls of a
/// lattice rarely cut a member loose — a frame hangs together through every deck and brace — so a lattice tower or bridge came
/// out of revision 1 almost whole. A break is a thin plane (two or three cells) cut clean through the frame; the settle then
/// brings down whatever no longer hangs on the ground or a kept cell:
/// <list type="bullet">
/// <item><b>Tall</b> (at least <see cref="TallRatio"/> times as high above the ground row as its longest side): a horizontal
/// plane, gently tilted, at 25–65 % of the height — the top of the tower falls.</item>
/// <item><b>Long</b> (its longest side at least <see cref="LongRatio"/> times its height and three times its other side): a
/// pair of vertical planes across the long axis that cut a section out of an unsupported run (slices with no block on the
/// ground row, a span between two pylons — the longer the run, the likelier it breaks) — the section falls into the gap.</item>
/// <item>Anything else (a compact frame) has no breaks.</item>
/// </list>
/// One to three breaks, more with the strength. A plane never takes a kept cell, a cell at or below the ground row or in a
/// protected row; its faces fray over three cells. Every choice is a hash of the seed on lanes of its own, so the steps of
/// revision 1 are untouched.
/// </summary>
internal sealed partial class StructureRuiner
{
    private const long LaneBreak = 0x20, LaneBreakFray = 0x21;

    /// <summary>A frame at least this many times as high (above the ground row) as its longest side breaks horizontally.</summary>
    private const double TallRatio = 1.5;

    /// <summary>A frame whose longest side is at least this many times its height (and three times its other side) breaks
    /// across that side.</summary>
    private const double LongRatio = 1.5;

    /// <summary>The shortest unsupported run (slices along the long axis) a section is cut out of.</summary>
    private const int MinSpanRun = 10;

    /// <summary>How far a break's faces fray, and the chance of a cell at distance 1, 2, 3 from the plane to go with it.</summary>
    private static readonly double[] FrayChance = { 0.5, 0.25, 0.12 };

    /// <summary>A box of the grid (inclusive): x and z both ways, y up to its top.</summary>
    private readonly struct Bounds
    {
        public Bounds(int x0, int x1, int y1, int z0, int z1) => (X0, X1, Y1, Z0, Z1) = (x0, x1, y1, z0, z1);

        public int X0 { get; }

        public int X1 { get; }

        public int Y1 { get; }

        public int Z0 { get; }

        public int Z1 { get; }
    }

    private void Breaks()
    {
        if (_revision < 2 || _hull || _profile != LandmarkRules.ProfileLattice || !TakeableBounds(out var b))
        {
            return;
        }

        int height = b.Y1 - _ground;
        int lx = b.X1 - b.X0 + 1, lz = b.Z1 - b.Z0 + 1;
        int longSide = System.Math.Max(lx, lz), shortSide = System.Math.Min(lx, lz);
        int count = System.Math.Clamp(1 + (int)(U(LaneBreak, 0, 0, 0) * (0.5 + (2 * _s))), 1, 3);
        if (height >= TallRatio * longSide)
        {
            for (int i = 0; i < count; i++)
            {
                HorizontalBreak(i, b, height);
            }
        }
        else if (longSide >= LongRatio * height && longSide >= 3 * shortSide)
        {
            SpanBreaks(count, lx >= lz);
        }
    }

    /// <summary>The box round every block the decay may take above the ground row; false when there is none.</summary>
    private bool TakeableBounds(out Bounds b)
    {
        int x0 = int.MaxValue, x1 = -1, y1 = -1, z0 = int.MaxValue, z1 = -1;
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = _ground + 1; y < _g.H; y++)
            {
                for (int z = 0; z < _g.L; z++)
                {
                    if (_g.Blocks[_g.Index(x, y, z)] != 0 && Takeable(x, y, z))
                    {
                        (x0, x1, y1) = (System.Math.Min(x0, x), System.Math.Max(x1, x), System.Math.Max(y1, y));
                        (z0, z1) = (System.Math.Min(z0, z), System.Math.Max(z1, z));
                    }
                }
            }
        }

        b = new Bounds(x0, x1, y1, z0, z1);
        return x1 >= 0;
    }

    /// <summary>Takes the cell when the decay may, with the fray's chance <paramref name="d"/> cells off the plane (0 = in it).</summary>
    private void BreakCell(int x, int y, int z, int d)
    {
        if (_g.Solid(x, y, z) && Takeable(x, y, z) && (d == 0 || (d <= FrayChance.Length && U(LaneBreakFray, x, y, z) < FrayChance[d - 1])))
        {
            Fell(x, y, z);
        }
    }

    /// <summary>The distance of an offset <paramref name="k"/> from a plane of <paramref name="t"/> cells that starts at 0.</summary>
    private static int PlaneDistance(int k, int t) => k < 0 ? -k : k >= t ? k - t + 1 : 0;

    /// <summary>A horizontal plane of two or three rows at 25–65 % of the height, tilted by up to a quarter row per column in
    /// x and z (neighbouring columns differ by a row at most, so nothing reaches across a plane two rows thick).</summary>
    private void HorizontalBreak(int i, Bounds b, int height)
    {
        int y0 = _ground + 1 + (int)System.Math.Round(height * (0.25 + (0.4 * U(LaneBreak, i, 1, 0))));
        int t = U(LaneBreak, i, 2, 0) < 0.5 ? 2 : 3;
        double sx = (U(LaneBreak, i, 3, 0) - 0.5) * 0.5, sz = (U(LaneBreak, i, 4, 0) - 0.5) * 0.5;
        int cx = (b.X0 + b.X1) / 2, cz = (b.Z0 + b.Z1) / 2, f = FrayChance.Length;
        for (int x = b.X0; x <= b.X1; x++)
        {
            for (int z = b.Z0; z <= b.Z1; z++)
            {
                int yc = y0 + (int)System.Math.Floor((sx * (x - cx)) + (sz * (z - cz)));
                for (int k = -f; k < t + f; k++)
                {
                    BreakCell(x, yc + k, z, PlaneDistance(k, t));
                }
            }
        }
    }

    /// <summary><paramref name="count"/> sections cut out of the unsupported runs along the long axis, each run picked with a
    /// weight of its length squared: a section 20–45 % of its run long, at least two slices off the run's ends, between two
    /// planes across the axis.</summary>
    private void SpanBreaks(int count, bool alongX)
    {
        var runs = UnsupportedRuns(alongX);
        double total = 0;
        foreach (var (_, len) in runs)
        {
            total += (double)len * len;
        }

        for (int i = 0; i < count && total > 0; i++)
        {
            double pick = U(LaneBreak, i, 5, 0) * total;
            var (start, len) = runs[runs.Count - 1];
            foreach (var run in runs)
            {
                pick -= (double)run.Len * run.Len;
                if (pick < 0)
                {
                    (start, len) = run;
                    break;
                }
            }

            int gap = System.Math.Clamp((int)System.Math.Round(len * (0.2 + (0.25 * U(LaneBreak, i, 6, 0)))), 6, len - 4);
            int p0 = start + 2 + (int)(U(LaneBreak, i, 7, 0) * (len - 3 - gap));
            int t = U(LaneBreak, i, 8, 0) < 0.5 ? 2 : 3;
            SpanPlane(alongX, p0, t, (i * 2) + 9);
            SpanPlane(alongX, p0 + gap - t, t, (i * 2) + 10);
        }
    }

    /// <summary>The runs of slices along the long axis that hold a takeable block above the ground row but nothing that
    /// anchors them in their own slice (a block on the ground row or the top protected row, a kept block), at least
    /// <see cref="MinSpanRun"/> long: (first slice, length).</summary>
    private List<(int Start, int Len)> UnsupportedRuns(bool alongX)
    {
        int n = alongX ? _g.W : _g.L, anchorRow = System.Math.Max(_ground, _t.ProtectRows);
        var supported = new bool[n];
        var filled = new bool[n];
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = System.Math.Max(0, anchorRow); y < _g.H; y++)
            {
                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z), a = alongX ? x : z;
                    if (_g.Blocks[i] == 0)
                    {
                        continue;
                    }

                    if (y == anchorRow || _g.Keep[i])
                    {
                        supported[a] = true;
                    }
                    else
                    {
                        filled[a] = true;
                    }
                }
            }
        }

        var runs = new List<(int, int)>();
        for (int a = 0; a < n; a++)
        {
            int start = a;
            while (a < n && filled[a] && !supported[a])
            {
                a++;
            }

            if (a - start >= MinSpanRun)
            {
                runs.Add((start, a - start));
            }
        }

        return runs;
    }

    /// <summary>A vertical plane of <paramref name="t"/> slices across the long axis from slice <paramref name="a0"/> up,
    /// leaning by up to a sixth of a slice per row and per cell across (neighbours differ by a slice at most).</summary>
    private void SpanPlane(bool alongX, int a0, int t, int lane)
    {
        int cross = alongX ? _g.L : _g.W, f = FrayChance.Length;
        double sy = (U(LaneBreak, lane, 1, 1) - 0.5) / 3.0, sc = (U(LaneBreak, lane, 2, 1) - 0.5) / 3.0;
        int ym = (_ground + _g.H) / 2, cm = cross / 2;
        for (int y = _ground + 1; y < _g.H; y++)
        {
            for (int c = 0; c < cross; c++)
            {
                int ac = a0 + (int)System.Math.Floor((sy * (y - ym)) + (sc * (c - cm)));
                for (int k = -f; k < t + f; k++)
                {
                    int a = ac + k;
                    BreakCell(alongX ? a : c, y, alongX ? c : a, PlaneDistance(k, t));
                }
            }
        }
    }
}
