// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3639: the CIVILIZATIONS (<c>data/civilizations/&lt;key&gt;.json</c>) and the templates of the framework civilizations
/// (<c>data/civilizations/&lt;key&gt;/*.json</c>) — a pool of their own: no settlement, station, landmark, giant ship or
/// underground pool ever offers a civilization template, and the civilization stage offers nothing else.
/// </summary>
public sealed partial class GameContent
{
    private readonly Dictionary<string, CivilizationDefinition> _civilizations = new(StringComparer.Ordinal);
    private List<CivilizationDefinition> _civilizationList = new();
    private List<StructureTemplate> _civilizationTemplates = new();

    /// <summary>Every loaded civilization, in file-name order (the order homes are picked in).</summary>
    public IReadOnlyList<CivilizationDefinition> Civilizations => _civilizationList;

    /// <summary>Every loaded template of a framework civilization, in file order.</summary>
    public IReadOnlyList<StructureTemplate> CivilizationTemplates => _civilizationTemplates;

    /// <summary>The civilization with this key, or null.</summary>
    public CivilizationDefinition? GetCivilization(string? key)
        => !string.IsNullOrEmpty(key) && _civilizations.TryGetValue(key!, out var c) ? c : null;

    /// <summary>A civilization template by exact key (a pinned record), or null when it no longer exists.</summary>
    public StructureTemplate? CivilizationTemplateByKey(string? key)
        => _civilizationTemplates.Find(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>The templates of <paramref name="civilization"/> in <paramref name="pool"/> a body may draw, in file order: not
    /// pin-only, in the enabled packs (null or empty = every pack), no younger than the world.</summary>
    public IReadOnlyList<StructureTemplate> CivilizationTemplatesFor(string civilization, string pool,
        IReadOnlyCollection<string>? enabledPacks = null, int generation = int.MaxValue)
        => _civilizationTemplates.Where(t => string.Equals(t.Civilization, civilization, StringComparison.Ordinal)
                                             && string.Equals(t.CivPool, pool, StringComparison.OrdinalIgnoreCase)
                                             && !t.PinOnly && t.MinTerrainGeneration <= generation
                                             && (enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(t.PackOrDefault)))
            .ToList();

    /// <summary>The props of <paramref name="civilization"/> whose <see cref="StructureTemplate.PropPool"/> is
    /// <paramref name="propPool"/>, in file order (pin-only ones included: a prop marker may name its pool on purpose).</summary>
    public IReadOnlyList<StructureTemplate> CivilizationPropsFor(string civilization, string propPool)
        => _civilizationTemplates.Where(t => string.Equals(t.Civilization, civilization, StringComparison.Ordinal)
                                             && string.Equals(t.CivPool, CivilizationRules.PoolProp, StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(t.PropPool, propPool, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The civilization of a block (the <see cref="BlockDefinition.Civilization"/> tag), or null.</summary>
    public CivilizationDefinition? CivilizationOfBlock(string? blockKey)
        => blockKey != null && _blocks.TryGetValue(blockKey, out var b) ? GetCivilization(b.Civilization) : null;

    /// <summary>
    /// Populates the civilizations and their templates. A civilization with a problem (<see cref="CivilizationRules.Problems"/>
    /// against the blocks and planet types) is dropped with a warning, a template with a problem
    /// (<see cref="CivilizationTemplateProblems"/>) too — a broken file never breaks the load. A framework civilization never
    /// stamps before <see cref="WorldDescription.CivilizationsGeneration"/>. Call after the blocks, the planets and the loot
    /// tables are set.
    /// </summary>
    public void SetCivilizations(IReadOnlyList<CivilizationDefinition>? civilizations, IReadOnlyList<StructureTemplate>? templates,
        Action<string>? warn = null)
    {
        _civilizations.Clear();
        var list = new List<CivilizationDefinition>();
        foreach (var c in civilizations ?? Array.Empty<CivilizationDefinition>())
        {
            if (c is null)
            {
                continue;
            }

            NormalizeCivilization(c);
            var problems = CivilizationRules.Problems(c, _blocks.ContainsKey, _planets.ContainsKey);
            if (_civilizations.ContainsKey(c.Key))
            {
                problems.Add("a civilization with that key is already loaded");
            }

            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping civilization '{c.Key}': {string.Join("; ", problems)}.");
                continue;
            }

            _civilizations[c.Key] = c;
            list.Add(c);
        }

        _civilizationList = list;
        _civilizationTemplates = KeepCivilizationTemplates(templates, warn);
        WarnUnknownCivilizationTags(warn);
    }

    private static void NormalizeCivilization(CivilizationDefinition c)
    {
        c.Scope ??= new CivilizationScope();
        c.HomeBody ??= new CivilizationHomeBody();
        c.Guarantee ??= new CivilizationGuarantee();
        c.Materials ??= new Dictionary<string, string>();
        c.Ruin ??= new CivilizationRuin();
        c.Sites ??= new List<CivilizationSitePool>();
        c.Vega ??= new List<CivilizationVegaLine>();
        c.Codex ??= new CivilizationCodex();
        c.Rumours ??= new List<string>();
        if (c.IsFramework)
        {
            c.MinTerrainGeneration = Math.Max(c.MinTerrainGeneration, WorldDescription.CivilizationsGeneration);
        }
    }

    private List<StructureTemplate> KeepCivilizationTemplates(IReadOnlyList<StructureTemplate>? templates, Action<string>? warn)
    {
        var kept = new List<StructureTemplate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in templates ?? Array.Empty<StructureTemplate>())
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Key))
            {
                warn?.Invoke("Skipping a civilization template without a key.");
                continue;
            }

            t.Cells ??= new List<TemplateCell>();
            t.Palette ??= new List<TemplatePaletteEntry>();
            t.Rows ??= new List<string>();
            t.PlanetTypes ??= new List<string>();
            t.RuinZones ??= new List<RuinZone>();
            t.Requires ??= new List<string>();
            var problems = CivilizationTemplateProblems(t);
            if (!seen.Add(t.Key))
            {
                problems.Add("a civilization template with that key is already loaded");
            }

            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping civilization template '{t.Key}': {string.Join("; ", problems)}.");
                continue;
            }

