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
    /// other locale parses on its first <see cref="GameContent.CreateLocalizer"/>.</para>
    /// <para>#2489: a story pack whose files cannot be read is skipped with its texts and reported through
    /// <paramref name="error"/> (falls back to <paramref name="warn"/>); the rest of the content loads.</para></summary>
    public static GameContent LoadFromDirectory(string dataDir, string? userContentDir = null, Action<string>? warn = null, GameLocale? eagerLocale = null,
        Action<string>? error = null)
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
        var failedPacks = new List<string>();
        var stories = LoadStoryPacks(Path.Combine(dataDir, "stories"), localeFiles, warn, error ?? warn, failedPacks);

        var locales = new Dictionary<GameLocale, Dictionary<string, string>>();
        var lazyLocales = new Dictionary<GameLocale, Func<Dictionary<string, string>>>();
        foreach (var kv in localeFiles)
        {
            var files = kv.Value;
            if (kv.Key == GameLocale.English || kv.Key == eagerLocale)
            {
                // #2465 (B70): a pack key that overwrites an earlier file's text is reported — once, on the English
                // table (the reference every pack must carry); the other languages merge the same way without noise.
                locales[kv.Key] = MergeLocaleFiles(files, kv.Key == GameLocale.English ? warn : null);
            }
            else
            {
                lazyLocales[kv.Key] = () => MergeLocaleFiles(files);
            }
        }

        // #1763: the authored creature species are optional content — no creatures.json → none.
        var authoredCreatures = LoadArray<AuthoredCreature>(Path.Combine(dataDir, "creatures.json"));

        var content = new GameContent(blocks, items, recipes, blueprints, modules, locales, planets, missions, ships, shipLayouts,
            lazyLocales, LoadLocaleCoverage(Path.Combine(dataDir, "locale_coverage.json")), authoredCreatures);

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

        // Structure kits (#1873): the shipped pool plus one kit per file from the editor's user-content folder.
        var kits = LoadArray<StructureKit>(Path.Combine(dataDir, "structure_kits.json"));
        if (!string.IsNullOrEmpty(userContentDir) && Directory.Exists(userContentDir))
        {
            kits.AddRange(LoadUserKits(Path.Combine(userContentDir!, "structure_kits"), warn));
        }

        content.SetStructureKits(kits, warn);
        content.SetStories(stories);
        content.SetFailedStoryPacks(failedPacks);

        // Achievements are optional content: a data folder without the file just has none.
        content.SetAchievements(LoadArray<AchievementDefinition>(Path.Combine(dataDir, "achievements.json")));

        // NPC dialogues (#1127) are optional the same way: no dialogs.json → no dialogues.
        content.SetDialogs(LoadArray<DialogDefinition>(Path.Combine(dataDir, "dialogs.json")));

        // The SPS relay upgrade (#1125) is optional the same way: no relay.json → no relay feature.
        string relayFile = Path.Combine(dataDir, "relay.json");
        content.SetRelay(File.Exists(relayFile)
            ? JsonSerializer.Deserialize<RelayDefinition>(File.ReadAllText(relayFile), JsonOptions)
            : null);

        // The wormhole tuning (#2242): no wormholes.json → the defaults (the world option still decides).
        string wormholeFile = Path.Combine(dataDir, "wormholes.json");
        content.SetWormholes(File.Exists(wormholeFile)
            ? JsonSerializer.Deserialize<WormholeDefinition>(File.ReadAllText(wormholeFile), JsonOptions)
            : null);

        // The space salvage data (#2352): no space_salvage.json → the defaults (the numbers that shipped before it).
        string salvageFile = Path.Combine(dataDir, "space_salvage.json");
        content.SetSpaceSalvage(File.Exists(salvageFile)
            ? JsonSerializer.Deserialize<SpaceSalvageDefinition>(File.ReadAllText(salvageFile), JsonOptions)
            : null);

        // "The village lives" (#2589): no village_life.json → no friend presents, chatter or festival (each part optional).
        string villageFile = Path.Combine(dataDir, "village_life.json");
        content.SetVillageLife(File.Exists(villageFile)
            ? JsonSerializer.Deserialize<VillageLifeDefinition>(File.ReadAllText(villageFile), JsonOptions)
            : null);

        // The villagers' personal wishes (#2601) are optional the same way: no wishes.json → nobody wishes anything.
        content.SetWishes(LoadArray<BlocksBeyondTheStars.Shared.Missions.WishTemplate>(Path.Combine(dataDir, "wishes.json")));

        // The wilderness folk's professions (#2640) are optional the same way: no lodges.json → no lodges.
        content.SetLodges(LoadArray<LodgeProfessionDefinition>(Path.Combine(dataDir, "lodges.json")));

        // The old logs in a derelict's supply crates (#2633) are optional the same way: no derelict_logs.json → no logs.
        string derelictLogFile = Path.Combine(dataDir, "derelict_logs.json");
        content.SetDerelictLogs(File.Exists(derelictLogFile)
            ? JsonSerializer.Deserialize<DerelictLogDefinition>(File.ReadAllText(derelictLogFile), JsonOptions)
            : null);

        // The enemies (#2971) are optional the same way: no enemies.json → the numbers that shipped before it.
        content.SetEnemies(LoadArray<EnemyDefinition>(Path.Combine(dataDir, "enemies.json")));

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

    /// <summary>Loads every kit file of the editor's user-content folder (#1873): one <see cref="StructureKit"/>
    /// per file, the key defaulting to the file name; a malformed file is reported and skipped.</summary>
    private static List<StructureKit> LoadUserKits(string dir, Action<string>? warn = null)
    {
        var result = new List<StructureKit>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var k = JsonSerializer.Deserialize<StructureKit>(File.ReadAllText(file), JsonOptions);
                if (k == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(k.Key))
                {
                    k.Key = Path.GetFileNameWithoutExtension(file);
                }

                result.Add(k);
            }
            catch (JsonException ex)
            {
                warn?.Invoke($"Skipping unreadable user kit '{file}': {ex.Message}");
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

    /// <summary>Parses one locale's files in order (base table, then each story pack's table in ordinal folder order)
    /// into one merged map — later files win on duplicate keys, exactly as the eager loader merged them. With
    /// <paramref name="warn"/> every file that overwrites keys of an earlier one is reported once (#2465, B70): two packs
    /// sharing a key used to resolve silently, and before the folders were sorted the winner depended on the OS.</summary>
    private static Dictionary<string, string> MergeLocaleFiles(List<string> files, Action<string>? warn = null)
    {
        var map = LoadObject(files[0]);
        var origin = warn is null ? null : new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < files.Count; i++)
        {
            Dictionary<string, List<string>>? overwritten = null; // earlier file → the keys this file took from it
            foreach (var kv in LoadObject(files[i]))
            {
                if (origin is not null && map.ContainsKey(kv.Key))
                {
                    string earlier = origin.TryGetValue(kv.Key, out var o) ? o : files[0];
                    overwritten ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    if (!overwritten.TryGetValue(earlier, out var keys))
                    {
                        overwritten[earlier] = keys = new List<string>();
                    }

                    keys.Add(kv.Key);
                }

                map[kv.Key] = kv.Value;
                if (origin is not null)
                {
                    origin[kv.Key] = files[i];
                }
            }

            if (overwritten is not null)
            {
                foreach (var pair in overwritten)
                {
                    warn!($"Locale file '{DescribeLocaleFile(files[i])}' overwrites {pair.Value.Count} key(s) of "
                          + $"'{DescribeLocaleFile(pair.Key)}' (the later file wins): {string.Join(", ", pair.Value.Take(5))}"
                          + (pair.Value.Count > 5 ? ", …" : string.Empty));
                }
            }
        }

        return map;
    }

    /// <summary>A short name for a locale file in a warning: <c>locales/en.json</c> or <c>stories/&lt;pack&gt;/locales/en.json</c>.</summary>
    private static string DescribeLocaleFile(string path)
    {
        string name = Path.GetFileName(path);
        string? packDir = Path.GetDirectoryName(Path.GetDirectoryName(path));
        string? storiesDir = packDir is null ? null : Path.GetDirectoryName(packDir);
        return storiesDir is not null && string.Equals(Path.GetFileName(storiesDir), "stories", StringComparison.Ordinal)
            ? $"stories/{Path.GetFileName(packDir)}/locales/{name}"
            : $"locales/{name}";
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

    /// <summary>Reads a story pack's optional <c>cutscenes.json</c> (#2467) from its folder; no file → no cutscenes. Public
    /// so tools and tests read a pack folder exactly like the game.</summary>
    public static List<CutsceneDefinition> LoadPackCutscenes(string packDir)
    {
        var file = Path.Combine(packDir, "cutscenes.json");
        return File.Exists(file) ? ParseCutscenes(File.ReadAllText(file)) : new List<CutsceneDefinition>();
    }

    /// <summary>Parses a <c>cutscenes.json</c> document: the object form <c>{ "cutscenes": [ … ] }</c> (room for later
    /// sibling tables) or a bare array of cutscenes.</summary>
    public static List<CutsceneDefinition> ParseCutscenes(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<CutsceneDefinition>>(json, JsonOptions) ?? new List<CutsceneDefinition>();
        }

        return JsonSerializer.Deserialize<StoryCutsceneFile>(json, JsonOptions)?.Cutscenes ?? new List<CutsceneDefinition>();
    }

    /// <summary>Loads pluggable story packs from <c>data/stories/&lt;id&gt;/story.json</c> and merges each
    /// pack's optional <c>locales/&lt;code&gt;.json</c> into the shared locale tables. An absent directory
    /// yields no packs (the content then falls back to the built-in default pack).
    /// <para>#2465 (B70): the folders load in ordinal name order — <c>Directory.GetDirectories</c> returns them in an
    /// OS-dependent order, and the order decides which pack's text wins a shared locale key. A pack with a reserved id
    /// is skipped and a repeated id is reported (the later folder wins).</para>
    /// <para>#2489: a pack whose <c>story.json</c>, <c>lore_sites.json</c> or <c>cutscenes.json</c> cannot be read or parsed is
    /// reported through <paramref name="error"/> (pack folder and file) and skipped as a whole, its locale files included —
    /// one malformed pack used to abort the whole content load. Its folder name (and its id, when the manifest parsed) goes
    /// to <paramref name="failed"/>, so the server can tell "this save's pack failed to load" from "not installed".</para></summary>
    private static List<StoryDefinition> LoadStoryPacks(string storiesDir, Dictionary<GameLocale, List<string>> localeFiles,
        Action<string>? warn, Action<string>? error, List<string> failed)
    {
        var result = new List<StoryDefinition>();
        if (!Directory.Exists(storiesDir))
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(storiesDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var storyFile = Path.Combine(dir, "story.json");
            if (!File.Exists(storyFile))
            {
                continue;
            }

            string folder = Path.GetFileName(dir);
            string reading = storyFile;
            StoryDefinition? def = null;
            void Skip(Exception ex)
            {
                failed.Add(folder);
                if (!string.IsNullOrEmpty(def?.Id))
                {
                    failed.Add(def!.Id);
                }

                error?.Invoke($"Story pack '{folder}' could not be loaded (stories/{folder}/{Path.GetFileName(reading)}: {ex.Message}) — "
                              + "the pack and its texts are skipped.");
            }

            try
            {
                def = JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(storyFile), JsonOptions);
                if (def is null || string.IsNullOrEmpty(def.Id))
                {
                    continue;
                }

                if (StoryRegistry.IsReservedPackId(def.Id))
                {
                    warn?.Invoke($"Story pack '{folder}' uses the reserved id '{def.Id}' — skipped.");
                    continue;
                }

                // Environmental lore texts (#1111) live in their own file — story.json stays the arc, this is
                // the (larger, contributor-friendly) site-text table. Absent file → whatever story.json holds.
                reading = Path.Combine(dir, "lore_sites.json");
                if (File.Exists(reading)
                    && JsonSerializer.Deserialize<List<Story.LoreSite>>(File.ReadAllText(reading), JsonOptions) is { } sites)
                {
                    def.LoreSites.AddRange(sites);
                }

                // Cutscenes (#2467) live in their own file too: shots, captions and options per scene.
                reading = Path.Combine(dir, "cutscenes.json");
                def.Cutscenes.AddRange(LoadPackCutscenes(dir));
            }
            catch (JsonException ex)
            {
                Skip(ex);
                continue;
            }
            catch (IOException ex)
            {
                Skip(ex);
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                Skip(ex);
                continue;
            }

            if (!seen.Add(def.Id))
            {
                warn?.Invoke($"Story pack id '{def.Id}' is used by more than one folder — '{folder}' replaces the earlier one.");
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
