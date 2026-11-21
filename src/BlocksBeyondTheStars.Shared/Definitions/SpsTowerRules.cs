// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The vocabulary and the numbers of the SPS GUIDE TOWERS (#3893, terrain generation 47, docs/developer/SPS_TOWERS.md): the
/// ruins of the towers the Scout and Pioneer Service raised on every prepared settlement world — "the sky-lights". A structure
/// kind of its own on the landmark stack, like the giant ship wrecks: an authored template (<c>data/sps_towers/*.json</c>, a
/// <see cref="StructureTemplate"/> flagged <see cref="StructureTemplate.SpsTower"/>) that only its own stamper offers, always
/// a ruin (the lattice profile), always <c>protected</c> — nothing of it can be mined, its chests stay lootable. Only on a
/// body with an inhabited settlement; no tower stands whole anywhere. Shared by the content validation, the tower generator
/// and the server's stamper.
/// </summary>
public static class SpsTowerRules
{
    /// <summary>The structure kind a planet type's whitelist names.</summary>
    public const string StructureKind = "sps_towers";

    /// <summary>The placement record kind (one record per body, index 0; an admin stamp takes the next index).</summary>
    public const string RecordKind = "sps_tower";

    /// <summary>The feature key and the structure-overlay key of a placed tower: <c>sps_tower:&lt;index&gt;</c>.</summary>
    public static string FeatureKey(int index) => RecordKind + ":" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The map marker type, the client list's kind (<c>NetLandmark.Kind</c>) and the scan readout's kind.</summary>
    public const string MapType = "sps_tower";

    /// <summary>The <see cref="StructureTemplate.Kind"/> a loaded tower carries.</summary>
    public const string TemplateKind = "sps_tower";

    /// <summary>The folder of the shipped towers under <c>data/</c>: one template per file, the key = the file name.</summary>
    public const string DataFolder = "sps_towers";

    /// <summary>Chance that an eligible body (one with an inhabited settlement) carries a tower — about every fifth to sixth —
    /// before the structures factor and the world option. <c>data/structure_types.json</c> (<c>sps_tower</c>) holds the live
    /// value; <c>ServerConfig.SpsTowerChance</c> overrides it when set. The first eligible body a save stamps carries one for
    /// sure, whatever the roll says.</summary>
    public const double Chance = 0.18;

    /// <summary>The share of the towers with a guard post beside the foot (a hash of the tower's seed).</summary>
    public const double GuardPostChance = 0.4;

    /// <summary>How far a tower keeps from a settlement's footprint (blocks).</summary>
    public const int SettlementDistance = 60;

    /// <summary>How far the tower's top stays below the body's atmosphere line (<c>PlanetType.AtmosphereHeight</c>, an
    /// absolute world row): a seat whose top would come nearer is refused.</summary>
    public const int AtmosphereMargin = 8;

    /// <summary>How near (blocks, to the footprint) a player on foot must come for a tower to go on everyone's map.</summary>
    public const int RevealRange = 100;

    /// <summary>How near (blocks, to the footprint) a player must come for the game event <c>sps_tower:found</c>.</summary>
    public const int FoundRange = 48;

    // --- the markers ---------------------------------------------------------------------------------------------

    /// <summary>The relay console: one per tower, in the relay room at the foot. The generator places the block
    /// <see cref="ConsoleBlock"/> in the marker's cell AFTER the ruin pass; the record remembers the cell.</summary>
    public const string ConsoleMarker = "sps_console";

    /// <summary>An emergency lamp: the generator places <see cref="LampBlock"/> in the marker's cell AFTER the ruin pass
    /// (which switches every light off), so it burns. Setting <see cref="LampOwnerSetting"/> = <see cref="LampOwnerTank"/>
    /// flags a lamp of the guard post's capsule tank; every other lamp is the tower's.</summary>
    public const string LampMarker = "sps_lamp";

    /// <summary>The capsule tank of a guard post: the generator stacks two <see cref="TankBlock"/> cells, the marker's cell
    /// and the one above it.</summary>
    public const string TankMarker = "sps_tank";

    /// <summary>The guard post's box: the marker sits in its low corner (template coordinates), its settings
    /// <see cref="PostWidthSetting"/>, <see cref="PostHeightSetting"/>, <see cref="PostLengthSetting"/> give the size. A tower
    /// that rolls no guard post loses every cell and marker of the box before the ruin pass.</summary>
    public const string GuardPostMarker = "sps_guard_post";

