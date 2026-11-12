// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// Generator entries (#3518): a <see cref="StructureTemplate"/> without cells whose <see cref="StructureTemplate.Source"/>
/// names a structure source and whose <see cref="StructureTemplate.SourceParams"/> set its parameters. They join the
/// settlement, city, station, landmark and underground pools as weighted entries (appended after the existing ones, on worlds
/// of terrain generation 38 or later). This class is the registry's side: the load check (<see cref="Validate"/>), the build
/// (<see cref="Build"/>) and the record form (<c>source:&lt;key&gt;</c>, <see cref="RecordPrefix"/>; a landmark keeps its key
/// and pins <c>src=&lt;source&gt;</c>, <see cref="PinKey"/>). The conversions into what each stamper takes are in
/// <see cref="BuiltConverters"/>.
/// </summary>
public static class SourceTemplates
{
    /// <summary>The record template (settlements, the city, stations, underground) of an instance built from a generator
    /// entry: <c>source:&lt;entry key&gt;</c>. A replay looks the entry up by key and builds it again; an entry that is gone
    /// leaves the stamped blocks as they are and re-derives nothing — it never falls back to another pick.</summary>
    public const string RecordPrefix = "source:";

    /// <summary>The landmark pin key (<c>src=&lt;source key&gt;</c>) a landmark built from a generator entry carries.</summary>
    public const string PinKey = "src";

    /// <summary>Whether a record's template names a generator entry.</summary>
    public static bool IsRecord(string? recordTemplate)
        => recordTemplate is not null && recordTemplate.StartsWith(RecordPrefix, StringComparison.Ordinal);

    /// <summary>The record template of an entry.</summary>
    public static string RecordOf(StructureTemplate entry) => RecordPrefix + entry.Key;

    /// <summary>The entry key a record template names (call only when <see cref="IsRecord"/>).</summary>
    public static string KeyOfRecord(string recordTemplate) => recordTemplate.Substring(RecordPrefix.Length);

    /// <summary>The sources that compose from the content's live kit pool and so can never be an entry: an entry's output must
    /// depend on its seed and parameters alone, or a new kit would change what an existing world replays.</summary>
    private static readonly string[] NotEntrySources = { "station_kit" };

    /// <summary>The sources that, built as an entry, build purely procedurally (<see cref="StructureBuildRequest.AsEntry"/>: no
    /// modules, no kit) — so their <c>kit</c> parameter must stay empty.</summary>
    private static readonly string[] ProceduralEntrySources = { "settlement", "city" };

    /// <summary>The one source a metropolis-tier entry may name (<see cref="CityGenerator.Tier"/>): the walled city, built
    /// around the landing pads it keeps open. Any other source would stand on them.</summary>
    public const string CityEntrySource = "city";

    /// <summary>Whether a source may stand as a generator entry (<c>station_kit</c> may not).</summary>
    public static bool CanBeEntry(string sourceKey) => Array.IndexOf(NotEntrySources, sourceKey) < 0;

    /// <summary>
    /// What is wrong with a generator entry of a pool (<see cref="StructureKit.KindStation"/>,
    /// <see cref="StructureKit.KindSettlement"/>, <see cref="StructureCatalog.LandmarkPool"/>) — null when it is fine: the
    /// source must exist, may stand as an entry (<see cref="CanBeEntry"/>), build for the pool (a station pool takes space
    /// sources; the settlement and landmark pools surface ones; an underground entry surface or underground ones; the
    /// metropolis tier only <see cref="CityEntrySource"/>) and take the parameters (<see cref="StructureSources.TryParseParams"/>)
    /// — a settlement or city entry with an empty <c>kit</c>.
    /// </summary>
    public static string? Problem(StructureTemplate entry, string pool, GameContent content)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var source = StructureSources.Get(entry.Source.Trim());
        if (source is null)
        {
            return $"unknown source '{entry.Source}'";
        }

        if (!CanBeEntry(source.Key))
        {
            return $"the source '{source.Key}' composes from the live kit pool — a generator entry must build from its seed and parameters alone";
        }

        if (!KindFits(source.Kind, pool, entry.Underground))
        {
            return $"the source '{source.Key}' builds {source.Kind.ToString().ToLowerInvariant()} structures, which the {pool} pool does not take"
                   + (entry.Underground ? " underground" : string.Empty);
        }

