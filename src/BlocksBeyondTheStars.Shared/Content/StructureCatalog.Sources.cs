// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The generator entries of the pools (#3518, <see cref="StructureTemplate.Source"/>): templates without cells whose
/// structure a procedural source builds. They live in the station, settlement and landmark pools like authored templates
/// but are listed apart — the legacy picks (<see cref="PickSettlementTemplate"/>, <see cref="PickStationTemplate"/>), the
/// complete-template tables and the authored landmark list never see one, so a stamper appends them AFTER its existing
/// entries and every pool without entries draws exactly as before. Shared cannot see the source registry (it lives in
/// WorldGeneration): the load drops what is malformed here (cells, a module role), and the registry's own check — the source
/// exists, builds for the pool, takes the parameters — runs through <see cref="RemoveSourceTemplates"/>
/// (<c>SourceTemplates.Validate</c> in WorldGeneration, called by the server at start).
/// </summary>
public sealed partial class StructureCatalog
{
    /// <summary>The pool name <see cref="RemoveSourceTemplates"/> hands its check for a landmark entry.</summary>
    public const string LandmarkPool = "landmark";

    /// <summary>
    /// The WHOLE generator entries a slot may draw from (#3518): of the <paramref name="kind"/>'s pool (station; settlement
    /// and city both read the settlement pool), of <paramref name="tier"/> (empty = medium, like the tier pools), not pin-only,
    /// not underground, in the enabled packs and on the planet type — in pool order (the draw is weighted over this order).
    /// </summary>
    public IReadOnlyList<StructureTemplate> SourceTemplatesFor(string kind, string tier, IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var pool = kind == StructureKit.KindStation ? StationTemplates : SettlementTemplates;
        string wanted = string.IsNullOrWhiteSpace(tier) ? "medium" : tier;
        var list = new List<StructureTemplate>();
        foreach (var t in pool)
        {
            if (t.IsSource && !t.PinOnly && !t.Underground && string.Equals(TierOf(t), wanted, StringComparison.OrdinalIgnoreCase)
                && TemplateFits(t, enabledPacks, planetType))
            {
                list.Add(t);
            }
        }

        return list;
    }

    /// <summary>#3518: the UNDERGROUND generator entries a planet may carry, of any tier — not pin-only, in the enabled packs and
    /// on the planet, in pool order. The underground stamper appends them after its kits and whole templates.</summary>
    public IReadOnlyList<StructureTemplate> UndergroundSourceTemplatesFor(IReadOnlyCollection<string>? enabledPacks, string? planetType)
    {
        var list = new List<StructureTemplate>();
        foreach (var t in SettlementTemplates)
        {
            if (t.IsSource && t.Underground && !t.PinOnly && TemplateFits(t, enabledPacks, planetType))
            {
                list.Add(t);
            }
        }

        return list;
    }

