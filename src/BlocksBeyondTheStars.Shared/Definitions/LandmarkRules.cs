// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The vocabulary and the numbers of the LANDMARKS (#3483): single giant buildings, authored in the structure editor,
/// that the landmark stamper seats on a planet on their own — most of them as ruins derived from the intact design
/// with the instance's seed, so no two look alike. Shared by the content validation, the server's stamper, the ruin
/// pass and the editor.
/// </summary>
public static class LandmarkRules
{
    /// <summary>The structure kind a planet type's whitelist names, the placement record's kind and the loot kind.</summary>
    public const string StructureKind = "landmarks";

    /// <summary>The placement record kind (one record per body, index 0).</summary>
    public const string RecordKind = "landmark";

    public const string RuinNever = "never";
    public const string RuinAlways = "always";

    public const string ProfileMasonry = "masonry";
    public const string ProfileLattice = "lattice";
    public const string ProfileUnderground = "underground";

    /// <summary>#3515: the ruin profile of a wreck — a hull with no ground (breaches, scorch, smashed glass, dead lights;
    /// what settles is what no longer hangs on the largest piece). The shared ruin pass knows it; a landmark does not take
    /// it (a landmark stands on the ground and needs its rubble and flora), so it is not in <see cref="Profiles"/>.</summary>
    public const string ProfileHull = "hull";

    /// <summary>#3609 (ruin revision 3): the ruin profile of a giant wreck lying on a planet — the hull decay of
    /// <see cref="ProfileHull"/> anchored on the ground rows and kept cells, with scrap rubble and (on a body with air)
    /// overgrowth. Only giant ships take it; a landmark does not.</summary>
    public const string ProfileCrash = "crash";

    public static readonly string[] RuinModes = { RuinNever, RuinAlways };

    /// <summary>The ruin profiles a LANDMARK template may name (the editor cycles through these).</summary>
    public static readonly string[] Profiles = { ProfileMasonry, ProfileLattice, ProfileUnderground };

    /// <summary>Every profile the shared ruin pass knows: the landmark ones plus <see cref="ProfileHull"/> and
    /// <see cref="ProfileCrash"/>.</summary>
    public static readonly string[] AllProfiles = { ProfileMasonry, ProfileLattice, ProfileUnderground, ProfileHull, ProfileCrash };

    /// <summary>#3609: the ruin profiles a GIANT SHIP's ruin zones may name (<see cref="GiantShipRules"/>): <see cref="ProfileCrash"/>
    /// and <see cref="ProfileHull"/> (a part that decays everywhere, ground or not) and <see cref="ProfileLattice"/> (a mast or a
    /// gantry). On a planet a zone decays with the profile it names; in space every zone decays as a hull (there is no ground) —
    /// only its strength is its own. An empty zone profile takes the setting's own (crash on a planet, hull in space).</summary>
    public static readonly string[] GiantShipProfiles = { ProfileCrash, ProfileHull, ProfileLattice };

