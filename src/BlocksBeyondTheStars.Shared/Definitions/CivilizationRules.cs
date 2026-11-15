// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3639: the vocabulary and the checks of the civilizations (<see cref="CivilizationDefinition"/>,
/// docs/developer/CIVILIZATIONS.md). A civilization or a civilization template with a problem is dropped with a warning, so
/// one bad file never breaks the load.
/// </summary>
public static class CivilizationRules
{
    /// <summary>The data folder of the civilization files (<c>data/civilizations/&lt;key&gt;.json</c>); a civilization's
    /// templates live in its subfolder (<c>data/civilizations/&lt;key&gt;/*.json</c>).</summary>
    public const string DataFolder = "civilizations";

    /// <summary>The kind a civilization template reports (<see cref="StructureTemplate.Kind"/>, informational).</summary>
    public const string TemplateKind = "civilization";

    /// <summary>A lost civilization: ruins, nobody home — no residents, no posts, no dens.</summary>
    public const string StateLost = "lost";

    public static readonly string[] States = { StateLost };

    /// <summary>Stamped by the civilization stage from the civilization's own templates.</summary>
    public const string PlacementFramework = "framework";

    /// <summary>A tag only: the structures keep their own stampers (the G.D.S., the Sounders, the SPS).</summary>
    public const string PlacementLegacy = "legacy";

    public static readonly string[] Placements = { PlacementFramework, PlacementLegacy };

    public const string StartAllowed = "allowed";
    public const string StartNever = "never";
    public const string StartOnly = "only";

    public static readonly string[] StartSystems = { StartAllowed, StartNever, StartOnly };

    // --- template pools (StructureTemplate.CivPool) ---

    /// <summary>The civilization's giant building (seated first, the best seat).</summary>
    public const string PoolGiant = "giant";

    /// <summary>A city or a large complex.</summary>
    public const string PoolCity = "city";

    /// <summary>A small site.</summary>
    public const string PoolSmall = "small";

    /// <summary>A small piece a <see cref="PropMarker"/> places (a statue, a fallen machine) — never a site of its own.</summary>
    public const string PoolProp = "prop";

    /// <summary>The pools a home body's sites draw from (<see cref="CivilizationSitePool.Pool"/>).</summary>
    public static readonly string[] SitePools = { PoolGiant, PoolCity, PoolSmall };

    /// <summary>Every pool a civilization template may name.</summary>
    public static readonly string[] TemplatePools = { PoolGiant, PoolCity, PoolSmall, PoolProp };

    // --- cells and markers ---

    /// <summary>#3640: the cell kind of an AIR POCKET — this cell stays air even under water (a cavity under water fills
    /// with water). Template cells and palette entries.</summary>
    public const string AirKind = "air";

    /// <summary>#3641: a marker that places a small piece of the same civilization (settings <c>template=&lt;key&gt;</c> or
    /// <c>pool=&lt;propPool&gt;</c>) on the ground after the ruin pass.</summary>
    public const string PropMarker = "prop";

    // --- under water (#3640) ---

    /// <summary>Blocks of water a sunken site keeps over its top; a sea too shallow for that gets a dug basin.</summary>
    public const int SunkenHeadroom = 3;

    /// <summary>How deep a sunken site may dig into the sea floor below the deepest natural floor of its footprint.</summary>
    public const int SunkenMaxDig = 48;

    // --- home bodies ---

    public const string AtmosphereBreathable = "breathable";
    public const string AtmosphereToxic = "toxic";
    public const string AtmosphereNone = "none";

    public static readonly string[] Atmospheres = { AtmosphereBreathable, AtmosphereToxic, AtmosphereNone };

    public const string ConditionNotAsteroid = "notAsteroid";
    public const string ConditionNotGas = "notGas";
    public const string ConditionNotVoid = "notVoid";
    public const string ConditionPlanet = "planet";
    public const string ConditionMoon = "moon";

    public static readonly string[] Conditions = { ConditionNotAsteroid, ConditionNotGas, ConditionNotVoid, ConditionPlanet, ConditionMoon };

    /// <summary>The structure kinds a home body may give up (<see cref="CivilizationHomeBody.Suppress"/>).</summary>
    public static readonly string[] SuppressibleKinds = { StructureTypeRules.Landmark, StructureTypeRules.GiantWreck };

    /// <summary>The most home bodies one civilization may ask for per galaxy.</summary>
    public const int MaxHomes = 8;

