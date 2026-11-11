// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A landmark as it stands in the world (#3486): the voxel structure (after the ruin pass, when the landmark is a
/// ruin), the row that sits on the ground and the cavity cells its underground part carves. Structure-local
/// coordinates; row <see cref="GroundRow"/> lies on the ground, the rows below it in the rock.
/// </summary>
public sealed class LandmarkLayout
{
    private readonly BitArray _cavity;

    internal LandmarkLayout(SettlementStructure structure, int groundRow, BitArray cavity, bool ruined, int turns, int cavityCount)
    {
        Structure = structure;
        GroundRow = groundRow;
        _cavity = cavity;
        Ruined = ruined;
        Turns = turns;
        CavityCount = cavityCount;
    }

    /// <summary>The blocks, their modifiers and the markers that survived (a ruin keeps its chests, sounds and dens).</summary>
    public SettlementStructure Structure { get; }

    public int GroundRow { get; }

    public bool Ruined { get; }

    /// <summary>The quarter turns the template was turned by (0..3).</summary>
    public int Turns { get; }

    /// <summary>How many cells the underground part carves (0 for a landmark that only stands on the ground).</summary>
    public int CavityCount { get; }

    /// <summary>Whether the cell is carved to air (#3490) — a tunnel or a cavern of the underground part.</summary>
    public bool IsCavity(int x, int y, int z)
        => Structure.InBounds(x, y, z) && _cavity[(((x * Structure.Height) + y) * Structure.Length) + z];
}

/// <summary>
/// Builds a <see cref="LandmarkLayout"/> from a landmark template (#3486): the compact body is read straight into a
/// voxel grid (never expanded into cell objects), turned by a number of quarter turns, material tokens resolved for
/// the planet's surface, and — when the template stands as a ruin — run through the shared ruin pass with the
/// instance's seed. Pure and deterministic: the server stamps with it, re-derives the markers with it on every later
/// load, and the structure editor previews a ruin with it.
/// </summary>
public static class LandmarkGenerator
{
    /// <summary>The tier name a landmark's structure carries.</summary>
    public const string Tier = "landmark";

    /// <summary>The revision of the ruin pass a fresh landmark is stamped with (pinned in its placement record).</summary>
    public const int RuinRevision = StructureRuiner.Revision;

    /// <summary>The marker types a RUIN keeps. Everything alive or working — residents, vendors, boards, doors, rooms,
    /// pre-built circuits — is gone from a ruin; what is left is what a visitor finds.</summary>
    public static readonly string[] RuinMarkers =
    {
        LandmarkRules.ChestMarker, "loot", "data_terminal", "relic_cache", LandmarkRules.SoundMarker, LandmarkRules.DenMarker,
    };

    /// <summary>
    /// Builds the landmark. <paramref name="ruin"/> null = what the template says (<see cref="LandmarkRules.IsRuin"/>);
    /// the editor passes true / false to preview either. <paramref name="surfaceBlock"/> is the biome surface the
    /// material tokens and the overgrowth follow.
    /// </summary>
    public static LandmarkLayout Build(StructureTemplate t, long seed, int turns, GameContent content, string surfaceBlock,
        bool? ruin = null)
    {
        turns = ((turns % 4) + 4) % 4;
        bool swap = (turns & 1) == 1;
        int w = System.Math.Max(1, swap ? t.Length : t.Width);
        int h = System.Math.Max(1, t.Height);
        int l = System.Math.Max(1, swap ? t.Width : t.Length);
        var grid = new VoxelGrid(w, h, l);
        var materials = ModuleMaterials.ForSettlement("village", surfaceBlock, alien: false, content);

        FillBlocks(t, turns, grid, content, materials);
        var markers = ReadMarkers(t, turns, grid, content);
        int groundRow = System.Math.Clamp(t.GroundRow, 0, h - 1);
        bool ruined = ruin ?? LandmarkRules.IsRuin(t);
        if (ruined)
        {
            StructureRuiner.Run(grid, content, seed, LandmarkRules.ProfileOf(t), t.RuinStrength, groundRow, markers, surfaceBlock);
            markers.RemoveAll(m => System.Array.IndexOf(RuinMarkers, m.Type) < 0);
            SeatMarkers(grid, markers, groundRow);
        }

        int cavities = 0;
        foreach (bool c in grid.Cavity)
        {
            cavities += c ? 1 : 0;
        }

        var structure = new SettlementStructure(w, h, l, Tier, ruined, inhabitant: string.Empty, grid.Blocks, markers,
            buildingCount: 1, grid.Mods, grid.Shapes);
        return new LandmarkLayout(structure, groundRow, grid.Cavity, ruined, turns, cavities);
    }