    /// <summary>A giant ship's ruin zone profile for a setting (#3609): in space (<paramref name="space"/>) always
    /// <see cref="ProfileHull"/>; on a planet the zone's own when it names one of <see cref="GiantShipProfiles"/>, else
    /// <see cref="ProfileCrash"/>.</summary>
    public static string GiantProfileOf(RuinZone zone, bool space)
    {
        if (space)
        {
            return ProfileHull;
        }

        foreach (string p in GiantShipProfiles)
        {
            if (string.Equals(zone.Profile, p, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return ProfileCrash;
    }

    public const string RequiresAtmosphere = "atmosphere";
    public const string RequiresInhabited = "inhabited";

    public static readonly string[] Conditions = { RequiresAtmosphere, RequiresInhabited };

    /// <summary>The cell kind of a cavity (#3490): "this cell is air" — the tunnels and caverns of an underground part.</summary>
    public const string CavityKind = "cavity";

    // --- the school club's landmarks (terrain generation 39): seats, protection, ruin zones ---

    /// <summary><see cref="StructureTemplate.Seat"/>: a span across water or a valley (a bridge).</summary>
    public const string SeatSpan = "span";

    /// <summary>#3640: <see cref="StructureTemplate.Seat"/> of a civilization template under water: the footprint on the sea floor,
    /// water over its top (a basin dug into the floor where the sea is too shallow). Only a civilization template may name it.</summary>
    public const string SeatSeabed = "seabed";

    /// <summary>The seat words a landmark may name (empty = the guaranteed search).</summary>
    public static readonly string[] Seats = { SeatSpan };

    /// <summary>A span's gap: the ground under it lies at least this far below the deck row (the ground row), or water fills
    /// it (its surface at least two blocks below the deck row).</summary>
    public const int SpanGapDepth = 12;

    /// <summary>A span's gap runs without a break along the long axis for at least this many blocks — or a sixth of the long
    /// axis when that is more, or its middle third when that is less — and its middle lies in the middle third.</summary>
    public const int SpanGapMinLength = 40;

    /// <summary>A span's banks: the ground under both ends (an eighth of the long axis each) lies within this many blocks of
    /// the deck row, and nowhere between them does it rise higher than that above it.</summary>
    public const int SpanBankTolerance = 4;

    /// <summary>The shortest gap a span of long axis <paramref name="length"/> needs (<see cref="SpanGapMinLength"/>).</summary>
    public static int SpanGapLength(int length) => Math.Max(length / 6, Math.Min(SpanGapMinLength, length / 3));

    /// <summary>The shortest long axis a span may have (each third needs a few columns).</summary>
    public const int SpanMinLength = 12;

    /// <summary><see cref="StructureTemplate.Protection"/>: the rule of the structure's kind (the default).</summary>
    public const string ProtectionAuto = "auto";

    /// <summary><see cref="StructureTemplate.Protection"/>: read-only for players, whatever its kind.</summary>
    public const string ProtectionProtected = "protected";

    /// <summary><see cref="StructureTemplate.Protection"/>: free to mine and build on, whatever its kind.</summary>
    public const string ProtectionOpen = "open";

    /// <summary>The protection words, in the order the editors cycle them.</summary>
    public static readonly string[] ProtectionModes = { ProtectionAuto, ProtectionProtected, ProtectionOpen };

    /// <summary>The most ruin zones one landmark may carry.</summary>
    public const int MaxRuinZones = 16;

    /// <summary>Chance that an eligible planet carries a landmark (about every third), before the structures factor.</summary>
    public const double Chance = 0.34;

    /// <summary>The largest landmark the editor and the stamper accept, per axis (#3491).</summary>
    public const int MaxSize = 256;

    /// <summary>A body must be at least this many footprints around to carry a landmark.</summary>
    public const int MinCircumferenceFootprints = 4;

    // --- marker types a landmark may carry beside the settlement ones ---

    /// <summary>#3488: a chest with authored content (settings: <c>items</c>, <c>table</c>, <c>chance</c>, <c>block</c>).</summary>
    public const string ChestMarker = "chest";

    /// <summary>#3494: a world sound emitter (settings: <c>sound</c>, <c>radius</c>, <c>volume</c>).</summary>
    public const string SoundMarker = "sound";

    /// <summary>#3495: a creature den (settings: <c>species</c>, <c>count</c>, <c>radius</c>).</summary>
    public const string DenMarker = "den";

    /// <summary>#3986: a fallen guardian giant lying in the landmark (settings: <c>facing</c> 0–3 — the lying body's
    /// <c>GuardianGiantBody</c> facing in template coordinates, 0 = head toward −Z, 1 −X, 2 +Z, 3 +X; <c>wake</c> 0/1 — whether
    /// it can still wake, default 0). The marker stands on the floor the body lies on (like a chest); the stamp writes the lying
    /// body there and pins a guardian giant record (WorldGeneration's <c>LandmarkGiants</c>).</summary>
    public const string GuardianGiantMarker = "guardian_giant";

    /// <summary>The settings key of a <see cref="GuardianGiantMarker"/>'s facing.</summary>
    public const string GuardianGiantFacingSetting = "facing";

    /// <summary>The settings key of whether a <see cref="GuardianGiantMarker"/>'s giant can wake (1) or never does (0, the default).</summary>
    public const string GuardianGiantWakeSetting = "wake";

    /// <summary>Whether the template stands as a ruin.</summary>
    public static bool IsRuin(StructureTemplate t)
        => string.Equals(t.Ruin, RuinAlways, StringComparison.OrdinalIgnoreCase);

    /// <summary>The template's ruin profile, <see cref="ProfileMasonry"/> when unset or unknown.</summary>
    public static string ProfileOf(StructureTemplate t)
    {
        foreach (string p in Profiles)
        {
            if (string.Equals(t.RuinProfile, p, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return ProfileMasonry;
    }

    /// <summary>Whether the template names the condition.</summary>
    public static bool Requires(StructureTemplate t, string condition)
    {
        foreach (string r in t.Requires)
        {
            if (string.Equals(r, condition, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A ruin zone's profile: its own when it names a landmark profile, else the template's.</summary>
    public static string ProfileOf(StructureTemplate t, RuinZone zone)
    {
        foreach (string p in Profiles)
        {
            if (string.Equals(zone.Profile, p, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return ProfileOf(t);
    }

    /// <summary>#3640: whether the template sits on the sea floor (<see cref="SeatSeabed"/>).</summary>
    public static bool IsSeabedSeat(StructureTemplate t) => string.Equals(t.Seat, SeatSeabed, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the landmark is seated as a span (<see cref="SeatSpan"/>).</summary>
    public static bool IsSpanSeat(StructureTemplate t) => string.Equals(t.Seat, SeatSpan, StringComparison.OrdinalIgnoreCase);

    /// <summary>A template's protection word, normalized: <see cref="ProtectionAuto"/> when it names none or an unknown one
    /// (no template at all reads as auto too).</summary>
    public static string ProtectionOf(StructureTemplate? t) => ProtectionOf(t?.Protection);

    /// <summary>A protection word, normalized: <see cref="ProtectionAuto"/> for empty or unknown.</summary>
    public static string ProtectionOf(string? value)
    {
        foreach (string mode in ProtectionModes)
        {
            if (string.Equals(value, mode, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return ProtectionAuto;
    }

    /// <summary>Whether a structure stamped from <paramref name="t"/> is protected: <paramref name="byKind"/> — its kind's own
    /// rule — under <see cref="ProtectionAuto"/>, else what the template says. A landmark's rule is "while intact".</summary>
    public static bool IsProtected(StructureTemplate? t, bool byKind) => ProtectionOf(t) switch
    {
        ProtectionProtected => true,
        ProtectionOpen => false,
        _ => byKind,
    };

    /// <summary>What is wrong with a template's protection word (any kind of template), or null when it is fine.</summary>
    public static string? ProtectionProblem(StructureTemplate t) => ProtectionProblemOf(t)?.English;

    /// <summary><see cref="ProtectionProblem"/> as a code the structure editor translates (#3692).</summary>
    public static TemplateProblem? ProtectionProblemOf(StructureTemplate t)
        => string.IsNullOrEmpty(t.Protection)
           || Array.FindIndex(ProtectionModes, m => string.Equals(m, t.Protection, StringComparison.OrdinalIgnoreCase)) >= 0
            ? null
            : new TemplateProblem("protection.unknown", $"unknown protection '{t.Protection}' (auto | protected | open)", t.Protection);

    /// <summary>What is wrong with a landmark's ruin zones, one sentence each: too many, a box outside the structure (an entry
    /// without a size of its own is not checked), an unknown profile, a strength outside 0..1.</summary>
    public static List<string> ZoneProblems(StructureTemplate t) => TemplateProblem.Sentences(ZoneProblemList(t));

    /// <summary><see cref="ZoneProblems"/> as codes the structure editor translates (#3692).</summary>
    public static List<TemplateProblem> ZoneProblemList(StructureTemplate t)
    {
        var problems = new List<TemplateProblem>();
        var zones = t.RuinZones ?? new List<RuinZone>();
        if (zones.Count > MaxRuinZones)
        {
            problems.Add(new TemplateProblem("zone.too_many", $"it has more than {MaxRuinZones} ruin zones", TemplateProblem.Num(MaxRuinZones)));
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

            bool sized = t.Width > 0 && t.Height > 0 && t.Length > 0;
            if (sized && (Math.Min(z.X0, z.X1) < 0 || Math.Min(z.Y0, z.Y1) < 0 || Math.Min(z.Z0, z.Z1) < 0
                          || Math.Max(z.X0, z.X1) >= t.Width || Math.Max(z.Y0, z.Y1) >= t.Height || Math.Max(z.Z0, z.Z1) >= t.Length))
            {
                problems.Add(new TemplateProblem("zone.outside", at + " reaches outside the structure", n));
            }

            if (!string.IsNullOrEmpty(z.Profile) && Array.FindIndex(Profiles, p => string.Equals(p, z.Profile, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                problems.Add(new TemplateProblem("zone.profile", $"{at} names unknown ruin profile '{z.Profile}' (masonry | lattice | underground)",
                    n, z.Profile));
            }

            if (z.Strength < 0 || z.Strength > 1)
            {
                problems.Add(new TemplateProblem("zone.strength", at + ": the strength must lie between 0 and 1", n));
            }
        }

        return problems;
    }

    /// <summary>What is wrong with a landmark's seat and protection words (the school club's fields, generation 39).</summary>
    private static void AddSeatAndProtectionProblems(StructureTemplate t, List<TemplateProblem> problems)
    {
        if (!string.IsNullOrEmpty(t.Seat) && Array.FindIndex(Seats, s => string.Equals(s, t.Seat, StringComparison.OrdinalIgnoreCase)) < 0
            && !(IsSeabedSeat(t) && t.Civilization.Length > 0))
        {
            problems.Add(new TemplateProblem("seat.unknown", $"unknown seat '{t.Seat}' (span; seabed for a civilization template)", t.Seat));
        }
        else if (IsSpanSeat(t) && !t.IsSource && Math.Max(t.Width, t.Length) < SpanMinLength)
        {
            problems.Add(new TemplateProblem("seat.span_short", $"a span needs a long axis of at least {SpanMinLength} blocks",
                TemplateProblem.Num(SpanMinLength)));
        }

        if (ProtectionProblemOf(t) is { } protection)
        {
            problems.Add(protection);
        }

        if (t.MinTerrainGeneration < 0)
        {
            problems.Add(new TemplateProblem("generation.negative", "the minimum terrain generation cannot be negative"));
        }
    }

    /// <summary>What is wrong with a landmark template, one sentence each — empty when it is fine. A generator entry (#3518)
    /// has no size of its own (its source builds it), so only its sizes that are set are checked.</summary>
    public static List<string> Problems(StructureTemplate t) => TemplateProblem.Sentences(ProblemList(t));

    /// <summary><see cref="Problems"/> as codes the structure editor translates (#3692): the same checks, the same sentences.</summary>
    public static List<TemplateProblem> ProblemList(StructureTemplate t)
    {
        var problems = new List<TemplateProblem>();
        if (!t.IsSource && (t.Width <= 0 || t.Height <= 0 || t.Length <= 0))
        {
            problems.Add(new TemplateProblem("size.none", "it has no size"));
        }
        else if (t.Width > MaxSize || t.Height > MaxSize || t.Length > MaxSize)
        {
            problems.Add(new TemplateProblem("size.too_large", $"it is larger than {MaxSize} blocks on an axis", TemplateProblem.Num(MaxSize)));
        }

        AddRuinProblems(t, problems);
        if (t.GroundRow < 0 || (t.Height > 0 && t.GroundRow >= t.Height))
        {
            problems.Add(new TemplateProblem("ground_row.outside", "the ground row lies outside the structure"));
        }

        if (t.IsModule)
        {
            problems.Add(new TemplateProblem("landmark.module", "a landmark cannot also be a kit module"));
        }

        if (t.Underground)
        {
            problems.Add(new TemplateProblem("landmark.underground", "a landmark cannot also be an underground structure (use the ground row)"));
        }

        AddSeatAndProtectionProblems(t, problems);
        problems.AddRange(ZoneProblemList(t));
        return problems;
    }

    /// <summary>The ruin mode, profile and strength and the conditions of a landmark.</summary>
    private static void AddRuinProblems(StructureTemplate t, List<TemplateProblem> problems)
    {
        if (t.Ruin.Length > 0 && Array.FindIndex(RuinModes, m => string.Equals(m, t.Ruin, StringComparison.OrdinalIgnoreCase)) < 0)
        {
            problems.Add(new TemplateProblem("ruin.mode", $"unknown ruin mode '{t.Ruin}' (never | always)", t.Ruin));
        }

        if (t.RuinProfile.Length > 0 && Array.FindIndex(Profiles, p => string.Equals(p, t.RuinProfile, StringComparison.OrdinalIgnoreCase)) < 0)
        {
            problems.Add(string.Equals(t.RuinProfile, ProfileHull, StringComparison.OrdinalIgnoreCase)
                ? new TemplateProblem("ruin.profile_wreck", "the ruin profile 'hull' is for wrecks, not landmarks (masonry | lattice | underground)", ProfileHull)
                : string.Equals(t.RuinProfile, ProfileCrash, StringComparison.OrdinalIgnoreCase)
                    ? new TemplateProblem("ruin.profile_wreck", "the ruin profile 'crash' is for giant ship wrecks, not landmarks (masonry | lattice | underground)",
                        ProfileCrash)
                    : new TemplateProblem("ruin.profile", $"unknown ruin profile '{t.RuinProfile}' (masonry | lattice | underground)", t.RuinProfile));
        }

        if (t.RuinStrength < 0 || t.RuinStrength > 1)
        {
            problems.Add(new TemplateProblem("ruin.strength", "the ruin strength must lie between 0 and 1"));
        }

        foreach (string r in t.Requires)
        {
            if (Array.FindIndex(Conditions, c => string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                problems.Add(new TemplateProblem("condition.unknown", $"unknown condition '{r}' (atmosphere | inhabited)", r));
            }
        }
    }
}
