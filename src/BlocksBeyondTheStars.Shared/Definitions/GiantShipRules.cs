// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The vocabulary and the numbers of the GIANT SHIPS (#3625): huge authored spaceships (up to <see cref="MaxSize"/> blocks
/// per axis) that the game never lets anyone build — no blueprint, no craft path, never a <c>ShipDefinition</c>. For now they
/// exist only as wrecks, derived from the intact design with the instance's seed: crashed on a planet (the landmark stamp,
/// profile <see cref="LandmarkRules.ProfileCrash"/>) and drifting in space (the derelict-station technique, profile
/// <see cref="LandmarkRules.ProfileHull"/> plus the derelict decay). Shared by the content validation, the server's
/// stampers, the ruin pass, the client and the editor.
/// </summary>
public static class GiantShipRules
{
    /// <summary>The structure kind a planet type's whitelist names.</summary>
    public const string StructureKind = "giant_wrecks";

    /// <summary>The placement record kind of a wreck on a planet (one record per body, index 0; an admin stamp takes the
    /// next index).</summary>
    public const string RecordKind = "giant_wreck";

    /// <summary>The feature key and the structure-overlay key of a placed planet wreck: <c>giant_wreck:&lt;index&gt;</c>.</summary>
    public static string FeatureKey(int index) => RecordKind + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The map marker type (planet map and flight chart).</summary>
    public const string MapType = "giant_wreck";

    /// <summary>The largest giant ship per axis (the editor's landmark room, the overlay, the far silhouette).</summary>
    public const int MaxSize = 256;

    /// <summary>Chance that an eligible planet carries a giant wreck (about every 20th), before the structures factor.
    /// <c>data/structure_types.json</c> (<c>giant_wreck</c>) holds the live value; <c>ServerConfig.GiantWreckChance</c>
    /// overrides it when set.</summary>
    public const double Chance = 0.05;

    /// <summary>In space: a fixed system other than the start system holds a giant wreck with
    /// <c>Frequency.Probability() × SpaceOddsScale</c> (the derelicts use 0.3).</summary>
    public const double SpaceOddsScale = 0.15;

    /// <summary>The most rolled giant wrecks one system holds (the start system's guaranteed one comes on top).</summary>
    public const int MaxPerSystem = 1;

    /// <summary>The id prefix of a giant wreck in space: a derelict (<c>derelict:</c>) of its own family, so every derelict
    /// rule — no air, nobody home, no trade, falling out is a spacewalk — holds for it.</summary>
    public const string IdPrefix = "derelict:giant-";

    /// <summary>The <c>WorldMetadata.GeneratedLoot</c> key prefix that marks a stored derelict row as a giant wreck's
    /// (<c>derelict-shape:giant:&lt;id&gt;</c>), so it is never rebuilt as a legacy station.</summary>
    public const string ShapeMarkerPrefix = "derelict-shape:giant:";

    /// <summary>The marker of the way in: in space the boarding arrival, on a planet the open way inside. A template names
    /// at least one; the first one is the arrival.</summary>
    public const string EntranceMarker = "entrance";

    /// <summary>The <c>style</c> of an alien giant ship: its wreck scorches and breaks into crystal shards, not scrap.</summary>
    public const string StyleAlien = "alien";

    /// <summary>The locale key of a giant ship's display name: <c>giantship.&lt;key&gt;.name</c>.</summary>
    public static string NameKey(string key) => "giantship." + key + ".name";

    /// <summary>The locale key of a giant ship's Codex text: <c>giantship.&lt;key&gt;.desc</c>.</summary>
    public static string DescriptionKey(string key) => "giantship." + key + ".desc";

    /// <summary>Whether <paramref name="id"/> is a giant wreck in space.</summary>
    public static bool IsGiantWreckId(string? id) => id != null && id.StartsWith(IdPrefix, System.StringComparison.Ordinal);

    // --- the template (#3607): where the files live, what they may hold, and what makes one unusable ---

    /// <summary>The <see cref="StructureTemplate.Kind"/> a loaded giant ship carries (informational, like <c>landmark</c>).</summary>
    public const string TemplateKind = "giant_ship";

    /// <summary>The folder of the shipped giant ships under <c>data/</c>: one template per file, the key = the file name.</summary>
    public const string DataFolder = "giant_ships";

