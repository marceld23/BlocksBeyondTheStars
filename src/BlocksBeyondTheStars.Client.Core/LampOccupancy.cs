// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A box of world cells: min inclusive, max exclusive on every axis.</summary>
    public readonly struct CellBox
    {
        public CellBox(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
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

        /// <summary>Number of cells inside (0 for an empty box).</summary>
        public long Volume => (long)Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY) * Math.Max(0, MaxZ - MinZ);
    }

    /// <summary>
    /// #3233: the occupancy volume behind the High preset's headlamp shadows — a 64³ byte grid of the blocks around the
    /// player (0 = open … 255 = an opaque cube), Unity-free so the headless tests pin it. The cells are stored with WRAP
    /// addressing (a world cell lands at its coordinates mod 64), the same way the shader samples the 3D texture, so
    /// moving the window only refills the slabs that came into it. The window snaps in steps of
    /// <see cref="Snap"/> cells, keeping the player at least 28 cells from every face — the lamp's reach is 30 m, the
    /// little beyond reads as open. The Unity side (<c>LampVolume</c>) uploads <see cref="Cells"/> into the 3D texture.
    /// </summary>
    public sealed class LampOccupancy
    {
        /// <summary>Cells per side of the volume (and of the 3D texture).</summary>
        public const int Size = 64;

        /// <summary>The window moves in steps of this many cells.</summary>
        public const int Snap = 8;

        /// <summary>The window's low face sits this many cells below the snapped centre.</summary>
        private const int Lead = 28;

        /// <summary>Side of one refresh brick (<see cref="Brick"/>): the volume holds (64 / 16)³ = 64 of them.</summary>
        public const int BrickSize = 16;

        /// <summary>Number of refresh bricks.</summary>
        public const int BrickCount = (Size / BrickSize) * (Size / BrickSize) * (Size / BrickSize);

        /// <summary>The occupancy, x fastest, then y, then z — the layout of a Unity Texture3D.</summary>
        public byte[] Cells { get; } = new byte[Size * Size * Size];

        /// <summary>False until the first <see cref="Recenter"/>.</summary>
        public bool Placed { get; private set; }

        /// <summary>The window's lowest world cell on each axis.</summary>
        public int OriginX { get; private set; }

        public int OriginY { get; private set; }

        public int OriginZ { get; private set; }

        /// <summary>The window origin on one axis for a centre cell there.</summary>
        public static int OriginFor(int center) => (FloorDiv(center, Snap) * Snap) - Lead;

        /// <summary>The index of world cell (x, y, z) in <see cref="Cells"/> (wrap addressing).</summary>
        public static int Index(int x, int y, int z)
            => (x & (Size - 1)) + ((y & (Size - 1)) * Size) + ((z & (Size - 1)) * Size * Size);

        /// <summary>
        /// Moves the window so the cell (<paramref name="cx"/>, <paramref name="cy"/>, <paramref name="cz"/>) sits well
        /// inside it. The boxes of world cells that came into the window go into <paramref name="dirty"/> (the whole
        /// window the first time or after a jump); they must be filled before the volume is used. False when it stayed.
        /// </summary>
        public bool Recenter(int cx, int cy, int cz, List<CellBox> dirty)
        {
            int ox = OriginFor(cx), oy = OriginFor(cy), oz = OriginFor(cz);
            if (Placed && ox == OriginX && oy == OriginY && oz == OriginZ)
            {
                return false;
            }

            var next = new CellBox(ox, oy, oz, ox + Size, oy + Size, oz + Size);
            bool jump = !Placed || Math.Abs(ox - OriginX) >= Size || Math.Abs(oy - OriginY) >= Size || Math.Abs(oz - OriginZ) >= Size;
            if (jump)
            {
                dirty.Add(next);
            }
            else
            {
                AddEnteringSlabs(next, OriginX, OriginY, OriginZ, dirty);
            }

            OriginX = ox;
            OriginY = oy;
            OriginZ = oz;
            Placed = true;
            return true;
        }

        /// <summary>The cells of <paramref name="next"/> outside the old window on each axis (overlapping corners are
        /// listed twice — refilling a cell twice is harmless).</summary>
        private static void AddEnteringSlabs(CellBox next, int oldX, int oldY, int oldZ, List<CellBox> dirty)
        {
            if (next.MinX < oldX)
            {
                dirty.Add(new CellBox(next.MinX, next.MinY, next.MinZ, oldX, next.MaxY, next.MaxZ));
            }
            else if (next.MinX > oldX)
            {
                dirty.Add(new CellBox(oldX + Size, next.MinY, next.MinZ, next.MaxX, next.MaxY, next.MaxZ));
            }

            if (next.MinY < oldY)
            {
                dirty.Add(new CellBox(next.MinX, next.MinY, next.MinZ, next.MaxX, oldY, next.MaxZ));
            }
            else if (next.MinY > oldY)
            {
                dirty.Add(new CellBox(next.MinX, oldY + Size, next.MinZ, next.MaxX, next.MaxY, next.MaxZ));
            }

            if (next.MinZ < oldZ)
            {
                dirty.Add(new CellBox(next.MinX, next.MinY, next.MinZ, next.MaxX, next.MaxY, oldZ));
            }
            else if (next.MinZ > oldZ)
            {
                dirty.Add(new CellBox(next.MinX, next.MinY, oldZ + Size, next.MaxX, next.MaxY, next.MaxZ));
            }
        }

        /// <summary>The <paramref name="brick"/>-th 16³ brick of the current window (0 .. <see cref="BrickCount"/> − 1,
        /// wrapped) — the round-robin refresh that picks up streamed chunks and block changes within about a second.</summary>
        public CellBox Brick(int brick)
        {
            const int perSide = Size / BrickSize;
            int b = ((brick % BrickCount) + BrickCount) % BrickCount;
            int bx = b % perSide, by = (b / perSide) % perSide, bz = b / (perSide * perSide);
            int x = OriginX + (bx * BrickSize), y = OriginY + (by * BrickSize), z = OriginZ + (bz * BrickSize);
            return new CellBox(x, y, z, x + BrickSize, y + BrickSize, z + BrickSize);
        }

        /// <summary>
        /// Fills <paramref name="box"/> from <paramref name="world"/>: <paramref name="opacity"/> maps a block id and its
        /// packed shape (0 = plain cube) to 0..255; air and cells of chunks not streamed yet are open. Reads chunk by
        /// chunk (one lookup per chunk, not per cell). True when any stored cell changed (the texture needs an upload).
        /// </summary>
        public bool Fill(CellBox box, ClientWorld world, Func<ushort, int, byte> opacity)
        {
            const int cs = WorldConstants.ChunkSize;
            bool changed = false;
            for (int cz = FloorDiv(box.MinZ, cs); cz <= FloorDiv(box.MaxZ - 1, cs); cz++)
            {
                for (int cy = FloorDiv(box.MinY, cs); cy <= FloorDiv(box.MaxY - 1, cs); cy++)
                {
                    for (int cx = FloorDiv(box.MinX, cs); cx <= FloorDiv(box.MaxX - 1, cs); cx++)
                    {
                        world.TryGetChunk(new ChunkCoord(cx, cy, cz), out var chunk);
                        var part = new CellBox(
                            Math.Max(box.MinX, cx * cs), Math.Max(box.MinY, cy * cs), Math.Max(box.MinZ, cz * cs),
                            Math.Min(box.MaxX, (cx * cs) + cs), Math.Min(box.MaxY, (cy * cs) + cs), Math.Min(box.MaxZ, (cz * cs) + cs));
                        changed |= FillChunkPart(part, chunk, cx * cs, cy * cs, cz * cs, opacity);
                    }
                }
            }

            return changed;
        }

        /// <summary>One chunk's share of a fill; <paramref name="chunk"/> null = not streamed (open).</summary>
        private bool FillChunkPart(CellBox part, ChunkData? chunk, int baseX, int baseY, int baseZ, Func<ushort, int, byte> opacity)
        {
            bool changed = false;
            for (int z = part.MinZ; z < part.MaxZ; z++)
            {
                for (int y = part.MinY; y < part.MaxY; y++)
                {
                    for (int x = part.MinX; x < part.MaxX; x++)
                    {
                        byte v = 0;
                        if (chunk != null)
                        {
                            int lx = x - baseX, ly = y - baseY, lz = z - baseZ;
                            var id = chunk.Get(lx, ly, lz);
                            if (!id.IsAir)
                            {
                                v = opacity(id.Value, chunk.GetShape(lx, ly, lz));
                            }
                        }

                        int i = Index(x, y, z);
                        if (Cells[i] != v)
                        {
                            Cells[i] = v;
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -(((-a) + b - 1) / b);
    }
}
