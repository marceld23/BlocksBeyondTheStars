// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3607: the giant ship pool of the content (<c>data/giant_ships/*.json</c> plus the editor's
/// <c>usercontent/giant_ship_templates/</c>). A giant ship is never a ship type: <see cref="Ships"/>, <see cref="GetShip"/>,
/// the user ships and the blueprints never see one.</summary>
public sealed partial class GameContent
{
    /// <inheritdoc cref="StructureCatalog.GiantShips"/>
    public IReadOnlyList<StructureTemplate> GiantShips => _structures.GiantShips;

    /// <inheritdoc cref="StructureCatalog.GiantShipByKey"/>
    public StructureTemplate? GetGiantShip(string? key) => _structures.GiantShipByKey(key);

    /// <inheritdoc cref="StructureCatalog.GiantShipsFor"/>
    public IReadOnlyList<StructureTemplate> GiantShipsFor(IReadOnlyCollection<string>? enabledPacks, string? planetTypeKey,
        bool namedTypeOnly = false, int generation = int.MaxValue)
        => _structures.GiantShipsFor(enabledPacks, planetTypeKey, namedTypeOnly, generation);

    /// <summary>Populates the giant ship pool (#3607): every template checked by <see cref="GiantShipProblems"/>, a broken one
    /// dropped with a warning — a giant ship never breaks the load. Call after the blocks and the loot tables are set.</summary>
    public void SetGiantShips(IReadOnlyList<StructureTemplate>? ships, Action<string>? warn = null)
        => _structures.SetGiantShips(ships, GiantShipProblems, warn);

    /// <summary>
    /// Everything wrong with a giant ship template against THIS content, one sentence each — empty when it can be used:
    /// <see cref="GiantShipRules.Problems"/> with the content's blocks (a floor must be a solid block, the headroom passable)
    /// plus every chest's <c>table</c> naming a loot table the content has. The load drops a template with any problem; the
    /// builders' check (the Tools command) prints them.
    /// </summary>
    public List<string> GiantShipProblems(StructureTemplate t) => TemplateProblem.Sentences(GiantShipProblemList(t));

    /// <summary><see cref="GiantShipProblems"/> as codes the structure editor translates (#3692): the same checks, the same
    /// sentences.</summary>
    public List<TemplateProblem> GiantShipProblemList(StructureTemplate t)
    {
        var problems = GiantShipRules.ProblemList(t, _blocks.ContainsKey,
            id => !_blocks.TryGetValue(id, out var b) || (b.Solid && !b.Liquid));
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
            problems.Add(new TemplateProblem("chest.unknown_tables", "a chest names unknown loot table(s) " + string.Join(", ", unknownTables),
                string.Join(", ", unknownTables)));
        }

        if (BlueprintLockedDrops(t) is { Count: > 0 } locked)
        {
            problems.Add(new TemplateProblem("giant_ship.blueprint_drops",
                "blocks that drop a blueprint-locked item (a wreck is minable and would hand it out for free): " + string.Join(", ", locked),
                string.Join(", ", locked)));
        }

        return problems;
    }

    /// <summary>The block categories whose blueprint-locked drop a wreck must not hand out: devices, not materials (clear
    /// glass or a pumpkin plant drop blueprint-made items too, but they are building stuff or grow wild).</summary>
    private static readonly HashSet<string> DeviceCategories = new(StringComparer.Ordinal) { "machine", "light", "door" };

    /// <summary>
    /// The DEVICE blocks of the template (<see cref="DeviceCategories"/>) whose drop is an item every recipe of which needs
    /// a blueprint (a heal tank, a fabricator, a signal display): a giant wreck is open to mining, so such a block would hand
    /// a blueprint-locked machine to anyone with a drill. An item with no recipe at all or with one recipe free of a
    /// blueprint is fine.
    /// </summary>
    private List<string> BlueprintLockedDrops(StructureTemplate t)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        TemplateBody.ForEachPlain(t, c =>
        {
            if (!c.Cavity && !string.IsNullOrEmpty(c.Id))
            {
                ids.Add(c.Id);
            }
        });
        var locked = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (_blocks.TryGetValue(id, out var block) && DeviceCategories.Contains(block.Category)
                && block.Drops.Exists(d => d.Item is { Length: > 0 } item && OnlyBlueprintMakes(item)))
            {
                locked.Add(id);
            }
        }

        return new List<string>(locked);
    }

    private bool OnlyBlueprintMakes(string item)
    {
        bool anyRecipe = false;
        foreach (var r in _recipes.Values)
        {
            if (r.Outputs.Exists(o => o.Item == item))
            {
                anyRecipe = true;
                if (string.IsNullOrEmpty(r.RequiredBlueprint))
                {
                    return false;
                }
            }
        }

        return anyRecipe;
    }
}
