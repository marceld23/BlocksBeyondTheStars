// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3497: the FAR SILHOUETTE of a landmark — the stamped layout sampled down to one cell per
/// <see cref="Step"/>³ blocks, from the ground row up. Beyond the streamed chunks a build is drawn only as a height
/// blob per 8- or 32-block cell, so a tower, a gate and a bridge all look like a bump; the client draws this model
/// there instead and they can be told apart.
/// <para>A model cell is filled when at least <see cref="MinBlocks"/> of its blocks are solid (a one-block wall across
/// the cell counts, a stray stone does not) and takes the block — with its dye — that most of them are. Cells index
/// like the layout: <c>(x * Height + y) * Length + z</c>; 0 = empty, else a 1-based palette index.</para>
/// </summary>
public sealed class LandmarkSilhouette
{
    /// <summary>Blocks per model cell along every axis.</summary>
    public const int DefaultStep = 4;

    /// <summary>The fewest solid blocks that fill a model cell.</summary>
    public const int MinBlocks = 3;

    private LandmarkSilhouette(int step, int w, int h, int l, byte[] cells, ushort[] blocks, int[] tints)
    {
        Step = step;
        Width = w;
        Height = h;
        Length = l;
        Cells = cells;
        Blocks = blocks;
        Tints = tints;
    }

    public int Step { get; }

    public int Width { get; }

    public int Height { get; }

    public int Length { get; }

    public byte[] Cells { get; }

    /// <summary>The palette's blocks …</summary>
    public ushort[] Blocks { get; }

    /// <summary>… and their dyes (0xRRGGBB, 0 = none), parallel.</summary>
    public int[] Tints { get; }

    /// <summary>The palette index (1-based) of a model cell, 0 when it is empty or outside.</summary>
    public int At(int x, int y, int z)
        => x < 0 || y < 0 || z < 0 || x >= Width || y >= Height || z >= Length ? 0 : Cells[(((x * Height) + y) * Length) + z];

    /// <summary>Samples <paramref name="layout"/> down. The model starts at the layout's ground row: what lies in the
    /// rock is never seen from afar.</summary>
    public static LandmarkSilhouette Build(LandmarkLayout layout, int step = DefaultStep)
    {
        var s = layout.Structure;
        step = System.Math.Max(1, step);
        int g = layout.GroundRow;
        int w = (s.Width + step - 1) / step, h = (s.Height - g + step - 1) / step, l = (s.Length + step - 1) / step;
        var cells = new byte[w * h * l];
        var palette = new Dictionary<(ushort Block, int Tint), int>();
        var blocks = new List<ushort>();
        var tints = new List<int>();
        var tally = new Dictionary<(ushort Block, int Tint), int>();
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    var most = Dominant(s, x * step, g + (y * step), z * step, step, tally);
                    if (most is null)
                    {
                        continue;
                    }

                    if (!palette.TryGetValue(most.Value, out int index))
                    {
                        if (blocks.Count >= byte.MaxValue)
                        {
                            index = 1; // a 256th material: drawn as the first one rather than dropped
                        }
                        else
                        {
                            blocks.Add(most.Value.Block);
                            tints.Add(most.Value.Tint);
                            index = blocks.Count;
                            palette[most.Value] = index;
                        }
                    }

                    cells[(((x * h) + y) * l) + z] = (byte)index;
                }
            }
        }

        return new LandmarkSilhouette(step, w, h, l, cells, blocks.ToArray(), tints.ToArray());
    }

    /// <summary>
    /// Samples a landmark given as its non-air cells (terrain generation 39: the cells of its structure overlay) — the same
    /// model <see cref="Build"/> makes of a layout with exactly these blocks. Cells are in model-space blocks: X in
    /// [0, <paramref name="width"/>), Y counted from the ground row in [0, <paramref name="height"/>), Z in
    /// [0, <paramref name="length"/>), each cell at most once (an overlay holds a cell once); a cell outside that box and an
    /// air cell are ignored.
    /// </summary>
    public static LandmarkSilhouette FromCells(int width, int height, int length,
        IEnumerable<(int X, int Y, int Z, ushort Block, int Tint)> cells, int step = DefaultStep)
    {
        step = System.Math.Max(1, step);
        int w = (width + step - 1) / step, h = (height + step - 1) / step, l = (length + step - 1) / step;

        // One key per cell: model cell, block, dye — sorted, a model cell's cells lie together, its materials in ascending
        // (block, dye) order, which is Dominant's tie rule.
        var keys = new List<(long Model, ushort Block, int Tint)>();
        foreach (var (x, y, z, block, tint) in cells)
        {
            if (block == 0 || x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= length)
            {
                continue;
            }

            keys.Add(((((long)(x / step) * h) + (y / step)) * l + (z / step), block, tint));
        }

        keys.Sort();
        var model = new byte[w * h * l];
        var palette = new Dictionary<(ushort Block, int Tint), int>();
        var blocks = new List<ushort>();
        var tints = new List<int>();
        for (int i = 0; i < keys.Count;)
        {
            long cell = keys[i].Model;
            int solid = 0, bestCount = 0;
            (ushort Block, int Tint) best = default;
            while (i < keys.Count && keys[i].Model == cell)
            {
                var material = (keys[i].Block, keys[i].Tint);
                int run = 0;
                while (i < keys.Count && keys[i].Model == cell && (keys[i].Block, keys[i].Tint) == material)
                {
                    run++;
                    i++;
                }

                solid += run;
                if (run > bestCount)
                {
                    (best, bestCount) = (material, run);
                }
            }

            if (solid >= MinBlocks)
            {
                model[cell] = (byte)PaletteIndex(best, palette, blocks, tints);
            }
        }

        return new LandmarkSilhouette(step, w, h, l, model, blocks.ToArray(), tints.ToArray());
    }

    /// <summary>The 1-based palette index of a material, added on first use (a 256th material reads as the first).</summary>
    private static int PaletteIndex((ushort Block, int Tint) material, Dictionary<(ushort Block, int Tint), int> palette,
        List<ushort> blocks, List<int> tints)
    {
        if (palette.TryGetValue(material, out int index))
        {
            return index;
        }

        if (blocks.Count >= byte.MaxValue)
        {
            return 1;
        }

        blocks.Add(material.Block);
        tints.Add(material.Tint);
        palette[material] = blocks.Count;
        return blocks.Count;
    }

    /// <summary>The block (with its dye) most of the solid blocks of one model cell are — null when fewer than
    /// <see cref="MinBlocks"/> are solid.</summary>
    private static (ushort Block, int Tint)? Dominant(SettlementStructure s, int x0, int y0, int z0, int step,
        Dictionary<(ushort Block, int Tint), int> tally)
    {
        tally.Clear();
        int solid = 0;
        for (int x = x0; x < x0 + step && x < s.Width; x++)
        {
            for (int y = y0; y < y0 + step && y < s.Height; y++)
            {
                for (int z = z0; z < z0 + step && z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b == 0)
                    {
                        continue;
                    }

                    solid++;
                    var key = (b, s.GetModifier(x, y, z).Tint);
                    tally[key] = tally.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
        }

        if (solid < MinBlocks)
        {
            return null;
        }

        (ushort Block, int Tint) best = default;
        int bestCount = 0;
        foreach (var kv in tally)
        {
            // Ties break on the lower block id and tint, so the model never depends on dictionary order.
            if (kv.Value > bestCount || (kv.Value == bestCount && (kv.Key.Block < best.Block || (kv.Key.Block == best.Block && kv.Key.Tint < best.Tint))))
            {
                best = kv.Key;
                bestCount = kv.Value;
            }
        }

        return best;
    }
}
