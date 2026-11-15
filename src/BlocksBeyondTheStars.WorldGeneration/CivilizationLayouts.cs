// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A step that dresses a landmark-style grid after the ruin pass (#3641: a civilization's props).</summary>
public abstract class LayoutDress
{
    internal abstract void Apply(VoxelGrid grid, List<SettlementMarker> markers);
}

/// <summary>
/// #3639: what a civilization's site is built of — its materials for the tokens, its ruin look — and (#3641) the props its
/// <see cref="CivilizationRules.PropMarker"/> markers place. Pure and deterministic: the server stamps with it and re-derives
/// the markers with it on every later load.
/// </summary>
public static class CivilizationLayouts
{
    /// <summary>The civilization's materials: a village's for the surface (<see cref="ModuleMaterials.ForSettlement"/>), each
    /// token the civilization names replaced by its block.</summary>
    public static ModuleMaterials MaterialsOf(CivilizationDefinition civ, string surfaceBlock, GameContent content)
    {
        var m = ModuleMaterials.ForSettlement("village", surfaceBlock, alien: false, content);
        foreach (var kv in civ.Materials)
        {
            ushort id = content.GetBlock(kv.Value)?.NumericId.Value ?? 0;
            if (id == 0)
            {
                continue;
            }

            switch (kv.Key)
            {
                case MaterialTokens.Wall: m.Wall = id; break;
                case MaterialTokens.Accent: m.Accent = id; break;
                case MaterialTokens.Roof: m.Roof = id; break;
                case MaterialTokens.Floor: m.Floor = id; break;
                case MaterialTokens.Path: m.Path = id; break;
            }
        }

        return m;
    }

    /// <summary>The civilization's ruin look: its rubble and overgrowth (none on an airless body or under water — the sea
    /// floor keeps its own flora).</summary>
    public static RuinOptions RuinOptionsOf(CivilizationDefinition civ, bool airless, bool underwater)
    {
        string growth = civ.Ruin.Overgrowth ?? string.Empty;
        var options = new RuinOptions
        {
            Overgrowth = !airless && growth != "none" && (!underwater || growth.Length > 0),
            OvergrowthBlock = growth.Length > 0 && growth != "none" ? growth : null,
        };
        if (civ.Ruin.Rubble.Count > 0)
        {
            var palette = new List<RuinWeightedBlock>();
            foreach (string b in civ.Ruin.Rubble)
            {
                palette.Add(new RuinWeightedBlock(b));
            }

            options.RubblePalette = palette;
        }

        return options;
    }

    /// <summary>An authored site of the civilization: the landmark build with the civilization's materials, ruin look and props.</summary>
    public static LandmarkLayout Build(StructureTemplate t, CivilizationDefinition civ, long seed, int turns, GameContent content,
        string surfaceBlock, int revision, bool airless, bool underwater)
    {
        var materials = MaterialsOf(civ, surfaceBlock, content);
        return LandmarkGenerator.Build(t, seed, turns, content, surfaceBlock, ruin: null, revision, materials,
            RuinOptionsOf(civ, airless, underwater), new CivilizationPropDress(civ, seed, content, materials));
    }

    /// <summary>The prop a marker cell places, or null: <c>template=&lt;key&gt;</c> names one of the civilization's props, else
    /// <c>pool=&lt;propPool&gt;</c> draws one of the pool by a hash of the seed and the cell.</summary>
    public static StructureTemplate? PropFor(CivilizationDefinition civ, GameContent content, string settings, long seed, Vector3i cell)
    {
        var s = MarkerSettings.Parse(settings);
        string key = s.Get("template");
        if (key.Length > 0)
        {
            var t = content.CivilizationTemplateByKey(key);
            return t != null && t.Civilization == civ.Key && !t.IsSource ? t : null;
        }

        var pool = content.CivilizationPropsFor(civ.Key, s.Get("pool"));
        return pool.Count == 0 ? null : pool[StableHashing.Index(CellLane("pick", cell), seed, pool.Count)];
    }