    /// <summary>The structure editor's folder of the player's own giant ships under <c>usercontent/</c>.</summary>
    public const string UserFolder = "giant_ship_templates";

    /// <summary>The marker of a doorway between two rooms (setting <see cref="AxisSetting"/>): the derelict decay reads the
    /// rooms of a wreck in space from these.</summary>
    public const string DoorMarker = "door_slide";

    /// <summary>A doorway's setting, read the way the derelict decay reads it (<c>DerelictGenerator</c>): <c>x</c> = the
    /// doorway's WALL is a plane of constant x — one walks through it along x and the opening runs along z, from the marker's z
    /// to z + 1; <c>z</c> = the wall is a plane of constant z and the opening runs along x. The game's doorway is 2 wide and 3
    /// high, the marker in its bottom cell at the low end.</summary>
    public const string AxisSetting = "axis";

    /// <summary>The markers a giant ship may carry: the way in, the doorways, the chests, the sound emitters and the classic
    /// loot markers a ruin keeps (<c>loot</c>, <c>data_terminal</c>, <c>relic_cache</c>). A wreck is abandoned — everyone got
    /// away — so no person, vendor or creature den is ever part of one.</summary>
    public static readonly string[] Markers =
    {
        EntranceMarker, DoorMarker, LandmarkRules.ChestMarker, LandmarkRules.SoundMarker, "loot", "data_terminal", "relic_cache",
    };

    /// <summary>The ruin profiles a giant ship's ruin zones may name — the ruin pass's own list,
    /// <see cref="LandmarkRules.GiantShipProfiles"/> (<c>crash</c>, <c>hull</c>, <c>lattice</c> for a mast or a gantry); an
    /// empty one takes the setting's own (<c>crash</c> on a planet, <c>hull</c> in space). Masonry and underground stand on the
    /// ground with stone rubble; a ship never does.</summary>
    public static IReadOnlyList<string> ZoneProfiles => LandmarkRules.GiantShipProfiles;

    /// <summary>
    /// What is wrong with a giant ship template, one sentence each — empty when it can be used. Checked: a size of 1 to
    /// <see cref="MaxSize"/> on every axis, a body with at least one block, every block known to
    /// <paramref name="blockExists"/> (concrete block keys — a giant ship names no material tokens) and inside the box, the
    /// ground row inside, the ruin strength and every ruin zone (<see cref="ZoneProblems"/>), the style (empty or
    /// <see cref="StyleAlien"/>), the protection word, the markers (known, inside the box, a doorway with an axis) and at
    /// least one <see cref="EntranceMarker"/> that stands on a floor with two cells of headroom. <paramref name="isSolid"/>
    /// tells a floor or a wall from a passable block (a ladder, a torch); without it every block counts as solid.
    /// A giant ship is never a module, an underground structure, a generator entry or a landmark.
    /// </summary>
    public static List<string> Problems(StructureTemplate t, Func<string, bool> blockExists, Func<string, bool>? isSolid = null)
        => TemplateProblem.Sentences(ProblemList(t, blockExists, isSolid));

    /// <summary><see cref="Problems"/> as codes the structure editor translates (#3692): the same checks, the same sentences.</summary>
    public static List<TemplateProblem> ProblemList(StructureTemplate t, Func<string, bool> blockExists, Func<string, bool>? isSolid = null)
    {
        var problems = new List<TemplateProblem>();
        AddShapeProblems(t, problems);
        AddWordProblems(t, problems);
        problems.AddRange(ZoneProblemList(t));
        AddBodyAndMarkerProblems(t, blockExists, isSolid ?? (_ => true), problems);
        return problems;
    }

