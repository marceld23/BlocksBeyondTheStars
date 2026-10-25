// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The trunks of the generated trees — the counterpart of <see cref="TreeFoliage"/>. They take the world's bark colour
/// (the block shader's bark mode) and show end grain on their cut faces. The pale bark slot (#2851, generation 29) gets a
/// LIGHT bark colour band, so birches and bottle trees stand out against the dark trunks of the same wood. The giant tree's
/// trunk is mahogany on every world (#2968) and drops itself, so a giant trunk can be patched without a seam.
/// </summary>
public static class TreeWood
{
    /// <summary>The common trunk.</summary>
    public const string LogKey = "wood_log";

    /// <summary>The pale bark slot (#2851): birch-like trunks with a light bark colour.</summary>
    public const string PaleLogKey = "wood_log_b";

    /// <summary>The giant tree's trunk (mirrors <c>WorldGenerator.GiantLogKey</c>, which Shared cannot reference).</summary>
    public const string GiantLogKey = "giant_log";

    /// <summary>Every trunk block that takes the world's bark colour.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { LogKey, PaleLogKey, GiantLogKey };

    /// <summary>True for a trunk block that takes the world's bark colour.</summary>
    public static bool IsKey(string? key) => key is LogKey or PaleLogKey or GiantLogKey;
}
