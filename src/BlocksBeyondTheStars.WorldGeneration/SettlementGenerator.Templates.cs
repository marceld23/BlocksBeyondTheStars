// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// How a whole settlement template stands on a world of terrain generation 38 (#3519): turned by quarter turns like a
/// landmark, its markers carrying their settings (a chest's content, a sound, a den), and — when the settlement rolled a
/// ruin — fallen through the shared ruin pass with the template's keep cells standing. The server pins the turn and the
/// verdict in the placement record.
/// </summary>
public sealed class TemplateStamp
{
    /// <summary>Quarter turns about the vertical axis (<see cref="TemplateTransform.RotateY"/>), 0..3.</summary>
    public int Turns { get; set; }

    /// <summary>Whether every marker carries its settings line as data (generation 38: always).</summary>
    public bool MarkerSettings { get; set; } = true;

    /// <summary>Whether the template stands as a fallen settlement.</summary>
    public bool Ruined { get; set; }

    /// <summary>The instance seed the ruin pass draws from (on a lane of its own, <c>ruin:template:&lt;key&gt;</c>).</summary>
    public long Seed { get; set; }

    /// <summary>The biome surface block the ruin's rubble and flora follow.</summary>
    public string Surface { get; set; } = "stone";
}

/// <summary>A whole template's build (#1828, #3519) — see <see cref="FromTemplate(StructureTemplate, GameContent, ModuleMaterials?, TemplateStamp?)"/>.</summary>
public static partial class SettlementGenerator
{
    /// <summary>
    /// Builds a settlement structure from a whole template. Without a <paramref name="stamp"/> exactly as every world before
    /// terrain generation 38 builds it (intact, unturned, markers without settings). With one (#3519): the template turned by
    /// <see cref="TemplateStamp.Turns"/>, every marker carrying its settings, and — for a <see cref="TemplateStamp.Ruined"/>
    /// stamp — the fallen settlement of the shared ruin pass (the template's profile and strength, masonry by default; its keep
    /// cells stand; only the markers a ruin keeps are left, seated on the rubble).
    /// </summary>
    public static SettlementStructure FromTemplate(StructureTemplate t, GameContent content, ModuleMaterials? materials, TemplateStamp? stamp)
    {
        var authored = t;
        if (stamp is { Turns: not 0 })
        {
            t = TemplateTransform.RotateY(t, stamp.Turns);
        }

        int w = System.Math.Max(1, t.Width), h = System.Math.Max(1, t.Height), l = System.Math.Max(1, t.Length);
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int Tint, int Glow)>();
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();
        var keep = new List<int>();
        int buildings = ReadTemplateCells(t, content, materials, stamp?.MarkerSettings == true, (w, h, l), blocks, mods, shapes, markers, keep);

        markers.AddRange(FurnishAuthoredRooms((x, y, z) => blocks[(x * h + y) * l + z], w, h, l, markers,
            RoomFurnisher.PaletteFor(RoomFurnisher.StyleFor(t.Tier, alien: false), content),
            (long)WorldGenerator.StableHash("furnish:" + t.Key), SinkFor(blocks, w, h, l, mods, shapes))); // #2986: + guest beds

        if (stamp is { Ruined: true })
        {
            var grid = VoxelGrid.Over(w, h, l, blocks, mods, shapes);
            keep.ForEach(i => grid.Keep[i] = true);
            string profile = LandmarkRules.ProfileOf(authored);
            double strength = authored.RuinStrength > 0 ? System.Math.Min(1.0, authored.RuinStrength) : StructureRuiner.DefaultStrength(profile);
            return Fall(grid, markers, t.Tier, "template:" + authored.Key, stamp.Seed, stamp.Surface, profile, strength, content);
        }

