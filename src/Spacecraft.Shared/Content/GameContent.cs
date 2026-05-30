using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Localization;
using Spacecraft.Shared.Primitives;

namespace Spacecraft.Shared.Content;

/// <summary>
/// The shared, validated registry of all data-driven game content (blocks, items,
/// recipes, blueprints, ship modules) plus localization tables. Built once and shared
/// by client and server so their game rules cannot drift apart.
///
/// Block numeric ids are assigned deterministically (sorted by key, air = 0) so a given
/// content set always yields the same palette. NOTE: adding blocks shifts ids; a future
/// version will persist the palette into the savegame to stay save-compatible.
/// </summary>
public sealed class GameContent
{
    private readonly Dictionary<string, BlockDefinition> _blocks;
    private readonly Dictionary<string, ItemDefinition> _items;
    private readonly Dictionary<string, RecipeDefinition> _recipes;
    private readonly Dictionary<string, BlueprintDefinition> _blueprints;
    private readonly Dictionary<string, ShipModuleDefinition> _shipModules;
    private readonly Dictionary<string, PlanetType> _planets;
    private readonly BlockDefinition?[] _blocksById;
    private readonly Dictionary<GameLocale, Dictionary<string, string>> _locales;

    public IReadOnlyDictionary<string, BlockDefinition> Blocks => _blocks;
    public IReadOnlyDictionary<string, ItemDefinition> Items => _items;
    public IReadOnlyDictionary<string, RecipeDefinition> Recipes => _recipes;
    public IReadOnlyDictionary<string, BlueprintDefinition> Blueprints => _blueprints;
    public IReadOnlyDictionary<string, ShipModuleDefinition> ShipModules => _shipModules;
    public IReadOnlyDictionary<string, PlanetType> Planets => _planets;

    public GameContent(
        IEnumerable<BlockDefinition> blocks,
        IEnumerable<ItemDefinition> items,
        IEnumerable<RecipeDefinition> recipes,
        IEnumerable<BlueprintDefinition> blueprints,
        IEnumerable<ShipModuleDefinition> shipModules,
        IDictionary<GameLocale, Dictionary<string, string>> locales,
        IEnumerable<PlanetType>? planets = null)
    {
        _blocks = blocks.ToDictionary(b => b.Key);
        _items = items.ToDictionary(i => i.Key);
        _recipes = recipes.ToDictionary(r => r.Key);
        _blueprints = blueprints.ToDictionary(b => b.Key);
        _shipModules = shipModules.ToDictionary(m => m.Key);
        _planets = (planets ?? Enumerable.Empty<PlanetType>()).ToDictionary(p => p.Key);
        _locales = new Dictionary<GameLocale, Dictionary<string, string>>(locales);

        AssignBlockIds();

        // Palette lookup: index by numeric id (air at 0).
        ushort max = 0;
        foreach (var b in _blocks.Values)
        {
            if (b.NumericId.Value > max)
            {
                max = b.NumericId.Value;
            }
        }

        _blocksById = new BlockDefinition?[max + 1];
        foreach (var b in _blocks.Values)
        {
            _blocksById[b.NumericId.Value] = b;
        }
    }

    private void AssignBlockIds()
    {
        ushort next = 1; // 0 reserved for air
        foreach (var key in _blocks.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var block = _blocks[key];
            block.NumericId = key == "air" ? BlockId.Air : new BlockId(next++);
        }
    }

    public BlockDefinition? GetBlock(string key) => _blocks.TryGetValue(key, out var b) ? b : null;

    public BlockDefinition? BlockById(BlockId id)
        => id.Value < _blocksById.Length ? _blocksById[id.Value] : null;

    public ItemDefinition? GetItem(string key) => _items.TryGetValue(key, out var i) ? i : null;
    public RecipeDefinition? GetRecipe(string key) => _recipes.TryGetValue(key, out var r) ? r : null;
    public BlueprintDefinition? GetBlueprint(string key) => _blueprints.TryGetValue(key, out var b) ? b : null;
    public ShipModuleDefinition? GetShipModule(string key) => _shipModules.TryGetValue(key, out var m) ? m : null;
    public PlanetType? GetPlanet(string key) => _planets.TryGetValue(key, out var p) ? p : null;

