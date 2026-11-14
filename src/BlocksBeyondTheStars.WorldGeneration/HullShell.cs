// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The visible shell of a big voxel hull, kept up to date while its cells change (#3612). A giant wreck in space holds a few
/// hundred thousand cells; the pilots outside only ever see the outer skin and what shows through its windows, so the server
/// sends exactly that — the rule of <c>StationHull.VisibleCells</c>: every
/// block next to outside space, plus, through see-through cells (glass, force fields) and shaped cells, the blocks up to
/// <see cref="StationHull.WindowDepth"/> steps inside. <see cref="Build"/> computes that once (the same cells as
/// <c>VisibleCells</c> — a test pins it); after that <see cref="Changed"/> answers, per changed cell, which cells it laid open:
/// a mined hull block lets outside space flow into the hall behind it, and every wall of that hall becomes visible.
/// <para>The shell only ever GROWS: a block placed into a hole hides nothing that was already sent (a pilot keeps a few
/// cells they can no longer see — cheaper than taking them back), and a visible cell that turned to air keeps its mark, so
/// its removal still goes to the pilots who hold it. Cells outside the box the shell was built for (a block built out past
/// the hull) count as visible. Three bit sets over the box with a one-cell padding: outside space, the window reach, the
/// shell.</para>
/// </summary>
public sealed class HullShell
{
    private static readonly Vector3i[] Steps =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private readonly Vector3i _min;
    private readonly int _w, _h, _l, _ph, _pl;
    private readonly ulong[] _outside;
    private readonly ulong[] _window;
    private readonly ulong[] _visible;
    private readonly HashSet<ushort> _seeThrough;
    private readonly Func<Vector3i, ushort> _block;
    private readonly Func<Vector3i, int> _shape;

    private HullShell(Vector3i min, Vector3i max, HashSet<ushort> seeThrough, Func<Vector3i, ushort> block, Func<Vector3i, int> shape)
    {
        _min = min;
        _w = max.X - min.X + 1;
        _h = max.Y - min.Y + 1;
        _l = max.Z - min.Z + 1;
        _ph = _h + 2;
        _pl = _l + 2;
        int words = (int)(((long)(_w + 2) * _ph * _pl + 63) / 64);
        _outside = new ulong[words];
        _window = new ulong[words];
        _visible = new ulong[words];
        _seeThrough = seeThrough;
        _block = block;
        _shape = shape;
    }

    /// <summary>The box the shell was built for (absolute cells, both corners inclusive).</summary>
    public Vector3i Min => _min;

    public Vector3i Max => new(_min.X + _w - 1, _min.Y + _h - 1, _min.Z + _l - 1);

    /// <summary>
    /// Builds the shell of the hull whose cells lie in the box <paramref name="min"/> … <paramref name="max"/>.
    /// <paramref name="cells"/> lists every non-air cell once (read into a dense grid for this pass);
    /// <paramref name="block"/> and <paramref name="shape"/> read the LIVE hull afterwards (<see cref="Changed"/>) — the
    /// packed shape decides whether a non-see-through block lets the view pass (<see cref="ShapeCode.IsCube"/>).
    /// </summary>
    public static HullShell Build(Vector3i min, Vector3i max, IEnumerable<KeyValuePair<Vector3i, ushort>> cells,
        Func<Vector3i, ushort> block, Func<Vector3i, int> shape, HashSet<ushort> seeThrough)
    {
        var shell = new HullShell(min, max, seeThrough, block, shape);
        var dense = new ushort[(long)shell._w * shell._h * shell._l];
        foreach (var kv in cells)
        {
            var c = kv.Key - min;
            if (shell.InBox(c.X, c.Y, c.Z))
            {
                dense[shell.Dense(c.X, c.Y, c.Z)] = kv.Value;
            }
        }

        shell.FloodOutside(dense);
        var budget = shell.WindowBudgets(dense);
        shell.MarkVisible(dense, budget);
        return shell;
    }

    /// <summary>Whether a pilot is shown this cell (or was, before it turned to air). Everything outside the box is.</summary>
    public bool IsVisible(Vector3i cell)
    {
        var c = cell - _min;
        return !InBox(c.X, c.Y, c.Z) || Has(_visible, Idx(c.X, c.Y, c.Z));
    }

