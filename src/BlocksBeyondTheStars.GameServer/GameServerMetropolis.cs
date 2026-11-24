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
/// The metropolises (#3983): every <c>city</c> place of the body type's place list (Earth's nine) is a <see cref="CityPlace"/>
/// with <see cref="CityPlace.Metro"/> set, stamped by the settlement stage before the hospitality roll — so the rolled villages
/// keep clear of it — and committed together with them.
/// <list type="bullet">
/// <item><b>The place.</b> Its centre is the place's block column; the landing pad listed at the same spot lies in the middle of
/// the plaza district (<see cref="CityPlace.Plaza"/>), whose floor is the pad's level, and the pad ring stays open ground. Its
/// record is settlement <see cref="CityPlaceIndexBase"/> + its position among the list's cities (template
/// <c>city:&lt;theme&gt;</c>, the kit, the laid district map as its layout, <see cref="CityOverlayPin"/> in its plan field),
/// its seed hashed from the place key and the body, its name the English text of its name key (shown in every player's
/// language: <see cref="SettlementNameFor"/>).</item>
/// <item><b>The ground plan</b> (<see cref="MetropolisPlanner"/>) is laid once on the generator's ground (sea, kept, out of
/// reach) and pinned; a replay composes from the record alone.</item>
/// <item><b>The cells</b> are ONE structure overlay per city (<c>city:&lt;index&gt;</c>, like a landmark's): the stamper writes
/// through an overlay sink, only the claimed columns (the map's <c>.</c> districts keep their sea and land).</item>
/// <item><b>The landmarks</b> stand on their squares as given landmarks (records <see cref="CityLandmarkIndexBase"/> + 10 × the
/// city + the landmark's position in the city's list), at the city's floor, stamped right after the city and replayed with the
/// other given landmarks.</item>
/// </list>
/// A metropolis is a body's first stamp: on a body that was played on before its place appeared it records a skip.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The settlement record index of the first listed city.</summary>
    internal const int CityPlaceIndexBase = 1000;

    /// <summary>The landmark record index of the first city's first landmark (given-landmark band, apart from the landmark
    /// places at <see cref="GivenLandmarkIndexBase"/> + their position).</summary>
    internal const int CityLandmarkIndexBase = 1500;

    /// <summary>The landmark record indices one city owns.</summary>
    internal const int CityLandmarkStride = 10;

    /// <summary>The plan field of a metropolis record: its cells are one structure overlay.</summary>
    internal const string CityOverlayPin = "storage=overlay";

    /// <summary>The kit a metropolis composes from when its place names none.</summary>
    internal const string EarthCityKit = "city_earth_1";

    /// <summary>A planned metropolis with what is stamped after it.</summary>
    private sealed class MetroCity
    {
        public required CityPlace Place;
        public required PlacedSettlement City;

        /// <summary>The landmarks to stamp on its squares — fresh plans only (a replay's come back with the given landmarks).</summary>
        public List<(GivenLandmarkPlace Place, int Index)> Landmarks { get; } = new();
    }

    /// <summary>The metropolises of the active body's place list, as city places (empty without a list).</summary>
    private List<CityPlace> ListedCityPlaces()
    {
        var places = new List<CityPlace>();
        if (_content.PlacesFor(_world.Planet.Key) is not { } list)
        {
            return places;
        }

        int i = 0;
        foreach (var p in list.OfKind(PlaceListKinds.City))
        {
            var (x, z) = p.BlockOn(_world.Circumference);
            var pad = _landingPads.FirstOrDefault(l => l.CenterX == x && l.CenterZ == z);
            long seed = unchecked((long)StableHashing.Mixed("city:" + p.Key + ":" + _world.LocationId, _meta.Seed));
            var place = new CityPlace(CityPlaceIndexBase + i, new GivenSeat(x, z, 0, pad?.CenterY), p.ThemeOrKey, seed,
                p.Grid == 0 ? CityGenerator.Modules : p.Grid)
            {
                Metro = true,
                Plaza = p.Plaza.Count == 2 ? (p.Plaza[0], p.Plaza[1]) : null,
                Stations = p.Stations,
                Palette = p.Palette,
                Kit = p.Kit.Length > 0 ? p.Kit : EarthCityKit,
                NameKey = p.NameKey,
                Name = p.NameKey.Length > 0 ? Localize("en", p.NameKey) : p.Key,
            };
            place.Landmarks.AddRange(p.Landmarks);
            if (pad is not null)
            {
                int half = LandingPadRadius + 3;
                place.OpenZones.Add((x - half, z - half, x + half, z + half));
            }

            places.Add(place);
            i++;
        }

        return places;
    }

    /// <summary>Commits the metropolises and the rolled settlements together (the cities first: the primary settlement is a
    /// city), then stamps the fresh cities' landmarks on their squares.</summary>
    private void CommitMetropolises(List<MetroCity> metros, List<PlacedSettlement> rolled, System.Random rng)
    {
        if (metros.Count == 0 && rolled.Count == 0)
        {
            return;
        }

        var planet = _world.Planet;
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        CommitSettlements(metros.Select(m => m.City).Concat(rolled).ToList(), surface, rng);
        long commit = clock.ElapsedMilliseconds;
        StampMetroLandmarks(metros);
        _metroStampMillis = (_metroStampMillis.Plan, commit, clock.ElapsedMilliseconds - commit);
        foreach (var m in metros)
        {
            var s = m.City.Structure;
            _log.Info($"Stamped the city '{m.City.Name}' ({s.Width}×{s.Length}, {_npcs.Count(n => n.Settlement == m.City.Name)} residents) " +
                      $"at {m.Place.Centre.X},{m.Place.Centre.Z} on '{_world.LocationId}'.");
        }
    }

    /// <summary>The box the rolled settlements keep clear of (the city is not in the footprint registry before the commit).</summary>
    private static (int Cx, int Cz, int Hw, int Hl) MetroReservation(PlacedSettlement city)
        => (city.Origin.X + (city.Structure.Width / 2), city.Origin.Z + (city.Structure.Length / 2),
            (city.Structure.Width / 2) + 1, (city.Structure.Length / 2) + 1);

    /// <summary>Plans every metropolis of the body (records, compositions, ground) — committed later with the rolled settlements.</summary>
    private List<MetroCity> PlanMetropolises(System.Random rng, IReadOnlyList<StructureTemplate> kitPool)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cities = new List<MetroCity>();
        var places = ListedCityPlaces();
        foreach (var place in places)
        {
            if (PlanMetropolis(place, rng, kitPool) is { } city)
            {
                cities.Add(city);
            }
        }

        if (places.Count > 0)
        {
            ReportStamp("settlement", places.Count, cities.Count);
        }

        _metroStampMillis = (clock.ElapsedMilliseconds, 0, 0);
        return cities;
    }

    /// <summary>One metropolis: its record (pinned, or laid now), its composition and its claimed ground. Null when it was
    /// skipped before, cannot be laid, or its kit is gone.</summary>
    private MetroCity? PlanMetropolis(CityPlace place, System.Random rng, IReadOnlyList<StructureTemplate> kitPool)
    {
        int index = place.SettlementIndex;
        var rec = FindPlacementRecord("settlement", index);
        if (rec is { Placed: false })
        {
            return null;
        }

        var kit = _content.KitByKey(rec?.Kit ?? place.Kit);
        MetropolisPlanner.Plan? fresh = null;
        Vector3i origin;
        CityLayoutSpec layout;
        if (rec is not null)
        {
            if (!CityLayoutSpec.TryParse(rec.KitLayout, out layout) || !layout.Metro)
            {
                _log.Warn($"The city record {index.ToString(CultureInfo.InvariantCulture)} on '{_world.LocationId}' pins no metropolis layout — nothing is restored.");
                return null;
            }

            origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
        }
        else if (kit is null || !_worlds.Active.VirginAtLoad)
        {
            _log.Warn($"The city '{place.Name}' cannot stand on '{_world.LocationId}' ({(kit is null ? "its kit is missing" : "the body was played on")}).");
            RecordPlacementSkip("settlement", index);
            SavePlacementRecords();
            return null;
        }
        else
        {
            (origin, fresh) = LayMetropolis(place);
            layout = fresh.Layout;
            RecordPlacement("settlement", index, origin, origin.Y, false, "shelf", place.Name, "city:" + place.Theme, modules: 2,
                kit: kit.Key, kitLayout: layout.Serialize(), hashRevision: StructurePlacementRecord.MixedHashRevision);
            FindPlacementRecord("settlement", index)!.Plan = CityOverlayPin;
        }

        var city = ComposeMetropolis(place, rec, layout, kit, kitPool, origin, rng);
        var metro = new MetroCity { Place = place, City = city };
        if (fresh is not null)
        {
            ReserveMetroGround(city.Structure, origin);
            AddMetroLandmarks(metro, fresh, origin);
        }

        return metro;
    }

    /// <summary>A fresh metropolis's ground plan: its origin (the place's centre in the middle of the plaza district, the floor
    /// at the pad's level or the plaza's ground) and the plan laid on the generator's ground round it.</summary>
    private (Vector3i Origin, MetropolisPlanner.Plan Plan) LayMetropolis(CityPlace place)
    {
        var probe = new CityLayoutSpec(place.MetroGrid, MetropolisPlanner.DistrictSize, MetropolisPlanner.Street, MetropolisPlanner.Height, null, metro: true);
        var (px, pz) = place.PlazaCell;
        var (dx, dz) = probe.DistrictOrigin(px, pz);
        int ox = place.Centre.X - dx - (MetropolisPlanner.DistrictSize / 2), oz = place.Centre.Z - dz - (MetropolisPlanner.DistrictSize / 2);
        var planet = _world.Planet;
        int floorY = place.Centre.GroundY ?? _generator.SurfaceHeight(planet, place.Centre.X, place.Centre.Z);
        var request = new MetropolisPlanner.Request
        {
            Grid = place.MetroGrid,
            Plaza = (px, pz),
            Stations = place.Stations,
            Seed = place.Seed,
            Landmarks = place.Landmarks.Select(k => _content.LandmarkByKey(k)).Where(t => t is not null)
                .Select(t => new MetropolisPlanner.LandmarkRequest(t!.Key, t.Width, t.Length)).ToList(), // the load checks every key
            Ground = (gx, gz) => MetropolisPlanner.SampleDistrict(probe, ox, oz, gx, gz, floorY,
                (x, z) => _generator.IsSurfaceWater(planet, x, z), (x, z) => _generator.SurfaceHeight(planet, x, z)),
        };
        var plan = MetropolisPlanner.Lay(request);
        foreach (string key in plan.Unplaced)
        {
            _log.Warn($"The landmark '{key}' finds no free square in the city '{place.Name}' on '{_world.LocationId}'.");
        }

        return (new Vector3i(ox, floorY, oz), plan);
    }

    /// <summary>The metropolis's structure (its pinned or fresh composition, its theme's colours) as the stamper takes it: its
    /// cells go into the overlay <c>city:&lt;index&gt;</c>.</summary>
    private PlacedSettlement ComposeMetropolis(CityPlace place, StructurePlacementRecord? rec, CityLayoutSpec layout, StructureKit? kit,
        IReadOnlyList<StructureTemplate> kitPool, Vector3i origin, System.Random rng)
    {
        int index = place.SettlementIndex;
        var composition = rec?.Composition is { } pinned ? new List<string>(pinned) : new List<string>();
        var structure = CityGenerator.Generate(place.Seed, _content, place.LocalOpenZones(origin.X, origin.Z), null, 0.0, composition, _log.Warn,
            layout, kit, kitPool, legacyModuleRoll: false, terrainGeneration: PipelineGeneration(_meta.Description.TerrainGeneration),
            theme: place.ThemeKey, materials: ModuleMaterials.FromPalette(_content, place.Palette));
        PinCityComposition(rec, composition, index);
        PinSettlementTraits(rec ?? FindPlacementRecord("settlement", index), CityGenerator.Tier, ruined: false);
        SavePlacementRecords();
        var footprint = new CityFootprint(origin.X, origin.Z, origin.X + structure.Width - 1, origin.Z + structure.Length - 1, CoolRooms: false);
        if (!_worlds.Active.CityFootprints.Contains(footprint))
        {
            _worlds.Active.CityFootprints.Add(footprint);
        }

        return new PlacedSettlement
        {
            Structure = structure,
            Origin = origin,
            GroundY = origin.Y,
            Tier = CityGenerator.Tier,
            Ruined = false,
            OnIsland = false,
            Name = rec?.Name is { Length: > 0 } pinnedName ? pinnedName : place.Name,
            NameKey = place.NameKey,
            Rng = rng,
            Seat = "shelf",
            OverlayKey = CityOverlayKey(index),
        };
    }

    /// <summary>The structure-overlay key of a metropolis's cells.</summary>
    internal static string CityOverlayKey(int settlementIndex) => "city:" + settlementIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>A fresh metropolis reserves the ground of every claimed district (with the streets round it) at its floor —
    /// never the districts left to the land — with the city world's grade.</summary>
    private void ReserveMetroGround(SettlementStructure structure, Vector3i origin)
    {
        if (!_siteGroundOpen || !GeographyWorld || !_worlds.Active.VirginAtLoad)
        {
            return;
        }

        var grade = CityGrade();
        var sites = ClaimedRectangles(structure)
            .Select(r => new SiteFlat(origin.X + r.X, origin.Z + r.Z, r.W, r.L, origin.Y, grade.Radius, grade.MaxCut, grade.MaxFill))
            .ToList();
        int reach = grade.Radius + 1;
        AddSiteFlats(sites, origin.X - reach, origin.Z - reach, origin.X + structure.Width + reach, origin.Z + structure.Length + reach);
    }

    /// <summary>The claimed columns of a structure with a column map as rectangles (greedy, row by row); the whole box without one.</summary>
    internal static List<(int X, int Z, int W, int L)> ClaimedRectangles(SettlementStructure s)
    {
        var rects = new List<(int, int, int, int)>();
        if (!s.HasColumnMap)
        {
            rects.Add((0, 0, s.Width, s.Length));
            return rects;
        }

        var done = new bool[s.Width * s.Length];
        bool Open(int x, int z) => x < s.Width && z < s.Length && !done[(x * s.Length) + z] && s.FloorAt(x, z) >= 0;
        for (int z = 0; z < s.Length; z++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                if (!Open(x, z))
                {
                    continue;
                }

                int w = 1, l = 1;
                while (Open(x + w, z)) w++;
                while (Enumerable.Range(x, w).All(cx => Open(cx, z + l))) l++;
                for (int dx = 0; dx < w; dx++)
                    for (int dz = 0; dz < l; dz++)
                        done[((x + dx) * s.Length) + z + dz] = true;
                rects.Add((x, z, w, l));
            }
        }

        return rects;
    }

    /// <summary>The landmarks of a fresh metropolis: each on its square's centre at the city's floor, turned as the square says.</summary>
    private void AddMetroLandmarks(MetroCity metro, MetropolisPlanner.Plan plan, Vector3i origin)
    {
        int city = metro.Place.SettlementIndex - CityPlaceIndexBase;
        foreach (var square in plan.Squares)
        {
            int k = metro.Place.Landmarks.IndexOf(square.Key);
            if (k < 0 || k >= CityLandmarkStride)
            {
                continue;
            }

            var seat = new GivenSeat(origin.X + square.CentreX, origin.Z + square.CentreZ, square.Turns, origin.Y);
            metro.Landmarks.Add((new GivenLandmarkPlace(square.Key, seat), CityLandmarkIndexBase + (city * CityLandmarkStride) + k));
        }
    }

    /// <summary>Stamps the landmarks of the fresh metropolises (after the cities are in the world). A record that exists already
    /// was stamped and is replayed with the given landmarks.</summary>
    private void StampMetroLandmarks(IEnumerable<MetroCity> cities)
    {
        foreach (var city in cities)
        {
            foreach (var (place, index) in city.Landmarks)
            {
                if (_meta.Placements.Exists(r => r.LocationId == _world.LocationId && r.Kind == LandmarkRules.RecordKind && r.Index == index))
                {
                    continue;
                }

                if (PlanGivenLandmark(place, index) is { } plan)
                {
                    CommitLandmark(plan);
                    continue;
                }

                RecordPlacementSkip(LandmarkRules.RecordKind, index);
                SavePlacementRecords();
            }
        }
    }

    /// <summary>Phase B of a settlement stamp for one structure (inside the transaction): its cells as block edits, or — a
    /// metropolis (<see cref="PlacedSettlement.OverlayKey"/>) — collected into its structure overlay and stored; an overlay
    /// that is stored already (a crash before the stamp mark) is not stamped again.</summary>
    private void StampSettlementCells(PlacedSettlement p, string surface)
    {
        if (p.OverlayKey is not { } key)
        {
            StampSettlementBlocks(p, surface);
            return;
        }

        if (_world.HasStructureOverlay(key))
        {
            return;
        }

        var sink = new OverlayCellSink(_world);
        p.Cells = sink;
        StampSettlementBlocks(p, surface);
        p.Cells = null;
        int cells = StoreStructureOverlay(key, sink);
        _log.Info($"Stored the city '{p.Name}' on '{_world.LocationId}' as the overlay '{key}' ({cells} cells).");
    }

    /// <summary>Test seam (#3983): every metropolis of the active world — its record index, name, name key, box corner, layout
    /// (the composed structure) and pinned district map.</summary>
    internal IReadOnlyList<(int Index, string Name, string NameKey, Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec)> MetropolisesForTest
    {
        get
        {
            var list = new List<(int, string, string, Vector3i, SettlementStructure, CityLayoutSpec)>();
            foreach (var rec in _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == "settlement" && r.Placed
                                                             && r.Index >= CityPlaceIndexBase && r.Plan == CityOverlayPin))
            {
                if (_settlements.FirstOrDefault(s => s.Name == rec.Name) is { Layout: { } layout } s && CityLayoutSpec.TryParse(rec.KitLayout, out var spec))
                {
                    list.Add((rec.Index, s.Name, s.NameKey, s.Min, layout, spec));
                }
            }

            return list;
        }
    }

    /// <summary>The last metropolis stamp's wall time: planning (layout, records, composition, ground), the commit (the cells
    /// into the overlays, the instances, the residents — the rolled settlements' too) and the landmarks.</summary>
    private (long Plan, long Commit, long Landmarks) _metroStampMillis;

    /// <summary>Test seam (#3983): <see cref="_metroStampMillis"/>.</summary>
    internal (long Plan, long Commit, long Landmarks) MetroStampMillisForTest => _metroStampMillis;

    /// <summary>Test seam (#3983): every settlement's name as the map and VEGA show it in <paramref name="locale"/>.</summary>
    internal IReadOnlyList<string> SettlementDisplayNamesForTest(string locale) => _settlements.Select(s => SettlementNameFor(s, locale)).ToList();

    /// <summary>Test seam (#3983): every NPC's role, look, settlement and landmark.</summary>
    internal IReadOnlyList<(string Role, string Look, string Settlement, string LandmarkKey)> NpcPostsForTest
        => _npcs.Select(n => (n.Role, n.Look, n.Settlement, n.LandmarkKey)).ToList();

    /// <summary>Test seam (#3983): a stored structure overlay's cells and encoded bytes, null when the world holds none.</summary>
    internal (int Cells, int Bytes)? StructureOverlaySizeForTest(string key)
        => _world.StructureOverlayFor(key) is { } overlay ? (overlay.CellCount, overlay.Encode().Length) : null;

    /// <summary>A settlement's name in a player's language: its name key's text (a metropolis), else its name.</summary>
    private string SettlementNameFor(SettlementInstance s, string locale)
        => s.NameKey.Length > 0 ? Localize(locale, s.NameKey) : s.Name;
}
