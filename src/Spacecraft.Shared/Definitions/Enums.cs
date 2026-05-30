namespace Spacecraft.Shared.Definitions;

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
}

/// <summary>
/// Where a recipe can be crafted. Mirrors the crafting locations from the game design
/// (personal quick-crafting, workshop, refinery, lab, machine room).
/// </summary>
public enum CraftingStation
{
    Hand,
    Workshop,
    Refinery,
    Lab,
    MachineRoom,
}
