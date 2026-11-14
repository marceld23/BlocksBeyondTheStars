// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The markers of a giant wreck after the ruin pass (#3609): which survive, where they rest, and the boarding arrival.</summary>
public static partial class GiantShipGenerator
{
    /// <summary>
    /// After the ruin pass, in place: only the <paramref name="kept"/> types stay; a doorway stays where the author put it (the
    /// derelict decay reads the rooms from it), and so does an entrance that still stands. On a planet every other marker comes
    /// to rest on what is left (<see cref="RuinMarkerRules.Seat(VoxelGrid, Vector3i, int)"/>, the landmark rule). In
    /// <paramref name="space"/> nothing falls — there is no ground to fall to: a marker stays where it is while it can (a sound
    /// while its cell is air, anything else while it stands). A marker that needs a floor (a chest, a loot cache, a den, an
    /// entrance) and has none after that — its floor fell, into space or the rock, its cell got filled — and a sound whose cell
    /// got filled move to the nearest free cell with a floor under it and a roof somewhere above it (any floor when the wreck has
    /// no roofed one; in space never above the topmost block left), never onto a cell another marker already took. Returns the
    /// footing it judged by.
    /// </summary>
    internal static Footing SettleMarkers(VoxelGrid grid, List<SettlementMarker> markers, int groundRow, IReadOnlyList<string> kept,
        GameContent content, bool space = false)
    {
        RuinMarkerRules.KeepOnly(markers, kept);
        var footing = new Footing(grid, groundRow, content, space);
        var taken = new HashSet<int>();
        var moved = new List<(int Index, Vector3i From)>();
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m.Type == DoorMarker)
            {
                continue;
            }

            // An entrance keeps its deck: it does not drop through a hole in its floor, it moves to the nearest standable
            // cell (most often beside the hole); on a planet everything else rests on what is left, as in a landmark ruin.
            bool seat = !space && m.Type != GiantShipRules.EntranceMarker;
            var at = seat ? RuinMarkerRules.Seat(grid, m.LocalPos, groundRow) : m.LocalPos;
            if (NeedsFloor(m.Type) ? !footing.Standable(at) : space && !footing.IsAir(at))
            {
                moved.Add((i, m.LocalPos)); // placed after every marker that stands has claimed its cell
            }
            else
            {
                footing.Claim(taken, at);
            }

