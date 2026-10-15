// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// <b>Lodges</b> (#2640): small inhabited huts out in the open land — the first people met outside a settlement. A lodge
/// is a <see cref="LodgeGenerator"/> structure stamped after the bandit camps (boot stage "structures"):
/// <list type="bullet">
/// <item><b>New worlds only.</b> Terrain generation 25 (<see cref="WorldDescription.WildernessFolkGeneration"/>) or newer,
/// <see cref="Shared.Configuration.ServerConfig.PlaceLodges"/> and <c>PlaceSettlements</c> on, never on a void, airless,
/// ruined-only, gas or city world (the restricted Titas/Valuma types never reach the stage).</item>
/// <item><b>How many.</b> λ = hospitability shaped to (h − 0.35)/0.65 × world size × <see cref="LodgeBaseDensity"/> ×
/// the world's "Settlements" factor, drawn in Bernoulli slots, at most <see cref="LodgeRules.HardCap"/>: lush worlds have
/// one to four, harsh ones none or one.</item>
/// <item><b>Where.</b> The camps' mechanics: placement records <c>lodge/i</c> (the record's template pins the
/// profession), <c>TryPlaceStructureGuaranteed</c> with the camp seat policy, the voxels stamped once
/// (<c>FeatureStamped("lodges")</c>), the instances re-derived on every load. Reserved: the pads, the wreck zone, the
/// settlements and camps with <see cref="LodgeReserveMargin"/> blocks round them (a hut stands clearly outside), the
/// intercity line, the rail ruin and the other lodges.</item>
/// <item><b>Which profession.</b> Weighted by the planet (<see cref="LodgeRules.WeightFor"/> over huntable fauna, flora,
/// tier-1 ores and trees), the professions of a world spread before one repeats.</item>
/// <item><b>Protected like a village</b> (<see cref="IsLodgeBlock"/>, hooked into <c>IsSettlementBlock</c>): the house with
/// its fire and bench and the yard — never the grove or the hunting ground. Picking a plant stays allowed.</item>
/// <item><b>The store crate</b> (<see cref="LodgeStoreIdPrefix"/>): a real container the residents' work fills; a player
/// may open it and look, never take, move or put anything (<see cref="LodgeStoreRefusal"/>).</item>
/// <item><b>The map</b>: POI <c>lodge</c> ("{name}'s hut") once a player on foot came within <see cref="LodgeRevealRange"/>
/// (<see cref="TickLodges"/>) or a villager told of it (GameServerLodgeResidents.cs).</item>
/// </list>
/// A lodge is its own type, never a <c>SettlementInstance</c> — the settlement list has many readers (rail, wishes,
/// festivals, boards) that must not pick up a hut.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Expected lodges at hospitability 1, world size 1 and the Normal "Settlements" option.</summary>
    private const double LodgeBaseDensity = 2.2;

    /// <summary>The ground kept free round every settlement and camp (a hut stands clearly outside them).</summary>
    internal const int LodgeReserveMargin = 40;

    /// <summary>The ground kept free between two lodges.</summary>
    private const int LodgeSpacing = 24;

    /// <summary>A player on foot this close finds the lodge (it appears on the map).</summary>
    internal const float LodgeRevealRange = 40f;

    /// <summary>The id prefix of a lodge's store crate (<c>lodge:&lt;world&gt;:&lt;key&gt;</c>).</summary>
    internal const string LodgeStoreIdPrefix = "lodge:";

    /// <summary>The most different goods a store crate keeps — a new one pushes the oldest out ("brought to the village").</summary>
    internal const int LodgeStoreStacks = 6;

    /// <summary>The most of one good a store crate keeps.</summary>
    internal const int LodgeStoreItemCap = 40;

    /// <summary>One stamped lodge on a world (#2640): the hut, its yard, its markers and who lives there.</summary>
    internal sealed class LodgeInstance
    {
        /// <summary>Stable per-world key, "x:z" of the structure origin.</summary>
        public string Key = string.Empty;

        /// <summary>The placement record index (<c>lodge/i</c>).</summary>
        public int Index;

        /// <summary>World cell of the structure's local (0, 0, 0).</summary>
        public Vector3i Origin;

        public int GroundY;
        public Vector3f Center;
        public required SettlementStructure Layout;

        /// <summary>The lodge's own profession (the yard's): <see cref="LodgeProfessionDefinition.Key"/>.</summary>
        public string Profession = string.Empty;

        /// <summary>The yard kind (<see cref="LodgeRules"/>).</summary>
        public string Yard = string.Empty;

        /// <summary>The protected areas, local and inclusive (<see cref="LodgeGenerator.ProtectedAreas"/>).</summary>
        public IReadOnlyList<(int MinX, int MinZ, int MaxX, int MaxZ)> Protected = System.Array.Empty<(int, int, int, int)>();

        /// <summary>Every marker in world space (cell centres).</summary>
        public readonly List<(string Type, Vector3f Pos)> Markers = new();

        /// <summary>The quarry face: each cell and the block it was stamped with (restored on load).</summary>
        public readonly List<(Vector3i Cell, ushort Block)> Face = new();

        /// <summary>The crop plot's cells.</summary>
        public readonly List<Vector3i> Plot = new();

        /// <summary>The grove's trunk bases.</summary>
        public readonly List<Vector3i> Grove = new();

        /// <summary>The store crate's cell, or null.</summary>
        public Vector3i? Store;

        /// <summary>The store crate's container id ("" = none).</summary>
        public string StoreId = string.Empty;

        /// <summary>The first resident's name — the hut is named after them ("Mira's hut").</summary>
        public string ResidentName = string.Empty;

        /// <summary>The map reveal key (<c>&lt;world&gt;|lodge:&lt;key&gt;</c>).</summary>
        public string RevealKey = string.Empty;

        /// <summary>The work anchor in front of the yard (feet position).</summary>
        public Vector3f YardSpot;
    }

    private List<LodgeInstance> _lodges => _worlds.Active.Lodges;

    /// <summary>Per-world 1 Hz throttle of <see cref="TickLodges"/>.</summary>
    private readonly Dictionary<string, double> _lodgeTickAt = new(System.StringComparer.Ordinal);

    // ---------------- Stamping ----------------

    /// <summary>Boot stage "structures", after the bandit camps: stamps this body's lodges once (generation 25+) and
    /// re-derives their instances and residents on every load.</summary>
    private void StampLodges()
    {
        if (!_config.PlaceLodges || !_config.PlaceSettlements || _content.Lodges.Count == 0
            || _generator.TerrainGeneration < WorldDescription.WildernessFolkGeneration)
        {
            return; // never on an older world; switched off; or no lodge data
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || planet.IsAirless || planet.RuinedSettlementsOnly || planet.IsGasWorld
            || planet.CityWorld.Length > 0)
        {
            return; // nobody lives out in the open on a station, an airless rock, a toxic ruin world, a gas giant or the city world
        }

        double factor = _meta.Description.Settlements.StructureFactor();
        if (factor <= 0)
        {
            return; // "Settlements: Off" — no huts either
        }

        long lSeed = _meta.Seed ^ WorldGenerator.StableHash("lodge:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(lSeed ^ (lSeed >> 32))));
        double h = System.Math.Clamp((Hospitability(planet) - 0.35) / 0.65, 0.0, 1.0);
        double sizeFactor = _world.Circumference / SettlementRefCirc;
        int count = DrawCount(rng, h * sizeFactor * LodgeBaseDensity * factor, LodgeRules.HardCap);
        if (count <= 0)
        {
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var ores = Tier1OresOf(planet);
        var traits = LodgeTraits(planet, ores);

        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 12, LandingPadRadius + 12));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        reserved.Add((wreckX, wreckZ, WreckReservedHalfExtent + 8, WreckReservedHalfExtent + 8));
        foreach (var s in _settlements)
        {
            reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2,
                (s.Max.X - s.Min.X) / 2 + 1 + LodgeReserveMargin, (s.Max.Z - s.Min.Z) / 2 + 1 + LodgeReserveMargin));
        }

        foreach (var camp in _banditCamps)
        {
            reserved.Add(((camp.Min.X + camp.Max.X) / 2, (camp.Min.Z + camp.Max.Z) / 2,
                (camp.Max.X - camp.Min.X) / 2 + 1 + LodgeReserveMargin, (camp.Max.Z - camp.Min.Z) / 2 + 1 + LodgeReserveMargin));
        }

        AppendIntercityReservations(reserved); // #2125: the intercity line's stations and route
        AppendRailRuinReservations(reserved); // #2166: the abandoned monorail station
        foreach (var (cx, cz, hw, hl) in _worlds.Active.RuinFootprints)
        {
            reserved.Add((cx, cz, hw + LodgeSpacing, hl + LodgeSpacing)); // nobody lives in the rubble of a fallen town
        }

        bool blocksAlreadyStamped = FeatureStamped("lodges");
        var used = new List<string>();
        var placed = new List<(PlacedSettlement P, LodgeProfessionDefinition Profession, int Index)>();
        for (int i = 0; i < count; i++)
        {
            long instSeed = lSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));
            var rec = FindPlacementRecord("lodge", i);
            if (rec is not null && !rec.Placed)
            {
                continue;
            }

            // The record pins the profession (its template field); a fresh lodge rolls it from the planet.
            var profession = rec is not null && _content.LodgeProfession(rec.Template) is { } pinned
                ? pinned
                : PickLodgeProfession(traits, used, RngFor(instSeed, "profession"));
            if (profession is null)
            {
                if (rec is null)
                {
                    RecordPlacementSkip("lodge", i); // nothing on this world calls for anyone to live out here
                }

                continue;
            }

            bool alien = RngFor(instSeed, "alien").NextDouble() < 0.3;
            var structure = LodgeGenerator.Generate(instSeed, profession.Yard, surface, ores, alien, _content);

            Vector3i origin;
            int groundY;
            bool onIsland;
            string seat;
            if (rec is not null)
            {
                origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
                groundY = rec.GroundY;
                onIsland = rec.OnIsland;
                seat = rec.Seat;
            }
            else if (!_worlds.Active.VirginAtLoad)
            {
                // A world materialised without its lodges (a save of the contract build): its ground stays untouched —
                // the hut "was elsewhere all along" (the unique-site rule).
                RecordPlacementSkip("lodge", i);
                continue;
            }
            else
            {
                if (!TryPlaceStructureGuaranteed(structure, RngFor(instSeed, "search"), reserved,
                        wantIsland: false, SeatPolicy.Camp, avoidPlayerEdits: false,
                        out origin, out groundY, out onIsland, out seat))
                {
                    RecordPlacementSkip("lodge", i);
                    continue;
                }

                RecordPlacement("lodge", i, origin, groundY, onIsland, seat, "lodge_" + i, template: profession.Key);
            }

            used.Add(profession.Key);
            placed.Add((new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = "lodge",
                Ruined = false,
                OnIsland = onIsland,
                Name = "lodge_" + i,
                Rng = ir,
                Seat = seat,
            }, profession, i));
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1 + LodgeSpacing, structure.Length / 2 + 1 + LodgeSpacing));
        }

        SavePlacementRecords();
        ReportStamp("lodge", count, placed.Count);
        if (placed.Count == 0)
        {
            return;
        }

        // Voxels: one-time only. A lodge is protected, so nobody but its residents changes it — and their work refills.
        if (!blocksAlreadyStamped)
        {
            _repo.RunInTransaction(() =>
            {
                foreach (var (p, _, _) in placed)
                {
                    StampSettlementBlocks(p, surface);
                }
            });
            MarkFeatureStamped("lodges");
        }

        // Instances, the store crates, the doors and the residents: re-derived on every load.
        foreach (var (p, profession, index) in placed)
        {
            MaterializeLodge(p, profession, index, traits);
        }

        RegisterDoors(); // the hinge doors of the huts (rebuilds the whole registry, settlements' and ships' included)
        BroadcastNpcs();
        _log.Info($"Lodges on '{_world.LocationId}': {_lodges.Count} ({string.Join(", ", _lodges.Select(l => l.Profession))}).");
    }

    /// <summary>Everything a stamped lodge is at runtime: the instance, the door marker (the registry hangs a hinge door in
    /// the doorway), the store crate, the residents and — #2643 — its quarry face made whole again (a restart that caught
    /// it half mined).</summary>
    private LodgeInstance MaterializeLodge(PlacedSettlement p, LodgeProfessionDefinition profession, int index,
        IReadOnlyDictionary<string, double> traits)
    {
        var lodge = BuildLodgeInstance(p, profession, index);
        foreach (var m in p.Structure.Markers)
        {
            if (m.Type == LodgeGenerator.MarkerDoor)
            {
                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                _settlementMarkers.Add((m.Type, pos));
                RecordAuthoredDoor(p, m);
            }
        }

        EnsureLodgeStore(lodge);
        _lodges.Add(lodge);
        SpawnLodgeResidents(lodge, profession, traits);
        if (lodge.Face.Count > 0)
        {
            RestoreNpcCellsNow(lodge.Face);
        }

        return lodge;
    }

    /// <summary>The lodge instance of a placed structure: markers in world space, the yard's cells, the protection.</summary>
    private LodgeInstance BuildLodgeInstance(PlacedSettlement p, LodgeProfessionDefinition profession, int index)
    {
        var s = p.Structure;
        var lodge = new LodgeInstance
        {
            Key = $"{p.Origin.X}:{p.Origin.Z}",
            Index = index,
            Origin = new Vector3i(p.Origin.X, p.GroundY, p.Origin.Z),
            GroundY = p.GroundY,
            Center = new Vector3f(p.Origin.X + s.Width / 2f, p.GroundY + 1, p.Origin.Z + s.Length / 2f),
            Layout = s,
            Profession = profession.Key,
            Yard = profession.Yard,
            Protected = LodgeGenerator.ProtectedAreas(profession.Yard),
        };
        lodge.RevealKey = _world.LocationId + "|lodge:" + lodge.Key;
        lodge.StoreId = LodgeStoreIdPrefix + _world.LocationId + ":" + lodge.Key;
        lodge.YardSpot = lodge.Center;

        int circ = _world.Circumference;
        foreach (var m in s.Markers)
        {
            var cell = new Vector3i(WorldConstants.WrapX(p.Origin.X + m.LocalPos.X, circ), p.GroundY + m.LocalPos.Y, p.Origin.Z + m.LocalPos.Z);
            var pos = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            lodge.Markers.Add((m.Type, pos));
            switch (m.Type)
            {
                case LodgeGenerator.MarkerQuarry:
                    lodge.Face.Add((cell, s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)));
                    break;
                case LodgeGenerator.MarkerPlot:
                    lodge.Plot.Add(cell);
                    break;
                case LodgeGenerator.MarkerGrove:
                    lodge.Grove.Add(cell);
                    break;
                case LodgeGenerator.MarkerStore:
                    lodge.Store = cell;
                    break;
                case LodgeGenerator.MarkerYard:
                    lodge.YardSpot = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
                    break;
            }
        }

        return lodge;
    }

    /// <summary>The planet's own tier-1 ores: its veins whose block is an ore a tier-1 tool breaks (never a rare-tier vein).</summary>
    private List<string> Tier1OresOf(PlanetType planet)
        => planet.Ores
            .Where(o => !o.RareTier && _content.GetBlock(o.Block) is { Category: "ore", MinToolTier: <= 1 })
            .Select(o => o.Block)
            .Distinct()
            .ToList();

    /// <summary>How much this world offers each profession, 0..1 per <see cref="LodgeRules.Traits"/> trait.</summary>
    private Dictionary<string, double> LodgeTraits(PlanetType planet, List<string> ores)
    {
        // #2645: only a world whose roster has something a hunter may hunt calls for one (the rocky worlds often do not).
        int huntable = !WorldHasHuntableSpecies() ? 0 : _speciesRoster.Count(sp => sp.Habitat == CreatureHabitat.Land
            && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish);
        double trees = planet.TreeDensity ?? (planet.FloraDensity > 0 ? 0.012 : 0.0);
        return new Dictionary<string, double>(System.StringComparer.Ordinal)
        {
            [LodgeRules.TraitFauna] = System.Math.Clamp(huntable / 3.0, 0.0, 1.0),
            [LodgeRules.TraitFlora] = System.Math.Clamp(planet.FloraDensity / 0.15, 0.0, 1.0),
            [LodgeRules.TraitOres] = System.Math.Clamp(ores.Count / 3.0, 0.0, 1.0),
            [LodgeRules.TraitTrees] = System.Math.Clamp(trees / 0.03, 0.0, 1.0),
        };
    }

    /// <summary>A profession for the next lodge, weighted by the planet; one already living on this world is drawn only
    /// once every fitting one has its hut. Null when no profession fits the world at all.</summary>
    private LodgeProfessionDefinition? PickLodgeProfession(IReadOnlyDictionary<string, double> traits, List<string> used, System.Random rng)
    {
        var fitting = _content.Lodges.Select(p => (P: p, W: LodgeRules.WeightFor(p, traits))).Where(x => x.W > 0).ToList();
        if (fitting.Count == 0)
        {
            return null;
        }

        var fresh = fitting.Where(x => !used.Contains(x.P.Key)).ToList();
        var pool = fresh.Count > 0 ? fresh : fitting;
        double total = pool.Sum(x => x.W);
        double roll = rng.NextDouble() * total;
        foreach (var (p, w) in pool)
        {
            roll -= w;
            if (roll < 0)
            {
                return p;
            }
        }

        return pool[pool.Count - 1].P;
    }

    /// <summary>Adds every lodge of this world to a later stamper's reservations (the monuments, the factories).</summary>
    private void AppendLodgeReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        foreach (var l in _lodges)
        {
            reserved.Add((l.Origin.X + l.Layout.Width / 2, l.Origin.Z + l.Layout.Length / 2, l.Layout.Width / 2 + 2, l.Layout.Length / 2 + 2));
        }
    }

    /// <summary>Whether a point (or a small area round it) lies on a lodge — <c>OverlapsAnySettlement</c> asks it, so the
    /// wreck, the vaults, the chests, the data cubes and the unique sites keep clear of the huts.</summary>
    private bool OverlapsLodge(int x, int z, int halfExtent)
    {
        if (_lodges.Count == 0)
        {
            return false;
        }

        int circ = _world.Circumference;
        foreach (var l in _lodges)
        {
            int hw = l.Layout.Width / 2 + 1, hl = l.Layout.Length / 2 + 1;
            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(x - (l.Origin.X + l.Layout.Width / 2), circ));
            int dz = System.Math.Abs(z - (l.Origin.Z + l.Layout.Length / 2));
            if (dx < hw + halfExtent + SettlementCollisionMargin && dz < hl + halfExtent + SettlementCollisionMargin)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Protection ----------------

    /// <summary>True if the block lies in a lodge's protected part (#2640): the house with its fire and bench, and the
    /// yard — the plot, the quarry face, the woodpile or the drying frame — from the foundation's depth below up to the
    /// roof. The grove and the hunting ground are free. Part of <c>IsSettlementBlock</c>, so mining, blasts, fire, the
    /// pump and the drills respect it; the residents' own work cores ask the narrower settlement-box check.</summary>
    internal bool IsLodgeBlock(Vector3i pos)
    {
        if (_lodges.Count == 0)
        {
            return false;
        }

        int circ = _world.Circumference;
        foreach (var l in _lodges)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - l.Origin.X, circ);
            int lz = pos.Z - l.Origin.Z;
            int ly = pos.Y - l.GroundY;
            if (lx < 0 || lz < 0 || lx >= l.Layout.Width || lz >= l.Layout.Length || ly < -48 || ly >= l.Layout.Height)
            {
                continue;
            }

            foreach (var (minX, minZ, maxX, maxZ) in l.Protected)
            {
                if (lx >= minX && lx <= maxX && lz >= minZ && lz <= maxZ)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the lodge's own layout puts a block at this cell — its log frame and bench count as the hut's,
    /// not as a natural tree (the vegetation exemption of <c>IsSettlementProtected</c>).</summary>
    private bool IsLodgeLayoutCell(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var l in _lodges)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - l.Origin.X, circ);
            int lz = pos.Z - l.Origin.Z;
            int ly = pos.Y - l.GroundY;
            if (lx >= 0 && lz >= 0 && ly >= 0 && lx < l.Layout.Width && ly < l.Layout.Height && lz < l.Layout.Length
                && l.Layout.Get(lx, ly, lz) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The lodge whose footprint holds this cell, or null.</summary>
    private LodgeInstance? LodgeAt(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var l in _lodges)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - l.Origin.X, circ);
            int lz = pos.Z - l.Origin.Z;
            if (lx >= 0 && lz >= 0 && lx < l.Layout.Width && lz < l.Layout.Length)
            {
                return l;
            }
        }

        return null;
    }

    // ---------------- The store crate ----------------

    /// <summary>Makes sure the lodge's store crate exists as a container (created empty once, then persisted).</summary>
    private void EnsureLodgeStore(LodgeInstance lodge)
    {
        if (lodge.Store is not { } cell || _containers.Any(c => c.Id == lodge.StoreId))
        {
            return;
        }

        AddContainer(new StoredContainer
        {
            Id = lodge.StoreId,
            Planet = _world.LocationId,
            Kind = "crate",
            Position = cell,
            Items = new List<ItemStack>(),
        });
    }

    private StoredContainer? LodgeStore(LodgeInstance lodge)
        => lodge.StoreId.Length == 0 ? null : _containers.FirstOrDefault(c => c.Id == lodge.StoreId);

    /// <summary>A resident puts its yield into the store crate: stacks merge up to <see cref="LodgeStoreItemCap"/>, and
    /// a new good in a full crate (<see cref="LodgeStoreStacks"/> kinds) pushes the oldest one out — the resident took
    /// that lot to the village. Persisted and broadcast like every crate change.</summary>
    private void DepositIntoLodgeStore(LodgeInstance lodge, string item, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(item) || _content.GetItem(item) is null || LodgeStore(lodge) is not { } crate)
        {
            return;
        }

        var items = crate.Items.Where(s => !s.IsEmpty).ToList();
        int at = items.FindIndex(s => s.Item == item);
        if (at >= 0)
        {
            items[at] = new ItemStack(item, System.Math.Min(LodgeStoreItemCap, items[at].Count + count));
        }
        else
        {
            while (items.Count >= LodgeStoreStacks)
            {
                items.RemoveAt(0); // the oldest lot went to the village
            }

            items.Add(new ItemStack(item, System.Math.Min(LodgeStoreItemCap, count)));
        }

        crate.Items = items;
        _repo.SaveContainer(crate);
        BroadcastContainers();
    }

    /// <summary>
    /// Why a player may not take from, put into, sort or rearrange this container (#2640), or "" when they may: a lodge's
    /// store crate belongs to its residents — "That's Mira's — ask them!". Opening it to look stays allowed.
    /// </summary>
    private string LodgeStoreRefusal(string containerId)
    {
        if (!containerId.StartsWith(LodgeStoreIdPrefix, System.StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string name = _lodges.FirstOrDefault(l => l.StoreId == containerId)?.ResidentName ?? string.Empty;
        return "@srv.lodge.store_owned:" + name;
    }

    // ---------------- The map ----------------

    /// <summary>Appends the lodges a player already found (or was told about) to the POI list (#2640).</summary>
    private void AppendLodgePois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var l in _lodges)
        {
            if (_meta.RevealedPois.Contains(l.RevealKey))
            {
                pois.Add(new NetPoi
                {
                    Type = "lodge",
                    Name = Localize(session.Locale, "poi.lodge").Replace("{0}", l.ResidentName),
                    X = l.Center.X,
                    Z = l.Center.Z,
                });
            }
        }
    }

    /// <summary>Per-world 1 Hz pass: a lodge is found by a player on foot within <see cref="LodgeRevealRange"/> — it goes
    /// on everyone's map (the reveal is world-wide and persisted, like a villager's hint).</summary>
    private void TickLodges(double dt)
    {
        _ = dt;
        if (_worlds.Active is null || _lodges.Count == 0)
        {
            return;
        }

        string world = _world.LocationId;
        if (_lodgeTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _lodgeTickAt[world] = _uptime + 1.0;
        foreach (var l in _lodges)
        {
            if (_meta.RevealedPois.Contains(l.RevealKey))
            {
                continue;
            }

            foreach (var s in JoinedInActiveWorld())
            {
                if (!s.Spectating && OnFootHere(s)
                    && WrapDistSq(s.State.Position, l.Center) <= LodgeRevealRange * (double)LodgeRevealRange)
                {
                    RevealPoi(l.RevealKey);
                    break;
                }
            }
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2640): the lodges of the active world.</summary>
    public IReadOnlyList<(string Key, string Profession, Vector3i Origin, Vector3f Center, string StoreId, bool Revealed)> LodgesForTest
        => _lodges.Select(l => (l.Key, l.Profession, l.Origin, l.Center, l.StoreId, _meta.RevealedPois.Contains(l.RevealKey))).ToList();

    /// <summary>Test/inspection (#2640): a lodge's markers in world space.</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> LodgeMarkersForTest(string key)
        => _lodges.FirstOrDefault(l => l.Key == key)?.Markers.ToList() ?? new List<(string, Vector3f)>();

    /// <summary>Test/inspection (#2640): a lodge's quarry face cells and their stamped blocks.</summary>
    public IReadOnlyList<(Vector3i Cell, ushort Block)> LodgeFaceForTest(string key)
        => _lodges.FirstOrDefault(l => l.Key == key)?.Face.ToList() ?? new List<(Vector3i, ushort)>();

    /// <summary>Test seam (#2640): whether a cell is part of a lodge's protected house or yard.</summary>
    public bool IsLodgeBlockForTest(Vector3i pos) => IsLodgeBlock(pos);

    /// <summary>Test seam (#2640): runs the lodge reveal pass now (skips the 1 Hz throttle).</summary>
    public void TickLodgesForTest()
    {
        _lodgeTickAt.Remove(_world.LocationId);
        TickLodges(0);
    }

    /// <summary>Test seam (#2640): builds a lodge of a profession with its local origin at the given cell — the blocks
    /// written straight in (no terrain fitting), then the instance, door, store and residents exactly as a stamped one
    /// gets them. <paramref name="beds"/> 0 = as the seed decides. Returns the lodge key.</summary>
    public string SpawnLodgeForTest(int x, int groundY, int z, string profession, int beds = 0, long seed = 1, bool alien = false)
    {
        var prof = _content.LodgeProfession(profession) ?? throw new System.ArgumentException("unknown lodge profession " + profession, nameof(profession));
        var planet = _world.Planet;
        var ores = Tier1OresOf(planet);
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var s = LodgeGenerator.Generate(seed, prof.Yard, surface, ores, alien, _content, beds);
        int circ = _world.Circumference;
        for (int lx = 0; lx < s.Width; lx++)
            for (int ly = 0; ly < s.Height; ly++)
                for (int lz = 0; lz < s.Length; lz++)
                {
                    ushort b = s.Get(lx, ly, lz);
                    if (b != 0)
                    {
                        _world.SetBlock(new Vector3i(WorldConstants.WrapX(x + lx, circ), groundY + ly, z + lz), new BlockId(b), 0, 0, s.GetShape(lx, ly, lz));
                    }
                }

        var p = new PlacedSettlement
        {
            Structure = s,
            Origin = new Vector3i(x, groundY, z),
            GroundY = groundY,
            Tier = "lodge",
            Ruined = false,
            OnIsland = false,
            Name = "lodge_test",
            Rng = new System.Random(7),
            Seat = "flat",
        };
        var lodge = MaterializeLodge(p, prof, _lodges.Count, LodgeTraits(planet, ores));
        RegisterDoors();
        BroadcastNpcs();
        return lodge.Key;
    }

    /// <summary>Test seam (#2640): the player opens a crate's screen (mirrors <c>OpenContainerIntent</c>).</summary>
    public void OpenContainerForTest(string playerId, string containerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleOpenContainer(session, new OpenContainerIntent { ContainerId = containerId, Open = true });
        }
    }

    /// <summary>Test seam (#2641): puts an item into a lodge's store as a resident's work would.</summary>
    public void DepositIntoLodgeStoreForTest(string key, string item, int count)
    {
        if (_lodges.FirstOrDefault(l => l.Key == key) is { } lodge)
        {
            DepositIntoLodgeStore(lodge, item, count);
        }
    }
}
