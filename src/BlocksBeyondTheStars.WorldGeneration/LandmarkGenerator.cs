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

    /// <summary>The newest revision of the ruin pass (pinned in a placement record as <c>rev</c>; a record replays through
    /// the revision it pins, #3515). A fresh landmark takes <see cref="RuinRevisionFor"/> its world's generation.</summary>
    public const int RuinRevision = StructureRuiner.Revision;

    /// <summary>The ruin revision a FRESH landmark of a world of terrain generation <paramref name="terrainGeneration"/> is
    /// stamped with: the newest (2, the lattice breaks) from the school club's generation 39 on, revision 1 before — an older
    /// world's new landmarks and their records stay exactly as that world's build stamped them.</summary>
    public static int RuinRevisionFor(int terrainGeneration)
        => terrainGeneration >= Shared.World.WorldDescription.SchoolClubLandmarksGeneration ? RuinRevision : 1;

    /// <summary>The marker types a RUIN keeps (<see cref="RuinMarkerRules.Kept"/>, the same array).</summary>
    public static readonly string[] RuinMarkers = RuinMarkerRules.Kept;

    /// <summary>
    /// Builds the landmark with the newest ruin revision (<see cref="RuinRevision"/>). <paramref name="ruin"/> null =
    /// what the template says (<see cref="LandmarkRules.IsRuin"/>); the editor passes true / false to preview either.
    /// <paramref name="surfaceBlock"/> is the biome surface the material tokens and the overgrowth follow.
    /// </summary>
    public static LandmarkLayout Build(StructureTemplate t, long seed, int turns, GameContent content, string surfaceBlock,
        bool? ruin = null)
        => Build(t, seed, turns, content, surfaceBlock, ruin, RuinRevision);

    /// <summary>
    /// Builds the landmark with ruin revision <paramref name="revision"/> — the one its placement record pins (#3515),
    /// so a standing landmark is re-derived exactly as it was stamped. Otherwise as the overload without a revision.
    /// </summary>
    public static LandmarkLayout Build(StructureTemplate t, long seed, int turns, GameContent content, string surfaceBlock,
        bool? ruin, int revision)
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
        return Finish(t, grid, markers, groundRow, ruin ?? LandmarkRules.IsRuin(t), seed, content, surfaceBlock, revision, turns);
    }

    /// <summary>
    /// The landmark from its filled grid (shared by the authored body and a generator entry, #3518): when it stands as a ruin,
    /// the shared ruin pass with the template's profile and strength, then only the markers a ruin keeps, seated on what is
    /// left; then the layout with its cavity count.
    /// </summary>
    internal static LandmarkLayout Finish(StructureTemplate t, VoxelGrid grid, List<SettlementMarker> markers, int groundRow, bool ruined,
        long seed, GameContent content, string surfaceBlock, int revision, int turns)
    {
        if (ruined && t.RuinZones is { Count: > 0 })
        {
            // The school club's ruin zones (generation 39 content): a template that names none never comes here, so its
            // ruin is byte-identical to the pass as it shipped.
            var cells = new Vector3i[markers.Count];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = markers[i].LocalPos;
            }

            StructureRuiner.RunZoned(grid, content, seed, LandmarkRules.ProfileOf(t), t.RuinStrength, groundRow, cells, surfaceBlock,
                revision, options: null, ZonesFor(t, turns));
            RuinMarkerRules.KeepOnly(markers);
            RuinMarkerRules.Seat(grid, markers, groundRow);
        }
        else if (ruined)
        {
            StructureRuiner.Run(grid, content, seed, LandmarkRules.ProfileOf(t), t.RuinStrength, groundRow, markers, surfaceBlock,
                revision, options: null);
            RuinMarkerRules.KeepOnly(markers);
            RuinMarkerRules.Seat(grid, markers, groundRow);
        }

        int cavities = 0;
        foreach (bool c in grid.Cavity)
        {
            cavities += c ? 1 : 0;
        }

        var structure = new SettlementStructure(grid.W, grid.H, grid.L, Tier, ruined, inhabitant: string.Empty, grid.Blocks, markers,
            buildingCount: 1, grid.Mods, grid.Shapes);
        return new LandmarkLayout(structure, groundRow, grid.Cavity, ruined, turns, cavities);
    }

    /// <summary>The template's ruin zones in the grid's coordinates: each box turned with the structure (a generator entry, which
    /// never turns, keeps its boxes), its profile resolved (empty = the template's) and its strength as authored.</summary>
    internal static List<RuinZoneBox> ZonesFor(StructureTemplate t, int turns)
    {
        var boxes = new List<RuinZoneBox>(t.RuinZones.Count);
        foreach (var z in t.RuinZones)
        {
            if (z is null)
            {
                continue;
            }

            var (ax, az) = Turn(z.X0, z.Z0, t.Width, t.Length, turns);
            var (bx, bz) = Turn(z.X1, z.Z1, t.Width, t.Length, turns);
            if (turns == 0 || t.Width <= 0 || t.Length <= 0)
            {
                (ax, az, bx, bz) = (z.X0, z.Z0, z.X1, z.Z1);
            }

            boxes.Add(new RuinZoneBox(ax, z.Y0, az, bx, z.Y1, bz, LandmarkRules.ProfileOf(t, z), z.Strength));
        }

        return boxes; // clipped to the grid by the pass
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