    public const string LampOwnerSetting = "of";
    public const string LampOwnerTank = "tank";
    public const string PostWidthSetting = "w";
    public const string PostHeightSetting = "h";
    public const string PostLengthSetting = "l";

    /// <summary>The block of the relay console.</summary>
    public const string ConsoleBlock = "sps_console";

    /// <summary>The block of an emergency lamp as a tower is found: red. Lighting a tower turns them <see cref="LitLampBlock"/>.</summary>
    public const string LampBlock = "light_red";

    /// <summary>The block a lamp becomes when its tower (or its tank) is brought back: blue.</summary>
    public const string LitLampBlock = "light_blue";

    /// <summary>The block of one cell of the sealed capsule tank (two stacked).</summary>
    public const string TankBlock = "capsule_tank";

    /// <summary>The markers a tower template may carry: its own four plus what a ruin keeps of a landmark's.</summary>
    public static readonly string[] Markers =
    {
        ConsoleMarker, LampMarker, TankMarker, GuardPostMarker,
        LandmarkRules.ChestMarker, LandmarkRules.SoundMarker, "loot", "data_terminal", "relic_cache",
    };

    /// <summary>The blocks the generator places itself; a template body must not hold them (they would die in the ruin pass
    /// or stand where no record finds them).</summary>
    public static readonly string[] PlacedBlocks = { ConsoleBlock, TankBlock };

    /// <summary>The locale key of a tower template's display name: <c>landmark.&lt;key&gt;.name</c> — a landmark's key, on
    /// purpose: the Codex lists a scanned tower among the landmarks, with the text of <see cref="DescriptionKey"/>.</summary>
    public static string NameKey(string key) => "landmark." + key + ".name";

    /// <summary>The locale key of a tower template's Codex text: <c>landmark.&lt;key&gt;.desc</c>.</summary>
    public static string DescriptionKey(string key) => "landmark." + key + ".desc";

    /// <summary>The world key of a tower record: <c>&lt;location&gt;#&lt;index&gt;</c>.</summary>
    public static string SiteId(string locationId, int index) => locationId + "#" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>How high the template stands above the ground: its rows above the ground row.</summary>
    public static int HeightAboveGround(StructureTemplate t) => Math.Max(0, t.Height - Math.Clamp(t.GroundRow, 0, Math.Max(0, t.Height - 1)));

    /// <summary>
    /// What is wrong with a tower template, one sentence each — empty when it can be used. Checked: the landmark rules (size,
    /// ruin words, ground row, zones — <see cref="LandmarkRules.Problems"/>) plus the tower's own: always a ruin, always
    /// <c>protected</c>, never a landmark, a giant ship or a civilization template; every block known to
    /// <paramref name="blockExists"/> and none of <see cref="PlacedBlocks"/>; every marker one of <see cref="Markers"/> and
    /// inside the box; exactly one <see cref="ConsoleMarker"/>; at most one guard post with a size, holding exactly one
    /// <see cref="TankMarker"/> (and no tank without a post); at least one tower lamp.
    /// </summary>
    public static List<string> Problems(StructureTemplate t, Func<string, bool> blockExists)
    {
        var problems = LandmarkRules.Problems(t);
        if (!LandmarkRules.IsRuin(t))
        {
            problems.Add("a guide tower always stands as a ruin (ruin: always)");
        }

        if (LandmarkRules.ProtectionOf(t) != LandmarkRules.ProtectionProtected)
        {
            problems.Add("a guide tower is always protected (protection: protected)");
        }

        if (t.Landmark || t.GiantShip || t.Civilization.Length > 0 || t.IsSource)
        {
            problems.Add("a guide tower cannot also be a landmark, a giant ship, a civilization template or a generator entry");
        }

        AddBodyProblems(t, blockExists, problems);
        AddMarkerProblems(t, problems);
        return problems;
    }

    private static void AddBodyProblems(StructureTemplate t, Func<string, bool> blockExists, List<string> problems)
    {
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var placed = new SortedSet<string>(StringComparer.Ordinal);
        int blocks = 0;
        TemplateBody.ForEachPlain(t, p =>
        {
            if (p.Cavity)
            {
                return;
            }

            blocks++;
            string id = p.Id ?? string.Empty;
            if (!blockExists(id))
            {
                unknown.Add(id.Length == 0 ? "(empty)" : id);
            }
            else if (Array.IndexOf(PlacedBlocks, id) >= 0)
            {
                placed.Add(id);
            }
        });

        if (blocks == 0)
        {
            problems.Add("it has no body (no block at all)");
        }

        if (unknown.Count > 0)
        {
            problems.Add("unknown block(s) " + string.Join(", ", unknown) + " (a guide tower names concrete blocks, no material tokens)");
        }

        if (placed.Count > 0)
        {
            problems.Add("block(s) " + string.Join(", ", placed) + " are placed by the markers, never by the body");
        }
    }

