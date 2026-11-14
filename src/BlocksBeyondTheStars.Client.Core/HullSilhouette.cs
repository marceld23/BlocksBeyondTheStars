// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3615: the far look of a big station hull in the flight view — a giant wreck is up to 256 blocks a side, thousands of
/// chunk meshes when drawn block by block. From afar the flight view draws this instead: a coarse occupancy model (one cell
/// per <see cref="Step"/>³ blocks, coloured by the block most of its blocks are), meshed by the landmarks' far-silhouette
/// mesher (<see cref="LandmarkSilhouetteMesh"/>). Up close the real chunks take over.
/// <para>Hollow is skin only: an empty model cell that open space cannot reach from outside (a hall inside the hull) is
/// filled, so the mesh carries only the outside skin — the halls' walls would never be seen from afar anyway.</para>
/// <para>Unity-free and built from the design message's own arrays (never changed after arrival), so the flight view can run
/// it on a worker thread while the frame goes on.</para>
/// </summary>
public sealed class HullSilhouette
{
    /// <summary>Blocks per model cell along each axis for a hull of up to <see cref="MaxCellsPerAxis"/> × 4 blocks.</summary>
    public const int DefaultStep = 4;

    /// <summary>The most model cells along an axis — a bigger structure takes a larger step instead.</summary>
    public const int MaxCellsPerAxis = 96;

    private HullSilhouette(NetLandmark model, LandmarkSilhouetteMesh mesh, int minX, int minY, int minZ)
    {
        Model = model;
        Mesh = mesh;
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
    }

    /// <summary>The coarse model (palette in <see cref="NetLandmark.Blocks"/> / <see cref="NetLandmark.Tints"/>).</summary>
    public NetLandmark Model { get; }

    /// <summary>The skin mesh; positions in blocks from the model's corner.</summary>
    public LandmarkSilhouetteMesh Mesh { get; }

    /// <summary>Blocks per model cell.</summary>
    public int Step => Model.Step;

    /// <summary>The design cell of the model's (0, 0, 0) corner.</summary>
    public int MinX { get; }

    public int MinY { get; }

    public int MinZ { get; }

    /// <summary>Builds the silhouette of the cells (<paramref name="xs"/>, <paramref name="ys"/>, <paramref name="zs"/>,
    /// <paramref name="blocks"/>; <paramref name="tints"/> one per cell or empty). Air cells are skipped; mismatched arrays
    /// are read up to the shortest. An empty design gives an empty model and mesh.</summary>
    public static HullSilhouette Build(int[] xs, int[] ys, int[] zs, ushort[] blocks, int[]? tints, int step = DefaultStep)
    {
        int n = Math.Min(Math.Min(xs?.Length ?? 0, ys?.Length ?? 0), Math.Min(zs?.Length ?? 0, blocks?.Length ?? 0));
        bool tinted = tints is not null && tints.Length >= n;
        if (!Bounds(xs!, ys!, zs!, blocks!, n, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ))
        {
            var empty = new NetLandmark { Step = Math.Max(1, step) };
            return new HullSilhouette(empty, LandmarkSilhouetteMesh.Build(empty), 0, 0, 0);
        }

        int extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) + 1;
        step = Math.Max(Math.Max(1, step), (extent + MaxCellsPerAxis - 1) / MaxCellsPerAxis);
        var model = new NetLandmark
        {
            Step = step,
            Width = ((maxX - minX) / step) + 1,
            Height = ((maxY - minY) / step) + 1,
            Length = ((maxZ - minZ) / step) + 1,
        };

