// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// FALLEN GUARDIAN GIANTS (#3680, terrain generation 44) — the stamp: huge block-built hunter robots (<see cref="GuardianGiantBody"/>)
/// lying in the landscape, fallen long ago. The boss that rarely wakes is not here (see <see cref="GuardianGiantRules"/>); this
/// file only lays the bodies down and writes their records.
/// <list type="bullet">
/// <item><b>The gate.</b> The <c>guardian_giant</c> kind of <c>data/structure_types.json</c>: switched on, the world's terrain
/// generation at least its <c>minGeneration</c> (44), its conditions (not void, not gas, not an asteroid, not a city world), a
/// type with a structure whitelist only when it names <c>guardian_giants</c>; the host's <c>ServerConfig.PlaceGuardianGiants</c>;
/// never the finale body (its stage stamps nothing else); only a body untouched at load.</item>
/// <item><b>The roll.</b> <see cref="GuardianGiantSites.Count"/>: a first giant below the kind's chance (0.34;
/// <c>ServerConfig.GuardianGiantChance</c> overrides it) × the structures frequency, a second one below the kind's first
/// threshold (0.3), the kind's cap (2). Each giant's quarter turn, body seed, search seed and whether it can wake (about a
/// quarter) are hashes of their own lanes.</item>
/// <item><b>The seat.</b> The guaranteed search with the footprint registry (clear of pads, the crash site and the wreck,
/// towns, camps, huts, landmarks, giant wrecks, civilization sites and the other giant by the kind's <c>keepClearFrom</c>),
/// ranked flat-first, and only a <c>flat</c> seat (dry, the ground spread at most 4; never water, ice or lava). The seat
/// reserves its ground (grade 6): the bed under the body is level, no tree grows into it.</item>
/// <item><b>Pinned, written once.</b> One placement record per giant (kind <see cref="GuardianGiantRecordKind"/>, the index its
/// place in the roll; a giant without a seat records a skip) with the bed's corner, floor and size and, in its layout field,
/// <c>facing</c>, <c>seed</c>, <c>wake</c> and the anchor <c>ax</c>/<c>ay</c>/<c>az</c>; beside it the
/// <see cref="GuardianGiantRecord"/> the boss logic keeps (<see cref="WorldMetadata.GuardianGiants"/>, state
/// <see cref="GuardianGiantRecord.Dormant"/>, the corpse = the resting place). The body (<see cref="GuardianGiantBuilder"/>,
/// lights out) is written once as ordinary block edits — mineable, unprotected — and marked as a stamped feature.</item>
/// <item><b>Every later load</b> registers each giant's footprint from its record and writes nothing: the blocks are the
/// world's edits, the record's state is the boss logic's. Only a stamp that never finished (no feature mark) is written again;
/// a <see cref="GuardianGiantRecord"/> that went missing comes back dormant from the pin.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The placement-record kind of a guardian giant (one record per giant, the index its place in the body's roll).</summary>
    internal const string GuardianGiantRecordKind = GuardianGiantRules.StructureKind;

    /// <summary>Pin keys of a giant's placement record: its quarter turn, body seed, whether it can wake, its anchor.</summary>
    internal const string GuardianGiantFacingPin = "facing";

    /// <summary>See <see cref="GuardianGiantFacingPin"/>.</summary>
    internal const string GuardianGiantSeedPin = "seed";

    /// <summary>See <see cref="GuardianGiantFacingPin"/>.</summary>
    internal const string GuardianGiantWakePin = "wake";

    /// <summary>A giant seated for the first time: its place in the roll, the hashes, the anchor and the bed (corner at the
    /// floor row, size).</summary>
    private readonly record struct GuardianGiantSeat(int Index, int Facing, int Seed, bool CanWake, Vector3i Anchor, Vector3i Bed,
        int Width, int Length);

    /// <summary>The stamped-feature key of a giant's body (<see cref="FeatureStamped"/>).</summary>
    internal static string GuardianGiantFeature(int index) => GuardianGiantRecordKind + ":" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Test seam: the guardian giants of the active body, by index.</summary>
    internal IReadOnlyList<GuardianGiantRecord> StampedGuardianGiantsForTest
        => _meta.GuardianGiants.Values.Where(g => g.LocationId == _world.LocationId).OrderBy(g => g.Index).ToList();

    /// <summary>Whether the active body may roll guardian giants at all (the switches, the gate, the kind of body, the
    /// whitelist).</summary>
    private bool GuardianGiantsAllowed(PlanetType planet, bool restricted)
        => _config.PlaceGuardianGiants
           && StructureKindDue(StructureTypeRules.GuardianGiant)
           && StructureConditionsHold(StructureTypeRules.GuardianGiant, planet)
           && StructureKindAllowed(StructureTypeRules.GuardianGiant, planet, restricted);

    /// <summary>The chance of a first giant on the active body: the kind's (or the host's) × the structures frequency.</summary>
    private double GuardianGiantOdds()
    {
        double factor = System.Math.Clamp(_meta.Description.Settlements.StructureFactor(), 0.0, 2.0);
        return StructureChance(StructureTypeRules.GuardianGiant, _config.GuardianGiantChance) * factor;
    }

    /// <summary>The boot stamp (one line right after the giant wrecks): replay every record of the body, or — on a body untouched
    /// at load that may carry them — roll, seat and lay the giants down. A record is replayed whatever the switches say now: a
    /// giant a world already has stays.</summary>
    private void StampGuardianGiants(bool restricted)
    {
        var loaded = _worlds.Active;
        if (loaded.GuardianGiantsStamped)
        {
            return;
        }

        loaded.GuardianGiantsStamped = true;
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        var records = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == GuardianGiantRecordKind)
            .OrderBy(r => r.Index).ToList();
        if (records.Count > 0)
        {
            foreach (var rec in records.Where(r => r.Placed))
            {
                ReplayGuardianGiant(rec);
            }

            SavePlacementRecords();
            return;
        }

        if (!GuardianGiantsAllowed(planet, restricted))
        {
            return; // nothing decided: a world that switches them on later asks again (and a visited body then records a skip)
        }

        LayGuardianGiants(loaded.VirginAtLoad);
    }

    /// <summary>A fresh decision on the active body: the count, then each giant seated and committed (a giant without a seat
    /// records a skip). A body that had edits at load decides "none" once.</summary>
    private void LayGuardianGiants(bool virgin)
    {
        var rules = StructureType(StructureTypeRules.GuardianGiant);
        int count = virgin ? GuardianGiantSites.Count(_meta.Seed, _world.LocationId, GuardianGiantOdds(), rules.Thresholds, rules.Cap) : 0;
        if (count == 0)
        {
            RecordPlacementSkip(GuardianGiantRecordKind, 0); // decided once: none here (no roll, or a visited body)
            SavePlacementRecords();
            return;
        }

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            if (SeatGuardianGiant(i) is { } seat)
            {
                CommitGuardianGiant(seat);
                placed++;
            }
            else
            {
                RecordPlacementSkip(GuardianGiantRecordKind, i);
            }
        }

        SavePlacementRecords();
        ReportStamp(GuardianGiantRecordKind, count, placed);
    }

    /// <summary>Seats the body's <paramref name="index"/>-th giant: its quarter turn sizes the bed, the guaranteed search finds
    /// dry, flat ground clear of everything stamped before it. Null when no flat seat is reachable.</summary>
    private GuardianGiantSeat? SeatGuardianGiant(int index)
    {
        string body = _world.LocationId;
        int facing = GuardianGiantSites.Facing(_meta.Seed, body, index);
        var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(facing);
        int width = maxX - minX + 1, length = maxZ - minZ + 1;
        var box = SettlementStructure.Empty(width, GuardianGiantBody.LyingTop - GuardianGiantBody.LyingBottom + 1, length,
            GuardianGiantRecordKind);
        var policy = new SeatPolicy(AllowStilts: false, AllowLava: false, PreferRugged: false);
        var rng = RngFor(GuardianGiantSites.SearchSeed(_meta.Seed, body, index), "search");
        var reserved = KeepClearFor(StructureTypeRules.GuardianGiant, new List<(int Cx, int Cz, int Hw, int Hl)>()); // generation 44: the registry
        if (!TryPlaceStructureGuaranteed(box, rng, reserved, wantIsland: false, policy, avoidPlayerEdits: false, out var origin,
                out int groundY, out _, out _, seatOk: (_, seat) => seat == "flat", siteKind: StructureTypeRules.GuardianGiant))
        {
            return null;
        }

        var anchor = new Vector3i(WorldConstants.WrapX(origin.X - minX, _world.Circumference), groundY + GuardianGiantBuilder.AnchorAboveGround,
            origin.Z - minZ);
        return new GuardianGiantSeat(index, facing, GuardianGiantSites.BodySeed(_meta.Seed, body, index),
            GuardianGiantSites.CanWake(_meta.Seed, body, index), anchor, new Vector3i(origin.X, groundY, origin.Z), width, length);
    }

    /// <summary>Pins a fresh giant (its placement record and its <see cref="GuardianGiantRecord"/>), reserves its bed, registers
    /// its footprint and writes its body once.</summary>
    private void CommitGuardianGiant(GuardianGiantSeat s)
    {
        string body = _world.LocationId;
        string pin = new MarkerSettings()
            .Set(GuardianGiantFacingPin, s.Facing)
            .Set(GuardianGiantSeedPin, s.Seed)
            .Set(GuardianGiantWakePin, s.CanWake ? 1 : 0)
            .Set("ax", s.Anchor.X)
            .Set("ay", s.Anchor.Y)
            .Set("az", s.Anchor.Z)
            .ToString();
        RecordPlacement(GuardianGiantRecordKind, s.Index, s.Bed, s.Bed.Y, onIsland: false, "flat", string.Empty, kitLayout: pin);
        var rec = FindPlacementRecord(GuardianGiantRecordKind, s.Index)!;
        rec.Width = s.Width;
        rec.Length = s.Length;
        _meta.GuardianGiants[GuardianGiantRecord.Key(body, s.Index)] = NewGuardianGiantRecord(s.Index, s.Anchor, s.Facing, s.Seed, s.CanWake);
        ReserveSiteGround(StructureTypeRules.GuardianGiant, s.Width, s.Length, s.Bed, s.Bed.Y, "flat"); // #3569: a level bed, no tree in it
        SavePlacementRecords(); // the records and the reserved bed, before a block is written
        RegisterFootprint(StructureTypeRules.GuardianGiant, s.Bed.X, s.Bed.Z, s.Width, s.Length, GuardianGiantRecord.Key(body, s.Index));
        WriteGuardianGiantOnce(s.Index, s.Anchor, s.Seed, s.Facing, s.Bed, s.Width, s.Length);
    }

    /// <summary>A later load: the giant's footprint from its placement record; its <see cref="GuardianGiantRecord"/> back as
    /// dormant only when it went missing (its state is never touched); its body written only when the first stamp never
    /// finished.</summary>
    private void ReplayGuardianGiant(StructurePlacementRecord rec)
    {
        string body = _world.LocationId;
        var pin = MarkerSettings.Parse(rec.KitLayout);
        int facing = pin.GetInt(GuardianGiantFacingPin, GuardianGiantSites.Facing(_meta.Seed, body, rec.Index));
        int seed = pin.GetInt(GuardianGiantSeedPin, GuardianGiantSites.BodySeed(_meta.Seed, body, rec.Index));
        bool canWake = pin.GetBool(GuardianGiantWakePin, GuardianGiantSites.CanWake(_meta.Seed, body, rec.Index));
        var anchor = new Vector3i(pin.GetInt("ax", rec.X), pin.GetInt("ay", rec.GroundY + GuardianGiantBuilder.AnchorAboveGround),
            pin.GetInt("az", rec.Z));
        string key = GuardianGiantRecord.Key(body, rec.Index);
        if (!_meta.GuardianGiants.ContainsKey(key))
        {
            _log.Warn($"Guardian giant '{key}' had a placement record but no giant record — it is restored dormant.");
            _meta.GuardianGiants[key] = NewGuardianGiantRecord(rec.Index, anchor, facing, seed, canWake);
            _placementRecordsDirty = true;
        }

        RegisterFootprint(StructureTypeRules.GuardianGiant, rec.X, rec.Z, rec.Width, rec.Length, key);
        WriteGuardianGiantOnce(rec.Index, anchor, seed, facing, new Vector3i(rec.X, rec.GroundY, rec.Z), rec.Width, rec.Length);
    }

    /// <summary>A giant's record as the stamp leaves it: dormant, lying where it fell, the corpse its resting place.</summary>
    private GuardianGiantRecord NewGuardianGiantRecord(int index, Vector3i anchor, int facing, int seed, bool canWake) => new()
    {
        LocationId = _world.LocationId,
        Index = index,
        X = anchor.X,
        Y = anchor.Y,
        Z = anchor.Z,
        Facing = facing,
        Seed = seed,
        CanWake = canWake,
        State = GuardianGiantRecord.Dormant,
        CorpseX = anchor.X,
        CorpseY = anchor.Y,
        CorpseZ = anchor.Z,
        CorpseFacing = facing,
    };

    /// <summary>Writes a giant's lying body (lights out) as ordinary block edits — once: a body marked as stamped is never
    /// written again, so what players mine stays mined and the boss logic owns the cells from then on.</summary>
    private void WriteGuardianGiantOnce(int index, Vector3i anchor, int seed, int facing, Vector3i bed, int width, int length)
    {
        string feature = GuardianGiantFeature(index);
        if (FeatureStamped(feature))
        {
            return;
        }

        var (surface, _) = FoundationGround(bed, width, length, LandmarkSurfaceBlock());
        var sink = new WorldCellSink(_world);
        int cells = 0;
        _repo.RunInTransaction(() => cells = GuardianGiantBuilder.Build(sink, _content, surface, anchor.X, anchor.Y, anchor.Z, seed, facing, lit: false));
        MarkFeatureStamped(feature); // after the commit: a crash mid-stamp writes again, never half-marks
        _log.Info($"Laid guardian giant '{GuardianGiantRecord.Key(_world.LocationId, index)}' down at {anchor.X},{anchor.Y},{anchor.Z} " +
                  $"(facing {facing}): {cells} cells.");
    }
}
