// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The hand-designed station/settlement template pools, their tier-matched sub-pools, the distinct pack list and the
/// structure kits (#1873), plus the weighted, tier-, pack- and planet-filtered picks world-gen and the composers draw
/// from. Owned by <see cref="GameContent"/>, which delegates its structure members here. The generator entries (#3518)
/// live in the same pools; their own lookups are in StructureCatalog.Sources.cs.
/// </summary>
public sealed partial class StructureCatalog
{
    /// <summary>Hand-designed station/settlement templates (empty unless a pool file is present);
    /// world-gen may roll one of these instead of the procedural generator.</summary>
    public IReadOnlyList<StructureTemplate> StationTemplates { get; private set; } = System.Array.Empty<StructureTemplate>();
    public IReadOnlyList<StructureTemplate> SettlementTemplates { get; private set; } = System.Array.Empty<StructureTemplate>();

    // Tier-matched sub-pools (key = tier, lower-cased) so a slot only ever rolls a same-tier template.
    private Dictionary<string, List<StructureTemplate>> _stationsByTier = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<StructureTemplate>> _settlementsByTier = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All distinct pack names present across both pools and the kits (for the world-creation pack picker).</summary>
    public IReadOnlyList<string> StructurePacks { get; private set; } = System.Array.Empty<string>();

    /// <summary>Structure kits (#1873): the module names with their entries. Set after the templates, since a kit's
    /// entries resolve against them.</summary>
    public IReadOnlyList<StructureKit> StructureKits { get; private set; } = System.Array.Empty<StructureKit>();

    /// <summary>Populates the optional structure-template pools (called by the content loader). Builds the
    /// tier-matched sub-pools + the distinct pack list used by selection and the creation UI. #3333: a station template
    /// flagged <see cref="StructureTemplate.Underground"/> is reported through <paramref name="warn"/> and kept as a surface
    /// template (stations hang in space).</summary>
    public void SetStructureTemplates(IReadOnlyList<StructureTemplate> stations, IReadOnlyList<StructureTemplate> settlements, Action<string>? warn = null)
    {
        StationTemplates = stations ?? System.Array.Empty<StructureTemplate>();
        SettlementTemplates = settlements ?? System.Array.Empty<StructureTemplate>();
        foreach (var t in StationTemplates)
        {
            if (t != null && t.Underground)
            {
                warn?.Invoke($"Station template '{t.Key}' is marked underground — stations hang in space, so it stays a surface template.");
                t.Underground = false;
            }
        }

        // #3484: a landmark is never a settlement — one that reached this pool (an export saved to the wrong folder) is
        // reported and left out; the landmark pool is filled by SetLandmarks.
        if (SettlementTemplates.Any(t => t != null && t.Landmark))
        {
            foreach (var t in SettlementTemplates.Where(t => t != null && t.Landmark))
            {
                warn?.Invoke($"Settlement template '{t.Key}' is a landmark — it belongs in the landmark folder and is not offered as a settlement.");
            }

            SettlementTemplates = SettlementTemplates.Where(t => t == null || !t.Landmark).ToList();
        }

        // #3518: a generator entry holds no cells and is never a module; #3519: a material token nobody knows is reported.
        StationTemplates = KeepWellFormedSources(StationTemplates, StructureKit.KindStation, warn);
        SettlementTemplates = KeepWellFormedSources(SettlementTemplates, StructureKit.KindSettlement, warn);
        ReportUnknownTokens(StationTemplates, StructureKit.KindStation, warn);
        ReportUnknownTokens(SettlementTemplates, StructureKit.KindSettlement, warn);
        ResetUnknownProtection(StationTemplates, StructureKit.KindStation, warn);
        ResetUnknownProtection(SettlementTemplates, StructureKit.KindSettlement, warn);

        _stationsByTier = GroupByTier(StationTemplates);
        _settlementsByTier = GroupByTier(SettlementTemplates);
        RefreshStructurePacks();
    }

