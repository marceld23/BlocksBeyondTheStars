namespace Spacecraft.Shared.Definitions;

/// <summary>
/// Data-driven crafting recipe, loaded from <c>data/recipes.json</c>. The server
/// validates station, blueprint unlock and material availability before crafting.
/// </summary>
public sealed class RecipeDefinition
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Where this recipe can be crafted.</summary>
    public CraftingStation Station { get; set; } = CraftingStation.Hand;

    /// <summary>Blueprint that must be unlocked first; null/empty = available from the start.</summary>
    public string? RequiredBlueprint { get; set; }

    public List<ItemAmount> Inputs { get; set; } = new();
    public List<ItemAmount> Outputs { get; set; } = new();
}