            if (!at.Equals(m.LocalPos))
            {
                markers[i] = new SettlementMarker(m.Type, at, m.DoorAxis, m.Data);
            }
        }

        foreach (var (i, from) in moved)
        {
            var m = markers[i];
            if (footing.Nearest(from, taken) is { } spot)
            {
                footing.Claim(taken, spot);
                markers[i] = new SettlementMarker(m.Type, spot, m.DoorAxis, m.Data);
            }
        }

        return footing;
    }

    /// <summary>Whether a marker of this type needs a floor under it (a crate stands, a visitor arrives, animals live there).</summary>
    private static bool NeedsFloor(string type)
        => type != LandmarkRules.SoundMarker && type != DoorMarker;

    /// <summary>The boarding arrival: the first entrance that survived; without one the standable cell nearest the middle of the
    /// top (a wreck always has a way aboard); with no standable cell at all, the middle of the top row.</summary>
    private static Vector3i EntranceOf(List<SettlementMarker> markers, Footing footing)
    {
        foreach (var m in markers)
        {
            if (m.Type == GiantShipRules.EntranceMarker)
            {
                return m.LocalPos;
            }
        }

        var top = new Vector3i(footing.Grid.W / 2, footing.Grid.H - 1, footing.Grid.L / 2);
        return footing.Nearest(top, new HashSet<int>()) ?? top;
    }

    /// <summary>Where a marker can stand in a ruined grid: its cell and the one above free, a floor under it.</summary>
    internal sealed class Footing
    {
        private readonly int _groundRow;
        private readonly GameContent _content;
        private readonly bool _space;
        private readonly Dictionary<ushort, bool> _solid = new();
        private List<int>? _spots;

        public Footing(VoxelGrid grid, int groundRow, GameContent content, bool space = false)
        {
            Grid = grid;
            _groundRow = groundRow;
            _content = content;
            _space = space;
        }

        public VoxelGrid Grid { get; }

        /// <summary>Whether the cell lies in the grid and holds no block (a sound needs nothing more).</summary>
        public bool IsAir(Vector3i p) => Grid.In(p.X, p.Y, p.Z) && Grid.Blocks[Grid.Index(p.X, p.Y, p.Z)] == 0;

        /// <summary>Whether a cell is free to stand in: no block, and not in the rock (below the ground row only a cavity is
        /// air). Above the grid is open.</summary>
        private bool Free(int x, int y, int z)
        {
            if (y >= Grid.H)
            {
                return Grid.In(x, Grid.H - 1, z);
            }

            if (!Grid.In(x, y, z))
            {
                return false;
            }

            int i = Grid.Index(x, y, z);
            return Grid.Blocks[i] == 0 && (y >= _groundRow || Grid.Cavity[i]);
        }

        /// <summary>Whether a cell carries what stands on it: a solid block, or the rock below the ground row (below the grid's
        /// bottom row too when that row is buried). Water and plants carry nothing.</summary>
        private bool Floor(int x, int y, int z)
        {
            if (y < 0)
            {
                return _groundRow > 0;
            }

            int i = Grid.Index(x, y, z);
            ushort b = Grid.Blocks[i];
            return b != 0 ? IsSolid(b) : y < _groundRow && !Grid.Cavity[i];
        }

        private bool IsSolid(ushort id)
        {
            if (!_solid.TryGetValue(id, out bool solid))
            {
                solid = _content.BlockById(new Shared.Primitives.BlockId(id)) is { Solid: true };
                _solid[id] = solid;
            }

            return solid;
        }

        /// <summary>Whether something can stand at <paramref name="p"/>: the cell and the one above free, a floor under it.</summary>
        public bool Standable(Vector3i p)
            => Grid.In(p.X, p.Y, p.Z) && Free(p.X, p.Y, p.Z) && Free(p.X, p.Y + 1, p.Z) && Floor(p.X, p.Y - 1, p.Z);

        /// <summary>Marks the cell and the one above as taken by a marker.</summary>
        public void Claim(HashSet<int> taken, Vector3i p)
        {
            if (Grid.In(p.X, p.Y, p.Z))
            {
                taken.Add(Grid.Index(p.X, p.Y, p.Z));
            }

            if (Grid.In(p.X, p.Y + 1, p.Z))
            {
                taken.Add(Grid.Index(p.X, p.Y + 1, p.Z));
            }
        }

        /// <summary>The standable cell nearest <paramref name="from"/> (squared distance, the first in cell order on a tie) that
        /// is not taken — roofed cells only, unless the grid has none; null when nothing is standable.</summary>
        public Vector3i? Nearest(Vector3i from, HashSet<int> taken)
        {
            _spots ??= Spots();
            int layer = Grid.H * Grid.L;
            long bestD = long.MaxValue;
            Vector3i? best = null;
            foreach (int i in _spots)
            {
                int x = i / layer, y = (i % layer) / Grid.L, z = i % Grid.L;
                if (taken.Contains(i) || (Grid.In(x, y + 1, z) && taken.Contains(Grid.Index(x, y + 1, z))))
                {
                    continue;
                }

                long dx = x - from.X, dy = y - from.Y, dz = z - from.Z, d = (dx * dx) + (dy * dy) + (dz * dz);
                if (d < bestD)
                {
                    (bestD, best) = (d, new Vector3i(x, y, z));
                }
            }

            return best;
        }

        /// <summary>Every standable cell with a block somewhere above it (inside the hull), in cell order — or every standable
        /// cell when none is roofed (in space only those below the topmost block left: never outside the wreck's box). A
        /// standable cell always has a block under it, so it never lies below the wreck either. One pass over the grid.</summary>
        private List<int> Spots()
        {
            var top = new int[Grid.W * Grid.L]; // per column: the highest row holding a block, -1 for none
            int highest = -1;
            for (int x = 0; x < Grid.W; x++)
            {
                for (int z = 0; z < Grid.L; z++)
                {
                    int y = Grid.H - 1;
                    while (y >= 0 && Grid.Blocks[Grid.Index(x, y, z)] == 0)
                    {
                        y--;
                    }

                    top[(x * Grid.L) + z] = y;
                    highest = System.Math.Max(highest, y);
                }
            }

            var roofed = new List<int>();
            var open = new List<int>();
            for (int i = 0; i < Grid.Blocks.Length; i++)
            {
                int layer = Grid.H * Grid.L, x = i / layer, y = (i % layer) / Grid.L, z = i % Grid.L;
                if (Standable(new Vector3i(x, y, z)))
                {
                    if (top[(x * Grid.L) + z] > y + 1)
                    {
                        roofed.Add(i);
                    }
                    else if (!_space || y < highest)
                    {
                        open.Add(i);
                    }
                }
            }

            return roofed.Count > 0 ? roofed : open;
        }
    }
}