    /// <summary>
    /// Populates the kit pool (#1873), validating each kit against the template pools: an entry whose module key
    /// resolves nowhere is dropped (with a warning), a required entry places at least one, a kit with an unknown
    /// kind, no key or no usable entry is dropped. Pool order is kept — the composers draw by weight in this order.
    /// </summary>
    public void SetStructureKits(IReadOnlyList<StructureKit>? kits, Action<string>? warn = null)
    {
        var kept = new List<StructureKit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kit in kits ?? System.Array.Empty<StructureKit>())
        {
            if (kit == null || string.IsNullOrWhiteSpace(kit.Key))
            {
                warn?.Invoke("Skipping a structure kit without a key.");
                continue;
            }

            string kind = kit.KindOrDefault;
            if (System.Array.IndexOf(StructureKit.Kinds, kind) < 0)
            {
                warn?.Invoke($"Skipping structure kit '{kit.Key}': unknown kind '{kit.Kind}' (station | settlement | city).");
                continue;
            }

            if (!seen.Add(kit.Key))
            {
                warn?.Invoke($"Skipping structure kit '{kit.Key}': a kit with that key is already loaded.");
                continue;
            }

            kit.Kind = kind;
            if (kit.Underground && kind == StructureKit.KindStation)
            {
                warn?.Invoke($"Structure kit '{kit.Key}': a station kit cannot be underground (stations hang in space) — treated as a surface kit.");
                kit.Underground = false; // #3333: the flag is meaningful for settlement and city kits only
            }

            var entries = new List<KitEntry>();
            foreach (var e in kit.Entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Module))
                {
                    continue;
                }

                if (TemplateByKey(kind, e.Module) is not { } module)
                {
                    warn?.Invoke($"Structure kit '{kit.Key}': entry '{e.Module}' names no {kind} module — dropped.");
                    continue;
                }

                if (module.IsSource)
                {
                    warn?.Invoke($"Structure kit '{kit.Key}': entry '{e.Module}' is a generator entry, not a module — dropped.");
                    continue; // #3518: a generator entry builds a whole structure, never a kit's piece
                }

                if (e.Required && e.Min < 1)
                {
                    e.Min = 1;
                }

                if (e.Max < e.Min)
                {
                    e.Max = e.Min;
                }

