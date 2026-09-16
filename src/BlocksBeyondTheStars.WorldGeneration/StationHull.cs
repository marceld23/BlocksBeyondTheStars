// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Where a ship docks with a station (#1917): the centre of the hangar mouth on the hull's outer face, in
/// structure cell units (cell (x,y,z) spans x..x+1), and the horizontal direction pointing OUT of the mouth.</summary>
public readonly struct StationDock
{
    public readonly float X, Y, Z;
    public readonly int OutX, OutZ;

    /// <summary>True when the dock is a real force-field hangar mouth; false for the fallback (the outer wall nearest to
    /// the hangar marker, or the middle of the station's -Z face when it has no hangar at all).</summary>
    public readonly bool IsMouth;

    public StationDock(float x, float y, float z, int outX, int outZ, bool isMouth)
    {
        X = x;
        Y = y;
        Z = z;
        OutX = outX;
        OutZ = outZ;
        IsMouth = isMouth;
    }
}

/// <summary>
/// What a station looks like from outside (#1917). Generated stations fly as their real voxel hull 1:1, like player
/// stations — but a colossal station holds tens of thousands of interior cells nobody in flight can see.
/// <see cref="VisibleCells"/> keeps what a pilot can see: every block that touches outside space, plus what shows through a
/// window or the hangar mouth a short way in. <see cref="FindDock"/> finds the hangar mouth the ship flies into.
/// </summary>
public static class StationHull
{
    /// <summary>How far (in cells) the view reaches into the station through a window or a see-through mouth.</summary>
    public const int WindowDepth = 12;

    private static readonly Vector3i[] Steps =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>The see-through block ids of the content (glass, force fields …) — see <see cref="StationKitExterior.SeeThroughKeys"/>.</summary>
    public static HashSet<ushort> SeeThroughIds(GameContent content)
    {
        var ids = new HashSet<ushort>();
        foreach (var key in StationKitExterior.SeeThroughKeys)
        {
            if (content.GetBlock(key) is { } def)
            {
                ids.Add(def.NumericId.Value);
            }
        }

        return ids;
    }

    /// <summary>Outside space of a structure: the air connected to a one-cell padding around its box.</summary>
    private sealed class Outside
    {
        private readonly bool[] _cells;
        private readonly int _w, _h, _l, _ph, _pl;