        if (pool == StructureKit.KindSettlement && !entry.Underground && entry.Tier == CityGenerator.Tier && source.Key != CityEntrySource)
        {
            // The metropolis tier is a city world's city, seated on the landing pads: only the city source keeps them open.
            return $"a {CityGenerator.Tier} entry stands on a city world's landing pads — only the '{CityEntrySource}' source keeps them open";
        }

        if (!StructureSources.TryParseParams(source, content, entry.SourceParams, out var values, out string error))
        {
            return error;
        }

        return Array.IndexOf(ProceduralEntrySources, source.Key) >= 0 && values.TryGetValue("kit", out var kit) && kit.Length > 0
            ? $"a '{source.Key}' generator entry builds procedurally — leave its kit empty (the pools draw kits themselves)"
            : null;
    }

    /// <summary>Drops every generator entry of <paramref name="content"/> the registry does not accept (<see cref="Problem"/>),
    /// with one warning each. Idempotent; the server calls it once at start, after the content load. Returns how many were
    /// dropped.</summary>
    public static int Validate(GameContent content, Action<string>? warn = null)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        return content.Structures.RemoveSourceTemplates((t, pool) => Problem(t, pool, content), warn);
    }

    /// <summary>
    /// Builds a generator entry as it stands in a world: its source with the entry's parameters (an invalid one at its default),
    /// <paramref name="seed"/> (the instance seed), the planet's surface and type and the world's terrain generation — as an
    /// entry (<see cref="StructureBuildRequest.AsEntry"/>: from seed and parameters alone, never from the live pools). Null —
    /// with the reason in <paramref name="failure"/> — when the source is gone, may not stand as an entry or cannot build.
    /// Never throws for a bad entry: whatever its source throws is reported here, so one entry can never stop a world loading.
    /// </summary>
    public static BuiltStructure? Build(StructureTemplate entry, GameContent content, long seed, string surface, string? planetType,
        int generation, out string failure)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        failure = string.Empty;
        var source = StructureSources.Get((entry.Source ?? string.Empty).Trim());
        if (source is null || !CanBeEntry(source.Key))
        {
            failure = source is null ? $"unknown source '{entry.Source}'" : $"the source '{source.Key}' cannot stand as a generator entry";
            return null;
        }

        try
        {
            _ = StructureSources.TryParseParams(source, content, entry.SourceParams, out var values, out _);
            var request = new StructureBuildRequest(content, seed)
            {
                Params = values,
                Surface = string.IsNullOrEmpty(surface) ? "stone" : surface,
                PlanetType = planetType ?? string.Empty,
                Generation = generation,
                AsInWorld = true,
                AsEntry = true,
            };
            return source.Build(request);
        }
        catch (Exception ex) // any of them: a broken entry is reported and treated as unbuildable
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static bool KindFits(StructureSourceKind kind, string pool, bool underground) => pool switch
    {
        StructureKit.KindStation => kind == StructureSourceKind.Space,
        StructureKit.KindSettlement when underground => kind is StructureSourceKind.Surface or StructureSourceKind.Underground,
        _ => kind == StructureSourceKind.Surface,
    };
}

