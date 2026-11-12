// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>#3521: what a layout wreck carries beyond the block arrays of the box wreck — the current and the intact dye,
/// glow and shape per cell (sparse, keyed by the cell index), the inside of its hull and the layout offset.</summary>
internal sealed class WreckCellExtras
{
    public WreckCellExtras(Vector3i offset, BitArray interior, Dictionary<int, (int Tint, int Glow)> mods, Dictionary<int, int> shapes,
        Dictionary<int, (int Tint, int Glow)> intactMods, Dictionary<int, int> intactShapes)
    {
        Offset = offset;
        Interior = interior;
        Mods = mods;
        Shapes = shapes;
        IntactMods = intactMods;
        IntactShapes = intactShapes;
    }

    public Vector3i Offset { get; }

    public BitArray Interior { get; }

    public Dictionary<int, (int Tint, int Glow)> Mods { get; }

    public Dictionary<int, int> Shapes { get; }

    public Dictionary<int, (int Tint, int Glow)> IntactMods { get; }

    public Dictionary<int, int> IntactShapes { get; }
}

/// <summary>
/// #3521: the intact hull of a ship layout as a wreck's repair mask — the cells exactly as the flyable ship is built from
/// them (<see cref="ShipCellBlocks.Map"/> plus the floor guarantee), shifted so the lowest corner is (0, 0, 0). Two rules
/// make it a mask a player can finish: a block no item places (<c>carbon</c>, the <c>data_cache</c> console) becomes the
/// hull, and an alien hull is crystal. Hatches and doors stay open — the way in.
/// </summary>
internal sealed class WreckLayoutHull
{
    private WreckLayoutHull(int w, int h, int l, Vector3i offset)
    {
        W = w;
        H = h;
        L = l;
        Offset = offset;
        Intact = new ushort[w * h * l];
        Interior = new BitArray(w * h * l);
    }

    public int W { get; }

    public int H { get; }

    public int L { get; }

    /// <summary>The layout cell of local (0, 0, 0).</summary>
    public Vector3i Offset { get; }

    public ushort[] Intact { get; }

    public Dictionary<int, (int Tint, int Glow)> Mods { get; } = new();

    public Dictionary<int, int> Shapes { get; } = new();

    /// <summary>Air of the intact hull between its floor and its roof, inside the columns the hull encloses.</summary>
    public BitArray Interior { get; }

    /// <summary>The station tiles (type, local cell) in layout order.</summary>
    public List<(string Type, Vector3i Cell)> Stations { get; } = new();

    /// <summary>The hatches and doors (local cells) in layout order.</summary>
    public List<Vector3i> Doorways { get; } = new();

    /// <summary>The local row of the layout's floor (layout row 0).</summary>
    public int FloorRow => -Offset.Y;

    public int Index(int x, int y, int z) => (((x * H) + y) * L) + z;

    public bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L;

    public ushort IntactAt(int x, int y, int z) => In(x, y, z) ? Intact[Index(x, y, z)] : (ushort)0;

