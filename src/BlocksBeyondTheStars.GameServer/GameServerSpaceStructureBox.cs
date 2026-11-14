// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3612 — what a BIG space structure (a giant wreck: a few hundred thousand cells) needs so that an edit costs the size of
/// the edit, not the size of the hull:
/// <list type="bullet">
/// <item>the cell box is cached and follows every <see cref="Set(Vector3i, Shared.Primitives.BlockId)"/> through per-axis
/// slice counts — the box used to be a scan of every cell, run several times per edit;</item>
/// <item><see cref="ShellOnly"/>: the pilots outside get the visible shell (<see cref="HullShell"/>), never the hidden
/// interior, and the cells an edit lays open (<see cref="LaidOpen"/>);</item>
/// <item><see cref="Derived"/>: the cells are a pure function of a template and a seed, so the save keeps only the players'
/// changes (<see cref="Touched"/> feeds that delta — see <c>GameServerSpaceStructureDerived.cs</c>).</item>
/// </list>
/// </summary>
public sealed partial class SpaceStructure
{
    /// <summary>The most laid-open cells kept for the next send; past it the pilots get the whole design again.</summary>
    internal const int LaidOpenLimit = 4096;

    private Slices? _xs, _ys, _zs;
    private int _trackedCount = -1;

    /// <summary>#3612: the pilots outside are sent the visible shell only (<see cref="Shell"/>), not every cell — set for a
    /// giant wreck. The server keeps every cell; only what goes over the wire is the shell.</summary>
    public bool ShellOnly { get; set; }

    /// <summary>The visible shell of a <see cref="ShellOnly"/> structure — built on its first send, then kept up to date by
    /// every <see cref="Set(Vector3i, Shared.Primitives.BlockId)"/>.</summary>
    public HullShell? Shell { get; set; }

    /// <summary>Cells a change laid open since the last send (a hole into a hall shows its walls) — drained by the sender.</summary>
    internal HashSet<Vector3i> LaidOpen { get; } = new();

    /// <summary>True when more cells were laid open than <see cref="LaidOpenLimit"/> keeps: the next send is the whole design.</summary>
    internal bool LaidOpenOverflow { get; set; }

    /// <summary>#3612: the cells are a pure function of template + seed (registered through
    /// <c>GameServer.RegisterDerivedStation</c>): the save stores only the players' changes, never the cell list.</summary>
    public bool Derived { get; set; }

    /// <summary>The cells changed since the derived delta was last written (null = not tracked).</summary>
    internal HashSet<Vector3i>? Touched { get; set; }

    /// <summary>The cell-grid bounding box (both corners inclusive; the origin cell for an empty build). The first call
    /// counts the cells per X, Y and Z slice; after that every <c>Set</c> keeps the counts, so this is O(1) — a cell
    /// mined off the box's face moves that face to the next slice that still holds a cell. A caller that wrote
    /// <see cref="Cells"/> directly (only the NPC station hull does, before any box is asked for) is caught by the count.</summary>
    public (Vector3i Min, Vector3i Max) CellBox()
    {
        if (Cells.Count == 0)
        {
            return (new Vector3i(0, 0, 0), new Vector3i(0, 0, 0));
        }

        if (_xs is null || _trackedCount != Cells.Count)
        {
            RebuildBox();
        }

        return (new Vector3i(_xs!.Min, _ys!.Min, _zs!.Min), new Vector3i(_xs.Max, _ys.Max, _zs.Max));
    }

    private void RebuildBox()
    {
        _xs = new Slices();
        _ys = new Slices();
        _zs = new Slices();
        foreach (var c in Cells.Keys)
        {
            _xs.Add(c.X);
            _ys.Add(c.Y);
            _zs.Add(c.Z);
        }

        _trackedCount = Cells.Count;
    }

    private void TrackBox(Vector3i pos)
    {
        if (_xs is null)
        {
            return;
        }

        _xs.Add(pos.X);
        _ys!.Add(pos.Y);
        _zs!.Add(pos.Z);
        _trackedCount++;
    }

    private void UntrackBox(Vector3i pos)
    {
        if (_xs is null)
        {
            return;
        }

        _xs.Remove(pos.X);
        _ys!.Remove(pos.Y);
        _zs!.Remove(pos.Z);
        _trackedCount--;
    }

    /// <summary>After every cell write: the derived delta notes the cell, the shell learns what it laid open.</summary>
    private void AfterSet(Vector3i pos)
    {
        Touched?.Add(pos);
        if (Shell is null)
        {
            return;
        }

        if (LaidOpenOverflow)
        {
            Shell.Changed(pos, new List<Vector3i>()); // the whole design goes out next anyway — only keep the shell right
            return;
        }

        Shell.Changed(pos, LaidOpen);
        if (LaidOpen.Count > LaidOpenLimit)
        {
            LaidOpen.Clear();
            LaidOpenOverflow = true;
        }
    }

    /// <summary>The number of cells per slice of one axis, with the lowest and highest slice that holds any.</summary>
    private sealed class Slices
    {
        private readonly Dictionary<int, int> _count = new();

        public int Min { get; private set; } = int.MaxValue;

        public int Max { get; private set; } = int.MinValue;

        public void Add(int v)
        {
            _count[v] = (_count.TryGetValue(v, out int n) ? n : 0) + 1;
            if (v < Min)
            {
                Min = v;
            }

            if (v > Max)
            {
                Max = v;
            }
        }

        public void Remove(int v)
        {
            if (!_count.TryGetValue(v, out int n))
            {
                return;
            }

            if (n > 1)
            {
                _count[v] = n - 1;
                return;
            }

            _count.Remove(v);
            if (_count.Count == 0)
            {
                Min = int.MaxValue;
                Max = int.MinValue;
                return;
            }

            while (!_count.ContainsKey(Min))
            {
                Min++; // the face moves in to the next slice that still holds a cell (bounded by the box)
            }

            while (!_count.ContainsKey(Max))
            {
                Max--;
            }
        }
    }
}