    /// <summary>The size, the ground row and the flags of another kind.</summary>
    private static void AddShapeProblems(StructureTemplate t, List<TemplateProblem> problems)
    {
        if (t.Width <= 0 || t.Height <= 0 || t.Length <= 0)
        {
            problems.Add(new TemplateProblem("size.none", "it has no size"));
        }
        else if (t.Width > MaxSize || t.Height > MaxSize || t.Length > MaxSize)
        {
            problems.Add(new TemplateProblem("size.too_large", $"it is larger than {MaxSize} blocks on an axis", TemplateProblem.Num(MaxSize)));
        }

        if (t.GroundRow < 0 || (t.Height > 0 && t.GroundRow >= t.Height))
        {
            problems.Add(new TemplateProblem("ground_row.outside", "the ground row lies outside the structure"));
        }

        if (t.IsModule)
        {
            problems.Add(new TemplateProblem("giant_ship.module", "a giant ship cannot also be a kit module"));
        }

        if (t.Underground)
        {
            problems.Add(new TemplateProblem("giant_ship.underground", "a giant ship cannot also be an underground structure (use the ground row)"));
        }

        if (t.IsSource)
        {
            problems.Add(new TemplateProblem("giant_ship.entry", "a giant ship cannot be a generator entry (it is authored block by block)"));
        }

        if (t.Landmark)
        {
            problems.Add(new TemplateProblem("giant_ship.landmark", "a giant ship cannot also be a landmark"));
        }
    }

    /// <summary>The words and numbers of the template: ruin strength, style, protection, minimum generation.</summary>
    private static void AddWordProblems(StructureTemplate t, List<TemplateProblem> problems)
    {
        if (t.RuinStrength < 0 || t.RuinStrength > 1 || double.IsNaN(t.RuinStrength))
        {
            problems.Add(new TemplateProblem("ruin.strength", "the ruin strength must lie between 0 and 1"));
        }

        if (!string.IsNullOrEmpty(t.Style) && !string.Equals(t.Style, StyleAlien, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(new TemplateProblem("giant_ship.style", $"unknown style '{t.Style}' (empty | {StyleAlien})", t.Style));
        }

        if (LandmarkRules.ProtectionProblemOf(t) is { } protection)
        {
            problems.Add(protection);
        }

        if (t.MinTerrainGeneration < 0)
        {
            problems.Add(new TemplateProblem("generation.negative", "the minimum terrain generation cannot be negative"));
        }
    }

    /// <summary>What is wrong with a giant ship's ruin zones, one sentence each: too many
    /// (<see cref="LandmarkRules.MaxRuinZones"/>), a box outside the structure, a profile other than
    /// <see cref="ZoneProfiles"/>, a strength outside 0..1.</summary>
    public static List<string> ZoneProblems(StructureTemplate t) => TemplateProblem.Sentences(ZoneProblemList(t));

    /// <summary><see cref="ZoneProblems"/> as codes the structure editor translates (#3692).</summary>
    public static List<TemplateProblem> ZoneProblemList(StructureTemplate t)
    {
        var problems = new List<TemplateProblem>();
        var zones = t.RuinZones ?? new List<RuinZone>();
        if (zones.Count > LandmarkRules.MaxRuinZones)
        {
            problems.Add(new TemplateProblem("zone.too_many", $"it has more than {LandmarkRules.MaxRuinZones} ruin zones",
                TemplateProblem.Num(LandmarkRules.MaxRuinZones)));
        }

        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            string n = TemplateProblem.Num(i + 1);
            string at = "ruin zone " + n;
            if (z is null)
            {
                problems.Add(new TemplateProblem("zone.empty", at + " is empty", n));
                continue;
            }

            if (Math.Min(z.X0, z.X1) < 0 || Math.Min(z.Y0, z.Y1) < 0 || Math.Min(z.Z0, z.Z1) < 0
                || Math.Max(z.X0, z.X1) >= t.Width || Math.Max(z.Y0, z.Y1) >= t.Height || Math.Max(z.Z0, z.Z1) >= t.Length)
            {
                problems.Add(new TemplateProblem("zone.outside", at + " reaches outside the structure", n));
            }

            if (!string.IsNullOrEmpty(z.Profile) && !ZoneProfiles.Contains(z.Profile, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(new TemplateProblem("zone.profile_ship",
                    $"{at} names ruin profile '{z.Profile}' — a giant ship's zones take {string.Join(" | ", ZoneProfiles)}", n, z.Profile));
            }

            if (z.Strength < 0 || z.Strength > 1 || double.IsNaN(z.Strength))
            {
                problems.Add(new TemplateProblem("zone.strength", at + ": the strength must lie between 0 and 1", n));
            }
        }

        return problems;
    }

    /// <summary>One walk over the body (a giant ship can hold millions of cells, so it is read once): the blocks it names,
    /// where they lie, and what stands under and above every entrance. Then the markers.</summary>
    private static void AddBodyAndMarkerProblems(StructureTemplate t, Func<string, bool> blockExists, Func<string, bool> isSolid,
        List<TemplateProblem> problems)
    {
        var entrances = t.Cells.Where(c => c != null && c.Kind == "marker" && c.Id == EntranceMarker).ToList();
        var watched = new Dictionary<(int, int, int), string?>();
        foreach (var e in entrances)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                watched[(e.X, e.Y + dy, e.Z)] = null;
            }
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        int blocks = 0, outside = 0;
        TemplateBody.ForEachPlain(t, p =>
        {
            string id = p.Id ?? string.Empty; // a hand-edited file may say "id": null — a problem, never a failed load
            if (watched.ContainsKey((p.X, p.Y, p.Z)))
            {
                watched[(p.X, p.Y, p.Z)] = p.Cavity ? null : id; // the last cell written at a place wins, as in the stamp
            }

            if (p.Cavity)
            {
                return;
            }

            blocks++;
            if (!blockExists(id))
            {
                unknown.Add(id.Length == 0 ? "(empty)" : id);
            }

            if (!Inside(t, p.X, p.Y, p.Z))
            {
                outside++;
            }
        });

        if (blocks == 0)
        {
            problems.Add(new TemplateProblem("giant_ship.no_body", "it has no body (no block at all)"));
        }

        if (unknown.Count > 0)
        {
            problems.Add(new TemplateProblem("blocks.unknown_ship",
                "unknown block(s) " + string.Join(", ", unknown) + " (a giant ship names concrete blocks, no material tokens)", string.Join(", ", unknown)));
        }

        if (outside > 0)
        {
            problems.Add(new TemplateProblem("blocks.outside", outside.ToString(CultureInfo.InvariantCulture) + " block cell(s) lie outside the structure's box",
                TemplateProblem.Num(outside)));
        }

        AddMarkerProblems(t, entrances, watched, isSolid, problems);
    }

