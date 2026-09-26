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

    /// <summary>Coarse grouping used by building UIs (editor palettes) to sort blocks into sections:
    /// "building", "terrain", "ore", "flora", "light", "door" or "machine". Localized section titles
    /// live under <c>ui.cat.*</c> in the locale tables.</summary>
    public string Category { get; set; } = "building";

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

    /// <summary>
    /// A weighted table from which ONE entry is drawn when the block is mined, on top of <see cref="Drops"/>
    /// (school club wave 3, #1761: scrap yields "whatever is inside"). The draw is a hash of the cell and the
    /// world seed, never a random stream, so a block re-placed on the same cell yields the same thing again —
    /// nothing to farm by placing and breaking. An entry with an empty item is the "nothing" outcome. Null or
    /// empty = the classic fixed drops only.
    /// </summary>
    public List<WeightedDrop>? RandomDrops { get; set; }

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
    /// Makes the block a real light source (#2036): the colour (0xRRGGBB) it floods into its surroundings on top
    /// of its own <see cref="Emission"/> glow. A darker colour reaches less far — each channel fades one step per
    /// block. <c>0</c> opts a block out; null leaves it to <see cref="BlockLight.NaturalColorOf"/>'s fallback for
    /// authored fixtures. Set in <c>data/blocks.json</c>.
    /// </summary>
    public int? LightColor { get; set; }

    /// <summary>
    /// What the block's tile shows (#1900): <c>"material"</c> (a surface — stone, planks, steel; the default) or
    /// <c>"picture"</c> (a drawing of the whole object — the bed seen from above, a flower pot). A picture only fits
    /// the face it was drawn for, so a picture block that renders as a non-cube form declares <see cref="Faces"/>.
    /// Every block the server stamps with a form must say which one it is (a content test holds that).
    /// </summary>
    public string? TileKind { get; set; }

    /// <summary>Texture slots per part and side of the block's built-in form (#1900), see
    /// <see cref="BlockFaceTexture"/>. Null = every face shows the slice of the block's own tile it covers.</summary>
    public List<BlockFaceTexture>? Faces { get; set; }

    /// <summary>Animation of the block's OFFICIAL texture (#1957): the speed of the frames bundled as
    /// <c>Resources/textures/&lt;key&gt;__anim.bytes</c> (frames 2..n; frame 1 is the ordinary tile). Null = a still
    /// tile. <c>tools/merge_texture.py</c> writes it when it adopts an animated texture.</summary>
    public BlockAnimation? Anim { get; set; }

    /// <summary>
    /// Whether this block may be re-coloured by the player (the always-available "Dye"/"Glow" crafting
    /// actions). Plain building/terrain materials, glass and the light fixtures are tintable (#1126); machines,
    /// doors and flora are excluded because they carry their own optics/tint logic. Set in <c>data/blocks.json</c>.
    /// </summary>
    public bool Tintable { get; set; }

    /// <summary>
    /// Whether this block catches fire (#785). Set in <c>data/blocks.json</c> for plants, wood and leaves.
    /// Ground cover (grass, alien grass, mycelium) is deliberately NOT flammable, so a brush fire can't run
    /// away across a whole biome, and neither are aquatic plants (kelp, seagrass, coral, water lilies).
    /// Replaces the old key-prefix rule, which missed pine needles, palm fronds and mushrooms — a burning
    /// pine kept its canopy — while setting underwater kelp alight.
    /// </summary>
    public bool Flammable { get; set; }

    /// <summary>
    /// Whether this block is loose material that FALLS when its support goes (#1319 — sand, ash, snow): the
    /// server settles it instantly onto the next support below, sinks it one cell per fluid step through
    /// water/lava (replacing the fluid), and leaves a carved form of it in place. Set in <c>data/blocks.json</c>.
    /// Only ever woken by a mutation (mining, placing, burning, a retracting fluid) — generated overhangs stay
    /// until touched, so worldgen and its determinism are untouched.
    /// </summary>
    public bool Granular { get; set; }

    /// <summary>
    /// Whether this block holds air as a wall of a sealed base room (#794). Airtight blocks are what the
    /// base's sealed-room life-support fill stops against; everything else leaks. Defaults are derived by
    /// the content registry from the category — terrain/building/ore/machine/light seal (natural rock
    /// included, so a dug-out cave can become a habitat), flora and doors don't — minus a curated
    /// loose-material list (dirt, sand, snow, …). A block may also opt in explicitly via <c>airtight</c>
    /// in <c>data/blocks.json</c> (the force field and the energy gate's membrane do). Only a full-cube
    /// cell seals — shaped cells are treated as leaky at fill time.
    /// </summary>
    public bool Airtight { get; set; }

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

/// <summary>How a block's official texture animates (#1957).</summary>
public sealed class BlockAnimation
{
    /// <summary>Frames per second — one of <c>TextureTiles.AllowedFps</c> (2, 4, 8, 12).</summary>
    public int Fps { get; set; }
}