        public Outside(StationStructure s)
        {
            _w = s.Width;
            _h = s.Height;
            _l = s.Length;
            _ph = _h + 2;
            _pl = _l + 2;
            _cells = new bool[(_w + 2) * _ph * _pl];
            var queue = new Queue<Vector3i>();
            _cells[Idx(-1, -1, -1)] = true;
            queue.Enqueue(new Vector3i(-1, -1, -1));
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Steps)
                {
                    var n = p + d;
                    if (InPad(n.X, n.Y, n.Z) && !_cells[Idx(n.X, n.Y, n.Z)] && (!s.InBounds(n.X, n.Y, n.Z) || s.Get(n.X, n.Y, n.Z) == 0))
                    {
                        _cells[Idx(n.X, n.Y, n.Z)] = true;
                        queue.Enqueue(n);
                    }
                }
            }
        }

        public int Size => _cells.Length;

        public int Idx(int x, int y, int z) => ((x + 1) * _ph + (y + 1)) * _pl + (z + 1);

        public bool InPad(int x, int y, int z) => x >= -1 && y >= -1 && z >= -1 && x <= _w && y <= _h && z <= _l;

        public bool Is(int x, int y, int z) => InPad(x, y, z) && _cells[Idx(x, y, z)];
    }

    /// <summary>
    /// Every non-air cell a pilot can see from outside: the cells next to outside space, plus — through see-through cells
    /// (glass, force fields) and shaped cells (slabs, stairs, furniture) — the cells next to the air up to
    /// <see cref="WindowDepth"/> steps inside. Returned in a fixed x/y/z order.
    /// </summary>
    public static List<Vector3i> VisibleCells(StationStructure s, GameContent content)
    {
        var seeThrough = SeeThroughIds(content);
        var outside = new Outside(s);
        int w = s.Width, h = s.Height, l = s.Length;
        bool Passable(int x, int y, int z)
        {
            ushort b = s.Get(x, y, z);
            return b != 0 && (seeThrough.Contains(b) || s.GetShape(x, y, z) != 0);
        }

        // The view through see-through and shaped cells, with a depth budget that shrinks with every step inside.
        var budget = new sbyte[outside.Size];
        var inside = new Queue<Vector3i>();
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    if (!Passable(x, y, z))
                    {
                        continue;
                    }

                    foreach (var d in Steps)
                    {
                        if (outside.Is(x + d.X, y + d.Y, z + d.Z))
                        {
                            budget[outside.Idx(x, y, z)] = WindowDepth;
                            inside.Enqueue(new Vector3i(x, y, z));
                            break;
                        }
                    }
                }

        while (inside.Count > 0)
        {
            var p = inside.Dequeue();
            int next = budget[outside.Idx(p.X, p.Y, p.Z)] - 1;
            if (next <= 0)
            {
                continue;
            }

            foreach (var d in Steps)
            {
                var n = p + d;
                if (!s.InBounds(n.X, n.Y, n.Z) || outside.Is(n.X, n.Y, n.Z) || budget[outside.Idx(n.X, n.Y, n.Z)] >= next)
                {
                    continue;
                }

                if (s.Get(n.X, n.Y, n.Z) == 0 || Passable(n.X, n.Y, n.Z))
                {
                    budget[outside.Idx(n.X, n.Y, n.Z)] = (sbyte)next;
                    inside.Enqueue(n);
                }
            }
        }

        bool Seen(int x, int y, int z) => outside.Is(x, y, z) || (s.InBounds(x, y, z) && budget[outside.Idx(x, y, z)] > 0);

        var cells = new List<Vector3i>();
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    if (s.Get(x, y, z) == 0)
                    {
                        continue;
                    }

                    bool visible = budget[outside.Idx(x, y, z)] > 0;
                    for (int i = 0; i < Steps.Length && !visible; i++)
                    {
                        visible = Seen(x + Steps[i].X, y + Steps[i].Y, z + Steps[i].Z);
                    }

                    if (visible)
                    {
                        cells.Add(new Vector3i(x, y, z));
                    }
                }

        return cells;
    }

    /// <summary>
    /// The hangar mouth: the force-field wall cell nearest to the <c>hangar</c> marker that has outside space in front of
    /// it and open air behind it; its connected force-field patch gives the mouth's centre. Without a force-field mouth,
    /// the outer wall nearest to the marker along ±X / ±Z; without a hangar marker, the middle of the station's -Z face.
    /// </summary>
    public static StationDock FindDock(StationStructure s, GameContent content)
    {
        Vector3i? hangar = null;
        foreach (var m in s.Markers)
        {
            if (m.Type == "hangar")
            {
                hangar = m.LocalPos;
                break;
            }
        }

        if (hangar is not { } marker)
        {
            return new StationDock(s.Width / 2f, s.Height / 2f, 0f, 0, -1, isMouth: false);
        }

        var outside = new Outside(s);
        var dirs = new (int X, int Z)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        ushort field = content.GetBlock("force_field")?.NumericId.Value ?? 0;
        (Vector3i Cell, int X, int Z, long Dist)? best = null;
        if (field != 0)
        {
            for (int x = 0; x < s.Width; x++)
                for (int y = 0; y < s.Height; y++)
                    for (int z = 0; z < s.Length; z++)
                    {
                        if (s.Get(x, y, z) != field)
                        {
                            continue;
                        }

                        foreach (var (dx, dz) in dirs)
                        {
                            int bx = x - dx, bz = z - dz;
                            if (!outside.Is(x + dx, y, z + dz) || !s.InBounds(bx, y, bz) || s.Get(bx, y, bz) != 0 || outside.Is(bx, y, bz))
                            {
                                continue; // not a mouth: no space in front, or no room behind
                            }

                            long dist = (long)(x - marker.X) * (x - marker.X) + (long)(y - marker.Y) * (y - marker.Y) * 4 + (long)(z - marker.Z) * (z - marker.Z);
                            if (best is null || dist < best.Value.Dist)
                            {
                                best = (new Vector3i(x, y, z), dx, dz, dist);
                            }
                        }
                    }
        }

        if (best is { } mouth)
        {
            return MouthOf(s, mouth.Cell, mouth.X, mouth.Z, field, outside);
        }

        // No force-field mouth: the nearest outer wall straight out from the marker.
        (Vector3i Wall, int X, int Z, int Steps)? wall = null;
        foreach (var (dx, dz) in dirs)
        {
            for (int step = 1; step <= 128; step++)
            {
                int x = marker.X + dx * step, z = marker.Z + dz * step;
                if (!s.InBounds(x, marker.Y, z))
                {
                    break;
                }

                if (s.Get(x, marker.Y, z) != 0 && outside.Is(x + dx, marker.Y, z + dz))
                {
                    if (wall is null || step < wall.Value.Steps)
                    {
                        wall = (new Vector3i(x, marker.Y, z), dx, dz, step);
                    }

                    break;
                }
            }
        }

        if (wall is { } wf)
        {
            float y = wf.Wall.Y + 1f;
            return wf.X != 0
                ? new StationDock(wf.X > 0 ? wf.Wall.X + 1f : wf.Wall.X, y, wf.Wall.Z + 0.5f, wf.X, 0, isMouth: false)
                : new StationDock(wf.Wall.X + 0.5f, y, wf.Z > 0 ? wf.Wall.Z + 1f : wf.Wall.Z, 0, wf.Z, isMouth: false);
        }

        return new StationDock(marker.X + 0.5f, marker.Y + 1f, 0f, 0, -1, isMouth: false);
    }

    /// <summary>The centre of the force-field patch (cells with outside space in front) in the wall plane of <paramref name="cell"/>.</summary>
    private static StationDock MouthOf(StationStructure s, Vector3i cell, int dx, int dz, ushort field, Outside outside)
    {
        var seen = new HashSet<Vector3i> { cell };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(cell);
        int aMin = int.MaxValue, aMax = int.MinValue, yMin = int.MaxValue, yMax = int.MinValue;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int a = dx != 0 ? p.Z : p.X;
            aMin = Math.Min(aMin, a); aMax = Math.Max(aMax, a);
            yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
            foreach (var n in dx != 0
                ? new[] { new Vector3i(p.X, p.Y + 1, p.Z), new Vector3i(p.X, p.Y - 1, p.Z), new Vector3i(p.X, p.Y, p.Z + 1), new Vector3i(p.X, p.Y, p.Z - 1) }
                : new[] { new Vector3i(p.X, p.Y + 1, p.Z), new Vector3i(p.X, p.Y - 1, p.Z), new Vector3i(p.X + 1, p.Y, p.Z), new Vector3i(p.X - 1, p.Y, p.Z) })
            {
                if (s.InBounds(n.X, n.Y, n.Z) && s.Get(n.X, n.Y, n.Z) == field && outside.Is(n.X + dx, n.Y, n.Z + dz) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        float along = (aMin + aMax + 1) / 2f;
        float y = (yMin + yMax + 1) / 2f;
        return dx != 0
            ? new StationDock(dx > 0 ? cell.X + 1f : cell.X, y, along, dx, 0, isMouth: true)
            : new StationDock(along, y, dz > 0 ? cell.Z + 1f : cell.Z, 0, dz, isMouth: true);
    }
}
