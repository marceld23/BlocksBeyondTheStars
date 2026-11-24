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
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Cities as places (#3980): every composed city (<see cref="CityGenerator"/>) is stamped from a <see cref="CityPlace"/> —
/// its centre (a <see cref="GivenSeat"/>: no search), its theme, its grid, its seed and the open zones the composer leaves
/// clear — through ONE path, <see cref="StampCityPlaces"/>. The city world (#1793) is "one city place at landing pad 0"
/// (<see cref="GdsCityPlace"/>): settlement record 0, template <c>city:gds</c>, the settlement seed — so every existing city
/// stamps and replays exactly as before. A world may carry several places; each is pinned under its own settlement index and
/// all of them are committed together (the settlement list is rebuilt from the committed set).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>What a city is composed from: a kit (with its grid), a generator entry, or the place's own layout.</summary>
    private sealed class CityDesign
    {
        public StructureKit? Kit;
        public StructureTemplate? Source;
        public CityLayoutSpec Layout = CityLayoutSpec.Default;
        public bool KitCity;
    }

    /// <summary>The city world (#1793): instead of the hospitality roll, exactly ONE gigantic walled city composed by
    /// <see cref="CityGenerator"/>, centred on landing pad 0 so the ship comes down on the plaza inside the walls — one city
    /// place (<see cref="GdsCityPlace"/>) stamped through <see cref="StampCityPlaces"/>.</summary>
    private void StampCityWorld(PlanetType planet, System.Random rng, long sSeed, string surface,
        IReadOnlyList<StructureTemplate> modules, double moduleChance, IReadOnlyList<StructureTemplate> kitPool)
    {
        if (_landingPads.Count == 0)
        {
            return;
        }

        StampCityPlaces(new[] { GdsCityPlace(planet, sSeed) }, rng, surface, modules, moduleChance, kitPool);
    }

    /// <summary>The G.D.S. world's one city place: centred on pad 0 at the pad's level, pinned as settlement 0, its theme the
    /// planet type's <c>cityWorld</c>, its seed the settlement seed. The pad ring (the plaza keeps it clear for the ship) and
    /// the wreck crash site (it stamps at its fixed offset from pad 0 whatever stands there) are its open zones.</summary>
    private CityPlace GdsCityPlace(PlanetType planet, long sSeed)
    {
        var pad = _landingPads[0];
        var place = new CityPlace(settlementIndex: 0, new GivenSeat(pad.CenterX, pad.CenterZ, 0, pad.CenterY), planet.CityWorld, sSeed);
        int padHalf = LandingPadRadius + 3;
        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        place.OpenZones.Add((pad.CenterX - padHalf, pad.CenterZ - padHalf, pad.CenterX + padHalf, pad.CenterZ + padHalf));
        place.OpenZones.Add((wreckX - WreckReservedHalfExtent, wreckZ - WreckReservedHalfExtent,
            wreckX + WreckReservedHalfExtent, wreckZ + WreckReservedHalfExtent));
        return place;
    }

    /// <summary>Stamps every city place of the body: each is designed, seated on its given centre, pinned (a fresh one) and
    /// reserves its graded ground; then all of them are committed together (phases B–D, shared with the roll). A place that
    /// cannot be built is left out; when none can, nothing is committed.</summary>
    private void StampCityPlaces(IReadOnlyList<CityPlace> places, System.Random rng, string surface,
        IReadOnlyList<StructureTemplate> modules, double moduleChance, IReadOnlyList<StructureTemplate> kitPool)
    {
        var placed = new List<(CityPlace Place, PlacedSettlement City)>();
        foreach (var place in places)
        {
            if (PlanCityPlace(place, rng, surface, modules, moduleChance, kitPool) is { } city)
            {
                placed.Add((place, city));
            }
        }

        if (placed.Count == 0)
        {
            return;
        }

        ReportStamp("settlement", places.Count, placed.Count);
        CommitSettlements(placed.Select(p => p.City).ToList(), surface, rng);
        foreach (var (place, city) in placed)
        {
            _log.Info($"Stamped the '{place.Theme}' city '{city.Name}' ({city.Structure.Width}×{city.Structure.Length}, {_npcs.Count} residents) " +
                      $"at {place.Centre.X},{place.Centre.Z} on '{_world.LocationId}'.");
        }
    }

    /// <summary>One city place: its design (pinned or drawn), its seat on the given centre, its record, the composition and
    /// its reserved ground. Null when it cannot be built (a pinned or drawn generator entry that no longer builds).</summary>
    private PlacedSettlement? PlanCityPlace(CityPlace place, System.Random rng, string surface,
        IReadOnlyList<StructureTemplate> modules, double moduleChance, IReadOnlyList<StructureTemplate> kitPool)
    {
        int index = place.SettlementIndex;
        var rec = FindPlacementRecord("settlement", index);
        if (ResolveCityDesign(place, rec) is not { } design)
        {
            return null;
        }

        // #3518: a generator entry is built before the city is seated — its own footprint, centred like the city's.
        var sourceCity = design.Source is null ? null : BuildWholeSettlement(design.Source, CityGenerator.Tier, ruined: false, turns: 0, place.Seed, surface);
        if (design.Source is not null && sourceCity is null)
        {
            return null; // warned: the entry cannot be built — nothing stands here
        }

        int size = sourceCity?.Width ?? design.Layout.Footprint;
        int length = sourceCity?.Length ?? size;
        var (origin, groundY, name, modulesOn) = rec is { Placed: true }
            ? (new Vector3i(rec.X, rec.GroundY, rec.Z), rec.GroundY, rec.Name, rec.Modules >= 1) // #1827: a pre-module city keeps its districts
            : RecordFreshCity(place, design, size, length);

        // #1872: the module per district is pinned like a settlement's plots (see StampSettlement).
        var zones = place.LocalOpenZones(origin.X, origin.Z);
        List<string>? composition = modulesOn
            ? (rec?.Composition is { } pinned ? new List<string>(pinned) : new List<string>())
            : null;
        var suburbs = CitySuburbPins(rec, design.Layout, origin, size, zones); // #3599: a grown city's suburbs, pinned like its districts
        // #2690: `rec` is the record as loaded (null for a city composed right now) — a pre-#1872 one freezes the legacy roll.
        var structure = sourceCity ?? CityGenerator.Generate(place.Seed, _content, zones, modulesOn ? modules : null, moduleChance, composition, _log.Warn,
            design.Layout, design.Kit, design.KitCity ? kitPool : null, UsesLegacyModuleRoll(rec),
            terrainGeneration: _meta.Description.TerrainGeneration, suburbPlans: suburbs, theme: place.ThemeKey); // #3519: generation 38 — the district modules' marker settings
        PinCitySuburbs(rec, suburbs, index);
        PinCityComposition(rec, composition, index);
        PinSettlementTraits(rec ?? FindPlacementRecord("settlement", index), CityGenerator.Tier, ruined: false); // #3513: like every settlement
        SavePlacementRecords();
        SeatCityGround(place, structure, origin, fresh: rec is null);
        return new PlacedSettlement
        {
            Structure = structure,
            Origin = origin,
            GroundY = groundY,
            Tier = CityGenerator.Tier,
            Ruined = false,
            OnIsland = false,
            Name = name,
            Rng = rng,
            Seat = "shelf",
            Protection = ProtectionOnThisWorld(sourceCity is null ? null : design.Source), // generation 39: a city entry's word
        };
    }

    /// <summary>What the city is composed from. #1876: a kit city — the record pins the kit and its grid; a fresh city takes
    /// the place's kit when it names one, else draws a city kit for this planet type (by weight) when one exists, else the
    /// place's own layout. Off keeps the layout procedural. Null when a pinned generator entry is gone (#3518: its blocks stay,
    /// nothing is re-derived).</summary>
    private CityDesign? ResolveCityDesign(CityPlace place, StructurePlacementRecord? rec)
    {
        var design = new CityDesign { Layout = place.Layout };
        if (rec is { Placed: true, Modules: >= 2 })
        {
            design.KitCity = true;
            design.Kit = _content.KitByKey(rec.Kit);
            if (!CityLayoutSpec.TryParse(rec.KitLayout, out design.Layout))
            {
                design.Layout = place.Layout;
            }
        }
        else if (rec is { Placed: true } && SourceTemplates.IsRecord(rec.Template))
        {
            design.Source = PinnedSettlementSource(rec); // #3518: replays itself
            if (design.Source is null)
            {
                return null;
            }
        }
        else if (rec is null && place.Kit.Length > 0 && _content.KitByKey(place.Kit) is { } given)
        {
            (design.Kit, design.KitCity, design.Layout) = (given, true, CityLayoutSpec.FromKit(given));
        }
        else if (rec is null && _meta.Description.SettlementTemplateUse.Probability() > 0)
        {
            DrawCityKitOrSource(place, design);
        }

        return design;
    }

    /// <summary>A fresh city's draw (on its own lane of the place's seed): a city kit of the planet type by weight, or a
    /// generator entry of the metropolis tier appended after the kits (#3518, generation 38).</summary>
    private void DrawCityKitOrSource(CityPlace place, CityDesign design)
    {
        var kits = _content.KitsFor(StructureKit.KindCity, null, _meta.Description.EnabledStructurePacks, _world.Planet.Key, _meta.Description.TerrainGeneration); // #3583
        var sources = SettlementSourcesFor(CityGenerator.Tier);
        int total = sources.Sum(s => System.Math.Max(1, s.Weight));
        foreach (var k in kits)
        {
            total += System.Math.Max(1, k.Weight);
        }

        if (total <= 0)
        {
            return;
        }

        int r = RngFor(place.Seed, "citykit").Next(total);
        foreach (var k in kits)
        {
            r -= System.Math.Max(1, k.Weight);
            if (r < 0)
            {
                design.Kit = k;
                break;
            }
        }

        design.Source = design.Kit is null ? AppendedSourceAt(sources, r) : null;
        if (design.Kit != null)
        {
            design.KitCity = true;
            design.Layout = CityLayoutSpec.FromKit(design.Kit);
        }
    }

    /// <summary>A city composed right now: seated on its given centre (at the given ground row, else the footprint's median
    /// ground), named, and pinned (kind <c>settlement</c>, the place's index, seat shelf, template <c>city:&lt;theme&gt;</c> or the
    /// generator entry's record).</summary>
    private (Vector3i Origin, int GroundY, string Name, bool ModulesOn) RecordFreshCity(CityPlace place, CityDesign design, int width, int length)
    {
        var origin = GivenOrigin(place.Centre, width, length); // #3980: the given seat — no search
        int groundY = origin.Y;
        string name = place.Name.Length > 0 ? place.Name : CityDisplayName(RngFor(place.Seed, "cityname"));
        RecordPlacement("settlement", place.SettlementIndex, origin, groundY, false, "shelf", name,
            design.Source is null ? "city:" + place.Theme : SourceTemplates.RecordOf(design.Source),
            modules: design.KitCity ? 2 : 1, kit: design.Kit?.Key ?? string.Empty, kitLayout: design.KitCity ? design.Layout.Serialize() : string.Empty,
            hashRevision: StructurePlacementRecord.MixedHashRevision);
        return (origin, groundY, name, true);
    }

    /// <summary>The city's ground: its footprint joins the world's city footprints (the G.D.S. theme's with cool rooms); #3571:
    /// a city composed right now reserves its whole footprint at its centre's level (the pads inside keep their own levelling,
    /// which runs last) and grades a wide ring around it.</summary>
    private void SeatCityGround(CityPlace place, SettlementStructure structure, Vector3i origin, bool fresh)
    {
        var footprint = new CityFootprint(origin.X, origin.Z, origin.X + structure.Width - 1, origin.Z + structure.Length - 1,
            CoolRooms: place.ThemeKey == CityPlace.DefaultTheme);
        if (!_worlds.Active.CityFootprints.Contains(footprint))
        {
            _worlds.Active.CityFootprints.Add(footprint);
        }

        if (fresh)
        {
            ReserveSiteGround(StructureTypeRules.Settlement, structure.Width, structure.Length, origin, origin.Y, "shelf", CityGrade());
        }
    }

    /// <summary>#1872: a fresh composition (or a pre-#1872 record's first one) is written into the city's record once.</summary>
    private void PinCityComposition(StructurePlacementRecord? loaded, List<string>? composition, int index)
    {
        if (composition is { Count: > 0 } && (loaded ?? FindPlacementRecord("settlement", index)) is { } cityRec && cityRec.Composition is null)
        {
            cityRec.Composition = composition;
            _placementRecordsDirty = true;
            SavePlacementRecords();
        }
    }

    /// <summary>The G.D.S. city's name (#1793): a root the desert-world names share, under the initials nobody explains.</summary>
    private static string CityDisplayName(System.Random rng)
    {
        string[] roots = { "Veyra", "Sarath", "Ossira", "Tarmun", "Zephar", "Kel-Dun", "Ilvane", "Quorra" };
        return "G.D.S. " + roots[rng.Next(roots.Length)];
    }
}
