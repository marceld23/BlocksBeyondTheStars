// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// What researching a blueprint opens (#2250) — the forward view the research screen never had: the items whose recipes
/// name it, the ship modules that require it, the blueprints that list it as a prerequisite, and the functions gated in
/// code that the data declares on the blueprint itself (<see cref="Definitions.BlueprintDefinition.Features"/>). All of
/// it is read from the content, so new content shows up without touching the screen.
/// </summary>
public sealed class BlueprintUnlocks
{
    /// <summary>Locale keys of the code-gated functions (the bio lab's Change tab, …).</summary>
    public IReadOnlyList<string> Features { get; }

    /// <summary>Item keys made by the recipes that require the blueprint, each once, in recipe order.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>Ship module keys that require the blueprint.</summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>Blueprint keys that list this one as a prerequisite, cheapest first.</summary>
    public IReadOnlyList<string> LeadsTo { get; }

    private BlueprintUnlocks(IReadOnlyList<string> features, IReadOnlyList<string> items, IReadOnlyList<string> modules,
        IReadOnlyList<string> leadsTo)
    {
        Features = features;
        Items = items;
        Modules = modules;
        LeadsTo = leadsTo;
    }

    /// <summary>True when nothing is listed.</summary>
    public bool IsEmpty => Features.Count == 0 && Items.Count == 0 && Modules.Count == 0 && LeadsTo.Count == 0;

    /// <summary>The forward view of <paramref name="blueprintKey"/> (empty for an unknown key).</summary>
    public static BlueprintUnlocks For(GameContent content, string blueprintKey)
    {
        var bp = content.GetBlueprint(blueprintKey);
        if (bp is null)
        {
            return new BlueprintUnlocks(System.Array.Empty<string>(), System.Array.Empty<string>(),
                System.Array.Empty<string>(), System.Array.Empty<string>());
        }

        var items = new List<string>();
        foreach (var recipe in content.Recipes.Values)
        {
            if (recipe.RequiredBlueprint != blueprintKey)
            {
                continue;
            }

            foreach (var output in recipe.Outputs)
            {
                if (!items.Contains(output.Item))
                {
                    items.Add(output.Item);
                }
            }
        }

        var modules = content.ShipModules.Values
            .Where(m => m.RequiredBlueprint == blueprintKey)
            .Select(m => m.Key)
            .ToList();

        var leadsTo = content.Blueprints.Values
            .Where(b => b.Prerequisites.Contains(blueprintKey))
            .OrderBy(b => b.KnowledgeCost)
            .ThenBy(b => b.Key, System.StringComparer.Ordinal)
            .Select(b => b.Key)
            .ToList();

        return new BlueprintUnlocks(bp.Features.ToList(), items, modules, leadsTo);
    }
}