    /// <summary>One cell of the template turned by <paramref name="turns"/> quarter turns — the rule of
    /// <see cref="TemplateTransform"/>: (x, z) → (L − 1 − z, x), width and length swapping each turn.</summary>
    internal static (int X, int Z) Turn(int x, int z, int width, int length, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            int nx = length - 1 - z;
            z = x;
            x = nx;
            (width, length) = (length, width);
        }

        return (x, z);
    }

    private static int TurnShape(int shape, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            shape = TemplateTransform.TurnShape(shape);
        }

        return shape;
    }

    private static void FillBlocks(StructureTemplate t, int turns, VoxelGrid grid, GameContent content, ModuleMaterials materials)
    {
        var ids = new Dictionary<string, ushort>(System.StringComparer.Ordinal);
        TemplateBody.ForEachPlain(t, p =>
        {
            var (x, z) = Turn(p.X, p.Z, t.Width, t.Length, turns);
            if (!grid.In(x, p.Y, z))
            {
                return;
            }

            int i = grid.Index(x, p.Y, z);
            if (p.Cavity)
            {
                grid.Clear(x, p.Y, z);
                grid.Cavity[i] = true;
                grid.Keep[i] = false;
                return;
            }

            if (!ids.TryGetValue(p.Id, out ushort id))
            {
                id = MaterialTokens.IsToken(p.Id) ? materials.Resolve(p.Id) : content.GetBlock(p.Id)?.NumericId.Value ?? 0;
                ids[p.Id] = id;
            }

            if (id == 0)
            {
                return; // an unknown block is left out (the content load already reported it)
            }

            grid.Set(x, p.Y, z, id, p.Tint, p.Glow, TurnShape(p.Shape, turns));
            grid.Cavity[i] = false;
            grid.Keep[i] = p.Keep;
        });
    }

    /// <summary>
    /// After the ruin pass: every surviving marker comes to rest on what is left. A marker whose floor fell drops
    /// straight down to the next solid cell (below the ground row a cell that is neither a block nor a cavity is the
    /// planet's rock and carries it too), and its own cell and the one above are freed — so a chest lies in the rubble
    /// of the floor it stood on instead of hanging in the air.
    /// </summary>
    private static void SeatMarkers(VoxelGrid grid, List<SettlementMarker> markers, int groundRow)
    {
        bool Carries(int x, int y, int z)
            => y < 0 || grid.Solid(x, y, z) || (y < groundRow && !grid.Cavity[grid.Index(x, y, z)]);

        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            int x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z;
            while (y > 0 && !Carries(x, y - 1, z))
            {
                y--;
            }

            for (int dy = 0; dy <= 1; dy++)
            {
                if (grid.Solid(x, y + dy, z))
                {
                    grid.Clear(x, y + dy, z);
                }
            }

            if (y != m.LocalPos.Y)
            {
                markers[i] = new SettlementMarker(m.Type, new Vector3i(x, y, z), m.DoorAxis, m.Data);
            }
        }
    }

    /// <summary>The template's marker cells, turned, with their settings line as the marker's data — plus a
    /// <see cref="TemplateDevices.Marker"/> for every pre-built Crystal Net device (#2260), like every other composer.</summary>
    private static List<SettlementMarker> ReadMarkers(StructureTemplate t, int turns, VoxelGrid grid, GameContent content)
    {
        var markers = new List<SettlementMarker>();
        foreach (var cell in t.Cells)
        {
            var (x, z) = Turn(cell.X, cell.Z, t.Width, t.Length, turns);
            if (!grid.In(x, cell.Y, z))
            {
                continue;
            }

            var pos = new Vector3i(x, cell.Y, z);
            if (cell.Kind == "marker")
            {
                markers.Add(new SettlementMarker(cell.Id, pos, data: cell.Config));
            }
            else if (cell.Kind == "block" && content.GetBlock(cell.Id) is not null && TemplateDevices.DataFor(cell) is { } device)
            {
                markers.Add(new SettlementMarker(TemplateDevices.Marker, pos, data: device));
            }
        }

        return markers;
    }
}