    /// <summary>Every marker known and inside the box; then the counts (<see cref="AddMarkerCountProblems"/>).</summary>
    private static void AddMarkerProblems(StructureTemplate t, List<string> problems)
    {
        int consoles = 0, posts = 0, tanks = 0, towerLamps = 0, tanksInPost = 0;
        var box = GuardPostBox(t);
        foreach (var c in t.Cells)
        {
            if (c == null || c.Kind != "marker")
            {
                continue;
            }

            string at = $"marker '{c.Id}' at ({c.X},{c.Y},{c.Z})";
            if (Array.IndexOf(Markers, c.Id) < 0)
            {
                problems.Add($"{at} is not a guide tower marker ({string.Join(" | ", Markers)})");
            }

            if (c.X < 0 || c.Y < 0 || c.Z < 0 || c.X >= t.Width || c.Y >= t.Height || c.Z >= t.Length)
            {
                problems.Add(at + " lies outside the structure");
            }

            bool inPost = box is { } b && b.Contains(c.X, c.Y, c.Z);
            consoles += c.Id == ConsoleMarker ? 1 : 0;
            posts += c.Id == GuardPostMarker ? 1 : 0;
            tanks += c.Id == TankMarker ? 1 : 0;
            tanksInPost += c.Id == TankMarker && inPost ? 1 : 0;
            towerLamps += c.Id == LampMarker && !IsTankLamp(c.Config) ? 1 : 0;
        }

        AddMarkerCountProblems(problems, consoles, towerLamps, posts, hasBox: box is not null, tanks, tanksInPost);
    }

    /// <summary>Exactly one console, a lamp of the tower's own, at most one guard post with a size, its one tank inside it.</summary>
    private static void AddMarkerCountProblems(List<string> problems, int consoles, int towerLamps, int posts, bool hasBox, int tanks,
        int tanksInPost)
    {
        if (consoles != 1)
        {
            problems.Add($"it needs exactly one '{ConsoleMarker}' marker (the relay console), it has {consoles}");
        }

        if (towerLamps == 0)
        {
            problems.Add($"it has no '{LampMarker}' marker of the tower itself (the emergency lamps)");
        }

        if (posts > 1 || (posts == 1 && !hasBox))
        {
            problems.Add($"it may hold one '{GuardPostMarker}' marker with a size ({PostWidthSetting}, {PostHeightSetting}, {PostLengthSetting})");
        }

        if (tanks != posts || tanksInPost != tanks)
        {
            problems.Add($"a guard post holds exactly one '{TankMarker}' marker inside its box, and no tank stands without a post");
        }
    }

    /// <summary>Whether a lamp marker's settings flag it as the capsule tank's.</summary>
    public static bool IsTankLamp(string? settings)
        => string.Equals(MarkerSettings.Parse(settings).Get(LampOwnerSetting), LampOwnerTank, StringComparison.OrdinalIgnoreCase);

    /// <summary>The guard post's box in template coordinates (inclusive corners), or null when the template has none or its
    /// marker names no size.</summary>
    public static GuardPostBox? GuardPostBox(StructureTemplate t)
    {
        foreach (var c in t.Cells)
        {
            if (c == null || c.Kind != "marker" || c.Id != GuardPostMarker)
            {
                continue;
            }

            var s = MarkerSettings.Parse(c.Config);
            int w = s.GetInt(PostWidthSetting, 0), h = s.GetInt(PostHeightSetting, 0), l = s.GetInt(PostLengthSetting, 0);
            return w > 0 && h > 0 && l > 0 ? new GuardPostBox(c.X, c.Y, c.Z, c.X + w - 1, c.Y + h - 1, c.Z + l - 1) : null;
        }

        return null;
    }
}

/// <summary>A guard post's box, inclusive corners (<see cref="SpsTowerRules.GuardPostBox"/>).</summary>
public readonly struct GuardPostBox
{
    public GuardPostBox(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        (X0, Y0, Z0, X1, Y1, Z1) = (Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int Z0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int Z1 { get; }

    public bool Contains(int x, int y, int z) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1 && z >= Z0 && z <= Z1;
}
