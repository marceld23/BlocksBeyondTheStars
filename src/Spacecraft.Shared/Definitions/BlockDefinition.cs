using Spacecraft.Shared.Primitives;

namespace Spacecraft.Shared.Definitions;

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

    // --- Assigned by the registry, not present in JSON ---

    /// <summary>Dense numeric id assigned at load time; what chunks actually store.</summary>
    public BlockId NumericId { get; internal set; }
}
