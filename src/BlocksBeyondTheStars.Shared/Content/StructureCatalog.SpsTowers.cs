// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3893: the SPS GUIDE TOWER pool — the ruins of the Scout and Pioneer Service's guide towers (<see cref="SpsTowerRules"/>).
/// A pool of its own like the giant ships': no settlement, station, module, landmark or underground pool ever offers one, and
/// nothing but a tower enters it.
/// </summary>
public sealed partial class StructureCatalog
{
    /// <summary>Every loaded guide tower, in pool order (the stamper draws by weight in this order).</summary>
    public IReadOnlyList<StructureTemplate> SpsTowers { get; private set; } = System.Array.Empty<StructureTemplate>();

    /// <summary>Populates the tower pool. A template without a key, with a key already taken or with a problem (whatever
    /// <paramref name="problems"/> finds — <see cref="SpsTowerRules.Problems"/> with the content's blocks and loot tables) is
    /// dropped with a warning, so one bad file never breaks the load. Each kept template is flagged
    /// <see cref="StructureTemplate.SpsTower"/> and gets the conventional name key when it names none.</summary>
    public void SetSpsTowers(IReadOnlyList<StructureTemplate>? towers, Func<StructureTemplate, IEnumerable<string>> problems,
        Action<string>? warn = null)
    {
        var kept = new List<StructureTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in towers ?? System.Array.Empty<StructureTemplate>())
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Key))
            {
                warn?.Invoke("Skipping a guide tower without a key.");
                continue;
            }

            if (!seen.Add(t.Key))
            {
                warn?.Invoke($"Skipping guide tower '{t.Key}': a guide tower with that key is already loaded.");
                continue;
            }

            t.SpsTower = true;
            t.Cells ??= new List<TemplateCell>(); // a hand-edited file may say "cells": null — a problem at most, never a failed load
            t.Palette ??= new List<TemplatePaletteEntry>();
            t.Rows ??= new List<string>();
            t.PlanetTypes ??= new List<string>();
            t.Requires ??= new List<string>();
            t.RuinZones ??= new List<RuinZone>();
            var found = problems(t).ToList();
            if (found.Count > 0)
            {
                warn?.Invoke($"Skipping guide tower '{t.Key}': {string.Join("; ", found)}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(t.NameKey))
            {
                t.NameKey = SpsTowerRules.NameKey(t.Key);
            }

            kept.Add(t);
        }

        SpsTowers = kept;
        RefreshStructurePacks();
    }

    /// <summary>A guide tower by exact key (a pinned record, an admin stamp), or null when it no longer exists. Pin-only
    /// towers and every pack are found.</summary>
    public StructureTemplate? SpsTowerByKey(string? key)
    {
        foreach (var t in SpsTowers)
        {
            if (string.Equals(t.Key, key, StringComparison.Ordinal))
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// The guide towers a body may carry, in pool order: not pin-only, in the enabled packs (null or empty = every pack), no
    /// younger than the world (<see cref="StructureTemplate.MinTerrainGeneration"/> ≤ <paramref name="generation"/>), and on
    /// the planet type — an empty <see cref="StructureTemplate.PlanetTypes"/> means every type, unless
    /// <paramref name="namedTypeOnly"/> (a planet type with a structure whitelist naming <see cref="SpsTowerRules.StructureKind"/>):
    /// then only the towers whose list NAMES the type are offered.
    /// </summary>
    public IReadOnlyList<StructureTemplate> SpsTowersFor(IReadOnlyCollection<string>? enabledPacks, string? planetType,
        bool namedTypeOnly = false, int generation = int.MaxValue)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in SpsTowers)
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
}
