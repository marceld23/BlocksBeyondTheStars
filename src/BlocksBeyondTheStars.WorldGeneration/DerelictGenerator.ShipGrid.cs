// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

public static partial class DerelictGenerator
{
    /// <summary>
    /// The decay grid's SHIP mode (#3614): what the derelict decay needs to work on a giant wreck — a grid read from a
    /// <see cref="GiantSpaceLayout"/>, and flood fills, labels and masks over flat arrays with index arithmetic (no cell sets,
    /// no per-cell allocation: a giant holds a million cells and more). The station paths never call any of this.
    /// </summary>
    private sealed partial class Grid
    {
        /// <summary>A room label: never visited (and never a room — a block, the outside, a doorway).</summary>
        private const ushort Unvisited = 0;

        /// <summary>A room label: visited, but a pocket too small to be a room.</summary>
        private const ushort NoRoom = ushort.MaxValue;

        /// <summary>A working copy of a giant wreck in space (same index order as the layout: <c>(x*H+y)*L+z</c>).</summary>
        public Grid(GiantSpaceLayout g)
        {
            W = g.Width;
            H = g.Height;
            L = g.Length;
            _b = (ushort[])g.Blocks.Clone();
            foreach (var kv in g.Mods)
            {
                _mods[kv.Key] = kv.Value;
            }

            foreach (var kv in g.Shapes)
            {
                _shapes[kv.Key] = kv.Value;
            }
        }

        public int Size => _b.Length;

        public bool InBounds(Vector3i c) => InBounds(c.X, c.Y, c.Z);

        public ushort Get(Vector3i c) => Get(c.X, c.Y, c.Z);

        public ushort BlockAt(int i) => _b[i];

        public int ShapeAt(int i) => _shapes.TryGetValue(i, out int s) ? s : 0;

        public (int Tint, int Glow) ModAt(int i) => _mods.TryGetValue(i, out var m) ? m : (0, 0);

        public Vector3i CellOf(int i) => new(i / (H * L), (i / L) % H, i % L);

        /// <summary>The face neighbour of cell <paramref name="i"/> in direction <paramref name="dir"/> (0..5: +x, −x, +y, −y,
        /// +z, −z), or −1 outside the grid.</summary>
        public int Step(int i, int dir)
        {
            int hl = H * L;
            return dir switch
            {
                0 => i / hl < W - 1 ? i + hl : -1,
                1 => i / hl > 0 ? i - hl : -1,
                2 => (i / L) % H < H - 1 ? i + L : -1,
                3 => (i / L) % H > 0 ? i - L : -1,
                4 => i % L < L - 1 ? i + 1 : -1,
                _ => i % L > 0 ? i - 1 : -1,
            };
        }

        /// <summary>A grid-sized mask with the given cells set (cells outside the grid are ignored).</summary>
        public bool[] Mask(IEnumerable<Vector3i> cells)
        {
            var mask = new bool[_b.Length];
            foreach (var c in cells)
            {
                if (InBounds(c))
                {
                    mask[Idx(c.X, c.Y, c.Z)] = true;
                }
            }

            return mask;
        }

        /// <summary>Sets a cell and its six neighbours in a mask.</summary>
        public void MarkAround(bool[] mask, Vector3i c)
        {
            if (InBounds(c))
            {
                MarkAround(mask, Idx(c.X, c.Y, c.Z));
                return;
            }

            foreach (var n in Neighbours6(c))
            {
                if (InBounds(n))
                {
                    mask[Idx(n.X, n.Y, n.Z)] = true;
                }
            }
        }

        /// <summary>Sets cell <paramref name="i"/> and its face neighbours in a mask.</summary>
        public void MarkAround(bool[] mask, int i)
        {
            mask[i] = true;
            for (int dir = 0; dir < 6; dir++)
            {
                int n = Step(i, dir);
                if (n >= 0)
                {
                    mask[n] = true;
                }
            }
        }

