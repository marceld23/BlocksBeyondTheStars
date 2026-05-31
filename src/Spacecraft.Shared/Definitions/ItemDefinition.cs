namespace Spacecraft.Shared.Definitions;

/// <summary>Tool-specific properties, present only on items of category <see cref="ItemCategory.Tool"/>.</summary>
public sealed class ToolProperties
{
    public ToolKind Kind { get; set; } = ToolKind.Drill;

    /// <summary>Capability tier; must be &gt;= a block's MinToolTier to mine it.</summary>
    public int Tier { get; set; } = 1;

    /// <summary>Energy consumed per use (0 = no energy cost).</summary>
    public float EnergyPerUse { get; set; }

    /// <summary>Weapon hit damage (<see cref="ToolKind.Weapon"/>); 0 falls back to a tier-scaled default.</summary>
    public float Damage { get; set; }

    /// <summary>Weapon reach in blocks (melee = short, ranged = long); 0 uses the default attack reach.</summary>
    public float Range { get; set; }
}

/// <summary>
/// Data-driven definition of an item (material, block-in-hand, tool, consumable, component),
/// loaded from <c>data/items.json</c>.
/// </summary>
public sealed class ItemDefinition
{
    /// <summary>Unique string key, e.g. "iron_ingot", "titanium_drill".</summary>
    public string Key { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;

    public ItemCategory Category { get; set; } = ItemCategory.Material;

    /// <summary>Maximum stack size in a single inventory slot.</summary>
    public int MaxStack { get; set; } = 99;

    /// <summary>If this item places a block, the block key it places (else null/empty).</summary>
    public string? PlacesBlock { get; set; }

    /// <summary>Tool properties when <see cref="Category"/> is <see cref="ItemCategory.Tool"/>.</summary>
    public ToolProperties? Tool { get; set; }

    /// <summary>
    /// Health change when this item is consumed (<see cref="ItemCategory.Consumable"/> only):
    /// positive = food/medicine that heals, negative = poison that harms. 0 = not eat-consumable.
    /// </summary>
    public float ConsumeHealth { get; set; }

    /// <summary>Hunger restored when this item is eaten (food); 0 = doesn't satiate.</summary>
    public float ConsumeHunger { get; set; }

    // --- Equipment effects (applied while the item is carried) ---

    /// <summary>Armor: 0..1 fraction of incoming physical damage this piece blocks (summed, capped).</summary>
    public float ArmorResistance { get; set; }

    /// <summary>Extra maximum suit oxygen this item grants (e.g. a bigger tank).</summary>
    public float OxygenBonus { get; set; }

    /// <summary>Scanner: multiplies knowledge gained from a first scan (1 = no bonus).</summary>
    public float ScanKnowledgeMultiplier { get; set; } = 1f;
}
