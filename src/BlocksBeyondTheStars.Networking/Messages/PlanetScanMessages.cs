// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Client → server: run the ship's planet scanner (#2140) on a body. Needs the <c>planet_scanner</c> module and the
/// player aboard; the body must lie in the current star system. Empty <see cref="BodyId"/> = the body the ship is at.
/// </summary>
public sealed class PlanetScanIntent
{
    public string BodyId { get; set; } = string.Empty;
}

/// <summary>One ore vein in a <see cref="PlanetScanResult"/>.</summary>
public sealed class NetPlanetOre
{
    /// <summary>The vein's block key ("iron_ore") — the client names it from <c>block.&lt;key&gt;.name</c>.</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>0 = rare, 1 = moderate, 2 = common on THIS world (see <see cref="PlanetScanResult"/> bands).</summary>
    public byte Abundance { get; set; }

    /// <summary>The depth below the surface where the vein starts.</summary>
    public int MinDepth { get; set; }

    /// <summary>Needs a tier-2 drill.</summary>
    public bool RareTier { get; set; }
}

/// <summary>
/// Server → client: the planet scanner's report (#2140 — "which resources does this planet have?"). Computed from the
/// same per-world rolls the terrain generator uses, so it matches what the ground holds.
/// </summary>
public sealed class PlanetScanResult
{
    public string BodyId { get; set; } = string.Empty;

    /// <summary>The body's display name and planet type key (the client names the type via <c>planet.&lt;key&gt;.name</c>).</summary>
    public string BodyName { get; set; } = string.Empty;
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>This world's overall ore richness: 0 = lean, 1 = average, 2 = rich.</summary>
    public byte Richness { get; set; }

    /// <summary>The veins, most abundant first.</summary>
    public NetPlanetOre[] Ores { get; set; } = System.Array.Empty<NetPlanetOre>();

    /// <summary>Extras beyond the veins.</summary>
    public bool OilPockets { get; set; }
    public bool DataCaches { get; set; }
    public bool SurfaceOutcrops { get; set; }
    public bool CraterMetals { get; set; }
    public bool GasWorld { get; set; }

    /// <summary>#2239/#2240: the ship-scanner tier that produced this report — 1 = the overview (every ship's cockpit),
    /// 2 = + the resources (Deep scanner), 3 = + rare ores and secrets (Quantum scanner). 0 from an older server.</summary>
    public byte Tier { get; set; }

    /// <summary>#2239: true when the resources were left out because the tier is too low — the card offers the Deep
    /// scanner instead of an empty list.</summary>
    public bool ResourcesLocked { get; set; }

    /// <summary>#2239: the overview card — one row per topic, in display order.</summary>
    public NetOverviewRow[] Rows { get; set; } = System.Array.Empty<NetOverviewRow>();

    /// <summary>#2239: the overall traffic light: 0 = harmless, 1 = take care, 2 = dangerous.</summary>
    public byte Danger { get; set; }

    /// <summary>#2239: knowledge this report paid (the first overview of a body), 0 otherwise.</summary>
    public int KnowledgeGained { get; set; }
}

/// <summary>#2239: one row of the planet overview card — locale keys and a level, never prose.</summary>
public sealed class NetOverviewRow
{
    /// <summary>The topic (<c>air</c>, <c>temperature</c>, <c>gravity</c>, <c>weather</c>, <c>water</c>, <c>lava</c>,
    /// <c>plants</c>, <c>animals</c>, <c>machines</c>, <c>terrain</c>, <c>structures</c>, <c>frontier</c>) — picks the
    /// row's icon and its label <c>ui.overview.topic.&lt;topic&gt;</c>.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Locale key of the short answer ("breathable", "freezing", …).</summary>
    public string ValueKey { get; set; } = string.Empty;

    /// <summary>0 = harmless (green), 1 = take care (yellow), 2 = dangerous (red), 3 = neutral information (white).</summary>
    public byte Level { get; set; }

    /// <summary>Optional locale key of a second line ("protection needed", "water hurts", …), empty when none.</summary>
    public string DetailKey { get; set; } = string.Empty;

    /// <summary>Optional language-neutral figure appended to the answer ("1.3 g", "7"), empty when none.</summary>
    public string Extra { get; set; } = string.Empty;
}
