// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Chunk visibility culling (#1823) — Minecraft's section visibility graph. Each meshed chunk records which of its
    /// six faces are connected through non-opaque cells (<see cref="ComputeConnectivity"/>); a breadth-first walk from
    /// the camera's chunk crosses only connected faces and never steps back toward the camera
    /// (<see cref="Walk"/>). A chunk the walk never reaches cannot be seen, whatever the frustum says: underground, the
    /// rock between the player and the surface hides almost everything. Pure CPU, so it works in the browser build.
    /// </summary>
    public static class ChunkVisibility
    {
        /// <summary>Face indices: −X, +X, −Y, +Y, −Z, +Z.</summary>
        public const int NegX = 0, PosX = 1, NegY = 2, PosY = 3, NegZ = 4, PosZ = 5;

        /// <summary>Every pair connected — an all-open (or never-meshed) chunk.</summary>
        public const ushort AllConnected = 0x7FFF;

        private static readonly int[] PairBit = BuildPairBits();

        private static int[] BuildPairBits()
        {
            var bits = new int[36];
            int bit = 0;
            for (int a = 0; a < 6; a++)
                for (int b = a + 1; b < 6; b++)
                {
                    bits[a * 6 + b] = bit;
                    bits[b * 6 + a] = bit;
                    bit++;
                }

            return bits;
        }

        /// <summary>Whether faces <paramref name="a"/> and <paramref name="b"/> connect in a connectivity mask.</summary>
        public static bool Connects(ushort mask, int a, int b) => a == b || (mask & (1 << PairBit[a * 6 + b])) != 0;

        public static int Opposite(int face) => face ^ 1;

        /// <summary>Flood-fills the 16³ cells of a chunk and records which faces each open region touches.</summary>
        /// <param name="isOpen">Whether a local cell (x, y, z in 0..15) lets sight through (air, glass, fluid, flora, shapes).</param>
        public static ushort ComputeConnectivity(Func<int, int, int, bool> isOpen)
        {
            const int S = WorldConstants.ChunkSize;
            var visited = new bool[S * S * S];
            var queue = new int[S * S * S];
            ushort mask = 0;
            for (int start = 0; start < visited.Length; start++)
            {
                if (visited[start])
                {
                    continue;
                }

                int sx = start % S, sy = (start / S) % S, sz = start / (S * S);
                visited[start] = true;
                if (!isOpen(sx, sy, sz))
                {
                    continue;
                }

                int faces = 0, head = 0, tail = 0;
                queue[tail++] = start;
                while (head < tail)
                {
                    int c = queue[head++];
                    int x = c % S, y = (c / S) % S, z = c / (S * S);
                    if (x == 0) faces |= 1 << NegX;
                    if (x == S - 1) faces |= 1 << PosX;
                    if (y == 0) faces |= 1 << NegY;
                    if (y == S - 1) faces |= 1 << PosY;
                    if (z == 0) faces |= 1 << NegZ;
                    if (z == S - 1) faces |= 1 << PosZ;

                    Visit(x - 1, y, z); Visit(x + 1, y, z);
                    Visit(x, y - 1, z); Visit(x, y + 1, z);
                    Visit(x, y, z - 1); Visit(x, y, z + 1);
                }

                for (int a = 0; a < 6; a++)
                    for (int b = a + 1; b < 6; b++)
                    {
                        if ((faces & (1 << a)) != 0 && (faces & (1 << b)) != 0)
                        {
                            mask |= (ushort)(1 << PairBit[a * 6 + b]);
                        }
                    }

                void Visit(int x, int y, int z)
                {
                    if ((uint)x >= S || (uint)y >= S || (uint)z >= S)
                    {
                        return;
                    }

                    int i = x + y * S + z * S * S;
                    if (visited[i])
                    {
                        return;
                    }

                    visited[i] = true;
                    if (isOpen(x, y, z))
                    {
                        queue[tail++] = i;
                    }
                }
            }

            return mask;
        }

        /// <summary>
        /// Walks from the camera's chunk and adds every chunk that may be visible to <paramref name="visible"/>.
        /// </summary>
        /// <param name="camera">The chunk holding the camera (raw, unwrapped coordinates).</param>
        /// <param name="connectivityOf">The connectivity of a chunk (raw coordinates — the caller wraps), or null
        /// when the chunk is not loaded: the walk does not continue through it (nothing there can be drawn, and nothing
        /// behind it is streamed). A loaded chunk not meshed yet should report <see cref="AllConnected"/>.</param>
        /// <param name="horizontalRadius">Chebyshev radius (chunks) the walk may reach.</param>
        /// <param name="verticalRadius">How many chunks up / down the walk may reach.</param>
        public static void Walk(ChunkCoord camera, Func<ChunkCoord, ushort?> connectivityOf, int horizontalRadius,
            int verticalRadius, ICollection<ChunkCoord> visible)
        {
            var seen = new HashSet<ChunkCoord> { camera };
            visible.Add(camera);
            var queue = new Queue<(ChunkCoord Coord, int Entry, int Dirs)>();
            // The camera's own chunk connects to every face (the camera can stand anywhere inside it).
            for (int d = 0; d < 6; d++)
            {
                Step(camera, d, 1 << d);
            }

            while (queue.Count > 0)
            {
                var (coord, entry, dirs) = queue.Dequeue();
                ushort? conn = connectivityOf(coord);
                if (conn is null)
                {
                    continue;
                }

                for (int d = 0; d < 6; d++)
                {
                    if ((dirs & (1 << Opposite(d))) != 0 || !Connects(conn.Value, entry, d))
                    {
                        continue; // never back toward the camera; only through an open passage
                    }

                    Step(coord, d, dirs | (1 << d));
                }
            }

            void Step(ChunkCoord from, int dir, int dirs)
            {
                var next = dir switch
                {
                    NegX => new ChunkCoord(from.X - 1, from.Y, from.Z),
                    PosX => new ChunkCoord(from.X + 1, from.Y, from.Z),
                    NegY => new ChunkCoord(from.X, from.Y - 1, from.Z),
                    PosY => new ChunkCoord(from.X, from.Y + 1, from.Z),
                    NegZ => new ChunkCoord(from.X, from.Y, from.Z - 1),
                    _ => new ChunkCoord(from.X, from.Y, from.Z + 1),
                };

                if (Math.Abs(next.X - camera.X) > horizontalRadius || Math.Abs(next.Z - camera.Z) > horizontalRadius
                    || Math.Abs(next.Y - camera.Y) > verticalRadius || !seen.Add(next))
                {
                    return;
                }

                if (connectivityOf(next) is null)
                {
                    return; // not loaded: nothing to show there, and the walk does not tunnel through
                }

                visible.Add(next);
                queue.Enqueue((next, Opposite(dir), dirs));
            }
        }
    }

    /// <summary>
    /// The client's chunk-build order (#1818): nearest first within the player's own surroundings, then weighted toward
    /// where the camera looks and where the player is heading — the chunks a jetpack flight is about to reach are built
    /// before the ones it leaves behind. Offsets and look-ahead in blocks.
    /// </summary>
    public static class ChunkBuildPriority
    {
        /// <summary>Horizontal radius (blocks) within which plain distance decides — the player's footing and walls.</summary>
        public const float NearRadius = 24f;

        public static float Key(float dx, float dy, float dz, float forwardX, float forwardZ, float aheadX, float aheadZ)
        {
            float horizSq = dx * dx + dz * dz;
            float plain = horizSq + dy * dy;
            if (horizSq <= NearRadius * NearRadius)
            {
                return plain;
            }

            float ax = dx - aheadX, az = dz - aheadZ;
            float len = (float)Math.Sqrt(horizSq);
            float facing = (dx * forwardX + dz * forwardZ) / len;
            float factor = 1f + 0.5f * (1f - Math.Max(-1f, Math.Min(1f, facing)));
            return 1_000_000f + (ax * ax + dy * dy + az * az) * factor;
        }
    }
}