    /// <summary>The most sites one pool may put on a home body.</summary>
    public const int MaxSitesPerPool = 16;

    // --- VEGA ---

    public const string VegaFirst = "first";
    public const string VegaInside = "inside";
    public const string VegaBlock = "block";
    public const string VegaProp = "prop";
    public const string VegaAir = "air";

    public static readonly string[] VegaTriggers = { VegaFirst, VegaInside, VegaBlock, VegaProp, VegaAir };

    // --- records ---

    /// <summary>The prefix of a civilization site's placement record kind.</summary>
    public const string RecordKindPrefix = "civ:";

    /// <summary>The placement record kind of a civilization's sites (<c>civ:&lt;key&gt;</c>); the record index is the site's.</summary>
    public static string RecordKind(string civilization) => RecordKindPrefix + civilization;

    /// <summary>The structure overlay key of one civilization site.</summary>
    public static string FeatureKey(string civilization, int index)
        => RecordKind(civilization) + ":" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The discovery ledger key of a civilization (one per player).</summary>
    public static string DiscoveryKey(string civilization) => RecordKind(civilization);

    /// <summary>Whether <paramref name="value"/> is one of <paramref name="words"/> (case-insensitive).</summary>
    public static bool IsOneOf(string? value, string[] words)
    {
        foreach (string w in words)
        {
            if (string.Equals(value, w, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A <c>[min, max]</c> list read as a range: one value = exactly that, none = <paramref name="fallback"/>.</summary>
    public static (int Min, int Max) RangeOf(List<int>? values, int fallback = 1)
    {
        if (values is null || values.Count == 0)
        {
            return (fallback, fallback);
        }

        int min = values[0];
        int max = values.Count > 1 ? values[1] : values[0];
        return (Math.Min(min, max), Math.Max(min, max));
    }

    /// <summary>The problems of a civilization file; empty = fine.</summary>
    public static List<string> Problems(CivilizationDefinition c, Func<string, bool> blockExists, Func<string, bool> planetTypeExists)
    {
        var p = new List<string>();
        if (string.IsNullOrWhiteSpace(c.Key))
        {
            p.Add("no key");
        }

        if (string.IsNullOrWhiteSpace(c.NameKey))
        {
            p.Add("no nameKey");
        }

        CheckWord(p, "state", c.State, States);
        CheckWord(p, "placement", c.Placement, Placements);
        if (c.IsFramework)
        {
            ScopeProblems(p, c.Scope ?? new CivilizationScope(), planetTypeExists);
            SiteProblems(p, c.Sites);
        }

        foreach (string kind in c.HomeBody?.Suppress ?? new List<string>())
        {
            CheckWord(p, "homeBody.suppress", kind, SuppressibleKinds);
        }

        foreach (string type in c.Guarantee?.PlanetTypes ?? new List<string>())
        {
            if (!planetTypeExists(type))
            {
                p.Add($"guarantee names the unknown planet type '{type}'");
            }
        }

        MaterialProblems(p, c, blockExists);
        VegaProblems(p, c.Vega);
        return p;
    }

    private static void ScopeProblems(List<string> p, CivilizationScope s, Func<string, bool> planetTypeExists)
    {
        var (min, max) = RangeOf(s.Count);
        if (min < 0 || max < 1 || max > MaxHomes)
        {
            p.Add($"scope.count must lie in 0..{MaxHomes} with a maximum of at least 1");
        }

        if (s.CountWeights is { Count: > 0 } w && w.Count != max - min + 1)
        {
            p.Add($"scope.countWeights needs {max - min + 1} weights (one per count {min}..{max})");
        }

        if (s.PerSystem < 0)
        {
            p.Add("scope.perSystem must not be negative");
        }

        CheckWord(p, "scope.startSystem", s.StartSystem, StartSystems);
        foreach (string a in s.Atmospheres ?? new List<string>())
        {
            CheckWord(p, "scope.atmospheres", a, Atmospheres);
        }

        foreach (string cond in s.Conditions ?? new List<string>())
        {
            CheckWord(p, "scope.conditions", cond, Conditions);
        }

        foreach (string type in s.PlanetTypes ?? new List<string>())
        {
            if (!planetTypeExists(type))
            {
                p.Add($"scope names the unknown planet type '{type}'");
            }
        }
    }

    private static void SiteProblems(List<string> p, List<CivilizationSitePool>? sites)
    {
        if (sites is null || sites.Count == 0)
        {
            p.Add("a framework civilization needs at least one site pool");
            return;
        }

        foreach (var site in sites)
        {
            CheckWord(p, "sites.pool", site.Pool, SitePools);
            var (min, max) = RangeOf(site.Count);
            if (min < 0 || max > MaxSitesPerPool)
            {
                p.Add($"sites '{site.Pool}': count must lie in 0..{MaxSitesPerPool}");
            }
        }
    }

    private static void MaterialProblems(List<string> p, CivilizationDefinition c, Func<string, bool> blockExists)
    {
        foreach (var kv in c.Materials ?? new Dictionary<string, string>())
        {
            if (!MaterialTokens.IsToken(kv.Key))
            {
                p.Add($"materials names '{kv.Key}', which is no material token ({string.Join(", ", MaterialTokens.All)})");
            }

            if (!blockExists(kv.Value))
            {
                p.Add($"materials '{kv.Key}' names the unknown block '{kv.Value}'");
            }
        }

        foreach (string block in c.Ruin?.Rubble ?? new List<string>())
        {
            if (!blockExists(block))
            {
                p.Add($"ruin.rubble names the unknown block '{block}'");
            }
        }

        string overgrowth = c.Ruin?.Overgrowth ?? string.Empty;
        if (overgrowth.Length > 0 && overgrowth != "none" && !blockExists(overgrowth))
        {
            p.Add($"ruin.overgrowth names the unknown block '{overgrowth}'");
        }
    }

    private static void VegaProblems(List<string> p, List<CivilizationVegaLine>? lines)
    {
        foreach (var line in lines ?? new List<CivilizationVegaLine>())
        {
            CheckWord(p, "vega.trigger", line.Trigger, VegaTriggers);
            if (string.IsNullOrWhiteSpace(line.Key))
            {
                p.Add($"vega line '{line.Trigger}' has no key");
            }

            bool needsValue = IsOneOf(line.Trigger, new[] { VegaBlock, VegaProp });
            if (needsValue && string.IsNullOrWhiteSpace(line.Value))
            {
                p.Add($"vega line '{line.Trigger}' needs a value");
            }
        }
    }

    /// <summary>The problems of a template of civilization <paramref name="c"/> (beside the landmark checks): its pool, a
    /// prop's prop pool, and — for a lost civilization — no residents and no dens.</summary>
    public static List<string> TemplateProblems(StructureTemplate t, CivilizationDefinition? c)
    {
        var p = new List<string>();
        if (c is null)
        {
            p.Add($"names the unknown civilization '{t.Civilization}'");
            return p;
        }

        if (!c.IsFramework)
        {
            p.Add($"civilization '{c.Key}' is a legacy tag — it stamps no templates of its own");
        }

        CheckWord(p, "civPool", t.CivPool, TemplatePools);
        bool prop = string.Equals(t.CivPool, PoolProp, StringComparison.OrdinalIgnoreCase);
        if (prop && t.IsSource)
        {
            p.Add("a prop is never a generator entry");
        }
        else if (prop && string.IsNullOrWhiteSpace(t.PropPool))
        {
            p.Add("a prop needs a propPool (the name a prop marker asks for)");
        }

        if (string.Equals(c.State, StateLost, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var cell in t.Cells)
            {
                if (cell.Kind == "marker" && !LostMarkerAllowed(cell.Id))
                {
                    p.Add($"a lost civilization has nobody home — the marker '{cell.Id}' at {cell.X},{cell.Y},{cell.Z} is not allowed");
                    break;
                }
            }
        }

        return p;
    }

    /// <summary>The markers a lost civilization's template may carry: chests, loot, sounds, props, doors and circuits —
    /// nothing that brings a resident or a creature.</summary>
    public static bool LostMarkerAllowed(string? marker)
        => marker is LandmarkRules.ChestMarker or LandmarkRules.SoundMarker or PropMarker or "loot" or "data_terminal" or "relic_cache"
           || TemplateDevices.Marker == marker
           || BlocksBeyondTheStars.Shared.World.DoorBlocks.IsDoorMarker(marker);

    private static void CheckWord(List<string> p, string field, string? value, string[] words)
    {
        if (!IsOneOf(value, words))
        {
            p.Add($"{field} '{value}' is unknown ({string.Join(" | ", words)})");
        }
    }
}
