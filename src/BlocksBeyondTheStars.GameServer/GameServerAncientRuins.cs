// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Underground structures (#3333/#3334, terrain generation 35): the buried G.D.S. ruins of Justus' "Alte Städte" — old purple
/// and red metal cities under the ground, flooded with the glowing white liquid Ressaw — and every other structure an author
/// flags "Underground" in the editor (an underground settlement or city kit, an underground whole template).
/// <para>Two slots per world. The CITY slot (<see cref="UndergroundCitySlot"/>): a world whose type has a city (<c>gds_desert</c>)
/// always carries a G.D.S. ruin under its city, reached through a sealed hatch in a street crossing ("Go. Don't dig."). The
/// ROLL slot (<see cref="UndergroundRollSlot"/>): every non-void planet carries one underground structure rarely
/// (<see cref="AncientRuinChance"/>, a pure hash of world seed and body), drawn by weight from ALL the underground kits and whole
/// templates the planet may carry, away from the pads and every structure, reached through a vent tower and a ladder.</para>
/// <para>A G.D.S. ruin kit (<see cref="AncientRuinGenerator.IsGdsRuinKit"/>) goes through the ruin pass; anything else is
/// enclosed exactly as it was built (<see cref="AncientRuinGenerator.Enclose"/>). Both are seated <see cref="AncientRuinCover"/>
/// rows under the LOWEST surface over their footprint, carved, sealed in a stone shell and stamped once
/// (<see cref="FeatureStamped"/> + a placement record per slot, #1990 / #586): a mined structure stays mined. Every load
/// re-derives the runtime state (entrances, crates, doors) from the pinned records. The candidates, the faithful sources and
/// their markers are in GameServerAncientRuinSources.cs, the seats and the stamp in GameServerAncientRuinStamp.cs.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The placement-record kind and the stamped-feature prefix (<c>underground:&lt;slot&gt;</c>) of every slot.</summary>
    internal const string UndergroundKind = "underground";

    /// <summary>The structure kind of a G.D.S. ruin's crates (<c>gds_cache:&lt;n&gt;</c>, the loot code's contract).</summary>
    internal const string AncientRuinKind = "gds_ruin";

    /// <summary>The rare roll every eligible planet makes (any underground kit or template).</summary>
    internal const int UndergroundRollSlot = 0;

    /// <summary>The G.D.S. ruin a city world always hides under its city.</summary>
    internal const int UndergroundCitySlot = 1;

    /// <summary>The chance that a planet's roll slot carries a structure (a few percent; scaled by the Vaults option).</summary>
    internal const double AncientRuinChance = 0.04;

    /// <summary>The key a restricted planet type names in <c>allowedStructures</c> to carry underground structures.</summary>
    internal const string AncientRuinStructureKey = "gds_ruins";

    /// <summary>Rows from the lowest surface over the footprint down to the ceiling shell.</summary>
    internal const int AncientRuinCover = 8;

    /// <summary>The height of the vent tower's walls above the ground.</summary>
    internal const int AncientRuinTowerHeight = 5;

    private const int AncientRuinVentHintRadius = 24;
    private const int AncientRuinHatchHintRadius = 8;

    /// <summary>A stamped underground structure on the active world — re-derived on every load from its placement record.</summary>
    internal sealed class AncientRuinSite
    {
        /// <summary>The slot it fills (<see cref="UndergroundRollSlot"/> or <see cref="UndergroundCitySlot"/>).</summary>
        public int Slot { get; init; }

        /// <summary>Structure-local (0, 0, 0) in world space; Y is the floor row.</summary>
        public Vector3i Origin { get; init; }

        public int Width { get; init; }

        public int Length { get; init; }

        /// <summary>Rows from the floor up (the ceiling shell is the next row).</summary>
        public int Height { get; init; }

        /// <summary>The top of the shaft: the hatch's glass in the city street, or the vent tower's ground cell.</summary>
        public Vector3i Entrance { get; init; }

        /// <summary>True for the sealed hatch under a G.D.S. city, false for a vent tower in the open land.</summary>
        public bool Hatch { get; init; }

        /// <summary>True for a G.D.S. ruin, false for an authored structure enclosed as built.</summary>
        public bool Gds { get; init; }

        /// <summary>What it was built from: <c>gds</c>, <c>city</c>, <c>settlement:&lt;tier&gt;</c> or <c>template:&lt;key&gt;</c>.</summary>
        public string Source { get; init; } = string.Empty;

        public string Kit { get; init; } = string.Empty;

        /// <summary>The G.D.S. crate cells in crate order (crate n is <c>gds_cache:n</c>); empty for an authored structure.</summary>
        public IReadOnlyList<Vector3i> Caches { get; init; } = System.Array.Empty<Vector3i>();

        /// <summary>The footprint as the surface stampers keep clear of it (centre and half extents).</summary>
        public (int Cx, int Cz, int Hw, int Hl) Rect => (Origin.X + Width / 2, Origin.Z + Length / 2, Width / 2 + 1, Length / 2 + 1);
    }

    /// <summary>Where a structure goes and what it is made of — fresh from the seed or replayed from its record.</summary>
    private sealed class AncientRuinPlan
    {
        public int Slot { get; set; }

        /// <summary><c>gds</c>, <c>city</c>, <c>settlement:&lt;tier&gt;</c> or <c>template:&lt;key&gt;</c> — pinned as the record's template.</summary>
        public string Source { get; set; } = SourceGds;

        public StructureKit? Kit { get; set; }
        public string KitKey { get; set; } = string.Empty;

        /// <summary>The kit's grid as pinned: a city spec (G.D.S. and city kits) or a settlement spec.</summary>
        public string KitLayout { get; set; } = string.Empty;

        public CityLayoutSpec Spec { get; set; }
        public List<string> Composition { get; set; } = new();

        /// <summary>An authored structure as composed (null for a G.D.S. ruin, which is composed at commit).</summary>
        public WorldGeneration.SettlementStructure? Built { get; set; }

        public int Width { get; set; }
        public int Length { get; set; }
        public int Height { get; set; }
        public int OriginX { get; set; }
        public int OriginZ { get; set; }
        public int GroundY { get; set; }
        public int CrossI { get; set; }
        public int CrossJ { get; set; }
        public bool Hatch { get; set; }

        /// <summary>The top of the shaft: the city street row under a hatch, the ground under a vent tower.</summary>
        public int EntranceY { get; set; }

        public long Seed { get; set; }
        public bool Fresh { get; set; }

        public bool Gds => Source == SourceGds;

        /// <summary>The seat as pinned in the record: <c>hatch|vent:i:j:entranceY</c>.</summary>
        public string Seat => string.Join(":", Hatch ? "hatch" : "vent", CrossI.ToString(CultureInfo.InvariantCulture),
            CrossJ.ToString(CultureInfo.InvariantCulture), EntranceY.ToString(CultureInfo.InvariantCulture));
    }

    private const string SourceGds = "gds";

    /// <summary>The underground structures of the active world (tests/inspection).</summary>
    internal IReadOnlyList<AncientRuinSite> AncientRuinsForTest => _worlds.Active.AncientRuins;

    /// <summary>The active world's G.D.S. ruin under its city, else its first underground structure, else null.</summary>
    internal AncientRuinSite? AncientRuinForTest
        => _worlds.Active.AncientRuins.FirstOrDefault(s => s.Slot == UndergroundCitySlot) ?? _worlds.Active.AncientRuins.FirstOrDefault();

    /// <summary>Whether body <paramref name="locationId"/> of the world <paramref name="worldSeed"/> rolls its roll slot (a G.D.S.
    /// city world fills its city slot anyway). <paramref name="factor"/> is the Vaults option's structure factor.</summary>
    internal static bool AncientRuinRolls(long worldSeed, string locationId, double factor)
        => StableHashing.Unit("gds_ruin:roll:" + locationId, worldSeed) < AncientRuinChance * factor;

    /// <summary>The boot stamp (one line in the structure chain, before the vaults so they keep clear of it): the city slot on
    /// a city world, then the roll slot when the planet rolls it.</summary>
    private void StampAncientRuins(bool restricted)
    {
        var loaded = _worlds.Active;
        if (loaded.AncientRuinStamped)
        {
            return;
        }

        loaded.AncientRuinStamped = true;
        var (city, roll) = AncientRuinSlotsFor(_world.Planet, _world.LocationId, restricted);
        if (city)
        {
            StampUndergroundSlot(UndergroundCitySlot);
        }

        if (roll)
        {
            StampUndergroundSlot(UndergroundRollSlot);
        }
    }

    /// <summary>The slots the boot stamp fills on body <paramref name="locationId"/> of type <paramref name="planet"/>: the city
    /// slot on a city world, the roll slot when the body rolls it — none where underground structures are off. The one rule
    /// the stamp and the admin's <c>/ressaw</c> prediction (#3421, GameServerAncientRuinsAdmin.cs) share, so they never drift.</summary>
    private (bool City, bool Roll) AncientRuinSlotsFor(PlanetType planet, string locationId, bool restricted)
    {
        if (!AncientRuinsAllowed(planet, restricted))
        {
            return (false, false);
        }

        return (!string.IsNullOrEmpty(planet.CityWorld),
            AncientRuinRolls(_meta.Seed, locationId, _meta.Description.Vaults.StructureFactor()));
    }

    private bool AncientRuinsAllowed(PlanetType planet, bool restricted)
        => !planet.Void && !planet.IsGasWorld && _config.PlaceRuins
           && _generator.TerrainGeneration >= WorldDescription.AncientRuinsGeneration
           && (!restricted || planet.AllowedStructures.Contains(AncientRuinStructureKey));

    /// <summary>Whether the boot's structure stage stamps only what <paramref name="planet"/>'s whitelist names (generation 8,
    /// Titas and Valuma; #3358: Soaniri on any save). The boot and the <c>/ressaw</c> prediction both read it here.</summary>
    private bool StructuresRestricted(PlanetType planet)
        => planet.RestrictStructures
           && (_generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration
               || string.Equals(planet.Key, SoundersIds.PlanetType, System.StringComparison.Ordinal));

    /// <summary>One slot: replay its record, or — on a world untouched at load — plan, seat and stamp it fresh. A world that
    /// already holds edits without a record gets a pinned skip: nothing is ever carved under a base after the fact.</summary>
    private void StampUndergroundSlot(int slot)
    {
        var rec = FindPlacementRecord(UndergroundKind, slot);
        if (rec is not null)
        {
            if (rec.Placed && UndergroundPlanFromRecord(rec) is { } pinned)
            {
                CommitAncientRuin(pinned);
            }

            return;
        }

        var plan = _worlds.Active.VirginAtLoad ? PlanUnderground(slot) : null;
        if (plan is null)
        {
            RecordPlacementSkip(UndergroundKind, slot);
            SavePlacementRecords();
            if (_worlds.Active.VirginAtLoad)
            {
                ReportStamp(UndergroundKind, 1, 0);
            }

            return;
        }

        CommitAncientRuin(plan);
    }

    /// <summary>A fresh plan for a slot: a source by weight, then a seat under the city (the city slot) or in the open land.</summary>
    private AncientRuinPlan? PlanUnderground(int slot)
    {
        var plan = PickUndergroundSource(slot, UndergroundSeed(slot));
        if (plan is null)
        {
            _log.Warn($"No underground structure fits slot {slot} of '{_world.LocationId}' ({_world.Planet.Key}).");
            return null;
        }

        bool seated = (slot == UndergroundCitySlot && plan.Gds && TrySeatAncientRuinUnderCity(plan)) || TrySeatAncientRuinInOpenLand(plan);
        return seated ? plan : null;
    }

    private long UndergroundSeed(int slot)
        => unchecked((long)StableHashing.Mixed($"underground:{slot}:{_world.LocationId}", _meta.Seed));

    /// <summary>Composes the planned structure, pins a fresh one, stamps its blocks once, then re-derives the runtime state —
    /// the G.D.S. crates or an authored structure's doors, containers and circuits — and remembers the site.</summary>
    private void CommitAncientRuin(AncientRuinPlan plan)
    {
        var layout = plan.Gds
            ? AncientRuinGenerator.Compose(plan.Seed, _content, plan.Spec, plan.Kit, ModulePool(), plan.Composition, plan.CrossI, plan.CrossJ, _log.Warn)
            : AncientRuinGenerator.Enclose(plan.Seed, _content, plan.Built!, plan.KitKey, plan.Composition);
        if (plan.Fresh)
        {
            RecordPlacement(UndergroundKind, plan.Slot, new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ), plan.GroundY, false, plan.Seat,
                string.Empty, plan.Source, modules: 2, composition: new List<string>(layout.Composition), kit: plan.KitKey,
                kitLayout: plan.KitLayout, hashRevision: StructurePlacementRecord.MixedHashRevision);
            SavePlacementRecords();
        }

        string feature = UndergroundKind + ":" + plan.Slot.ToString(CultureInfo.InvariantCulture);
        if (!FeatureStamped(feature))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cells = 0;
            _repo.RunInTransaction(() => cells = WriteAncientRuin(plan, layout));
            MarkFeatureStamped(feature); // after the commit: a crash mid-stamp re-stamps, never half-marks
            _log.Info($"Stamped an underground {plan.Source} ({plan.KitKey}, {layout.Width}×{layout.Length}, {(plan.Hatch ? "hatch" : "vent")}) " +
                      $"on '{_world.LocationId}': {cells} cells in {clock.ElapsedMilliseconds} ms.");
        }

        var caches = SpawnAncientRuinCaches(plan, layout);
        if (!layout.IsGdsRuin)
        {
            ApplyUndergroundMarkers(plan, layout);
        }

        var (sx, sz) = layout.Shaft;
        _worlds.Active.AncientRuins.Add(new AncientRuinSite
        {
            Slot = plan.Slot,
            Origin = new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ),
            Width = layout.Width,
            Length = layout.Length,
            Height = layout.Height,
            Entrance = new Vector3i(WorldConstants.WrapX(plan.OriginX + sx, _world.Circumference), plan.EntranceY, plan.OriginZ + sz),
            Hatch = plan.Hatch,
            Gds = layout.IsGdsRuin,
            Source = plan.Source,
            Kit = plan.KitKey,
            Caches = caches,
        });
        ReportStamp(UndergroundKind, 1, 1);
    }

    private List<StructureTemplate> ModulePool() => _content.SettlementTemplates.Where(t => t.IsModule).ToList();

    /// <summary>One crate container per G.D.S. crate cell (<c>gds_cache:n</c>, once ever through the structure-loot path; crate
    /// n &lt; 4 holds diving-suit piece n). Returns the cells in world space (none for an authored structure).</summary>
    private List<Vector3i> SpawnAncientRuinCaches(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        var rng = RngFor(plan.Seed, "loot");
        var cells = new List<Vector3i>(layout.Caches.Count);
        for (int n = 0; n < layout.Caches.Count; n++)
        {
            var c = layout.Caches[n];
            var cell = new Vector3i(WorldConstants.WrapX(plan.OriginX + c.X, _world.Circumference), plan.GroundY + c.Y, plan.OriginZ + c.Z);
            cells.Add(cell);
            SpawnStructureLoot(AncientRuinKind, AncientRuinGenerator.CacheMarker + ":" + n.ToString(CultureInfo.InvariantCulture),
                new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), rng);
        }

        return cells;
    }

    /// <summary>Whether a point (or a small area around it) lies over an underground structure — the surface stampers that
    /// ask <see cref="OverlapsAnySettlement"/> keep clear of it (a vault's chamber would cut into the cavity).</summary>
    private bool OverlapsAncientRuin(int x, int z, int halfExtent)
    {
        foreach (var site in _worlds.Active.AncientRuins)
        {
            var (cx, cz, hw, hl) = site.Rect;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - cx, _world.Circumference));
            if (dx < hw + halfExtent + SettlementCollisionMargin && System.Math.Abs(z - cz) < hl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>VEGA's hint at an entrance (once per player): within 24 blocks of a vent tower, within 8 of the city hatch (the
    /// city is where the ship lands — the hatch is found on foot). Called from the 1 Hz advisor pass.</summary>
    private void TickAncientRuinHint(PlayerSession session)
    {
        var p = session.State;
        if (p.AboardShip)
        {
            return;
        }

        foreach (var site in _worlds.Active.AncientRuins)
        {
            string hint = site.Hatch ? "gds_hatch" : site.Gds ? "gds_ruin" : "underground";
            int radius = site.Hatch ? AncientRuinHatchHintRadius : AncientRuinVentHintRadius;
            if (WrapDistSq(p.Position, site.Entrance) <= (double)radius * radius)
            {
                ShipAiHintOnce(session, hint);
            }
        }
    }

    /// <summary>Test seam: composes a slot's structure again from its record (null without one).</summary>
    internal AncientRuinLayout? ComposeAncientRuinForTest(int slot)
    {
        if (FindPlacementRecord(UndergroundKind, slot) is not { Placed: true } rec || UndergroundPlanFromRecord(rec) is not { } plan)
        {
            return null;
        }

        return plan.Gds
            ? AncientRuinGenerator.Compose(plan.Seed, _content, plan.Spec, plan.Kit, ModulePool(), plan.Composition, plan.CrossI, plan.CrossJ)
            : AncientRuinGenerator.Enclose(plan.Seed, _content, plan.Built!, plan.KitKey, plan.Composition);
    }

    /// <summary>Test seam: plans and stamps the roll slot on the active world as if it had rolled (false when it already has a
    /// record or no source or seat was found).</summary>
    internal bool ForceAncientRuinForTest()
    {
        if (FindPlacementRecord(UndergroundKind, UndergroundRollSlot) is not null)
        {
            return false;
        }

        var plan = PlanUnderground(UndergroundRollSlot);
        if (plan is null)
        {
            return false;
        }

        CommitAncientRuin(plan);
        return true;
    }
}
