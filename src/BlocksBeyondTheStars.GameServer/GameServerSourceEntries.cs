// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Generator entries in the pools (#3518) and whole templates the generation-38 way (#3519) — the pick and replay helpers of
/// the settlement, city, station, landmark and underground stampers.
/// <list type="bullet">
/// <item><b>The draw.</b> A pool's generator entries are appended AFTER its existing entries (the kits of a settlement, city or
/// station table; the authored landmarks; the underground kits and whole templates), only on a world of terrain generation 38
/// or later — so a pool without entries draws exactly as before. Weights are clamped to ≥ 1 like every pool; an entry that
/// must not be drawn is filtered out, never weighted 0.</item>
/// <item><b>The record.</b> Settlements, the city, stations and underground structures record <c>source:&lt;entry key&gt;</c>
/// (<see cref="SourceTemplates.RecordPrefix"/>); a landmark keeps its key and pins <c>src=&lt;source&gt;</c>. A replay builds the
/// entry again with the instance's seed; an entry that is gone (or no longer builds) leaves the stamped blocks as they are and
/// re-derives nothing — never another pick. A station, which is stamped every session, takes the procedural interior then,
/// like a kit that no longer replays.</item>
/// <item><b>Whole templates</b> (generation 38): turned by quarter turns (<c>turns=</c> in the record's kit layout, a hash of
/// the instance seed when fresh), their markers carrying their settings, and — on a ruin verdict, which a ruined-only world
/// always gives — fallen through the shared ruin pass with their keep cells standing.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    // ---------------------------------------------------------------- the draw

    /// <summary>The settlement pool's generator entries of a tier on this body (none before generation 38).</summary>
    private IReadOnlyList<StructureTemplate> SettlementSourcesFor(string tier)
        => TemplatePipelineWorld
            ? _content.SourceTemplatesFor(StructureKit.KindSettlement, tier, _meta.Description.EnabledStructurePacks, _world.Planet.Key)
            : System.Array.Empty<StructureTemplate>();

    /// <summary>The station pool's generator entries of a size tier (none before generation 38).</summary>
    private IReadOnlyList<StructureTemplate> StationSourcesFor(string sizeTier)
        => TemplatePipelineWorld
            ? _content.SourceTemplatesFor(StructureKit.KindStation, sizeTier, _meta.Description.EnabledStructurePacks, null)
            : System.Array.Empty<StructureTemplate>();

    /// <summary>
    /// The joint table of complete templates, kits and generator entries (#3518): with no entry exactly
    /// <see cref="PickTemplateOrKit"/>; with entries, the template branch is unchanged and the kit branch draws over the kits
    /// followed by the entries (so it is taken whenever kits or entries exist and the coin does not ask for a template).
    /// </summary>
    internal static (StructureTemplate? Template, StructureKit? Kit, StructureTemplate? Source) PickTemplateKitOrSource(
        IReadOnlyList<StructureTemplate> templates, IReadOnlyList<StructureKit> kits, IReadOnlyList<StructureTemplate> sources,
        bool wantTemplate, System.Random rng)
    {
        if (sources.Count == 0 || (templates.Count > 0 && wantTemplate))
        {
            var (t, k) = PickTemplateOrKit(templates, kits, wantTemplate, rng);
            return (t, k, null);
        }

        int total = 0;
        foreach (var k in kits) total += System.Math.Max(1, k.Weight);
        foreach (var s in sources) total += System.Math.Max(1, s.Weight);
        int r = rng.Next(total);
        foreach (var k in kits)
        {
            r -= System.Math.Max(1, k.Weight);
            if (r < 0)
            {
                return (null, k, null);
            }
        }

        return (null, null, AppendedSourceAt(sources, r));
    }

    /// <summary>The entry a draw that ran past every earlier entry lands on: <paramref name="r"/> is what is left of it.</summary>
    private static StructureTemplate? AppendedSourceAt(IReadOnlyList<StructureTemplate> sources, int r)
    {
        foreach (var s in sources)
        {
            r -= System.Math.Max(1, s.Weight);
            if (r < 0)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Test seam (#3518): the joint pick as keys (template, kit, entry).</summary>
    public static (string Template, string Kit, string Source) PickTemplateKitOrSourceForTest(IReadOnlyList<StructureTemplate> templates,
        IReadOnlyList<StructureKit> kits, IReadOnlyList<StructureTemplate> sources, bool wantTemplate, System.Random rng)
    {
        var (t, k, s) = PickTemplateKitOrSource(templates, kits, sources, wantTemplate, rng);
        return (t?.Key ?? string.Empty, k?.Key ?? string.Empty, s?.Key ?? string.Empty);
    }

    // ---------------------------------------------------------------- settlements and the city

    /// <summary>The generator entry a settlement record (<c>source:&lt;key&gt;</c>) pins, or null — warned — when it is gone.</summary>
    private StructureTemplate? PinnedSettlementSource(StructurePlacementRecord rec)
    {
        string key = SourceTemplates.KeyOfRecord(rec.Template);
        var entry = _content.SourceTemplateByKey(StructureKit.KindSettlement, key);
        if (entry is null)
        {
            _log.Warn($"Generator entry '{key}' of settlement {rec.Index} on '{_world.LocationId}' is no longer in the content — its blocks stay, nothing else is restored.");
        }

        return entry;
    }

    /// <summary>
    /// A whole settlement of generation 38: a whole template (turned, marker settings, fallen on a ruin verdict, its tokens in
    /// this planet's materials) or a generator entry (built with the instance seed, fallen on a ruin verdict). Null — warned —
    /// when an entry's source cannot build it.
    /// </summary>
    private SettlementStructure? BuildWholeSettlement(StructureTemplate entry, string tier, bool ruined, int turns, long instSeed, string surface)
    {
        if (!entry.IsSource)
        {
            return SettlementGenerator.FromTemplate(entry, _content, ModuleMaterials.ForSettlement(entry.Tier, surface, alien: false, _content),
                new TemplateStamp { Turns = turns, Ruined = ruined, Seed = instSeed, Surface = surface });
        }

        var built = SourceTemplates.Build(entry, _content, instSeed, surface, _world.Planet.Key, _meta.Description.TerrainGeneration, out string failure);
        if (built is null)
        {
            _log.Warn($"Generator entry '{entry.Key}' ({entry.Source}) cannot be built on '{_world.LocationId}': {failure} — nothing stands there.");
            return null;
        }

        return BuiltConverters.ToSettlement(built, tier, _content, belowGround: false, ruined, entry.Key, instSeed, surface);
    }

    /// <summary>A whole template's quarter turns (#3519): the record's <c>turns=</c> (0 when it has none), or — fresh — a hash
    /// of the instance seed.</summary>
    private static int SettlementTemplateTurns(StructurePlacementRecord? rec, long instSeed)
        => rec is { Placed: true }
            ? MarkerSettings.Parse(rec.KitLayout).GetInt("turns", 0) & 3
            : StableHashing.Index("settlement:turn", instSeed, 4);

    /// <summary>What a fresh settlement records as its template: <c>source:&lt;key&gt;</c> for a generator entry, the whole
    /// template's key, or the procedural/kit marker (<see cref="FreshProceduralTemplate"/>).</summary>
    private string RecordedSettlementTemplate(StructureTemplate? template, StructureTemplate? source, StructureKit? kit)
        => source is not null ? SourceTemplates.RecordOf(source) : template?.Key ?? FreshProceduralTemplate(kit);

    /// <summary>What a fresh settlement records as its kit layout: a kit's grid, a turned template's <c>turns=…</c>, or nothing.</summary>
    private static string RecordedSettlementLayout(StructureKit? kit, SettlementLayoutSpec? layout, int turns)
        => kit != null && layout is { } spec ? spec.Serialize()
            : turns != 0 ? new MarkerSettings().Set("turns", turns).ToString()
            : string.Empty;

    // ---------------------------------------------------------------- stations

    /// <summary>A station's generator entry built with the station's seed — null (warned) when the entry is gone or its source
    /// cannot build it.</summary>
    private StationStructure? BuildStationSource(BoardableStation station, string entryKey, long sSeed)
    {
        var entry = _content.SourceTemplateByKey(StructureKit.KindStation, entryKey);
        string failure = "it is no longer in the content";
        var built = entry is null ? null : SourceTemplates.Build(entry, _content, sSeed, "stone", null, _meta.Description.TerrainGeneration, out failure);
        if (built is null)
        {
            _log.Warn($"Station '{station.Name}': generator entry '{entryKey}' cannot be built ({failure}) — the procedural interior instead.");
            return null;
        }

        return BuiltConverters.ToStation(built, station.SizeTier);
    }

    // ---------------------------------------------------------------- landmarks and underground

    /// <summary>A landmark built from a generator entry (unturned; a ruin when the entry says so) — null (warned) when its
    /// source cannot build it.</summary>
    private LandmarkLayout? BuildSourceLandmark(StructureTemplate entry, long seed, int revision)
    {
        string surface = LandmarkSurfaceBlock();
        var built = SourceTemplates.Build(entry, _content, seed, surface, _world.Planet.Key, _meta.Description.TerrainGeneration, out string failure);
        if (built is null)
        {
            _log.Warn($"Landmark entry '{entry.Key}' ({entry.Source}) cannot be built on '{_world.LocationId}': {failure}.");
            return null;
        }

        return BuiltConverters.ToLandmarkLayout(built, entry, seed, _content, surface, revision);
    }

    /// <summary>An underground generator entry, every row kept (the enclosure lies underground) — null (warned) when it is gone
    /// or its source cannot build it.</summary>
    private SettlementStructure? BuildUndergroundSource(string entryKey, long seed, string surface)
    {
        var entry = _content.SourceTemplateByKey(StructureKit.KindSettlement, entryKey);
        string failure = "it is no longer in the content";
        var built = entry is null ? null : SourceTemplates.Build(entry, _content, seed, surface, _world.Planet.Key, _meta.Description.TerrainGeneration, out failure);
        if (built is null)
        {
            _log.Warn($"Underground generator entry '{entryKey}' on '{_world.LocationId}' cannot be built: {failure}.");
            return null;
        }

        return BuiltConverters.ToSettlement(built, entry!.Tier, _content, belowGround: true);
    }
}