        var palette = new Palette();
        var cells = Majority(xs!, ys!, zs!, blocks!, tinted ? tints : null, n, model, minX, minY, minZ, palette);
        FillHollows(cells, model.Width, model.Height, model.Length);
        model.Cells = cells;
        model.Blocks = palette.Blocks.ToArray();
        model.Tints = palette.Tints.ToArray();
        return new HullSilhouette(model, LandmarkSilhouetteMesh.Build(model), minX, minY, minZ);
    }

    private static bool Bounds(int[] xs, int[] ys, int[] zs, ushort[] blocks, int n,
        out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ)
    {
        minX = minY = minZ = int.MaxValue;
        maxX = maxY = maxZ = int.MinValue;
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            if (blocks[i] == 0)
            {
                continue;
            }

            any = true;
            minX = Math.Min(minX, xs[i]);
            minY = Math.Min(minY, ys[i]);
            minZ = Math.Min(minZ, zs[i]);
            maxX = Math.Max(maxX, xs[i]);
            maxY = Math.Max(maxY, ys[i]);
            maxZ = Math.Max(maxZ, zs[i]);
        }

        return any;
    }

    /// <summary>The model cells: per cell the palette index (1-based) of the block most of its blocks are, 0 = empty.</summary>
    private static byte[] Majority(int[] xs, int[] ys, int[] zs, ushort[] blocks, int[]? tints, int n, NetLandmark model,
        int minX, int minY, int minZ, Palette palette)
    {
        int h = model.Height, l = model.Length, step = model.Step;
        var cells = new byte[model.Width * h * l];
        var best = new int[cells.Length];
        var counts = new Dictionary<long, int>();
        for (int i = 0; i < n; i++)
        {
            if (blocks[i] == 0)
            {
                continue;
            }

            int cell = ((((xs[i] - minX) / step * h) + ((ys[i] - minY) / step)) * l) + ((zs[i] - minZ) / step);
            byte index = palette.IndexOf(blocks[i], tints?[i] ?? 0);
            long key = ((long)cell << 8) | index;
            counts.TryGetValue(key, out int count);
            counts[key] = ++count;
            if (count > best[cell])
            {
                best[cell] = count;
                cells[cell] = index;
            }
        }

        return cells;
    }

    /// <summary>Fills every empty cell that open space cannot reach from outside the model (a flood over the empty cells
    /// from the model's border), so only the outside skin is meshed.</summary>
    private static void FillHollows(byte[] cells, int w, int h, int l)
    {
        var outside = new bool[cells.Length];
        var queue = new Queue<int>();
        int Index(int x, int y, int z) => (((x * h) + y) * l) + z;
        void Visit(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l)
            {
                return;
            }

            int i = Index(x, y, z);
            if (cells[i] == 0 && !outside[i])
            {
                outside[i] = true;
                queue.Enqueue(i);
            }
        }

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    if (x == 0 || y == 0 || z == 0 || x == w - 1 || y == h - 1 || z == l - 1)
                    {
                        Visit(x, y, z);
                    }
                }
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int z = i % l, y = i / l % h, x = i / l / h;
            Visit(x + 1, y, z);
            Visit(x - 1, y, z);
            Visit(x, y + 1, z);
            Visit(x, y - 1, z);
            Visit(x, y, z + 1);
            Visit(x, y, z - 1);
        }

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == 0 && !outside[i])
            {
                cells[i] = 1; // never seen: any colour will do
            }
        }
    }

    /// <summary>The model's palette: one entry per (block, dye), at most 255 (the cells are bytes) — a further pair reads as
    /// the first entry of its block, else the first entry.</summary>
    private sealed class Palette
    {
        private readonly Dictionary<long, byte> _byPair = new();
        private readonly Dictionary<ushort, byte> _byBlock = new();

        public List<ushort> Blocks { get; } = new();

        public List<int> Tints { get; } = new();

        public byte IndexOf(ushort block, int tint)
        {
            long key = ((long)block << 32) | (uint)tint;
            if (_byPair.TryGetValue(key, out byte index))
            {
                return index;
            }

            if (Blocks.Count >= byte.MaxValue)
            {
                return _byBlock.TryGetValue(block, out byte same) ? same : (byte)1;
            }

            Blocks.Add(block);
            Tints.Add(tint);
            index = (byte)Blocks.Count;
            _byPair[key] = index;
            if (!_byBlock.ContainsKey(block))
            {
                _byBlock[block] = index;
            }

            return index;
        }
    }
}
