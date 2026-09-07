// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministic, seed-based chunk generator. Given a world seed, a <see cref="PlanetType"/>
/// and a <see cref="ChunkCoord"/> it always produces the same blocks, so the procedural
/// baseline never needs to be stored — only player deltas are persisted (see
/// technical requirements §11).
/// </summary>
public sealed partial class WorldGenerator
{
    private readonly long _worldSeed;
    private readonly GameContent _content;

    // The walkable east–west circumference of the world currently being generated (the noise circular domain
    // + the longitude wrap). Set per active world by the server; defaults to the standard size so tests and
    // any direct callers keep their 6000-block world.
    private int _circumference = WorldConstants.Circumference;

    public WorldGenerator(long worldSeed, GameContent content)
    {
        _worldSeed = worldSeed;
        _content = content;
    }

    /// <summary>The circumference this generator is currently producing terrain for.</summary>
    public int Circumference => _circumference;

    // True when the active body is an airless moon (item 33): its terrain is cratered even though its planet
    // TYPE may carry an atmosphere on a full-size planet. The asteroid type carries Cratered in data instead,
    // so it craters everywhere (incl. standalone queries). Set via SetWorldMode at world-load.
    private bool _crateredWorld;

    /// <summary>The current cratered-world flag (so a caller can save/restore it around a transient query
    /// for a different body, e.g. computing another body's landing pads).</summary>
    public bool Cratered => _crateredWorld;

    // The active world's planned landing pads. Pad terrain is FLATTENED at generation time (the landed
    // ship is a placed structure object, not stamped blocks — it needs level, clear ground). Set via
    // SetWorldMode whenever the active world changes; empty = no flattening (void worlds, tests).
    private IReadOnlyList<LandingPadFlatten> _landingPads = System.Array.Empty<LandingPadFlatten>();

    /// <summary>The active world's landing pads (so a caller can save/restore them like <see cref="Cratered"/>).</summary>
    public IReadOnlyList<LandingPadFlatten> LandingPads => _landingPads;

    // The active BODY's identity salt (#478): StableHash of its location id, mixed into PlanetSeed so every
    // celestial body rolls its own terrain character, flora/fauna rosters and structures. Without it every
    // world of the same planet TYPE was identical (same relief drama, same species, same settlement names).
    // 0 = legacy/unset (tests, callers that predate the overhaul) — those keep the per-type behaviour.
    private long _locationSalt;
    private string _locationId = string.Empty;

    /// <summary>The location id this generator is currently configured for (empty = none; per-type legacy).</summary>
    public string LocationId => _locationId;

    /// <summary>
    /// Configures ALL per-world mode state (circumference, airless-moon cratering, landing-pad flattening,
    /// body identity) in one call. This generator instance is shared across every resident world, and #424
    /// S13 showed the old individual setters invited asymmetric configuration — one path set circumference +
    /// pads but not cratered, another circumference + cratered but not pads, so correctness rested entirely
    /// on the single-active-world invariant. One all-or-nothing setter makes a full re-configure the only
    /// option. Callers re-apply it before every generate/query batch for a body (chunk gen itself stays on
    /// the single tick thread — this method is about complete state, not thread-safety).
    /// <paramref name="locationId"/> is the body's location id (#478): it salts the per-world rolls so two
    /// bodies of the same planet type are different worlds. Null/empty keeps the legacy per-type seeding.
    /// </summary>
    public void SetWorldMode(int circumference, bool cratered, IReadOnlyList<LandingPadFlatten>? landingPads,
        string? locationId = null, double frontierOreBoost = 1.0)
    {
        _circumference = circumference;
        _crateredWorld = cratered;
        _landingPads = landingPads ?? System.Array.Empty<LandingPadFlatten>();
        _locationId = locationId ?? string.Empty;
        _locationSalt = string.IsNullOrEmpty(locationId) ? 0L : StableHash(locationId);
        // Frontier scaling (#1122): outer star systems multiply their RARE-tier veins (RareTier ores
        // only) by this factor. 1.0 (every legacy caller, every home-system world) is a no-op — the
        // boost rides OUTSIDE the calibration, like the world-option ore factor, so the memoised
        // calibration cache needs no key extension.
        _frontierOreBoost = frontierOreBoost;
        InvalidateColumnCaches(); // #1526: the column memos assume a fixed world mode
    }

    /// <summary>Rare-vein multiplier for the CURRENT body (#1122), set per world via
    /// <see cref="SetWorldMode"/>. 1.0 = home/near systems and all legacy callers.</summary>
    private double _frontierOreBoost = 1.0;

    /// <summary>The currently configured frontier rare-vein multiplier (#1122) — exposed so save/restore
    /// callers can re-apply the COMPLETE mode state (#424 S13), like <see cref="LocationId"/>.</summary>
    public double FrontierOreBoost => _frontierOreBoost;

    // Continents (#704): only worlds CREATED with the flag may roll continental terrain — the offset
    // relocates the oceans wholesale, so existing galaxies must keep their coasts (WorldDescription
    // gate, the SystemVariance/AsteroidBelts pattern). False is the load-safe default everywhere.
    // Galaxy-global like the world-option factors, so it is set ONCE (server start / preview bake),
    // not per SetWorldMode call.
    private bool _continentsEnabled;

    /// <summary>Whether this generator currently applies the continents feature gate (#704).</summary>
    public bool ContinentsEnabled => _continentsEnabled;

    /// <summary>Enables the continents feature for this generator (#704) — from the save's
    /// <c>WorldDescription.TerrainContinents</c> on the server, from the join handshake on the client.
    /// Call BEFORE any chunk or height query, like <see cref="SetWorldOptionFactors"/>.</summary>
    public void SetContinentsEnabled(bool enabled)
    {
        _continentsEnabled = enabled;
        InvalidateColumnCaches(); // #1526
    }

    /// <summary>Volcanoes on every lava-core world + sea-mount lift (#1631) — from the save's
    /// <c>WorldDescription.LavaCoreVolcanoes</c>. Off = the #477 rule (watery breathable worlds, cones
    /// measured from the ground), so existing saves keep their terrain. Call BEFORE any height query.</summary>
    public void SetLavaCoreVolcanoes(bool enabled)
    {
        _lavaCoreVolcanoes = enabled;
        InvalidateColumnCaches();
    }

    private bool _lavaCoreVolcanoes;

    /// <summary>The terrain generation this world was created with (#1644, landscape-variety package) — from
    /// the save's <c>WorldDescription.TerrainGeneration</c> on the server, from the join handshake on the
    /// client. 0 = the classic generators only, so every existing save keeps its terrain; the per-world
    /// profile reads it as <c>w.Generation</c> and every generation-1 feature gates on it. Call BEFORE any
    /// height query, like the other mode setters.</summary>
    public void SetTerrainGeneration(int generation)
    {
        _terrainGeneration = generation;
        InvalidateColumnCaches();
    }

