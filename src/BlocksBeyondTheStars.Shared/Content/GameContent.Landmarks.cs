// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The landmark pool (#3484) and the loot tables (#3488) of the content.</summary>
public sealed partial class GameContent
{
    private readonly Dictionary<string, LootTableDefinition> _lootTables = new(StringComparer.Ordinal);

    /// <inheritdoc cref="StructureCatalog.Landmarks"/>
    public IReadOnlyList<StructureTemplate> Landmarks => _structures.Landmarks;

    /// <inheritdoc cref="StructureCatalog.LandmarkByKey"/>
    public StructureTemplate? LandmarkByKey(string key) => _structures.LandmarkByKey(key);

    /// <inheritdoc cref="StructureCatalog.LandmarksFor"/>
    public IReadOnlyList<StructureTemplate> LandmarksFor(IReadOnlyCollection<string>? enabledPacks, string? planetType,
        bool atmosphere, bool inhabited, bool namedTypeOnly = false, bool sources = false)
        => _structures.LandmarksFor(enabledPacks, planetType, atmosphere, inhabited, namedTypeOnly, sources);

    /// <summary>Populates the landmark pool (#3484). Beside <see cref="LandmarkRules.Problems"/> a landmark is dropped
    /// (with a warning) when a block of its body does not exist — a landmark never breaks the load.</summary>
    public void SetLandmarks(IReadOnlyList<StructureTemplate>? landmarks, Action<string>? warn = null)
    {
        _structures.SetLandmarks(landmarks, warn, UnknownLandmarkBlocks);
        foreach (var planet in _planets.Values)
        {
            foreach (string key in planet.FixedLandmarks ?? new List<string>())
            {
                if (LandmarkByKey(key) is null)
                {
                    warn?.Invoke($"Planet '{planet.Key}' names the fixed landmark '{key}', which is not in the landmark pool — skipped.");
                }
            }
        }
    }

    /// <summary>
    /// The fixed landmarks of a planet type (terrain generation 39, <see cref="PlanetType.FixedLandmarks"/>): each key the
    /// landmark pool holds, with its position in the list (its placement record index), in list order. A key the pool does
    /// not hold is skipped (reported at load). Pack, planet types, conditions and <c>pinOnly</c> are not asked: a fixed
    /// landmark always stands on its type.
    /// </summary>
    public IReadOnlyList<(int Index, StructureTemplate Template)> FixedLandmarksFor(PlanetType? planet)
    {
        var list = new List<(int, StructureTemplate)>();
        var keys = planet?.FixedLandmarks ?? new List<string>();
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] is { Length: > 0 } key && LandmarkByKey(key) is { } template)
            {
                list.Add((i, template));
            }
        }

        return list;
    }

    /// <summary>The block keys a landmark names that the content does not have (material tokens are fine).</summary>
    private IEnumerable<string> UnknownLandmarkBlocks(StructureTemplate t)
    {
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        void Check(string kind, string id)
        {
            if (kind == "block" && !MaterialTokens.IsToken(id) && !_blocks.ContainsKey(id))
            {
                unknown.Add(id.Length == 0 ? "(empty)" : id);
            }
        }

        foreach (var p in t.Palette)
        {
            Check(p.Kind, p.Id);
        }

        foreach (var c in t.Cells)
        {
            Check(c.Kind, c.Id);
        }

        return unknown.Count == 0
            ? Array.Empty<string>()
            : new[] { "unknown block(s) " + string.Join(", ", unknown) };
    }

    /// <summary>#3488: the loot tables by key (<c>data/loot_tables.json</c>).</summary>
    public IReadOnlyDictionary<string, LootTableDefinition> LootTables => _lootTables;

    /// <summary>The loot table with this key, or null.</summary>
    public LootTableDefinition? LootTable(string? key)
        => !string.IsNullOrEmpty(key) && _lootTables.TryGetValue(key!, out var t) ? t : null;

    /// <summary>Populates the loot tables; a table without a key is ignored, a later table replaces an earlier one of
    /// the same key (a user table over a shipped one).</summary>
    public void SetLootTables(IEnumerable<LootTableDefinition>? tables)
    {
        _lootTables.Clear();
        foreach (var t in tables ?? Enumerable.Empty<LootTableDefinition>())
        {
            if (t != null && !string.IsNullOrWhiteSpace(t.Key))
            {
                _lootTables[t.Key] = t;
            }
        }
    }

    /// <summary>Every loot table names real items, sane ranges and chances.</summary>
    private void ValidateLootTables(List<string> problems, Action<string, string?> requireItem)
    {
        foreach (var table in _lootTables.Values)
        {
            if (table.Rolls.Count == 0)
            {
                problems.Add($"Loot table '{table.Key}' has no roll.");
            }

            foreach (var roll in table.Rolls)
            {
                if (roll.Chance < 0 || roll.Chance > 1 || roll.Picks < 1 || roll.Entries.Count == 0)
                {
                    problems.Add($"Loot table '{table.Key}' has a roll with a chance outside 0..1, no pick or no entry.");
                }

                foreach (var e in roll.Entries)
                {
                    requireItem($"Loot table '{table.Key}'", e.Item);
                    if (e.Min < 1 || e.Max < e.Min || e.Weight < 1)
                    {
                        problems.Add($"Loot table '{table.Key}' entry '{e.Item}' needs 1 <= min <= max and a weight of at least 1.");
                    }
                }
            }
        }
    }
}
