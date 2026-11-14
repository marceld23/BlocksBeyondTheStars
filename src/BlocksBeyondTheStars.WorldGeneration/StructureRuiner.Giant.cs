// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Revision 3 of the pass (#3609): the GIANT WRECKS — authored spaceships up to 256 blocks a side, ruined with the instance's
/// seed (<see cref="GiantShipGenerator"/>). Revision 1's steps were tuned on landmarks of a few dozen blocks: 14 collapse balls
/// of radius 8 at most are barely visible on a hull of 256 × 80 × 100, its erosion and scorch work cell by cell (on a big hull
/// of thin plating that reads as moth-eaten, not as a wreck), every pane of glass went the same way and in space only the
/// largest piece was left. Revision 3 changes the profiles <c>hull</c> and <c>crash</c> only — a landmark profile builds
/// exactly what revision 2 builds. From a distance a giant wreck reads as mostly intact plating with a few big breaches, its
/// breaks and darkened burn zones:
/// <list type="bullet">
/// <item><b>No cell-by-cell erosion.</b> Every hole is a coherent shape at least four cells across: the collapse balls (their
/// radius grows with the hull's volume, they take a share of its own blocks that grows with the strength, their faces are
/// noise-shaped, never frayed cell by cell),
/// the breaks, and the bites (StructureRuiner.GiantDecay.cs) — small noise-shaped balls, most of them on the edge of a breach.</item>
/// <item><b>Thin parts are spared</b> — a ball or a bite lands in a slice across the long axis with the share of material that
/// slice holds (a tapered bow or tail is hit far less), so a thin section may crack but stays recognisable.</item>
/// <item><b>Profile crash</b> — a giant wreck lying on a planet: the rows up to the ground row (the buried keel) and the kept
/// cells anchor; what no longer hangs on them falls and leaves rubble on the ground beside and inside the hull (the caller's
/// palette: scrap, or crystal shards for an alien ship), overgrowth only when the caller says the body has air.</item>
/// <item><b>Hull breaks</b> — a hull whose long axis is at least <see cref="BreakRatio"/> times each other side (and
/// <see cref="BreakMinLength"/> long) breaks across that axis into two or three sections: planes 2–4 cells thick, gently
/// leaning, their faces jagged in coherent noise, never through a slice that holds a kept cell. On a planet the sections stay
/// (the buried keel holds them); in space each one that is big enough stays.</item>
/// <item><b>Scorch in patches</b> (StructureRuiner.GiantDecay.cs) — blackened patches and streaks round the breaches and the
/// break faces (noise-shaped, thinning out over four cells), and a few large burn zones elsewhere; never a sprinkle.</item>
/// <item><b>Glass in patches</b> — a third to a half of the panes, more of them next to a breach. Every light is dead.</item>
/// <item><b>Space</b> (<c>hull</c> at revision 3) — no ground: every piece of at least <see cref="MinPieceCells"/> touching
/// blocks stays where it is (and the largest one always), the crumbs go.</item>
/// <item>Water and plants are never scorched. (Water freezing is an option of every revision,
/// <see cref="RuinOptions.FreezeWater"/>.)</item>
/// </list>
/// Every choice is a hash or value noise of the seed on lanes of its own, so no step moves another; every step is one pass over
/// the grid (or over the cells a ball or a plane covers), so a 2-million-cell hull costs a few passes, never a flood per cell.
/// </summary>
internal sealed partial class StructureRuiner
{
    /// <summary>The giant wrecks' revision (#3609) — the newest the pass knows. A record of a newer build runs this one.</summary>
    public const int GiantRevision = 3;

    /// <summary>In space (revision 3) a piece of at least this many touching blocks stays; smaller ones drift off.</summary>
    public const int MinPieceCells = 400;

    private const long LaneGiantSpot = 0x22, LaneGiantShape = 0x23, LaneHullBreak = 0x24, LaneGlassPatch = 0x25, LaneBreakFace = 0x26;

    /// <summary>The block water freezes to, and the water itself.</summary>
    private const string WaterBlock = "water", FrozenBlock = "ice";

    /// <summary>The shortest long axis that breaks into sections.</summary>
    private const int BreakMinLength = 96;

    /// <summary>A hull breaks when its long axis is at least this many times each of its other sides.</summary>
    private const int BreakRatio = 3;

