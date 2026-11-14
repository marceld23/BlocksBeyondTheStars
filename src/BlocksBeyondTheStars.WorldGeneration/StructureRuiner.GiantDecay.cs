// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Revision 3 (#3609), the giant wrecks' surface: the bites that gnaw at the breaches (instead of erosion cell by cell), the
/// scorch patches round the breaches and the burn zones (instead of a sprinkle), and the thickness that spares a thin section.
/// See StructureRuiner.Giant.cs for the whole revision.
/// </summary>
internal sealed partial class StructureRuiner
{
    private const long LaneGiantBite = 0x30, LaneScorchPatch = 0x31, LaneBurnZone = 0x32;

    /// <summary>The share of the hull's takeable blocks the bites take per unit of strength (a bite is a noise-shaped ball of
    /// radius 2–3).</summary>
    private const double BiteShareStrength = 0.08;

    /// <summary>The share of the bites that land anywhere on the hull; the rest gnaw at the edge of a breach.</summary>
    private const double BitesElsewhere = 0.15;

    /// <summary>A slice across the long axis that holds at least this share of the fullest slice's blocks is "thick" (it takes
    /// balls and bites at full rate); a thinner one in proportion, down to <see cref="MinThickness"/>.</summary>
    private const double ThickShare = 0.5, MinThickness = 0.1;

    /// <summary>How far the scorch reaches from a breach (cells, through anything), and the patch noise's threshold at each
    /// distance 1..4 for the default near chance (0.3; the options' near chance scales it). The noise makes the patches; the
    /// falling threshold thins them out away from the breach.</summary>
    private const int ScorchReach = 3;

    private static readonly double[] ScorchLimit = { 0.0, 0.42, 0.34, 0.26 };

    /// <summary>The burn zones away from the breaches (when the options' far chance is above 0): where a low-frequency noise is
    /// above <see cref="BurnZoneLevel"/>, the patch noise below <see cref="BurnZoneLimit"/> is blackened — a few large
    /// darkened zones, never a sprinkle.</summary>
    private const double BurnZoneScale = 12.0, BurnZoneLevel = 0.8, BurnZoneLimit = 0.4, ScorchPatchScale = 3.5;

    private double[]? _thickness; // per slice across the long axis (revision 3), computed before the first ball

    // ---------------------------------------------------------------- thickness

    /// <summary>How much of a full slice the slice across the long axis at (x, z) holds, 0.1..1 (see <see cref="ThickShare"/>):
    /// the tapered bow of a hull is thin, its middle is not. Counted once, on the intact hull.</summary>
    private double Thickness(int x, int z)
    {
        _thickness ??= SliceThickness();
        return _thickness[_g.W >= _g.L ? x : z];
    }

