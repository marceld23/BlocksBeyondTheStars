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

    /// <summary>The second leaf slot (#2851, generation 29): another tree kind's crown, with a look and a colour of its own.</summary>
    public const string LeavesBKey = "tree_leaves_b";

    /// <summary>The third leaf slot (#2851, generation 29).</summary>
    public const string LeavesCKey = "tree_leaves_c";

    /// <summary>The blossom tree's crown (#2853, generation 29) — it keeps its painted blossom colours on every world.</summary>
    public const string BlossomKey = "blossom_leaves";

    /// <summary>Every crown block.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        LeavesKey, NeedlesKey, FrondKey, GiantLeavesKey, FifiPlant.LeafKey, LeavesBKey, LeavesCKey, BlossomKey,
    };

    /// <summary>The three leaf slots (#2851): the crowns a world gives three different looks from one shared pool, so the
    /// tree kinds of one wood wear three different leaves.</summary>
    public static readonly IReadOnlyList<string> LeafSlotKeys = new[] { LeavesKey, LeavesBKey, LeavesCKey };

    /// <summary>The crowns that take this world's flora colour (every crown but the Fifi plant's and the blossom tree's,
    /// which keep their painted colours).</summary>
    public static readonly IReadOnlyList<string> TintedKeys = new[]
    {
        LeavesKey, NeedlesKey, FrondKey, GiantLeavesKey, LeavesBKey, LeavesCKey,
    };

    /// <summary>True for a crown block — see the class summary for what that implies.</summary>
    public static bool IsKey(string? key) => key is LeavesKey or NeedlesKey or FrondKey or GiantLeavesKey or FifiPlant.LeafKey
        or LeavesBKey or LeavesCKey or BlossomKey;

    /// <summary>True for a crown that takes the world's flora colour (see <see cref="TintedKeys"/>).</summary>
    public static bool IsTinted(string? key) => key is LeavesKey or NeedlesKey or FrondKey or GiantLeavesKey or LeavesBKey or LeavesCKey;
}
