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
/// GIANT SHIP WRECKS ON PLANETS (#3610, terrain generation 42): about every twentieth planet carries the wreck of a huge
/// authored spaceship (<c>data/giant_ships/*.json</c>, <see cref="GiantShipRules"/>), crashed long ago and abandoned —
/// everyone got away. It is stamped with the landmark machinery (GameServerLandmark*.cs), from its own list and records:
/// <list type="bullet">
/// <item><b>The gate.</b> The <c>giant_wreck</c> kind of <c>data/structure_types.json</c>: switched on, the world's terrain
/// generation at least its <c>minGeneration</c> (42), its conditions (not void, not gas — an airless moon or an asteroid may
/// carry one), a type with a structure whitelist only when it names <c>giant_wrecks</c> (and then only ships that name the
/// type); the host's <c>ServerConfig.PlaceGiantWrecks</c>; never the finale body; only a body untouched at load.</item>
/// <item><b>The roll.</b> A hash of seed and body below the kind's chance (<c>ServerConfig.GiantWreckChance</c> overrides
/// it) × the structures frequency × the world option (<see cref="WorldDescription.GiantWrecks"/>, Normal = 1, Off = none).
/// Then one of the ships the body is several footprints around (the landmark rule) by weight, a quarter turn and the
/// instance seed — each a hash of its own lane; a pick without a seat gives way to another one (up to four tries).</item>
/// <item><b>The seat.</b> The guaranteed search with the footprint registry (kind <c>giant_wreck</c>: clear of the pads,
/// the crash site, the settlements, the landmark, …): dry ground, or partly in shallow water (every sampled column's
/// ground at most <see cref="GiantWreckShallowWater"/> under the hull) — never in lava.</item>
/// <item><b>Pinned, stamped once.</b> One placement record (<see cref="GiantShipRules.RecordKind"/>, index 0; an admin's
/// hand stamp takes the next index) with the template key, the seat and, in its layout field, <c>turns</c>, <c>rev</c>
/// (ruin revision 3), <c>seed</c>, <c>air</c>, <c>fp</c> (<see cref="GiantShipGenerator.Fingerprint"/>) and
/// <c>storage=overlay</c>. The cells (<see cref="GiantShipGenerator.BuildPlanet"/>, profile <c>crash</c>) go once through the
/// landmark's cell writer into ONE structure overlay (key <see cref="GiantShipRules.FeatureKey"/>) — never a block edit per
/// cell, whatever the world's generation. A body that says no records a skip.</item>
/// <item><b>Every later load</b> re-derives the layout from the record for its markers (the chests, once ever: the landmark
/// chest path with a lane and a container prefix of their own, <see cref="GiantWreckChestContainerPrefix"/>; the sound
/// emitters), the far silhouette and the streaming lift. A record whose template changed since (another fingerprint or ruin revision)
/// or left the content keeps its blocks and re-derives nothing (warned).</item>
/// </list>
/// The world parts — discovery, the client's list — are in GameServerGiantWrecksWorld.cs, the admin's command in
/// GameServerGiantWrecksAdmin.cs.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The pin key of the template fingerprint a giant wreck was built from.</summary>
    internal const string GiantWreckFingerprintPin = "fp";

    /// <summary>The pin key of whether the body had air when the wreck was built (plants, water or ice).</summary>
    internal const string GiantWreckAirPin = "air";

    /// <summary>How far a giant wreck lying partly in water may reach down to the ground under it (blocks below its ground
    /// row) — shallow water only.</summary>
    internal const int GiantWreckShallowWater = 4;

    /// <summary>Container id prefix of a giant wreck's chest (the landmark chest path, its own prefix).</summary>
    internal const string GiantWreckChestContainerPrefix = "loot_giantwreck_chest_";

    /// <summary>The lane of a giant wreck chest's existence and content hashes, and its <c>GeneratedLoot</c> key prefix.</summary>
    private const string GiantWreckChestLane = "giantwreck";

    internal sealed partial class LandmarkInstance
    {
        /// <summary>Whether this is a giant ship wreck (#3610) rather than a landmark: it lives in its own list and has its own
        /// map type, VEGA line, scan readout and Codex group.</summary>
        public bool GiantWreck { get; init; }
    }

    private List<LandmarkInstance> _giantWrecks => _worlds.Active.GiantWrecks;

    /// <summary>Test seam: the giant wrecks of the active world.</summary>
    internal IReadOnlyList<LandmarkInstance> GiantWrecksForTest => _giantWrecks;

    /// <summary>Whether body <paramref name="locationId"/> of the world <paramref name="worldSeed"/> rolls a giant wreck.</summary>
    internal static bool GiantWreckRolls(long worldSeed, string locationId, double chance)
        => LaneRoll(worldSeed, "giantwreck:roll:" + locationId) < System.Math.Clamp(chance, 0.0, 1.0);

    /// <summary>The chance of the active body: the kind's (or the host's) × the structures frequency × the world option.</summary>
    private double GiantWreckOdds()
    {
        double factor = System.Math.Clamp(_meta.Description.Settlements.StructureFactor(), 0.0, 2.0);
        double option = _meta.Description.GiantWrecks.Probability() / Frequency.Normal.Probability();
        return StructureChance(StructureTypeRules.GiantWreck, _config.GiantWreckChance) * factor * option;
    }

    /// <summary>Whether the active body may roll a giant wreck at all (the switches, the gate, the kind of body, the
    /// whitelist, never the finale body).</summary>
    private bool GiantWrecksAllowed(PlanetType planet, bool restricted)
        => _config.PlaceGiantWrecks && _meta.Description.GiantWrecks != Frequency.Off && _world.LocationId != GuardianCoreBodyId
           && StructureType(StructureTypeRules.GiantWreck) is { Enabled: true } rules
           && _generator.TerrainGeneration >= rules.MinGeneration
           && StructureConditionsHold(StructureTypeRules.GiantWreck, planet) // not void, not gas, as shipped
           && StructureKindAllowed(StructureTypeRules.GiantWreck, planet, restricted);

    /// <summary>The boot stamp (one line right after the landmarks): replay every record of the body, or — on a body untouched
    /// at load that may carry one — roll, pick, seat and stamp. A record is replayed whatever the switches say now: a wreck a
    /// world already has stays.</summary>
    private void StampGiantWrecks(bool restricted)
    {
        var loaded = _worlds.Active;
        if (loaded.GiantWrecksStamped)
        {
            return;
        }

        loaded.GiantWrecksStamped = true;
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        // Index 0 is the rolled wreck, higher ones an admin stamped by hand.
        var records = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == GiantShipRules.RecordKind)
            .OrderBy(r => r.Index).ToList();
        if (records.Count > 0)
        {
            foreach (var rec in records)
            {
                if (rec.Placed && GiantWreckPlanFromRecord(rec) is { } pinned)
                {
                    CommitGiantWreck(pinned);
                }
            }

            return;
        }

        if (!GiantWrecksAllowed(planet, restricted))
        {
            return; // nothing decided: a world that switches them on later asks again (and a visited body then records a skip)
        }

        var plan = loaded.VirginAtLoad && !CivilizationSuppresses(StructureTypeRules.GiantWreck) ? PlanGiantWreck(planet, restricted) : null; // #3639
        if (plan is null)
        {
            RecordPlacementSkip(GiantShipRules.RecordKind, 0); // decided once: none here (no roll, no fit, or a visited body)
            SavePlacementRecords();
            return;
        }

        CommitGiantWreck(plan);
    }

    /// <summary>A fresh plan: the roll, the weighted pick among the ships that fit the body, the turn and the seat. Null when
    /// the body carries none.</summary>
    private LandmarkPlan? PlanGiantWreck(PlanetType planet, bool restricted)
    {
        string body = _world.LocationId;
        if (!GiantWreckRolls(_meta.Seed, body, GiantWreckOdds()))
        {
            return null;
        }

        var candidates = _content.GiantShipsFor(_meta.Description.EnabledStructurePacks, planet.Key, namedTypeOnly: restricted,
                generation: _generator.TerrainGeneration)
            .Where(t => LandmarkFitsBody(t, _world.Circumference)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        for (int attempt = 0; attempt < LandmarkPickAttempts && candidates.Count > 0; attempt++)
        {
            string lane = attempt == 0 ? body : body + ":" + attempt.ToString(CultureInfo.InvariantCulture);
            var template = PickLandmark(candidates, "giantwreck:pick:" + lane);
            var plan = new LandmarkPlan
            {
                Template = template,
                Seed = unchecked((long)StableHashing.Mixed("giantwreck:seed:" + body, _meta.Seed)),
                Turns = StableHashing.Index("giantwreck:turn:" + body, _meta.Seed, 4),
                Revision = GiantShipGenerator.RuinRevision,
                Fresh = true,
            };
            plan.Layout = BuildGiantWreckLayout(plan, !planet.IsAirless);
            if (TrySeatGiantWreck(plan))
            {
                return plan;
            }

            candidates.Remove(template); // no seat for this one here: another ship may lie down where it could not
        }

        ReportStamp(GiantShipRules.RecordKind, 1, 0);
        return null;
    }

    /// <summary>The wreck of the plan's ship lying on the active body: ruin revision 3, profile <c>crash</c>; plants only with
    /// air, water frozen without it; from terrain generation 45 its beds are the abandoned twin (no home, no rest, no settler).</summary>
    private LandmarkLayout BuildGiantWreckLayout(LandmarkPlan plan, bool atmosphere)
        => GiantShipGenerator.BuildPlanet(plan.Template, plan.Seed, plan.Turns, _content, LandmarkSurfaceBlock(), atmosphere,
            abandonBeds: AbandonedBeds.Applies(_generator.TerrainGeneration));

    /// <summary>Seats the planned wreck with the guaranteed search (#586) and the footprint registry: dry ground of any
    /// relief the site grade can level, or partly in shallow water; never lava.</summary>
    private bool TrySeatGiantWreck(LandmarkPlan plan)
    {
        var s = plan.Layout!.Structure;
        var policy = new SeatPolicy(AllowStilts: false, AllowLava: false, PreferRugged: false);
        bool SeatOk(Vector3i origin, string seat) => seat switch
        {
            "flat" or "slope" or "shelf" => true,
            "stilts" => GiantWreckWaterShallow(origin, s.Width, s.Length),
            _ => false,
        };

        if (!TryPlaceStructureGuaranteed(s, RngFor(plan.Seed, "search"), KeepClearFor(StructureTypeRules.GiantWreck, LandmarkReserved()),
                wantIsland: false, policy, avoidPlayerEdits: false, out var origin, out int groundY, out _, out string seatStyle,
                seatOk: SeatOk, siteKind: StructureTypeRules.GiantWreck))
        {
            return false;
        }

        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (origin.X, origin.Z, groundY, seatStyle);
        return true;
    }

    /// <summary>Whether a seat over water is shallow enough for a wreck: the ground under every sampled column of the
    /// footprint lies at most <see cref="GiantWreckShallowWater"/> below the hull's ground row.</summary>
    private bool GiantWreckWaterShallow(Vector3i origin, int width, int length)
    {
        var planet = _world.Planet;
        foreach (var (x, z) in FootprintSamples(origin.X, origin.Z, width, length))
        {
            if (_generator.SurfaceHeight(planet, x, z) < origin.Y - 1 - GiantWreckShallowWater)
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- the record

    private string GiantWreckPin(LandmarkPlan plan, bool atmosphere)
        => new MarkerSettings()
            .Set("turns", plan.Turns)
            .Set("rev", plan.Revision)
            .Set("seed", plan.Seed.ToString(CultureInfo.InvariantCulture))
            .Set(GiantWreckAirPin, atmosphere ? 1 : 0)
            .Set(GiantWreckFingerprintPin, GiantShipGenerator.Fingerprint(plan.Template))
            .Set(LandmarkStoragePin, LandmarkOverlayStorage)
            .ToString();

    /// <summary>The plan a placed record pins — null (warned) when its ship left the content, or changed since it was stamped
    /// (another fingerprint or ruin revision): its blocks stay in the overlay, nothing else is re-derived.</summary>
    private LandmarkPlan? GiantWreckPlanFromRecord(StructurePlacementRecord rec)
    {
        var template = _content.GetGiantShip(rec.Template);
        if (template is null)
        {
            _log.Warn($"Giant wreck '{rec.Template}' on '{_world.LocationId}' is no longer in the content — its blocks stay, nothing else is restored.");
            return null;
        }

        var pin = MarkerSettings.Parse(rec.KitLayout);
        int revision = pin.GetInt("rev", GiantShipGenerator.RuinRevision);
        string pinned = pin.Get(GiantWreckFingerprintPin);
        if (revision != GiantShipGenerator.RuinRevision || (pinned.Length > 0 && pinned != GiantShipGenerator.Fingerprint(template)))
        {
            _log.Warn($"Giant wreck '{rec.Template}' on '{_world.LocationId}' was stamped from another version of its ship (ruin revision {revision}, " +
                      $"fingerprint '{pinned}') — its blocks stay, nothing else is restored.");
            return null;
        }

        long seed = long.TryParse(pin.Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)
            ? s
            : unchecked((long)StableHashing.Mixed("giantwreck:seed:" + _world.LocationId, _meta.Seed));
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = seed,
            Turns = pin.GetInt("turns", 0),
            Revision = revision,
            OriginX = rec.X,
            OriginZ = rec.Z,
            GroundY = rec.GroundY,
            Seat = rec.Seat,
            Index = rec.Index,
        };
        plan.Layout = BuildGiantWreckLayout(plan, pin.GetInt(GiantWreckAirPin, _world.Planet.IsAirless ? 0 : 1) != 0);
        return plan;
    }

    /// <summary>Pins a fresh wreck, stamps its cells once as its structure overlay, then builds the runtime instance and what
    /// its markers become (the landmark's chests and emitters). A wreck is open: what players mine stays mined.</summary>
    private void CommitGiantWreck(LandmarkPlan plan)
    {
        var layout = plan.Layout!;
        string feature = GiantShipRules.FeatureKey(plan.Index);
        var origin = new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ);
        if (plan.Fresh)
        {
            RecordPlacement(GiantShipRules.RecordKind, plan.Index, origin, plan.GroundY, false, plan.Seat, plan.Template.Name,
                plan.Template.Key, kitLayout: GiantWreckPin(plan, !_world.Planet.IsAirless), hashRevision: StructurePlacementRecord.MixedHashRevision);
            SavePlacementRecords();
            // #3571: a fresh wreck of the boot's structures stage reserves its ground (an admin's stamp does not)
            ReserveSiteGround(StructureTypeRules.GiantWreck, layout.Structure.Width, layout.Structure.Length, origin, plan.GroundY, plan.Seat);
        }

        var instance = new LandmarkInstance
        {
            Template = plan.Template,
            Layout = layout,
            Origin = new Vector3i(plan.OriginX, plan.GroundY - layout.GroundRow, plan.OriginZ),
            GroundY = plan.GroundY,
            Seed = plan.Seed,
            GiantWreck = true,
            OverlayResolved = true,
            OverlayKey = feature, // always one structure overlay — never a block edit per cell, on any generation
        };

        if (!FeatureStamped(feature))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cells = 0;
            _repo.RunInTransaction(() => cells = WriteLandmark(instance, plan.Seat));
            MarkFeatureStamped(feature); // after the commit: a crash mid-stamp re-stamps, never half-marks
            _log.Info($"Stamped giant wreck '{instance.Key}' ({layout.Structure.Width}×{layout.Structure.Height}×{layout.Structure.Length}, " +
                      $"seat {plan.Seat}) on '{_world.LocationId}': {cells} cells in {clock.ElapsedMilliseconds} ms.");
        }

        _giantWrecks.Add(instance);
        RegisterFootprint(StructureTypeRules.GiantWreck, instance.Rect);
        MaterializeLandmark(instance); // the chests (once ever), the loot markers, the sound emitters, the reveal key
        if (plan.Fresh)
        {
            ReportStamp(GiantShipRules.RecordKind, 1, 1);
        }
    }

    /// <summary>The giant wreck whose box holds the cell, or null.</summary>
    private LandmarkInstance? GiantWreckAt(Vector3i cell)
    {
        foreach (var w in _giantWrecks)
        {
            int dx = WorldConstants.WrapDeltaX(cell.X - w.Origin.X, _world.Circumference);
            if (dx >= 0 && dx < w.Structure.Width && cell.Y >= w.Origin.Y && cell.Y < w.Max.Y && cell.Z >= w.Origin.Z && cell.Z < w.Max.Z)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>Whether a point (or a small area around it) lies in a giant wreck's footprint — part of
    /// <see cref="OverlapsLandmark"/>, so every check that keeps clear of the landmark keeps clear of the wrecks too.</summary>
    private bool OverlapsGiantWreck(int x, int z, int halfExtent)
    {
        foreach (var w in _giantWrecks)
        {
            var (cx, cz, hw, hl) = w.Rect;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - cx, _world.Circumference));
            if (dx < hw + halfExtent + SettlementCollisionMargin && System.Math.Abs(z - cz) < hl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }
}