    public int MaxStackOf(string itemKey) => _items.TryGetValue(itemKey, out var i) ? i.MaxStack : 1;

    /// <summary>Builds a localizer for the given locale, with English as the fallback table.</summary>
    public Localizer CreateLocalizer(GameLocale locale)
    {
        var active = _locales.TryGetValue(locale, out var table) ? table : new Dictionary<string, string>();
        var fallback = _locales.TryGetValue(GameLocale.English, out var en) ? en : active;
        return new Localizer(locale, active, fallback);
    }

    /// <summary>
    /// Cross-validates all references between definitions. Throws
    /// <see cref="ContentValidationException"/> if anything is inconsistent.
    /// </summary>
    public void Validate()
    {
        var problems = new List<string>();

        void RequireItem(string ctx, string? itemKey)
        {
            if (!string.IsNullOrEmpty(itemKey) && !_items.ContainsKey(itemKey!))
            {
                problems.Add($"{ctx} references unknown item '{itemKey}'.");
            }
        }

        void RequireBlueprint(string ctx, string? bpKey)
        {
            if (!string.IsNullOrEmpty(bpKey) && !_blueprints.ContainsKey(bpKey!))
            {
                problems.Add($"{ctx} references unknown blueprint '{bpKey}'.");
            }
        }

        foreach (var block in _blocks.Values)
        {
            foreach (var drop in block.Drops)
            {
                RequireItem($"Block '{block.Key}' drop", drop.Item);
            }
        }

        foreach (var item in _items.Values)
        {
            if (!string.IsNullOrEmpty(item.PlacesBlock) && !_blocks.ContainsKey(item.PlacesBlock!))
            {
                problems.Add($"Item '{item.Key}' places unknown block '{item.PlacesBlock}'.");
            }
        }

        foreach (var recipe in _recipes.Values)
        {
            RequireBlueprint($"Recipe '{recipe.Key}'", recipe.RequiredBlueprint);
            foreach (var input in recipe.Inputs)
            {
                RequireItem($"Recipe '{recipe.Key}' input", input.Item);
            }

            foreach (var output in recipe.Outputs)
            {
                RequireItem($"Recipe '{recipe.Key}' output", output.Item);
            }

            if (recipe.Outputs.Count == 0)
            {
                problems.Add($"Recipe '{recipe.Key}' has no outputs.");
            }
        }

        foreach (var bp in _blueprints.Values)
        {
            foreach (var pre in bp.Prerequisites)
            {
                RequireBlueprint($"Blueprint '{bp.Key}' prerequisite", pre);
            }

            foreach (var cost in bp.UnlockCost)
            {
                RequireItem($"Blueprint '{bp.Key}' unlock cost", cost.Item);
            }
        }

        foreach (var module in _shipModules.Values)
        {
            RequireBlueprint($"Ship module '{module.Key}'", module.RequiredBlueprint);
            foreach (var cost in module.BuildCost)
            {
                RequireItem($"Ship module '{module.Key}' build cost", cost.Item);
            }
        }

        void RequireBlock(string ctx, string? blockKey)
        {
            if (!string.IsNullOrEmpty(blockKey) && !_blocks.ContainsKey(blockKey!))
            {
                problems.Add($"{ctx} references unknown block '{blockKey}'.");
            }
        }

        foreach (var planet in _planets.Values)
        {
            RequireBlock($"Planet '{planet.Key}' surface", planet.SurfaceBlock);
            RequireBlock($"Planet '{planet.Key}' sub-surface", planet.SubSurfaceBlock);
            RequireBlock($"Planet '{planet.Key}' deep", planet.DeepBlock);
            foreach (var ore in planet.Ores)
            {
                RequireBlock($"Planet '{planet.Key}' ore", ore.Block);
            }
        }

        if (problems.Count > 0)
        {
            throw new ContentValidationException(problems);
        }
    }
}
