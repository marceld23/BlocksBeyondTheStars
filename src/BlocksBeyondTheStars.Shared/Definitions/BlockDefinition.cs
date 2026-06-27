// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Data-driven definition of a block type, loaded from <c>data/blocks.json</c>.
/// The <see cref="NumericId"/> is assigned by the content registry at load time and is
/// what gets stored inside chunks.
/// </summary>
public sealed class BlockDefinition
{
    /// <summary>Unique string key, e.g. "stone", "iron_ore".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Localization key for the display name (resolved via the locale tables).</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Relative mining time multiplier; higher = slower to mine.</summary>
    public float Hardness { get; set; } = 1f;

    /// <summary>Whether this block can be mined at all.</summary>
    public bool Mineable { get; set; } = true;

    /// <summary>Whether the block is solid (collision / opaque). Air is not solid.</summary>
    public bool Solid { get; set; } = true;

    /// <summary>Tool kind required to mine effectively. <see cref="ToolKind.None"/> = hands are fine.</summary>
    public ToolKind RequiredTool { get; set; } = ToolKind.None;

    /// <summary>Minimum tool tier required to mine this block (0 = any/hands).</summary>
    public int MinToolTier { get; set; }

    /// <summary>Items produced when this block is mined.</summary>
    public List<ItemAmount> Drops { get; set; } = new();

    // --- Optional render hints (data-driven appearance for custom materials) ---
    // When null the client falls back to its built-in per-key look; when set they let a
    // material authored in the Material Editor render correctly without any code change.

    /// <summary>Surface gloss 0 (matte) .. 1 (mirror-ish), or null to use the built-in look.</summary>
    public float? Gloss { get; set; }

    /// <summary>Metalness 0 (dielectric) .. 1 (metal tints its highlight by albedo), or null for the built-in look.</summary>
    public float? Metal { get; set; }

    /// <summary>Self-illumination 0 (none) .. 1 (full glow), or null for the built-in look.</summary>
    public float? Emission { get; set; }

    /// <summary>Base RGB tint (0xRRGGBB) used for the procedural texture + color fallback, or null for the built-in palette.</summary>
    public int? Color { get; set; }

    /// <summary>
    /// Whether this block may be re-coloured by the player (the always-available "Dye"/"Glow" crafting
    /// actions). Only plain building/terrain materials are tintable; machines, doors, glass, flora and
    /// light blocks are excluded because they carry their own optics/tint logic. Set in <c>data/blocks.json</c>.
    /// </summary>
    public bool Tintable { get; set; }

    /// <summary>
    /// Whether the player may re-form this block into another shape (the always-available "Shape" crafting
    /// action — spheres, ramps, pyramids, …). Restricted to the same plain building/terrain materials as
    /// <see cref="Tintable"/>; machines, doors, glass, flora, fluids and light blocks are excluded because a
    /// custom hull would break their bespoke geometry/logic. Set by the content registry (not in JSON).
    /// </summary>
    public bool Shapeable { get; set; }

    /// <summary>
    /// Whether surface flora may root on top of this block — i.e. it is a host surface for at least one
    /// species in <see cref="FloraCatalog"/> (grass, dirt, mud, sand, stone, crystal, water, …). Derived
    /// from the catalog by the content registry (not in JSON) so client and server agree, and so worldgen,
    /// harvest-regrow and the client's "fertile ground" cue all read the same flag instead of re-deriving
    /// the host set. A plain terrain block; says nothing about which species (that stays in the catalog).
    /// </summary>
    public bool FloraHost { get; set; }

    // --- Assigned by the registry, not present in JSON ---

    /// <summary>Dense numeric id assigned at load time; what chunks actually store.</summary>
    public BlockId NumericId { get; internal set; }
}