    /// <summary>The share of the hull's takeable blocks the collapse balls take: base + strength × this (calibrated with the
    /// bites and breaks so about 20–30 % of a hull's cells are gone at the default strength, everything together). A share of
    /// the hull's own blocks, not of its box: the same strength takes as much of a deck-filled carrier as of a hollow shell.</summary>
    private const double SpotShareBase = 0.04, SpotShareStrength = 0.2;

    /// <summary>The most collapse balls one wreck tries (a safety cap: the share is reached long before).</summary>
    private const int MaxGiantSpots = 4000;

    /// <summary>How far a break's face reaches past its core plane, at most, on either side (coherent noise decides).</summary>
    private const int BreakFaceReach = 3;

    /// <summary>The glass patches: the noise scale, and the threshold — base + strength × factor, plus a bonus next to a breach.
    /// Calibrated for a third to a half of the panes at the default strength.</summary>
    private const double GlassPatchScale = 4.0, GlassBase = 0.28, GlassStrength = 0.12, GlassNearBreach = 0.15;

    private readonly Dictionary<ushort, bool> _soft = new(); // block id → not solid (water, plants)

    /// <summary>Whether a block is not solid (water, flora) — never scorched by revision 3. Cached per id.</summary>
    private bool Soft(ushort id)
    {
        if (!_soft.TryGetValue(id, out bool soft))
        {
            soft = _content.BlockById(new Shared.Primitives.BlockId(id)) is { Solid: false };
            _soft[id] = soft;
        }

        return soft;
    }

    // ---------------------------------------------------------------- collapse that scales

    /// <summary>Balls that grow with the hull: the radius with the cube root of its box (3..14); one after another until they
    /// took <see cref="SpotShareBase"/> + strength × <see cref="SpotShareStrength"/> of its takeable blocks. Each ball is centred
    /// on a block the decay may take (so it always bites the hull) — in a thin slice only with the share of material that slice
    /// holds — and its face is noise-shaped.</summary>
    private void GiantCollapse()
    {
        var centres = TakeableBlocks();
        if (centres.Count == 0)
        {
            return;
        }

        int maxR = System.Math.Clamp(IntCubeRoot((long)_g.W * _g.H * _g.L) / 9, 3, 14);
        int minR = System.Math.Max(2, maxR / 3);
        long target = (long)((SpotShareBase + (SpotShareStrength * _s)) * centres.Count), taken = 0;
        int layer = _g.H * _g.L;
        for (int k = 0; k < MaxGiantSpots && taken < target; k++)
        {
            int c = centres[(int)(U(LaneGiantSpot, k, 0, 0) * centres.Count)];
            int x = c / layer, y = (c % layer) / _g.L, z = c % _g.L;
            if (U(LaneGiantSpot, k, 2, 0) < Thickness(x, z))
            {
                taken += ShapedBall(x, y, z, minR + (int)(U(LaneGiantSpot, k, 1, 0) * (maxR - minR + 1)));
            }
        }
    }

    /// <summary>Every block the decay may take, as cell indices in cell order.</summary>
    private List<int> TakeableBlocks()
    {
        var cells = new List<int>();
        int layer = _g.H * _g.L;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            int y = (i % layer) / _g.L;
            if (_g.Blocks[i] != 0 && y >= _floor && Takeable(i / layer, y, i % _g.L))
            {
                cells.Add(i);
            }
        }

