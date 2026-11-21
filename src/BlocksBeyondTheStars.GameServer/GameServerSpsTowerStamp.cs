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
/// THE GUIDE TOWERS' STAMP (#3893, terrain generation 47): the ruin of an SPS guide tower on a body with an inhabited
/// settlement — rare, huge, at most one per body, and at least one per save. It is stamped with the landmark machinery
/// (GameServerLandmark*.cs), from its own pool, list and records, like the giant ship wrecks:
/// <list type="bullet">
/// <item><b>The gate.</b> The <c>sps_tower</c> kind of <c>data/structure_types.json</c>: switched on, the world's terrain
/// generation at least its <c>minGeneration</c> (47), its conditions (air; not void, gas or an asteroid), a type with a
/// structure whitelist only when it names <c>sps_towers</c>; the host's <c>ServerConfig.PlaceSpsTowers</c> and the world option
/// <see cref="WorldDescription.SpsTowers"/>; never the finale body, never a civilization's home; only a body untouched at
/// load that carries an inhabited settlement (the settlements are stamped before).</item>
/// <item><b>The roll and the guarantee.</b> A hash of seed and body below the kind's chance (<c>ServerConfig.SpsTowerChance</c>
/// overrides it) × the structures frequency × the world option. While the save has not placed its guaranteed tower
/// (<see cref="WorldMetadata.SpsTowerGuaranteeSpent"/>) the roll is skipped: the first eligible body gets one for sure —
/// unless the chance is 0. A tower that finds no seat leaves the guarantee for the next body. The guaranteed tower always
/// keeps its guard post (#3897: the sleeper's tank); every other tower rolls it by its seed.</item>
/// <item><b>The seat.</b> The guaranteed search with the footprint registry (60 from a settlement's footprint, 24 from the
/// pads, clear of the landmark, the wrecks, the giants …) on dry ground — and only where the tower's top stays
/// <see cref="SpsTowerRules.AtmosphereMargin"/> rows below the body's atmosphere line. A template without a seat gives way
/// to another one.</item>
/// <item><b>Pinned, stamped once.</b> One placement record (<see cref="SpsTowerRules.RecordKind"/>, index 0; an admin's hand
/// stamp takes the next index) with the template key, the seat and, in its layout field, <c>turns</c>, <c>rev</c>,
/// <c>seed</c>, <c>air</c>, <c>post</c> (the guard post, 0/1), <c>fp</c> and <c>storage=overlay</c>. The cells
/// (<see cref="SpsTowerGenerator.Build"/>) go once through the landmark's cell writer into ONE structure overlay (key
/// <see cref="SpsTowerRules.FeatureKey"/>). Its box is protected: nothing of a tower can be mined, its chests open.</item>
/// <item><b>The tower's record.</b> Beside the placement record a <see cref="SpsTowerRecord"/> in
/// <see cref="WorldMetadata.SpsTowers"/> — the console's cell, the lamp cells, the guard post and its tank — written once,
/// never rewritten: the tower's life keeps its state there.</item>
/// <item><b>Every later load</b> re-derives the layout from the placement record for its chests, the far silhouette and the
/// streaming lift. A record whose template changed since or left the content keeps its blocks and its tower record and
/// re-derives nothing else (warned).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The pin key of whether the tower was built with its guard post (0/1).</summary>
    internal const string SpsTowerPostPin = "post";

    private sealed partial class LandmarkPlan
    {
        /// <summary>#3893: whether a guide tower is built with its guard post.</summary>
        public bool GuardPost;

        /// <summary>#3893: whether a guide tower came from the body's roll (it spends the save's guarantee) rather than an
        /// admin's hand.</summary>
        public bool Rolled;
    }

    /// <summary>Whether body <paramref name="locationId"/> of the world <paramref name="worldSeed"/> rolls a guide tower.</summary>
    internal static bool SpsTowerRolls(long worldSeed, string locationId, double chance)
        => LaneRoll(worldSeed, "spstower:roll:" + locationId) < System.Math.Clamp(chance, 0.0, 1.0);

    /// <summary>The chance of the active body: the kind's (or the host's) × the structures frequency × the world option.</summary>
    private double SpsTowerOdds()
    {
        double factor = System.Math.Clamp(_meta.Description.Settlements.StructureFactor(), 0.0, 2.0);
        double option = _meta.Description.SpsTowers.Probability() / Frequency.Normal.Probability();
        return StructureChance(StructureTypeRules.SpsTower, _config.SpsTowerChance) * factor * option;
    }

    /// <summary>Whether the active body may decide about a guide tower at all (the switches, the gate, the kind of body, the
    /// whitelist, never the finale body).</summary>
    private bool SpsTowersAllowed(PlanetType planet, bool restricted)
        => _config.PlaceSpsTowers && _meta.Description.SpsTowers != Frequency.Off && _world.LocationId != GuardianCoreBodyId
           && StructureType(StructureTypeRules.SpsTower) is { Enabled: true } rules
           && _generator.TerrainGeneration >= rules.MinGeneration
           && StructureConditionsHold(StructureTypeRules.SpsTower, planet) // air; not void, gas or an asteroid, as shipped
           && StructureKindAllowed(StructureTypeRules.SpsTower, planet, restricted);

    /// <summary>The boot stamp (one line right after the guardian giants): replay every record of the body, or — on a body
    /// untouched at load that may carry one — roll (or take the save's guarantee), pick, seat and stamp. A record is replayed
    /// whatever the switches say now: a tower a world already has stays.</summary>
    private void StampSpsTowers(bool restricted)
    {
        var loaded = _worlds.Active;
        if (loaded.SpsTowersStamped)
        {
            return;
        }

        loaded.SpsTowersStamped = true;
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        var records = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == SpsTowerRules.RecordKind)
            .OrderBy(r => r.Index).ToList();
        if (records.Count > 0)
        {
            foreach (var rec in records)
            {
                if (rec.Placed && SpsTowerPlanFromRecord(rec) is { } pinned)
                {
                    CommitSpsTower(pinned);
                }
            }

            return;
        }

        if (!SpsTowersAllowed(planet, restricted))
        {
            return; // nothing decided: a world that switches them on later asks again (and a visited body then records a skip)
        }

        // A tower stands only where people live: the body carries an inhabited settlement, and no lost civilization is at home.
        bool eligible = loaded.VirginAtLoad && InhabitedSettlementCount > 0 && HomeCivilizations().Count == 0;
        var plan = eligible ? PlanSpsTower(planet, restricted) : null;
        if (plan is null)
        {
            RecordPlacementSkip(SpsTowerRules.RecordKind, 0); // decided once: none here (no roll, no seat, nobody home, or a visited body)
            SavePlacementRecords();
            return;
        }

        CommitSpsTower(plan);
    }

    /// <summary>A fresh plan: the guarantee or the roll, the weighted pick among the templates that fit the body, the turn,
    /// the guard post and the seat. Null when the body carries none.</summary>
    private LandmarkPlan? PlanSpsTower(PlanetType planet, bool restricted)
    {
        string body = _world.LocationId;
        double odds = SpsTowerOdds();
        bool guaranteed = !_meta.SpsTowerGuaranteeSpent && odds > 0; // the save's first eligible body carries one for sure
        if (!guaranteed && !SpsTowerRolls(_meta.Seed, body, odds))
        {
            return null;
        }

        var candidates = _content.SpsTowersFor(_meta.Description.EnabledStructurePacks, planet.Key, namedTypeOnly: restricted,
                generation: _generator.TerrainGeneration)
            .Where(t => LandmarkFitsBody(t, _world.Circumference)).ToList();
        for (int attempt = 0; attempt < LandmarkPickAttempts && candidates.Count > 0; attempt++)
        {
            string lane = attempt == 0 ? body : body + ":" + attempt.ToString(CultureInfo.InvariantCulture);
            var template = PickLandmark(candidates, "spstower:pick:" + lane);
            long seed = unchecked((long)StableHashing.Mixed("spstower:seed:" + body, _meta.Seed));
            var plan = new LandmarkPlan
            {
                Template = template,
                Seed = seed,
                Turns = StableHashing.Index("spstower:turn:" + body, _meta.Seed, 4),
                Revision = SpsTowerGenerator.RuinRevision,
                // #3897: the save's guaranteed tower always keeps its guard post (the mission "The Sleeper" needs a tank in
                // reach); every other tower rolls it by its seed as before. Pinned in the record (post=1) like any other.
                GuardPost = (guaranteed && SpsTowerRules.GuardPostBox(template) is not null) || SpsTowerGenerator.RollsGuardPost(template, seed),
                Rolled = true,
                Fresh = true,
            };
            plan.Layout = BuildSpsTowerLayout(plan, !planet.IsAirless);
            if (TrySeatSpsTower(plan))
            {
                return plan;
            }

            candidates.Remove(template); // no seat for this one here (too tall for this sky, no room): a lower ruin may fit
        }

        ReportStamp(SpsTowerRules.RecordKind, 1, 0);
        return null;
    }

    /// <summary>The ruin of the plan's template on the active body: with or without its guard post, the lattice ruin at the
    /// plan's revision, then the console, the lamps and the tank (<see cref="SpsTowerGenerator"/>).</summary>
    private LandmarkLayout BuildSpsTowerLayout(LandmarkPlan plan, bool atmosphere)
        => SpsTowerGenerator.Build(plan.Template, plan.Seed, plan.Turns, _content, LandmarkSurfaceBlock(), atmosphere, plan.GuardPost,
            plan.Revision);

    /// <summary>Seats the planned tower with the guaranteed search (#586) and the footprint registry: dry ground of any relief
    /// the site grade can level — and only where its top stays below the body's atmosphere line (an absolute world row).</summary>
    private bool TrySeatSpsTower(LandmarkPlan plan)
    {
        var s = plan.Layout!.Structure;
        var policy = new SeatPolicy(AllowStilts: false, AllowLava: false, PreferRugged: false);
        int rise = SpsTowerRules.HeightAboveGround(plan.Template);
        double line = _world.Planet.AtmosphereHeight;
        bool SeatOk(Vector3i origin, string seat)
            => seat is "flat" or "slope" or "shelf" && SpsTowerFitsUnderSky(origin.Y, rise, line);

        if (!TryPlaceStructureGuaranteed(s, RngFor(plan.Seed, "search"), KeepClearFor(StructureTypeRules.SpsTower, LandmarkReserved()),
                wantIsland: false, policy, avoidPlayerEdits: false, out var origin, out int groundY, out _, out string seatStyle,
                seatOk: SeatOk, siteKind: StructureTypeRules.SpsTower))
        {
            return false;
        }

        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (origin.X, origin.Z, groundY, seatStyle);
        return true;
    }

    /// <summary>Whether a tower that stands <paramref name="rise"/> rows above its seat at world row <paramref name="groundY"/>
    /// keeps its top <see cref="SpsTowerRules.AtmosphereMargin"/> rows below the atmosphere line <paramref name="line"/>
    /// (0 = the body has none).</summary>
    internal static bool SpsTowerFitsUnderSky(int groundY, int rise, double line)
        => line <= 0 || groundY + rise + SpsTowerRules.AtmosphereMargin <= line;

    // ---------------------------------------------------------------- the record

    private string SpsTowerPin(LandmarkPlan plan, bool atmosphere)
        => new MarkerSettings()
            .Set("turns", plan.Turns)
            .Set("rev", plan.Revision)
            .Set("seed", plan.Seed.ToString(CultureInfo.InvariantCulture))
            .Set(GiantWreckAirPin, atmosphere ? 1 : 0)
            .Set(SpsTowerPostPin, plan.GuardPost ? 1 : 0)
            .Set(GiantWreckFingerprintPin, SpsTowerGenerator.Fingerprint(plan.Template))
            .Set(LandmarkStoragePin, LandmarkOverlayStorage)
            .ToString();

    /// <summary>The plan a placed record pins — null (warned) when its template left the content, or changed since it was
    /// stamped (another fingerprint): its blocks stay in the overlay, its tower record stays, nothing else is re-derived.</summary>
    private LandmarkPlan? SpsTowerPlanFromRecord(StructurePlacementRecord rec)
    {
        var template = _content.GetSpsTower(rec.Template);
        if (template is null)
        {
            _log.Warn($"Guide tower '{rec.Template}' on '{_world.LocationId}' is no longer in the content — its blocks stay, nothing else is restored.");
            return null;
        }

        var pin = MarkerSettings.Parse(rec.KitLayout);
        string pinned = pin.Get(GiantWreckFingerprintPin);
        if (pinned.Length > 0 && pinned != SpsTowerGenerator.Fingerprint(template))
        {
            _log.Warn($"Guide tower '{rec.Template}' on '{_world.LocationId}' was stamped from another version of its template " +
                      $"(fingerprint '{pinned}') — its blocks stay, nothing else is restored.");
            return null;
        }

        long seed = long.TryParse(pin.Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)
            ? s
            : unchecked((long)StableHashing.Mixed("spstower:seed:" + _world.LocationId, _meta.Seed));
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = seed,
            Turns = pin.GetInt("turns", 0),
            Revision = pin.GetInt("rev", SpsTowerGenerator.RuinRevision),
            GuardPost = pin.GetInt(SpsTowerPostPin, 0) != 0,
            OriginX = rec.X,
            OriginZ = rec.Z,
            GroundY = rec.GroundY,
            Seat = rec.Seat,
            Index = rec.Index,
        };
        plan.Layout = BuildSpsTowerLayout(plan, pin.GetInt(GiantWreckAirPin, _world.Planet.IsAirless ? 0 : 1) != 0);
        return plan;
    }

    /// <summary>Pins a fresh tower, stamps its cells once as its structure overlay, protects its box, then builds the runtime
    /// instance, what its markers become (the landmark's chests and emitters) and — once — its tower record.</summary>
    private void CommitSpsTower(LandmarkPlan plan)
    {
        var layout = plan.Layout!;
        string feature = SpsTowerRules.FeatureKey(plan.Index);
        var origin = new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ);
        if (plan.Fresh)
        {
            RecordPlacement(SpsTowerRules.RecordKind, plan.Index, origin, plan.GroundY, false, plan.Seat, plan.Template.Name,
                plan.Template.Key, kitLayout: SpsTowerPin(plan, !_world.Planet.IsAirless), hashRevision: StructurePlacementRecord.MixedHashRevision);
            SavePlacementRecords();
            // a fresh tower of the boot's structures stage reserves its ground (an admin's stamp does not: the stage flag is down)
            ReserveSiteGround(StructureTypeRules.SpsTower, layout.Structure.Width, layout.Structure.Length, origin, plan.GroundY, plan.Seat);
        }

        var instance = new LandmarkInstance
        {
            Template = plan.Template,
            Layout = layout,
            Origin = new Vector3i(plan.OriginX, plan.GroundY - layout.GroundRow, plan.OriginZ),
            GroundY = plan.GroundY,
            Seed = plan.Seed,
            SpsTower = true,
            SpsTowerIndex = plan.Index,
            Protected = true, // a guide tower is read-only for players: nothing of it can be mined, its chests stay lootable
            OverlayResolved = true,
            OverlayKey = feature, // always one structure overlay — never a block edit per cell
        };

        if (!FeatureStamped(feature))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cells = 0;
            _repo.RunInTransaction(() => cells = WriteLandmark(instance, plan.Seat));
            MarkFeatureStamped(feature); // after the commit: a crash mid-stamp re-stamps, never half-marks
            _log.Info($"Stamped guide tower '{instance.Key}' ({layout.Structure.Width}×{layout.Structure.Height}×{layout.Structure.Length}, " +
                      $"{(plan.GuardPost ? "with" : "without")} a guard post, seat {plan.Seat}) on '{_world.LocationId}': {cells} cells in {clock.ElapsedMilliseconds} ms.");
        }

        _spsTowers.Add(instance);
        RegisterProtectedFootprint(StructureTypeRules.SpsTower, instance.Rect, new StructureFootprints.GuardBox(
            instance.Origin.X, instance.Origin.Y - LandmarkPlinthDepth, instance.Origin.Z,
            instance.Max.X - 1, instance.Max.Y - 1, instance.Max.Z - 1));
        MaterializeLandmark(instance); // the chests (once ever), the loot markers, the sound emitters
        instance.RevealKey = SpsTowerRevealKey(instance.SpsTowerIndex);
        EnsureSpsTowerRecord(plan, instance);
        if (plan.Fresh)
        {
            if (plan.Rolled && !_meta.SpsTowerGuaranteeSpent)
            {
                _meta.SpsTowerGuaranteeSpent = true; // the save has its tower: every later body rolls the normal chance
                _repo.SaveMetadata(_meta);
            }

            ReportStamp(SpsTowerRules.RecordKind, 1, 1);
        }
    }

    /// <summary>Writes the tower's <see cref="SpsTowerRecord"/> the first time it is committed (and again only when the save
    /// lost it): where it stands, the relay console's cell, the lamp cells, the guard post and its tank — canonical world
    /// cells read from the built layout. A record that exists is never touched: its state is the tower's life's.</summary>
    private void EnsureSpsTowerRecord(LandmarkPlan plan, LandmarkInstance instance)
    {
        if (FindSpsTower(_world.LocationId, plan.Index) is not null)
        {
            return;
        }

        var parts = SpsTowerGenerator.PartsOf(instance.Layout);
        SpsTowerCell Cell(Vector3i local) => new(WorldConstants.CanonicalBlock(instance.Cell(local), _world.Circumference));
        var corner = WorldConstants.CanonicalBlock(new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ), _world.Circumference);
        _meta.SpsTowers.Add(new SpsTowerRecord
        {
            LocationId = _world.LocationId,
            Index = plan.Index,
            Template = plan.Template.Key,
            X = corner.X,
            Y = corner.Y,
            Z = corner.Z,
            Width = instance.Structure.Width,
            Length = instance.Structure.Length,
            Height = instance.Structure.Height - instance.Layout.GroundRow,
            Turns = plan.Turns,
            Console = parts.Console is { } console ? Cell(console) : null,
            TowerLamps = parts.TowerLamps.Select(Cell).ToList(),
            HasGuardPost = parts.HasGuardPost,
            Tank = parts.Tank is { } tank ? Cell(tank) : null,
            TankLamps = parts.TankLamps.Select(Cell).ToList(),
        });
        _repo.SaveMetadata(_meta);
    }

    /// <summary>The guide tower whose box holds the cell, or null.</summary>
    private LandmarkInstance? SpsTowerAt(Vector3i cell)
    {
        foreach (var t in _spsTowers)
        {
            int dx = WorldConstants.WrapDeltaX(cell.X - t.Origin.X, _world.Circumference);
            if (dx >= 0 && dx < t.Structure.Width && cell.Y >= t.Origin.Y && cell.Y < t.Max.Y && cell.Z >= t.Origin.Z && cell.Z < t.Max.Z)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>Whether a point (or a small area around it) lies in a guide tower's footprint — part of
    /// <see cref="OverlapsLandmark"/>, so every check that keeps clear of the landmark keeps clear of the towers too.</summary>
    private bool OverlapsSpsTower(int x, int z, int halfExtent)
    {
        foreach (var t in _spsTowers)
        {
            var (cx, cz, hw, hl) = t.Rect;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - cx, _world.Circumference));
            if (dx < hw + halfExtent + SettlementCollisionMargin && System.Math.Abs(z - cz) < hl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }
}
