// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    /// in-game appears in the next new world without a Python merge or rebuild.
    /// <para>#1522: only the English locale table and <paramref name="eagerLocale"/> are parsed here; every
    /// other locale parses on its first <see cref="GameContent.CreateLocalizer"/>.</para></summary>
    public static GameContent LoadFromDirectory(string dataDir, string? userContentDir = null, Action<string>? warn = null, GameLocale? eagerLocale = null)
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

        // #1522: the locale files are only COLLECTED here — base table first, then every story pack's table
        // in merge order. English and the requested locale parse now; the rest parse on first use.
        var localeFiles = new Dictionary<GameLocale, List<string>>();
        var localeDir = Path.Combine(dataDir, "locales");
        if (Directory.Exists(localeDir))
        {
            foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
            {
                var file = Path.Combine(localeDir, locale.Code() + ".json");
                if (File.Exists(file))
                {
                    localeFiles[locale] = new List<string> { file };
                }
            }
        }

        // Pluggable story packs: data/stories/<id>/story.json + each pack's optional locale files (merged
        // into the shared locale tables so the beat text localizes normally).
        var stories = LoadStoryPacks(Path.Combine(dataDir, "stories"), localeFiles);

        var locales = new Dictionary<GameLocale, Dictionary<string, string>>();
        var lazyLocales = new Dictionary<GameLocale, Func<Dictionary<string, string>>>();
        foreach (var kv in localeFiles)
        {
            var files = kv.Value;
            if (kv.Key == GameLocale.English || kv.Key == eagerLocale)
            {
                locales[kv.Key] = MergeLocaleFiles(files);
            }
            else
            {
                lazyLocales[kv.Key] = () => MergeLocaleFiles(files);
            }
        }

        var content = new GameContent(blocks, items, recipes, blueprints, modules, locales, planets, missions, ships, shipLayouts,
            lazyLocales, LoadLocaleCoverage(Path.Combine(dataDir, "locale_coverage.json")));

        // Optional hand-designed structure template pools (empty when the files are absent).
        var stationTemplates = LoadArray<StructureTemplate>(Path.Combine(dataDir, "station_templates.json"));
        var settlementTemplates = LoadArray<StructureTemplate>(Path.Combine(dataDir, "settlement_templates.json"));

        // Writable user-content folder (editor output): one StructureTemplate per file. Merged on top of
        // the shipped pools so in-game builds are picked up at world creation without a rebuild.
        if (!string.IsNullOrEmpty(userContentDir) && Directory.Exists(userContentDir))
        {
            stationTemplates.AddRange(LoadUserTemplates(Path.Combine(userContentDir!, "station_templates"), "station", warn));
            settlementTemplates.AddRange(LoadUserTemplates(Path.Combine(userContentDir!, "settlement_templates"), "settlement", warn));
        }

        content.SetStructureTemplates(stationTemplates, settlementTemplates);
        content.SetStories(stories);

        // Achievements are optional content: a data folder without the file just has none.
        content.SetAchievements(LoadArray<AchievementDefinition>(Path.Combine(dataDir, "achievements.json")));

        // NPC dialogues (#1127) are optional the same way: no dialogs.json → no dialogues.
        content.SetDialogs(LoadArray<DialogDefinition>(Path.Combine(dataDir, "dialogs.json")));

        // The SPS relay upgrade (#1125) is optional the same way: no relay.json → no relay feature.
        string relayFile = Path.Combine(dataDir, "relay.json");
        content.SetRelay(File.Exists(relayFile)
            ? JsonSerializer.Deserialize<RelayDefinition>(File.ReadAllText(relayFile), JsonOptions)
            : null);

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
    private static List<StructureTemplate> LoadUserTemplates(string dir, string kind, Action<string>? warn = null)
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
            catch (JsonException ex)
            {
                warn?.Invoke($"Skipping unreadable user template '{file}': {ex.Message}");
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
        => ParseLocaleTable(File.ReadAllText(path));

    /// <summary>Parses one locale's files in order (base table, then each story pack's table) into one
    /// merged map — later files win on duplicate keys, exactly as the eager loader merged them.</summary>
    private static Dictionary<string, string> MergeLocaleFiles(List<string> files)
    {
        var map = LoadObject(files[0]);
        for (int i = 1; i < files.Count; i++)
        {
            foreach (var kv in LoadObject(files[i]))
            {
                map[kv.Key] = kv.Value;
            }
        }

        return map;
    }

    /// <summary>Reads the build-time locale coverage manifest (<c>data/locale_coverage.json</c>, written by
    /// <c>scripts/locale-coverage.py</c>): locale code → fraction of the English key set it covers. An
    /// absent file yields an empty map and the language picker measures the tables instead (#1522).</summary>
    private static Dictionary<GameLocale, double> LoadLocaleCoverage(string path)
    {
        var result = new Dictionary<GameLocale, double>();
        if (!File.Exists(path))
        {
            return result;
        }

        var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path), JsonOptions);
        if (raw == null)
        {
            return result;
        }

        foreach (var kv in raw)
        {
            if (GameLocaleExtensions.TryParse(kv.Key, out var locale))
            {
                result[locale] = kv.Value;
            }
        }

        return result;
    }

    /// <summary>Parses one locale table (a flat key→text JSON object) from an in-memory string. Public
    /// because the browser client fetches <c>locales/*.json</c> over HTTP before its content cache is
    /// complete — the shell screens must localize without waiting for the full load — and has to use the
    /// exact same parser/options as the filesystem path.</summary>
    public static Dictionary<string, string> ParseLocaleTable(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
           ?? new Dictionary<string, string>();

    /// <summary>Loads pluggable story packs from <c>data/stories/&lt;id&gt;/story.json</c> and merges each
    /// pack's optional <c>locales/&lt;code&gt;.json</c> into the shared locale tables. An absent directory
    /// yields no packs (the content then falls back to the built-in default pack).</summary>
    private static List<StoryDefinition> LoadStoryPacks(string storiesDir, Dictionary<GameLocale, List<string>> localeFiles)
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

            // Environmental lore texts (#1111) live in their own file — story.json stays the arc, this is
            // the (larger, contributor-friendly) site-text table. Absent file → whatever story.json holds.
            var loreFile = Path.Combine(dir, "lore_sites.json");
            if (File.Exists(loreFile)
                && JsonSerializer.Deserialize<List<Story.LoreSite>>(File.ReadAllText(loreFile), JsonOptions) is { } sites)
            {
                def.LoreSites.AddRange(sites);
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

                if (!localeFiles.TryGetValue(locale, out var files))
                {
                    localeFiles[locale] = files = new List<string>();
                }

                files.Add(file); // merged after the base table by MergeLocaleFiles, on first use
            }
        }

        return result;
    }
}
