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

    /// <summary>For <see cref="CraftingStation.Market"/> recipes: which settlement vendor THEME offers this
    /// barter (e.g. "miners", "traders", "researchers", "settlers"). Empty = offered everywhere (every vendor
    /// and the ship's own trade console), so different settlements (a mining village vs a trade city) post
    /// different goods. Ignored for non-market recipes.</summary>
    public string MarketTheme { get; set; } = string.Empty;

    public List<ItemAmount> Inputs { get; set; } = new();
    public List<ItemAmount> Outputs { get; set; } = new();
}
