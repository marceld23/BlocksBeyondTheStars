// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The shared, validated registry of all data-driven game content (blocks, items,
/// recipes, blueprints, ship modules) plus localization tables. Built once and shared
/// by client and server so their game rules cannot drift apart.
///
/// Block numeric ids are assigned deterministically (sorted by key, air = 0) so a given
/// content set always yields the same palette. NOTE: adding blocks shifts ids. A save records
/// its palette and is remapped by key on the first load under another block set
/// (<see cref="BlockPalette"/>); a client and a server compare <see cref="BlockFingerprint"/>
/// in the join and refuse each other when their block sets differ.
/// </summary>
public sealed partial class GameContent
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
    // #1522: locale tables the loader did not parse eagerly — parsed on first use (CreateLocalizer /
    // LocaleCoverage) and cached in _locales from then on. The loader keeps only English + the active
    // locale eager; the other twelve tables (~3 MB JSON, ~45k strings) stay on disk until asked for.
    private readonly Dictionary<GameLocale, Func<Dictionary<string, string>>> _lazyLocales = new();
    // #1522: build-time coverage manifest (data/locale_coverage.json, scripts/locale-coverage.py) so the
    // language picker can answer without parsing every table; an unlisted locale is measured instead.
    private readonly Dictionary<GameLocale, double> _localeCoverageManifest = new();
    private readonly object _localeGate = new();
    private readonly CraftingGraph _crafting;
    private readonly StructureCatalog _structures = new();

    public IReadOnlyDictionary<string, BlockDefinition> Blocks => _blocks;
    public IReadOnlyDictionary<string, ItemDefinition> Items => _items;
    public IReadOnlyDictionary<string, RecipeDefinition> Recipes => _recipes;
    public IReadOnlyDictionary<string, BlueprintDefinition> Blueprints => _blueprints;
    public IReadOnlyDictionary<string, ShipModuleDefinition> ShipModules => _shipModules;
    public IReadOnlyDictionary<string, ShipDefinition> Ships => _ships;
    public IReadOnlyDictionary<string, PlanetType> Planets => _planets;
    public IReadOnlyDictionary<string, MissionDefinition> Missions => _missions;

    /// <summary>The hand-designed structure pools, kits and picks (#1873); the structure members below delegate here.</summary>
    public StructureCatalog Structures => _structures;

    /// <inheritdoc cref="StructureCatalog.StationTemplates"/>
    public IReadOnlyList<StructureTemplate> StationTemplates => _structures.StationTemplates;

    /// <inheritdoc cref="StructureCatalog.SettlementTemplates"/>
    public IReadOnlyList<StructureTemplate> SettlementTemplates => _structures.SettlementTemplates;

    /// <inheritdoc cref="StructureCatalog.StructurePacks"/>
    public IReadOnlyList<string> StructurePacks => _structures.StructurePacks;

    /// <inheritdoc cref="StructureCatalog.StructureKits"/>
    public IReadOnlyList<StructureKit> StructureKits => _structures.StructureKits;

    /// <inheritdoc cref="StructureCatalog.SetStructureTemplates"/>
    public void SetStructureTemplates(IReadOnlyList<StructureTemplate> stations, IReadOnlyList<StructureTemplate> settlements) => _structures.SetStructureTemplates(stations, settlements);

    /// <inheritdoc cref="StructureCatalog.SetStructureKits"/>
    public void SetStructureKits(IReadOnlyList<StructureKit>? kits, Action<string>? warn = null) => _structures.SetStructureKits(kits, warn);

    /// <inheritdoc cref="StructureCatalog.TemplateByKey"/>
    public StructureTemplate? TemplateByKey(string kind, string key) => _structures.TemplateByKey(kind, key);

    /// <inheritdoc cref="StructureCatalog.KitByKey"/>
    public StructureKit? KitByKey(string key) => _structures.KitByKey(key);

    /// <inheritdoc cref="StructureCatalog.KitsFor"/>
    public IReadOnlyList<StructureKit> KitsFor(string kind, string? tier, IReadOnlyCollection<string>? enabledPacks, string? planetType) => _structures.KitsFor(kind, tier, enabledPacks, planetType);

    /// <inheritdoc cref="StructureCatalog.CompleteTemplatesFor"/>
    public IReadOnlyList<StructureTemplate> CompleteTemplatesFor(string kind, string tier, IReadOnlyCollection<string>? enabledPacks, string? planetType) => _structures.CompleteTemplatesFor(kind, tier, enabledPacks, planetType);

    /// <inheritdoc cref="StructureCatalog.SettlementModulesFor"/>
    public IReadOnlyList<StructureTemplate> SettlementModulesFor(IReadOnlyCollection<string>? enabledPacks, string? planetType) => _structures.SettlementModulesFor(enabledPacks, planetType);

    /// <inheritdoc cref="StructureCatalog.PickStationTemplate"/>
    public StructureTemplate? PickStationTemplate(string tier, IReadOnlyCollection<string>? enabledPacks, System.Random rng, bool legacyOnly = false) => _structures.PickStationTemplate(tier, enabledPacks, rng, legacyOnly);

    /// <inheritdoc cref="StructureCatalog.PickSettlementTemplate"/>
    public StructureTemplate? PickSettlementTemplate(string tier, IReadOnlyCollection<string>? enabledPacks, System.Random rng, string? planetType = null, bool legacyOnly = false) => _structures.PickSettlementTemplate(tier, enabledPacks, rng, planetType, legacyOnly);

    /// <inheritdoc cref="StructureCatalog.SettlementTemplateByKey"/>
    public StructureTemplate? SettlementTemplateByKey(string key) => _structures.SettlementTemplateByKey(key);

    /// <inheritdoc cref="StructureCatalog.SettlementModuleByKey"/>
    public StructureTemplate? SettlementModuleByKey(string key) => _structures.SettlementModuleByKey(key);

    /// <inheritdoc cref="StructureCatalog.StationTemplateByKey"/>
    public StructureTemplate? StationTemplateByKey(string key) => _structures.StationTemplateByKey(key);

    // --- Pluggable story packs (loaded from data/stories/<id>/story.json by the content loader) ---

    private IReadOnlyDictionary<string, StoryDefinition> _stories =
        new Dictionary<string, StoryDefinition>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Installed story packs, keyed by id (e.g. "vega_protocol"). The active story per save is one of
    /// these (or "none"); the engine is story-agnostic, so adding a storyline is adding a pack.</summary>
    public IReadOnlyDictionary<string, StoryDefinition> Stories => _stories;

    private HashSet<string> _failedStoryPacks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>#2489: the story packs the loader skipped because a file of theirs could not be read — their folder names,
    /// and their ids where the manifest parsed. Empty on a clean load.</summary>
    public IReadOnlyCollection<string> FailedStoryPacks => _failedStoryPacks;

    /// <summary>#2489: whether the pack <paramref name="id"/> is installed but failed to load (as opposed to not installed at
    /// all) — the server then keeps the save's own story row instead of making the fallback story its active one.</summary>
    public bool StoryPackFailedToLoad(string? id) => !string.IsNullOrEmpty(id) && _failedStoryPacks.Contains(id!);

    /// <summary>Records the packs the loader had to skip (called by the content loader, #2489).</summary>
    public void SetFailedStoryPacks(IEnumerable<string>? ids)
        => _failedStoryPacks = new HashSet<string>((ids ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)), StringComparer.OrdinalIgnoreCase);

    /// <summary>The SPS relay upgrade (#1125): costs + lane link range, or null when <c>data/relay.json</c>
    /// is absent — the feature then simply does nothing (the achievements pattern).</summary>
    public RelayDefinition? Relay { get; private set; }

    /// <summary>Installs the relay definition (called by the content loader). A definition without any cost
    /// line is treated as absent — a relay that costs nothing would complete on the first click.</summary>
    public void SetRelay(RelayDefinition? relay)
        => Relay = relay is { Costs.Count: > 0 } ? relay : null;

    /// <summary>The wormhole tuning (#2242) from <c>data/wormholes.json</c>; the defaults when the file is absent (the
    /// world option decides whether a galaxy has wormholes at all).</summary>
    public WormholeDefinition Wormholes { get; private set; } = new();

    /// <summary>Installs the wormhole tuning (called by the content loader); null keeps the defaults.</summary>
    public void SetWormholes(WormholeDefinition? wormholes) => Wormholes = wormholes ?? new WormholeDefinition();

    /// <summary>The space salvage data (#2352) from <c>data/space_salvage.json</c>: debris-field themes and loot, combat
    /// debris, the wreck payout. The defaults when the file is absent.</summary>
    public SpaceSalvageDefinition SpaceSalvage { get; private set; } = new();

    /// <summary>True once <c>data/space_salvage.json</c> was installed — only then does <see cref="Validate"/> check its
    /// rows (the built-in defaults name items a minimal test content may not define).</summary>
    private bool _spaceSalvageFromData;

    /// <summary>Installs the space salvage data (called by the content loader); null keeps the defaults.</summary>
    public void SetSpaceSalvage(SpaceSalvageDefinition? salvage)
    {
        SpaceSalvage = salvage ?? new SpaceSalvageDefinition();
        _spaceSalvageFromData = salvage is not null;
    }

    /// <summary>"The village lives" (epic #2589) from <c>data/village_life.json</c>: friend presents, villager chatter, the
    /// festival. Empty sections when the file (or a section) is absent — that part of village life simply does nothing.</summary>
    public VillageLifeDefinition VillageLife { get; private set; } = new();

    /// <summary>Installs the village-life data (called by the content loader); null keeps the empty defaults. Null lists
    /// from a hand-edited file become empty ones so no reader has to guard against them.</summary>
    public void SetVillageLife(VillageLifeDefinition? villageLife)
    {
        var v = villageLife ?? new VillageLifeDefinition();
        v.Chatter ??= new List<VillageChatterTopic>();
        v.FriendGifts ??= new List<ItemAmount>();
        v.Festival ??= new VillageFestivalDefinition();
        v.Festival.Gift ??= new List<ItemAmount>();
        v.Festival.Cheers ??= new List<string>();
        foreach (var topic in v.Chatter)
        {
            if (topic is null)
            {
                continue; // reported by Validate
            }

            topic.Key ??= string.Empty;
            topic.When ??= "any";
            topic.Needs ??= string.Empty;
            topic.Hint ??= string.Empty;
            topic.Lines ??= new List<string>();
        }

        VillageLife = v;
    }

    private IReadOnlyList<DialogDefinition> _dialogs = new List<DialogDefinition>();

    /// <summary>Engine NPC dialogues (#1127) in authored order — empty when <c>data/dialogs.json</c> is
    /// absent (the feature then simply does nothing). Story packs carry their own dialogues separately.</summary>
    public IReadOnlyList<DialogDefinition> Dialogs => _dialogs;

    /// <summary>Installs the dialogue table (called by the content loader). Entries without a key or any
    /// node are dropped — a half-authored dialogue must not break every other one.</summary>
    public void SetDialogs(IEnumerable<DialogDefinition>? dialogs)
    {
        var list = new List<DialogDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dialogs ?? Enumerable.Empty<DialogDefinition>())
        {
            if (d == null || string.IsNullOrEmpty(d.Key) || d.Nodes.Count == 0 || !seen.Add(d.Key))
            {
                continue;
            }

            list.Add(d);
        }

        _dialogs = list;
    }

    private IReadOnlyList<LodgeProfessionDefinition> _lodges = new List<LodgeProfessionDefinition>();

    /// <summary>The professions of the wilderness folk (#2640/#2641) in authored order — empty when
    /// <c>data/lodges.json</c> is absent (worlds then grow no lodges).</summary>
    public IReadOnlyList<LodgeProfessionDefinition> Lodges => _lodges;

    /// <summary>The lodge profession with this job key, or null (also how "is this a lodge job?" is asked).</summary>
    public LodgeProfessionDefinition? LodgeProfession(string? job)
    {
        if (string.IsNullOrEmpty(job))
        {
            return null;
        }

        foreach (var p in _lodges)
        {
            if (string.Equals(p.Key, job, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>Installs the lodge professions (called by the content loader). Null rows are dropped and null weights of
    /// a hand-edited file become empty ones; everything else is checked by <see cref="Validate"/>.</summary>
    public void SetLodges(IEnumerable<LodgeProfessionDefinition>? lodges)
    {
        var list = new List<LodgeProfessionDefinition>();
        foreach (var l in lodges ?? Enumerable.Empty<LodgeProfessionDefinition>())
        {
            if (l is null)
            {
                continue;
            }

            l.Key ??= string.Empty;
            l.Held ??= string.Empty;
            l.Theme ??= string.Empty;
            l.Activity ??= string.Empty;
            l.Work ??= string.Empty;
            l.Yard ??= string.Empty;
            l.Partner ??= string.Empty;
            l.Weights ??= new Dictionary<string, double>();
            list.Add(l);
        }

        _lodges = list;
    }

    private IReadOnlyList<WishTemplate> _wishes = new List<WishTemplate>();

    /// <summary>The villagers' personal wish templates (#2601) in authored order — empty when <c>data/wishes.json</c> is
    /// absent (villagers then simply have no wishes).</summary>
    public IReadOnlyList<WishTemplate> Wishes => _wishes;

    /// <summary>Installs the wish templates (called by the content loader). Null rows are dropped and null lists of a
    /// hand-edited file become empty ones; everything else is checked by <see cref="Validate"/>.</summary>
    public void SetWishes(IEnumerable<WishTemplate>? wishes)
    {
        var list = new List<WishTemplate>();
        foreach (var w in wishes ?? Enumerable.Empty<WishTemplate>())
        {
            if (w is null)
            {
                continue;
            }

            w.Key ??= string.Empty;
            w.Kind ??= WishTemplate.KindDeliver;
            w.Item ??= string.Empty;
            w.Jobs ??= new List<string>();
            w.Requires ??= string.Empty;
            w.Gift ??= new List<ItemAmount>();
            w.Reason ??= string.Empty;
            list.Add(w);
        }

        _wishes = list;
    }

    /// <summary>The fragments of the old logs in a derelict station's supply crates (#2633) from
    /// <c>data/derelict_logs.json</c> — empty when the file is absent (the crates then hold supplies only).</summary>
    public DerelictLogDefinition DerelictLogs { get; private set; } = new();

    /// <summary>Installs the derelict log fragments (called by the content loader); null keeps the empty default. Null lists
    /// of a hand-edited file become empty ones, blank entries are dropped.</summary>
    public void SetDerelictLogs(DerelictLogDefinition? logs)
    {
        var d = logs ?? new DerelictLogDefinition();
        d.Jobs = (d.Jobs ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        d.Escapes = (d.Escapes ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        d.Closings = (d.Closings ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        d.Incidents = (d.Incidents ?? new List<DerelictLogIncident>()).Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Key)).ToList();
        if (d.DayMax < d.DayMin)
        {
            d.DayMax = d.DayMin;
        }

        DerelictLogs = d;
    }

    private IReadOnlyList<AchievementDefinition> _achievements = new List<AchievementDefinition>();

    /// <summary>Achievements in authored order (empty when <c>data/achievements.json</c> is absent — the
    /// feature then simply does nothing rather than failing to load the world).</summary>
    public IReadOnlyList<AchievementDefinition> Achievements => _achievements;

    /// <summary>Installs the achievement table (called by the content loader). Entries without a key or
    /// counter are dropped: a half-authored row must not break every other achievement.</summary>
    public void SetAchievements(IEnumerable<AchievementDefinition>? achievements)
    {
        var list = new List<AchievementDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in achievements ?? Enumerable.Empty<AchievementDefinition>())
        {
            if (a == null || string.IsNullOrEmpty(a.Key) || string.IsNullOrEmpty(a.Counter) || !seen.Add(a.Key))
            {
                continue;
            }

            if (a.Target < 1)
            {
                a.Target = 1;
            }

            list.Add(a);
        }

        _achievements = list;
    }

    /// <summary>Installs the story packs (called by the content loader). A pack with a reserved id
    /// (<see cref="StoryRegistry.ReservedPackIds"/>) is dropped; a later pack with the same id replaces an earlier one
    /// (the loader passes them in ordinal folder order). The built-in VEGA pack is added whenever the data does not
    /// carry a usable <c>vega_protocol</c> (#2465, N11) — it used to be added only when NO pack loaded, so a data folder
    /// holding just a beat-less player pack (the tutorial) would have made that the default world story, persisted for
    /// good.</summary>
    public void SetStories(IEnumerable<StoryDefinition> stories)
    {
        var dict = new Dictionary<string, StoryDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stories ?? Enumerable.Empty<StoryDefinition>())
        {
            if (!string.IsNullOrEmpty(s.Id) && !StoryRegistry.IsReservedPackId(s.Id))
            {
                dict[s.Id] = s;
            }
        }

        // A data copy that cannot be the world story (no beats — a broken install) is replaced too: the default story's
        // row must always resolve, or a restart would start it fresh and overwrite the save's real VEGA row (B07).
        if (!dict.TryGetValue(StoryRegistry.DefaultStoryId, out var vega) || !StoryRegistry.CanBeWorldStory(vega))
        {
            dict[StoryRegistry.Default.Id] = StoryRegistry.Default;
        }

        _stories = dict;
    }

    /// <summary>The default world story: "vega_protocol" when it can be one (it always is installed — the built-in
    /// copy fills in), else the first world-capable pack by id, else the built-in VEGA pack. Never a beat-less or
    /// player-scoped pack (<see cref="StoryRegistry.CanBeWorldStory"/>).</summary>
    public StoryDefinition DefaultStory
        => _stories.TryGetValue(StoryRegistry.DefaultStoryId, out var d) && StoryRegistry.CanBeWorldStory(d)
            ? d
            : (_stories.Values.Where(StoryRegistry.CanBeWorldStory).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault()
               ?? StoryRegistry.Default);

    /// <summary>Looks up a story pack of any kind by id (pack texts, player packs); returns false (and the default pack)
    /// for empty/unknown ids. Choosing the save's story goes through <see cref="TryGetWorldStory"/> instead.</summary>
    public bool TryGetStory(string? id, out StoryDefinition def)
    {
        if (!string.IsNullOrEmpty(id) && _stories.TryGetValue(id!, out var d))
        {
            def = d;
            return true;
        }

        def = DefaultStory;
        return false;
    }

    /// <summary>Looks up a pack that may be the save's world-shared story (#2465, N11): installed, world-scoped and with
    /// at least one beat (<see cref="StoryRegistry.CanBeWorldStory"/>). Returns false (and <see cref="DefaultStory"/>)
    /// for empty or unknown ids and for player packs such as the tutorial — so <c>--story tutorial</c>, an admin's
    /// <c>StorySelectIntent{tutorial}</c> or a stored row naming one can never make it the world story.</summary>
    public bool TryGetWorldStory(string? id, out StoryDefinition def)
    {
        if (!string.IsNullOrEmpty(id) && _stories.TryGetValue(id!, out var d) && StoryRegistry.CanBeWorldStory(d))
        {
            def = d;
            return true;
        }

        def = DefaultStory;
        return false;
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
        IEnumerable<ShipLayout>? shipLayouts = null,
        IDictionary<GameLocale, Func<Dictionary<string, string>>>? lazyLocales = null,
        IDictionary<GameLocale, double>? localeCoverage = null,
        IEnumerable<AuthoredCreature>? authoredCreatures = null)
    {
        _authoredCreatures = (authoredCreatures ?? Enumerable.Empty<AuthoredCreature>()).ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
        _blocks = blocks.ToDictionary(b => b.Key);
        _items = items.ToDictionary(i => i.Key);
        IndexItemGroups(items); // #2958
        _recipes = recipes.ToDictionary(r => r.Key);
        _blueprints = blueprints.ToDictionary(b => b.Key);
        _shipModules = shipModules.ToDictionary(m => m.Key);
        _ships = (ships ?? Enumerable.Empty<ShipDefinition>()).ToDictionary(s => s.Key);
        _shipLayouts = (shipLayouts ?? Enumerable.Empty<ShipLayout>()).ToDictionary(l => l.Key);
        _planets = (planets ?? Enumerable.Empty<PlanetType>()).ToDictionary(p => p.Key);
        _missions = (missions ?? Enumerable.Empty<MissionDefinition>()).ToDictionary(m => m.Id);
        _locales = new Dictionary<GameLocale, Dictionary<string, string>>(locales);
        if (lazyLocales != null)
        {
            foreach (var kv in lazyLocales)
            {
                if (!_locales.ContainsKey(kv.Key))
                {
                    _lazyLocales[kv.Key] = kv.Value;
                }
            }
        }

        if (localeCoverage != null)
        {
            foreach (var kv in localeCoverage)
            {
                _localeCoverageManifest[kv.Key] = kv.Value;
            }
        }

        AssignBlockIds();
        MarkTintableDefaults();
        MarkShapeableDefaults();
        MarkFloraHostDefaults();
        MarkAirtightDefaults();
        _crafting = new CraftingGraph(_recipes.Values);

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

        BlockFingerprint = ComputeBlockFingerprint();
        _lastBuiltBlockFingerprint = BlockFingerprint;
    }

    /// <summary>
    /// A stable fingerprint of the block palette (#2222): the block keys in numeric-id order, joined with '\n',
    /// hashed with FNV-1a (64 bit) and written as 16 lowercase hex digits. Chunks travel as raw numeric ids and the
    /// ids follow the sorted keys, so two builds whose block sets differ draw each other's blocks as their
    /// neighbours — the join compares this value on both sides and refuses a mismatch. Integer arithmetic over
    /// UTF-8 bytes only: the same on every platform and runtime, computed once.
    /// </summary>
    public string BlockFingerprint { get; }

    private static volatile string? _lastBuiltBlockFingerprint;

    /// <summary>The <see cref="BlockFingerprint"/> of the content this process built last, or null before any was
    /// built (#2222). The game client loads one content set per process; a network client that was not handed a
    /// fingerprint explicitly falls back to this one. A process that holds several different content sets (the
    /// test suite) must hand the fingerprint over explicitly instead.</summary>
    public static string? LastBuiltBlockFingerprint => _lastBuiltBlockFingerprint;

    private string ComputeBlockFingerprint()
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        bool first = true;
        foreach (var block in _blocksById)
        {
            if (block is null)
            {
                continue;
            }

            if (!first)
            {
                hash = (hash ^ (byte)'\n') * prime;
            }

            first = false;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(block.Key))
            {
                hash = (hash ^ b) * prime;
            }
        }

        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private readonly Dictionary<string, AuthoredCreature> _authoredCreatures;

    /// <summary>The authored creature species (#1763, <c>data/creatures.json</c>), keyed case-insensitively.</summary>
    public IReadOnlyDictionary<string, AuthoredCreature> AuthoredCreatures => _authoredCreatures;

    /// <summary>The authored records a planet type names, in its own order (unknown keys are skipped — validation
    /// reports them). Empty for every type that names none.</summary>
    public IReadOnlyList<AuthoredCreature> AuthoredCreaturesFor(PlanetType planet)
    {
        var list = new List<AuthoredCreature>();
        foreach (var key in planet.AuthoredCreatures)
        {
            if (_authoredCreatures.TryGetValue(key, out var record))
            {
                list.Add(record);
            }
        }

        return list;
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

    /// <summary>
    /// Curated building/terrain materials that the always-available Dye/Glow action may recolour. Machines,
    /// doors, ice/fluids, flora, ores and special blocks are deliberately excluded (they carry their own
    /// optics/logic). A block may also opt in explicitly via <c>tintable</c> in JSON; this only ADDS to the
    /// curated defaults — glass and the light fixtures opt in that way (#1126: coloured glass + tinted
    /// lamps), which makes them dyeable WITHOUT joining the shapeable set below (shaped glass would need a
    /// transparent shaped mesh). Applied here so client and server derive the identical set.
    /// </summary>
    private static readonly HashSet<string> TintableDefaults = new(StringComparer.Ordinal)
    {
        "stone", "dirt", "basalt", "sand", "mud", "grass", "snow", "salt", "mycelium", "alien_grass",
        "deepslate", "granite", "ash", "wood_log", "wood_log_b", // #2958: the pale log is a building log too
        "moss_stone", "sandstone", "scree", "bone", // #1647 landscape-variety terrain set (tar keeps its fluid look)
        "iron_wall", "steel_wall", "bronze_block", "brass_block", "steel_floor", "metal_panel", "concrete",
        "cargo_floor", "medbay_panel", "lab_panel", "engine_panel",
        "ancient_brick", "rune_stone", // #1834 ruin masonry: a mined pillar keeps its form, a mined rune its glow
    };

    private void MarkTintableDefaults()
    {
        foreach (var key in TintableDefaults)
        {
            if (_blocks.TryGetValue(key, out var block))
            {
                block.Tintable = true;
            }
        }
    }

    /// <summary>
    /// Marks which blocks the always-available "Shape" action may re-form into spheres/ramps/pyramids/…
    /// We reuse the curated tintable building-material set: exactly the plain solids that look right as a
    /// custom hull (machines/doors/glass/flora/fluids/light blocks stay excluded). Applied here so client
    /// and server derive the identical set.
    /// </summary>
    private void MarkShapeableDefaults()
    {
        foreach (var key in TintableDefaults)
        {
            if (_blocks.TryGetValue(key, out var block))
            {
                block.Shapeable = true;
            }
        }
    }

    /// <summary>
    /// Loose, granular or organic materials — plus pass-through props — that never hold air, even though
    /// their category would seal by default (#794). Dug dirt or piled sand is not an airtight wall; the
    /// torch/ladder/stairs are props with gaps; a geyser vent is literally a hole; fire is burning air.
    /// </summary>
    private static readonly HashSet<string> AirtightExceptions = new(StringComparer.Ordinal)
    {
        "dirt", "grass", "sand", "mud", "snow", "salt", "ash", "mycelium", "alien_grass",
        "fire", "geyser_vent", "torch", "ladder", "stairs",
        // Low-tech furniture (#803-#809): props with gaps (or an open flame) — never an airtight wall.
        // The wood crate is deliberately absent: a closed box seals like the iron crate does.
        "bed", "campfire", "lantern", "rug", "flower_pot",
    };

    /// <summary>
    /// Derives which blocks hold air inside a sealed base room (#794): terrain/building/ore/machine/light
    /// categories seal (natural rock included — a dug-out cave can become a habitat), flora and doors leak,
    /// minus the curated loose-material exceptions. An explicit <c>airtight</c> opt-in from
    /// <c>data/blocks.json</c> is never overridden (the force field and energy gate membranes seal despite
    /// their door category). Applied here so client and server derive the identical set.
    /// </summary>
    private void MarkAirtightDefaults()
    {
        foreach (var block in _blocks.Values)
        {
            if (block.Airtight)
            {
                continue; // explicit JSON opt-in stays
            }

            bool categorySeals = block.Category is "terrain" or "building" or "ore" or "machine" or "light";
            block.Airtight = categorySeals && block.Key != "air" && !AirtightExceptions.Contains(block.Key);
        }
    }

    /// <summary>
    /// Marks every block that any <see cref="FloraCatalog"/> species can root on as a <see
    /// cref="BlockDefinition.FloraHost"/>. Derived from the catalog's host lists (the single source of truth)
    /// so worldgen, server harvest-regrow and the client's "fertile ground" cue all share one flag instead of
    /// re-deriving the host set. Applied here so client and server compute the identical set.
    /// </summary>
    private void MarkFloraHostDefaults()
    {
        foreach (var species in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.All)
        {
            if (species.Fruit)
            {
                continue; // #2038: a fruit hangs from a leaf — foliage is no ground the player plants on
            }

            foreach (var hostKey in species.Hosts)
            {
                if (_blocks.TryGetValue(hostKey, out var block))
                {
                    block.FloraHost = true;
                }
            }

            foreach (var hostKey in species.LateHosts) // a late host block only exists on a new world
            {
                if (_blocks.TryGetValue(hostKey, out var block))
                {
                    block.FloraHost = true;
                }
            }

            foreach (var hostKey in species.CaveHosts) // generation 11: the cave rock a species roots on underground
            {
                if (_blocks.TryGetValue(hostKey, out var block))
                {
                    block.FloraHost = true;
                }
            }
        }
    }

    /// <summary>The crafting-depth graph (#826); <see cref="CraftDepth"/> and <see cref="MaxInputDepth"/> delegate here.</summary>
    public CraftingGraph Crafting => _crafting;

    /// <inheritdoc cref="CraftingGraph.CraftDepth"/>
    public int CraftDepth(string itemKey) => _crafting.CraftDepth(itemKey);

    /// <inheritdoc cref="CraftingGraph.MaxInputDepth"/>
    public int MaxInputDepth(IEnumerable<ItemAmount> cost) => _crafting.MaxInputDepth(cost);

    public BlockDefinition? GetBlock(string key) => _blocks.TryGetValue(key, out var b) ? b : null;

    public BlockDefinition? BlockById(BlockId id)
        => id.Value < _blocksById.Length ? _blocksById[id.Value] : null;

    /// <summary>The current block-id palette: numeric id → block key, for every loaded block (incl. air = 0).
    /// Numeric ids are assigned by key sort order (<see cref="AssignBlockIds"/>), so they shift whenever a
    /// block is added whose key sorts before existing ones. Persisting this lets a save remap its stored
    /// edits to the current ids on load instead of silently decoding them to the wrong blocks.</summary>
    public IReadOnlyDictionary<ushort, string> BlockPalette()
    {
        var palette = new Dictionary<ushort, string>(_blocks.Count);
        foreach (var b in _blocks.Values)
        {
            palette[b.NumericId.Value] = b.Key;
        }

        return palette;
    }

    // Item keys may carry a colour modifier (e.g. "mud#t3f6fb0" for dyed mud). Definition lookups
    // strip it back to the base key — a dyed item shares its base item's definition (max stack,
    // placed block, name, …); the colour lives only in the key + the per-voxel modifier store.
    public ItemDefinition? GetItem(string key) => _items.TryGetValue(ItemKey.Base(key), out var i) ? i : null;
    public RecipeDefinition? GetRecipe(string key) => _recipes.TryGetValue(key, out var r) ? r : null;
    public BlueprintDefinition? GetBlueprint(string key) => _blueprints.TryGetValue(key, out var b) ? b : null;
    public ShipModuleDefinition? GetShipModule(string key) => _shipModules.TryGetValue(key, out var m) ? m : null;
    public ShipDefinition? GetShip(string key) => _ships.TryGetValue(key, out var s) ? s : null;

    /// <summary>A design's cargo capacity as built: the <c>cargo_slots</c> of its start modules. The hold is
    /// only ever sized from modules (the server's ResizeCargo), so this is the one number that can never
    /// disagree with it — ships.json used to carry its own <c>cargoSlots</c>, which advertised 96 for a
    /// hauler whose hold was 72 (#1261).</summary>
    public int StartCargoSlots(ShipDefinition ship)
    {
        int slots = 0;
        foreach (var key in ship.StartModules)
        {
            if (GetShipModule(key) is { } m && m.Stats.TryGetValue("cargo_slots", out var s))
            {
                slots += (int)s;
            }
        }

        return slots;
    }
    public ShipLayout? GetShipLayout(string? key) => !string.IsNullOrEmpty(key) && _shipLayouts.TryGetValue(key, out var l) ? l : null;
    public PlanetType? GetPlanet(string key) => _planets.TryGetValue(key, out var p) ? p : null;
    public MissionDefinition? GetMission(string id) => _missions.TryGetValue(id, out var m) ? m : null;

    public int MaxStackOf(string itemKey) => _items.TryGetValue(ItemKey.Base(itemKey), out var i) ? i.MaxStack : 1;

    /// <summary>The parsed table for a locale, parsing a lazily registered one on first use (#1522).
    /// Null when the locale has no table at all.</summary>
    private Dictionary<string, string>? LocaleTable(GameLocale locale)
    {
        lock (_localeGate)
        {
            if (_locales.TryGetValue(locale, out var table))
            {
                return table;
            }

            if (_lazyLocales.Remove(locale, out var load))
            {
                table = load();
                _locales[locale] = table;
                return table;
            }

            return null;
        }
    }

    /// <summary>Whether the locale has a table at all — parsed or still on disk.</summary>
    public bool HasLocale(GameLocale locale)
    {
        lock (_localeGate)
        {
            return _locales.ContainsKey(locale) || _lazyLocales.ContainsKey(locale);
        }
    }

    /// <summary>How many locale tables are parsed and resident right now (diagnostics and the lazy-load
    /// tests; the loader starts at one or two).</summary>
    public int LoadedLocaleCount
    {
        get
        {
            lock (_localeGate)
            {
                return _locales.Count;
            }
        }
    }

    /// <summary>Coverage for the picker: the manifest value when the build shipped one (no table parse for
    /// one click), the measured value for a locale the manifest does not know yet.</summary>
    private double SelectableCoverage(GameLocale locale)
        => _localeCoverageManifest.TryGetValue(locale, out var fromManifest) ? fromManifest : LocaleCoverage(locale);

    /// <summary>Fraction of the English key set the given locale covers (1.0 for English itself,
    /// 0.0 when the locale has no table). The settings language picker uses this to decide which
    /// community languages are complete enough to offer.</summary>
    public double LocaleCoverage(GameLocale locale)
    {
        if (locale == GameLocale.English)
        {
            return 1.0;
        }

        var en = LocaleTable(GameLocale.English);
        var table = LocaleTable(locale);
        if (en == null || en.Count == 0 || table == null)
        {
            return 0.0;
        }

        int covered = 0;
        foreach (var key in en.Keys)
        {
            if (table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                covered++;
            }
        }

        return (double)covered / en.Count;
    }

    /// <summary>The locales the settings picker should offer: English and German always (the two
    /// mandatory languages), community languages once their coverage clears
    /// <paramref name="minCoverage"/>. Order follows the enum, so the picker cycle is stable.</summary>
    public IReadOnlyList<GameLocale> SelectableLocales(double minCoverage = 0.45)
    {
        var result = new List<GameLocale>();
        foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
        {
            if (locale == GameLocale.English || locale == GameLocale.German
                || (HasLocale(locale) && SelectableCoverage(locale) >= minCoverage))
            {
                result.Add(locale);
            }
        }

        return result;
    }

    /// <summary>Builds a localizer for the given locale, with English as the fallback table.</summary>
    public Localizer CreateLocalizer(GameLocale locale)
    {
        var active = LocaleTable(locale) ?? new Dictionary<string, string>();
        var fallback = LocaleTable(GameLocale.English) ?? active;
        return new Localizer(locale, active, fallback);
    }

    /// <summary>
    /// The number of block slots in the client's texture atlas — numeric block ids must stay BELOW it. The atlas
    /// has 32×32 = 1024 slots, but only the first band belongs to blocks (#1952): the client's <c>AtlasBands</c>
    /// deals the rest to official extra tiles (400..511), animation strips of the local pack and of world
    /// textures (512..983) and the derived variant / end-grain tiles (984..1023, since #2851). Mirrors
    /// <c>AtlasBands.BlockEnd</c>.
    /// </summary>
    public const int AtlasTileCapacity = 400;
}