    /// <summary>#3518: a generator entry of the <paramref name="kind"/>'s pool by exact key (the replay of a record
    /// <c>source:&lt;key&gt;</c>; pin-only and underground ones included), or null when it no longer exists.</summary>
    public StructureTemplate? SourceTemplateByKey(string kind, string key)
    {
        var pool = kind == StructureKit.KindStation ? StationTemplates : SettlementTemplates;
        foreach (var t in pool)
        {
            if (t.IsSource && t.Key == key)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// Drops every generator entry <paramref name="problemOf"/> finds a problem with — called with the template and its pool
    /// (<see cref="StructureKit.KindStation"/>, <see cref="StructureKit.KindSettlement"/> or <see cref="LandmarkPool"/>), null
    /// when it is fine — with one warning each, and rebuilds the pools. Authored templates are never asked. Returns how many
    /// were dropped; nothing changes (and nothing is rebuilt) when none is.
    /// </summary>
    public int RemoveSourceTemplates(Func<StructureTemplate, string, string?> problemOf, Action<string>? warn = null)
    {
        if (problemOf is null)
        {
            throw new ArgumentNullException(nameof(problemOf));
        }

        int dropped = 0;
        List<StructureTemplate> Keep(IReadOnlyList<StructureTemplate> pool, string poolName)
        {
            var kept = new List<StructureTemplate>(pool.Count);
            foreach (var t in pool)
            {
                if (t.IsSource && problemOf(t, poolName) is { } problem)
                {
                    warn?.Invoke($"Skipping generator entry '{t.Key}' ({poolName}): {problem}.");
                    dropped++;
                    continue;
                }

                kept.Add(t);
            }

            return kept;
        }

        var stations = Keep(StationTemplates, StructureKit.KindStation);
        var settlements = Keep(SettlementTemplates, StructureKit.KindSettlement);
        var landmarks = Keep(Landmarks, LandmarkPool);
        if (dropped == 0)
        {
            return 0;
        }

        StationTemplates = stations;
        SettlementTemplates = settlements;
        Landmarks = landmarks;
        _stationsByTier = GroupByTier(StationTemplates);
        _settlementsByTier = GroupByTier(SettlementTemplates);
        RefreshStructurePacks();
        return dropped;
    }

    /// <summary>What is wrong with the SHAPE of a generator entry, whatever its source says: it holds cells (an entry is
    /// built, never authored) or is a kit module (an entry is a whole structure). Empty for an authored template.</summary>
    public static List<string> SourceShapeProblems(StructureTemplate t)
    {
        var problems = new List<string>();
        if (t is null || !t.IsSource)
        {
            return problems;
        }

        if (t.Cells.Count > 0 || t.Rows.Count > 0)
        {
            problems.Add("a generator entry holds no cells — its source builds the structure");
        }

        if (t.IsModule)
        {
            problems.Add("a generator entry is a whole structure, never a kit module");
        }

        return problems;
    }

    private static string TierOf(StructureTemplate t) => string.IsNullOrWhiteSpace(t.Tier) ? "medium" : t.Tier;

    /// <summary>The template key a generation-38 record carries for a procedural settlement
    /// (<c>StructurePlacementRecord.ProceduralTemplate</c>).</summary>
    private const string ProceduralRecordKey = "procedural";

    /// <summary>The record prefixes of a kit composition (<c>kit:</c>) and a generator entry (<c>source:</c>).</summary>
    private static readonly string[] RecordKeyPrefixes = { "kit:", "source:" };

    /// <summary>
    /// True when no template may take <paramref name="key"/> (case-insensitive): the record forms a stamper writes instead of a
    /// template key — <c>procedural</c>, <c>kit:…</c> and <c>source:…</c>. A template keyed like one would be mistaken for it
    /// on every replay (a template named <c>procedural</c> replayed as the procedural village).
    /// </summary>
    public static bool IsReservedTemplateKey(string? key)
    {
        string k = (key ?? string.Empty).Trim();
        return string.Equals(k, ProceduralRecordKey, StringComparison.OrdinalIgnoreCase)
            || RecordKeyPrefixes.Any(p => k.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Why a template cannot join its pool: a reserved key (<see cref="IsReservedTemplateKey"/>) or a malformed
    /// generator entry (<see cref="SourceShapeProblems"/>). Empty when it can.</summary>
    private static List<string> PoolProblems(StructureTemplate t)
    {
        var problems = SourceShapeProblems(t);
        if (IsReservedTemplateKey(t.Key))
        {
            problems.Insert(0, "the key is reserved for the placement records (procedural, kit:…, source:…)");
        }

        return problems;
    }

    /// <summary>The pool without the templates that cannot join it (<see cref="PoolProblems"/>: a reserved key, a malformed
    /// generator entry), one warning each; the same list when every template is fine.</summary>
    private static IReadOnlyList<StructureTemplate> KeepWellFormedSources(IReadOnlyList<StructureTemplate> pool, string kind, Action<string>? warn)
    {
        if (!pool.Any(t => t != null && PoolProblems(t).Count > 0))
        {
            return pool;
        }

        var kept = new List<StructureTemplate>(pool.Count);
        foreach (var t in pool)
        {
            var problems = t is null ? new List<string>() : PoolProblems(t);
            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping {kind} {(t!.IsSource ? "generator entry" : "template")} '{t.Key}': {string.Join("; ", problems)}.");
                continue;
            }

            kept.Add(t!);
        }

        return kept;
    }

    /// <summary>#3519: a block cell that names a material token nobody knows (<c>@wal</c> for <c>@wall</c>) would stamp as
    /// air — it is reported, once per template and token, and left as it is.</summary>
    private static void ReportUnknownTokens(IReadOnlyList<StructureTemplate> pool, string kind, Action<string>? warn)
    {
        if (warn is null)
        {
            return;
        }

        foreach (var t in pool)
        {
            if (t is null)
            {
                continue;
            }

            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var c in t.Cells)
            {
                if (c.Kind == "block" && c.Id.StartsWith("@", StringComparison.Ordinal) && !MaterialTokens.IsToken(c.Id))
                {
                    unknown.Add(c.Id);
                }
            }

            if (unknown.Count > 0)
            {
                warn($"{kind} template '{t.Key}' names unknown material token(s) {string.Join(", ", unknown)} " +
                     $"({string.Join(" | ", MaterialTokens.All)}) — those cells stay air.");
            }
        }
    }
}
