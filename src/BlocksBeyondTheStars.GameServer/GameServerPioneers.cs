// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// <b>Pioneer outposts</b> (#2681) and <b>their pioneers</b> (#2682) — "player bases of NPCs" (Screelit): the lodge variant
/// a player-like NPC lives in. An outpost IS a <see cref="LodgeInstance"/> (with <see cref="LodgeInstance.Outpost"/> set),
/// so the lodge machinery carries it unchanged: the protection (<see cref="IsLodgeBlock"/>, 48 blocks down, so the cellar
/// is covered), the owned stores (look, don't take — <see cref="LodgeStoreRefusal"/>; no machine or base sees them,
/// <see cref="IsWildernessStoreId"/>), the reveal on foot (<see cref="TickLodges"/>), the villagers' hint, the person rules
/// of the resident and the job state machine. What is its own:
/// <list type="bullet">
/// <item><b>Which worlds</b> (<see cref="PioneersBelongHere"/>): the lodges' — terrain generation 25 or newer, lodges and
/// settlements on, not void, airless, ruined-only, gas or city, "Settlements" not off — plus <see cref="Shared.Configuration.ServerConfig.PlacePioneers"/>
/// and a <b>breathable</b> atmosphere. λ = the lodges' hospitability shape × world size × <see cref="PioneerBaseDensity"/>,
/// at most <see cref="PioneerHardCap"/> per world (its own cap, beside the huts'). Placement records <c>outpost/i</c> (the
/// template pins the resident's work, the kit layout the boat), voxels stamped once (<c>FeatureStamped("outposts")</c>) and
/// only on a body nobody has visited yet (<c>VirginAtLoad</c>), so the chunk pipeline stays bit-identical.</item>
/// <item><b>The structure</b> (<see cref="PioneerOutpostGenerator"/>): a closed room of stone, the biome's earth, iron or
/// steel, a decorative <c>base_core</c> (never registered as a base — no zone, no air machine, mining it is refused anyway),
/// 2–4 crates and a cellar box with a few goods from the start, a bed, a chair, a ladder shaft to the cellar — visible behind
/// a closet door, or hidden under a planter whose soil is the one cell a visitor may dig. By the water a log rowboat.</item>
/// <item><b>The pioneer</b>: one per outpost, player-shaped with a pixel skin (<c>NetNpc</c> look <see cref="PixelLook"/>,
/// the skin seed in <c>FaceVariant</c> — the client builds the pixels from the seed and its skin pool, nothing more goes over
/// the wire), role <c>settler</c> (talk, no trade), job <see cref="PioneerJob"/>, nameplate "Pioneer", a lodge person key.
/// Never hostile (an NPC has no attack at all). Works on the lodge job loop round the outpost — hunting, mining or gathering,
/// whatever the world offers — with the lodge residents' day (work, the chair in the evening, the bed at night).</item>
/// <item><b>Away</b> about a third of the in-game days during working hours (<see cref="PioneerAwayAt"/>, deterministic by
/// the outpost's seed and the day): watched, they walk off and vanish out of sight, and come back from out of sight the
/// same way (the wanderers' and base guests' leave rule); unwatched they are simply gone or home.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The pioneer's job — its own dialogues (<c>data/dialogs.json</c> <c>pioneer</c>, <c>pioneer_friend</c>).</summary>
    internal const string PioneerJob = "pioneer";

    /// <summary>The pioneer's nameplate role.</summary>
    internal const string PioneerNameKey = "npc.role.pioneer";

    /// <summary>The client look key of a pioneer (#2682): the avatar wears a pixel skin built from the face seed.</summary>
    internal const string PixelLook = "pixel";

    /// <summary>The map POI type of an outpost.</summary>
    internal const string OutpostPoiType = "outpost";

    /// <summary>The id prefix of an outpost's crates and cellar box (<c>outpost:&lt;world&gt;:&lt;key&gt;[#n|#cellar]</c>).</summary>
    internal const string OutpostStoreIdPrefix = "outpost:";

    /// <summary>The nameplate activity of a pioneer on the way out or back.</summary>
    internal const string PioneerExploringKey = "npc.activity.exploring";

    /// <summary>Expected outposts at hospitability 1, world size 1 and the Normal "Settlements" option.</summary>
    private const double PioneerBaseDensity = 1.4;

    /// <summary>The most outposts on one world (its own cap, beside the huts' <see cref="LodgeRules.HardCap"/>).</summary>
    internal const int PioneerHardCap = 2;

    /// <summary>A pioneer is away on its away days from this local day fraction …</summary>
    internal const double PioneerAwayFrom = 0.30;

    /// <summary>… until this one (the working hours: back for the evening).</summary>
    internal const double PioneerAwayUntil = 0.66;

    /// <summary>A player this close to the outpost sees the pioneer come home (it walks in from out of sight).</summary>
    private const float PioneerReturnSeenRange = 60f;

    /// <summary>Where a returning pioneer appears, round the outpost.</summary>
    private static readonly float[] PioneerReturnRadii = { 40f, 47f, 55f };

    /// <summary>A walk home that takes longer ends at home (unseen) or keeps trying (watched).</summary>
    private const double PioneerReturnTimeout = 90.0;

    /// <summary>How far round the outpost a mining pioneer looks for an outcrop.</summary>
    private const int PioneerMineRadius = 16;

    /// <summary>How far from the outpost's edge the boat may lie on the water.</summary>
    internal const int OutpostBoatReach = 10;

    /// <summary>A pioneer's trip away from the outpost (RAM only — re-derived from the schedule on every load).</summary>
    internal enum PioneerTrip : byte
    {
        Home,
        Leaving,
        Away,
        Returning,
    }

    /// <summary>What makes a lodge a pioneer outpost (#2681/#2682).</summary>
    internal sealed class PioneerOutpost
    {
        /// <summary>The instance seed (the generator's, the away schedule's).</summary>
        public long Seed;

        public string WallKind = string.Empty;
        public bool HiddenShaft;

        /// <summary>Every store of the outpost — the crates in the room (the first is the work's) and the cellar box.</summary>
        public readonly List<string> StoreIds = new();

        public string CellarStoreId = string.Empty;

        /// <summary>The planter soil over a hidden shaft (canonical cells) — free to dig.</summary>
        public readonly HashSet<Vector3i> Soil = new();

        /// <summary>The boat's cells (canonical) — protected like the outpost.</summary>
        public readonly HashSet<Vector3i> Boat = new();

        public Vector3i? Hatch;
        public Vector3i? CellarSpot;
        public Vector3i? Core;

        /// <summary>The client's pixel-skin seed (never 0).</summary>
        public int SkinSeed;

        public PioneerTrip Trip;
        public double TripSince;
        public double TripBearing;
    }

    /// <summary>The goods a pioneer keeps in the room's crates from the start (item, fewest, most).</summary>
    private static readonly (string Item, int Min, int Max)[] OutpostGoods =
    {
        ("wood_log", 6, 20), ("torch", 2, 6), ("lantern", 1, 2), ("plant_fiber", 4, 12), ("berries", 3, 9),
        ("cooked_meat", 2, 5), ("iron_ore", 3, 9), ("copper_ore", 3, 9), ("bone", 1, 4), ("grain", 3, 8),
    };

    /// <summary>What lies in the cellar's box.</summary>
    private static readonly (string Item, int Min, int Max)[] OutpostCellarGoods =
    {
        ("iron_ingot", 2, 6), ("crystal", 1, 3), ("gold_ingot", 1, 2), ("silver_ingot", 1, 3), ("field_medkit", 1, 1),
        ("hearty_stew", 1, 2),
    };

    // ---------------- Where ----------------

    /// <summary>Whether pioneer outposts belong on the active world (see the class summary).</summary>
    private bool PioneersBelongHere()
    {
        if (!_config.PlacePioneers || !_config.PlaceLodges || !_config.PlaceSettlements || _content.Lodges.Count == 0
            || _generator.TerrainGeneration < WorldDescription.WildernessFolkGeneration)
        {
            return false; // switched off, no professions, or an older world
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || planet.IsAirless || planet.RuinedSettlementsOnly || planet.IsGasWorld
            || planet.CityWorld.Length > 0
            || !string.Equals(planet.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase)
            || (planet.RestrictStructures && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration))
        {
            return false; // a pioneer settles where people can breathe
        }

        return _meta.Description.Settlements.StructureFactor() > 0;
    }

    /// <summary>Boot stage "structures", right after the lodges: stamps this body's outposts once and re-derives their
    /// instances, stores and pioneers on every load.</summary>
    private void StampPioneerOutposts()
    {
        if (!PioneersBelongHere())
        {
            return;
        }

        var planet = _world.Planet;
        long pSeed = _meta.Seed ^ WorldGenerator.StableHash("outpost:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(pSeed ^ (pSeed >> 32))));
        double h = System.Math.Clamp((Hospitability(planet) - 0.35) / 0.65, 0.0, 1.0);
        double sizeFactor = _world.Circumference / SettlementRefCirc;
        int count = DrawCount(rng, h * sizeFactor * PioneerBaseDensity * _meta.Description.Settlements.StructureFactor(), PioneerHardCap);
        if (count <= 0)
        {
            return;
        }

        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var traits = LodgeTraits(planet, Tier1OresOf(planet));
        var reserved = OutpostReservations();
        bool blocksAlreadyStamped = FeatureStamped("outposts");
        var placed = new List<(PlacedSettlement P, PioneerOutpostGenerator.Outpost Outpost, LodgeProfessionDefinition Work, int Index, long Seed, string Boat)>();
        for (int i = 0; i < count; i++)
        {
            long instSeed = OutpostInstanceSeed(pSeed, i);
            var rec = FindPlacementRecord("outpost", i);
            if (rec is not null && !rec.Placed)
            {
                continue;
            }

            var work = rec is not null && _content.LodgeProfession(rec.Template) is { } pinned
                ? pinned
                : PickPioneerWork(traits, RngFor(instSeed, "work"));
            var outpost = PioneerOutpostGenerator.Generate(instSeed, surface, _content);

            Vector3i origin;
            int groundY;
            bool onIsland;
            string seat;
            string boat;
            if (rec is not null)
            {
                origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
                groundY = rec.GroundY;
                onIsland = rec.OnIsland;
                seat = rec.Seat;
                boat = rec.KitLayout;
            }
            else if (!_worlds.Active.VirginAtLoad)
            {
                RecordPlacementSkip("outpost", i); // a body somebody has been on keeps its ground (the unique-site rule)
                continue;
            }
            else
            {
                if (!TryPlaceOutpost(outpost.Structure, instSeed, reserved, out origin, out groundY, out onIsland, out seat, out boat))
                {
                    RecordPlacementSkip("outpost", i);
                    continue;
                }

                RecordPlacement("outpost", i, origin, groundY, onIsland, seat, "outpost_" + i, template: work.Key, kitLayout: boat);
            }

            placed.Add((new PlacedSettlement
            {
                Structure = outpost.Structure,
                Origin = origin,
                GroundY = groundY,
                Tier = "outpost",
                Ruined = false,
                OnIsland = onIsland,
                Name = "outpost_" + i,
                Rng = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32)))),
                Seat = seat,
            }, outpost, work, i, instSeed, boat));
            int s = PioneerOutpostGenerator.Size;
            reserved.Add((origin.X + s / 2, origin.Z + s / 2, s / 2 + 1 + LodgeSpacing, s / 2 + 1 + LodgeSpacing));
        }

        SavePlacementRecords();
        ReportStamp("outpost", count, placed.Count);
        if (placed.Count == 0)
        {
            return;
        }

        // Voxels: one-time only (the outpost is protected; nobody but the soil-digger changes it).
        if (!blocksAlreadyStamped)
        {
            _repo.RunInTransaction(() =>
            {
                foreach (var (p, outpost, _, _, _, boat) in placed)
                {
                    StampSettlementBlocks(p, surface);
                    StampOutpostBelow(outpost, p.Origin, p.GroundY);
                    StampOutpostBoat(boat);
                }
            });
            MarkFeatureStamped("outposts");
        }

        foreach (var (p, outpost, work, index, seed, boat) in placed)
        {
            MaterializeOutpost(p, outpost, work, index, seed, boat);
        }

        RegisterDoors();
        BroadcastNpcs();
        _log.Info($"Pioneer outposts on '{_world.LocationId}': {placed.Count}.");
    }

    private static long OutpostInstanceSeed(long pSeed, int index) => pSeed ^ unchecked((long)(index + 1) * (long)0x9E3779B97F4A7C15);

    /// <summary>The ground an outpost keeps clear of: the lodges' reservations (pads, the wreck zone, settlements and camps
    /// with a margin, the intercity line, the rail ruin, the ruins) and every lodge already on the world.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> OutpostReservations()
    {
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

        AppendIntercityReservations(reserved);
        AppendRailRuinReservations(reserved);
        foreach (var (cx, cz, hw, hl) in _worlds.Active.RuinFootprints)
        {
            reserved.Add((cx, cz, hw + LodgeSpacing, hl + LodgeSpacing));
        }

        foreach (var l in _lodges)
        {
            reserved.Add((l.Origin.X + l.Layout.Width / 2, l.Origin.Z + l.Layout.Length / 2,
                l.Layout.Width / 2 + 1 + LodgeSpacing, l.Layout.Length / 2 + 1 + LodgeSpacing));
        }

        return reserved;
    }

    /// <summary>The camp seat search, up to three times: the first spot with water for a boat beside it wins, else the first
    /// spot at all. <paramref name="boat"/> is the boat's spec (<see cref="FindOutpostBoatSpot"/>), "" without one.</summary>
    private bool TryPlaceOutpost(SettlementStructure s, long instSeed, List<(int Cx, int Cz, int Hw, int Hl)> reserved,
        out Vector3i origin, out int groundY, out bool onIsland, out string seat, out string boat)
    {
        origin = default;
        groundY = 0;
        onIsland = false;
        seat = "flat";
        boat = string.Empty;
        bool any = false;
        foreach (string lane in new[] { "search", "search2", "search3" })
        {
            if (!TryPlaceStructureGuaranteed(s, RngFor(instSeed, lane), reserved, wantIsland: false, SeatPolicy.Camp,
                    avoidPlayerEdits: false, out var o, out int gy, out bool island, out string st))
            {
                continue;
            }

            string spot = FindOutpostBoatSpot(_generator, _world.Planet, _world.Circumference, o, gy);
            if (!any || spot.Length > 0)
            {
                (origin, groundY, onIsland, seat, boat) = (o, gy, island, st, spot);
                any = true;
            }

            if (spot.Length > 0)
            {
                break;
            }
        }

        return any;
    }

    /// <summary>The pioneer's work, weighted by the planet like a lodge's profession — hunting, mining or gathering, never
    /// felling; a hunter where the world has game is a little likelier ("they hunt and stay near their base").</summary>
    private LodgeProfessionDefinition PickPioneerWork(IReadOnlyDictionary<string, double> traits, System.Random rng)
    {
        var fitting = _content.Lodges
            .Where(p => p.Work is LodgeRules.WorkHunt or LodgeRules.WorkMine or LodgeRules.WorkGather)
            .Select(p => (P: p, W: LodgeRules.WeightFor(p, traits) * (p.Work == LodgeRules.WorkHunt ? 1.5 : 1.0)))
            .Where(x => x.W > 0)
            .ToList();
        if (fitting.Count == 0)
        {
            return _content.Lodges.FirstOrDefault(p => p.Work == LodgeRules.WorkGather) ?? _content.Lodges[0];
        }

        double roll = rng.NextDouble() * fitting.Sum(x => x.W);
        foreach (var (p, weight) in fitting)
        {
            roll -= weight;
            if (roll < 0)
            {
                return p;
            }
        }

        return fitting[fitting.Count - 1].P;
    }

    // ---------------- Stamping ----------------

    /// <summary>The cellar and the shaft below the floor row (inside the repo transaction).</summary>
    private void StampOutpostBelow(PioneerOutpostGenerator.Outpost outpost, Vector3i origin, int groundY)
    {
        int circ = _world.Circumference;
        foreach (var (local, block, shape) in outpost.Below)
        {
            var cell = new Vector3i(WorldConstants.WrapX(origin.X + local.X, circ), groundY + local.Y, origin.Z + local.Z);
            if (WithinBuildHeight(cell.Y))
            {
                _world.SetBlock(cell, block == 0 ? BlockId.Air : new BlockId(block), 0, 0, shape);
            }
        }
    }

    /// <summary>The boat on the water (inside the repo transaction); nothing without a spec.</summary>
    private void StampOutpostBoat(string spec)
    {
        foreach (var (cell, block, shape) in OutpostBoatCells(spec))
        {
            _world.SetBlock(cell, new BlockId(block), 0, 0, shape);
        }
    }

    /// <summary>The boat's cells in the world from its spec "<c>x,y,z,x|z</c>" (corner on the water's top row, axis).</summary>
    private List<(Vector3i Cell, ushort Block, int Shape)> OutpostBoatCells(string spec)
    {
        var cells = new List<(Vector3i, ushort, int)>();
        if (!TryParseBoatSpec(spec, out int bx, out int by, out int bz, out bool alongX))
        {
            return cells;
        }

        int circ = _world.Circumference;
        foreach (var (local, block, shape) in PioneerOutpostGenerator.Boat(alongX, _content))
        {
            cells.Add((new Vector3i(WorldConstants.WrapX(bx + local.X, circ), by + local.Y, bz + local.Z), block, shape));
        }

        return cells;
    }

    private static bool TryParseBoatSpec(string spec, out int x, out int y, out int z, out bool alongX)
    {
        x = y = z = 0;
        var parts = (spec ?? string.Empty).Split(',');
        alongX = parts.Length == 4 && parts[3] == "x";
        return parts.Length == 4
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
               && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out z)
               && parts[3] is "x" or "z";
    }

    /// <summary>
    /// Where the outpost's boat lies (#2681): a 3 × 6 patch of open water within <see cref="OutpostBoatReach"/> of the
    /// footprint's edge, parallel to it, every column liquid (no ice) and at least two deep with one water level that lies
    /// at most six below the floor — nearest the outpost first, the front first. Pure generator queries (no chunk is
    /// loaded), so the search is the same on every machine. Returns the spec "<c>x,y,z,x|z</c>" or "" with no water near.
    /// </summary>
    internal static string FindOutpostBoatSpot(WorldGenerator gen, PlanetType planet, int circ, Vector3i origin, int groundY)
    {
        int size = PioneerOutpostGenerator.Size, len = PioneerOutpostGenerator.BoatLength, wid = PioneerOutpostGenerator.BoatWidth;
        for (int d = 2; d <= OutpostBoatReach; d++)
        {
            for (int side = 0; side < 4; side++)
            {
                for (int off = 0; off + len <= size; off += 3)
                {
                    // Front (+Z) and back (−Z): along X; right (+X) and left (−X): along Z.
                    bool alongX = side < 2;
                    int x0 = side switch
                    {
                        0 or 1 => origin.X + off,
                        2 => origin.X + size - 1 + d,
                        _ => origin.X - d - (wid - 1),
                    };
                    int z0 = side switch
                    {
                        0 => origin.Z + size - 1 + d,
                        1 => origin.Z - d - (wid - 1),
                        _ => origin.Z + off,
                    };
                    if (BoatWaterFits(gen, planet, circ, x0, z0, alongX ? len : wid, alongX ? wid : len, groundY, out int top))
                    {
                        return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", WorldConstants.WrapX(x0, circ), top, z0, alongX ? "x" : "z");
                    }
                }
            }
        }

        return string.Empty;
    }

    private static bool BoatWaterFits(WorldGenerator gen, PlanetType planet, int circ, int x0, int z0, int w, int l, int groundY, out int top)
    {
        top = int.MinValue;
        for (int dx = 0; dx < w; dx++)
        {
            for (int dz = 0; dz < l; dz++)
            {
                int x = WorldConstants.WrapX(x0 + dx, circ), z = z0 + dz;
                if (!gen.TryGetWaterSurface(planet, x, z, out int waterTop, out int seabed) || waterTop - seabed < 2
                    || waterTop > groundY || waterTop < groundY - 6 || gen.SurfaceIceThickness(planet, x, z) > 0
                    || (top != int.MinValue && waterTop != top))
                {
                    return false;
                }

                top = waterTop;
            }
        }

        return top != int.MinValue;
    }

    // ---------------- Runtime ----------------

    /// <summary>Everything a stamped outpost is at runtime: the lodge instance (whole footprint protected, the soil free, the
    /// boat protected), the doors, the stores and the pioneer.</summary>
    private LodgeInstance MaterializeOutpost(PlacedSettlement p, PioneerOutpostGenerator.Outpost outpost, LodgeProfessionDefinition work,
        int index, long seed, string boat)
    {
        var s = p.Structure;
        int circ = _world.Circumference;
        var lodge = new LodgeInstance
        {
            Key = $"{p.Origin.X}:{p.Origin.Z}",
            Index = index,
            Origin = new Vector3i(p.Origin.X, p.GroundY, p.Origin.Z),
            GroundY = p.GroundY,
            Center = new Vector3f(p.Origin.X + s.Width / 2f, p.GroundY + 1, p.Origin.Z + s.Length / 2f),
            Layout = s,
            Profession = work.Key,
            Yard = string.Empty,
            Protected = new[] { (0, 0, s.Width - 1, s.Length - 1) },
        };
        lodge.RevealKey = _world.LocationId + "|" + OutpostPoiType + ":" + lodge.Key;
        lodge.StoreId = OutpostStoreIdPrefix + _world.LocationId + ":" + lodge.Key;
        lodge.YardSpot = lodge.Center;
        var o = new PioneerOutpost
        {
            Seed = seed,
            WallKind = outpost.WallKind,
            HiddenShaft = outpost.HiddenShaft,
            SkinSeed = unchecked((int)WorldGenerator.StableHash("pioneer-skin:" + _world.LocationId + ":" + lodge.Key)) | 1,
        };
        lodge.Outpost = o;

        var stores = new List<Vector3i>();
        Vector3i? cellarStore = null;
        foreach (var m in s.Markers.Concat(outpost.BelowMarkers))
        {
            var cell = new Vector3i(WorldConstants.WrapX(p.Origin.X + m.LocalPos.X, circ), p.GroundY + m.LocalPos.Y, p.Origin.Z + m.LocalPos.Z);
            var pos = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            lodge.Markers.Add((m.Type, pos));
            switch (m.Type)
            {
                case PioneerOutpostGenerator.MarkerDoor:
                    // The door registry's frame, exactly as MaterializeLodge: RecordAuthoredDoor hangs the door at the
                    // UNWRAPPED origin + marker, so the door marker must not be wrapped either — west of x = 0 a wrapped
                    // marker sat a whole circumference away from its own door.
                    _settlementMarkers.Add((m.Type, new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f,
                        p.Origin.Z + m.LocalPos.Z + 0.5f)));
                    RecordAuthoredDoor(p, m);
                    break;
                case PioneerOutpostGenerator.MarkerStore:
                    stores.Add(cell);
                    break;
                case PioneerOutpostGenerator.MarkerCellarStore:
                    cellarStore = cell;
                    break;
                case PioneerOutpostGenerator.MarkerCellar:
                    o.CellarSpot = cell;
                    break;
                case PioneerOutpostGenerator.MarkerSoil:
                    o.Soil.Add(WorldConstants.CanonicalBlock(cell, circ));
                    break;
                case PioneerOutpostGenerator.MarkerHatch:
                    o.Hatch = cell;
                    break;
                case PioneerOutpostGenerator.MarkerCore:
                    o.Core = cell;
                    break;
                case PioneerOutpostGenerator.MarkerYard:
                    lodge.YardSpot = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
                    break;
            }
        }

        foreach (var (cell, _, _) in OutpostBoatCells(boat))
        {
            o.Boat.Add(WorldConstants.CanonicalBlock(cell, circ));
        }

        lodge.Store = stores.Count > 0 ? stores[0] : null;
        EnsureOutpostStores(lodge, stores, cellarStore);
        _lodges.Add(lodge);
        SpawnPioneer(lodge, work);
        return lodge;
    }

    /// <summary>Whether a cell is the soil over a hidden shaft (free to dig) or one of the boat's (protected), and which.</summary>
    private bool OutpostCellFree(PioneerOutpost o, Vector3i pos, out bool boat)
    {
        boat = false;
        if (o.Soil.Count == 0 && o.Boat.Count == 0)
        {
            return false;
        }

        var c = WorldConstants.CanonicalBlock(pos, _world.Circumference);
        if (o.Boat.Contains(c))
        {
            boat = true;
            return true;
        }

        return o.Soil.Contains(c);
    }

    /// <summary>The crates in the room and the cellar's box as owned containers, each filled with a few goods the first time
    /// (then persisted like any crate: the pioneer's work and presents change them).</summary>
    private void EnsureOutpostStores(LodgeInstance lodge, List<Vector3i> crates, Vector3i? cellar)
    {
        var o = lodge.Outpost!;
        for (int i = 0; i < crates.Count; i++)
        {
            string id = i == 0 ? lodge.StoreId : lodge.StoreId + "#" + i.ToString(CultureInfo.InvariantCulture);
            o.StoreIds.Add(id);
            EnsureOutpostStore(id, crates[i], OutpostGoods, RngFor(o.Seed, "crate" + i.ToString(CultureInfo.InvariantCulture)), 2, 3);
        }

        if (cellar is { } cell)
        {
            o.CellarStoreId = lodge.StoreId + "#cellar";
            o.StoreIds.Add(o.CellarStoreId);
            EnsureOutpostStore(o.CellarStoreId, cell, OutpostCellarGoods, RngFor(o.Seed, "cellar"), 2, 2);
        }
    }

    private void EnsureOutpostStore(string id, Vector3i cell, (string Item, int Min, int Max)[] goods, System.Random rng, int minKinds, int maxKinds)
    {
        if (_containers.Any(c => c.Id == id))
        {
            return;
        }

        var pool = goods.Where(g => _content.GetItem(g.Item) is not null).ToList();
        var items = new List<ItemStack>();
        int kinds = rng.Next(minKinds, maxKinds + 1);
        for (int k = 0; k < kinds && pool.Count > 0; k++)
        {
            var (item, min, max) = pool[rng.Next(pool.Count)];
            pool.RemoveAll(g => g.Item == item);
            items.Add(new ItemStack(item, rng.Next(min, max + 1)));
        }

        AddContainer(new StoredContainer
        {
            Id = id,
            Planet = _world.LocationId,
            Kind = "crate",
            Position = cell,
            Items = items,
        });
    }

    /// <summary>The stores a resident's present comes from: a hut's crate; an outpost's room crates (never the cellar's box).</summary>
    private IEnumerable<StoredContainer> LodgeGiftStores(LodgeInstance lodge)
    {
        if (lodge.Outpost is not { } o)
        {
            if (LodgeStore(lodge) is { } crate)
            {
                yield return crate;
            }

            yield break;
        }

        foreach (string id in o.StoreIds)
        {
            if (id != o.CellarStoreId && _containers.FirstOrDefault(c => c.Id == id) is { } c)
            {
                yield return c;
            }
        }
    }

    /// <summary>The refusal when a player mines an outpost's or a hut's protected cell — whose it is.</summary>
    private string LodgeProtectRefusal(Vector3i pos)
    {
        var lodge = LodgeAt(pos);
        return (lodge?.Outpost is not null ? "@srv.protect.outpost:" : "@srv.protect.lodge:") + (lodge?.ResidentName ?? string.Empty);
    }

    // ---------------- The pioneer ----------------

    /// <summary>The outpost's one resident (see the class summary), appended after the settlement NPCs.</summary>
    private void SpawnPioneer(LodgeInstance outpost, LodgeProfessionDefinition work)
    {
        if (_npcs.Any(n => n.LodgeKey == outpost.Key) || outpost.Outpost is not { } o)
        {
            return;
        }

        Vector3i? bed = null, seat = null;
        var rest = outpost.YardSpot;
        foreach (var (type, pos) in outpost.Markers)
        {
            switch (type)
            {
                case PioneerOutpostGenerator.MarkerBed:
                    bed ??= pos.ToBlock();
                    break;
                case PioneerOutpostGenerator.MarkerSeat:
                    seat ??= pos.ToBlock();
                    break;
                case PioneerOutpostGenerator.MarkerNpc:
                    rest = pos;
                    break;
            }
        }

        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("pioneer:" + _world.LocationId + ":" + outpost.Key))));
        var npc = MakeNpc("settler", "settlers", robotic: false, new Vector3f(rest.X, (float)System.Math.Floor(rest.Y), rest.Z), rng);
        npc.Job = PioneerJob;
        npc.NameKey = PioneerNameKey;
        npc.Look = PixelLook;
        npc.FaceSeed = o.SkinSeed;
        npc.Size = 1f; // a player's size — the skin does the rest
        npc.Held = work.Held;
        npc.LodgeKey = outpost.Key;
        npc.PersonKey = LodgePersonKey(outpost, 0);
        npc.Work = outpost.YardSpot;
        npc.HasWork = true;
        SetNpcBed(npc, bed);
        SetNpcSeat(npc, seat);
        MarkFurnitureLooked(npc);
        npc.RoutineEnabled = true;
        npc.WorkState = new NpcWorkState { Plan = PioneerWorkPlan(outpost, work) };
        _npcs.Add(npc);
        outpost.ResidentName = npc.Name;
        if (PioneerAwayAt(o.Seed, MarketDay, LocalDayFraction(outpost.Center)))
        {
            HidePioneer(o, npc); // away when the world loads: simply not there
        }
    }

    /// <summary>The pioneer's work round the outpost — the lodge residents' plans, never inside a lodge or outpost.</summary>
    private NpcWorkPlan PioneerWorkPlan(LodgeInstance outpost, LodgeProfessionDefinition work)
    {
        System.Action<string, int> bank = (item, count) => DepositIntoLodgeStore(outpost, item, count);
        System.Func<Vector3i, bool> allowed = c => !IsLodgeBlock(c);
        return work.Work switch
        {
            LodgeRules.WorkMine => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Mine,
                Center = outpost.YardSpot,
                Radius = PioneerMineRadius,
                Bank = bank,
                SiteAllowed = allowed,
                ActivityKey = work.ActivityKey,
                Held = work.Held,
            },
            LodgeRules.WorkHunt => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Hunt,
                Center = outpost.YardSpot,
                Radius = LodgeHuntRadius,
                Bank = bank,
                ActivityKey = work.ActivityKey,
                Held = work.Held,
            },
            _ => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Gather,
                Center = outpost.YardSpot,
                Radius = LodgeGatherRadius,
                Bank = bank,
                SiteAllowed = allowed,
                ActivityKey = work.ActivityKey,
                Held = work.Held,
            },
        };
    }

    /// <summary>Whether a pioneer is away now (#2682): on about a third of the in-game days — by the outpost's seed and the
    /// day — during the working hours <see cref="PioneerAwayFrom"/>..<see cref="PioneerAwayUntil"/> of the outpost's local
    /// day. Pure, so every server and every reload agrees.</summary>
    internal static bool PioneerAwayAt(long seed, long day, double localDayFraction)
        => PioneerAwayDay(seed, day) && localDayFraction >= PioneerAwayFrom && localDayFraction < PioneerAwayUntil;

    /// <summary>Whether an in-game day is one of the pioneer's days out (about one in three).</summary>
    internal static bool PioneerAwayDay(long seed, long day)
    {
        // The seed and the day mixed through a 64-bit finalizer (an FNV string hash's low bits are too regular for a % 3).
        unchecked
        {
            ulong x = ((ulong)seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)day * 0xC2B2AE3D27D4EB4FUL) ^ 0x2682UL;
            x ^= x >> 33;
            x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33;
            x *= 0xC4CEB9FE1A85EC53UL;
            x ^= x >> 33;
            return x % 3UL == 0;
        }
    }

    /// <summary>1 Hz (from <see cref="TickLodges"/>): every pioneer's trip takes a step.</summary>
    private void StepPioneers()
    {
        foreach (var l in _lodges)
        {
            if (l.Outpost is not { } o || _npcs.FirstOrDefault(n => n.LodgeKey == l.Key) is not { } npc)
            {
                continue;
            }

            StepPioneerTrip(l, o, npc, PioneerAwayAt(o.Seed, MarketDay, LocalDayFraction(l.Center)));
        }
    }

    /// <summary>One step of a pioneer's trip: leave when the schedule says so (walking off out of sight when watched), come
    /// back when it ends (walking in from out of sight when somebody is near the outpost). A talk holds everything.</summary>
    private void StepPioneerTrip(LodgeInstance l, PioneerOutpost o, ServerNpc npc, bool away)
    {
        if (_uptime < npc.TalkHoldUntil)
        {
            return;
        }

        switch (o.Trip)
        {
            case PioneerTrip.Home when away:
                if (!AnyPlayerWithin(npc.Pos, BaseGuestDespawnUnseenRange))
                {
                    HidePioneer(o, npc); // nobody sees it go
                }
                else
                {
                    BeginPioneerLeaving(l, o, npc);
                }

                break;
            case PioneerTrip.Leaving:
                StepPioneerLeaving(l, o, npc, away);
                break;
            case PioneerTrip.Away when !away:
                BringPioneerHome(l, o, npc);
                break;
            case PioneerTrip.Returning:
                StepPioneerReturning(l, o, npc, away);
                break;
        }
    }

    private void BeginPioneerLeaving(LodgeInstance l, PioneerOutpost o, ServerNpc npc)
    {
        StandUp(npc);
        if (npc.WorkState is { } ws)
        {
            ResetNpcWork(npc, ws); // a tree already coming down still comes down
        }

        npc.RoutineEnabled = false;
        npc.ActivityKey = PioneerExploringKey;
        npc.AttendUntil = 0;
        var watcher = JoinedInActiveWorld().Where(s => !s.Spectating && !InSpace(s.State.PlayerId))
            .OrderBy(s => WrapDistSq(s.State.Position, npc.Pos)).FirstOrDefault();
        o.TripBearing = watcher is not null && WrapDistSq(watcher.State.Position, npc.Pos) > 1.0
            ? BearingFrom(watcher.State.Position, npc.Pos)
            : BearingFrom(l.Center, npc.Pos);
        o.Trip = PioneerTrip.Leaving;
        o.TripSince = _uptime;
        PioneerWalkOn(o, npc);
        BroadcastNpcs();
    }

    private void PioneerWalkOn(PioneerOutpost o, ServerNpc npc)
    {
        foreach (float d in new[] { 36f, 30f, 24f, 18f, 12f })
        {
            if (GuestSpotAlong(npc.Pos, o.TripBearing, d) is { } spot)
            {
                SetNpcGoal(npc, spot, NpcArrival.None, 0.6f);
                return;
            }
        }

        StopGuestWalking(npc);
    }

    /// <summary>The base guests' leave rule: gone at the end of the walk when nobody is within 32, or after two minutes with
    /// nobody within 24; still watched at the end of the walk, on a little further. The working hours over before it got
    /// away: it turns round.</summary>
    private void StepPioneerLeaving(LodgeInstance l, PioneerOutpost o, ServerNpc npc, bool away)
    {
        if (!away)
        {
            BeginPioneerReturn(l, o, npc);
            return;
        }

        if (_uptime - o.TripSince > BaseGuestHardDespawnSeconds && !AnyPlayerWithin(npc.Pos, BaseGuestHardDespawnRange))
        {
            HidePioneer(o, npc);
            return;
        }

        if (npc.Goal is not null)
        {
            return;
        }

        if (!AnyPlayerWithin(npc.Pos, BaseGuestDespawnUnseenRange))
        {
            HidePioneer(o, npc);
            return;
        }

        if (GuestSpotAlong(npc.Pos, o.TripBearing, BaseGuestLeaveStep) is { } further)
        {
            SetNpcGoal(npc, further, NpcArrival.None, 0.6f);
        }
    }

    /// <summary>Out of the world for now: not sent, not moved, no routine, no work.</summary>
    private void HidePioneer(PioneerOutpost o, ServerNpc npc)
    {
        StandUp(npc);
        if (npc.WorkState is { } ws)
        {
            ResetNpcWork(npc, ws);
        }

        npc.VisitingAway = true;
        npc.RoutineEnabled = false;
        npc.Goal = null;
        npc.Path = null;
        npc.ActivityKey = string.Empty;
        o.Trip = PioneerTrip.Away;
        o.TripSince = _uptime;
        BroadcastNpcs();
    }

    /// <summary>The trip is over: with somebody near the outpost the pioneer walks in from out of sight, else it is simply home.</summary>
    private void BringPioneerHome(LodgeInstance l, PioneerOutpost o, ServerNpc npc)
    {
        if (AnyPlayerWithin(l.Center, PioneerReturnSeenRange) && PioneerReturnSpot(l, o) is { } spot)
        {
            npc.Pos = spot;
            npc.Home = spot;
            npc.VisitingAway = false;
            BeginPioneerReturn(l, o, npc);
            return;
        }

        npc.Pos = npc.Rest;
        npc.Home = npc.Rest;
        npc.VisitingAway = false;
        ArrivePioneerHome(o, npc);
    }

    private void BeginPioneerReturn(LodgeInstance l, PioneerOutpost o, ServerNpc npc)
    {
        npc.RoutineEnabled = false;
        npc.ActivityKey = PioneerExploringKey;
        o.Trip = PioneerTrip.Returning;
        o.TripSince = _uptime;
        SetNpcGoal(npc, l.YardSpot, NpcArrival.None, 1.5f);
        BroadcastNpcs();
    }

    private void StepPioneerReturning(LodgeInstance l, PioneerOutpost o, ServerNpc npc, bool away)
    {
        if (away)
        {
            BeginPioneerLeaving(l, o, npc); // a new day out began before it got home
            return;
        }

        bool there = npc.Goal is null || WrapDistSq(npc.Pos, l.YardSpot) <= 3.0 * 3.0;
        bool stuck = npc.PathFailures >= 2 || _uptime - o.TripSince > PioneerReturnTimeout;
        if (!there && stuck)
        {
            if (AnyPlayerWithin(npc.Pos, BaseGuestHardDespawnRange))
            {
                o.TripSince = _uptime; // watched: it keeps trying
                SetNpcGoal(npc, l.YardSpot, NpcArrival.None, 1.5f);
                return;
            }

            npc.Pos = l.YardSpot; // unseen: it got home somehow
            npc.Home = l.YardSpot;
            there = true;
        }

        if (there)
        {
            ArrivePioneerHome(o, npc);
        }
    }

    /// <summary>Back to the lodge resident's day: the routine takes over at its next check.</summary>
    private void ArrivePioneerHome(PioneerOutpost o, ServerNpc npc)
    {
        if (npc.Goal is { } yard)
        {
            // As ArriveAtGoal would: the yard it walked back to becomes home, so until the routine picks the next goal
            // the leash holds it there instead of pulling it back out to the return ring it appeared on.
            npc.Home = yard;
            npc.Leash = npc.GoalLeash;
        }

        npc.Goal = null;
        npc.Path = null;
        npc.ActivityKey = string.Empty;
        npc.RoutineEnabled = true;
        npc.Phase = NpcPhase.Unset;
        npc.PhaseCheckedAt = double.NegativeInfinity;
        o.Trip = PioneerTrip.Home;
        o.TripSince = _uptime;
        BroadcastNpcs();
    }

    /// <summary>Where a returning pioneer appears: standable open ground on the rings round the outpost, out of every
    /// player's sight where such a spot exists (it is simply there when they look), else in sight.</summary>
    private Vector3f? PioneerReturnSpot(LodgeInstance l, PioneerOutpost o)
    {
        int refY = l.GroundY + 1;
        int circ = _world.Circumference;
        double start = (o.Seed & 0xFF) / 256.0 * System.Math.PI * 2.0;
        Vector3f? seen = null;
        foreach (float radius in PioneerReturnRadii)
        {
            for (int k = 0; k < 12; k++)
            {
                double a = start + k * (System.Math.PI * 2.0 / 12);
                int x = WorldConstants.WrapX((int)System.Math.Floor(l.Center.X + System.Math.Cos(a) * radius), circ);
                int z = (int)System.Math.Floor(l.Center.Z + System.Math.Sin(a) * radius);
                if (!TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureWideGroundScan, out int feet)
                    || System.Math.Abs(feet - refY) > 10
                    || StandableSpot(x, feet, z) is not { } spot || BanditColumnRejected(x, feet, z)
                    || WandererSpotProtected(new Vector3i(x, feet, z)))
                {
                    continue;
                }

                if (!AnyPlayerSees(spot))
                {
                    return spot;
                }

                seen ??= spot;
            }
        }

        return seen;
    }

    private bool AnyPlayerSees(Vector3f spot)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, spot) <= 80.0 * 80.0
                && HasLineOfSight(s.State.Position, spot))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Talk ----------------

    /// <summary>The place an AI line says a lodge resident lives in — a hut, or a pioneer's outpost.</summary>
    private static string LodgeAiPlaceOf(ServerNpc npc)
        => npc.Job == PioneerJob ? "an outpost of their own out in the wild" : LodgeAiPlace;

    /// <summary>The situation of a lodge resident's AI dialogue opening.</summary>
    private static string LodgeAiSituationOf(ServerNpc npc)
        => npc.Job == PioneerJob
            ? "the speaker's own little outpost out in the wild — a closed base they built themselves, where they live, hunt and gather"
            : "the speaker's own little hut out in the wild, where they live and work";

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2681): the pioneer outposts of the active world.</summary>
    public IReadOnlyList<(string Key, Vector3i Origin, Vector3f Center, string WallKind, bool Hidden, IReadOnlyList<string> StoreIds,
        string CellarStoreId, int BoatCells, string Work, bool Revealed)> OutpostsForTest
        => _lodges.Where(l => l.Outpost is not null)
            .Select(l => (l.Key, l.Origin, l.Center, l.Outpost!.WallKind, l.Outpost.HiddenShaft, (IReadOnlyList<string>)l.Outpost.StoreIds.ToList(),
                l.Outpost.CellarStoreId, l.Outpost.Boat.Count, l.Profession, _meta.RevealedPois.Contains(l.RevealKey)))
            .ToList();

    /// <summary>Test/inspection (#2681): an outpost's markers in world space (the cellar's included).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> OutpostMarkersForTest(string key) => LodgeMarkersForTest(key);

    /// <summary>Test/inspection (#2681): an outpost's boat cells.</summary>
    public IReadOnlyList<Vector3i> OutpostBoatForTest(string key)
        => _lodges.FirstOrDefault(l => l.Key == key)?.Outpost?.Boat.ToList() ?? new List<Vector3i>();

    /// <summary>Test/inspection (#2682): an outpost's pioneer — id, name, role, job, nameplate, look, skin seed, person key, the
    /// trip and whether it is out of the world.</summary>
    public (int Id, string Name, string Role, string Job, string NameKey, string Look, int SkinSeed, string PersonKey, string Trip, bool Away)? PioneerForTest(string key)
    {
        var l = _lodges.FirstOrDefault(x => x.Key == key);
        var npc = _npcs.FirstOrDefault(n => n.LodgeKey == key);
        if (l?.Outpost is not { } o || npc is null)
        {
            return null;
        }

        return (npc.Id, npc.Name, npc.Role, npc.Job, npc.NameKey, npc.Look, npc.FaceSeed, npc.PersonKey, o.Trip.ToString(), npc.VisitingAway);
    }

    /// <summary>Test seam (#2681): whether a player base is founded at a cell (an outpost's decorative core founds none).</summary>
    public bool BaseFoundedAtForTest(Vector3i cell)
        => _bases.Any(b => b.Planet == _world.LocationId && b.Cell.X == cell.X && b.Cell.Y == cell.Y && b.Cell.Z == cell.Z);

    /// <summary>Test seam (#2682): one step of every pioneer's trip now.</summary>
    public void StepPioneersForTest() => StepPioneers();

    /// <summary>Test seam (#2682): whether the outpost's pioneer is away by the schedule right now.</summary>
    public bool PioneerAwayNowForTest(string key)
        => _lodges.FirstOrDefault(l => l.Key == key)?.Outpost is { } o
           && PioneerAwayAt(o.Seed, MarketDay, LocalDayFraction(_lodges.First(l => l.Key == key).Center));

    /// <summary>Test seam (#2682): the outpost's away-schedule seed.</summary>
    public long OutpostSeedForTest(string key) => _lodges.FirstOrDefault(l => l.Key == key)?.Outpost?.Seed ?? 0;

    /// <summary>Test seam (#2681): builds an outpost with its local origin at the given cell — the blocks written straight in
    /// (no terrain fitting, the cellar below, no boat unless <paramref name="boat"/> names one), then the instance, doors,
    /// stores and pioneer exactly as a stamped one gets them. Returns the outpost key.</summary>
    public string SpawnPioneerOutpostForTest(int x, int groundY, int z, long seed = 1, string? wallKind = null, bool? hidden = null,
        int chests = 0, string work = "hunter", string boat = "")
    {
        var job = _content.LodgeProfession(work) ?? throw new System.ArgumentException("unknown work " + work, nameof(work));
        var planet = _world.Planet;
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var outpost = PioneerOutpostGenerator.Generate(seed, surface, _content, wallKind, hidden, chests);
        var s = outpost.Structure;
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

        StampOutpostBelow(outpost, new Vector3i(x, groundY, z), groundY);
        StampOutpostBoat(boat);
        var p = new PlacedSettlement
        {
            Structure = s,
            Origin = new Vector3i(x, groundY, z),
            GroundY = groundY,
            Tier = "outpost",
            Ruined = false,
            OnIsland = false,
            Name = "outpost_test",
            Rng = new System.Random(7),
            Seat = "flat",
        };
        var lodge = MaterializeOutpost(p, outpost, job, _lodges.Count, seed, boat);
        RegisterDoors();
        BroadcastNpcs();
        return lodge.Key;
    }
}
