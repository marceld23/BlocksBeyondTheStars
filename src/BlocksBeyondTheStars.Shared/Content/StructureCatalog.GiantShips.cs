// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3607: the GIANT SHIP pool — huge authored spaceships that exist only as wrecks (<see cref="GiantShipRules"/>). A pool of its
/// own like the landmarks': no settlement, station, module, landmark or underground pool ever offers one (each of those drops a
/// template flagged <see cref="StructureTemplate.GiantShip"/> with a warning), and nothing but a giant ship enters it. A giant
/// ship is no ship type either: it never reaches <c>GameContent.Ships</c>, the user ships or any blueprint.
/// </summary>
public sealed partial class StructureCatalog
{
    /// <summary>Every loaded giant ship, in pool order (the stampers draw by weight in this order).</summary>
    public IReadOnlyList<StructureTemplate> GiantShips { get; private set; } = System.Array.Empty<StructureTemplate>();

    /// <summary>Populates the giant ship pool. A template without a key, with a key already taken or with a problem
    /// (whatever <paramref name="problems"/> finds — the content's full check, <see cref="GiantShipRules.Problems"/> with the
    /// blocks and loot tables) is dropped with a warning, so one bad file never breaks the load. Each kept template is flagged
    /// <see cref="StructureTemplate.GiantShip"/> and gets the conventional name key when it names none.</summary>
    public void SetGiantShips(IReadOnlyList<StructureTemplate>? ships, Func<StructureTemplate, IEnumerable<string>> problems,
        Action<string>? warn = null)
    {
        var kept = new List<StructureTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in ships ?? System.Array.Empty<StructureTemplate>())
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Key))
            {
                warn?.Invoke("Skipping a giant ship without a key.");
                continue;
            }

            if (!seen.Add(t.Key))
            {
                warn?.Invoke($"Skipping giant ship '{t.Key}': a giant ship with that key is already loaded.");
                continue;
            }

            t.GiantShip = true;
            t.Cells ??= new List<TemplateCell>(); // a hand-edited file may say "cells": null — a problem at most, never a failed load
            t.Palette ??= new List<TemplatePaletteEntry>();
            t.Rows ??= new List<string>();
            t.PlanetTypes ??= new List<string>();
            var found = problems(t).ToList();
            if (found.Count > 0)
            {
                warn?.Invoke($"Skipping giant ship '{t.Key}': {string.Join("; ", found)}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(t.NameKey))
            {
                t.NameKey = GiantShipRules.NameKey(t.Key);
            }

            kept.Add(t);
        }

        GiantShips = kept;
        RefreshStructurePacks();
    }

    /// <summary>A giant ship by exact key (a pinned record, an admin stamp), or null when it no longer exists. Pin-only ships
    /// and every pack are found.</summary>
    public StructureTemplate? GiantShipByKey(string? key)
    {
        foreach (var t in GiantShips)
        {
            if (string.Equals(t.Key, key, StringComparison.Ordinal))
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// The giant ships a body may carry, in pool order: not pin-only, in the enabled packs (null or empty = every pack), no
    /// younger than the world (<see cref="StructureTemplate.MinTerrainGeneration"/> ≤ <paramref name="generation"/>), and on
    /// the planet type — an empty <see cref="StructureTemplate.PlanetTypes"/> means every type, unless
    /// <paramref name="namedTypeOnly"/> (a planet type with a structure whitelist naming <see cref="GiantShipRules.StructureKind"/>):
    /// then only the ships whose list NAMES the type are offered.
    /// </summary>
    public IReadOnlyList<StructureTemplate> GiantShipsFor(IReadOnlyCollection<string>? enabledPacks, string? planetType,
        bool namedTypeOnly = false, int generation = int.MaxValue)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in GiantShips)
        {
            if (t.PinOnly || t.MinTerrainGeneration > generation || !TemplateFits(t, enabledPacks, planetType))
            {
                continue;
            }

            if (namedTypeOnly && (string.IsNullOrEmpty(planetType) || !t.PlanetTypes.Contains(planetType!, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            list.Add(t);
        }

        return list;
    }

    /// <summary>Leaves the giant ships out of a pool that is not theirs (a file saved to the wrong folder), with a warning each.</summary>
    private static IReadOnlyList<StructureTemplate> WithoutGiantShips(IReadOnlyList<StructureTemplate> pool, string what, Action<string>? warn)
    {
        if (!pool.Any(t => t != null && t.GiantShip))
        {
            return pool;
        }

        foreach (var t in pool.Where(t => t != null && t.GiantShip))
        {
            warn?.Invoke($"{what} '{t.Key}' is a giant ship — it belongs in data/{GiantShipRules.DataFolder} and is not offered as one.");
        }

        return pool.Where(t => t == null || !t.GiantShip).ToList();
    }
}
