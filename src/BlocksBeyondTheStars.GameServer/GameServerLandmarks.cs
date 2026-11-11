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
/// LANDMARKS (#3483, the school club's giant buildings): at most ONE giant authored building per planet, on about every
/// third planet — a leaning tower, a city gate, a mine — most of them ruins the shared ruin pass derives from the intact
/// design with the instance's seed, so no two look alike.
/// <list type="bullet">
/// <item><b>The gate.</b> <see cref="WorldDescription.LandmarkGeneration"/> (37). A server-side stamp: every chunk of every
/// generation stays bit-identical, the gate only keeps a landmark out of worlds created before it. Never on an asteroid,
/// an airless, gas or void world or the finale body; a type with a structure whitelist carries only landmarks that name it.</item>
/// <item><b>The roll and the pick.</b> <see cref="LandmarkRolls"/>: a pure hash of seed and body below
/// <c>ServerConfig.LandmarkChance</c> × the structures frequency. Then one of the planet's candidates
/// (<c>GameContent.LandmarksFor</c>: pack, planet type, <c>atmosphere</c>, <c>inhabited</c> = the body carries an inhabited
/// settlement) by weight, a quarter turn and the instance seed — each a hash of its own.</item>
/// <item><b>Pinned, stamped once.</b> One placement record (<see cref="LandmarkRules.RecordKind"/>, index 0) holds the
/// template key, the seat, the turn, the seed and the ruin pass revision; the blocks are written once (feature
/// <c>landmark:0</c>) and a ruin stays as the players leave it. Every later load re-derives the layout from the record for
/// the markers — chests (once ever), doors, sound emitters, dens, residents. A body that was visited before the feature
/// gets a pinned skip: nothing ever grows next to a base after the fact.</item>
/// </list>
/// The stamp itself is in GameServerLandmarkStamp.cs, what the markers become in GameServerLandmarkLife.cs.
/// </summary>
public sealed partial class GameServer
{
    private const string LandmarkFeature = LandmarkRules.RecordKind + ":0";

    /// <summary>How far a landmark keeps from the pads' levelled ground beyond its own footprint.</summary>
    private const int LandmarkPadMargin = 16;

    /// <summary>One landmark of the active world.</summary>
    internal sealed class LandmarkInstance
    {
        public required StructureTemplate Template { get; init; }

        public required LandmarkLayout Layout { get; init; }

        /// <summary>World cell of structure-local (0, 0, 0) — Y is the world row of template row 0.</summary>
        public required Vector3i Origin { get; init; }

        /// <summary>World Y of the ground row (the seat).</summary>
        public required int GroundY { get; init; }

        public required long Seed { get; init; }

        public string Key => Template.Key;

        public SettlementStructure Structure => Layout.Structure;

        /// <summary>Exclusive upper corner of the box.</summary>
        public Vector3i Max => new(Origin.X + Structure.Width, Origin.Y + Structure.Height, Origin.Z + Structure.Length);

        /// <summary>An inhabited landmark: read-only for visitors, its posts staffed (#3499). A ruin never is.</summary>
        public bool Protected => Template.Protected && !Layout.Ruined;

        public (int Cx, int Cz, int Hw, int Hl) Rect
            => (Origin.X + (Structure.Width / 2), Origin.Z + (Structure.Length / 2), (Structure.Width / 2) + 1, (Structure.Length / 2) + 1);

        /// <summary>The world cell of a structure-local cell (X not yet wrapped).</summary>
        public Vector3i Cell(Vector3i local) => new(Origin.X + local.X, Origin.Y + local.Y, Origin.Z + local.Z);

        /// <summary>The centre of the footprint at the seat.</summary>
        public Vector3f Center => new(Origin.X + (Structure.Width / 2f), GroundY + 1, Origin.Z + (Structure.Length / 2f));
    }

    /// <summary>What the stamp needs to know about one landmark: the pick, the seat and whether it is new.</summary>
    private sealed class LandmarkPlan
    {
        public required StructureTemplate Template;
        public required long Seed;
        public required int Turns;
        public int OriginX, OriginZ, GroundY;
        public string Seat = "flat";
        public bool Fresh;
        public LandmarkLayout? Layout;
    }