    /// <summary>
    /// The cell <paramref name="cell"/> just changed (the live hull already holds its new block): adds to
    /// <paramref name="laidOpen"/> every OTHER non-air cell that becomes visible through it. A cell that turned to air and
    /// touches outside space lets the outside flow through every air cell connected to it; a cell that became see-through or
    /// shaped (or an air cell inside a window's reach) lets the view in up to <see cref="StationHull.WindowDepth"/> steps. A
    /// block placed where a pilot can see it joins the shell (the caller sends that cell itself in any case).
    /// </summary>
    public void Changed(Vector3i cell, ICollection<Vector3i> laidOpen)
    {
        var c = cell - _min;
        if (!InBox(c.X, c.Y, c.Z))
        {
            return; // out past the box: always visible, and it hides or opens nothing inside
        }

        int i = Idx(c.X, c.Y, c.Z);
        bool air = _block(cell) == 0;
        var opened = new List<Vector3i>();
        var seeds = new List<(Vector3i Cell, sbyte Budget)>();
        if (air)
        {
            if (TouchesOutside(c))
            {
                FloodFrom(c, opened, seeds);
            }
            else if (Has(_window, i) || TouchesWindow(c))
            {
                seeds.Add((c, StationHull.WindowDepth - 1));
            }
        }
        else
        {
            Clear(_outside, i);
            bool seen = TouchesOutside(c) || TouchesWindow(c) || Has(_window, i);
            if (seen)
            {
                Set(_visible, i);
                if (Passable(c))
                {
                    seeds.Add((c, StationHull.WindowDepth));
                }
            }
        }

        opened.AddRange(WindowFrom(seeds));
        opened.Add(c);
        CollectNewlyVisible(opened, c, laidOpen);
    }

    // ---------------------------------------------------------------- the build