            kept.Add(t);
        }

        return kept;
    }

    /// <summary>
    /// Everything wrong with a civilization template against THIS content, one sentence each — empty when it can be used:
    /// the landmark checks (<see cref="LandmarkRules.Problems"/>), the civilization's own (<see cref="CivilizationRules.TemplateProblems"/>:
    /// its civilization and pool, nobody home in a lost civilization), every block known, every chest's table a loot table.
    /// </summary>
    public List<string> CivilizationTemplateProblems(StructureTemplate t)
    {
        var problems = LandmarkRules.Problems(t);
        problems.AddRange(CivilizationRules.TemplateProblems(t, GetCivilization(t.Civilization)));
        problems.AddRange(UnknownLandmarkBlocks(t));
        foreach (var c in t.Cells)
        {
            if (c != null && c.Kind == "marker" && c.Id == LandmarkRules.ChestMarker
                && MarkerSettings.Parse(c.Config).Get("table") is { Length: > 0 } table && LootTable(table) is null)
            {
                problems.Add($"a chest names the unknown loot table '{table}'");
                break;
            }
        }

        return problems;
    }

    /// <summary>A block, item, kit or loot table that names an unknown civilization keeps its tag (it groups nothing) — warned
    /// once per name, so a typo is seen.</summary>
    private void WarnUnknownCivilizationTags(Action<string>? warn)
    {
        if (warn is null)
        {
            return;
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        void Check(string? tag)
        {
            if (!string.IsNullOrEmpty(tag) && !_civilizations.ContainsKey(tag!))
            {
                unknown.Add(tag!);
            }
        }

        foreach (var b in _blocks.Values)
        {
            Check(b.Civilization);
        }

        foreach (var i in _items.Values)
        {
            Check(i.Civilization);
        }

        foreach (var t in _lootTables.Values)
        {
            Check(t.Civilization);
        }

        if (unknown.Count > 0)
        {
            warn($"Content names unknown civilization(s) {string.Join(", ", unknown)} — those tags group nothing.");
        }
    }
}