                entries.Add(e);
            }

            kit.Entries = entries;
            if (entries.Count == 0 && kind == StructureKit.KindStation)
            {
                warn?.Invoke($"Skipping structure kit '{kit.Key}': it has no usable entry."); // a station needs a start module
                continue;
            }

            if (kit.Start.Length > 0 && TemplateByKey(kind, kit.Start) is not { IsSource: false })
            {
                warn?.Invoke($"Structure kit '{kit.Key}': start module '{kit.Start}' does not exist — the first required entry starts the composition.");
                kit.Start = string.Empty;
            }

            kept.Add(kit);
        }

        StructureKits = kept;
        RefreshStructurePacks();
    }

    /// <summary>A whole template's <see cref="StructureTemplate.Protection"/> word the game does not know (generation 39) is
    /// reported and read as <see cref="LandmarkRules.ProtectionAuto"/> — the template keeps its kind's rule and stays in the
    /// pool (a landmark with one is dropped instead, like any landmark with an unknown word).</summary>
    private static void ResetUnknownProtection(IReadOnlyList<StructureTemplate> templates, string kind, Action<string>? warn)
    {
        foreach (var t in templates)
        {
            if (t != null && LandmarkRules.ProtectionProblem(t) is { } problem)
            {
                warn?.Invoke($"The {kind} template '{t.Key}': {problem} — it keeps the rule of its kind.");
                t.Protection = string.Empty;
            }
        }
    }

    private void RefreshStructurePacks()
    {
        var packs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in StationTemplates) packs.Add(t.PackOrDefault);
        foreach (var t in SettlementTemplates) packs.Add(t.PackOrDefault);
        foreach (var k in StructureKits) packs.Add(k.PackOrDefault);
        foreach (var t in Landmarks) packs.Add(t.PackOrDefault);
        StructurePacks = packs.ToList();
    }

    // --- landmarks (#3484) -------------------------------------------------------------------------------------

    /// <summary>#3484: the LANDMARKS — single authored buildings the landmark stamper seats on a planet on their own.
    /// A pool of its own: no settlement, module or underground pool ever offers one, and nothing else enters it.</summary>
    public IReadOnlyList<StructureTemplate> Landmarks { get; private set; } = System.Array.Empty<StructureTemplate>();

    /// <summary>Populates the landmark pool. A template without a key, with a key already taken or with a problem
    /// (<see cref="LandmarkRules.Problems"/>, plus whatever <paramref name="extraProblems"/> finds — the content's block
    /// check) is dropped with a warning, so one bad export never breaks the load. Pool order is kept: the stamper draws
    /// by weight in this order.</summary>
    public void SetLandmarks(IReadOnlyList<StructureTemplate>? landmarks, Action<string>? warn = null,
        Func<StructureTemplate, IEnumerable<string>>? extraProblems = null)
    {
        var kept = new List<StructureTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in landmarks ?? System.Array.Empty<StructureTemplate>())
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Key))
            {
                warn?.Invoke("Skipping a landmark without a key.");
                continue;
            }

            if (!seen.Add(t.Key))
            {
                warn?.Invoke($"Skipping landmark '{t.Key}': a landmark with that key is already loaded.");
                continue;
            }

            t.Landmark = true;
            var problems = LandmarkRules.Problems(t);
            problems.AddRange(SourceShapeProblems(t)); // #3518: a generator entry holds no cells
            if (extraProblems != null)
            {
                problems.AddRange(extraProblems(t));
            }

            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping landmark '{t.Key}': {string.Join("; ", problems)}.");
                continue;
            }

            kept.Add(t);
        }

        Landmarks = kept;
        RefreshStructurePacks();
    }

    /// <summary>A landmark by exact key (pinned replays), or null when it no longer exists.</summary>
    public StructureTemplate? LandmarkByKey(string key)
    {
        foreach (var t in Landmarks)
        {
            if (t.Key == key)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// The landmarks a planet may carry, in pool order: not pin-only, in the enabled packs, on the planet type, and
    /// with their conditions met — <see cref="LandmarkRules.RequiresAtmosphere"/> needs <paramref name="atmosphere"/>,
    /// <see cref="LandmarkRules.RequiresInhabited"/> needs <paramref name="inhabited"/>. With
    /// <paramref name="namedTypeOnly"/> (a planet type with a structure whitelist, #3500) only landmarks whose
    /// <see cref="StructureTemplate.PlanetTypes"/> NAME the type are offered — "every world" does not reach it.
    /// <paramref name="sources"/> (#3518): false lists the authored landmarks, true the generator entries only — a stamper
    /// appends those after the authored ones, so a pool without entries draws exactly as before.
    /// </summary>
    public IReadOnlyList<StructureTemplate> LandmarksFor(IReadOnlyCollection<string>? enabledPacks, string? planetType,
        bool atmosphere, bool inhabited, bool namedTypeOnly = false, bool sources = false)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in Landmarks)
        {
            if (t.IsSource != sources || t.PinOnly || !TemplateFits(t, enabledPacks, planetType))
            {
                continue;
            }

            if (namedTypeOnly && (string.IsNullOrEmpty(planetType) || !t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if ((LandmarkRules.Requires(t, LandmarkRules.RequiresAtmosphere) && !atmosphere)
                || (LandmarkRules.Requires(t, LandmarkRules.RequiresInhabited) && !inhabited))
            {
                continue;
            }

            list.Add(t);
        }

        return list;
    }

    /// <summary>A template of either pool by key — modules included (the kit entries resolve through here);
    /// station kits look in the station pool, settlement and city kits in the settlement pool.</summary>
    public StructureTemplate? TemplateByKey(string kind, string key)
    {
        var pool = kind == StructureKit.KindStation ? StationTemplates : SettlementTemplates;
        foreach (var t in pool)
        {
            if (t.Key == key)
            {
                return t;
            }
        }

        return null;
    }

    public StructureKit? KitByKey(string key)
    {
        foreach (var k in StructureKits)
        {
            if (k.Key == key)
            {
                return k;
            }
        }

        return null;
    }

    /// <summary>
    /// The kits a slot may draw from (#1873): of <paramref name="kind"/>, of <paramref name="tier"/> (null = any
    /// tier, for city kits), in the enabled packs and on the planet type, in pool order (the draw is weighted over
    /// this order — never sort).
    /// </summary>
    public IReadOnlyList<StructureKit> KitsFor(string kind, string? tier, IReadOnlyCollection<string>? enabledPacks, string? planetType)
        => KitsFor(kind, tier, enabledPacks, planetType, int.MaxValue);

    /// <summary><see cref="KitsFor(string, string?, IReadOnlyCollection{string}?, string?)"/> for a world of terrain generation
    /// <paramref name="generation"/> (#3583): a kit younger than the world (<see cref="StructureKit.MinTerrainGeneration"/>)
    /// is left out, so an unvisited body of an older world never draws it.</summary>
    public IReadOnlyList<StructureKit> KitsFor(string kind, string? tier, IReadOnlyCollection<string>? enabledPacks, string? planetType, int generation)
    {
        var list = new List<StructureKit>();
        foreach (var k in StructureKits)
        {
            if (k.KindOrDefault != kind || k.PinOnly || k.MinTerrainGeneration > generation)
            {
                continue; // #1885: a pin-only kit is never drawn for a new structure (KitByKey still finds it)
            }

            if (IsUndergroundKit(k))
            {
                continue; // #3333: an underground kit is composed only by the underground stamper (UndergroundKitsFor)
            }

            if (tier != null && !string.Equals(string.IsNullOrWhiteSpace(k.Tier) ? "medium" : k.Tier, tier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (KitFits(k, enabledPacks, planetType))
            {
                list.Add(k);
            }
        }

        return list;
    }

    /// <summary>#3333: the UNDERGROUND settlement / city kits a planet may carry — not pin-only, in the enabled packs and on
    /// the planet (an empty <see cref="StructureKit.PlanetTypes"/> = every world), in pool order. A station kit with the flag
    /// is a surface kit (stations hang in space) and never shows up here.</summary>
    public IReadOnlyList<StructureKit> UndergroundKitsFor(IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var list = new List<StructureKit>();
        foreach (var k in StructureKits)
        {
            if (!k.PinOnly && IsUndergroundKit(k) && KitFits(k, enabledPacks, planetType))
            {
                list.Add(k);
            }
        }

        return list;
    }

    /// <summary>#3333: true for a settlement or city kit flagged <see cref="StructureKit.Underground"/>.</summary>
    public static bool IsUndergroundKit(StructureKit k)
        => k.Underground && (k.KindOrDefault == StructureKit.KindSettlement || k.KindOrDefault == StructureKit.KindCity);

    private static bool KitFits(StructureKit k, IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        bool packOk = enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(k.PackOrDefault);
        bool planetOk = k.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
            || k.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase);
        return packOk && planetOk;
    }

    /// <summary>The COMPLETE templates of a tier a slot may draw from — non-module, not pin-only, not underground
    /// (#3333), in the enabled packs and on the planet — for the joint random table with the kits (#1874).</summary>
    public IReadOnlyList<StructureTemplate> CompleteTemplatesFor(string kind, string tier, IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var byTier = kind == StructureKit.KindStation ? _stationsByTier : _settlementsByTier;
        var list = new List<StructureTemplate>();
        if (!byTier.TryGetValue(string.IsNullOrWhiteSpace(tier) ? "medium" : tier, out var candidates))
        {
            return list;
        }

        foreach (var t in candidates)
        {
            if (!t.PinOnly && !t.Underground && TemplateFits(t, enabledPacks, planetType))
            {
                list.Add(t);
            }
        }

        return list;
    }

    /// <summary>#3333: the UNDERGROUND whole settlement templates a planet may carry, of any tier — not pin-only, in the
    /// enabled packs and on the planet, in pool order. Only the underground stamper reads this; the surface pools
    /// (<see cref="CompleteTemplatesFor"/>, <see cref="PickSettlementTemplate"/>) never offer them. A station template is
    /// never underground (<see cref="SetStructureTemplates"/> clears the flag).</summary>
    public IReadOnlyList<StructureTemplate> UndergroundTemplatesFor(IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in SettlementTemplates)
        {
            if (!t.IsModule && !t.IsSource && t.Underground && !t.PinOnly && TemplateFits(t, enabledPacks, planetType))
            {
                list.Add(t); // #3518: the underground generator entries are listed apart (UndergroundSourceTemplatesFor)
            }
        }

        return list;
    }

    private static bool TemplateFits(StructureTemplate t, IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        bool packOk = enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(t.PackOrDefault);
        bool planetOk = t.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
            || t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase);
        return packOk && planetOk;
    }

    /// <summary>#3333: whether a settlement MODULE belongs underground — flagged itself, or a module of an underground kit
    /// (its home <see cref="StructureTemplate.Kit"/>). Such a module never enters the surface module pool
    /// (<see cref="SettlementModulesFor"/>); its kit's entries still resolve it by key. A module of a surface kit that an
    /// underground kit merely borrows stays a surface module.</summary>
    public bool IsUndergroundModule(StructureTemplate t)
    {
        if (t.Underground)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(t.Kit))
        {
            return false;
        }

        var kit = KitByKey(t.Kit);
        return kit != null && IsUndergroundKit(kit);
    }

    /// <summary>The settlement building MODULES (#1826: templates with a <see cref="StructureTemplate.Role"/>)
    /// a world may compose into its settlements and city: pack-filtered like whole templates, planet-type
    /// filtered like settlements, in pool order (the composers pick by hash, so the order is part of the
    /// determinism contract — never sort). Empty when no module fits. #3333: an underground module
    /// (<see cref="IsUndergroundModule"/>) is never in this surface pool.</summary>
    public IReadOnlyList<StructureTemplate> SettlementModulesFor(IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in SettlementTemplates)
        {
            if (!t.IsModule || !StructureRoles.IsKnown(t.Role) || t.Width <= 0 || t.Height <= 0 || t.Length <= 0 || IsUndergroundModule(t))
            {
                continue;
            }

            if (TemplateFits(t, enabledPacks, planetType))
            {
                list.Add(t);
            }
        }

        return list;
    }

    private static Dictionary<string, List<StructureTemplate>> GroupByTier(IReadOnlyList<StructureTemplate> pool)
    {
        var byTier = new Dictionary<string, List<StructureTemplate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in pool)
        {
            if (t.IsModule || t.IsSource)
            {
                continue; // #1826: a building module is composed INTO a settlement, never rolled as one; #3518: a generator
                          // entry has pools of its own (SourceTemplatesFor), so the legacy picks never see one
            }

            var tier = string.IsNullOrWhiteSpace(t.Tier) ? "medium" : t.Tier;
            if (!byTier.TryGetValue(tier, out var list))
            {
                byTier[tier] = list = new List<StructureTemplate>();
            }

            list.Add(t);
        }

        return byTier;
    }

    /// <summary>Weighted, tier-matched, pack-filtered pick of a hand-designed station template — or null
    /// when no template fits (the caller then falls back to the procedural generator). <paramref name="enabledPacks"/>
    /// = null/empty means "all packs allowed".</summary>
    public StructureTemplate? PickStationTemplate(string tier, IReadOnlyCollection<string>? enabledPacks, System.Random rng, bool legacyOnly = false)
        => PickFromTier(_stationsByTier, tier, enabledPacks, rng, planetType: null, legacyOnly);

    /// <summary>Settlement twin of <see cref="PickStationTemplate"/>. <paramref name="planetType"/> lets a
    /// template restrict itself to a set of world types (#1115); empty/null matches every template.
    /// <paramref name="legacyOnly"/> replays a pre-#1115 structure: only <see cref="StructureTemplate.LegacyPool"/>
    /// templates compete, which reproduces the old selection stream draw-for-draw (no layout morphs).</summary>
    public StructureTemplate? PickSettlementTemplate(string tier, IReadOnlyCollection<string>? enabledPacks, System.Random rng, string? planetType = null, bool legacyOnly = false)
        => PickFromTier(_settlementsByTier, tier, enabledPacks, rng, planetType, legacyOnly);

    /// <summary>A settlement template by exact key (pinned replays, #1115), or null when it no longer exists.</summary>
    public StructureTemplate? SettlementTemplateByKey(string key)
        => _settlementsByTier.Values.SelectMany(l => l).FirstOrDefault(t => t.Key == key);

    /// <summary>A settlement building module by exact key (#1826), or null — whole templates never match.</summary>
    public StructureTemplate? SettlementModuleByKey(string key)
        => SettlementTemplates.FirstOrDefault(t => t.IsModule && t.Key == key);

    /// <summary>A station template by exact key (pinned replays, #1115), or null when it no longer exists.</summary>
    public StructureTemplate? StationTemplateByKey(string key)
        => _stationsByTier.Values.SelectMany(l => l).FirstOrDefault(t => t.Key == key);

    private static StructureTemplate? PickFromTier(
        Dictionary<string, List<StructureTemplate>> byTier, string tier, IReadOnlyCollection<string>? enabledPacks, System.Random rng, string? planetType = null, bool legacyOnly = false)
    {
        if (!byTier.TryGetValue(string.IsNullOrWhiteSpace(tier) ? "medium" : tier, out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        if (legacyOnly)
        {
            candidates = candidates.Where(t => t.LegacyPool).ToList();
            if (candidates.Count == 0)
            {
                return null; // no legacy template in this tier — exactly the pre-#1115 outcome (no draw)
            }
        }
        else
        {
            // #1874: a pin-only template stays for the worlds that pinned it (the legacy replay above still
            // sees it) and is never rolled for a new structure. #3333: neither is an underground one — only the
            // underground stamper seats it (a legacy replay reproduces what already stands, so it is left alone).
            candidates = candidates.Where(t => !t.PinOnly && !t.Underground).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }
        }

        bool PackAllowed(StructureTemplate t)
            => (enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(t.PackOrDefault))
                && (t.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
                    || t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase));

        int total = 0;
        foreach (var t in candidates)
        {
            if (PackAllowed(t))
            {
                total += System.Math.Max(1, t.Weight);
            }
        }

        if (total == 0)
        {
            return null; // tier has templates, but none in the enabled packs
        }

        int roll = rng.Next(total);
        foreach (var t in candidates)
        {
            if (!PackAllowed(t))
            {
                continue;
            }

            roll -= System.Math.Max(1, t.Weight);
            if (roll < 0)
            {
                return t;
            }
        }

        return null; // unreachable, but keeps the compiler + analysers happy
    }
}
