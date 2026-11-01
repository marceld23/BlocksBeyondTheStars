// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a room scan finds in a cell.</summary>
    public enum RoomCell
    {
        /// <summary>Air the room continues through.</summary>
        Open,

        /// <summary>Anything that is not air — a wall, a window, a door, furniture.</summary>
        Wall,

        /// <summary>Not streamed yet: the room cannot be judged.</summary>
        Unknown,
    }

    /// <summary>
    /// The room the camera stands in, for the High preset's room reflections (#3057): a capped flood-fill of open cells
    /// from the camera cell. A closed room — the ship's cabin, a sealed base room — yields its axis-aligned box; an open
    /// door, a cave mouth or the outdoors overflows the caps and yields nothing, so the reflection simply fades out.
    /// The server sends no room shapes (only air counts), so the client measures the room it can see. Unity-free.
    /// </summary>
    public static class RoomBounds
    {
        /// <summary>The most open cells a room may have (a 16 × 16 × 16 hall).</summary>
        public const int MaxCells = 4096;

        /// <summary>The longest a room may be along any axis, in blocks.</summary>
        public const int MaxExtent = 40;

        private static readonly (int X, int Y, int Z)[] Steps = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

        /// <summary>Scans the room around the start cell. True with the room's cell box (inclusive cell indices) when the
        /// open cells close up within the caps; false when the start is not open, a cell is unknown, or the space leaks.</summary>
        public static bool TryFind(int sx, int sy, int sz, Func<int, int, int, RoomCell> cellAt, out RoomBox box)
        {
            box = default;
            if (cellAt == null || cellAt(sx, sy, sz) != RoomCell.Open)
            {
                return false;
            }

            var seen = new HashSet<(int, int, int)> { (sx, sy, sz) };
            var queue = new Queue<(int X, int Y, int Z)>();
            queue.Enqueue((sx, sy, sz));
            int minX = sx, minY = sy, minZ = sz, maxX = sx, maxY = sy, maxZ = sz;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var s in Steps)
                {
                    int x = c.X + s.X, y = c.Y + s.Y, z = c.Z + s.Z;
                    if (seen.Contains((x, y, z)))
                    {
                        continue;
                    }

                    var kind = cellAt(x, y, z);
                    if (kind == RoomCell.Unknown)
                    {
                        return false;
                    }

                    if (kind == RoomCell.Wall)
                    {
                        continue;
                    }

                    seen.Add((x, y, z));
                    if (seen.Count > MaxCells)
                    {
                        return false;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    minZ = Math.Min(minZ, z);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    maxZ = Math.Max(maxZ, z);
                    if (maxX - minX >= MaxExtent || maxY - minY >= MaxExtent || maxZ - minZ >= MaxExtent)
                    {
                        return false;
                    }

                    queue.Enqueue((x, y, z));
                }
            }

            box = new RoomBox(minX, minY, minZ, maxX, maxY, maxZ);
            return true;
        }

        /// <summary>The open space around the start cell within <paramref name="radius"/> cells on every axis — for a room
        /// whose door stands open (the closed scan leaks out of it): the box of the open cells the fill reaches inside that
        /// cube. False when the start is not open or a cell inside is unknown.</summary>
        public static bool TryFindNear(int sx, int sy, int sz, int radius, Func<int, int, int, RoomCell> cellAt, out RoomBox box)
        {
            return TryFind(sx, sy, sz, (x, y, z) =>
            {
                if (Math.Abs(x - sx) > radius || Math.Abs(y - sy) > radius || Math.Abs(z - sz) > radius)
                {
                    return RoomCell.Wall; // the edge of the search cube closes the room
                }

                return cellAt(x, y, z);
            }, out box);
        }
    }

    /// <summary>A room's open cells as an inclusive cell box; the world-space box runs from Min to Max + 1.</summary>
    public readonly struct RoomBox : IEquatable<RoomBox>
    {
        public RoomBox(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        public int MinX { get; }
        public int MinY { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxY { get; }
        public int MaxZ { get; }

        /// <summary>Whether a cell lies in the box grown by <paramref name="margin"/> cells on every side.</summary>
        public bool Contains(int x, int y, int z, int margin = 0)
            => x >= MinX - margin && x <= MaxX + margin && y >= MinY - margin && y <= MaxY + margin
               && z >= MinZ - margin && z <= MaxZ + margin;

        public bool Equals(RoomBox other)
            => MinX == other.MinX && MinY == other.MinY && MinZ == other.MinZ
               && MaxX == other.MaxX && MaxY == other.MaxY && MaxZ == other.MaxZ;

        public override bool Equals(object? obj) => obj is RoomBox other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(MinX, MinY, MinZ, MaxX, MaxY, MaxZ);

        public static bool operator ==(RoomBox a, RoomBox b) => a.Equals(b);

        public static bool operator !=(RoomBox a, RoomBox b) => !a.Equals(b);
    }
}