    /// <summary>The terrain generation currently applied (see <see cref="SetTerrainGeneration"/>).</summary>
    public int TerrainGeneration => _terrainGeneration;

    private int _terrainGeneration;

    /// <summary>The flattened pad surface height for a column, or null when it is not on a pad.</summary>
    private int? PadSurfaceAt(int worldX, int worldZ)
        => PadColumnAt(worldX, worldZ, out var pad, out int target) && target == pad.SurfaceY ? target : null;

    /// <summary>The pad a column belongs to and the height its ground is levelled to: the pad's surface
    /// over the pad proper, and — for an islet pad (#1453/#1620) — the level plateau out to the plateau
    /// radius, then a 2:1 beach slope falling away to the islet radius. Both islet rims are wobbled by
    /// ±<see cref="IsletRimWobble"/> blocks of seeded noise (never inside the reserved pad), so the island
    /// reads as a natural outline rather than a stamped disc. False when the column is on no pad at all.</summary>
    private bool PadColumnAt(int worldX, int worldZ, out LandingPadFlatten pad, out int target)
    {
        for (int i = 0; i < _landingPads.Count; i++)
        {
            var p = _landingPads[i];
            int dx = WorldConstants.WrapDeltaX(worldX - p.CenterX, _circumference);
            int dz = worldZ - p.CenterZ;
            int d2 = dx * dx + dz * dz;
            if (d2 <= p.Radius * p.Radius)
            {
                pad = p;
                target = p.SurfaceY;
                return true;
            }

            if (p.ClassicShape)
            {
                // The pre-generation-2 islet (#1453): a 1:1 beach slope from the pad rim out to the islet
                // radius, no plateau, no wobble — kept byte-for-byte for saves created with it (#1665).
                if (d2 <= p.IsletRadius * p.IsletRadius)
                {
                    pad = p;
                    target = p.SurfaceY - (int)System.Math.Ceiling(System.Math.Sqrt(d2) - p.Radius);
                    return true;
                }

                continue;
            }

            if (p.Islet && d2 <= (p.IsletRadius + IsletRimWobble) * (p.IsletRadius + IsletRimWobble))
            {
                long wobbleSeed = _worldSeed ^ StableHash("islet:" + p.CenterX + ":" + p.CenterZ);
                double dist = System.Math.Sqrt(d2) + (FbmT(wobbleSeed, worldX, worldZ, 14.0, octaves: 2) - 0.5) * 2.0 * IsletRimWobble;
                if (dist <= p.PlateauRadius)
                {
                    pad = p;
                    target = p.SurfaceY;
                    return true;
                }

                if (dist <= p.IsletRadius)
                {
                    pad = p;
                    target = p.SurfaceY - (int)System.Math.Ceiling((dist - p.PlateauRadius) * 0.5);
                    return true;
                }
            }
        }

        pad = default;
        target = 0;
        return false;
    }

    /// <summary>How far the islet's plateau and beach rims wander from their nominal radius (#1620).</summary>
    private const double IsletRimWobble = 3.0;

    /// <summary>Flora chance per plateau column outside the reserved pad on an islet (#1620) — a few tufts
    /// of the biome's own flora, not a meadow, so the pad stays readable from the air.</summary>
    private const double IsletFloraChance = 0.16;

    private const int PadFoundationDepth = 8; // plug caves this deep under a pad (no falling into one)