    internal static string CellLane(string what, Vector3i cell)
        => "civ:prop:" + what + ":" + cell.X.ToString(CultureInfo.InvariantCulture) + ","
           + cell.Y.ToString(CultureInfo.InvariantCulture) + "," + cell.Z.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// #3641: places a civilization's props — every <see cref="CivilizationRules.PropMarker"/> marker, after the ruin pass and
/// seated on what it left, becomes a small authored piece of the same civilization standing on that floor: centred on the
/// marker, turned by a hash of the cell, written only into empty cells (never into a wall, a cavity or an air pocket). The
/// prop markers stay in the layout (nothing materializes them) so VEGA knows where the props stand (#3642).
/// </summary>
public sealed class CivilizationPropDress : LayoutDress
{
    private readonly CivilizationDefinition _civ;
    private readonly long _seed;
    private readonly GameContent _content;
    private readonly ModuleMaterials _materials;

    public CivilizationPropDress(CivilizationDefinition civ, long seed, GameContent content, ModuleMaterials materials)
    {
        _civ = civ;
        _seed = seed;
        _content = content;
        _materials = materials;
    }

    internal override void Apply(VoxelGrid grid, List<SettlementMarker> markers)
    {
        // Every other marker's cell and the cell above it stay free: a crate stands there, a hidden chest's cover stone above it.
        var reserved = new HashSet<Vector3i>();
        foreach (var m in markers)
        {
            if (m.Type != CivilizationRules.PropMarker)
            {
                reserved.Add(m.LocalPos);
                reserved.Add(new Vector3i(m.LocalPos.X, m.LocalPos.Y + 1, m.LocalPos.Z));
            }
        }

        foreach (var m in markers)
        {
            if (m.Type == CivilizationRules.PropMarker && CivilizationLayouts.PropFor(_civ, _content, m.Data, _seed, m.LocalPos) is { } prop)
            {
                Place(grid, prop, m.LocalPos, reserved);
            }
        }
    }

    /// <summary>The columns of a prop's box the marker's room reaches: from the marker's column, every neighbour column whose
    /// two cells at the marker's height are free — so a prop never reaches through a wall into the next passage.</summary>
    private static bool[,] RoomColumns(VoxelGrid grid, Vector3i at, int x0, int z0, int w, int l)
    {
        var reached = new bool[w, l];
        bool Open(int x, int z) => !grid.Solid(x0 + x, at.Y, z0 + z) && !grid.Solid(x0 + x, at.Y + 1, z0 + z);
        var queue = new Queue<(int X, int Z)>();
        int sx = at.X - x0, sz = at.Z - z0;
        if (sx < 0 || sz < 0 || sx >= w || sz >= l)
        {
            return reached;
        }

        reached[sx, sz] = true;
        queue.Enqueue((sx, sz));
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (nx >= 0 && nz >= 0 && nx < w && nz < l && !reached[nx, nz] && Open(nx, nz))
                {
                    reached[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }

        return reached;
    }

    private void Place(VoxelGrid grid, StructureTemplate prop, Vector3i at, HashSet<Vector3i> reserved)
    {
        int turns = StableHashing.Index(CivilizationLayouts.CellLane("turn", at), _seed, 4);
        bool swap = (turns & 1) == 1;
        int w = Math.Max(1, swap ? prop.Length : prop.Width);
        int h = Math.Max(1, prop.Height);
        int l = Math.Max(1, swap ? prop.Width : prop.Length);
        var piece = new VoxelGrid(w, h, l);
        LandmarkGenerator.FillBlocks(prop, turns, piece, _content, _materials);
        int x0 = at.X - (w / 2), y0 = at.Y - Math.Clamp(prop.GroundRow, 0, h - 1), z0 = at.Z - (l / 2);
        var room = RoomColumns(grid, at, x0, z0, w, l);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    ushort block = piece.Get(x, y, z);
                    int gx = x0 + x, gy = y0 + y, gz = z0 + z;
                    if (block == 0 || !room[x, z] || !grid.In(gx, gy, gz) || grid.Get(gx, gy, gz) != 0 || grid.Cavity[grid.Index(gx, gy, gz)]
                        || reserved.Contains(new Vector3i(gx, gy, gz)))
                    {
                        continue;
                    }

                    int i = piece.Index(x, y, z);
                    var (tint, glow) = piece.Mods.TryGetValue(i, out var mod) ? mod : (0, 0);
                    grid.Set(gx, gy, gz, block, tint, glow, piece.Shapes.TryGetValue(i, out int shape) ? shape : 0);
                }
            }
        }
    }
}
