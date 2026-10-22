// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The hand-designed station/settlement template pools, their tier-matched sub-pools, the distinct pack list and the
/// structure kits (#1873), plus the weighted, tier-, pack- and planet-filtered picks world-gen and the composers draw
/// from. Owned by <see cref="GameContent"/>, which delegates its structure members here.
/// </summary>
public sealed class StructureCatalog
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
    /// tier-matched sub-pools + the distinct pack list used by selection and the creation UI.</summary>
    public void SetStructureTemplates(IReadOnlyList<StructureTemplate> stations, IReadOnlyList<StructureTemplate> settlements)
    {
        StationTemplates = stations ?? System.Array.Empty<StructureTemplate>();
        SettlementTemplates = settlements ?? System.Array.Empty<StructureTemplate>();

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
            var entries = new List<KitEntry>();
            foreach (var e in kit.Entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Module))
                {
                    continue;
                }

                if (TemplateByKey(kind, e.Module) is null)
                {
                    warn?.Invoke($"Structure kit '{kit.Key}': entry '{e.Module}' names no {kind} module — dropped.");
                    continue;
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

            if (kit.Start.Length > 0 && TemplateByKey(kind, kit.Start) is null)
            {
                warn?.Invoke($"Structure kit '{kit.Key}': start module '{kit.Start}' does not exist — the first required entry starts the composition.");
                kit.Start = string.Empty;
            }

            kept.Add(kit);
        }

        StructureKits = kept;
        RefreshStructurePacks();
    }

    private void RefreshStructurePacks()
    {
        var packs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in StationTemplates) packs.Add(t.PackOrDefault);
        foreach (var t in SettlementTemplates) packs.Add(t.PackOrDefault);
        foreach (var k in StructureKits) packs.Add(k.PackOrDefault);
        StructurePacks = packs.ToList();
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
    {
        var list = new List<StructureKit>();
        foreach (var k in StructureKits)
        {
            if (k.KindOrDefault != kind || k.PinOnly)
            {
                continue; // #1885: a pin-only kit is never drawn for a new structure (KitByKey still finds it)
            }

            if (tier != null && !string.Equals(string.IsNullOrWhiteSpace(k.Tier) ? "medium" : k.Tier, tier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool packOk = enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(k.PackOrDefault);
            bool planetOk = k.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
                || k.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase);
            if (packOk && planetOk)
            {
                list.Add(k);
            }
        }

        return list;
    }

    /// <summary>The COMPLETE templates of a tier a slot may draw from — non-module, not pin-only, in the enabled
    /// packs and on the planet — for the joint random table with the kits (#1874).</summary>
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
            bool packOk = enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(t.PackOrDefault);
            bool planetOk = t.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
                || t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase);
            if (!t.PinOnly && packOk && planetOk)
            {
                list.Add(t);
            }
        }

        return list;
    }

    /// <summary>The settlement building MODULES (#1826: templates with a <see cref="StructureTemplate.Role"/>)
    /// a world may compose into its settlements and city: pack-filtered like whole templates, planet-type
    /// filtered like settlements, in pool order (the composers pick by hash, so the order is part of the
    /// determinism contract — never sort). Empty when no module fits.</summary>
    public IReadOnlyList<StructureTemplate> SettlementModulesFor(IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in SettlementTemplates)
        {
            if (!t.IsModule || !StructureRoles.IsKnown(t.Role) || t.Width <= 0 || t.Height <= 0 || t.Length <= 0)
            {
                continue;
            }

            bool packOk = enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(t.PackOrDefault);
            bool planetOk = t.PlanetTypes.Count == 0 || string.IsNullOrEmpty(planetType)
                || t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase);
            if (packOk && planetOk)
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
            if (t.IsModule)
            {
                continue; // #1826: a building module is composed INTO a settlement, never rolled as one
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
            // sees it) and is never rolled for a new structure.
            candidates = candidates.Where(t => !t.PinOnly).ToList();
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
