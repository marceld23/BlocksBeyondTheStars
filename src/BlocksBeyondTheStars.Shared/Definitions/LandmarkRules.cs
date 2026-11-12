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

    public static readonly string[] RuinModes = { RuinNever, RuinAlways };

    /// <summary>The ruin profiles a LANDMARK template may name (the editor cycles through these).</summary>
    public static readonly string[] Profiles = { ProfileMasonry, ProfileLattice, ProfileUnderground };

    /// <summary>Every profile the shared ruin pass knows: the landmark ones plus <see cref="ProfileHull"/>.</summary>
    public static readonly string[] AllProfiles = { ProfileMasonry, ProfileLattice, ProfileUnderground, ProfileHull };

    public const string RequiresAtmosphere = "atmosphere";
    public const string RequiresInhabited = "inhabited";

    public static readonly string[] Conditions = { RequiresAtmosphere, RequiresInhabited };

    /// <summary>The cell kind of a cavity (#3490): "this cell is air" — the tunnels and caverns of an underground part.</summary>
    public const string CavityKind = "cavity";

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

    /// <summary>What is wrong with a landmark template, one sentence each — empty when it is fine. A generator entry (#3518)
    /// has no size of its own (its source builds it), so only its sizes that are set are checked.</summary>
    public static List<string> Problems(StructureTemplate t)
    {
        var problems = new List<string>();
        if (!t.IsSource && (t.Width <= 0 || t.Height <= 0 || t.Length <= 0))
        {
            problems.Add("it has no size");
        }
        else if (t.Width > MaxSize || t.Height > MaxSize || t.Length > MaxSize)
        {
            problems.Add($"it is larger than {MaxSize} blocks on an axis");
        }

        if (t.Ruin.Length > 0 && Array.FindIndex(RuinModes, m => string.Equals(m, t.Ruin, StringComparison.OrdinalIgnoreCase)) < 0)
        {
            problems.Add($"unknown ruin mode '{t.Ruin}' (never | always)");
        }

        if (t.RuinProfile.Length > 0 && Array.FindIndex(Profiles, p => string.Equals(p, t.RuinProfile, StringComparison.OrdinalIgnoreCase)) < 0)
        {
            problems.Add(string.Equals(t.RuinProfile, ProfileHull, StringComparison.OrdinalIgnoreCase)
                ? "the ruin profile 'hull' is for wrecks, not landmarks (masonry | lattice | underground)"
                : $"unknown ruin profile '{t.RuinProfile}' (masonry | lattice | underground)");
        }

        if (t.RuinStrength < 0 || t.RuinStrength > 1)
        {
            problems.Add("the ruin strength must lie between 0 and 1");
        }

        foreach (string r in t.Requires)
        {
            if (Array.FindIndex(Conditions, c => string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                problems.Add($"unknown condition '{r}' (atmosphere | inhabited)");
            }
        }

        if (t.GroundRow < 0 || (t.Height > 0 && t.GroundRow >= t.Height))
        {
            problems.Add("the ground row lies outside the structure");
        }

        if (t.IsModule)
        {
            problems.Add("a landmark cannot also be a kit module");
        }

        if (t.Underground)
        {
            problems.Add("a landmark cannot also be an underground structure (use the ground row)");
        }

        return problems;
    }
}
