// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The structure bookkeeping of #3513: every stamped kind writes a <see cref="StructurePlacementRecord"/>, and what the stampers
/// used to recompute on every load — the count per kind (<see cref="WorldMetadata.StructureCounts"/>), a settlement's tier and
/// ruin verdict, the planet wreck's ship design — is written down once and read back. The rule everywhere: the stamper still
/// makes every draw it made before (later draws of the same stream must not move), then a recorded value replaces the result.
/// A record from before a field existed gets the field on its next load, from the value computed then — the original one as
/// long as content and rules did not change, and frozen against any later change from then on.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The record kind of a standalone fallen town (<see cref="StampRuins"/>).</summary>
    internal const string RuinRecordKind = "ruin";

    /// <summary>The record kind of a rune monument (<see cref="StampMonuments"/>).</summary>
    internal const string MonumentRecordKind = "monument";

    /// <summary>The record kind of a stone text tablet (<see cref="StampTextMonuments"/>).</summary>
    internal const string TextTabletRecordKind = "text_tablet";

    /// <summary>The record kind of a scattered treasure chest (<see cref="StampChests"/>).</summary>
    internal const string ChestRecordKind = "chest";

    /// <summary>The record kind of a data cube (<see cref="StampDataCubes"/>).</summary>
    internal const string DataCubeRecordKind = "datacube";

    /// <summary>The record kind of the Guardian-core chamber (<see cref="StampGuardianCoreChamber"/>).</summary>
    internal const string GuardianCoreRecordKind = "guardian_core";

    /// <summary>
    /// The count a kind's roll gave on the active body (see <see cref="WorldMetadata.StructureCounts"/>): the pinned count when
    /// there is one, else <paramref name="rolled"/> — which is pinned now. The caller has drawn the roll already, so the stream
    /// after it is the same either way. Batched like the placement records (saved by <see cref="SavePlacementRecords"/> or the
    /// metadata save after the world load).
    /// </summary>
    private int PinnedCount(string kind, int rolled)
    {
        string key = _world.LocationId + "|" + kind;
        if (_meta.StructureCounts.TryGetValue(key, out int pinned))
        {
            return pinned;
        }

        _meta.StructureCounts[key] = rolled;
        _placementRecordsDirty = true;
        return rolled;
    }

    /// <summary>Writes a settlement's rolled tier and its ruin verdict into its record — once: a field that holds a value is
    /// never overwritten (#3513). A skip record or none at all is left alone.</summary>
    private void PinSettlementTraits(StructurePlacementRecord? rec, string tier, bool ruined)
    {
        if (rec is not { Placed: true })
        {
            return;
        }

        if (rec.Tier is null)
        {
            rec.Tier = tier;
            _placementRecordsDirty = true;
        }

        if (rec.Ruined is null)
        {
            rec.Ruined = ruined;
            _placementRecordsDirty = true;
        }
    }

    /// <summary>The template a FRESH settlement without a whole template is recorded with: a kit settlement keeps the empty
    /// template (its kit marks it), a procedural one on a generation-38 world says <see cref="StructurePlacementRecord.ProceduralTemplate"/>
    /// — before it, the empty template also meant "recorded before template pinning" and replayed through the legacy pool.</summary>
    private string FreshProceduralTemplate(StructureKit? kit)
        => kit is null && _generator.TerrainGeneration >= WorldDescription.StructurePipelineGeneration
            ? StructurePlacementRecord.ProceduralTemplate
            : string.Empty;

    /// <summary>The ship design of the planet wreck (#3513): the recorded one when its record names a design that still exists,
    /// else the one just drawn — written into the record when the record has none yet.</summary>
    private ShipDefinition PinnedWreckDesign(StructurePlacementRecord? rec, ShipDefinition rolled)
    {
        if (rec?.Design is { Length: > 0 } key)
        {
            if (_content.GetShip(key) is { } pinned)
            {
                return pinned;
            }

            _log.Warn($"The wreck on '{_world.LocationId}' was a '{key}', which is no longer in the content — it is rebuilt as a '{rolled.Key}'.");
        }

        return rolled;
    }

    /// <summary>Writes the planet wreck's design into its record once (#3513).</summary>
    private void PinWreckDesign(string designKey)
    {
        if (FindPlacementRecord("wreck", 0) is { Placed: true, Design: null } rec)
        {
            rec.Design = designKey;
            _placementRecordsDirty = true;
            SavePlacementRecords();
        }
    }

    /// <summary>Writes the planet wreck's hull extent into its record once (#3514, terrain generation 38+): the crash site later
    /// loads reserve before the wreck replays (<see cref="PlannedWreckSite"/>) is then the box that stands, not one sized by a
    /// fresh pick from a layout pool that may have changed since. A wreck without a structure (its layout is gone) records
    /// nothing.</summary>
    private void PinWreckBox(WreckStructure? structure)
    {
        if (UsesFootprintRegistry && structure is not null && FindPlacementRecord("wreck", 0) is { Placed: true, Width: 0 } rec)
        {
            rec.Width = structure.Width;
            rec.Length = structure.Length;
            _placementRecordsDirty = true;
            SavePlacementRecords();
        }
    }

    // ---------------------------------------------------------------- standalone ruins

    /// <summary>Records a standalone ruin where it landed (#3513) — with its tier and its footprint, so the keep-clear box the
    /// later stampers need is restored on every load (the ruins are stamped once and never re-derived).</summary>
    private void RecordRuin(int index, PlacedSettlement p)
    {
        RecordPlacement(RuinRecordKind, index, p.Origin, p.GroundY, p.OnIsland, p.Seat, p.Name);
        var rec = FindPlacementRecord(RuinRecordKind, index)!;
        rec.Tier = p.Tier;
        rec.Ruined = true;
        rec.Width = p.Structure.Width;
        rec.Length = p.Structure.Length;
    }

    /// <summary>The keep-clear box of a ruin as <see cref="WorldManager.LoadedWorld.RuinFootprints"/> holds it.</summary>
    private static (int Cx, int Cz, int Hw, int Hl) RuinFootprint(int originX, int originZ, int width, int length)
        => (originX + width / 2, originZ + length / 2, width / 2 + 1, length / 2 + 1);

    /// <summary>Rebuilds the active world's ruin footprints from the ruin records (#3513): before them the list existed only on
    /// the load that stamped the ruins, so every later load placed what came after the ruins as if they were not there.</summary>
    private void RestoreRuinFootprints()
    {
        string location = _world.LocationId;
        foreach (var rec in _meta.Placements)
        {
            if (rec.LocationId == location && rec.Kind == RuinRecordKind && rec.Placed && rec.Width > 0 && rec.Length > 0)
            {
                var footprint = RuinFootprint(rec.X, rec.Z, rec.Width, rec.Length);
                _worlds.Active.RuinFootprints.Add(footprint);
                RegisterFootprint(StructureTypeRules.Ruin, footprint); // #3514: the registry on every load, as on the first
            }
        }
    }

    // ---------------------------------------------------------------- monuments

    /// <summary>One monument as its record — or, on an older save, its stamped feature string — pins it.</summary>
    private readonly record struct MonumentPlacement(int Index, string Archetype, Vector3i Origin, bool Recorded);

    /// <summary>Records a monument where it landed (#3513): archetype as the template, the footprint as width and length.</summary>
    private void RecordMonument(int index, string archetype, Vector3i origin, int groundY, bool onIsland, string seat, SettlementStructure s)
    {
        RecordPlacement(MonumentRecordKind, index, origin, groundY, onIsland, seat, archetype, template: archetype);
        var rec = FindPlacementRecord(MonumentRecordKind, index)!;
        rec.Width = s.Width;
        rec.Length = s.Length;
    }

    /// <summary>
    /// The monuments this body stamped: every <c>monument@…</c> feature, read the way older builds read it, where a record of
    /// the same index replaces it (the record is preferred), plus any record without a feature. Empty when none were stamped.
    /// </summary>
    private List<MonumentPlacement> RecordedMonuments()
    {
        var found = new List<MonumentPlacement>();
        foreach (string feature in RecordedMonumentFeatures())
        {
            var parts = feature.Split(':');
            if (parts.Length != 5
                || !int.TryParse(parts[0], out int index)
                || !int.TryParse(parts[2], out int ox)
                || !int.TryParse(parts[3], out int gy)
                || !int.TryParse(parts[4], out int oz))
            {
                _log.Warn($"Ignoring malformed monument record '{feature}' on '{_world.LocationId}'.");
                continue;
            }

            found.Add(FindPlacementRecord(MonumentRecordKind, index) is { Placed: true, Template.Length: > 0 } rec
                ? new MonumentPlacement(index, rec.Template, new Vector3i(rec.X, rec.GroundY, rec.Z), Recorded: true)
                : new MonumentPlacement(index, parts[1], new Vector3i(ox, gy, oz), Recorded: false));
        }

        string location = _world.LocationId;
        foreach (var rec in _meta.Placements)
        {
            if (rec.LocationId == location && rec.Kind == MonumentRecordKind && rec is { Placed: true, Template.Length: > 0 }
                && !found.Exists(m => m.Index == rec.Index))
            {
                found.Add(new MonumentPlacement(rec.Index, rec.Template, new Vector3i(rec.X, rec.GroundY, rec.Z), Recorded: true));
            }
        }

        return found;
    }

    /// <summary>The body's <c>monument@…</c> stamped features, without the location and the prefix.</summary>
    private List<string> RecordedMonumentFeatures()
    {
        string prefix = _world.LocationId + "|" + MonumentRecordPrefix;
        var found = new List<string>();
        foreach (var f in _meta.StampedFeatures)
        {
            if (f.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                found.Add(f.Substring(prefix.Length));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- text tablets

    /// <summary>The record of a text tablet, found by its key (the template field) — the index is only its order.</summary>
    private StructurePlacementRecord? TextTabletRecord(string key)
        => _meta.Placements.Find(r => r.LocationId == _world.LocationId && r.Kind == TextTabletRecordKind && r.Template == key);

    /// <summary>The seat a text-tablet record pins, or null when the record lacks the facing.</summary>
    private static (int Fx, int Fz, Vector3i Anchor)? TextTabletSeat(StructurePlacementRecord? rec)
    {
        if (rec is not { Placed: true })
        {
            return null;
        }

        var pin = MarkerSettings.Parse(rec.KitLayout);
        int fx = pin.GetInt("fx", 0), fz = pin.GetInt("fz", 0);
        return (fx, fz) is (0, 0) ? null : (fx, fz, new Vector3i(rec.X, rec.GroundY, rec.Z));
    }

    /// <summary>Records a text tablet's seat (#3513), unless its key has a record already: the key as the template, the facing
    /// as <c>fx=…;fz=…</c>, the foot anchor as the origin. The index is the spec's order, or the next free one.</summary>
    private void RecordTextTablet(int order, string key, int pad, (int Fx, int Fz, Vector3i Anchor) seat)
    {
        if (TextTabletRecord(key) is not null)
        {
            return;
        }

        int index = FindPlacementRecord(TextTabletRecordKind, order) is null
            ? order
            : _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == TextTabletRecordKind).Max(r => r.Index) + 1;
        _meta.Placements.Add(new StructurePlacementRecord
        {
            LocationId = _world.LocationId,
            Kind = TextTabletRecordKind,
            Index = index,
            Placed = true,
            X = seat.Anchor.X,
            GroundY = seat.Anchor.Y,
            Z = seat.Anchor.Z,
            Seat = "pad",
            Name = key,
            Template = key,
            KitLayout = new MarkerSettings().Set("fx", seat.Fx).Set("fz", seat.Fz).Set("pad", pad).ToString(),
        });
        _placementRecordsDirty = true;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam (#3513): the pinned count of a kind on the active world, null when none is pinned.</summary>
    public int? StructureCountForTest(string kind)
        => _meta.StructureCounts.TryGetValue(_world.LocationId + "|" + kind, out int n) ? n : null;

    /// <summary>Test seam (#3513): the ship design the active world's wreck was built from, null without a wreck.</summary>
    public string? WreckDesignForTest => _worlds.Active.Wreck?.ShipType;

    /// <summary>Test seam (#3513): the active world's ruin footprints (centre x/z, half extents).</summary>
    public IReadOnlyList<(int Cx, int Cz, int Hw, int Hl)> RuinFootprintsForTest => _worlds.Active.RuinFootprints.ToList();
}
