using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Shared.Content;

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
    private readonly Dictionary<string, ShipDefinition> _ships;
    private readonly Dictionary<string, ShipLayout> _shipLayouts;
    private readonly Dictionary<string, PlanetType> _planets;
    private readonly Dictionary<string, MissionDefinition> _missions;
    private readonly BlockDefinition?[] _blocksById;
    private readonly Dictionary<GameLocale, Dictionary<string, string>> _locales;

    public IReadOnlyDictionary<string, BlockDefinition> Blocks => _blocks;
    public IReadOnlyDictionary<string, ItemDefinition> Items => _items;
    public IReadOnlyDictionary<string, RecipeDefinition> Recipes => _recipes;
    public IReadOnlyDictionary<string, BlueprintDefinition> Blueprints => _blueprints;
    public IReadOnlyDictionary<string, ShipModuleDefinition> ShipModules => _shipModules;
    public IReadOnlyDictionary<string, ShipDefinition> Ships => _ships;
    public IReadOnlyDictionary<string, PlanetType> Planets => _planets;
    public IReadOnlyDictionary<string, MissionDefinition> Missions => _missions;

    /// <summary>Hand-designed station/settlement templates (empty unless a pool file is present);
    /// world-gen may roll one of these instead of the procedural generator.</summary>
    public IReadOnlyList<StructureTemplate> StationTemplates { get; private set; } = System.Array.Empty<StructureTemplate>();
    public IReadOnlyList<StructureTemplate> SettlementTemplates { get; private set; } = System.Array.Empty<StructureTemplate>();

    /// <summary>Populates the optional structure-template pools (called by the content loader).</summary>
    public void SetStructureTemplates(IReadOnlyList<StructureTemplate> stations, IReadOnlyList<StructureTemplate> settlements)
    {
        StationTemplates = stations ?? System.Array.Empty<StructureTemplate>();
        SettlementTemplates = settlements ?? System.Array.Empty<StructureTemplate>();
    }

    public GameContent(
        IEnumerable<BlockDefinition> blocks,
        IEnumerable<ItemDefinition> items,
        IEnumerable<RecipeDefinition> recipes,
        IEnumerable<BlueprintDefinition> blueprints,
        IEnumerable<ShipModuleDefinition> shipModules,
        IDictionary<GameLocale, Dictionary<string, string>> locales,
        IEnumerable<PlanetType>? planets = null,
        IEnumerable<MissionDefinition>? missions = null,
        IEnumerable<ShipDefinition>? ships = null,
        IEnumerable<ShipLayout>? shipLayouts = null)
    {
        _blocks = blocks.ToDictionary(b => b.Key);
        _items = items.ToDictionary(i => i.Key);
        _recipes = recipes.ToDictionary(r => r.Key);
        _blueprints = blueprints.ToDictionary(b => b.Key);
        _shipModules = shipModules.ToDictionary(m => m.Key);
        _ships = (ships ?? Enumerable.Empty<ShipDefinition>()).ToDictionary(s => s.Key);
        _shipLayouts = (shipLayouts ?? Enumerable.Empty<ShipLayout>()).ToDictionary(l => l.Key);
        _planets = (planets ?? Enumerable.Empty<PlanetType>()).ToDictionary(p => p.Key);
        _missions = (missions ?? Enumerable.Empty<MissionDefinition>()).ToDictionary(m => m.Id);
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
    public ShipDefinition? GetShip(string key) => _ships.TryGetValue(key, out var s) ? s : null;
    public ShipLayout? GetShipLayout(string? key) => !string.IsNullOrEmpty(key) && _shipLayouts.TryGetValue(key, out var l) ? l : null;
    public PlanetType? GetPlanet(string key) => _planets.TryGetValue(key, out var p) ? p : null;
    public MissionDefinition? GetMission(string id) => _missions.TryGetValue(id, out var m) ? m : null;

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

        foreach (var ship in _ships.Values)
        {
            RequireBlueprint($"Ship '{ship.Key}'", ship.RequiredBlueprint);
            foreach (var cost in ship.CraftCost)
            {
                RequireItem($"Ship '{ship.Key}' craft cost", cost.Item);
            }

            foreach (var mod in ship.StartModules)
            {
                if (!_shipModules.ContainsKey(mod))
                {
                    problems.Add($"Ship '{ship.Key}' references unknown module '{mod}'.");
                }
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
            foreach (var biome in planet.Biomes)
            {
                RequireBlock($"Planet '{planet.Key}' biome surface", biome.SurfaceBlock);
                RequireBlock($"Planet '{planet.Key}' biome sub-surface", biome.SubSurfaceBlock);
            }
            foreach (var ore in planet.Ores)
            {
                RequireBlock($"Planet '{planet.Key}' ore", ore.Block);
            }
        }

        foreach (var mission in _missions.Values)
        {
            foreach (var reward in mission.Rewards)
            {
                RequireItem($"Mission '{mission.Id}' reward", reward.Item);
            }

            foreach (var obj in mission.Objectives)
            {
                switch (obj.Type)
                {
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Mine:
                        RequireBlock($"Mission '{mission.Id}' mine objective", obj.Target);
                        break;
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Collect:
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Deliver:
                        RequireItem($"Mission '{mission.Id}' objective", obj.Target);
                        break;
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new ContentValidationException(problems);
        }
    }
}