        return cells;
    }

    /// <summary>A ball of radius <paramref name="r"/> whose face is shaped by value noise (the radius varies by up to a cell
    /// either way, smoothly): every takeable cell inside goes — a ragged but coherent hole, no lone cells round its rim. Returns
    /// how many blocks it took.</summary>
    private int ShapedBall(int cx, int cy, int cz, int r)
    {
        int reach = r + 1, taken = 0;
        for (int x = System.Math.Max(0, cx - reach); x <= System.Math.Min(_g.W - 1, cx + reach); x++)
        {
            for (int y = System.Math.Max(_floor, cy - reach); y <= System.Math.Min(_g.H - 1, cy + reach); y++)
            {
                for (int z = System.Math.Max(0, cz - reach); z <= System.Math.Min(_g.L - 1, cz + reach); z++)
                {
                    int d2 = ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) + ((z - cz) * (z - cz));
                    if (d2 > reach * reach || _g.Blocks[_g.Index(x, y, z)] == 0 || !Takeable(x, y, z) || Spared(x, y, z))
                    {
                        continue;
                    }

                    double shaped = r + ((Noise.Value3D(_seed + LaneGiantShape, x / 2.5, y / 2.5, z / 2.5) - 0.5) * 2.0);
                    if (d2 <= shaped * shaped)
                    {
                        Fell(x, y, z);
                        taken++;
                    }
                }
            }
        }

        return taken;
    }

    /// <summary>The integer cube root (the largest c with c³ ≤ v) — exact on every platform, no floating-point root.</summary>
    private static int IntCubeRoot(long v)
    {
        int c = 0;
        while ((long)(c + 1) * (c + 1) * (c + 1) <= v)
        {
            c++;
        }

        return c;
    }

    // ---------------------------------------------------------------- hull breaks

    /// <summary>A long hull breaks across its long axis: one plane (two sections) aimed at 30–70 % of the length, or — more likely
    /// the stronger the decay — two (three sections) aimed at 22–40 % and 60–78 %. A plane's slab (with its lean and its face)
    /// never holds a kept cell (the stairs that join the decks): from its hashed aim it walks to the nearest clean slice in its
    /// window, else in the middle 70 % of the hull, at least a fifth of the length from the other break; with none, it is left
    /// out. Then the bites (StructureRuiner.GiantDecay.cs) gnaw at the edges of every breach.</summary>
    private void HullBreaks()
    {
        bool alongX = _g.W >= _g.L;
        int len = alongX ? _g.W : _g.L, cross = alongX ? _g.L : _g.W;
        if (len >= BreakMinLength && len >= BreakRatio * cross && len >= BreakRatio * _g.H)
        {
            bool[] kept = KeptSlices(alongX, len);
            int count = U(LaneHullBreak, 0, 0, 0) < 0.15 + (0.5 * _s) ? 2 : 1, other = -1;
            for (int i = 0; i < count; i++)
            {
                double lo = count == 1 ? 0.3 : i == 0 ? 0.22 : 0.6, span = count == 1 ? 0.4 : 0.18;
                int t = 2 + (int)(U(LaneHullBreak, i, 1, 0) * 3);
                double sy = (U(LaneHullBreak, i, 2, 0) - 0.5) / 4.0, sc = (U(LaneHullBreak, i, 3, 0) - 0.5) / 4.0;
                int aim = (int)(len * (lo + (span * U(LaneHullBreak, i, 4, 0))));
                int reach = (int)System.Math.Ceiling((System.Math.Abs(sy) * _g.H / 2.0) + (System.Math.Abs(sc) * cross / 2.0))
                    + t + BreakFaceReach + 1;
                int p = CleanSlice(kept, aim, (int)(len * lo), (int)(len * (lo + span)), reach, other);
                if (p < 0)
                {
                    p = CleanSlice(kept, aim, (int)(len * 0.15), (int)(len * 0.85), reach, other);
                }

                if (p >= 0)
                {
                    HullPlane(alongX, p, t, sy, sc, i);
                    other = p;
                }
            }
        }

        GiantBites();
    }

    /// <summary>The slice nearest <paramref name="aim"/> (aim, aim + 1, aim − 1, …) within <paramref name="from"/>..<paramref name="to"/>
    /// whose slab of <paramref name="reach"/> slices either way holds no kept cell and that lies at least a fifth of the length
    /// from <paramref name="other"/> (−1 = none); −1 when there is none.</summary>
    private static int CleanSlice(bool[] kept, int aim, int from, int to, int reach, int other)
    {
        int len = kept.Length;
        for (int d = 0; d <= len; d++)
        {
            for (int side = d == 0 ? 1 : 0; side < 2; side++)
            {
                int p = side == 0 ? aim - d : aim + d;
                if (p >= from && p <= to && (other < 0 || System.Math.Abs(p - other) >= len / 5) && NoKeptSlice(kept, p - reach, p + reach))
                {
                    return p;
                }
            }
        }

        return -1;
    }

    /// <summary>Per slice across the long axis: whether it holds a kept cell.</summary>
    private bool[] KeptSlices(bool alongX, int len)
    {
        var kept = new bool[len];
        int layer = _g.H * _g.L;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Keep[i])
            {
                kept[alongX ? i / layer : i % _g.L] = true;
            }
        }

        return kept;
    }

    private static bool NoKeptSlice(bool[] kept, int from, int to)
    {
        for (int a = System.Math.Max(0, from); a <= System.Math.Min(kept.Length - 1, to); a++)
        {
            if (kept[a])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A plane of <paramref name="t"/> slices across the long axis from slice <paramref name="a0"/>, leaning by
    /// <paramref name="sy"/> slices per row and <paramref name="sc"/> per cell across (at most a quarter: neighbours differ by a
    /// slice at most, so nothing reaches across) — above the ground row on a planet, every row in space. Its faces are jagged in
    /// coherent noise: each side reaches up to <see cref="BreakFaceReach"/> slices further, varying smoothly across the face, so
    /// the torn edge is ragged without a single loose cell.</summary>
    private void HullPlane(bool alongX, int a0, int t, double sy, double sc, int lane)
    {
        int cross = alongX ? _g.L : _g.W;
        int y0 = System.Math.Max(0, _ground + 1), ym = (y0 + _g.H) / 2, cm = cross / 2;
        long faceA = _seed + LaneBreakFace + (lane * 2), faceB = faceA + 1;
        for (int y = y0; y < _g.H; y++)
        {
            for (int c = 0; c < cross; c++)
            {
                int ac = a0 + (int)System.Math.Floor((sy * (y - ym)) + (sc * (c - cm)));
                int below = (int)(Noise.Value2D(faceA, y / 3.0, c / 3.0) * (BreakFaceReach + 0.99));
                int above = (int)(Noise.Value2D(faceB, y / 3.0, c / 3.0) * (BreakFaceReach + 0.99));
                for (int a = ac - below; a < ac + t + above; a++)
                {
                    BreakCell(alongX ? a : c, y, alongX ? c : a, 0);
                }
            }
        }
    }

    // ---------------------------------------------------------------- glass in patches, dead lights

    /// <summary>Revision 3's openings: glass is smashed in coherent patches (never at or below the ground row — it lies in
    /// the rock), more of it next to a breach; every light becomes its switched-off twin (or the options' one, or air). Never
    /// in a kept cell or a protected row.</summary>
    private void GiantOpenings()
    {
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
                    if (b == 0 || _g.Keep[i])
                    {
                        continue;
                    }

                    int kind = KindOf(b);
                    if (kind == 1 && y > _ground && SmashesGlass(x, y, z))
                    {
                        _g.Clear(x, y, z);
                    }
                    else if (kind == 2)
                    {
                        ushort off = _lightOff[b];
                        _g.Set(x, y, z, off, shape: off != 0 ? _g.ShapeAt(x, y, z) : 0);
                    }
                }
            }
        }
    }

    private bool SmashesGlass(int x, int y, int z)
    {
        double limit = GlassBase + (GlassStrength * S(x, z)) + (NearBreach(x, y, z) ? GlassNearBreach : 0.0);
        return Noise.Value3D(_seed + LaneGlassPatch, x / GlassPatchScale, y / GlassPatchScale, z / GlassPatchScale) < limit;
    }

    // ---------------------------------------------------------------- water freezes

    /// <summary><see cref="RuinOptions.FreezeWater"/>: every water cell becomes ice, keeping its tint and glow — kept cells
    /// and protected rows too.</summary>
    private void Freeze()
    {
        if (_t.FreezeFrom == 0)
        {
            return;
        }

        int layer = _g.H * _g.L;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] == _t.FreezeFrom)
            {
                var (tint, glow) = _g.Mods.TryGetValue(i, out var m) ? m : (0, 0);
                _g.Set(i / layer, (i % layer) / _g.L, i % _g.L, _t.FreezeTo, tint, glow);
            }
        }
    }

    // ---------------------------------------------------------------- pieces kept in space

    /// <summary>Revision 3 in space: every piece of at least <see cref="MinPieceCells"/> touching blocks holds, and the largest
    /// one always (the first in cell order on a tie) — one labelling pass over the grid.</summary>
    private void AnchorBigPieces(BitArray held, Stack<int> open)
    {
        var seen = new BitArray(_g.Blocks.Length);
        var stack = new Stack<int>();
        int best = -1, bestSize = 0;
        for (int i = 0; i < _g.Blocks.Length; i++)
        {
            if (_g.Blocks[i] == 0 || seen[i])
            {
                continue;
            }

            seen[i] = true;
            stack.Push(i);
            int size = 1 + Spread(seen, stack);
            if (size > bestSize)
            {
                (best, bestSize) = (i, size);
            }

            if (size >= MinPieceCells && !held[i])
            {
                held[i] = true;
                open.Push(i);
            }
        }

        if (best >= 0 && !held[best])
        {
            held[best] = true;
            open.Push(best);
        }
    }
}