    private double[] SliceThickness()
    {
        bool alongX = _g.W >= _g.L;
        var girth = new int[alongX ? _g.W : _g.L];
        int layer = _g.H * _g.L, max = 0;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] != 0)
            {
                int a = alongX ? i / layer : i % _g.L;
                max = System.Math.Max(max, ++girth[a]);
            }
        }

        var f = new double[girth.Length];
        for (int a = 0; a < f.Length; a++)
        {
            f[a] = max == 0 ? 1.0 : System.Math.Clamp(girth[a] / (ThickShare * max), MinThickness, 1.0);
        }

        return f;
    }

    // ---------------------------------------------------------------- bites

    /// <summary>The erosion of a giant wreck: noise-shaped balls of radius 2–3 (at least four cells across), one after another
    /// until they took <see cref="BiteShareStrength"/> × strength of the takeable blocks; most of them centred on a block at the
    /// edge of a breach (the balls and breaks before), the rest anywhere — each only with the thickness of its slice.</summary>
    private void GiantBites()
    {
        var all = TakeableBlocks();
        if (all.Count == 0)
        {
            return;
        }

        var edge = new List<int>();
        int layer = _g.H * _g.L;
        foreach (int i in all)
        {
            if (NearBreach(i / layer, (i % layer) / _g.L, i % _g.L))
            {
                edge.Add(i);
            }
        }

        long target = (long)(BiteShareStrength * _s * all.Count), taken = 0;
        for (int k = 0; k < all.Count && taken < target; k++)
        {
            var from = edge.Count > 0 && U(LaneGiantBite, k, 0, 0) >= BitesElsewhere ? edge : all;
            int c = from[(int)(U(LaneGiantBite, k, 1, 0) * from.Count)];
            int x = c / layer, y = (c % layer) / _g.L, z = c % _g.L;
            if (U(LaneGiantBite, k, 2, 0) < Thickness(x, z))
            {
                taken += ShapedBall(x, y, z, U(LaneGiantBite, k, 3, 0) < 0.5 ? 2 : 3);
            }
        }
    }

    // ---------------------------------------------------------------- scorch in patches

    /// <summary>Revision 3's scorch: a block (no glass, light, ladder, water or plant; not kept, not in a protected row) within
    /// <see cref="ScorchReach"/> cells of a breach is marked where the patch noise lies below the threshold of its distance — so
    /// the marks are blackened patches and streaks hugging the breaches and the break faces, thinning out away from them; when
    /// the options' far chance is above 0, a few large burn zones elsewhere too. A mark is what the options say (a block and/or
    /// a tint), as in every revision.</summary>
    private void GiantScorch((ushort Block, int Tint, double Near, double Far) scorch)
    {
        byte[] dist = BreachDistance(ScorchReach);
        double scale = scorch.Near / 0.3;
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = 0; y < _g.H; y++)
            {
                if (Protected(y))
                {
                    continue;
                }

                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z);
                    ushort b = _g.Blocks[i];
                    if (b == 0 || _g.Keep[i] || KindOf(b) != 0 || Soft(b))
                    {
                        continue;
                    }

                    double limit = dist[i] <= ScorchReach ? ScorchLimit[dist[i]] * scale : 0.0;
                    if (scorch.Far > 0 && limit < BurnZoneLimit
                        && Noise.Value3D(_seed + LaneBurnZone, x / BurnZoneScale, y / BurnZoneScale, z / BurnZoneScale) > BurnZoneLevel)
                    {
                        limit = BurnZoneLimit;
                    }

                    if (limit > 0 && Noise.Value3D(_seed + LaneScorchPatch, x / ScorchPatchScale, y / ScorchPatchScale, z / ScorchPatchScale) < limit)
                    {
                        int glow = scorch.Block == 0 && _g.Mods.TryGetValue(i, out var m) ? m.Glow : 0;
                        _g.Set(x, y, z, scorch.Block != 0 ? scorch.Block : b, scorch.Tint, glow, _g.ShapeAt(x, y, z));
                    }
                }
            }
        }
    }

    /// <summary>Per cell, how many steps (faces, through anything) it lies from a cell the decay took out, up to
    /// <paramref name="reach"/>; 255 beyond. One breadth-first pass from every breach at once.</summary>
    private byte[] BreachDistance(int reach)
    {
        var dist = new byte[_g.Blocks.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = _breach![i] ? (byte)0 : byte.MaxValue;
            if (dist[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        int layer = _g.H * _g.L;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (dist[i] >= reach)
            {
                continue;
            }

            int x = i / layer, y = (i % layer) / _g.L, z = i % _g.L;
            foreach (var (dx, dy, dz) in Faces)
            {
                if (_g.In(x + dx, y + dy, z + dz))
                {
                    int n = _g.Index(x + dx, y + dy, z + dz);
                    if (dist[n] == byte.MaxValue)
                    {
                        dist[n] = (byte)(dist[i] + 1);
                        queue.Enqueue(n);
                    }
                }
            }
        }

        return dist;
    }

    private static readonly (int X, int Y, int Z)[] Faces = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
}
