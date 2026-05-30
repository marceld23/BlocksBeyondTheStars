using System.Text.Json;
using System.Text.Json.Serialization;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Localization;

namespace Spacecraft.Shared.Content;

/// <summary>
/// Loads the data-driven game content from a <c>data/</c> directory layout:
/// <code>
/// data/blocks.json        data/items.json    data/recipes.json
/// data/blueprints.json    data/ship_modules.json
/// data/locales/en.json    data/locales/de.json
/// </code>
/// Each definition file is a JSON array; each locale file is a flat key→text object.
/// </summary>
public static class ContentLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    /// <summary>Loads and validates all content from the given data directory.</summary>
    public static GameContent LoadFromDirectory(string dataDir)
    {
        if (!Directory.Exists(dataDir))
        {
            throw new DirectoryNotFoundException($"Content directory not found: {dataDir}");
        }

        var blocks = LoadArray<BlockDefinition>(Path.Combine(dataDir, "blocks.json"));
        var items = LoadArray<ItemDefinition>(Path.Combine(dataDir, "items.json"));
        var recipes = LoadArray<RecipeDefinition>(Path.Combine(dataDir, "recipes.json"));
        var blueprints = LoadArray<BlueprintDefinition>(Path.Combine(dataDir, "blueprints.json"));
        var modules = LoadArray<ShipModuleDefinition>(Path.Combine(dataDir, "ship_modules.json"));
        var planets = LoadArray<PlanetType>(Path.Combine(dataDir, "planets.json"));

        var locales = new Dictionary<GameLocale, Dictionary<string, string>>();
        var localeDir = Path.Combine(dataDir, "locales");
        if (Directory.Exists(localeDir))
        {
            foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
            {
                var file = Path.Combine(localeDir, locale.Code() + ".json");
                if (File.Exists(file))
                {
                    locales[locale] = LoadObject(file);
                }
            }
        }

        var content = new GameContent(blocks, items, recipes, blueprints, modules, locales, planets);
        content.Validate();
        return content;
    }

    private static List<T> LoadArray<T>(string path)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }

    private static Dictionary<string, string> LoadObject(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
               ?? new Dictionary<string, string>();
    }
}