    private List<LandmarkInstance> _landmarks => _worlds.Active.Landmarks;

    /// <summary>Test seam: the landmarks of the active world.</summary>
    internal IReadOnlyList<LandmarkInstance> LandmarksForTest => _landmarks;

    /// <summary>Whether body <paramref name="locationId"/> of the world <paramref name="worldSeed"/> rolls a landmark.</summary>
    internal static bool LandmarkRolls(long worldSeed, string locationId, double chance)
        => LaneRoll(worldSeed, "landmark:" + locationId) < System.Math.Clamp(chance, 0.0, 1.0);

    /// <summary>Whether the active world may carry a landmark at all (the gate, the kind of body, the whitelist).</summary>
    private bool LandmarksAllowed(PlanetType planet, bool restricted)
        => _config.PlaceLandmarks
           && _generator.TerrainGeneration >= WorldDescription.LandmarkGeneration
           && !planet.Void && !planet.IsGasWorld && !planet.IsAirless && !IsAsteroidBody(planet)
           && (!restricted || planet.AllowedStructures.Contains(LandmarkRules.StructureKind));

    /// <summary>An asteroid: a body of the asteroid-field kind, or a type of the asteroid family (the school club's KuBa-W too).</summary>
    private bool IsAsteroidBody(PlanetType planet)
        => planet.Key.StartsWith("asteroid", System.StringComparison.Ordinal)
           || _galaxy?.Systems.SelectMany(s => s.Bodies).FirstOrDefault(b => b.Id == _world.LocationId)?.Kind == CelestialKind.AsteroidField;

    /// <summary>The boot stamp (one line in the structure chain, before the buried ruins so the vaults, cubes, chests and
    /// unique sites stamped after it keep clear): replay the record, or — on a body untouched at load — roll, pick, seat and
    /// stamp.</summary>
    private void StampLandmarks(bool restricted)
    {
        var loaded = _worlds.Active;
        if (loaded.LandmarksStamped)
        {
            return;
        }

        loaded.LandmarksStamped = true;
        var planet = _world.Planet;
        if (planet is null || !LandmarksAllowed(planet, restricted))
        {
            return;
        }

        var rec = FindPlacementRecord(LandmarkRules.RecordKind, 0);
        if (rec is not null)
        {
            if (rec.Placed && LandmarkPlanFromRecord(rec) is { } pinned)
            {
                CommitLandmark(pinned);
            }

            return;
        }

        var plan = loaded.VirginAtLoad ? PlanLandmark(planet, restricted) : null;
        if (plan is null)
        {
            RecordPlacementSkip(LandmarkRules.RecordKind, 0); // decided once: none here (no roll, no fit, or a visited body)
            SavePlacementRecords();
            return;
        }

        CommitLandmark(plan);
    }

    /// <summary>A fresh plan: the roll, the weighted pick among the planet's candidates that fit the body, the turn and the
    /// seat. Null when the body carries none.</summary>
    private LandmarkPlan? PlanLandmark(PlanetType planet, bool restricted)
    {
        double factor = System.Math.Clamp(_meta.Description.Settlements.StructureFactor(), 0.0, 2.0);
        if (factor <= 0 || !LandmarkRolls(_meta.Seed, _world.LocationId, _config.LandmarkChance * factor))
        {
            return null;
        }

        var candidates = _content.LandmarksFor(_meta.Description.EnabledStructurePacks, planet.Key, atmosphere: !planet.IsAirless,
                inhabited: InhabitedSettlementCount > 0, namedTypeOnly: restricted)
            .Where(t => LandmarkFitsBody(t, _world.Circumference)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        string body = _world.LocationId;
        int total = candidates.Sum(t => System.Math.Max(1, t.Weight));
        int at = (int)(StableHashing.Unit("landmark:pick:" + body, _meta.Seed) * total);
        var template = candidates[candidates.Count - 1];
        foreach (var t in candidates)
        {
            at -= System.Math.Max(1, t.Weight);
            if (at < 0)
            {
                template = t;
                break;
            }
        }

        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = unchecked((long)StableHashing.Mixed("landmark:" + body, _meta.Seed)),
            Turns = StableHashing.Index("landmark:turn:" + body, _meta.Seed, 4),
            Fresh = true,
        };
        plan.Layout = BuildLandmarkLayout(plan);
        if (!TrySeatLandmark(plan))
        {
            ReportStamp(LandmarkRules.RecordKind, 1, 0);
            return null;
        }

        return plan;
    }

