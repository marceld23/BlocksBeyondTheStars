// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The Fifi plant (#2085, terrain generation 16) — Sophie's plant from the school club: as tall as a tree, a green trunk, a
/// crown of yellow leaves, pink blossoms that glow and light up their surroundings, and berries that always grow back. The
/// plant is authored content: the same blocks, colours and edible berries on every world (world generation places it in
/// groves, <c>FifiPlantRules</c>). This names its four block keys once for world generation, the server and the client.
/// </summary>
public static class FifiPlant
{
    /// <summary>The green trunk.</summary>
    public const string StemKey = "fifi_stem";

    /// <summary>The yellow leaves — cutout foliage like a tree crown, but never re-coloured by the world's flora hue.</summary>
    public const string LeafKey = "fifi_leaf";

    /// <summary>The pink blossoms: they glow and cast light (<c>lightColor</c> in <c>data/blocks.json</c>).</summary>
    public const string BlossomKey = "fifi_blossom";

    /// <summary>The berries hanging under the leaves — an authored catalog species (<see cref="FloraCatalog.Species.Authored"/>).</summary>
    public const string BerriesKey = "flora_fifi_berries";

    /// <summary>Every block the plant is made of.</summary>
    public static readonly IReadOnlyList<string> PartKeys = new[] { StemKey, LeafKey, BlossomKey, BerriesKey };

    /// <summary>The scan ledger key every part shares, so the trunk, a leaf, a blossom and the berries count as ONE
    /// discovery — the way a tree's trunk and leaves do.</summary>
    public const string LedgerKey = "flora:fifi_plant";

    /// <summary>The scan subject every part reads as — the client names it through <c>ui.scan.subject.fifi_plant</c>.</summary>
    public const string ScanSubject = "fifi_plant";

    /// <summary>True for any block of the Fifi plant.</summary>
    public static bool IsPart(string? key) => key is StemKey or LeafKey or BlossomKey or BerriesKey;

    /// <summary>True for the plant's leaf block — cutout foliage in its own colour (the tree crowns take the world's hue).</summary>
    public static bool IsAuthoredFoliage(string? key) => key == LeafKey;
}
