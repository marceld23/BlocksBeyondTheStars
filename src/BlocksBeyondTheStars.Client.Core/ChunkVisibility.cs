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

        [ThreadStatic]
        private static bool[]? _visitedScratch;

        [ThreadStatic]
        private static int[]? _queueScratch;

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
            // Per-thread scratch: every chunk build on every mesh worker runs this, so no per-call garbage.
            var visited = _visitedScratch ??= new bool[S * S * S];
            var queue = _queueScratch ??= new int[S * S * S];
            Array.Clear(visited, 0, visited.Length);
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

        /// <summary>What the walk finds at a chunk: a loaded chunk answers its connectivity mask (0..<see cref="AllConnected"/>),
        /// an unloaded chunk answers <see cref="Blocked"/> (rock, or nothing streamed there — the walk stops) or
        /// <see cref="Passable"/> (open air above a column's streamed band: the walk passes through without drawing
        /// anything). The far columns stream only a band around their surface, so the air between the camera's
        /// altitude and a distant ridge is usually NOT loaded — without the pass-through a canopy or a roof would hide
        /// every distant hill.</summary>
        public const int Blocked = -1;
        public const int Passable = -2;

        [ThreadStatic]
        private static byte[]? _seenScratch;

        [ThreadStatic]
        private static int[]? _walkQueue;

        /// <summary>
        /// Walks from the camera's chunk and adds every loaded chunk that may be visible to <paramref name="visible"/>.
        /// Dense bookkeeping over the walk box (no per-node allocation): at radius 24 / 12 chunk layers that is ~60k cells.
        /// </summary>
        /// <param name="camera">The chunk holding the camera (raw, unwrapped coordinates).</param>
        /// <param name="classify">See <see cref="Blocked"/> / <see cref="Passable"/>; raw coordinates — the caller wraps.
        /// A loaded chunk not meshed yet should report <see cref="AllConnected"/>.</param>
        /// <param name="horizontalRadius">Chebyshev radius (chunks) the walk may reach.</param>
        /// <param name="minChunkY">Lowest chunk Y the walk visits (inclusive).</param>
        /// <param name="maxChunkY">Highest chunk Y the walk visits (inclusive) — one above the highest loaded chunk is
        /// enough: everything higher is open air a path could take just as well one layer lower.</param>
        public static void Walk(ChunkCoord camera, Func<ChunkCoord, int> classify, int horizontalRadius,
            int minChunkY, int maxChunkY, ICollection<ChunkCoord> visible)
        {
            minChunkY = Math.Min(minChunkY, camera.Y);
            maxChunkY = Math.Max(maxChunkY, camera.Y);
            int side = 2 * horizontalRadius + 1;
            int height = maxChunkY - minChunkY + 1;
            int cells = side * side * height;
            var seen = _seenScratch;
            if (seen == null || seen.Length < cells)
            {
                seen = _seenScratch = new byte[cells];
            }
            else
            {
                Array.Clear(seen, 0, cells);
            }

            // Queue entries: cell index, entry face, direction bits (packed: index << 9 | dirs << 3 | entry).
            var queue = _walkQueue;
            if (queue == null || queue.Length < cells)
            {
                queue = _walkQueue = new int[cells];
            }

            int head = 0, tail = 0;
            int Index(int x, int y, int z) => ((z - camera.Z + horizontalRadius) * height + (y - minChunkY)) * side + (x - camera.X + horizontalRadius);
            seen[Index(camera.X, camera.Y, camera.Z)] = 1;
            visible.Add(camera);
            // The camera's own chunk connects to every face (the camera can stand anywhere inside it).
            for (int d = 0; d < 6; d++)
            {
                Step(camera.X, camera.Y, camera.Z, d, 1 << d);
            }

            while (head < tail)
            {
                int packed = queue[head++];
                int index = packed >> 9;
                int dirs = (packed >> 3) & 0x3F;
                int entry = packed & 7;
                int x = index % side + camera.X - horizontalRadius;
                int rest = index / side;
                int y = rest % height + minChunkY;
                int z = rest / height + camera.Z - horizontalRadius;
                int kind = classify(new ChunkCoord(x, y, z));
                if (kind == Blocked)
                {
                    continue;
                }

                ushort conn = kind == Passable ? AllConnected : (ushort)kind;
                for (int d = 0; d < 6; d++)
                {
                    if ((dirs & (1 << Opposite(d))) != 0 || !Connects(conn, entry, d))
                    {
                        continue; // never back toward the camera; only through an open passage
                    }

                    Step(x, y, z, d, dirs | (1 << d));
                }
            }

            void Step(int fx, int fy, int fz, int dir, int dirs)
            {
                int nx = fx, ny = fy, nz = fz;
                switch (dir)
                {
                    case NegX: nx--; break;
                    case PosX: nx++; break;
                    case NegY: ny--; break;
                    case PosY: ny++; break;
                    case NegZ: nz--; break;
                    default: nz++; break;
                }

                if (Math.Abs(nx - camera.X) > horizontalRadius || Math.Abs(nz - camera.Z) > horizontalRadius
                    || ny < minChunkY || ny > maxChunkY)
                {
                    return;
                }

                int index = Index(nx, ny, nz);
                if (seen[index] != 0)
                {
                    return;
                }

                seen[index] = 1;
                int kind = classify(new ChunkCoord(nx, ny, nz));
                if (kind == Blocked)
                {
                    return; // rock, or nothing streamed there: nothing to show, and the walk does not tunnel through
                }

                if (kind != Passable)
                {
                    visible.Add(new ChunkCoord(nx, ny, nz)); // a loaded chunk the camera can reach
                }

                queue[tail++] = (index << 9) | (dirs << 3) | Opposite(dir);
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
