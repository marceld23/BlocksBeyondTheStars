// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The foliage of tree crowns — leaves, needles, fronds and the Fifi plant's yellow leaves. They render as cutout
/// cubes (the tile's baked alpha mask punches the holes), and since #2184 a body walks through them the way it walks
/// through grass: the client meshes no collider for them and they give no footing. The trunks (<c>wood_log</c>,
/// <c>giant_log</c>, <see cref="FifiPlant.StemKey"/>) are other blocks and stay solid. The <c>Solid</c> flag is
/// untouched, so a crown still blocks line of sight and light. This names the keys once for the mesher, the texture
/// rules, the door jamb rule and the creature probes, which used to carry their own (drifted) copies of the list.
/// </summary>
public static class TreeFoliage
{
    /// <summary>The broadleaf crown of the common tree (also the giant fern's fan and the giant cactus).</summary>
    public const string LeavesKey = "tree_leaves";

    /// <summary>The conifer crown.</summary>
    public const string NeedlesKey = "pine_needles";

    /// <summary>The palm crown.</summary>
    public const string FrondKey = "palm_frond";

    /// <summary>The giant tree's crown (mirrors <c>WorldGenerator.GiantLeavesKey</c>, which Shared cannot reference).</summary>
    public const string GiantLeavesKey = "giant_leaves";

    /// <summary>Every crown block.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { LeavesKey, NeedlesKey, FrondKey, GiantLeavesKey, FifiPlant.LeafKey };

    /// <summary>True for a crown block — see the class summary for what that implies.</summary>
    public static bool IsKey(string? key) => key is LeavesKey or NeedlesKey or FrondKey or GiantLeavesKey or FifiPlant.LeafKey;
}