        /// <summary>Every cell of <paramref name="block"/> becomes air (the dead force fields of a wreck).</summary>
        public void RemoveAll(ushort block)
        {
            if (block == 0)
            {
                return;
            }

            for (int i = 0; i < _b.Length; i++)
            {
                if (_b[i] == block)
                {
                    _b[i] = 0;
                    _mods.Remove(i);
                    _shapes.Remove(i);
                }
            }
        }

        /// <summary>The space around the wreck: air reachable from the grid's faces without passing a doorway cell
        /// (<paramref name="wall"/>) — a doorway parts two rooms even while it stands open, so the rooms behind the doorways
        /// stay rooms, and what the open way in (the entrance) and the ruin's holes lead into is outside.</summary>
        public bool[] FloodOutside(bool[] wall)
        {
            var ext = new bool[_b.Length];
            var q = new Queue<int>();
            for (int i = 0; i < _b.Length; i++)
            {
                var c = CellOf(i);
                bool edge = c.X == 0 || c.Y == 0 || c.Z == 0 || c.X == W - 1 || c.Y == H - 1 || c.Z == L - 1;
                if (edge && _b[i] == 0 && !wall[i])
                {
                    ext[i] = true;
                    q.Enqueue(i);
                }
            }

            while (q.Count > 0)
            {
                int i = q.Dequeue();
                for (int dir = 0; dir < 6; dir++)
                {
                    int n = Step(i, dir);
                    if (n >= 0 && !ext[n] && _b[n] == 0 && !wall[n])
                    {
                        ext[n] = true;
                        q.Enqueue(n);
                    }
                }
            }

            return ext;
        }

        /// <summary>Labels the inside air into rooms (doorway cells are walls): label <c>r + 1</c> for room r; pockets smaller
        /// than a room (and any past <see cref="MaxShipRooms"/>) are labelled <see cref="NoRoom"/>. One reused index buffer for
        /// the pocket being filled — no cell list is kept per room, only its size and the sums of its coordinates.</summary>
        public ushort[] LabelShipRooms(bool[] exterior, bool[] wall, List<ShipRoom> rooms)
        {
            var label = new ushort[_b.Length];
            var pocket = new List<int>();
            var q = new Queue<int>();
            for (int i = 0; i < _b.Length; i++)
            {
                if (label[i] != Unvisited || exterior[i] || _b[i] != 0 || wall[i])
                {
                    continue;
                }

                pocket.Clear();
                label[i] = NoRoom;
                q.Enqueue(i);
                while (q.Count > 0)
                {
                    int p = q.Dequeue();
                    pocket.Add(p);
                    for (int dir = 0; dir < 6; dir++)
                    {
                        int n = Step(p, dir);
                        if (n >= 0 && label[n] == Unvisited && !exterior[n] && _b[n] == 0 && !wall[n])
                        {
                            label[n] = NoRoom;
                            q.Enqueue(n);
                        }
                    }
                }

                if (pocket.Count >= MinRoomCells && rooms.Count < MaxShipRooms)
                {
                    rooms.Add(LabelRoom(label, pocket, (ushort)(rooms.Count + 1)));
                }
            }

            return label;
        }

        private ShipRoom LabelRoom(ushort[] label, List<int> pocket, ushort id)
        {
            long sx = 0, sy = 0, sz = 0;
            foreach (int p in pocket)
            {
                label[p] = id;
                var c = CellOf(p);
                sx += c.X;
                sy += c.Y;
                sz += c.Z;
            }

            return new ShipRoom(pocket.Count, sx, sy, sz);
        }

        /// <summary>The room of cell <paramref name="i"/>, or −1 (a block, the outside, a doorway, a small pocket).</summary>
        public int RoomOf(ushort[] label, int i)
        {
            ushort v = label[i];
            return v == Unvisited || v == NoRoom ? -1 : v - 1;
        }

