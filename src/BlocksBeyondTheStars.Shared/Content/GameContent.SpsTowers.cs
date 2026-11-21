// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3893: the guide tower pool of the content (<c>data/sps_towers/*.json</c>) — the ruins of the Scout and Pioneer
/// Service's guide towers. No landmark, settlement or giant ship pool ever sees one.</summary>
public sealed partial class GameContent
{
    /// <inheritdoc cref="StructureCatalog.SpsTowers"/>
    public IReadOnlyList<StructureTemplate> SpsTowers => _structures.SpsTowers;

    /// <inheritdoc cref="StructureCatalog.SpsTowerByKey"/>
    public StructureTemplate? GetSpsTower(string? key) => _structures.SpsTowerByKey(key);

    /// <inheritdoc cref="StructureCatalog.SpsTowersFor"/>
    public IReadOnlyList<StructureTemplate> SpsTowersFor(IReadOnlyCollection<string>? enabledPacks, string? planetTypeKey,
        bool namedTypeOnly = false, int generation = int.MaxValue)
        => _structures.SpsTowersFor(enabledPacks, planetTypeKey, namedTypeOnly, generation);

    /// <summary>Populates the tower pool: every template checked by <see cref="SpsTowerProblems"/>, a broken one dropped with a
    /// warning — a tower never breaks the load. Call after the blocks and the loot tables are set.</summary>
    public void SetSpsTowers(IReadOnlyList<StructureTemplate>? towers, Action<string>? warn = null)
        => _structures.SetSpsTowers(towers, SpsTowerProblems, warn);

    /// <summary>
    /// Everything wrong with a guide tower template against THIS content, one sentence each — empty when it can be used:
    /// <see cref="SpsTowerRules.Problems"/> with the content's blocks, the blocks the markers place (the relay console, the
    /// emergency lamp, the capsule tank) present in the content, and every chest's <c>table</c> naming a loot table the
    /// content has.
    /// </summary>
    public List<string> SpsTowerProblems(StructureTemplate t)
    {
        var problems = SpsTowerRules.Problems(t, _blocks.ContainsKey);
        foreach (string block in new[] { SpsTowerRules.ConsoleBlock, SpsTowerRules.LampBlock, SpsTowerRules.TankBlock })
        {
            if (!_blocks.ContainsKey(block))
            {
                problems.Add($"the content has no block '{block}' (a tower's markers place it)");
            }
        }

        var unknownTables = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in t.Cells)
        {
            if (c != null && c.Kind == "marker" && c.Id == LandmarkRules.ChestMarker
                && MarkerSettings.Parse(c.Config).Get("table") is { Length: > 0 } table && LootTable(table) is null)
            {
                unknownTables.Add(table);
            }
        }

        if (unknownTables.Count > 0)
        {
            problems.Add("a chest names unknown loot table(s) " + string.Join(", ", unknownTables));
        }

        return problems;
    }
}
