using System.Text.Json;
using System.Text.Json.Serialization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.Content;

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

    /// <summary>Loads and validates all content from the given data directory. When
    /// <paramref name="userContentDir"/> is given and exists, hand-designed structure templates dropped
    /// there by the in-game editor (<c>station_templates/*.json</c>, <c>settlement_templates/*.json</c>,
    /// one <see cref="StructureTemplate"/> per file) are merged into the pools — so a structure built
    /// in-game appears in the next new world without a Python merge or rebuild.</summary>
    public static GameContent LoadFromDirectory(string dataDir, string? userContentDir = null)
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
        var ships = LoadArray<ShipDefinition>(Path.Combine(dataDir, "ships.json"));
        var shipLayouts = LoadShipLayouts(Path.Combine(dataDir, "ship_layouts"));
        var planets = LoadArray<PlanetType>(Path.Combine(dataDir, "planets.json"));
        var missions = LoadArray<BlocksBeyondTheStars.Shared.Missions.MissionDefinition>(Path.Combine(dataDir, "missions.json"));

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

        // Pluggable story packs: data/stories/<id>/story.json + each pack's optional locale files (merged
        // into the shared locale tables BEFORE the content is built so the beat text localizes normally).
        var stories = LoadStoryPacks(Path.Combine(dataDir, "stories"), locales);

        var content = new GameContent(blocks, items, recipes, blueprints, modules, locales, planets, missions, ships, shipLayouts);

        // Optional hand-designed structure template pools (empty when the files are absent).
        var stationTemplates = LoadArray<StructureTemplate>(Path.Combine(dataDir, "station_templates.json"));
        var settlementTemplates = LoadArray<StructureTemplate>(Path.Combine(dataDir, "settlement_templates.json"));

        // Writable user-content folder (editor output): one StructureTemplate per file. Merged on top of
        // the shipped pools so in-game builds are picked up at world creation without a rebuild.
        if (!string.IsNullOrEmpty(userContentDir) && Directory.Exists(userContentDir))
        {
            stationTemplates.AddRange(LoadUserTemplates(Path.Combine(userContentDir!, "station_templates"), "station"));
            settlementTemplates.AddRange(LoadUserTemplates(Path.Combine(userContentDir!, "settlement_templates"), "settlement"));
        }

        content.SetStructureTemplates(stationTemplates, settlementTemplates);
        content.SetStories(stories);

        content.Validate();
        return content;
    }

    /// <summary>Loads every voxel ship layout from <c>data/ship_layouts/*.json</c> (key = file name).</summary>
    private static List<ShipLayout> LoadShipLayouts(string dir)
    {
        var result = new List<ShipLayout>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var layout = JsonSerializer.Deserialize<ShipLayout>(File.ReadAllText(file), JsonOptions);
            if (layout != null)
            {
                layout.Key = Path.GetFileNameWithoutExtension(file);
                result.Add(layout);
            }
        }

        return result;
    }

    /// <summary>Loads every <see cref="StructureTemplate"/> from a user-content sub-folder (one per file,
    /// key defaulting to the file name). Malformed files are skipped so one bad export can't break load.</summary>
    private static List<StructureTemplate> LoadUserTemplates(string dir, string kind)
    {
        var result = new List<StructureTemplate>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var t = JsonSerializer.Deserialize<StructureTemplate>(File.ReadAllText(file), JsonOptions);
                if (t == null || t.Cells.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(t.Key))
                {
                    t.Key = Path.GetFileNameWithoutExtension(file);
                }

                if (string.IsNullOrWhiteSpace(t.Kind))
                {
                    t.Kind = kind;
                }

                result.Add(t);
            }
            catch (JsonException)
            {
                // Ignore an unreadable export; the rest of the folder still loads.
            }
        }

        return result;
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

    /// <summary>Loads pluggable story packs from <c>data/stories/&lt;id&gt;/story.json</c> and merges each
    /// pack's optional <c>locales/&lt;code&gt;.json</c> into the shared locale tables. An absent directory
    /// yields no packs (the content then falls back to the built-in default pack).</summary>
    private static List<StoryDefinition> LoadStoryPacks(string storiesDir, Dictionary<GameLocale, Dictionary<string, string>> locales)
    {
        var result = new List<StoryDefinition>();
        if (!Directory.Exists(storiesDir))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(storiesDir))
        {
            var storyFile = Path.Combine(dir, "story.json");
            if (!File.Exists(storyFile))
            {
                continue;
            }

            var def = JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(storyFile), JsonOptions);
            if (def is null || string.IsNullOrEmpty(def.Id))
            {
                continue;
            }

            result.Add(def);

            var packLocaleDir = Path.Combine(dir, "locales");
            if (!Directory.Exists(packLocaleDir))
            {
                continue;
            }

            foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
            {
                var file = Path.Combine(packLocaleDir, locale.Code() + ".json");
                if (!File.Exists(file))
                {
                    continue;
                }

                if (!locales.TryGetValue(locale, out var map))
                {
                    locales[locale] = map = new Dictionary<string, string>();
                }

                foreach (var kv in LoadObject(file))
                {
                    map[kv.Key] = kv.Value;
                }
            }
        }

        return result;
    }
}
