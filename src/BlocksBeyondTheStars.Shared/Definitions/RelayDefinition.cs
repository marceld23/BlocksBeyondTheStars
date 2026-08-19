// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The SPS relay upgrade (#1125, Track F): what it costs to turn a commissioned player station into a
/// relay, and how close two relay systems must be (star-map units) for a jump lane to form between them.
/// Loaded from <c>data/relay.json</c>; when the file is absent the feature simply does nothing (the
/// achievements pattern), so a stripped-down data folder still loads.
/// </summary>
public sealed class RelayDefinition
{
    /// <summary>The full bill of materials, contributed co-op in any order (bulk metals + reactor fuel +
    /// circuit boards — the late-game ore chain's consumer, #1106).</summary>
    public List<ItemAmount> Costs { get; set; } = new();

    /// <summary>Maximum star-map distance between two relay systems for a jump lane to link them.
    /// "Adjacent" in lane terms — measured over <c>StarSystem.MapX/MapY</c>, so it is seed-stable.</summary>
    public float LinkRange { get; set; } = 500f;
}