    /// <summary>Outside space: the air connected to the padding's corner, as <c>StationHull</c>'s flood.</summary>
    private void FloodOutside(ushort[] dense)
    {
        var queue = new Queue<Vector3i>();
        Set(_outside, Idx(-1, -1, -1));
        queue.Enqueue(new Vector3i(-1, -1, -1));
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in Steps)
            {
                int x = p.X + d.X, y = p.Y + d.Y, z = p.Z + d.Z;
                if (InPad(x, y, z) && !Has(_outside, Idx(x, y, z)) && (!InBox(x, y, z) || dense[Dense(x, y, z)] == 0))
                {
                    Set(_outside, Idx(x, y, z));
                    queue.Enqueue(new Vector3i(x, y, z));
                }
            }
        }
    }

    /// <summary>The view through see-through and shaped cells: a depth budget that shrinks with every step inside
    /// (<c>StationHull.VisibleCells</c>' pass, on the dense grid). Marks the window bits; returns the budgets.</summary>
    private sbyte[] WindowBudgets(ushort[] dense)
    {
        var budget = new sbyte[_outside.Length * 64];
        var inside = new Queue<Vector3i>();
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                for (int z = 0; z < _l; z++)
                {
                    if (DensePassable(dense, x, y, z) && TouchesOutside(new Vector3i(x, y, z)))
                    {
                        budget[Idx(x, y, z)] = StationHull.WindowDepth;
                        inside.Enqueue(new Vector3i(x, y, z));
                    }
                }

        while (inside.Count > 0)
        {
            var p = inside.Dequeue();
            int next = budget[Idx(p.X, p.Y, p.Z)] - 1;
            if (next <= 0)
            {
                continue;
            }

            foreach (var d in Steps)
            {
                int x = p.X + d.X, y = p.Y + d.Y, z = p.Z + d.Z;
                if (!InBox(x, y, z) || Has(_outside, Idx(x, y, z)) || budget[Idx(x, y, z)] >= next)
                {
                    continue;
                }

                if (dense[Dense(x, y, z)] == 0 || DensePassable(dense, x, y, z))
                {
                    budget[Idx(x, y, z)] = (sbyte)next;
                    inside.Enqueue(new Vector3i(x, y, z));
                }
            }
        }

        for (int i = 0; i < budget.Length; i++)
        {
            if (budget[i] > 0)
            {
                Set(_window, i);
            }
        }

        return budget;
    }

    /// <summary>The shell: a non-air cell the view reaches itself, or one next to outside space or to the view's reach.</summary>
    private void MarkVisible(ushort[] dense, sbyte[] budget)
    {
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                for (int z = 0; z < _l; z++)
                {
                    if (dense[Dense(x, y, z)] == 0)
                    {
                        continue;
                    }

                    int i = Idx(x, y, z);
                    bool visible = budget[i] > 0;
                    for (int s = 0; s < Steps.Length && !visible; s++)
                    {
                        int nx = x + Steps[s].X, ny = y + Steps[s].Y, nz = z + Steps[s].Z;
                        visible = Has(_outside, Idx(nx, ny, nz)) || (InBox(nx, ny, nz) && budget[Idx(nx, ny, nz)] > 0);
                    }

                    if (visible)
                    {
                        Set(_visible, i);
                    }
                }
    }

    private bool DensePassable(ushort[] dense, int x, int y, int z)
    {
        ushort b = dense[Dense(x, y, z)];
        return b != 0 && (_seeThrough.Contains(b) || !ShapeCode.IsCube(_shape(new Vector3i(x, y, z) + _min)));
    }

    // ---------------------------------------------------------------- the updates

    /// <summary>Outside space flows from <paramref name="start"/> (air that touches it) through every connected air cell of
    /// the box not yet outside; each newly outside cell goes to <paramref name="opened"/>, and a see-through or shaped cell
    /// next to one becomes a window seed.</summary>
    private void FloodFrom(Vector3i start, List<Vector3i> opened, List<(Vector3i Cell, sbyte Budget)> seeds)
    {
        var queue = new Queue<Vector3i>();
        Set(_outside, Idx(start.X, start.Y, start.Z));
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            opened.Add(p);
            foreach (var d in Steps)
            {
                var n = p + d;
                if (!InBox(n.X, n.Y, n.Z) || Has(_outside, Idx(n.X, n.Y, n.Z)))
                {
                    continue;
                }

                if (_block(n + _min) == 0)
                {
                    Set(_outside, Idx(n.X, n.Y, n.Z));
                    queue.Enqueue(n);
                }
                else if (Passable(n))
                {
                    seeds.Add((n, StationHull.WindowDepth));
                }
            }
        }
    }

    /// <summary>The view's reach from <paramref name="seeds"/> (their own budgets), through air and see-through or shaped
    /// cells that are not outside space — a local copy of the build's window pass. Returns the cells it reached, all marked.</summary>
    private List<Vector3i> WindowFrom(List<(Vector3i Cell, sbyte Budget)> seeds)
    {
        var reached = new List<Vector3i>();
        var budget = new Dictionary<int, sbyte>();
        var queue = new Queue<Vector3i>();
        foreach (var (cell, b) in seeds)
        {
            int i = Idx(cell.X, cell.Y, cell.Z);
            if (!budget.TryGetValue(i, out sbyte had) || had < b)
            {
                budget[i] = b;
                Set(_window, i);
                reached.Add(cell);
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int next = budget[Idx(p.X, p.Y, p.Z)] - 1;
            if (next <= 0)
            {
                continue;
            }

            foreach (var d in Steps)
            {
                var n = p + d;
                if (!InBox(n.X, n.Y, n.Z) || Has(_outside, Idx(n.X, n.Y, n.Z))
                    || (budget.TryGetValue(Idx(n.X, n.Y, n.Z), out sbyte had) && had >= next))
                {
                    continue;
                }

                if (_block(n + _min) == 0 || Passable(n))
                {
                    budget[Idx(n.X, n.Y, n.Z)] = (sbyte)next;
                    Set(_window, Idx(n.X, n.Y, n.Z));
                    reached.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return reached;
    }

    /// <summary>Every non-air cell in or next to <paramref name="opened"/> that was not in the shell joins it; all but the
    /// changed cell itself go to <paramref name="laidOpen"/> (the caller sends that one anyway).</summary>
    private void CollectNewlyVisible(List<Vector3i> opened, Vector3i changed, ICollection<Vector3i> laidOpen)
    {
        foreach (var p in opened)
        {
            for (int s = -1; s < Steps.Length; s++)
            {
                var n = s < 0 ? p : p + Steps[s];
                if (!InBox(n.X, n.Y, n.Z))
                {
                    continue;
                }

                int i = Idx(n.X, n.Y, n.Z);
                if (Has(_visible, i) || _block(n + _min) == 0)
                {
                    continue;
                }

                bool seen = Has(_window, i) || TouchesOutside(n) || TouchesWindow(n);
                if (!seen)
                {
                    continue;
                }

                Set(_visible, i);
                if (n != changed)
                {
                    laidOpen.Add(n + _min);
                }
            }
        }
    }

    private bool Passable(Vector3i local)
    {
        var cell = local + _min;
        ushort b = _block(cell);
        return b != 0 && (_seeThrough.Contains(b) || !ShapeCode.IsCube(_shape(cell)));
    }

    private bool TouchesOutside(Vector3i c)
    {
        foreach (var d in Steps)
        {
            int x = c.X + d.X, y = c.Y + d.Y, z = c.Z + d.Z;
            if (!InPad(x, y, z) || Has(_outside, Idx(x, y, z)))
            {
                return true;
            }
        }

        return false;
    }

    private bool TouchesWindow(Vector3i c)
    {
        foreach (var d in Steps)
        {
            int x = c.X + d.X, y = c.Y + d.Y, z = c.Z + d.Z;
            if (InBox(x, y, z) && Has(_window, Idx(x, y, z)))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- indexing

    private bool InBox(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < _w && y < _h && z < _l;

    private bool InPad(int x, int y, int z) => x >= -1 && y >= -1 && z >= -1 && x <= _w && y <= _h && z <= _l;

    private int Idx(int x, int y, int z) => ((x + 1) * _ph + (y + 1)) * _pl + (z + 1);

    private int Dense(int x, int y, int z) => (x * _h + y) * _l + z;

    private static bool Has(ulong[] bits, int i) => (bits[i >> 6] & (1UL << (i & 63))) != 0;

    private static void Set(ulong[] bits, int i) => bits[i >> 6] |= 1UL << (i & 63);

    private static void Clear(ulong[] bits, int i) => bits[i >> 6] &= ~(1UL << (i & 63));
}
