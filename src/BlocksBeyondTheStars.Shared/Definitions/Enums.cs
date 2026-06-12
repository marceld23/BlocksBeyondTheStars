namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>High-level item classification driving inventory/UI behaviour.</summary>
public enum ItemCategory
{
    Material,
    Block,
    Tool,
    Consumable,
    Component,
}

/// <summary>Kind of tool, used to decide whether a block can be mined.</summary>
public enum ToolKind
{
    None,
    Drill,
    BlockPlacer,
    Scanner,
    Repair,
    Weapon,

    /// <summary>A right-click-activated gadget (item 36) — field medkit, stasis projector, terrain blaster.
    /// The specific effect is keyed by the item id in the server's gadget handler.</summary>
    Gadget,
}

/// <summary>
/// Where a recipe can be crafted. Mirrors the crafting locations from the game design
/// (personal quick-crafting, workshop, refinery, detoxifier, market).
/// </summary>
public enum CraftingStation
{
    Hand,
    Workshop,
    Refinery,
    Detoxifier,
    Market,
}