        /// <summary>The room a doorway's side cell belongs to: its own label, else the one of the cell above it (something
        /// stands in front of the doorway), else −1 — never a guess across a wall.</summary>
        public int ShipRoomAt(ushort[] label, Vector3i c)
        {
            if (InBounds(c) && RoomOf(label, Idx(c.X, c.Y, c.Z)) is int r and >= 0)
            {
                return r;
            }

            var up = new Vector3i(c.X, c.Y + 1, c.Z);
            return InBounds(up) ? RoomOf(label, Idx(up.X, up.Y, up.Z)) : -1;
        }

        /// <summary>Whether cell <paramref name="i"/> touches the outside (an exterior cell or the grid's edge).</summary>
        public bool TouchesOutside(bool[] exterior, int i)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                int n = Step(i, dir);
                if (n < 0 || exterior[n])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Tears a rough hole around <paramref name="centre"/> — never through a protected cell or a block that does not
        /// tear (a ladder, a plant). Returns how many cells it opened.</summary>
        public int ShipCarve(Vector3i centre, double radius, bool[] protect, HashSet<ushort> noCarve, Random rng)
        {
            int reach = (int)Math.Ceiling(radius), opened = 0;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        var c = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                        double r = radius - rng.NextDouble() * 0.7;
                        bool inside = (dx * dx) + (dy * dy) + (dz * dz) <= r * r || c == centre;
                        if (inside && InBounds(c) && Tearable(Idx(c.X, c.Y, c.Z), protect, noCarve))
                        {
                            Set(c.X, c.Y, c.Z, 0);
                            opened++;
                        }
                    }

            return opened;
        }

        public bool Tearable(int i, bool[] protect, HashSet<ushort> noCarve) => _b[i] != 0 && !protect[i] && !noCarve.Contains(_b[i]);

        /// <summary>Sets a block and keeps the cell's shape (a light switched back on keeps its mount).</summary>
        public void SetKeepShape(int i, ushort block)
        {
            int shape = ShapeAt(i);
            var c = CellOf(i);
            Set(c.X, c.Y, c.Z, block);
            SetShape(c.X, c.Y, c.Z, shape);
        }

        /// <summary>The wreck's <see cref="GiantDerelictLayout.BaseHash"/>: every cell by block KEY, every dye, glow and shape,
        /// the doors, the arrival and the guardian bay.</summary>
        public string ContentHash(GameContent content, List<DerelictDoor> doors, Vector3i arrival, Vector3i? bay)
        {
            var keyHash = new Dictionary<ushort, ulong>();
            foreach (var def in content.Blocks.Values)
            {
                keyHash[def.NumericId.Value] = StableHashing.Fnv64(def.Key);
            }

            ulong h = StableHashing.Mix64(((ulong)(uint)W << 40) ^ ((ulong)(uint)H << 20) ^ (uint)L);
            for (int i = 0; i < _b.Length; i++)
            {
                h = StableHashing.Mix64(h ^ (_b[i] == 0 ? 0UL : keyHash.TryGetValue(_b[i], out ulong k) ? k : _b[i]));
            }

            foreach (var kv in Sorted(_mods))
            {
                h = StableHashing.Mix64(h ^ (ulong)(uint)kv.Key ^ ((ulong)(uint)kv.Value.Tint << 24) ^ ((ulong)(uint)kv.Value.Glow << 40));
            }

            foreach (var kv in Sorted(_shapes))
            {
                h = StableHashing.Mix64(h ^ (ulong)(uint)kv.Key ^ ((ulong)(uint)kv.Value << 32));
            }

            foreach (var d in doors)
            {
                h = StableHashing.Mix64(h ^ (ulong)(uint)Idx(d.Cell.X, d.Cell.Y, d.Cell.Z) ^ (d.AxisX ? 1UL << 40 : 0UL));
            }

            h = StableHashing.Mix64(h ^ (ulong)(uint)Idx(arrival.X, arrival.Y, arrival.Z));
            h = StableHashing.Mix64(h ^ (bay is { } b ? (ulong)(uint)Idx(b.X, b.Y, b.Z) + 1UL : 0UL));
            return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static List<KeyValuePair<int, T>> Sorted<T>(Dictionary<int, T> d)
        {
            var list = new List<KeyValuePair<int, T>>(d);
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }
    }
}