    /// <summary>The lowest and the highest layout cell — the floor guarantee included (it can only add row 0).</summary>
    public static (Vector3i Min, Vector3i Max) Bounds(ShipLayout layout)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var c in layout.Cells)
        {
            minX = System.Math.Min(minX, c.X);
            minY = System.Math.Min(minY, c.Y);
            minZ = System.Math.Min(minZ, c.Z);
            maxX = System.Math.Max(maxX, c.X);
            maxY = System.Math.Max(maxY, c.Y);
            maxZ = System.Math.Max(maxZ, c.Z);
        }

        if (minX == int.MaxValue)
        {
            return (new Vector3i(0, 0, 0), new Vector3i(0, 0, 0));
        }

        if (ShipCellBlocks.FloorFootprint(layout).Count > 0)
        {
            minY = System.Math.Min(minY, 0);
            maxY = System.Math.Max(maxY, 0);
        }

        return (new Vector3i(minX, minY, minZ), new Vector3i(maxX, maxY, maxZ));
    }

    public static WreckLayoutHull Build(ShipLayout layout, GameContent content, bool alien)
    {
        var palette = ShipCellPalette.From(content);
        var cells = new Dictionary<Vector3i, (ushort Block, int Tint, int Glow, int Shape)>();
        var stations = new List<(string Type, Vector3i Cell)>();
        var doorways = new List<Vector3i>();
        MapCells(layout, content, palette, cells, stations, doorways);

        var footprint = ShipCellBlocks.FloorFootprint(layout);
        for (int fx = 0; fx < layout.Width; fx++)
        {
            for (int fz = 0; fz < layout.Length; fz++)
            {
                var fp = new Vector3i(fx, 0, fz);
                if (footprint.Contains((fx, fz)) && (!cells.TryGetValue(fp, out var at) || at.Block == 0))
                {
                    cells[fp] = (palette.Wall.Value, 0, 0, 0); // the floor guarantee of the flyable ship
                }
            }
        }

        var (min, max) = Bounds(layout);
        var hull = new WreckLayoutHull(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1, min);
        hull.Fill(cells, RepairSafe(content, palette, alien));
        hull.MarkInterior(layout, footprint);
        foreach (var (type, cell) in stations)
        {
            hull.Stations.Add((type, hull.Local(cell)));
        }

        foreach (var cell in doorways)
        {
            hull.Doorways.Add(hull.Local(cell));
        }

        return hull;
    }

    private Vector3i Local(Vector3i layoutCell) => new(layoutCell.X - Offset.X, layoutCell.Y - Offset.Y, layoutCell.Z - Offset.Z);

    /// <summary>Maps every layout cell in file order (a later cell on the same spot wins, as in the flyable ship).</summary>
    private static void MapCells(ShipLayout layout, GameContent content, ShipCellPalette palette,
        Dictionary<Vector3i, (ushort Block, int Tint, int Glow, int Shape)> cells, List<(string Type, Vector3i Cell)> stations,
        List<Vector3i> doorways)
    {
        foreach (var cell in layout.Cells)
        {
            var p = new Vector3i(cell.X, cell.Y, cell.Z);
            var mapped = ShipCellBlocks.Map(cell, palette, content);
            switch (mapped.Role)
            {
                case ShipCellRole.Opening:
                case ShipCellRole.Door:
                    doorways.Add(p);
                    continue;
                case ShipCellRole.Station:
                    stations.Add((cell.Id, p));
                    break;
            }

            cells[p] = mapped.CarriesModifiers
                ? (mapped.Block.Value, mapped.Tint, mapped.Glow, mapped.Shape)
                : (mapped.Block.Value, 0, 0, 0);
        }
    }

    /// <summary>The block a mapped block becomes in the mask: the hull for a block no item places, crystal for the hull of
    /// an alien wreck (when crystal can be placed).</summary>
    private static System.Func<ushort, ushort> RepairSafe(GameContent content, ShipCellPalette palette, bool alien)
    {
        var placeable = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var item in content.Items.Values)
        {
            if (!string.IsNullOrEmpty(item.PlacesBlock))
            {
                placeable.Add(item.PlacesBlock!);
            }
        }

        ushort wall = palette.Wall.Value;
        ushort crystal = content.GetBlock("crystal") is { } c && placeable.Contains(c.Key) ? c.NumericId.Value : wall;
        return block =>
        {
            if (block != 0 && block != wall && !(content.BlockById(new BlockId(block)) is { } def && placeable.Contains(def.Key)))
            {
                block = wall;
            }

            return alien && block == wall ? crystal : block;
        };
    }

    private void Fill(Dictionary<Vector3i, (ushort Block, int Tint, int Glow, int Shape)> cells, System.Func<ushort, ushort> repairSafe)
    {
        foreach (var kv in cells)
        {
            var p = Local(kv.Key);
            if (kv.Value.Block == 0 || !In(p.X, p.Y, p.Z))
            {
                continue;
            }

            int i = Index(p.X, p.Y, p.Z);
            Intact[i] = repairSafe(kv.Value.Block);
            if (kv.Value.Tint != 0 || kv.Value.Glow != 0)
            {
                Mods[i] = (kv.Value.Tint, kv.Value.Glow);
            }

            if (kv.Value.Shape != 0)
            {
                Shapes[i] = kv.Value.Shape;
            }
        }
    }

    /// <summary>The inside: in every enclosed column, the air of the intact hull from the row above the floor to the row
    /// below the roof (layout rows 1 .. height − 1).</summary>
    private void MarkInterior(ShipLayout layout, HashSet<(int X, int Z)> footprint)
    {
        foreach (var (fx, fz) in footprint)
        {
            for (int y = 1; y < layout.Height; y++)
            {
                var p = Local(new Vector3i(fx, y, fz));
                if (In(p.X, p.Y, p.Z) && Intact[Index(p.X, p.Y, p.Z)] == 0)
                {
                    Interior[Index(p.X, p.Y, p.Z)] = true;
                }
            }
        }
    }
}
