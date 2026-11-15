// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// LOST CIVILIZATIONS (#3639, terrain generation 43, docs/developer/CIVILIZATIONS.md): a framework civilization
/// (<c>data/civilizations/&lt;key&gt;.json</c>) lives only on its home bodies — picked once from the seed and pinned in
/// <c>WorldMetadata.CivilizationHomes</c> — and there its sites are stamped with the landmark machinery
/// (GameServerLandmark*.cs), from their own list and records:
/// <list type="bullet">
/// <item><b>The stage</b> runs right after the Sounder world, before the settlements, which keep clear of the sites through the
/// footprint registry (kind <see cref="StructureTypeRules.Civilization"/>). On a home body the kinds the civilization
/// suppresses (another landmark, a giant wreck) are not stamped.</item>
/// <item><b>The sites</b>, in the order of the civilization's <c>sites</c>: per pool a count rolled from the seed, per site
/// a weighted pick of the pool's templates (a generator entry builds through its source), a turn, the instance seed — each a
/// hash of its own lane — and the seat: the guaranteed search on land, the sea floor for an underwater civilization
/// (GameServerCivilizationStamp.cs). A pick without a seat gives way to another (up to four tries).</item>
/// <item><b>Pinned, stamped once</b>: one placement record per site (kind <c>civ:&lt;key&gt;</c>, the site's index) with the
/// template key, the seat and, in its layout field, <c>turns</c>, <c>rev</c>, <c>seed</c>, the water top of a sunken site and
/// <c>storage=overlay</c>; the cells go once into one structure overlay per site (an underwater site writes water, never
/// air, below the sea — GameServerCivilizationStamp.cs). A home body untouched at load that cannot hold a site records a skip.</item>
/// <item><b>Every later load</b> re-derives each site from its record for its markers (the chests, once ever, on their own lane
/// and container prefix), the discovery and the streaming lift. A lost civilization's site is never staffed.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    internal sealed partial class LandmarkInstance
    {
        /// <summary>#3639: the civilization whose site this is (empty = a landmark or a giant wreck).</summary>
        public string Civilization { get; init; } = string.Empty;

        /// <summary>#3639: the site's index on its body (its record index).</summary>
        public int CivilizationIndex { get; init; }

        /// <summary>#3640: the sea surface over a sunken site — the stamp writes water, not air, into its empty cells at and below
        /// it. Null for a site on land.</summary>
        public int? WaterTop { get; init; }

        /// <summary>#3642: the map name key of the site (its pool's, or its template's).</summary>
        public string SiteNameKey { get; init; } = string.Empty;
    }

    private sealed partial class LandmarkPlan
    {
        /// <summary>#3640: the sea surface over a sunken site (null on land).</summary>
        public int? WaterTop;

        /// <summary>#3642: the map name key of the site.</summary>
        public string SiteNameKey = string.Empty;
    }

    /// <summary>The pin key of a sunken site's water top.</summary>
    internal const string CivilizationWaterPin = "wt";

    /// <summary>Container id prefix of a civilization site's chest (the landmark chest path, its own prefix).</summary>
    internal const string CivilizationChestContainerPrefix = "loot_civ_chest_";

    /// <summary>The lane of a civilization chest's existence and content hashes.</summary>
    private const string CivilizationChestLane = "civ";

    private List<LandmarkInstance> _civSites => _worlds.Active.CivilizationSites;

    /// <summary>Test seam: the civilization sites of the active world.</summary>
    internal IReadOnlyList<LandmarkInstance> CivilizationSitesForTest => _civSites;

    /// <summary>Test seam: the pinned civilization homes.</summary>
    internal IReadOnlyDictionary<string, List<string>> CivilizationHomesForTest => _meta.CivilizationHomes;

    /// <summary>The framework civilizations whose home the active body is.</summary>
    private List<CivilizationDefinition> HomeCivilizations()
    {
        var list = new List<CivilizationDefinition>();
        foreach (var civ in _content.Civilizations)
        {
            if (civ.IsFramework && _meta.CivilizationHomes.TryGetValue(civ.Key, out var homes) && homes.Contains(_world.LocationId))
            {
                list.Add(civ);
            }
        }

        return list;
    }

    /// <summary>Whether a civilization at home on the active body keeps <paramref name="kind"/> off it (a world of terrain
    /// generation 43 or later; an older one has no homes).</summary>
    private bool CivilizationSuppresses(string kind)
    {
        if (!CivilizationKindOn())
        {
            return false; // the kind switched off stamps no site, so a home body keeps its landmark and its giant wreck
        }

        foreach (var civ in HomeCivilizations())
        {
            if (civ.HomeBody.Suppress.Contains(kind, System.StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the <c>civilization</c> kind of data/structure_types.json stamps on the active body (switched on, the
    /// body's terrain generation at least its <c>minGeneration</c>).</summary>
    private bool CivilizationKindOn()
        => StructureType(StructureTypeRules.Civilization) is { Enabled: true } rules && _generator.TerrainGeneration >= rules.MinGeneration;

    /// <summary>#3640: the galaxy of the world from its seed — with the civilizations' guarantees the world pinned on its first
    /// start (<c>WorldMetadata.CivilizationGuarantees</c>), or, on that first start of a generation-43 world, the ones the
    /// generator applied, pinned now (saved with the metadata).</summary>
    private Galaxy GenerateGalaxy(int systemCount)
    {
        var generator = new UniverseGenerator(_meta.Seed, _meta.Description, _content, _meta.DefaultPlanetType)
        {
            PinnedCivilizationGuarantees = _meta.CivilizationGuarantees,
        };
        var galaxy = generator.Generate(systemCount);
        if (_meta.CivilizationGuarantees is null && _meta.Description.TerrainGeneration >= WorldDescription.CivilizationsGeneration)
        {
            _meta.CivilizationGuarantees = new Dictionary<string, string>(generator.AppliedCivilizationGuarantees, System.StringComparer.Ordinal);
        }

        return galaxy;
    }

    /// <summary>Picks and pins the homes of every framework civilization that has none pinned yet (a world of terrain
    /// generation 43 or later, once the galaxy exists). A pinned home never moves; a civilization added later never takes a
    /// body another one holds.</summary>
    private void EnsureCivilizationHomes()
    {
        int generation = _meta.Description.TerrainGeneration;
        if (_galaxy is null || generation < WorldDescription.CivilizationsGeneration)
        {
            return;
        }

        var missing = _content.Civilizations
            .Where(c => c.IsFramework && c.MinTerrainGeneration <= generation && !_meta.CivilizationHomes.ContainsKey(c.Key)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var picked = CivilizationHomes.Pick(_galaxy, _content, _meta.Seed, System.Math.Max(1, _meta.Description.StarSystemCount),
            generation, StartSystemId);
        var taken = new HashSet<string>(_meta.CivilizationHomes.Values.SelectMany(h => h), System.StringComparer.Ordinal);
        foreach (var civ in missing)
        {
            var homes = picked.TryGetValue(civ.Key, out var h) ? h.Where(id => !taken.Contains(id)).ToList() : new List<string>();
            _meta.CivilizationHomes[civ.Key] = homes;
            taken.UnionWith(homes);
            _log.Info($"Civilization '{civ.Key}' lives on {(homes.Count == 0 ? "no body of this galaxy" : string.Join(", ", homes))}.");
        }

        _repo.SaveMetadata(_meta);
    }

    /// <summary>The boot stamp (right after the Sounder world): replay every record of every civilization on this body, or — on a
    /// home body untouched at load — plan, seat and stamp the civilization's sites. A record is replayed whatever the content
    /// says now: a site a world already has stays.</summary>
    private void StampCivilizations()
    {
        var loaded = _worlds.Active;
        if (loaded.CivilizationsStamped)
        {
            return;
        }

        loaded.CivilizationsStamped = true;
        if (_world.Planet is null || _world.Planet.Void)
        {
            return;
        }

        EnsureCivilizationHomes();
        foreach (var civ in _content.Civilizations)
        {
            if (civ.IsFramework)
            {
                StampCivilization(civ, loaded.VirginAtLoad);
            }
        }
    }

    private void StampCivilization(CivilizationDefinition civ, bool virgin)
    {
        string kind = CivilizationRules.RecordKind(civ.Key);
        var records = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == kind).OrderBy(r => r.Index).ToList();
        if (records.Count > 0)
        {
            foreach (var rec in records)
            {
                if (rec.Placed && CivilizationPlanFromRecord(civ, rec) is { } pinned)
                {
                    CommitCivilizationSite(civ, pinned);
                }
            }

            return;
        }

        bool home = _meta.CivilizationHomes.TryGetValue(civ.Key, out var homes) && homes.Contains(_world.LocationId);
        if (!home || _generator.TerrainGeneration < civ.MinTerrainGeneration
            || !CivilizationKindOn())
        {
            return; // nothing decided: the kind switched off in data/structure_types.json asks again once it is on
        }

        int placed = virgin ? PlanAndCommitCivilizationSites(civ) : 0;
        if (placed == 0)
        {
            RecordPlacementSkip(kind, 0); // decided once: none here (no site fits, or a visited body)
            SavePlacementRecords();
        }
    }

    /// <summary>Plans and stamps every site of the civilization on the active home body, in the order of its pools; each
    /// committed site reserves its footprint before the next one is seated. Returns how many stand.</summary>
    private int PlanAndCommitCivilizationSites(CivilizationDefinition civ)
    {
        int index = 0;
        string body = _world.LocationId;
        var fresh = new List<(LandmarkPlan Plan, LandmarkInstance Site)>();
        foreach (var pool in civ.Sites)
        {
            var (min, max) = CivilizationRules.RangeOf(pool.Count);
            int count = min + StableHashing.Index($"civ:{civ.Key}:{pool.Pool}:count:{body}", _meta.Seed, max - min + 1);
            for (int k = 0; k < count; k++, index++)
            {
                if (PlanCivilizationSite(civ, pool, index) is { } plan)
                {
                    var site = CivilizationInstance(civ, plan);
                    RecordFreshCivilizationSite(civ, plan, site);
                    fresh.Add((plan, site));
                }
            }
        }

        if (fresh.Count > 0)
        {
            SavePlacementRecords(); // every record before any cell: a crash mid-stamp replays them all, none is lost
        }

        foreach (var (plan, site) in fresh)
        {
            StampCivilizationSite(civ, plan, site);
        }

        _log.Info($"Civilization '{civ.Key}' on '{body}': {fresh.Count} of {index} sites stand.");
        return fresh.Count;
    }

    /// <summary>A fresh plan for one site: a weighted pick of the pool's templates, its turn, seed, layout and seat — null when
    /// none of the pool's templates finds a seat (after up to four tries).</summary>
    private LandmarkPlan? PlanCivilizationSite(CivilizationDefinition civ, CivilizationSitePool site, int index)
    {
        var candidates = _content.CivilizationTemplatesFor(civ.Key, site.Pool, _meta.Description.EnabledStructurePacks, _generator.TerrainGeneration)
            .ToList();
        string lane = $"civ:{civ.Key}:{_world.LocationId}:{index.ToString(CultureInfo.InvariantCulture)}";
        for (int attempt = 0; attempt < LandmarkPickAttempts && candidates.Count > 0; attempt++)
        {
            string tryLane = attempt == 0 ? lane : lane + ":" + attempt.ToString(CultureInfo.InvariantCulture);
            var template = PickLandmark(candidates, tryLane + ":pick");
            var plan = new LandmarkPlan
            {
                Template = template,
                Seed = unchecked((long)StableHashing.Mixed(tryLane + ":seed", _meta.Seed)),
                Turns = template.IsSource ? 0 : StableHashing.Index(tryLane + ":turn", _meta.Seed, 4),
                Revision = LandmarkGenerator.RuinRevisionFor(_generator.TerrainGeneration),
                Index = index,
                Fresh = true,
                SiteNameKey = site.NameKey,
            };
            plan.Layout = BuildCivilizationLayout(civ, plan);
            if (plan.Layout is not null && TrySeatCivilizationSite(civ, plan))
            {
                return plan;
            }

            candidates.Remove(template); // no seat for this one here: another of the pool may fit where it could not
        }

        ReportStamp(CivilizationRules.RecordKind(civ.Key), 1, 0);
        return null;
    }

    /// <summary>A site's layout: the authored template with the civilization's materials, ruin look and props, or a generator
    /// entry built by its source and run through the same pass — null (warned) when the source cannot build it.</summary>
    private LandmarkLayout? BuildCivilizationLayout(CivilizationDefinition civ, LandmarkPlan plan)
    {
        string surface = LandmarkSurfaceBlock();
        bool airless = _world.Planet.IsAirless, underwater = civ.Scope.Underwater;
        if (!plan.Template.IsSource)
        {
            return CivilizationLayouts.Build(plan.Template, civ, plan.Seed, plan.Turns, _content, surface, plan.Revision, airless, underwater);
        }

        var built = SourceTemplates.Build(plan.Template, _content, plan.Seed, surface, _world.Planet.Key, _meta.Description.TerrainGeneration,
            out string failure);
        if (built is null)
        {
            _log.Warn($"Civilization entry '{plan.Template.Key}' ({plan.Template.Source}) cannot be built on '{_world.LocationId}': {failure}.");
            return null;
        }

        var materials = CivilizationLayouts.MaterialsOf(civ, surface, _content);
        return BuiltConverters.ToLandmarkLayout(built, plan.Template, plan.Seed, _content, surface, plan.Revision,
            CivilizationLayouts.RuinOptionsOf(civ, airless, underwater), new CivilizationPropDress(civ, plan.Seed, _content, materials));
    }

    // ---------------------------------------------------------------- the record

    private string CivilizationPin(LandmarkPlan plan)
    {
        var pin = new MarkerSettings()
            .Set("turns", plan.Turns)
            .Set("rev", plan.Revision)
            .Set("seed", plan.Seed.ToString(CultureInfo.InvariantCulture))
            .Set(LandmarkStoragePin, LandmarkOverlayStorage);
        if (plan.Template.IsSource)
        {
            pin.Set(SourceTemplates.PinKey, plan.Template.Source);
        }

        if (plan.WaterTop is int top)
        {
            pin.Set(CivilizationWaterPin, top);
        }

        if (plan.SiteNameKey.Length > 0)
        {
            pin.Set("name", plan.SiteNameKey);
        }

        return pin.ToString();
    }

    /// <summary>The plan a placed record pins — null (warned) when its template left the content or changed sides (an authored
    /// record whose key is now a generator entry, or the other way round): its blocks stay, nothing else is re-derived.</summary>
    private LandmarkPlan? CivilizationPlanFromRecord(CivilizationDefinition civ, StructurePlacementRecord rec)
    {
        var template = _content.CivilizationTemplateByKey(rec.Template);
        var pin = MarkerSettings.Parse(rec.KitLayout);
        string pinnedSource = pin.Get(SourceTemplates.PinKey);
        if (template is null || template.Civilization != civ.Key
            || !string.Equals(pinnedSource, template.IsSource ? template.Source.Trim() : string.Empty, System.StringComparison.Ordinal))
        {
            _log.Warn($"Civilization site '{rec.Template}' of '{civ.Key}' on '{_world.LocationId}' is no longer in the content as it was stamped — " +
                      "its blocks stay, nothing else is restored.");
            return null;
        }

        long seed = long.TryParse(pin.Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) ? s : 0;
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = seed,
            Turns = pin.GetInt("turns", 0),
            Revision = pin.GetInt("rev", LandmarkGenerator.RuinRevision),
            OriginX = rec.X,
            OriginZ = rec.Z,
            GroundY = rec.GroundY,
            Seat = rec.Seat,
            Index = rec.Index,
            WaterTop = pin.Get(CivilizationWaterPin).Length > 0 ? pin.GetInt(CivilizationWaterPin, 0) : null,
            SiteNameKey = pin.Get("name"),
        };
        plan.Layout = BuildCivilizationLayout(civ, plan);
        return plan.Layout is null ? null : plan;
    }

    /// <summary>A placed record's site (a replay): its footprint, then its stamp — the overlay is written only when a crash left
    /// it unwritten.</summary>
    private void CommitCivilizationSite(CivilizationDefinition civ, LandmarkPlan plan)
    {
        var site = CivilizationInstance(civ, plan);
        RegisterFootprint(StructureTypeRules.Civilization, site.Rect);
        StampCivilizationSite(civ, plan, site);
    }

    /// <summary>Pins a fresh site in memory (its record is saved with the body's other sites), reserves its ground on land and
    /// its footprint, so the next site of the body keeps clear of it.</summary>
    private void RecordFreshCivilizationSite(CivilizationDefinition civ, LandmarkPlan plan, LandmarkInstance site)
    {
        var layout = plan.Layout!;
        var origin = new Vector3i(plan.OriginX, plan.GroundY, plan.OriginZ);
        RecordPlacement(CivilizationRules.RecordKind(civ.Key), plan.Index, origin, plan.GroundY, false, plan.Seat, plan.Template.Name,
            plan.Template.Key, kitLayout: CivilizationPin(plan), hashRevision: StructurePlacementRecord.MixedHashRevision);
        if (plan.WaterTop is null)
        {
            ReserveSiteGround(StructureTypeRules.Civilization, layout.Structure.Width, layout.Structure.Length, origin, plan.GroundY, plan.Seat);
        }

        RegisterFootprint(StructureTypeRules.Civilization, site.Rect);
    }

    /// <summary>The runtime instance of a planned or replayed site.</summary>
    private static LandmarkInstance CivilizationInstance(CivilizationDefinition civ, LandmarkPlan plan)
    {
        var layout = plan.Layout!;
        return new LandmarkInstance
        {
            Template = plan.Template,
            Layout = layout,
            Origin = new Vector3i(plan.OriginX, plan.GroundY - layout.GroundRow, plan.OriginZ),
            GroundY = plan.GroundY,
            Seed = plan.Seed,
            Civilization = civ.Key,
            CivilizationIndex = plan.Index,
            WaterTop = plan.WaterTop,
            SiteNameKey = plan.SiteNameKey,
            OverlayResolved = true,
            OverlayKey = CivilizationRules.FeatureKey(civ.Key, plan.Index), // always one structure overlay
        };
    }

    /// <summary>Stamps a site's cells once as its structure overlay, then what its markers become (the chests, the sound
    /// emitters). A ruin is open: what players mine stays mined.</summary>
    private void StampCivilizationSite(CivilizationDefinition civ, LandmarkPlan plan, LandmarkInstance site)
    {
        string feature = site.OverlayKey!;
        if (!FeatureStamped(feature))
        {
            var s = site.Structure;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int cells = 0;
            _repo.RunInTransaction(() => cells = WriteLandmark(site, plan.Seat));
            MarkFeatureStamped(feature); // after the commit: a crash mid-stamp re-stamps, never half-marks
            _log.Info($"Stamped civilization site '{site.Key}' of '{civ.Key}' ({s.Width}×{s.Height}×{s.Length}, seat {plan.Seat}) " +
                      $"on '{_world.LocationId}': {cells} cells in {clock.ElapsedMilliseconds} ms.");
        }

        _civSites.Add(site);
        MaterializeLandmark(site); // the chests (once ever), the loot markers, the sound emitters, the reveal key
        if (plan.Fresh)
        {
            ReportStamp(CivilizationRules.RecordKind(civ.Key), 1, 1);
        }
    }

    /// <summary>Whether a point (or a small area around it) lies in a civilization site's footprint — part of
    /// <see cref="OverlapsLandmark"/>, so every check that keeps clear of the landmark keeps clear of the sites too.</summary>
    private bool OverlapsCivilizationSite(int x, int z, int halfExtent)
    {
        foreach (var site in _civSites)
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

    /// <summary>The civilization site whose box holds the cell, or null.</summary>
    private LandmarkInstance? CivilizationSiteAt(Vector3i cell)
    {
        foreach (var site in _civSites)
        {
            int dx = WorldConstants.WrapDeltaX(cell.X - site.Origin.X, _world.Circumference);
            if (dx >= 0 && dx < site.Structure.Width && cell.Y >= site.Origin.Y && cell.Y < site.Max.Y && cell.Z >= site.Origin.Z && cell.Z < site.Max.Z)
            {
                return site;
            }
        }

        return null;
    }
}