    /// <summary>The markers: each known and inside the box, a doorway with its axis, and at least one entrance that stands on
    /// a solid floor with two passable cells above it (<paramref name="watched"/> holds what the body puts there).</summary>
    private static void AddMarkerProblems(StructureTemplate t, List<TemplateCell> entrances, Dictionary<(int, int, int), string?> watched,
        Func<string, bool> isSolid, List<TemplateProblem> problems)
    {
        foreach (var c in t.Cells)
        {
            if (c == null || c.Kind != "marker")
            {
                continue;
            }

            string at = $"marker '{c.Id}' at ({c.X},{c.Y},{c.Z})", id = c.Id ?? string.Empty, where = TemplateProblem.At(c.X, c.Y, c.Z);
            if (Array.IndexOf(Markers, c.Id) < 0)
            {
                problems.Add(new TemplateProblem("marker.not_ship", $"{at} is not a giant ship marker ({string.Join(" | ", Markers)})", id, where));
            }

            if (!Inside(t, c.X, c.Y, c.Z))
            {
                problems.Add(new TemplateProblem("marker.outside", at + " lies outside the structure", id, where));
            }

            if (c.Id == DoorMarker && MarkerSettings.Parse(c.Config).Get(AxisSetting) is not ("x" or "z"))
            {
                problems.Add(new TemplateProblem("marker.door_axis",
                    at + " needs axis=x (its wall is a plane of constant x) or axis=z (a plane of constant z)", id, where));
            }
        }

        if (entrances.Count == 0)
        {
            problems.Add(new TemplateProblem("giant_ship.no_entrance", $"it has no '{EntranceMarker}' marker (the way in)"));
        }

        foreach (var e in entrances)
        {
            bool floor = watched.TryGetValue((e.X, e.Y - 1, e.Z), out var below) && below != null && isSolid(below);
            bool Free(int dy) => !watched.TryGetValue((e.X, e.Y + dy, e.Z), out var id) || id == null || !isSolid(id);
            if (!floor || !Free(0) || !Free(1))
            {
                problems.Add(new TemplateProblem("giant_ship.entrance_room",
                    $"the entrance at ({e.X},{e.Y},{e.Z}) needs a solid floor below it and two free cells of headroom", TemplateProblem.At(e.X, e.Y, e.Z)));
            }
        }
    }

    private static bool Inside(StructureTemplate t, int x, int y, int z)
        => x >= 0 && y >= 0 && z >= 0 && x < t.Width && y < t.Height && z < t.Length;
}