        AddFallbackVendor(blocks, w, h, l, markers);
        return new SettlementStructure(w, h, l, t.Tier, ruined: false, inhabitant: "human", blocks, markers, System.Math.Max(1, buildings), mods, shapes);
    }

    /// <summary>
    /// A structure brought down by the shared ruin pass (#3519) in place — a fallen whole template or a fallen generator
    /// entry: profile and strength as given, ground row 0, the grid's keep cells standing, the instance seed on the lane
    /// <c>ruin:&lt;lane&gt;</c> with the generation-38 revision. Only the markers a ruin keeps are left
    /// (<see cref="RuinMarkerRules.KeepOnly"/>), each seated on what is left.
    /// </summary>
    internal static SettlementStructure Fall(VoxelGrid grid, List<SettlementMarker> markers, string tier, string lane, long seed,
        string surface, string profile, double strength, GameContent content)
    {
        StructureRuiner.Run(grid, content, StructureDecay.Seed(lane, seed), profile, strength, groundRow: 0, markers, surface,
            StructureDecay.Revision, options: null);
        RuinMarkerRules.KeepOnly(markers);
        RuinMarkerRules.Seat(grid, markers, groundRow: 0);
        return new SettlementStructure(grid.W, grid.H, grid.L, tier, ruined: true, inhabitant: string.Empty, grid.Blocks, markers,
            buildingCount: 1, grid.Mods, grid.Shapes);
    }

    /// <summary>The markers whose settings a generation-38 structure honours like a landmark (#3519): chests, sounds, dens.</summary>
    internal static bool IsSettingsMarker(string type)
        => type == LandmarkRules.ChestMarker || type == LandmarkRules.SoundMarker || type == LandmarkRules.DenMarker;

    /// <summary>The template's cells into the grid: markers (with their settings when asked), blocks with their dye, glow and
    /// shape (material tokens resolved, unknown keys left out), the pre-built devices (#2260), the keep cells' indices.
    /// Returns how many residents and vendors the template names (its building count).</summary>
    private static int ReadTemplateCells(StructureTemplate t, GameContent content, ModuleMaterials? materials, bool markerSettings,
        (int W, int H, int L) size, ushort[] blocks, Dictionary<int, (int Tint, int Glow)> mods, Dictionary<int, int> shapes,
        List<SettlementMarker> markers, List<int> keep)
    {
        var (w, h, l) = size;
        int buildings = 0;
        foreach (var cell in t.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.Z < 0 || cell.X >= w || cell.Y >= h || cell.Z >= l)
            {
                continue;
            }

            if (cell.Kind == "marker")
            {
                markers.Add(new SettlementMarker(cell.Id, new Vector3i(cell.X, cell.Y, cell.Z), data: markerSettings ? cell.Config : string.Empty));
                if (cell.Id == "npc" || cell.Id == "vendor") buildings++;
                continue;
            }

            ushort id = MaterialTokens.IsToken(cell.Id)
                ? (materials ?? ModuleMaterials.ForSettlement(t.Tier, "stone", alien: false, content)).Resolve(cell.Id) // #1885
                : content.GetBlock(cell.Id)?.NumericId.Value ?? 0;
            if (id == 0)
            {
                continue;
            }

            int idx = (cell.X * h + cell.Y) * l + cell.Z;
            blocks[idx] = id;
            if (cell.Tint != 0 || cell.Glow != 0) mods[idx] = (cell.Tint, cell.Glow);
            if (cell.Shape != 0) shapes[idx] = cell.Shape;
            if (cell.Keep) keep.Add(idx);
            if (TemplateDevices.DataFor(cell) is { } device)
            {
                markers.Add(new SettlementMarker(TemplateDevices.Marker, new Vector3i(cell.X, cell.Y, cell.Z), data: device)); // #2260
            }
        }

        return buildings;
    }

    /// <summary>
    /// Fallback vendor for templates without one — in a FREE cell (#480, was ST-9): the old fixed centre spot could sit inside
    /// a wall, burying the vendor. Scan upward at the centre for the first air cell with something solid below; a fully solid
    /// column falls back to the roof.
    /// NOTE (#480, was ST-9b): templates carry no door_slide/door_hinge markers unless the author places them — RegisterDoors
    /// hangs doors ONLY on markers, so authored doorways without markers stay open arches. Place door markers in the editor
    /// where you want working doors.
    /// </summary>
    private static void AddFallbackVendor(ushort[] blocks, int w, int h, int l, List<SettlementMarker> markers)
    {
        if (markers.Exists(m => m.Type == "vendor"))
        {
            return;
        }

        int vx = w / 2, vz = l / 2, vy = 1;
        for (int y = 1; y < h; y++)
        {
            if (blocks[(vx * h + y) * l + vz] == 0 && blocks[(vx * h + (y - 1)) * l + vz] != 0)
            {
                vy = y;
                break;
            }

            if (y == h - 1)
            {
                vy = h - 1; // solid column all the way up → roof
            }
        }

        markers.Add(new SettlementMarker("vendor", new Vector3i(vx, vy, vz)));
    }
}
