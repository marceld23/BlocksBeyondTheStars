// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The tuning of the wormholes (#2242), from <c>data/wormholes.json</c>. A data folder without the file plays with
/// these defaults; the world option <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.Wormholes"/> decides whether a galaxy has any.
/// </summary>
public sealed class WormholeDefinition
{
    /// <summary>Expected wormhole PAIRS per 12 star systems for each frequency of the world option (the fractional part
    /// is rolled from the seed). 12 systems is the standard universe: "rare" gives exactly one pair to find.</summary>
    public double VeryRarePairsPer12 { get; set; } = 0.5;
    public double RarePairsPer12 { get; set; } = 1.0;
    public double NormalPairsPer12 { get; set; } = 1.5;
    public double FrequentPairsPer12 { get; set; } = 2.5;

    /// <summary>Two ends sit at least this far apart on the star map (map units, the map is 1000 wide) — a wormhole to
    /// the neighbouring star would be pointless. Relaxed to "the farthest free system" when no system is that far.</summary>
    public float MinMapDistance { get; set; } = 350f;

    /// <summary>How far beyond the outermost body's orbit the rift opens (system units), plus a seeded spread.</summary>
    public float EdgeMargin { get; set; } = 320f;
    public float EdgeSpread { get; set; } = 220f;

    /// <summary>Flight units: within this the pilot is offered "fly through" — the client's prompt; the server accepts
    /// a transit a little beyond it.</summary>
    public float TransitRange { get; set; } = 26f;

    /// <summary>Flight units in front of the twin rift where the ship comes out.</summary>
    public float ArrivalOffset { get; set; } = 45f;

    /// <summary>Seconds after an arrival during which the same pilot cannot fly through again (no bouncing).</summary>
    public double ArrivalLockSeconds { get; set; } = 6.0;
}