    /// <summary>Levels the landing pads inside a freshly generated chunk: everything above the pad's
    /// surface height becomes air (terrain bumps, trees, props, flora, stray water), the surface cell gets
    /// the column's natural surface block, and caves directly below are plugged so the pad never collapses
    /// into a cavern. Runs as a post-pass so every feature stamp is covered uniformly.</summary>
    private void FlattenLandingPads(PlanetType planet, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, long seed)
    {
        if (_landingPads.Count == 0)
        {
            return;
        }

        var calib = CalibFor(planet);
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        var (_, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var beachId = BeachBlockFor(planet);
        for (int lx = 0; lx < cs; lx++)
            for (int lz = 0; lz < cs; lz++)
            {
                int worldX = origin.X + lx;
                int worldZ = origin.Z + lz;
                if (!PadColumnAt(worldX, worldZ, out var pad, out int padY))
                {
                    continue;
                }

                // An islet (#1453/#1620) is a mound raised out of the sea: every water/air cell from the seabed
                // up to the levelled height becomes fill, the level plateau wears the biome's own surface
                // (grass where the world has grass) over beach-block fill, the 2:1 beach slope is beach block
                // through and through, the pad top is sheared clear like any pad, and the slope keeps
                // whatever sea still stands above its lower rim.
                bool islet = pad.Islet;
                bool slope = islet && padY < pad.SurfaceY;
                int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, padY);
                // A classic islet (#1665) is beach block through and through, like the worlds it was made for.
                var surfaceId = slope || pad.ClassicShape ? beachId : biomes[biomeIndex].Surface;
                var subSurfaceId = islet ? beachId : biomes[biomeIndex].Sub;

                for (int ly = 0; ly < cs; ly++)
                {
                    int worldY = origin.Y + ly;
                    if (worldY > padY)
                    {
                        if (!slope)
                        {
                            chunk.Set(lx, ly, lz, BlockId.Air); // shear off anything above the pad level
                        }
                    }
                    else if (worldY == padY)
                    {
                        chunk.Set(lx, ly, lz, surfaceId); // a natural, level pad surface
                    }
                    else if (islet)
                    {
                        var cell = chunk.Get(lx, ly, lz);
                        if (cell.IsAir || cell.Value == seaFluid.Value || cell.Value == waterId.Value)
                        {
                            chunk.Set(lx, ly, lz, subSurfaceId); // the mound stands on the seabed, not on water
                        }
                    }
                    else if (worldY >= padY - PadFoundationDepth && chunk.Get(lx, ly, lz).IsAir)
                    {
                        chunk.Set(lx, ly, lz, subSurfaceId); // plug caves directly under the pad
                    }
                }

                // A few tufts of the biome's flora on the islet plateau, off the reserved pad (#1620).
                if (islet && !slope && !planet.Void && !pad.ClassicShape)
                {
                    int fy = padY + 1 - origin.Y;
                    int pdx = WorldConstants.WrapDeltaX(worldX - pad.CenterX, _circumference);
                    int pdz = worldZ - pad.CenterZ;
                    bool offPad = pdx * pdx + pdz * pdz > pad.Radius * pad.Radius;
                    if (offPad && fy >= 0 && fy < cs
                        && Noise.Value01(seed + 9004, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < IsletFloraChance)
                    {
                        var tuft = FloraForSurface(planet, biomes[biomeIndex], seed, worldX, worldZ, surfaceId);
                        if (!tuft.IsAir)
                        {
                            chunk.Set(lx, fy, lz, tuft);
                        }
                    }
                }
            }
    }

    /// <summary>True when the world's sea is water (not lava) — the islet fallback (#1619) only raises sand
    /// out of water; a lava sea keeps the seabed shaft.</summary>
    public bool SeaIsWater(PlanetType planet)
    {
        var (level, fluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        return level != int.MinValue && !waterId.IsAir && fluid.Value == waterId.Value;
    }

    // World options (creation-time, from the save's WorldDescription): global factors on top of the
    // seeded per-world variation. 1.0 = unchanged; deterministic because they come from persisted meta.
    private double _floraFactor = 1.0;
    private double _oreFactor = 1.0;

    /// <summary>Sets the world-option generation factors (flora/tree density × ore richness). The server
    /// calls this once at start from the save's metadata, before any chunk generates.</summary>
    public void SetWorldOptionFactors(double floraFactor, double oreFactor)
    {
        _floraFactor = floraFactor;
        _oreFactor = oreFactor;
        InvalidateColumnCaches(); // #1526 (the column phase does not read them today — cheap insurance)
    }

    /// <summary>
    /// Stable string hash (FNV-1a) — unlike <c>string.GetHashCode</c> this is identical
    /// across platforms and runs, which determinism across client/server depends on.
    /// </summary>
    public static long StableHash(string s)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }

            return (long)h;
        }
    }

    // The type key AND the body salt (#478): every body of the same planet type used to share this seed —
    // and with it relief drama, cave/ore rolls, rosters and biome counts. The salt makes it per body.
    private long PlanetSeed(PlanetType planet) => _worldSeed ^ StableHash(planet.Key) ^ _locationSalt;

    /// <summary>The roster seed for this body — the world seed salted with the body identity (#478). The
    /// server-side roster consumers (flora/tree/creature name + species lookups) MUST use the same formula,
    /// or scanned names would disagree with what worldgen actually planted.</summary>
    public long RosterSeed => _worldSeed ^ _locationSalt;

    // --- Round-world (torus) noise wrappers: X periodic at the circumference, Z at the latitude period
    // (≈ circumference/2), so terrain/caves/ores are seamless when circumnavigating in ANY direction. ---

    /// <summary>This world's north–south wrap period (blocks).</summary>
    private int LatPeriod => WorldConstants.LatitudePeriodFor(_circumference);

    private double FbmT(long seed, double worldX, double worldZ, double scale, int octaves)
        => Noise.FbmTorus(seed, worldX, worldZ, _circumference, LatPeriod, scale, octaves);

    private double ValueT(long seed, double worldX, double worldY, double worldZ, double scaleX, double scaleY, double scaleZ)
        => Noise.ValueTorus(seed, worldX, worldY, worldZ, _circumference, LatPeriod, scaleX, scaleY, scaleZ);

    /// <summary>Canonical Z for per-column hash rolls (trees/flora/props), so stamps match across the Z seam.</summary>
    private int Wz(int worldZ) => WorldConstants.WrapZ(worldZ, _circumference);

    // Terrain archetypes: regional landform SHAPES — flats, rolling plains, hills, mountains, canyons,
    // plus (#576) plateau decks, extreme peaks and rift gorges. A world uses a seed-picked subset, varied
    // across the surface by a large-scale field, so areas read as flat / rolling / mountainous — and on
    // worlds whose subset drew the new entries, as terraced mesa country, jagged extremes or gorge lands.
    private const int TerrainArchetypeCount = 8;

    /// <summary>The generation-1 archetype pool (#1645): the classic eight plus moorland, knob-and-kettle and
    /// coastal cliffs. Generation-0 worlds keep drawing from the eight (the subset roll depends on the pool size).</summary>
    private const int TerrainArchetypeCountGen1 = 11;

    // --- Per-world wonder profile (#712): every feature GATE, world roll and lowered string that the
    // #698–#709 wave added is constant for a given world, yet was re-derived per COLUMN — string
    // allocations (ToLowerInvariant), Noise.Hash world rolls and repeated planet-key hashing in the
    // hottest function in the codebase (~2.6× slower chunk gen / calibration / CI). Resolved ONCE per
    // world here, cached like the calibration; per-column code reads booleans and doubles only. ---
    private sealed class WonderProfile
    {
        public long Seed;                  // PlanetSeed, resolved once (string hash)
        public TerrainGrain Grain;         // #700 per-world direction roll
        public ContinentProfile Continent; // #704
        public string Style = string.Empty; // TerrainStyle lowered ONCE
        public bool Cratered;
        public bool Volcanoes, Calderas, Massifs, TableMountains, Rifts, MegaRift, Escarpment;
        public bool Arches, SeaStacks, Hoodoos, OverhangLandmarks;
        public bool Travertine, Penitentes, Cenotes, Crevasses;
        public bool SaltPolygons, BasaltFields, Tunnels, Caverns, AnyBands;
        public bool HybridEligible;        // #703
        public double HybridA, HybridB;    // #703 rolled fade thresholds
        public int SkyTiers;               // #707 (0 on non-floating worlds)
        public int Generation;             // #1644 terrain generation of this world (0 = classic generators)
        public TerrainTag Tags;            // #1644 the planet type's terrain tags, resolved at content load

        // #1645 (generation 1): the styles laid out as regions (gen 0: the single type style, or empty), the
        // per-world relief wavelength (gen 0: the type's TerrainScale exactly), the resolved biomes' relief
        // multipliers (null = off) and the rare whole-planet baseline regimes.
        public string[] Styles = System.Array.Empty<string>();
        public double Scale;
        public double[]? ReliefMuls;
        public bool Tilted, Stepped, EquatorRidge;

        // #1646 (generation 1): landmark families, overhang bands and underground finds — all false on gen 0.
        public bool ShieldVolcanoes, ImpactBasins, GlacialTroughs, Yardangs, DrumlinFields, Inselbergs;
        public bool StarDunes, MudVolcanoes, SinkholeChains, Maars, MushroomRocks, GlacierTongues;
        public bool NaturalBridges, CoastalOverhangs, IceCornices, Geodes, Strata;

        // #1647 (generation 1): water / lava bodies and surface paints — all false on gen 0.
        public bool Marshes, Oases, HotSprings, CalderaLakes, Playas, DeckBands, Moss, DryBeds;

        // Terrain generation 3 (the landform completion package, part 1): the sea-floor family that proves the
        // sea-relative landmark rows, the ice band that proves the band material, and the underground river
        // reaches that prove the sub-surface fluid spans — all false below generation 3.
        public bool Seamounts, Icebergs, UndergroundRivers;

        // Terrain generation 3, part 2 — the rock landforms.
        public bool SlotCanyons, Aretes, ToothRows, DesertPavement, RockGates, MountainHalls, PetrifiedDunes, RainbowStrata;

        // Terrain generation 3, part 3 — the caves: dripstone in every tunnel and cavern of a wet karst / wetland world.
        public bool Dripstone;

        // Terrain generation 3, part 4 — the volcanic and desert landforms.
        public bool ObsidianFields, LavaFlows, Barchans, FrostPolygons;

        // Terrain generation 3, part 5 — wetlands and rivers.
        public bool RiverMorphology, Rias, FloatingMats, PeatBogs, Thermokarst;

        // Terrain generation 3, part 6 — the coast and the sea floor.
        public bool SeaArches, Blowholes, CausewayIslands, ReefRings, ReefFields, BlueHoles, SubmarineCanyons, Trenches;

        // Terrain generation 3, part 7 — ice as a volume.
        public bool Glaciers, IceSheets, HangingValleys, IceCaves, SheetCaves;

        /// <summary>Aligned with <see cref="ActivePaints"/>: the row's colour cycle, or null (generation 3).</summary>
        public LandmarkCycleFn?[] ActivePaintCycles = System.Array.Empty<LandmarkCycleFn?>();

        /// <summary>The landmark table rows active on this world, in precedence order (#1644) — what
        /// <see cref="SurfaceHeightUncached"/> loops instead of a hand-written if-chain.</summary>
        public LandmarkOffsetFn[] ActiveLandmarks = System.Array.Empty<LandmarkOffsetFn>();

        /// <summary>The active SEA-RELATIVE rows (generation 3), run after <see cref="ActiveLandmarks"/> and only
        /// once the sea level is known — never inside the calibration sample that computes it.</summary>
        public LandmarkOffsetFn[] ActiveSeaLandmarks = System.Array.Empty<LandmarkOffsetFn>();

        /// <summary>The active rows' surface repaints, table order (#1644); run by the column phase.</summary>
        public LandmarkPaintFn[] ActivePaints = System.Array.Empty<LandmarkPaintFn>();
    }

    // --- Landmark registration table (#1644): one row per landform family. Adding a family = one row here
    // plus its Has*/Offset methods in a partial file; SurfaceHeightUncached and ComputeColumn never change.
    // Row order IS the per-column precedence (first non-zero overlay wins), kept exactly as the former
    // if-chain (volcano > caldera > massif > table mountain > overhangs > travertine > penitentes > cenote >
    // crevasse > rift > mega-rift) so every existing world is byte-identical. ---

    /// <summary>A landmark family's per-column height overlay (blocks; 0 = the family has nothing here).</summary>
    private delegate double LandmarkOffsetFn(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ);

    /// <summary>A landmark family's optional surface repaint at a column (null = keep the block the biome and
    /// paint chain chose). Runs after the classic paints and before the ejecta rays.
    /// <paramref name="fillToY"/> (terrain generation 3): the lowest world Y the paint block also claims below
    /// the topsoil — every SOLID cell from the surface down to it becomes the paint block, before ores, strata
    /// and data caches (caves, tunnels and caverns still carve through it). <see cref="int.MinValue"/> = the
    /// topsoil only, the classic behaviour.</summary>
    private delegate BlockId? LandmarkPaintFn(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY);

    /// <summary>A paint row's optional colour CYCLE at a column (terrain generation 3): the blocks the paint
    /// fill lays down in <c>RainbowBandThickness</c>-thick bands parallel to the surface, top first, instead
    /// of the single paint block. Null = the single block. Consulted only when the row's paint hit.</summary>
    private delegate BlockId[]? LandmarkCycleFn(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ);

    private readonly struct LandmarkKind
    {
        public LandmarkKind(string name, System.Func<WonderProfile, bool> active, LandmarkOffsetFn offset, LandmarkPaintFn? paint = null,
            bool seaRelative = false, LandmarkCycleFn? cycle = null)
        {
            Name = name;
            Active = active;
            Offset = offset;
            Paint = paint;
            SeaRelative = seaRelative;
            Cycle = cycle;
        }

        public readonly string Name;
        public readonly System.Func<WonderProfile, bool> Active; // reads the profile's cached gate boolean
        public readonly LandmarkOffsetFn Offset;
        public readonly LandmarkPaintFn? Paint;
        public readonly LandmarkCycleFn? Cycle;

        /// <summary>Terrain generation 3: the row shapes the SEA FLOOR and needs the calibrated sea level. It
        /// runs after every classic row and never inside the calibration sample (the #1631 sea-mount rule,
        /// generalised), so the percentile it reads is never its own output. Its offset must be 0 on a dry
        /// world, 0 wherever the raw ground is not at least two below the sea, and must never lift the result
        /// above one below the sea — so the land/sea partition the calibration saw stays exactly that.</summary>
        public readonly bool SeaRelative;
    }

    private static readonly LandmarkKind[] LandmarkKinds =
    {
        new("volcano", w => w.Volcanoes, static (g, p, w, x, z) => g.VolcanoOffset(p, w.Seed, x, z)),
        new("caldera", w => w.Calderas, static (g, p, w, x, z) => g.CalderaOffset(w.Seed, x, z)),
        new("massif", w => w.Massifs, static (g, p, w, x, z) => g.MassifOffset(p, w.Seed, x, z)),
        new("table-mountain", w => w.TableMountains, static (g, p, w, x, z) => g.TableMountainOffset(w.Seed, x, z)),
        new("overhang", w => w.OverhangLandmarks, static (g, p, w, x, z) => g.OverhangGroundOffset(p, w, x, z)),
        new("travertine", w => w.Travertine,
            static (g, p, w, x, z) => g.TryGetTravertine(w.Seed, x, z, out double deckRise, out _) ? deckRise : 0.0),
        new("penitentes", w => w.Penitentes, static (g, p, w, x, z) => g.PenitenteRise(p, w.Seed, x, z)),
        new("cenote", w => w.Cenotes, static (g, p, w, x, z) => g.CenoteOffset(p, w.Seed, x, z)),
        new("crevasse", w => w.Crevasses, static (g, p, w, x, z) => g.CrevasseOffset(w.Seed, x, z)),
        new("rift", w => w.Rifts, static (g, p, w, x, z) => g.RiftOffset(w.Seed, x, z)),
        new("mega-rift", w => w.MegaRift, static (g, p, w, x, z) => g.MegaRiftOffset(w.Seed, x, z)),
        // Generation-1 families (#1646), appended after the classic rows in footprint order (largest first);
        // their gates are false on every generation-0 world, so the classic precedence is untouched.
        new("shield-volcano", w => w.ShieldVolcanoes, static (g, p, w, x, z) => g.ShieldVolcanoOffset(p, w.Seed, x, z)),
        new("impact-basin", w => w.ImpactBasins, static (g, p, w, x, z) => g.ImpactBasinOffset(w.Seed, x, z)),
        new("glacial-trough", w => w.GlacialTroughs, static (g, p, w, x, z) => g.GlacialTroughOffset(w.Seed, x, z)),
        new("yardangs", w => w.Yardangs, static (g, p, w, x, z) => g.YardangOffset(w, x, z)),
        new("drumlin-field", w => w.DrumlinFields, static (g, p, w, x, z) => g.DrumlinFieldOffset(w, x, z)),
        new("inselberg", w => w.Inselbergs, static (g, p, w, x, z) => g.InselbergOffset(p, w.Seed, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.InselbergPaint(p, w, x, z, out fill)),
        new("star-dunes", w => w.StarDunes, static (g, p, w, x, z) => g.StarDuneOffset(w.Seed, x, z)),
        new("mud-volcanoes", w => w.MudVolcanoes, static (g, p, w, x, z) => g.MudVolcanoOffset(w.Seed, x, z)),
        new("sinkhole-chain", w => w.SinkholeChains, static (g, p, w, x, z) => g.SinkholeChainOffset(w.Seed, x, z)),
        new("maar", w => w.Maars, static (g, p, w, x, z) => g.MaarOffset(w.Seed, x, z)),
        new("mushroom-rock", w => w.MushroomRocks, static (g, p, w, x, z) => g.MushroomStemOffset(p, w, x, z)),
        new("glacier-tongue", w => w.GlacierTongues, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.GlacierTonguePaint(w, x, z, y, out fill)),
        // Terrain generation 3 — sea-relative rows (SeaRelative: true). They run after every row above and only
        // once the sea level is known, so the percentile they read is never their own output; the gates are
        // false below generation 3.
        new("seamount", w => w.Seamounts, static (g, p, w, x, z) => g.SeamountOffset(p, w, x, z), seaRelative: true),
        // Part 2 — the rock landforms, appended after part 1 so no earlier precedence moves. The two
        // paint-only rows carry a zero offset, like the classic glacier tongue.
        new("slot-canyon", w => w.SlotCanyons, static (g, p, w, x, z) => g.SlotCanyonOffset(w.Seed, x, z)),
        new("arete", w => w.Aretes, static (g, p, w, x, z) => g.AreteOffset(p, w.Seed, x, z)),
        new("tooth-row", w => w.ToothRows, static (g, p, w, x, z) => g.ToothRowOffset(p, w.Seed, x, z)),
        new("desert-pavement", w => w.DesertPavement, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.DesertPavementPaint(p, w, x, z, out fill)),
        new("petrified-dunes", w => w.PetrifiedDunes, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.PetrifiedDunePaint(w, x, z, y, out fill)),
        new("rainbow-strata", w => w.RainbowStrata, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.RainbowStrataPaint(p, w, x, z, y, out fill),
            cycle: static (g, p, w, x, z) => g.RainbowStrataCycle(w, x, z)),
        // Part 4 — volcanic and desert. The lava flow starts where the cone's own row ends (its foot); the
        // frost net's ridge is a one-block heave with a stone skin; the obsidian field is a paint alone.
        new("lava-flow", w => w.LavaFlows, static (g, p, w, x, z) => g.LavaFlowOffset(p, w, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.LavaFlowPaint(p, w, x, z, y, out fill)),
        new("barchans", w => w.Barchans, static (g, p, w, x, z) => g.BarchanOffset(w, x, z)),
        new("frost-polygons", w => w.FrostPolygons, static (g, p, w, x, z) => g.FrostPolygonOffset(p, w, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.FrostPolygonPaint(p, w, x, z, out fill)),
        new("obsidian-field", w => w.ObsidianFields, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.ObsidianFieldPaint(p, w, x, z, y, out fill)),
        // Part 5 — wetlands and rivers. The ria is sea-relative (it drowns the shelf coast, so it must know the
        // sea); the thaw-pond rim is a one-block heave; the floodplain and the bog are paints (peat last, so a
        // bog on a floodplain is peat).
        new("ria", w => w.Rias, static (g, p, w, x, z) => g.RiaOffset(p, w, x, z), seaRelative: true),
        new("thermokarst", w => w.Thermokarst, static (g, p, w, x, z) => g.ThermokarstOffset(p, w, x, z)),
        new("floodplain", w => w.RiverMorphology, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.FloodplainPaint(p, w, x, z, out fill)),
        new("peat-bog", w => w.PeatBogs, static (g, p, w, x, z) => 0.0,
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.PeatBogPaint(p, w, x, z, y, out fill)),
        // Part 6 — the coast and the sea floor: every row sea-relative. Order = precedence among them: the small
        // sharp forms (arch stem, blue hole) before the broad ones, the reef ring before the reef field it may
        // stand in, the great cuts last.
        new("sea-arch", w => w.SeaArches, static (g, p, w, x, z) => g.SeaArchOffset(p, w, x, z), seaRelative: true),
        new("blue-hole", w => w.BlueHoles, static (g, p, w, x, z) => g.BlueHoleOffset(p, w, x, z), seaRelative: true),
        new("causeway-island", w => w.CausewayIslands, static (g, p, w, x, z) => g.CausewayIslandOffset(p, w, x, z, out _), seaRelative: true),
        new("reef-ring", w => w.ReefRings, static (g, p, w, x, z) => g.ReefRingOffset(p, w, x, z, out _),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.ReefRingPaint(p, w, x, z, y, out fill),
            seaRelative: true),
        new("reef-field", w => w.ReefFields, static (g, p, w, x, z) => g.ReefFieldOffset(p, w, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.ReefFieldPaint(p, w, x, z, y, out fill),
            seaRelative: true),
        new("submarine-canyon", w => w.SubmarineCanyons, static (g, p, w, x, z) => g.SubmarineCanyonOffset(p, w, x, z), seaRelative: true),
        new("trench", w => w.Trenches, static (g, p, w, x, z) => g.TrenchOffset(p, w, x, z), seaRelative: true),
        // Part 7 — ice as a volume (land rows, appended after every earlier land row: a massif or a trough owns its
        // column first, which is what makes a nunatak poke through the ice sheet).
        new("hanging-valley", w => w.HangingValleys, static (g, p, w, x, z) => g.HangingValleyOffset(w.Seed, x, z)),
        new("glacier", w => w.Glaciers, static (g, p, w, x, z) => g.GlacierOffset(p, w, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.GlacierPaint(p, w, x, z, y, out fill)),
        new("ice-sheet", w => w.IceSheets, static (g, p, w, x, z) => g.IceSheetOffset(w, x, z),
            static (WorldGenerator g, PlanetType p, WonderProfile w, int x, int z, int y, out int fill) => g.IceSheetPaint(w, x, z, y, out fill)),
    };

    /// <summary>The landmark families active on this world in precedence order (tests).</summary>
    internal string[] LandmarkOrderForTest(PlanetType planet)
    {
        var w = WonderFor(planet);
        var names = new System.Collections.Generic.List<string>();
        foreach (var k in LandmarkKinds)
        {
            if (k.Active(w))
            {
                names.Add(k.Name);
            }
        }

        return names.ToArray();
    }

    /// <summary>Every per-world feature gate by name (tests — the tag equivalence test compares them against
    /// the pre-#1644 key/style predicates for all planet types).</summary>
    internal System.Collections.Generic.Dictionary<string, bool> WonderGatesForTest(PlanetType planet)
    {
        var w = WonderFor(planet);
        return new System.Collections.Generic.Dictionary<string, bool>
        {
            ["volcanoes"] = w.Volcanoes,
            ["calderas"] = w.Calderas,
            ["massifs"] = w.Massifs,
            ["tableMountains"] = w.TableMountains,
            ["rifts"] = w.Rifts,
            ["arches"] = w.Arches,
            ["seaStacks"] = w.SeaStacks,
            ["hoodoos"] = w.Hoodoos,
            ["travertine"] = w.Travertine,
            ["penitentes"] = w.Penitentes,
            ["cenotes"] = w.Cenotes,
            ["crevasses"] = w.Crevasses,
            ["saltPolygons"] = w.SaltPolygons,
            ["basaltFields"] = w.BasaltFields,
            ["tunnels"] = w.Tunnels,
            ["caverns"] = w.Caverns,
            ["lavaRivers"] = LavaRiversFor(planet),
            ["lavaOceanContinents"] = LavaOceanContinentsFor(planet),
            ["geyserVolcanic"] = GeyserVolcanicFor(planet, w.Volcanoes),
            ["crystalProps"] = CrystalPropsFor(planet),
            // terrain generation 3
            ["seamounts"] = w.Seamounts,
            ["icebergs"] = w.Icebergs,
            ["undergroundRivers"] = w.UndergroundRivers,
            ["glacierTongues"] = w.GlacierTongues,
            ["slotCanyons"] = w.SlotCanyons,
            ["aretes"] = w.Aretes,
            ["toothRows"] = w.ToothRows,
            ["desertPavement"] = w.DesertPavement,
            ["rockGates"] = w.RockGates,
            ["mountainHalls"] = w.MountainHalls,
            ["petrifiedDunes"] = w.PetrifiedDunes,
            ["rainbowStrata"] = w.RainbowStrata,
            ["dripstone"] = w.Dripstone,
            ["obsidianFields"] = w.ObsidianFields,
            ["lavaFlows"] = w.LavaFlows,
            ["barchans"] = w.Barchans,
            ["frostPolygons"] = w.FrostPolygons,
            ["riverMorphology"] = w.RiverMorphology,
            ["rias"] = w.Rias,
            ["floatingMats"] = w.FloatingMats,
            ["peatBogs"] = w.PeatBogs,
            ["thermokarst"] = w.Thermokarst,
            ["seaArches"] = w.SeaArches,
            ["blowholes"] = w.Blowholes,
            ["causewayIslands"] = w.CausewayIslands,
            ["reefRings"] = w.ReefRings,
            ["reefFields"] = w.ReefFields,
            ["blueHoles"] = w.BlueHoles,
            ["submarineCanyons"] = w.SubmarineCanyons,
            ["trenches"] = w.Trenches,
            ["glaciers"] = w.Glaciers,
            ["iceSheets"] = w.IceSheets,
            ["hangingValleys"] = w.HangingValleys,
            ["iceCaves"] = w.IceCaves,
            ["sheetCaves"] = w.SheetCaves,
        };
    }

    /// <summary>Every gate a generation-3 family reads, by name (tests) — the "is any landform family active
    /// on this world at all" question the golden control group rests on.</summary>
    internal static readonly string[] Gen3GateNames =
    {
        "seamounts", "icebergs", "undergroundRivers", "slotCanyons", "aretes", "toothRows",
        "desertPavement", "rockGates", "mountainHalls", "petrifiedDunes", "rainbowStrata", "dripstone",
        "obsidianFields", "lavaFlows", "barchans", "frostPolygons",
        "riverMorphology", "rias", "floatingMats", "peatBogs", "thermokarst",
        "seaArches", "blowholes", "causewayIslands", "reefRings", "reefFields", "blueHoles", "submarineCanyons", "trenches",
        "glaciers", "iceSheets", "hangingValleys", "iceCaves", "sheetCaves",
    };

    // Static cross-instance cache (client bakes fresh generators per preview; tests spin up hundreds)
    // PLUS a lock-free instance fast path: a generator works one world at a time, so per-column lookups
    // almost always hit the instance slot and never touch the lock.
    private static readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool, bool, int), WonderProfile> _wonders = new();
    private static readonly object _wonderLock = new object();
    private static readonly System.Collections.Generic.Queue<(long, string, int, bool, long, bool, bool, int)> _wonderOrder = new();
    private WonderProfile? _wonderCached;
    private (long, string, int, bool, long, bool, bool, int) _wonderCachedKey;

    /// <summary>#1527: the bounded static caches evict their OLDEST entry instead of clearing wholesale, so a
    /// world past the cap only re-derives one entry, not every resident body's.</summary>
    private static void EvictOldest<TValue>(
        System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool, bool, int), TValue> cache,
        System.Collections.Generic.Queue<(long, string, int, bool, long, bool, bool, int)> order, int cap)
    {
        while (cache.Count >= cap && order.Count > 0)
        {
            cache.Remove(order.Dequeue()); // a key evicted earlier and re-inserted is simply gone already
        }

        if (cache.Count >= cap)
        {
            cache.Clear(); // unreachable unless the order queue and the cache disagree — keep the bound anyway
        }
    }

    // #1526: per-instance column memos. SurfaceHeight and the whole column phase of Generate are pure functions
    // of (world mode, planet, x, z) — the ~6 stacked chunks of a column, the tree/prop margins, the pond/river
    // probes and the server's far-column band all asked for the same columns again and again. Keyed on the
    // planet key + the raw (unwrapped) column; cleared by the world-mode setters and at the cap. Locked because
    // the client's minimap bakes on its own thread while a server instance may be shared.
    private readonly System.Collections.Generic.Dictionary<(string, long), int> _surfaceCache = new();
    private readonly System.Collections.Generic.Dictionary<(string, long), double> _forestCache = new();
    private readonly System.Collections.Generic.Dictionary<(string, long), ColumnProfile> _columnProfiles = new();
    private readonly object _columnLock = new object();
    private const int SurfaceCacheCap = 262_144;   // ~6 MB of entries at most
    private const int ColumnProfileCap = 100_000;  // ~12 MB: a VD-8 view is ~74k columns

    private static long ColumnKey(int worldX, int worldZ) => ((long)(uint)worldX << 32) | (uint)worldZ;

    /// <summary>Drops every per-column memo — the world-mode setters call this because the memos are keyed
    /// on the planet + column only and rely on the mode (circumference, cratered, body salt, continents,
    /// option factors) staying put in between.</summary>
    private void InvalidateColumnCaches()
    {
        lock (_columnLock)
        {
            _surfaceCache.Clear();
            _forestCache.Clear();
            _columnProfiles.Clear();
        }

        lock (_volcanoLock)
        {
            _volcanoCells.Clear(); // #1631: the sea-mount lift depends on the world mode + calibration
        }

        lock (_seaCellLock)
        {
            _seaCells.Clear(); // generation 3: the sea-relative families roll against the calibrated sea
        }
    }

    /// <summary>How many column profiles are memoised right now (tests).</summary>
    internal int CachedColumnProfiles
    {
        get
        {
            lock (_columnLock)
            {
                return _columnProfiles.Count;
            }
        }
    }

    private WonderProfile WonderFor(PlanetType planet)
    {
        var key = (_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt, _continentsEnabled, _lavaCoreVolcanoes, _terrainGeneration);
        if (_wonderCached is { } fast && _wonderCachedKey == key)
        {
            return fast;
        }

        lock (_wonderLock)
        {
            if (!_wonders.TryGetValue(key, out var w))
            {
                long seed = PlanetSeed(planet);
                w = new WonderProfile
                {
                    Seed = seed,
                    Grain = GrainFor(seed),
                    Continent = ContinentProfileFor(planet, seed),
                    Style = planet.TerrainStyle?.ToLowerInvariant() ?? string.Empty,
                    Cratered = planet.Cratered || _crateredWorld,
                    Volcanoes = HasVolcanoes(planet),
                    Calderas = HasCalderas(planet),
                    Massifs = HasMassifs(planet),
                    TableMountains = HasTableMountains(planet),
                    Rifts = HasRifts(planet),
                    MegaRift = HasMegaRift(planet, seed),
                    Escarpment = HasEscarpment(planet, seed),
                    Arches = HasArches(planet),
                    SeaStacks = HasSeaStacks(planet),
                    Hoodoos = HasHoodoos(planet),
                    Travertine = HasTravertine(planet),
                    Penitentes = HasPenitentes(planet),
                    Cenotes = HasCenotes(planet),
                    Crevasses = HasCrevasses(planet),
                    SaltPolygons = HasSaltPolygons(planet),
                    BasaltFields = HasBasaltFields(planet),
                    Tunnels = HasTunnels(planet),
                    Caverns = HasCaverns(planet),
                    SkyTiers = planet.FloatingIslands ? SkyTiersFor(seed) : 0,
                    Generation = _terrainGeneration,
                    Tags = planet.Tags,
                };
                w.OverhangLandmarks = w.Arches || w.SeaStacks || w.Hoodoos;

                // #1645 relief rolls: generation 0 takes the classic values verbatim (byte-identical worlds).
                w.Scale = planet.TerrainScale;
                w.Styles = w.Style.Length != 0 ? new[] { w.Style } : System.Array.Empty<string>();
                if (_terrainGeneration >= 1)
                {
                    w.Scale = ScaleJitterFor(planet, seed);
                    w.Styles = PickStyles(planet, seed, w.Style, _terrainGeneration);
                    w.ReliefMuls = ReliefMulsFor(planet);
                    w.Tilted = HasTilt(planet, seed);
                    w.Stepped = HasStepped(planet, seed);
                    w.EquatorRidge = HasEquatorRidge(planet, seed);
                    if (w.Stepped)
                    {
                        w.Escarpment = true; // three storeys = the classic escarpment plus a second one
                    }

                    // #1646 landmark families, bands and underground finds.
                    w.ShieldVolcanoes = HasShieldVolcanoes(planet, seed);
                    w.ImpactBasins = HasImpactBasins(planet);
                    w.GlacialTroughs = HasGlacialTroughs(planet);
                    w.Yardangs = HasYardangs(planet);
                    w.DrumlinFields = HasDrumlinFields(planet, w.Styles);
                    w.Inselbergs = HasInselbergs(planet);
                    w.StarDunes = HasStarDunes(planet);
                    w.MudVolcanoes = HasMudVolcanoes(planet);
                    w.SinkholeChains = HasSinkholeChains(planet);
                    w.Maars = HasMaars(planet);
                    w.MushroomRocks = HasMushroomRocks(planet);
                    w.GlacierTongues = HasGlacierTongues(planet);
                    w.NaturalBridges = HasNaturalBridges(planet);
                    w.CoastalOverhangs = HasCoastalOverhangs(planet);
                    w.IceCornices = HasIceCornices(planet);
                    w.Geodes = HasGeodes(planet);
                    w.Strata = HasStrata(planet);

                    // #1647 bodies + paints.
                    w.Marshes = HasMarshes(planet);
                    w.Oases = HasOases(planet);
                    w.HotSprings = HasHotSprings(planet);
                    w.CalderaLakes = HasCalderaLakes(planet);
                    w.Playas = HasPlayas(planet);
                    w.DeckBands = DeckStyleWorld(w.Styles);
                    w.Moss = MossWorld(planet);
                    w.DryBeds = DryBedWorld(planet);
                }

                if (_terrainGeneration >= 3)
                {
                    // The landform completion package, part 1: one reference family per structural extension.
                    w.Seamounts = HasSeamounts(planet);
                    w.Icebergs = HasIcebergs(planet);
                    w.UndergroundRivers = HasUndergroundRivers(planet);

                    // Part 2: the rock landforms.
                    w.SlotCanyons = HasSlotCanyons(planet);
                    w.Aretes = HasAretes(planet, w.Styles);
                    w.ToothRows = w.Aretes;
                    w.DesertPavement = HasDesertPavement(planet);
                    w.RockGates = HasRockGates(planet);
                    w.MountainHalls = HasMountainHalls(planet);
                    w.PetrifiedDunes = HasPetrifiedDunes(w.Styles);
                    w.RainbowStrata = HasRainbowStrata(planet);

                    // Part 3: the caves.
                    w.Dripstone = HasDripstone(planet);

                    // Part 4: the volcanic and desert landforms.
                    w.ObsidianFields = HasObsidianFields(planet);
                    w.LavaFlows = HasLavaFlows(planet);
                    w.Barchans = HasBarchans(planet, w.Styles);
                    w.FrostPolygons = HasFrostPolygons(planet);

                    // Part 5: wetlands and rivers.
                    w.RiverMorphology = HasRiverMorphology(planet);
                    w.Rias = HasRias(planet);
                    w.FloatingMats = HasFloatingMats(planet);
                    w.PeatBogs = HasPeatBogs(planet);
                    w.Thermokarst = HasThermokarst(planet);

                    // Part 6: the coast and the sea floor.
                    w.SeaArches = HasSeaArches(planet);
                    w.Blowholes = HasBlowholes(planet);
                    w.CausewayIslands = HasCausewayIslands(planet);
                    w.ReefRings = HasReefRings(planet);
                    w.ReefFields = HasReefFields(planet);
                    w.BlueHoles = HasBlueHoles(planet);
                    w.SubmarineCanyons = HasSubmarineCanyons(planet);
                    w.Trenches = HasTrenches(planet);

                    // Part 7: ice as a volume.
                    w.Glaciers = HasGlaciers(planet);
                    w.IceSheets = HasIceSheets(planet);
                    w.HangingValleys = HasHangingValleys(planet);
                    w.IceCaves = HasIceCaves(planet);
                    w.SheetCaves = HasSheetCaves(planet);
                }

                var offsets = new System.Collections.Generic.List<LandmarkOffsetFn>(LandmarkKinds.Length);
                var seaOffsets = new System.Collections.Generic.List<LandmarkOffsetFn>();
                var paints = new System.Collections.Generic.List<LandmarkPaintFn>();
                var cycles = new System.Collections.Generic.List<LandmarkCycleFn?>();
                foreach (var kind in LandmarkKinds)
                {
                    if (!kind.Active(w))
                    {
                        continue;
                    }

                    (kind.SeaRelative ? seaOffsets : offsets).Add(kind.Offset);
                    if (kind.Paint is { } paint)
                    {
                        paints.Add(paint);
                        cycles.Add(kind.Cycle);
                    }
                }

                w.ActiveLandmarks = offsets.ToArray();
                w.ActiveSeaLandmarks = seaOffsets.ToArray();
                w.ActivePaints = paints.ToArray();
                w.ActivePaintCycles = cycles.ToArray();
                w.AnyBands = planet.FloatingIslands || w.Arches || w.SeaStacks || w.Hoodoos || w.Cenotes
                    || w.NaturalBridges || w.CoastalOverhangs || w.IceCornices || w.MushroomRocks // #1646
                    || w.Icebergs;
                // #703 hybrid fade; #1645: on a multi-style world the fade runs whenever more than one style was
                // rolled — identity styles (flats, spires) stay pure only as the sole pick.
                w.HybridEligible = _terrainGeneration >= 1 && w.Styles.Length != 0
                    ? (w.Styles.Length > 1 || StyleHybridEligible(w.Styles[0]))
                    : StyleHybridEligible(w.Style);
                ulong uh = Noise.Hash(seed ^ 0x57FADE, 2, 4, 8);
                w.HybridA = 0.34 + (uh & 0xFF) / 255.0 * 0.08;
                w.HybridB = w.HybridA + 0.08;
                EvictOldest(_wonders, _wonderOrder, 256); // #1527: oldest-out, never the whole cache
                _wonders[key] = w;
                _wonderOrder.Enqueue(key);
            }

            _wonderCached = w;
            _wonderCachedKey = key;
            return w;
        }
    }

    /// <summary>Computes the surface height (world Y) of a column for a planet — the raw terrain plus at
    /// most ONE landmark overlay from the landmark table (#1644, <see cref="LandmarkKinds"/>): volcano cones
    /// (#477), massifs, table mountains, rift chasms (#577/#578) and the #698–#709 families. Table order is
    /// the precedence (volcano &gt; caldera &gt; massif &gt; butte &gt; … &gt; rift), one landmark per column, so a
    /// landmark's own summit/fluid helpers always anchor to ground no other landmark has moved.
    /// Everything that consumes terrain (rivers, settlements, pads, previews) goes through here, so
    /// every system sees the same mountain.</summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        var key = (planet.Key, ColumnKey(worldX, worldZ));
        lock (_columnLock)
        {
            if (_surfaceCache.TryGetValue(key, out int cached))
            {
                return cached; // #1526
            }
        }

        int h = SurfaceHeightUncached(planet, worldX, worldZ);
        lock (_columnLock)
        {
            if (_surfaceCache.Count >= SurfaceCacheCap)
            {
                _surfaceCache.Clear();
            }

            _surfaceCache[key] = h;
        }

        return h;
    }

    /// <summary>The memo-free surface height — what <see cref="SurfaceHeight"/> caches (tests compare the two).</summary>
    internal int SurfaceHeightUncached(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet); // #712: every gate is a cached boolean; #1644: the active rows are cached too
        int h = RawSurfaceHeight(planet, w, worldX, worldZ);
        double overlay = 0.0;
        var landmarks = w.ActiveLandmarks;
        for (int i = 0; i < landmarks.Length && overlay == 0.0; i++)
        {
            overlay = landmarks[i](this, planet, w, worldX, worldZ); // table order = precedence, first hit wins
        }

        // Sea-relative rows (terrain generation 3) fire only once the sea is known — never inside the
        // calibration sample, so the percentile they read is never their own output (the #1631 sea-mount
        // rule, generalised). Last in precedence: a classic landmark always owns its column.
        var seaRows = w.ActiveSeaLandmarks;
        if (overlay == 0.0 && seaRows.Length != 0 && !_calibrating && !DisableSeaRowsForTest)
        {
            for (int i = 0; i < seaRows.Length && overlay == 0.0; i++)
            {
                overlay = seaRows[i](this, planet, w, worldX, worldZ);
            }
        }

        if (overlay != 0.0)
        {
            h += (int)System.Math.Round(overlay);
        }

        return h > MaxNaturalSurfaceY ? MaxNaturalSurfaceY : h;
    }

    private BlockId ResolveBlock(string key)
    {
        var def = _content.GetBlock(key)
                  ?? throw new InvalidOperationException($"World generation references unknown block '{key}'.");
        return def.NumericId;
    }
}