    /// <summary>A body carries a landmark only when it is several footprints around and a latitude band is left over — a
    /// giant on a small moon would wrap half of it.</summary>
    internal static bool LandmarkFitsBody(StructureTemplate t, int circumference)
    {
        int size = System.Math.Max(t.Width, t.Length);
        return circumference >= LandmarkRules.MinCircumferenceFootprints * size
               && (WorldConstants.LatitudePeriodFor(circumference) / 2) - (size / 2) - 16 >= 16;
    }

    private string LandmarkSurfaceBlock()
    {
        var planet = _world.Planet;
        return planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
    }

    private LandmarkLayout BuildLandmarkLayout(LandmarkPlan plan)
        => LandmarkGenerator.Build(plan.Template, plan.Seed, plan.Turns, _content, LandmarkSurfaceBlock());

    /// <summary>What a landmark keeps clear of: every pad (with a wide margin), the wreck site, the settlements, the rail
    /// line and its ruin, the lodges, the Sounder sites, the fallen ruins, the monuments, the camps, the factories, the labs.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> LandmarkReserved()
    {
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + LandmarkPadMargin, LandingPadRadius + LandmarkPadMargin));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        reserved.Add((wreckX, wreckZ, WreckReservedHalfExtent + 2, WreckReservedHalfExtent + 2));
        void Box(Vector3i min, Vector3i max) => reserved.Add(((min.X + max.X) / 2, (min.Z + max.Z) / 2, ((max.X - min.X) / 2) + 1, ((max.Z - min.Z) / 2) + 1));
        _settlements.ForEach(s => Box(s.Min, s.Max));
        AppendIntercityReservations(reserved);
        AppendRailRuinReservations(reserved);
        AppendLodgeReservations(reserved);
        AppendSounderWorldReservations(reserved);
        reserved.AddRange(_worlds.Active.RuinFootprints);
        _monuments.ForEach(m => Box(m.Min, m.Max));
        _banditCamps.ForEach(c => Box(c.Min, c.Max));
        _factories.ForEach(f => Box(f.Min, f.Max));
        _spsLabs.ForEach(l => Box(l.Min, l.Max));
        return reserved;
    }

    /// <summary>Seats the planned landmark with the guaranteed search (#586). A landmark that only stands on the ground may
    /// stand in water on its piers (a bridge, a drowned gate); one with an underground part needs dry, even ground for its
    /// way in, and nothing may stand in lava.</summary>
    private bool TrySeatLandmark(LandmarkPlan plan)
    {
        var layout = plan.Layout!;
        bool buried = layout.GroundRow > 0;
        var policy = new SeatPolicy(AllowStilts: !buried, AllowLava: false, PreferRugged: false);
        bool SeatOk(Vector3i origin, string seat) => !buried || seat is "flat" or "slope";
        if (!TryPlaceStructureGuaranteed(layout.Structure, RngFor(plan.Seed, "search"), LandmarkReserved(), wantIsland: false, policy,
                avoidPlayerEdits: false, out var origin, out int groundY, out _, out string seat, seatOk: SeatOk))
        {
            return false;
        }

        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (origin.X, origin.Z, groundY, seat);
        return true;
    }

    // ---------------------------------------------------------------- the record

    private static string LandmarkPin(LandmarkPlan plan)
        => new MarkerSettings()
            .Set("turns", plan.Turns)
            .Set("rev", LandmarkGenerator.RuinRevision)
            .Set("seed", plan.Seed.ToString(CultureInfo.InvariantCulture))
            .ToString();

    /// <summary>The plan a placed record pins — null when its template is gone (the blocks stay, nothing is re-derived).</summary>
    private LandmarkPlan? LandmarkPlanFromRecord(StructurePlacementRecord rec)
    {
        var template = _content.LandmarkByKey(rec.Template);
        if (template is null)
        {
            _log.Warn($"Landmark '{rec.Template}' on '{_world.LocationId}' is no longer in the content — its blocks stay, nothing else is restored.");
            return null;
        }

        var pin = MarkerSettings.Parse(rec.KitLayout);
        long seed = long.TryParse(pin.Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)
            ? s
            : unchecked((long)StableHashing.Mixed("landmark:" + _world.LocationId, _meta.Seed));
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = seed,
            Turns = pin.GetInt("turns", 0),
            OriginX = rec.X,
            OriginZ = rec.Z,
            GroundY = rec.GroundY,
            Seat = rec.Seat,
        };
        plan.Layout = BuildLandmarkLayout(plan);
        return plan;
    }

    /// <summary>Pins a fresh landmark, stamps its blocks once, then builds the runtime instance and what its markers become.</summary>
    private void CommitLandmark(LandmarkPlan plan)
    {
        var layout = plan.Layout!;
        if (plan.Fresh)
        {
            RecordPlacement(LandmarkRules.RecordKind, 0, new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ), plan.GroundY, false, plan.Seat,
                plan.Template.Name, plan.Template.Key, kitLayout: LandmarkPin(plan), hashRevision: StructurePlacementRecord.MixedHashRevision);
            SavePlacementRecords();
        }

        var instance = new LandmarkInstance
        {
            Template = plan.Template,
            Layout = layout,
            Origin = new Vector3i(plan.OriginX, plan.GroundY - layout.GroundRow, plan.OriginZ),
            GroundY = plan.GroundY,
            Seed = plan.Seed,
        };

        if (!FeatureStamped(LandmarkFeature))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cells = 0;
            _repo.RunInTransaction(() => cells = WriteLandmark(instance, plan.Seat));
            MarkFeatureStamped(LandmarkFeature); // after the commit: a crash mid-stamp re-stamps, never half-marks
            _log.Info($"Stamped landmark '{instance.Key}' ({layout.Structure.Width}×{layout.Structure.Height}×{layout.Structure.Length}, " +
                      $"{(layout.Ruined ? "ruin" : "intact")}, seat {plan.Seat}) on '{_world.LocationId}': {cells} cells in {clock.ElapsedMilliseconds} ms.");
        }

        _landmarks.Add(instance);
        MaterializeLandmark(instance);
        if (plan.Fresh)
        {
            ReportStamp(LandmarkRules.RecordKind, 1, 1);
        }
    }

    // ---------------------------------------------------------------- keeping clear

    /// <summary>Whether a point (or a small area around it) lies in a landmark's footprint — the stampers that ask
    /// <see cref="OverlapsAnySettlement"/> (the vaults, the cubes, the chests, the unique sites) keep clear of it.</summary>
    private bool OverlapsLandmark(int x, int z, int halfExtent)
    {
        foreach (var lm in _landmarks)
        {
            var (cx, cz, hw, hl) = lm.Rect;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - cx, _world.Circumference));
            if (dx < hw + halfExtent + SettlementCollisionMargin && System.Math.Abs(z - cz) < hl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The landmark whose box holds the cell, or null.</summary>
    private LandmarkInstance? LandmarkAt(Vector3i cell)
    {
        foreach (var lm in _landmarks)
        {
            int dx = WorldConstants.WrapDeltaX(cell.X - lm.Origin.X, _world.Circumference);
            if (dx >= 0 && dx < lm.Structure.Width && cell.Y >= lm.Origin.Y && cell.Y < lm.Max.Y
                && cell.Z >= lm.Origin.Z && cell.Z < lm.Max.Z)
            {
                return lm;
            }
        }

        return null;
    }
}
