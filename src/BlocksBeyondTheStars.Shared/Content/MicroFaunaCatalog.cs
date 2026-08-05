// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The canonical list of micro-fauna kind keys (#757). The critters themselves are simulated purely
/// client-side (client <c>MicroFauna.Kinds</c> owns visuals, motion and rosters — this list must stay in
/// sync with it; the client asserts the match at startup), but the SERVER needs the keys to validate a
/// <c>ScanIntent("microfauna", key)</c> against — the same existence-only check block scans get. Scan
/// locale keys follow <c>ui.scan.subject.&lt;key&gt;</c>.
/// </summary>
public static class MicroFaunaCatalog
{
    public static readonly string[] Keys =
    {
        "butterfly", "bee", "dragonfly", "fly", "moth", "firefly",
        "beetle", "ant", "caterpillar", "worm", "snail", "spider",
        "fish", "tadpole", "waterbeetle", "strider", "glowworm",
        "prismwing", "crystalbeetle", "embermite", "ashhopper", "frostmite",
        "gasbag", "sporedrifter", "wisp", "sandskimmer", "glowplankton", "cavemoth",
    };

    private static readonly HashSet<string> Known = new(Keys);

    /// <summary>Whether the key names a known micro-fauna kind (existence check for scan intents).</summary>
    public static bool IsKnown(string? key) => key is not null && Known.Contains(key);
}