/// <summary>
/// Turns a <see cref="BuiltStructure"/> into what each stamper takes (#3518): the settlement stamper's
/// <see cref="SettlementStructure"/> (the surface pools, the city and the underground enclosure), the station's
/// <see cref="StationStructure"/> and the landmark's <see cref="LandmarkLayout"/> — cell by cell, marker by marker (data and
/// door axis kept).
/// </summary>
public static class BuiltConverters
{
    /// <summary>
    /// A settlement-shaped structure of <paramref name="tier"/>. The rows below the built structure's ground row lie in the
    /// ground and are left out unless <paramref name="belowGround"/> (an underground enclosure keeps every row); a marker in a
    /// row left out goes with it. <paramref name="ruined"/> brings it down through the shared ruin pass exactly like a fallen
    /// whole template (<c>SettlementGenerator.Fall</c>: masonry, the instance seed on the lane <c>ruin:source:&lt;key&gt;</c>).
    /// </summary>
    public static SettlementStructure ToSettlement(BuiltStructure b, string tier, GameContent content, bool belowGround = false,
        bool ruined = false, string entryKey = "", long seed = 0, string surface = "stone")
    {
        if (b is null)
        {
            throw new ArgumentNullException(nameof(b));
        }

        int y0 = belowGround ? 0 : b.GroundRow;
        int w = b.Width, h = b.Height - y0, l = b.Length;
        var grid = new VoxelGrid(w, h, l);
        Copy(b, y0, grid);
        var markers = new List<SettlementMarker>();
        int buildings = 0;
        foreach (var m in b.Markers)
        {
            if (m.Y - y0 < 0)
            {
                continue;
            }

            markers.Add(new SettlementMarker(m.Type, new Shared.Geometry.Vector3i(m.X, m.Y - y0, m.Z), m.DoorAxis, m.Data));
            buildings += m.Type is "npc" or "vendor" ? 1 : 0;
        }

        if (ruined)
        {
            return SettlementGenerator.Fall(grid, markers, tier, "source:" + entryKey, seed, surface, LandmarkRules.ProfileMasonry,
                StructureRuiner.DefaultStrength(LandmarkRules.ProfileMasonry), content);
        }

        return new SettlementStructure(w, h, l, tier, ruined: false, inhabitant: "human", grid.Blocks, markers, Math.Max(1, buildings),
            grid.Mods, grid.Shapes);
    }

    /// <summary>A station structure of <paramref name="tier"/>: every row, the markers with their data, and the services a
    /// station must offer at its centre when the source built none (<c>StationGenerator.AddServiceMarkers</c>).</summary>
    public static StationStructure ToStation(BuiltStructure b, string tier)
    {
        if (b is null)
        {
            throw new ArgumentNullException(nameof(b));
        }

        var grid = new VoxelGrid(b.Width, b.Height, b.Length);
        Copy(b, 0, grid);
        var markers = new List<StationMarker>();
        foreach (var m in b.Markers)
        {
            markers.Add(new StationMarker(m.Type, new Shared.Geometry.Vector3i(m.X, m.Y, m.Z), m.Data));
        }

        StationGenerator.AddServiceMarkers(markers, b.Width, b.Length);
        return new StationStructure(b.Width, b.Height, b.Length, tier, 7, 6, 7, grid.Blocks, markers, new List<StationModule>(),
            grid.Mods, grid.Shapes);
    }

    /// <summary>
    /// A landmark layout of the entry <paramref name="entry"/>: the built structure unturned with its ground row and cavity,
    /// and — when the entry stands as a ruin (<see cref="LandmarkRules.IsRuin"/>) — the shared ruin pass with the entry's
    /// profile and strength and <paramref name="revision"/>, exactly as an authored landmark falls.
    /// </summary>
    public static LandmarkLayout ToLandmarkLayout(BuiltStructure b, StructureTemplate entry, long seed, GameContent content,
        string surfaceBlock, int revision)
    {
        if (b is null)
        {
            throw new ArgumentNullException(nameof(b));
        }

        var grid = new VoxelGrid(b.Width, b.Height, b.Length);
        Copy(b, 0, grid);
        foreach (int i in b.Cavity)
        {
            grid.Cavity[i] = true;
        }

        var markers = new List<SettlementMarker>();
        foreach (var m in b.Markers)
        {
            markers.Add(new SettlementMarker(m.Type, new Shared.Geometry.Vector3i(m.X, m.Y, m.Z), m.DoorAxis, m.Data));
        }

        return LandmarkGenerator.Finish(entry, grid, markers, b.GroundRow, LandmarkRules.IsRuin(entry), seed, content, surfaceBlock,
            revision, turns: 0);
    }

    /// <summary>Copies the built cells from row <paramref name="y0"/> up into the grid's row 0 up.</summary>
    private static void Copy(BuiltStructure b, int y0, VoxelGrid grid)
    {
        for (int x = 0; x < grid.W; x++)
        {
            for (int y = 0; y < grid.H; y++)
            {
                for (int z = 0; z < grid.L; z++)
                {
                    ushort block = b.Get(x, y + y0, z);
                    if (block == 0)
                    {
                        continue;
                    }

                    var (tint, glow) = b.GetModifier(x, y + y0, z);
                    grid.Set(x, y, z, block, tint, glow, b.GetShape(x, y + y0, z));
                }
            }
        }
    }
}
