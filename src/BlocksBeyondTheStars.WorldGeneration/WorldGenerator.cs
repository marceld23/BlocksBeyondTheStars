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
public sealed class WorldGenerator
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
        string? locationId = null)
    {
        _circumference = circumference;
        _crateredWorld = cratered;
        _landingPads = landingPads ?? System.Array.Empty<LandingPadFlatten>();
        _locationId = locationId ?? string.Empty;
        _locationSalt = string.IsNullOrEmpty(locationId) ? 0L : StableHash(locationId);
    }

    /// <summary>The flattened pad surface height for a column, or null when it is not on a pad.</summary>
    private int? PadSurfaceAt(int worldX, int worldZ)
    {
        for (int i = 0; i < _landingPads.Count; i++)
        {
            var p = _landingPads[i];
            int dx = WorldConstants.WrapDeltaX(worldX - p.CenterX, _circumference);
            int dz = worldZ - p.CenterZ;
            if (dx * dx + dz * dz <= p.Radius * p.Radius)
            {
                return p.SurfaceY;
            }
        }

        return null;
    }

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
        for (int lx = 0; lx < cs; lx++)
            for (int lz = 0; lz < cs; lz++)
            {
                int worldX = origin.X + lx;
                int worldZ = origin.Z + lz;
                if (PadSurfaceAt(worldX, worldZ) is not int padY)
                {
                    continue;
                }

                int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, padY);
                var surfaceId = biomes[biomeIndex].Surface;
                var subSurfaceId = biomes[biomeIndex].Sub;

                for (int ly = 0; ly < cs; ly++)
                {
                    int worldY = origin.Y + ly;
                    if (worldY > padY)
                    {
                        chunk.Set(lx, ly, lz, BlockId.Air); // shear off anything above the pad level
                    }
                    else if (worldY == padY)
                    {
                        chunk.Set(lx, ly, lz, surfaceId); // a natural, level pad surface
                    }
                    else if (worldY >= padY - PadFoundationDepth && chunk.Get(lx, ly, lz).IsAir)
                    {
                        chunk.Set(lx, ly, lz, subSurfaceId); // plug caves directly under the pad
                    }
                }
            }
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

    // Terrain archetypes (amplitude multiplier, ridged amount): flats, rolling plains, hills, mountains,
    // canyons. A world uses a seed-picked subset, varied across the surface by a large-scale field, so areas
    // read as flat / rolling / mountainous and (high end) carve into canyons.
    private static readonly (double Amp, double Ridged)[] TerrainArchetypes =
    {
        (0.18, 0.0),  // flats
        (0.55, 0.0),  // rolling plains
        (1.00, 0.0),  // hills
        (1.90, 0.12), // mountains
        (1.30, 0.65), // canyons
    };

    /// <summary>Per-world terrain drama ("Welten reicher" W-R1): a seeded ~0.9–1.5× multiplier on the relief,
    /// so the same planet type rolls gentle on one world and jagged/dramatic on the next. Craters
    /// (airless regolith) stay flat by design.</summary>
    private static double DramaFor(long seed)
        => 0.9 + 0.6 * ((seed * 2654435761L >> 16 & 0x3FF) / 1023.0);

    /// <summary>Computes the surface height (world Y) of a column for a planet — the raw terrain plus, on
    /// watery worlds, the volcano cones (#477). Everything that consumes terrain (rivers, settlements,
    /// pads, previews) goes through here, so every system sees the same mountain.</summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        int h = RawSurfaceHeight(planet, worldX, worldZ);
        if (HasVolcanoes(planet))
        {
            double vo = VolcanoOffset(planet, PlanetSeed(planet), worldX, worldZ);
            if (vo != 0.0)
            {
                h += (int)System.Math.Round(vo);
            }
        }

        return h;
    }

    /// <summary>The terrain height WITHOUT the volcano overlay — the base field volcano geometry itself is
    /// anchored to (the crater's lava level derives from the pre-cone ground under the cone's centre).</summary>
    private int RawSurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        long seed = PlanetSeed(planet);
        double n = FbmT(seed, worldX, worldZ, planet.TerrainScale, octaves: 4);
        double h = (n - 0.5) * 2.0; // [-1, 1] base rolling terrain

        // Airless moons + landable asteroids (item 33): mostly flat regolith (a gentle undulation only — no
        // hills/mountains/canyons) pocked with round impact craters carved on top.
        if (planet.Cratered || _crateredWorld)
        {
            double flat = h * 0.30 * planet.Amplitude;
            return planet.BaseHeight + (int)System.Math.Round(flat + CraterCarve(seed, worldX, worldZ, planet));
        }

        double drama = DramaFor(seed); // W-R1: per-world relief multiplier (gentle ↔ dramatic)

        // A planet may dictate an overall terrain SHAPE (item 21 V2) so worlds read structurally different —
        // mesas, dunes, spires, etc. — instead of every world using the same mixed blend.
        if (!string.IsNullOrEmpty(planet.TerrainStyle))
        {
            return planet.BaseHeight + (int)System.Math.Round(StyledHeightOffset(planet, planet.TerrainStyle, seed, h, worldX, worldZ) * drama);
        }

        // Regional terrain character: a large-scale field selects how rugged this area is (a blend across the
        // world's archetype subset), so the surface varies between flat plains, hills, mountains and canyons.
        var (amp, ridged) = TerrainProfile(planet, seed, worldX, worldZ);
        if (ridged > 0.0)
        {
            double r = (1.0 - System.Math.Abs(h)) * 2.0 - 1.0; // ridged: turn smooth swells into sharp valleys/ridges
            h = h * (1.0 - ridged) + r * ridged;
        }

        return planet.BaseHeight + (int)System.Math.Round(h * planet.Amplitude * amp * drama);
    }

    /// <summary>The vertical band [<paramref name="bottom"/>..<paramref name="top"/>] of a floating sky island at
    /// this column (item 21 V5), or <c>false</c> if no island covers it. The single source of truth for the
    /// island mask — used by chunk generation AND by settlement placement, so both agree on island heights.</summary>
    public bool FloatingIslandBand(PlanetType planet, int worldX, int worldZ, out int top, out int bottom)
    {
        top = int.MinValue;
        bottom = int.MaxValue;
        if (!planet.FloatingIslands)
        {
            return false;
        }

        long seed = PlanetSeed(planet);
        double im = FbmT(seed + 0x15A4D, worldX, worldZ, planet.TerrainScale * 1.4, octaves: 3);
        if (im <= 0.60) // matches the chunk-gen coverage threshold
        {
            return false;
        }

        double t = (im - 0.60) / 0.40;       // 0..1 toward an island's centre
        double alt = FbmT(seed + 0x15A4E, worldX, worldZ, planet.TerrainScale * 3.0, octaves: 2);
        int center = planet.BaseHeight + 28 + (int)((alt - 0.5) * 24.0);
        int half = 2 + (int)(t * 8.0);       // 2..10 thick
        top = center + half;
        bottom = center - half - (int)(t * 6.0); // tapered rocky underside
        return true;
    }

    /// <summary>The TOP world-Y of a floating sky island at this column, or <see cref="int.MinValue"/> if none.</summary>
    public int FloatingIslandTop(PlanetType planet, int worldX, int worldZ)
        => FloatingIslandBand(planet, worldX, worldZ, out int top, out _) ? top : int.MinValue;

    // --- Volcanoes (#477, decision #6): watery worlds grow sparse basalt cones with a molten summit
    // crater — lava on worlds whose seas are water. The cone lives INSIDE SurfaceHeight so every consumer
    // sees the same mountain; the crater's lava pool is a per-column fluid override in Generate, the same
    // mechanism ponds and rivers already use. Seam-safe by construction: one hotspot cell grid over the
    // torus, centres kept a full cone radius inside their cell so no cone ever straddles a wrap seam. ---
    private const double VolcanoCellSize = 1280.0; // hotspot grid pitch (≈2–5 candidate cells on a default world)
    private const double VolcanoChance = 0.55;     // fraction of hotspot cells that actually grow a cone

    /// <summary>Volcanoes grow only on watery, breathable-atmosphere worlds (#477): lava/ashen worlds have
    /// their own lava seas + flows, airless/cratered bodies are geologically dead, skylands stay floaty.</summary>
    private bool HasVolcanoes(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return hasAir && waterAb > 0.0;
    }

    private readonly struct VolcanoCone
    {
        public readonly int CenterX;
        public readonly int CenterZ;
        public readonly double Radius;
        public readonly double Height;
        public readonly double CraterR;
        public readonly double CraterDepth;

        public VolcanoCone(int cx, int cz, double radius, double height)
        {
            CenterX = cx;
            CenterZ = cz;
            Radius = radius;
            Height = height;
            CraterR = System.Math.Max(4.0, radius * 0.16);
            CraterDepth = height * 0.55 + 4.0;
        }
    }

    /// <summary>The volcano cone covering (worldX, worldZ), if any — with the distance to its centre.
    /// Deterministic hotspot-cell lookup; a centre never sits closer than its radius to a cell border, so
    /// checking the containing cell alone is complete (and the cone can never straddle a wrap seam).</summary>
    private bool TryGetVolcano(PlanetType planet, long seed, int worldX, int worldZ,
        out VolcanoCone cone, out double dist)
    {
        cone = default;
        dist = 0.0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;

        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period; // canonical [0, period)
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));

        ulong h = Noise.Hash(seed ^ 0x70C4A0, cxI, 0, czI);
        if ((h & 0xFFFF) / 65536.0 >= VolcanoChance)
        {
            return false; // this hotspot cell grew no volcano
        }

        double radius = 34.0 + ((h >> 16) & 0x3FF) / 1023.0 * 26.0; // 34..60
        double height = 24.0 + ((h >> 26) & 0x3FF) / 1023.0 * 22.0; // 24..46
        double margin = radius + 24.0;
        double ox = margin + ((h >> 36) & 0x3FF) / 1023.0 * System.Math.Max(1.0, cw - 2.0 * margin);
        double oz = margin + ((h >> 46) & 0x3FF) / 1023.0 * System.Math.Max(1.0, ch - 2.0 * margin);
        int centerX = (int)(cxI * cw + ox);
        int centerZc = (int)(czI * ch + oz);

        double dx = WorldConstants.WrapDeltaX(wx - centerX, _circumference);
        double dz = zc - centerZc;
        if (dz > period / 2.0) dz -= period;
        if (dz < -period / 2.0) dz += period;
        dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return false;
        }

        cone = new VolcanoCone(centerX, centerZc - period / 2, radius, height);
        return true;
    }

    /// <summary>The cone's height contribution at a distance from its centre: a smooth basalt slope rising
    /// to the rim, with the summit crater carved back down toward the vent.</summary>
    private static double ConeOffsetOf(in VolcanoCone v, double dist)
    {
        double t = 1.0 - dist / v.Radius;
        double cone = v.Height * System.Math.Pow(t, 1.6);
        if (dist < v.CraterR)
        {
            double bt = (v.CraterR - dist) / v.CraterR;
            cone -= v.CraterDepth * (bt * bt * (3.0 - 2.0 * bt)); // smoothstep bowl down to the vent
        }

        return cone;
    }

    private double VolcanoOffset(PlanetType planet, long seed, int worldX, int worldZ)
        => TryGetVolcano(planet, seed, worldX, worldZ, out var v, out double dist) ? ConeOffsetOf(v, dist) : 0.0;

    /// <summary>The Y of the crater pool's topmost lava cell — anchored to the pre-cone ground under the
    /// cone's centre so the pool is flat regardless of how the base terrain undulates below the flanks.</summary>
    private int CraterLavaTop(PlanetType planet, in VolcanoCone v)
        => RawSurfaceHeight(planet, v.CenterX, v.CenterZ)
           + (int)System.Math.Round(v.Height - v.CraterDepth + v.CraterDepth * 0.45);

    /// <summary>True when this column lies inside a volcano's summit crater; outputs the molten pool's top
    /// cell Y. Shared by Generate and the placement/water helpers so they can never disagree (#477).</summary>
    public bool TryGetVolcanoCrater(PlanetType planet, int worldX, int worldZ, out int lavaTopY)
    {
        lavaTopY = 0;
        if (!HasVolcanoes(planet))
        {
            return false;
        }

        long seed = PlanetSeed(planet);
        if (!TryGetVolcano(planet, seed, worldX, worldZ, out var v, out double dist) || dist >= v.CraterR - 0.5)
        {
            return false;
        }

        lavaTopY = CraterLavaTop(planet, v);
        return true;
    }

    /// <summary>Height offset (blocks, added to BaseHeight) for a planet with an explicit <see cref="PlanetType.TerrainStyle"/>
    /// (item 21 V2). <paramref name="h"/> is the base FBM swell in [-1,1]. Each style reshapes it into a distinct
    /// landform so worlds look structurally different. Deterministic + seam-safe (all noise wraps on X).</summary>
    private double StyledHeightOffset(PlanetType planet, string style, long seed, double h, int worldX, int worldZ)
    {
        double amp = planet.Amplitude;
        double Ridge(double v) => (1.0 - System.Math.Abs(v)) * 2.0 - 1.0; // smooth swell → sharp ridge/valley

        switch (style.ToLowerInvariant())
        {
            case "flats":
                return h * amp * 0.22; // near-flat plains (salt flats, ocean floor, low islands)

            case "hills":
                return h * amp * 0.75; // gentle rolling hills

            case "mountains":
                {
                    double r = h * 0.25 + Ridge(h) * 0.75; // sharp, rugged
                    if (r > 0)
                    {
                        r = System.Math.Pow(r, 1.35); // W-R1 crest sharpening: flatter mid-slopes, prouder peaks
                    }

                    return r * amp * 1.9;
                }

            case "canyons":
                {
                    double r = h * 0.35 + Ridge(h) * 0.65;
                    if (r < 0)
                    {
                        r = -System.Math.Pow(-r, 0.8); // W-R1: broader, deeper canyon floors below the mesatops
                    }

                    return r * amp * 1.4; // deep ridged canyons + mesatops
                }

            case "mesa":
                {
                    // Terraced plateaus: quantise the height into flat decks separated by sharp cliffs, with a little
                    // roll on each deck so the tops aren't dead flat.
                    double raw = h * amp * 1.15;
                    double step = System.Math.Max(3.0, amp * 0.30);
                    double deck = System.Math.Floor(raw / step) * step;
                    double roll = FbmT(seed + 0x3E5A, worldX, worldZ, planet.TerrainScale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 2.0; // ±2-block texture on each deck
                }

            case "dunes":
                {
                    // Parallel wind-blown ridges: a ridged mid-frequency field laid over a gentle base.
                    double d = FbmT(seed + 0x0D0E, worldX, worldZ, planet.TerrainScale * 0.45, octaves: 2);
                    double ridged = 1.0 - System.Math.Abs(d * 2.0 - 1.0); // 0..1 dune crests
                    return h * amp * 0.25 + ridged * amp * 0.85;
                }

            case "spires":
                {
                    // Mostly flat ground studded with sparse tall thin spikes (crystal needles / alien towers).
                    double basep = h * amp * 0.22;
                    double mask = FbmT(seed + 0x591E, worldX, worldZ, planet.TerrainScale * 0.4, octaves: 2);
                    if (mask > 0.72)
                    {
                        double t = (mask - 0.72) / 0.28; // 0..1 toward the spike centre
                        return basep + t * t * amp * 2.6;
                    }

                    return basep;
                }

            default:
                return h * amp; // unknown style → plain base swell
        }
    }

    // --- impact-crater field (item 33): seam-safe round basins via an FBM mask (the B7 pond-mask approach),
    // each ringed by a raised ejecta rim. Pure noise → deterministic and wraps across the X seam. ---
    private const double CraterThreshold = 0.60;  // mask above this is inside a crater (upper tail → scattered)
    private const double CraterBand = 0.16;        // mask range from the rim (0) to the deepest centre (1)
    private const double CraterMaxDepth = 7.0;     // bowl depth at the centre (blocks)
    private const double CraterRimHeight = 2.0;    // raised ejecta lip at the crater edge (blocks)
    private const double CraterRimBand = 0.07;     // mask range just outside the rim where the lip fades to flat

    /// <summary>Height offset (blocks) for the impact-crater field at a column: a smooth bowl inside each basin
    /// (deepening toward its centre) ringed by a raised rim, scattered across otherwise-flat ground (item 33).</summary>
    private double CraterCarve(long seed, int worldX, int worldZ, PlanetType planet)
    {
        double mask = FbmT(seed + 0x6A17, worldX, worldZ, planet.TerrainScale * 1.7, octaves: 3);
        double d = mask - CraterThreshold;
        if (d >= 0.0)
        {
            // Inside the basin: a smooth bowl down to -CraterMaxDepth, with a rim lip right at the edge.
            double t = System.Math.Min(1.0, d / CraterBand);
            double bowl = -CraterMaxDepth * (t * t * (3.0 - 2.0 * t));         // smoothstep deepening
            double lip = CraterRimHeight * System.Math.Max(0.0, 1.0 - t * 4.0); // a lip at the edge, gone a quarter in
            return bowl + lip;
        }

        // Just outside the rim: the raised ejecta lip, peaking at the edge and fading back to flat ground.
        double o = System.Math.Min(1.0, -d / CraterRimBand);
        return CraterRimHeight * (1.0 - o);
    }

    // Rare metals exposed as small clumps on deep crater floors — the reward for exploring craters (item 33).
    private const double CraterFloorMinDepth = 4.0;     // only craters at least this deep host metal
    private const double CraterMetalRegion = 0.55;      // per-crater gate: only SOME craters are metal-bearing
    private const double CraterMetalThreshold = 0.58;   // clump mask (within a metal crater) → a few scattered lumps
    private static readonly string[] CraterFloorMetals =
    {
        "titanium_ore", "gold_ore", "platinum_ore", "cobalt_ore", "uranium_ore", "tungsten_ore", "neodymium_ore",
    };

    /// <summary>For a cratered world, the rare-metal block to expose at a surface crater-floor column if this
    /// crater is metal-bearing and a clump roll hits — else null. Only SOME craters carry metal, and then only a
    /// few small clumps on the deeper floor (item 33).</summary>
    private BlockId? CraterFloorMetal(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (CraterCarve(seed, worldX, worldZ, planet) > -CraterFloorMinDepth)
        {
            return null; // not a deep crater floor
        }

        // Per-crater gate: a coarse mask (larger than the crater spacing → ~constant within one crater, varying
        // between craters) leaves most craters bare and only some metal-bearing.
        double region = FbmT(seed + 0x51A2, worldX, worldZ, planet.TerrainScale * 3.5, octaves: 2);
        if (region < CraterMetalRegion)
        {
            return null; // this crater holds no metal
        }

        // Within a metal-bearing crater, a small-scale clump mask scatters a few lumps (high freq → tiny clumps).
        double clump = FbmT(seed + 0x51A3, worldX, worldZ, planet.TerrainScale * 0.22, octaves: 2);
        if (clump < CraterMetalThreshold)
        {
            return null;
        }

        int pick = (int)(Noise.Value01(seed + 0x51A4, WorldConstants.WrapX(worldX, _circumference), 5, Wz(worldZ))
                         * CraterFloorMetals.Length);
        if (pick >= CraterFloorMetals.Length)
        {
            pick = CraterFloorMetals.Length - 1;
        }

        return _content.GetBlock(CraterFloorMetals[pick])?.NumericId;
    }

    /// <summary>The terrain archetype blend for a column: a large-scale region field picks among the world's
    /// seed-chosen subset of archetypes (deterministic, seam-free across the X wrap) and blends neighbours.</summary>
    private (double Amp, double Ridged) TerrainProfile(PlanetType planet, long seed, int worldX, int worldZ)
    {
        int pool = TerrainArchetypes.Length;
        long s = seed ^ 0x7E44A1;
        ulong us = (ulong)(s < 0 ? -s : s);
        int count = 2 + (int)(us % (ulong)(pool - 1)); // this world uses 2..pool archetypes
        int rot = (int)((us >> 8) % (ulong)pool);       // …starting at a seed-rotated offset in the list

        // A broad field (much larger than the base terrain) picks a position across the subset + blends it.
        double rug = FbmT(s, worldX, worldZ, planet.TerrainScale * 6.0, octaves: 3);
        double pos = (rug < 0 ? 0 : (rug > 0.9999 ? 0.9999 : rug)) * count; // [0, count)
        int i0 = (int)pos;
        int i1 = i0 + 1 < count ? i0 + 1 : count - 1;
        double t = pos - i0;
        double f = t * t * (3.0 - 2.0 * t); // smoothstep blend between adjacent archetypes

        var a0 = TerrainArchetypes[(rot + i0) % pool];
        var a1 = TerrainArchetypes[(rot + i1) % pool];
        return (a0.Amp + (a1.Amp - a0.Amp) * f, a0.Ridged + (a1.Ridged - a0.Ridged) * f);
    }

    /// <summary>The world's surface sea level (world Y) — the height water/lava fills basins to, or
    /// int.MinValue if the world has no surface fluid. Used to keep aquatic creatures in the water.</summary>
    public int SeaLevel(PlanetType planet) => ResolveSeaFluid(planet).Level;

    // --- Per-world calibration (#472/#473/#476): measured once per world instead of hand-tuned constants.
    // The old sea formula guessed against the raw Amplitude while the height function scales it 0.18–1.9×
    // per style, so most watery worlds had NO sea at all and the ocean type drowned 99.99 % of its surface.
    // The old cave/ore thresholds were tuned against the original 3D noise; the torus sampler halved the
    // field's σ and pushed them unreachably far into the tail (caves + ore became corner speckle). Both are
    // the same class of bug — a constant assuming a distribution the field doesn't have — so both are fixed
    // the same way: sample the ACTUAL distribution once per world and place thresholds by quantile. ---
    private sealed class WorldCalibration
    {
        public int SeaLevel = int.MinValue;   // int.MinValue = dry world
        public BlockId SeaFluid;
        public int[] SortedHeights = System.Array.Empty<int>(); // coarse whole-world surface sample, sorted
        public int MinHeight, MaxHeight;
        public double CaveThreshold;          // quantile-calibrated (0 = caves disabled)
        public double[] OreCdf = System.Array.Empty<double>();  // sorted ore-field samples (empirical CDF)
        public int LavaTableDepth = int.MaxValue; // cave cells deeper than this fill with lava (#472/#477 L-A)
        public double BaseTemperature;        // planet base + per-world variation (°C) — worldgen-static part
        public double LapsePerBlock;          // °C lost per block above the reference altitude (#476)
        public int TempRefY;                  // reference altitude: sea level, else BaseHeight
    }

    // STATIC cache: the calibration is a pure function of (world seed, planet, circumference, cratered,
    // body salt), so it is safe — and important — to share across generator instances: the client bakes a
    // fresh WorldGenerator per minimap/orbit texture and the tests spin up hundreds, each of which would
    // otherwise re-sample ~17k heights + 2×4096 field points.
    private static readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long), WorldCalibration> _calibs = new();
    private static readonly object _calibLock = new object();

    private WorldCalibration CalibFor(PlanetType planet)
    {
        var key = (_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt);
        lock (_calibLock)
        {
            if (_calibs.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var calib = BuildCalibration(planet);
            if (_calibs.Count >= 64)
            {
                _calibs.Clear(); // soft cap — entries are ~150 KB each (the sorted height sample)
            }

            _calibs[key] = calib;
            return calib;
        }
    }

    private WorldCalibration BuildCalibration(PlanetType planet)
    {
        long seed = PlanetSeed(planet);
        var c = new WorldCalibration();
        double R01(long salt) => (double)((ulong)(seed ^ salt) % 10000UL) / 10000.0;

        // 1) Whole-world height sample (coarse but torus-complete) — the basis for the percentile sea level
        //    and the altitude normalisation. ~17k samples on the default world; cached per world afterwards.
        int period = LatPeriod;
        int stepX = System.Math.Max(8, _circumference / 188);
        int stepZ = System.Math.Max(8, period / 94);
        var hs = new System.Collections.Generic.List<int>((_circumference / stepX + 1) * (period / stepZ + 1));
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < _circumference; x += stepX)
                hs.Add(RawSurfaceHeight(planet, x, z));
        hs.Sort();
        c.SortedHeights = hs.ToArray();
        c.MinHeight = c.SortedHeights[0];
        c.MaxHeight = c.SortedHeights[c.SortedHeights.Length - 1];

        // 2) Sea level by height percentile (#473): waterAbundance now really means "roughly this fraction
        //    of the world floods" — on every terrain style and every drama roll. Ocean-class worlds
        //    (abundance ≥ 1) roll their land fraction per world instead (decision #3): some are near-solid
        //    water, some archipelagos. Water still beats lava; airless worlds stay dry.
        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        double lavaAb = planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0);
        if (waterAb > 0.0 && _content.GetBlock("water") is { } water)
        {
            double frac = waterAb >= 1.0
                ? 0.78 + 0.19 * R01(0x5EA01)   // ocean-class band: 78–97 % water (islands guaranteed)
                : System.Math.Clamp(0.06 + 0.40 * waterAb + (R01(0x5EA02) - 0.5) * 0.08, 0.02, 0.60);
            c.SeaLevel = QuantileLevel(c.SortedHeights, frac);
            c.SeaFluid = water.NumericId;
        }
        else if (lavaAb > 0.0 && _content.GetBlock("lava") is { } lava)
        {
            // Only dry volcanic/airless worlds pool a lava sea (B54: visible across low + mid terrain).
            double frac = System.Math.Clamp(0.30 * lavaAb + (R01(0x5EA03) - 0.5) * 0.06, 0.05, 0.55);
            c.SeaLevel = QuantileLevel(c.SortedHeights, frac);
            c.SeaFluid = lava.NumericId;
        }

        // 3) Cave threshold by field quantile (#472): the data's caveThreshold maps to a target carve
        //    fraction (lower data value = cavier world, as before), jittered per world, then converted to
        //    whatever raw threshold the ACTUAL torus field needs to carve that fraction.
        if (planet.CaveThreshold > 0.0)
        {
            double carve = System.Math.Clamp(0.5 * (0.90 - planet.CaveThreshold), 0.02, 0.18);
            carve = System.Math.Clamp(carve + (R01(0x0CA7E) - 0.5) * 0.06, 0.015, 0.22);
            var caveCdf = FieldSamplesSorted(seed + 7777, 22.0, 16.0, 22.0);
            c.CaveThreshold = caveCdf[(int)((1.0 - carve) * (caveCdf.Length - 1))];
        }

        // 4) Ore field CDF (#472): SelectOre turns each vein's rarity into a quantile of this, so `rarity`
        //    finally IS the kept fraction (the multiplier bumps never fixed this because the knob was broken).
        c.OreCdf = FieldSamplesSorted(seed + 100, 9.0, 9.0, 9.0);

        // 5) Deep lava table (#472/#477 L-A): carved cave cells below this depth fill with molten rock — the
        //    danger half to the now-reachable deep ore bands. Kept below the cave-fauna scan (surface−49).
        c.LavaTableDepth = 64 + (int)((ulong)(seed ^ 0x1A7AB1EL) % 65UL); // 64..128

        // 6) Altitude climate (#476, cosmetic by decision #7): a per-world temperature base + lapse. The
        //    reference altitude is the (repaired) sea level so "warm at the coast, frozen on the peaks".
        c.TempRefY = c.SeaLevel != int.MinValue ? c.SeaLevel : planet.BaseHeight;
        c.BaseTemperature = planet.BaseTemperature + (R01(0x7E3BL) - 0.5) * 12.0; // per-world ±6 °C
        c.LapsePerBlock = 0.5 + 0.3 * R01(0x1A65EL); // 0.5..0.8 °C per block — snow caps land on the
                                                     // upper third of a temperate world's peaks (measured)
        return c;
    }

    /// <summary>The sea level that floods ≈<paramref name="frac"/> of the sampled columns. Integer terrain
    /// heights tie heavily (the base-height plateau, mesa decks, flats), so a naive rank quantile can
    /// overshoot the target by half the world — instead, pick the candidate level whose ACTUAL flooded
    /// fraction P(surface &lt; L) lands closest to the target.</summary>
    private static int QuantileLevel(int[] sortedHeights, double frac)
    {
        int n = sortedHeights.Length;
        double target = System.Math.Clamp(frac, 0.0, 1.0);
        int best = sortedHeights[0]; // floods nothing
        double bestErr = target;
        int i = 0;
        while (i < n)
        {
            int v = sortedHeights[i];
            int j = i;
            while (j < n && sortedHeights[j] == v)
            {
                j++;
            }

            // Candidate level v+1 floods everything ≤ v, i.e. j/n of the sampled world.
            double err = System.Math.Abs((double)j / n - target);
            if (err < bestErr)
            {
                bestErr = err;
                best = v + 1;
            }

            i = j;
        }

        return best;
    }

    /// <summary>Sorted samples of a ValueT field over this world's domain — its empirical CDF. Thresholds
    /// derived from this stay meaningful no matter how many interpolation axes the torus sampler stacks.</summary>
    private double[] FieldSamplesSorted(long fieldSeed, double scaleX, double scaleY, double scaleZ)
    {
        const int N = 4096;
        var vals = new double[N];
        int period = LatPeriod;
        for (int i = 0; i < N; i++)
        {
            double u1 = Noise.Value01(fieldSeed ^ 0x5A11, i, 1, 0);
            double u2 = Noise.Value01(fieldSeed ^ 0x5A11, i, 2, 0);
            double u3 = Noise.Value01(fieldSeed ^ 0x5A11, i, 3, 0);
            double x = u1 * _circumference;
            double y = -600.0 + u2 * 680.0; // the depth band caves/ore actually occupy
            double z = -period / 2.0 + u3 * period;
            vals[i] = ValueT(fieldSeed, x, y, z, scaleX, scaleY, scaleZ);
        }

        System.Array.Sort(vals);
        return vals;
    }

    /// <summary>Air temperature (°C) at a world Y for this planet — the per-world base minus the altitude
    /// lapse above the reference level (sea level, else BaseHeight). Worldgen-static: the server layers
    /// weather + day/night on top (#476; temperature stays cosmetic by decision #7).</summary>
    public double AirTemperatureAt(PlanetType planet, int worldY)
    {
        var c = CalibFor(planet);
        // long math: int.MinValue is the "no position" sentinel and must not overflow into a hot reading.
        return c.BaseTemperature - c.LapsePerBlock * System.Math.Max(0L, (long)worldY - c.TempRefY);
    }

    /// <summary>Surface temperature (°C) of a column — its surface altitude fed through the lapse.</summary>
    public double SurfaceTemperatureAt(PlanetType planet, int worldX, int worldZ)
        => AirTemperatureAt(planet, SurfaceHeight(planet, worldX, worldZ));

    private static double TempAt(WorldCalibration c, int worldY)
        => c.BaseTemperature - c.LapsePerBlock * System.Math.Max(0, worldY - c.TempRefY);

    private const double SnowLineC = 0.0;   // below this surface temperature the ground gets a snow cover
    private const double IceLineC = -14.0;  // …and below this it freezes to solid ice
    private const double TreeLineC = -4.0;  // no trees / giant mushrooms above the tree line
    private const double FloraFadeHiC = 4.0, FloraFadeLoC = -8.0; // flora density ramps to zero across this band

    /// <summary>Flora density multiplier for the cold: 1 in the warm lowlands, fading to 0 toward the ice.</summary>
    private static double ColdFloraFactor(WorldCalibration c, int surfaceY)
    {
        double t = TempAt(c, surfaceY);
        return System.Math.Clamp((t - FloraFadeLoC) / (FloraFadeHiC - FloraFadeLoC), 0.0, 1.0);
    }

    /// <summary>The world's surface sea: which fluid fills its basins and up to what world-Y level (#473 —
    /// percentile-based, see <see cref="BuildCalibration"/>). Returns (int.MinValue, Air) for a dry world.</summary>
    private (int Level, BlockId Fluid) ResolveSeaFluid(PlanetType planet)
    {
        var c = CalibFor(planet);
        return (c.SeaLevel, c.SeaFluid);
    }

    // World floor (B46/B?): every world has a DEEP solid foundation (a few hundred to a couple thousand blocks,
    // varied per world) ending in an unmineable bedrock layer, so caves never open a hole you can fall out of
    // the bottom through. Just above the bedrock sits a boundary band — molten lava on real planets, basalt on
    // airless moons/asteroids — so digging all the way down ends in lava/rock, never a void.
    private const int WorldFloorMinDepth = 256;   // the shallowest a world's foundation ever bottoms out
    private const int WorldFloorMaxDepth = 2048;  // …the deepest (per-world, deterministic)
    private const int FloorBandThickness = 6;     // thickness of the lava/basalt boundary band above the bedrock

    /// <summary>This world's solid-foundation depth below the surface (deterministic per world) — many hundreds
    /// to a couple thousand blocks, so there is always a deep foundation and no way to fall out the bottom.</summary>
    private static int FloorDepthFor(long seed)
        => WorldFloorMinDepth + (int)((ulong)(seed ^ 0x466C6F6F72L) % (ulong)(WorldFloorMaxDepth - WorldFloorMinDepth + 1));

    private const int PondMaxDepth = 5;     // deepest carve at a pond's centre (≥2 is swimmable)
    private const double PondBand = 0.10;   // mask range from "rim" (depth 0) to "centre" (full depth)
    private const int PondMaxSlope = 4;     // only carve on flat ground (Δheight over ±2 in x+z) so water sits level

    // Rivers no longer use a noise band + slope gate; they are routed downhill into a sink by RiverNetwork /
    // RiverField (see RiverFieldFor below and docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md).

    /// <summary>Local terrain steepness at a column: the summed |Δheight| over ±2 blocks in x and z. 0 on a flat
    /// plain, growing with the grade. Used to gate flush-filled water bodies (ponds, rivers) to ground level
    /// enough that the water surface doesn't step into free-standing walls.</summary>
    private int SurfaceSlope(PlanetType planet, int worldX, int worldZ)
        => System.Math.Abs(SurfaceHeight(planet, worldX + 2, worldZ) - SurfaceHeight(planet, worldX - 2, worldZ))
         + System.Math.Abs(SurfaceHeight(planet, worldX, worldZ + 2) - SurfaceHeight(planet, worldX, worldZ - 2));

    /// <summary>Carve depth (0 = none) for an upland pond at this column: a low-frequency mask scatters ponds
    /// (sized by its peaks → small pools + occasional lakes), gated to flat ground so the water surface stays
    /// level. Deterministic — pure noise. The caller fills the carved bowl with water up to the original
    /// surface, so a pond reads as a swimmable pool flush with the surrounding terrain (B7).</summary>
    private int PondDepthAt(PlanetType planet, long seed, int worldX, int worldZ, double threshold)
    {
        double mask = FbmT(seed + 0x7A11, worldX, worldZ, planet.TerrainScale * 4.0, octaves: 3);
        double strength = (mask - threshold) / PondBand;
        if (strength <= 0.0)
        {
            return 0;
        }

        // No ponds anywhere on a volcano (#477): the crater is molten and the flanks are steep basalt —
        // checked here (the single source of truth) so Generate and SurfacePondDepth can never disagree.
        if (HasVolcanoes(planet) && TryGetVolcano(planet, seed, worldX, worldZ, out _, out _))
        {
            return 0;
        }

        // Flat-ground gate — sampled lazily, only inside the pond mask, so it doesn't cost on every column.
        if (SurfaceSlope(planet, worldX, worldZ) > PondMaxSlope)
        {
            return 0;
        }

        return (int)System.Math.Round(System.Math.Min(1.0, strength) * PondMaxDepth);
    }

    // --- Routed rivers (Phase 1): per-world memoized network + block-resolution placement field ---
    // A river is no longer a height-blind noise band. RiverNetwork traces every river downhill (steepest
    // descent + fill-and-spill lakes) to a guaranteed sink (the sea or a self-formed lake); RiverField then
    // rasterizes that to block columns whose water surface FOLLOWS the terrain (no floating wall) and which
    // carry a waterfall drop at steep steps. The whole thing is integer + seed-deterministic, so the client
    // rebuilds the identical field — no network snapshot. See the plan doc.
    // STATIC like the calibration cache: the field is a pure function of (world seed, planet, size,
    // cratered, body salt), and fresh generator instances (tests, client preview bakes) would otherwise
    // re-run the ~300 ms network build per instance.
    private static readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long), RiverField> _riverFields = new();
    private static readonly object _riverLock = new object();

    /// <summary>This world's routed river placement (built once per world, then cached). Empty on worlds that
    /// get no rivers (no water sea, or WaterAbundance below the river threshold).</summary>
    public RiverField RiverFieldFor(PlanetType planet)
    {
        var key = (_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt);
        lock (_riverLock)
        {
            if (_riverFields.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var field = BuildRiverField(planet);
            if (_riverFields.Count >= 8)
            {
                _riverFields.Clear(); // soft cap: only a handful of worlds are resident at once
            }

            _riverFields[key] = field;
            return field;
        }
    }

    private RiverField BuildRiverField(PlanetType planet)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        if (seaLevel == int.MinValue)
        {
            return RiverField.Empty(_circumference); // dry world: no sea, nothing to drain into
        }

        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);

        int period = WorldConstants.LatitudePeriodFor(_circumference);
        int Height(int x, int z) => SurfaceHeight(planet, x, z);
        long refArea = (long)(WorldConstants.Circumference / 16) * (WorldConstants.LatitudePeriodFor(WorldConstants.Circumference) / 16);
        long area = (long)(_circumference / 16) * (period / 16);
        double areaScale = area / (double)refArea;

        // WATER rivers: the wetter water worlds. Density scales with WaterAbundance + world area (Phase 4).
        // channelFlowThreshold 1 (#474): the headwaters (FlowAccum == 1) are stamped too, so a river has a
        // SOURCE instead of appearing abruptly at the first confluence; density is steered via sourceCount.
        if (seaFluid == waterId && !waterId.IsAir && pondAbundance >= 0.4)
        {
            double wetness = System.Math.Min(1.0, System.Math.Max(0.0, (pondAbundance - 0.4) / 0.6));
            int sources = System.Math.Max(8, (int)System.Math.Round((40 + 80 * wetness) * areaScale));
            var net = RiverNetwork.Build(PlanetSeed(planet), _circumference, period, seaLevel, Height, cellSize: 16, sourceCount: sources);
            return RiverField.Build(net, Height, _circumference, fillFluid: waterId,
                channelFlowThreshold: 1, fullWidthAccum: 8);
        }

        // LAVA rivers (L2): only the `lava` and `ashen` worlds (user decision). Magma is viscous, so the
        // channels are FEWER, WIDER and SHALLOWER than water brooks — thick flows creeping into the lava sea.
        bool lavaWorld = string.Equals(planet.Key, "lava", System.StringComparison.OrdinalIgnoreCase)
                      || string.Equals(planet.Key, "ashen", System.StringComparison.OrdinalIgnoreCase);
        if (lavaWorld && seaFluid == lavaId && !lavaId.IsAir)
        {
            int sources = System.Math.Max(6, (int)System.Math.Round(26 * areaScale));
            var net = RiverNetwork.Build(PlanetSeed(planet), _circumference, period, seaLevel, Height, cellSize: 16, sourceCount: sources);
            // channelFlowThreshold 1: magma flows are sparse, so every routed source path counts as a channel
            // (they rarely merge the way dense water tributaries do). fullWidthAccum 1 (#474): a lava flow
            // reaches full width without needing tributaries — the old absolute divisor kept every lava
            // channel at width 1 (FlowAccum never exceeds 1 on a lava world), making this tuning inert.
            return RiverField.Build(net, Height, _circumference, fillFluid: lavaId,
                channelFlowThreshold: 1, maxWidth: 9, fullWidthAccum: 1, maxLakeDepth: 4, estuaryWiden: 4);
        }

        return RiverField.Empty(_circumference);
    }

    /// <summary>Upland-pond carve depth (0 = none) at a surface column — the same scattered-water gate
    /// <see cref="Generate"/> applies (B7), but with this world's pond-enable, threshold and seed resolved
    /// internally so callers (tree placement, ship landing) can keep things out of the water without
    /// duplicating the rule. Returns 0 on worlds that have no water ponds (dry / lava / airless).</summary>
    public int SurfacePondDepth(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        if (!(pondAbundance > 0.15) || seaFluid != waterId || waterId.IsAir)
        {
            return 0; // ponds only on watery worlds (matches Generate)
        }

        if (SurfaceHeight(planet, worldX, worldZ) <= seaLevel)
        {
            return 0; // below the global sea — the sea fills this column, not a pond
        }

        double pondThreshold = 0.70 - pondAbundance * 0.12;
        return PondDepthAt(planet, PlanetSeed(planet), worldX, worldZ, pondThreshold);
    }

    /// <summary>River water depth (0 = none) at a surface column — resolved from the routed
    /// <see cref="RiverFieldFor"/> placement so callers (tree/prop placement, ship landing, aquatic life,
    /// client preview) and <see cref="Generate"/> can never disagree about where river water is. A pond takes
    /// precedence (matches Generate's pond-first order); the sea owns columns at/below sea level.</summary>
    public int SurfaceRiverDepth(PlanetType planet, int worldX, int worldZ)
    {
        if (SurfacePondDepth(planet, worldX, worldZ) > 0)
        {
            return 0; // a pond already claims this column (pond-first precedence)
        }

        // The global sea owns columns at/below sea level — Generate skips the river fill there, so we must
        // too — and a volcano crater's molten pool is never a river column (#477).
        int seaLevel = ResolveSeaFluid(planet).Level;
        if (SurfaceHeight(planet, worldX, worldZ) <= seaLevel || TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return 0;
        }

        if (RiverFieldFor(planet).TryGet(worldX, worldZ, out var col))
        {
            int depth = col.WaterSurfaceY - col.BedY;
            return depth >= 1 ? depth : 1;
        }

        return 0;
    }

    /// <summary>True if this surface column is under water — beneath the global water sea, inside an upland
    /// pond/lake (B7), or in a river channel. A lava sea is not "water" here. Used to keep ship landings out of
    /// the water (B36).</summary>
    public bool IsSurfaceWater(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        if (seaFluid == waterId && !waterId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel)
        {
            return true; // beneath the global sea
        }

        return SurfacePondDepth(planet, worldX, worldZ) > 0   // inside an upland pond
            || SurfaceRiverDepth(planet, worldX, worldZ) > 0; // …or a river channel
    }

    /// <summary>True if this surface column is under a LAVA sea — or inside a volcano's molten summit
    /// crater (#477) — so a ship landing avoids it too (B54), the same way it avoids water.</summary>
    public bool IsSurfaceLava(PlanetType planet, int worldX, int worldZ)
    {
        if (TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return true;
        }

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        return seaFluid == lavaId && !lavaId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel;
    }

    /// <summary>The local water column at a surface (x,z): true if water actually covers it — the global sea,
    /// an upland pond, or a river — returning the water-surface Y (topmost filled cell) and the seabed Y (last
    /// solid cell below the water). Mirrors what <see cref="Generate"/> fills, so the server can place and keep
    /// aquatic life in ANY water body, not just the deep global sea. False (with 0s) for dry/lava columns.</summary>
    public bool TryGetWaterSurface(PlanetType planet, int worldX, int worldZ, out int waterTopY, out int seabedY)
    {
        waterTopY = 0;
        seabedY = 0;

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        if (seaFluid != waterId || waterId.IsAir)
        {
            return false; // a lava/dry world has no water bodies
        }

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);

        // Global sea: terrain sits at/below the sea level, so water fills surfaceY+1 .. seaLevel.
        if (surfaceY + 1 <= seaLevel)
        {
            waterTopY = seaLevel;
            seabedY = surfaceY;
            return true;
        }

        // Upland pond: a carved bowl filled flush to the original surface (pond-first precedence).
        int pond = SurfacePondDepth(planet, worldX, worldZ);
        if (pond > 0)
        {
            waterTopY = surfaceY;
            seabedY = surfaceY - pond;
            return true;
        }

        // River: read the routed field's ABSOLUTE surface/bed (#469). A pooled reach sits ABOVE the local
        // terrain by design (that is what makes it a pool), so reconstructing the band from surfaceY put
        // the reported water into solid rock — and aquatic creatures spawned inside it.
        if (surfaceY > seaLevel && !TryGetVolcanoCrater(planet, worldX, worldZ, out _)
            && RiverFieldFor(planet).TryGet(worldX, worldZ, out var col))
        {
            waterTopY = col.WaterfallDrop > 0 ? col.WaterSurfaceY + col.WaterfallDrop : col.WaterSurfaceY;
            seabedY = col.BedY;
            return true;
        }

        return false;
    }

    /// <summary>The local LAVA column at a surface (x,z): a volcano crater pool (#477), the global lava
    /// sea, or a lava river/flow — with the melt-surface Y and the bed Y. The molten counterpart of
    /// <see cref="TryGetWaterSurface"/>, so lava fauna can spawn and stay IN lava (#470 F4).</summary>
    public bool TryGetLavaSurface(PlanetType planet, int worldX, int worldZ, out int lavaTopY, out int bedY)
    {
        lavaTopY = 0;
        bedY = 0;
        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        if (TryGetVolcanoCrater(planet, worldX, worldZ, out int craterTop))
        {
            lavaTopY = craterTop;
            bedY = surfaceY;
            return true;
        }

        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        if (lavaId.IsAir)
        {
            return false;
        }

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        if (seaFluid == lavaId && surfaceY + 1 <= seaLevel)
        {
            lavaTopY = seaLevel;
            bedY = surfaceY;
            return true;
        }

        if (surfaceY > seaLevel)
        {
            var field = RiverFieldFor(planet);
            if (field.FillFluid == lavaId && field.TryGet(worldX, worldZ, out var col))
            {
                lavaTopY = col.WaterfallDrop > 0 ? col.WaterSurfaceY + col.WaterfallDrop : col.WaterSurfaceY;
                bedY = col.BedY;
                return true;
            }
        }

        return false;
    }

    public ChunkData Generate(PlanetType planet, ChunkCoord coord)
    {
        var chunk = new ChunkData(coord);

        // Void worlds (orbital stations) are pure empty space — only their stamped structure exists.
        if (planet.Void)
        {
            return chunk; // all air
        }

        long seed = PlanetSeed(planet);

        var biomes = ResolveBiomes(planet);
        var deepId = ResolveBlock(planet.DeepBlock);
        var dataCacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;
        bool flora = planet.FloraDensity > 0;

        // Per-world flora richness (2026-06-10 — "belebte Planeten"): each world rolls its own seeded
        // multiplier (0.8..1.6, biased upward) on the planet type's flora + tree density, so the same type
        // can be sparse scrubland on one world and lush growth on the next. Deterministic from the world
        // seed (server + client preview agree); barren types (density 0) stay barren.
        double floraMul = (0.8 + 0.8 * Noise.Value01(seed + 0xF10A, 11, 23, 37)) * _floraFactor;
        double floraDensity = System.Math.Min(0.9, planet.FloraDensity * floraMul);

        // World floor (B46): an unmineable bedrock layer bounds the dig depth so a player can't fall forever.
        // On real planets a band of lava sits just above it; airless moons + asteroids get solid rock instead.
        var bedrockId = _content.GetBlock("bedrock")?.NumericId ?? deepId;
        var lavaFloorId = _content.GetBlock("lava")?.NumericId ?? bedrockId;
        var basaltFloorId = _content.GetBlock("basalt")?.NumericId ?? bedrockId;
        bool airlessBody = planet.Cratered || _crateredWorld;
        int floorDepth = FloorDepthFor(seed);
        var floorBandId = airlessBody ? basaltFloorId : lavaFloorId; // boundary band: basalt on airless, lava on planets

        // Per-world interior variety (item 21) + calibration (#472): cave threshold and ore CDF come from
        // the measured field distribution, richness and the mantle stay seeded rolls.
        var calib = CalibFor(planet);
        double caveThreshold = calib.CaveThreshold;
        int lavaTableDepth = calib.LavaTableDepth;
        double oreRichness = PerWorldOreRichness(seed) * _oreFactor;
        int mantleDepth = PerWorldMantle(seed, floorDepth, out var mantleId);

        // Altitude climate (#476): snow/ice above the world's snow line, tree/flora fades handled in the
        // stamps. Precomputed gate: hot flat worlds skip the per-column check entirely.
        var snowId = _content.GetBlock("snow")?.NumericId ?? BlockId.Air;
        var iceId = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
        bool hasAtmosphere = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        bool snowPossible = hasAtmosphere && !airlessBody && !snowId.IsAir
            && TempAt(calib, calib.MaxHeight) < SnowLineC + 2.0;

        // Volcanoes (#477): watery worlds may carry basalt cones with molten summit craters.
        bool volcanoWorld = HasVolcanoes(planet);
        var basaltId = _content.GetBlock("basalt")?.NumericId ?? BlockId.Air;
        var craterLavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;

        // Surface seas: water fills terrain basins on worlds with an atmosphere; lava fills them on
        // volcanic / airless worlds (never both). A higher abundance raises the sea level so more low
        // ground floods — the basin's depth + any rises become shallow water / deep water / islands.
        var (fluidLevel, fluidId) = ResolveSeaFluid(planet);

        // Trees: multi-block trunk + leaf crown on grass/earth ground (a small auto density on flora worlds).
        double treeDensity = (planet.TreeDensity ?? (flora ? 0.012 : 0.0)) * floraMul;
        var logId = _content.GetBlock("wood_log")?.NumericId ?? BlockId.Air;
        var leafId = _content.GetBlock("tree_leaves")?.NumericId ?? BlockId.Air;
        bool trees = treeDensity > 0.0 && !logId.IsAir && !leafId.IsAir;

        // Giant mushrooms (item 21 V3): towering capped fungi on fungal (mycelium-surface) worlds.
        var stemId = _content.GetBlock("mushroom_stem")?.NumericId ?? BlockId.Air;
        var capId = _content.GetBlock("mushroom_cap")?.NumericId ?? BlockId.Air;
        var myceliumId = _content.GetBlock("mycelium")?.NumericId ?? BlockId.Air;
        bool giantMushrooms = !stemId.IsAir && !capId.IsAir && !myceliumId.IsAir
            && biomes.Exists(b => b.Surface == myceliumId);

        bool floatingIslands = planet.FloatingIslands; // item 21 V5: drifting sky-island slabs above the surface

        // Geysers / vents (item 21 follow-up): sparse erupting spouts — water geysers on reasonably wet worlds,
        // steam/lava vents on volcanic/ashen worlds. A marker block at the surface; the client attaches the
        // eruption VFX + hiss when the player is near. Deterministic, very sparse (landmark-rare).
        var geyserVentId = _content.GetBlock("geyser_vent")?.NumericId ?? BlockId.Air;
        double geyserWater = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        bool geyserVolcanic = (planet.LavaAbundance ?? 0.0) > 0.0
            || string.Equals(planet.Key, "lava", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(planet.Key, "ashen", System.StringComparison.OrdinalIgnoreCase)
            || volcanoWorld; // #477 L-C: volcano worlds vent too (hot springs / fumaroles on watery worlds)
        bool geysers = !geyserVentId.IsAir && (geyserWater > 0.25 || geyserVolcanic);

        // Aquatic flora: seabed plants (kelp stalks / coral reefs / seagrass) + lily pads on the surface, only
        // where the sea is water (never lava). World gen places them directly in the submerged columns below.
        var kelpId = _content.GetBlock("flora_kelp")?.NumericId ?? BlockId.Air;
        var lilyId = _content.GetBlock("flora_lily")?.NumericId ?? BlockId.Air;
        var coralId = _content.GetBlock("flora_coral")?.NumericId ?? BlockId.Air;
        var seagrassId = _content.GetBlock("flora_seagrass")?.NumericId ?? BlockId.Air;
        var seaWaterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        ResolveFlora(planet); // pick this world's active flora subset (sets the aquatic-archetype flags)
        // Each active seabed archetype contributes its block; nothing is planted if none of them grow here.
        bool seabedFlora = (_kelpActive && !kelpId.IsAir) || (_coralActive && !coralId.IsAir) || (_seagrassActive && !seagrassId.IsAir);
        bool waterFlora = flora && fluidId == seaWaterId && !seaWaterId.IsAir
            && (seabedFlora || (_lilyActive && !lilyId.IsAir));

        // Upland ponds/lakes (B7): scattered, swimmable water ABOVE the sea on flat ground. Frequency derives
        // from the world's WaterAbundance — the same property that sets the sea level — so wet worlds get more
        // (and larger) ponds, dry worlds almost none, and lava/airless worlds get none (their sea isn't water).
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        bool ponds = pondAbundance > 0.15 && fluidId == seaWaterId && !seaWaterId.IsAir;
        // The mask is FBM noise (∈[0,1], clustered around 0.5), so the bar sits in its upper tail; a wetter
        // world lowers it for more/larger ponds. The flat-ground gate keeps them scattered (not everywhere).
        double pondThreshold = 0.70 - pondAbundance * 0.12;

        // Rivers (routed): a gefälle-aware network traced once per world (RiverFieldFor), guaranteed to flow
        // downhill into a sink (the sea or a self-formed lake). Empty on non-river worlds, so this is a cheap
        // O(1) lookup per column below. Replaces the old height-blind noise band + flat-ground gate.
        var riverField = RiverFieldFor(planet);

        var origin = WorldConstants.ChunkOrigin(coord);

        for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
            for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
            {
                int worldX = origin.X + lx;
                int worldZ = origin.Z + lz;
                int surfaceY = SurfaceHeight(planet, worldX, worldZ);

                // An upland pond carves a shallow bowl here (seabed below the terrain) and fills it with water up to
                // the original surface (a pond flush with the surrounding ground), so the column reads as a swimmable
                // pool. Normal columns leave seabed=surface and fill the sea up to the global level, unchanged.
                int seabedY = surfaceY;
                int waterTop = fluidLevel;
                var columnFluid = fluidId;
                bool pondHere = false;
                if (ponds && surfaceY > fluidLevel)
                {
                    int pondDepth = PondDepthAt(planet, seed, worldX, worldZ, pondThreshold);
                    if (pondDepth > 0)
                    {
                        seabedY = surfaceY - pondDepth;
                        waterTop = surfaceY;
                        columnFluid = seaWaterId;
                        pondHere = true;
                    }
                }

                // Volcano (#477): the summit crater overrides the column's fluid to a molten pool — the same
                // per-column mechanism ponds/rivers use — and the cone's flanks turn to basalt below.
                bool craterHere = false;
                double coneRise = 0.0;
                if (volcanoWorld && TryGetVolcano(planet, seed, worldX, worldZ, out var vCone, out double vDist))
                {
                    coneRise = ConeOffsetOf(vCone, vDist);
                    if (vDist < vCone.CraterR - 0.5)
                    {
                        craterHere = true;
                        seabedY = surfaceY;
                        waterTop = CraterLavaTop(planet, vCone);
                        columnFluid = craterLavaId;
                    }
                }

                // Rivers (routed): the RiverField places a channel whose water surface FOLLOWS the terrain — a
                // thin sheet on a flowing reach (no floating wall), the pooled level inside a capped lake, and at
                // a flagged step a vertical waterfall column poured into the lower reach. Skipped where a pond,
                // a volcano crater or the global sea already claims the column. The river bed is carved to BedY.
                if (!pondHere && !craterHere && surfaceY > fluidLevel && riverField.TryGet(worldX, worldZ, out var river))
                {
                    seabedY = river.BedY;
                    waterTop = river.WaterfallDrop > 0 ? river.WaterSurfaceY + river.WaterfallDrop : river.WaterSurfaceY;
                    columnFluid = riverField.FillFluid; // water on watery worlds, lava on lava/ashen worlds (L2)
                }

                // Per-column biome → surface/sub-surface blocks (single-biome worlds use index 0).
                int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, surfaceY);
                var biome = biomes[biomeIndex];
                var surfaceId = biome.Surface;
                var subSurfaceId = biome.Sub;

                // Altitude climate (#476): above the snow line the ground gets a snow cover, further up solid
                // ice. Dithered (±1.5 °C noise) so the line wanders naturally instead of cutting a contour.
                if (snowPossible && surfaceY > waterTop)
                {
                    double surfT = TempAt(calib, surfaceY)
                        + (FbmT(seed + 0x51F0, worldX, worldZ, 24.0, octaves: 2) - 0.5) * 3.0;
                    if (surfT < IceLineC && !iceId.IsAir)
                    {
                        surfaceId = iceId;
                        subSurfaceId = iceId;
                    }
                    else if (surfT < SnowLineC)
                    {
                        surfaceId = snowId;
                    }
                }

                // Volcano flanks read as dark volcanic rock wherever the cone meaningfully rises (#477) —
                // after the snow pass, so the warm basalt wins over a snow cap near the vent.
                if (coneRise > 3.0 && !basaltId.IsAir)
                {
                    surfaceId = basaltId;
                    subSurfaceId = basaltId;
                }

                // Floating islands (item 21 V5): a per-column sky-island slab high above the surface — a grass-topped
                // deck on a tapered rocky underbelly, scattered by a region mask, drifting in the air. The band is
                // resolved by the shared helper so settlement placement can query the same island tops.
                int islandTop = int.MinValue, islandBottom = int.MaxValue;
                if (floatingIslands)
                {
                    FloatingIslandBand(planet, worldX, worldZ, out islandTop, out islandBottom);
                }

                // Crater-floor metal clumps (item 33): on a cratered world, the top cells of a metal-bearing deep
                // crater floor are exposed rare ore instead of regolith (only some craters, a few clumps each).
                BlockId? craterMetal = (planet.Cratered || _crateredWorld)
                    ? CraterFloorMetal(planet, seed, worldX, worldZ) : (BlockId?)null;

                // Non-uniform topsoil: this column's surface/sub-surface layer thickness (varies per column, not a
                // flat band) so the stone/ore boundary undulates and reaches close to the surface in the thin spots.
                int effSurfaceDepth = VariedSurfaceDepth(planet, seed, worldX, worldZ);

                for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                {
                    int worldY = origin.Y + ly;
                    if (worldY > seabedY)
                    {
                        if (worldY <= waterTop)
                        {
                            chunk.Set(lx, ly, lz, columnFluid); // sea fill in a basin, or an upland pond above it
                        }
                        else if (floatingIslands && worldY >= islandBottom && worldY <= islandTop)
                        {
                            // A sky island: grass-topped deck, sub-surface just under it, stone underbelly below.
                            var ib = worldY == islandTop ? surfaceId : (worldY >= islandTop - 2 ? subSurfaceId : deepId);
                            chunk.Set(lx, ly, lz, ib);
                        }

                        continue; // else air above the surface
                    }

                    int depth = seabedY - worldY;

                    // Unmineable world floor (B46/B?): solid bedrock at the very bottom of this world's deep
                    // foundation (no caves carved through it), with a boundary band just above — molten lava on real
                    // planets, basalt on airless moons/asteroids — so digging all the way down ends in lava/rock,
                    // never a void you can fall out of.
                    if (depth >= floorDepth)
                    {
                        chunk.Set(lx, ly, lz, bedrockId);
                        continue;
                    }

                    if (depth >= floorDepth - FloorBandThickness)
                    {
                        chunk.Set(lx, ly, lz, floorBandId);
                        continue;
                    }

                    // Carve caves below the surface layer (quantile-calibrated per world, #472).
                    if (caveThreshold > 0.0 && depth > 1)
                    {
                        double cave = ValueT(seed + 7777, worldX, worldY, worldZ, 22.0, 16.0, 22.0);
                        if (cave > caveThreshold)
                        {
                            // Below the world's lava table a carved cell fills with molten rock instead of
                            // air (#472/#477 L-A): the deep ore bands are now reachable — and dangerous.
                            // Airless bodies stay dry (their floor band is basalt for the same reason).
                            if (!airlessBody && depth > lavaTableDepth)
                            {
                                chunk.Set(lx, ly, lz, lavaFloorId);
                            }

                            continue; // cave => air (or the lava pocket above)
                        }
                    }

                    BlockId block;
                    if (craterMetal.HasValue && depth <= 1)
                    {
                        block = craterMetal.Value; // a rare-metal clump on the crater floor (top two cells)
                    }
                    else if (depth < effSurfaceDepth)
                    {
                        block = depth == 0 ? surfaceId : subSurfaceId;
                    }
                    else
                    {
                        // Deep crust turns to a dark basalt mantle below this world's mantle depth (item 21), so the
                        // interior isn't one uniform stone column on every world. Ores still vein through it.
                        var rock = depth >= mantleDepth ? mantleId : deepId;
                        block = SelectOre(planet, calib, seed, worldX, worldY, worldZ, depth, fallback: rock, oreRichness);

                        if (block == rock && planet.DataCacheRarity > 0 && !dataCacheId.IsAir)
                        {
                            double r = Noise.Value01(seed + 4242, WorldConstants.WrapX(worldX, _circumference), worldY, Wz(worldZ));
                            if (r < planet.DataCacheRarity)
                            {
                                block = dataCacheId;
                            }
                        }
                    }

                    chunk.Set(lx, ly, lz, block);
                }

                // Surface flora: one plant in the air cell directly above the surface (bounded — one per column,
                // no spreading), chosen by biome surface + a density roll. Columns that lie under the sea grow
                // aquatic flora instead (kelp + lily pads); land plants don't grow underwater.
                if (flora && seabedY + 1 > waterTop)
                {
                    var floraId = FloraForSurface(planet, biome, seed, worldX, worldZ);
                    int fy = seabedY + 1;
                    int fly = fy - origin.Y;
                    // Local density is modulated by a vegetation-richness mask (lush forest floors / meadows vs
                    // sparse open ground) + the per-biome density, so undergrowth gathers into thickets instead
                    // of an even sprinkle — and the same forest the trees cluster in is also carpeted with plants.
                    // The cold factor (#476) thins growth toward the snow line and stops it at the ice.
                    double localFloraDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ)
                        * ColdFloraFactor(calib, surfaceY);
                    if (!floraId.IsAir && fly >= 0 && fly < WorldConstants.ChunkSize
                        && Noise.Value01(seed + 9001, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < localFloraDensity)
                    {
                        chunk.Set(lx, fly, lz, floraId);
                    }
                }
                else if (waterFlora && columnFluid == seaWaterId && seabedY + 1 <= waterTop)
                {
                    // Submerged WATER column — the sea or an upland pond grows seabed plants / lily pads.
                    // The column-fluid check keeps kelp out of lava rivers and volcano craters (#477).
                    StampWaterFlora(chunk, origin, lx, lz, seed, worldX, worldZ, seabedY, waterTop,
                        kelpId, lilyId, coralId, seagrassId, floraDensity);
                }

                // Sky islands grow their own surface flora on top — a floating meadow, not a bare slab.
                if (flora && islandTop != int.MinValue)
                {
                    var isleFlora = FloraForSurface(planet, biome, seed, worldX, worldZ);
                    int ify = islandTop + 1 - origin.Y;
                    double isleDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ);
                    if (!isleFlora.IsAir && ify >= 0 && ify < WorldConstants.ChunkSize
                        && Noise.Value01(seed + 9002, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < isleDensity)
                    {
                        chunk.Set(lx, ify, lz, isleFlora);
                    }
                }
            }

        if (trees)
        {
            StampTrees(planet, seed, chunk, coord, biomes, logId, leafId, treeDensity, fluidLevel);
        }

        if (giantMushrooms)
        {
            StampGiantMushrooms(planet, seed, chunk, coord, biomes, stemId, capId, myceliumId, fluidLevel);
        }

        if (geysers)
        {
            StampGeysers(planet, seed, chunk, coord, geyserVentId, fluidLevel);
        }

        // Set-dressing ("Welten reicher" W-R2): sparse scatter props that break the flat-grid monotony —
        // boulder clusters of the world's own rock, crystal shard outcrops on crystal-bearing worlds, and
        // bare dead trees on dry atmospheric worlds. Existing blocks only; nothing carves terrain.
        if (!planet.Void)
        {
            var boulderId = ResolveBlock(planet.DeepBlock);
            var crystalId = _content.GetBlock("crystal")?.NumericId ?? BlockId.Air;
            bool crystalWorld = !crystalId.IsAir
                && (planet.Key.Contains("crystal") || planet.Ores.Exists(o => o.Block == "crystal") || planet.CaveThreshold > 0.62);
            bool dryWorld = (planet.WaterAbundance ?? 0.55) <= 0.15 && !planet.IsAirless && !logId.IsAir;
            StampSetDressing(planet, seed, chunk, coord, boulderId, crystalWorld ? crystalId : BlockId.Air,
                dryWorld ? logId : BlockId.Air, fluidLevel);
        }

        // Landing pads (ship-as-object): level + clear the planned pad areas so the placed ship structure
        // always sits on flat, solid, vegetation-free ground.
        FlattenLandingPads(planet, chunk, coord, biomes, seed);

        return chunk;
    }

    /// <summary>Stamps sparse scatter props ("Welten reicher" W-R2): boulder clusters (the world's deep rock),
    /// crystal shard outcrops, and bare dead trees — per-column deterministic rolls with a margin scan so a
    /// prop straddling a chunk edge generates identically from either side. Props sit ON the surface
    /// (air cells only) and never spawn in seas/ponds.</summary>
    private void StampSetDressing(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        BlockId boulderId, BlockId crystalId, BlockId deadLogId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;

        void SetCell(int wx, int wy, int wz, BlockId block)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (chunk.Get(lx, ly, lz).IsAir)
            {
                chunk.Set(lx, ly, lz, block); // props fill air only — never carve terrain/other features
            }
        }

        // Margin 6 so the widest feature (a stone circle, radius ~4) generates identically from either side
        // of a chunk edge.
        for (int wx = origin.X - 6; wx < origin.X + cs + 6; wx++)
            for (int wz = origin.Z - 6; wz < origin.Z + cs + 6; wz++)
            {
                int cx = WorldConstants.WrapX(wx, _circumference);

                // One roll per column per prop kind (distinct salts), all rare — these are scattered accents.
                bool boulder = !boulderId.IsAir && Noise.Value01(seed + 0xB01D, cx, 29, Wz(wz)) < 0.0012;
                bool shard = !crystalId.IsAir && Noise.Value01(seed + 0xC57A, cx, 31, Wz(wz)) < 0.0008;
                bool deadTree = !deadLogId.IsAir && Noise.Value01(seed + 0xDEAD, cx, 37, Wz(wz)) < 0.0009;
                // Small POIs (W-R3, blocks-only): lone monoliths + broken stone circles, rarer than the props —
                // landmark finds with a data cache at the base/centre worth detouring for.
                bool monolith = !boulderId.IsAir && Noise.Value01(seed + 0x3057, cx, 43, Wz(wz)) < 0.00018;
                bool circle = !boulderId.IsAir && Noise.Value01(seed + 0xC1AC, cx, 47, Wz(wz)) < 0.00007;
                if (!boulder && !shard && !deadTree && !monolith && !circle)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0)
                {
                    continue; // dry ground only
                }

                int h1 = (int)(Noise.Value01(seed + 0x5E7D, cx, 41, Wz(wz)) * 997); // per-column shape hash
                var cacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;

                if (monolith)
                {
                    // A lone weathered monolith, 5–7 tall, with a data cache leaning at its base.
                    int height = 5 + h1 % 3;
                    for (int dy = 1; dy <= height; dy++)
                    {
                        SetCell(wx, sy + dy, wz, boulderId);
                    }

                    if (!cacheId.IsAir)
                    {
                        SetCell(wx + 1, sy + 1, wz, cacheId);
                    }
                }
                else if (circle)
                {
                    // A broken stone circle: pillars on a radius-4 ring (some collapsed), a data cache at the
                    // centre. Each pillar grounds on its own column so the ring follows the terrain.
                    (int X, int Z)[] ring = { (4, 0), (3, 3), (0, 4), (-3, 3), (-4, 0), (-3, -3), (0, -4), (3, -3) };
                    for (int r = 0; r < ring.Length; r++)
                    {
                        if (((h1 >> r) & 1) == 0 && r % 3 == 2)
                        {
                            continue; // the odd collapsed pillar
                        }

                        int px = wx + ring[r].X, pz = wz + ring[r].Z;
                        int py = SurfaceHeight(planet, px, pz);
                        int ph = 2 + ((h1 >> r) & 1);
                        for (int dy = 1; dy <= ph; dy++)
                        {
                            SetCell(px, py + dy, pz, boulderId);
                        }
                    }

                    if (!cacheId.IsAir)
                    {
                        SetCell(wx, sy + 1, wz, cacheId);
                    }
                }
                else if (boulder)
                {
                    // An irregular 2–4 block boulder cluster of the world's own rock.
                    SetCell(wx, sy + 1, wz, boulderId);
                    if ((h1 & 1) == 0) SetCell(wx + 1, sy + 1, wz, boulderId);
                    if ((h1 & 2) == 0) SetCell(wx, sy + 1, wz + 1, boulderId);
                    if ((h1 & 12) == 0) SetCell(wx, sy + 2, wz, boulderId); // the odd two-tall rock
                }
                else if (shard)
                {
                    // A jutting crystal shard, 1–3 blocks tall (taller ones rarer).
                    int height = 1 + h1 % 3;
                    for (int dy = 1; dy <= height; dy++)
                    {
                        SetCell(wx, sy + dy, wz, crystalId);
                    }
                }
                else if (deadTree)
                {
                    // A bare dead trunk (3–5 tall) with a single stub branch near the top — no leaves.
                    int height = 3 + h1 % 3;
                    for (int dy = 1; dy <= height; dy++)
                    {
                        SetCell(wx, sy + dy, wz, deadLogId);
                    }

                    int bx = (h1 & 4) == 0 ? 1 : -1;
                    SetCell(wx + bx, sy + height - 1, wz, deadLogId);
                }
            }
    }

    /// <summary>Stamps sparse geyser/vent marker blocks on the surface (item 21 follow-up): the topmost ground
    /// cell of a rare column becomes a <c>geyser_vent</c> with open air above, which the client detects to play
    /// the eruption VFX + hiss. Never under water/ponds. Deterministic from the seed; very rare (a landmark).</summary>
    private void StampGeysers(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord, BlockId ventId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const double density = 0.0015; // per-column chance (rare — geysers are scattered landmarks)

        for (int wx = origin.X; wx < origin.X + cs; wx++)
            for (int wz = origin.Z; wz < origin.Z + cs; wz++)
            {
                if (Noise.Value01(seed + 0x6E7A, WorldConstants.WrapX(wx, _circumference), 23, Wz(wz)) >= density)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0)
                {
                    continue; // a vent needs open ground (not a sea/pond column)
                }

                int ly = sy - origin.Y;
                if (ly >= 0 && ly < cs)
                {
                    chunk.Set(wx - origin.X, ly, wz - origin.Z, ventId); // the surface cell becomes a vent
                }
            }
    }

    /// <summary>Stamps towering giant mushrooms (a fibrous stem + a domed cap) on a fungal world's mycelium
    /// ground (item 21 V3). Mirrors <see cref="StampTrees"/>: scans a margin so a mushroom straddling a chunk
    /// edge generates identically from either chunk, and the per-column roll wraps in X. Deterministic.</summary>
    private void StampGiantMushrooms(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, BlockId stemId, BlockId capId, BlockId myceliumId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int maxCapR = 4;       // the widest a cap can grow — the chunk-edge scan margin must cover it
        const double density = 0.012; // per-column chance on mycelium ground

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return;
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        for (int wx = origin.X - maxCapR; wx < origin.X + cs + maxCapR; wx++)
            for (int wz = origin.Z - maxCapR; wz < origin.Z + cs + maxCapR; wz++)
            {
                if (Noise.Value01(seed + 0x5340, WorldConstants.WrapX(wx, _circumference), 17, Wz(wz)) >= density)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)].Surface;
                if (surf != myceliumId)
                {
                    continue; // only on mycelium ground
                }

                if (TempAt(calib, sy) < TreeLineC)
                {
                    continue; // above the tree line (#476)
                }

                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0)
                {
                    continue; // not in water
                }

                // Per-mushroom size (loosely-coupled stem height + cap): a shared bell factor with independent
                // jitter on each, so a fungal grove reads as a mix of small and towering capped fungi.
                double sizeF = SizeFactor(seed + 0x53410, wx, wz, 0.30);  // overall size, ±30% (bell)
                double hJit = SizeFactor(seed + 0x53411, wx, wz, 0.12);  // independent stem-height jitter
                double cJit = SizeFactor(seed + 0x53412, wx, wz, 0.12);  // independent cap jitter
                int height = System.Math.Clamp((int)System.Math.Round(7.0 * sizeF * hJit), 4, 12);   // ~5..9 before
                int capR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF * cJit), 2, maxCapR); // 2..4
                int topY = sy + height;
                for (int ty = sy + 1; ty <= topY; ty++)
                {
                    SetCell(wx, ty, wz, stemId, overwrite: true);
                }

                // A domed cap: shrinking discs stacked above the stem top (taller dome for bigger caps).
                int capLayers = capR - 1;
                for (int dy = 0; dy <= capLayers; dy++)
                {
                    int rr = capR - dy;
                    for (int dx = -rr; dx <= rr; dx++)
                        for (int dz = -rr; dz <= rr; dz++)
                        {
                            if (dx * dx + dz * dz <= rr * rr + 1)
                            {
                                SetCell(wx + dx, topY + dy, wz + dz, capId, overwrite: false);
                            }
                        }
                }
            }
    }

    /// <summary>A deterministic per-instance size factor centred on 1.0 (a "bell" — the average of two
    /// uniform samples is triangular, so most individuals sit near the species size and extremes are rare).
    /// <paramref name="amp"/> is the half-range (0.30 = ±30%). Pure function of the world column, so it is
    /// identical on the server and every client (vegetation is meshed from the same blocks).</summary>
    private double SizeFactor(long salt, int wx, int wz, double amp)
    {
        int cx = WorldConstants.WrapX(wx, _circumference);
        double u = (Noise.Value01(salt, cx, 23, Wz(wz)) + Noise.Value01(salt ^ 0x9E3779B9, cx, 41, Wz(wz))) * 0.5;
        return 1.0 + (u - 0.5) * 2.0 * amp;
    }

    /// <summary>Stamps multi-block trees on grass/earth columns. Each biome's flora theme dictates the tree
    /// ARCHETYPES its woods are made of (broadleaf / conifer / palm / jungle / dead); a low-frequency grove
    /// mask picks one kind per patch so a wood is all conifers OR all palms, not a jumble of shapes. Each tree
    /// also gets its own size (loosely-coupled trunk + crown). Scans a margin (the MAX crown) so a tree
    /// straddling a chunk edge generates identically from either chunk; the per-column roll wraps in X.
    /// Deterministic from the seed.</summary>
    private void StampTrees(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, BlockId logId, BlockId leafId, double density, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int maxCrown = 4; // the widest a crown can grow (jungle canopy) — the chunk-edge scan margin must cover it
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;
        var sandId = _content.GetBlock("sand")?.NumericId ?? BlockId.Air;
        // Distinct foliage for needled / fronded crowns; fall back to the generic leaf if not in this content.
        var pineId = _content.GetBlock("pine_needles")?.NumericId ?? leafId;
        var palmId = _content.GetBlock("palm_frond")?.NumericId ?? leafId;

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return; // outside this chunk (a neighbour chunk stamps that part of the tree)
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return; // leaves only fill air, never carve the trunk or terrain
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        for (int wx = origin.X - maxCrown; wx < origin.X + cs + maxCrown; wx++)
            for (int wz = origin.Z - maxCrown; wz < origin.Z + cs + maxCrown; wz++)
            {
                int sy = SurfaceHeight(planet, wx, wz);
                var biome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)];

                // FORESTS: a low-frequency mask gathers trees into real groves/woods. Inside a forest patch the
                // density is ~9x, on the fringe ~2x, the open land between almost bare — scaled by the biome's
                // (and its theme's) tree density so savanna stays sparse, jungle dense, fungal/crystal treeless.
                double forest = FbmT(seed + 0xF07E57, wx, wz, planet.TerrainScale * 2.0, octaves: 3);
                double localDensity = density * biome.TreeMul * biome.Theme.TreeMul
                    * (forest > 0.62 ? 9.0 : forest > 0.52 ? 2.0 : 0.15);
                if (localDensity <= 0.0
                    || Noise.Value01(seed + 5150, WorldConstants.WrapX(wx, _circumference), 11, Wz(wz)) >= localDensity)
                {
                    continue;
                }

                if (TempAt(calib, sy) < TreeLineC)
                {
                    continue; // above the tree line (#476): woods stop before the snow does
                }

                // Pick a grove kind from the biome theme's tree palette (one kind per low-frequency patch).
                var kind = PickTreeKind(biome.Theme.Trees, seed, wx, wz, planet.TerrainScale);
                if (kind == TreeKind.None)
                {
                    continue; // this theme grows no trees here (e.g. fungal → giant mushrooms instead)
                }

                var surf = biome.Surface;
                bool earthy = surf == grassId || surf == dirtId || surf == mudId;
                bool sandyOk = surf == sandId && (kind == TreeKind.Palm || kind == TreeKind.Dead); // palms/dead snags on sand
                if (!earthy && !sandyOk)
                {
                    continue;
                }

                if (sy + 1 <= fluidLevel)
                {
                    continue; // not in the sea
                }

                if (SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0)
                {
                    continue; // B35: an upland pond/lake or a river here — a tree would stand in the water
                }

                // Per-tree size (loosely-coupled height + crown): a shared bell factor sets the overall scale,
                // with a smaller independent jitter on each so trunk height and crown width still vary apart.
                double sizeF = SizeFactor(seed + 0x71EE5, wx, wz, 0.30);              // overall tree size, ±30% (bell)
                double hJit = SizeFactor(seed + 0x71EE6, wx, wz, 0.12);              // independent height jitter
                double cJit = SizeFactor(seed + 0x71EE7, wx, wz, 0.12);              // independent crown jitter

                switch (kind)
                {
                    case TreeKind.Conifer: BuildConifer(wx, sy, wz, sizeF, hJit, cJit, logId, pineId, SetCell); break;
                    case TreeKind.Palm: BuildPalm(wx, sy, wz, sizeF, hJit, cJit, logId, palmId, SetCell); break;
                    case TreeKind.Jungle: BuildJungle(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
                    case TreeKind.Dead: BuildDead(wx, sy, wz, sizeF, hJit, logId, SetCell); break;
                    default: BuildBroadleaf(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
                }
            }
    }

    /// <summary>Picks one tree archetype for this column from the theme's palette. A low-frequency grove mask
    /// keeps a whole patch to a single kind (a pine wood, a palm grove), not a per-tree jumble.</summary>
    private TreeKind PickTreeKind(TreeKind[] palette, long seed, int wx, int wz, double terrainScale)
    {
        int valid = 0;
        foreach (var k in palette)
        {
            if (k != TreeKind.None)
            {
                valid++;
            }
        }

        if (valid == 0)
        {
            return TreeKind.None;
        }

        if (valid == 1)
        {
            foreach (var k in palette)
            {
                if (k != TreeKind.None)
                {
                    return k;
                }
            }
        }

        double grove = FbmT(seed + 0x70EE17, wx, wz, terrainScale * 3.0, octaves: 2);
        int pick = (int)(grove * valid);
        if (pick >= valid)
        {
            pick = valid - 1;
        }

        int n = 0;
        foreach (var k in palette)
        {
            if (k == TreeKind.None)
            {
                continue;
            }

            if (n++ == pick)
            {
                return k;
            }
        }

        return TreeKind.Broadleaf;
    }

    /// <summary>The classic deciduous tree: a straight trunk under a roughly spherical leaf crown.</summary>
    private static void BuildBroadleaf(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(5.5 * sizeF * hJit), 3, 10);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.0 * sizeF * cJit), 1, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = -1; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 1)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
    }

    /// <summary>A rainforest giant: very tall trunk under a broad, deep canopy.</summary>
    private static void BuildJungle(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(8.0 * sizeF * hJit), 7, 14);
        int crownR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF * cJit), 2, 4);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = -2; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 2)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
    }

    /// <summary>A boreal conifer: tall narrow trunk under a layered conical needle crown tapering to a tip.</summary>
    private static void BuildConifer(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(7.0 * sizeF * hJit), 5, 13);
        int baseR = System.Math.Clamp((int)System.Math.Round(2.0 * sizeF * cJit), 1, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        int crownStart = sy + System.Math.Max(2, height / 3);
        int tip = topY + 1;
        for (int y = crownStart; y <= topY; y++)
        {
            double f = (double)(tip - y) / (tip - crownStart); // wide at the base, ~0 near the tip
            int r = System.Math.Clamp((int)System.Math.Round(baseR * f), 0, baseR);
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz <= r * r + 1)
                    {
                        set(wx + dx, y, wz + dz, leafId, false);
                    }
                }
        }

        set(wx, tip, wz, leafId, false); // pointed tip
    }

    /// <summary>A palm: a bare slender trunk topped by a burst of drooping fronds.</summary>
    private static void BuildPalm(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 5, 11);
        int fr = System.Math.Clamp((int)System.Math.Round(2.0 * cJit), 2, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        set(wx, topY + 1, wz, leafId, false); // crown core
        set(wx, topY, wz, leafId, false);
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 }, { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } };
        for (int i = 0; i < dirs.GetLength(0); i++)
        {
            for (int d = 1; d <= fr; d++)
            {
                int y = topY - (d == fr ? 1 : 0); // the frond tips droop one cell
                set(wx + dirs[i, 0] * d, y, wz + dirs[i, 1] * d, leafId, false);
            }
        }
    }

    /// <summary>A bare dead snag: a trunk with a couple of stub branches and no leaves.</summary>
    private static void BuildDead(int wx, int sy, int wz, double sizeF, double hJit,
        BlockId logId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(4.5 * sizeF * hJit), 3, 8);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        set(wx + 1, topY - 1, wz, logId, false);
        set(wx - 1, topY - 2, wz, logId, false);
        set(wx, topY - 1, wz + 1, logId, false);
        set(wx, topY - 2, wz - 1, logId, false);
    }

    // --- Per-world interior variety (item 21): two worlds of the same TYPE still differ underground — one is
    // honeycombed with caves, the next nearly solid; one is ore-rich, the next lean; and the deep crust turns
    // to dark basalt at a depth that varies per world. All deterministic from the world seed. ---

    // (The old PerWorldCaveThreshold clamp lived here — replaced by the per-world quantile calibration in
    // BuildCalibration (#472): the data threshold now maps to a target carve FRACTION, jittered per world.)

    /// <summary>This world's ore-richness multiplier (1.2×..2.2× the planet's vein rarities) — some worlds are
    /// rich strikes, others lean, so the interior payoff varies even on the same planet type. Raised again from
    /// 0.85×..1.6× (itself up from 0.7×..1.4×) so diggable ore is noticeably more common on every planet type —
    /// new players kept reporting they "couldn't find any" (Severin playtests #1 and #2). The per-ore kept-fraction
    /// is still clamped to 0.95 in <see cref="SelectOre"/>, so even the richest worlds don't flood.</summary>
    private static double PerWorldOreRichness(long seed)
        => 1.2 + (double)((ulong)(seed ^ 0x0670EL) % 1000UL) / 1000.0 * 1.0;

    private static readonly string[] MantleRocks = { "basalt", "deepslate", "granite" };

    /// <summary>Depth below which this world's crust turns to a deep "mantle" rock — basalt, deepslate or granite,
    /// CHOSEN per world — instead of the surface stone, so the interior MATERIAL (not just cave/ore density)
    /// differs from world to world. ~1/4 of worlds keep a plain stone crust to the bottom.
    /// <see cref="int.MaxValue"/> = no mantle on this world.</summary>
    private int PerWorldMantle(long seed, int floorDepth, out BlockId mantleId)
    {
        uint pick = (uint)((ulong)(seed ^ 0x0DEE9L) % 1000UL);
        mantleId = _content.GetBlock(MantleRocks[pick % (uint)MantleRocks.Length])?.NumericId ?? BlockId.Air;
        if (mantleId.IsAir || pick < 250)
        {
            return int.MaxValue; // ~1/4 of worlds: solid stone crust all the way down (no distinct mantle)
        }

        // The mantle starts somewhere in the lower half of the foundation (varies per world).
        int lo = System.Math.Max(40, floorDepth / 2);
        int span = System.Math.Max(1, floorDepth - FloorBandThickness - lo);
        return lo + (int)((ulong)(seed ^ 0x0DA27L) % (ulong)span);
    }

    /// <summary>This column's topsoil thickness — the surface + sub-surface depth before the crust turns to stone.
    /// Instead of the planet's flat <see cref="PlanetType.SurfaceDepth"/> everywhere, a coarse 2D noise rolls it
    /// between 1 and that value, so the stone/ore boundary undulates: in the thin patches ore-bearing stone reaches
    /// within a block or two of the surface (shallow digging is sometimes rewarded), while other patches keep the
    /// full topsoil. Constant over Y (per-column). (Severin/user playtest #2 — "dug 2 blocks, only stone/soil, no ore".)</summary>
    private int VariedSurfaceDepth(PlanetType planet, long seed, int worldX, int worldZ)
    {
        int baseDepth = planet.SurfaceDepth;
        if (baseDepth <= 1)
        {
            return baseDepth;
        }

        double n = ValueT(seed + 5150, worldX, 0.0, worldZ, 18.0, 1.0, 18.0); // 0..1, broad smooth patches
        return 1 + (int)System.Math.Round(n * (baseDepth - 1));
    }

    private BlockId SelectOre(PlanetType planet, WorldCalibration calib, long seed, int x, int y, int z,
        int depth, BlockId fallback, double richness)
    {
        for (int i = 0; i < planet.Ores.Count; i++)
        {
            var ore = planet.Ores[i];
            if (depth < ore.MinDepth || depth > ore.MaxDepth)
            {
                continue;
            }

            // The coarse noise clusters into vein-like patches; the threshold comes from the field's OWN
            // measured distribution (#472), so the kept fraction is exactly what we ask for. The old fixed
            // formula assumed a uniform field and sat unreachably far in the interpolated torus sampler's
            // tail — the root cause of the recurring "can't find any ore" feedback. The scale keeps the
            // UNION over a planet's ~8 stacked veins near ~10 % of deep rock (measured) — without it,
            // per-vein literalism turned half the underground into ore. SHALLOW starter veins (minDepth
            // ≤ 8: iron/copper/silicate class) run twice as dense as the deep rarities: the quantile blobs
            // cluster, and a new player's first ten blocks must reward digging (Severin M3, user playtest
            // 2026-07-26 — "nothing but rock at the spawn").
            double scale = ore.MinDepth <= 8 ? 0.30 : 0.15;
            double cap = ore.MinDepth <= 8 ? 0.08 : 0.05;
            double frac = System.Math.Clamp(ore.Rarity * richness * scale, 0.0, cap);
            if (frac <= 0.0)
            {
                continue;
            }

            double threshold = calib.OreCdf[(int)((1.0 - frac) * (calib.OreCdf.Length - 1))];
            double n = ValueT(seed + 100 + i * 31, x, y, z, 9.0, 9.0, 9.0);
            if (n > threshold)
            {
                var oreBlock = _content.GetBlock(ore.Block);
                if (oreBlock is not null)
                {
                    return oreBlock.NumericId;
                }
            }
        }

        return fallback;
    }

    /// <summary>A biome resolved for this world: its surface/sub-surface blocks plus the per-biome flora
    /// theme + density multipliers used when seeding plants and trees (so one region reads lush + tropical
    /// and another sparse + arid within the same world).</summary>
    private readonly struct BiomeResolved
    {
        public BiomeResolved(BlockId surface, BlockId sub, double floraMul, double treeMul, FloraThemes.Theme theme)
        {
            Surface = surface;
            Sub = sub;
            FloraMul = floraMul;
            TreeMul = treeMul;
            Theme = theme;
        }

        public BlockId Surface { get; }
        public BlockId Sub { get; }
        public double FloraMul { get; }
        public double TreeMul { get; }
        public FloraThemes.Theme Theme { get; }
    }

    /// <summary>
    /// Resolves the surface/sub-surface blocks (+ per-biome flora theme &amp; density) the planet actually
    /// uses. A multi-biome planet lists a *pool* of biomes; how many of them this world uses is randomised
    /// per world from the seed (2..pool), so each multi-biome world differs. Single-biome → one entry.
    /// </summary>
    private List<BiomeResolved> ResolveBiomes(PlanetType planet)
    {
        var planetTheme = FloraThemes.Resolve(planet.FloraTheme);
        var list = new List<BiomeResolved>();
        if (planet.Biomes.Count <= 0)
        {
            list.Add(new BiomeResolved(ResolveBlock(planet.SurfaceBlock), ResolveBlock(planet.SubSurfaceBlock),
                1.0, 1.0, planetTheme));
            return list;
        }

        int pool = planet.Biomes.Count;
        int count = pool;
        if (pool > 1)
        {
            long s = PlanetSeed(planet) ^ 0x0B10C0;
            count = 2 + (int)((ulong)(s < 0 ? -s : s) % (ulong)(pool - 1)); // 2..pool, seed-derived
        }

        for (int i = 0; i < count; i++)
        {
            var b = planet.Biomes[i];
            var theme = string.IsNullOrWhiteSpace(b.FloraTheme) ? planetTheme : FloraThemes.Resolve(b.FloraTheme);
            list.Add(new BiomeResolved(ResolveBlock(b.SurfaceBlock), ResolveBlock(b.SubSurfaceBlock),
                b.FloraDensityMul, b.TreeDensityMul, theme));
        }

        return list;
    }

    /// <summary>True when this column's biome surface is welcoming ground (grass or dirt). The landing-pad
    /// chooser PREFERS such columns so new players spawn on green topsoil — where the thin-topsoil ore
    /// windows (Severin M3) are visible — instead of a mud marsh or bare rock. A preference only, never a
    /// hard requirement (see <see cref="HasEarthySurfaceBiome"/>).</summary>
    public bool IsEarthySurface(PlanetType planet, int worldX, int worldZ)
    {
        var biomes = ResolveBiomes(planet);
        var b = biomes[biomes.Count <= 1
            ? 0
            : BiomeIndex(CalibFor(planet), PlanetSeed(planet), worldX, worldZ, biomes.Count,
                SurfaceHeight(planet, worldX, worldZ))];
        var grass = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirt = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        return (!grass.IsAir && b.Surface == grass) || (!dirt.IsAir && b.Surface == dirt);
    }

    /// <summary>Whether this world has any grass/dirt biome at all — desert/ice/exotic worlds don't, and
    /// their pad placement must not waste its search (or reject every candidate) looking for one.</summary>
    public bool HasEarthySurfaceBiome(PlanetType planet)
    {
        var grass = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirt = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        foreach (var b in ResolveBiomes(planet))
        {
            if ((!grass.IsAir && b.Surface == grass) || (!dirt.IsAir && b.Surface == dirt))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The biome index at a world position (large regions), for per-biome systems like weather.</summary>
    public int BiomeIndexAt(PlanetType planet, int worldX, int worldZ)
    {
        int count = ResolveBiomes(planet).Count;
        return count <= 1
            ? 0
            : BiomeIndex(CalibFor(planet), PlanetSeed(planet), worldX, worldZ, count,
                SurfaceHeight(planet, worldX, worldZ));
    }

    /// <summary>How many distinct biomes this planet's world uses.</summary>
    public int BiomeCount(PlanetType planet) => ResolveBiomes(planet).Count;

    /// <summary>Picks a biome per column: broad region noise (stretched so the outer list entries actually
    /// get real coverage — the raw FBM clusters around 0.5 and starved them) blended with the column's
    /// normalised ALTITUDE (#476), so a planet's biome list reads bottom-to-top: entry 0 hugs the lowlands,
    /// the last entry caps the peaks. Regions stay large so per-biome weather covers a meaningful area.</summary>
    private int BiomeIndex(WorldCalibration calib, long seed, int worldX, int worldZ, int count, int surfaceY)
    {
        double n = Noise.FbmTorus(seed ^ 0x0B10E, worldX, worldZ, _circumference,
            WorldConstants.LatitudePeriodFor(_circumference), 360.0, octaves: 3);
        double spread = System.Math.Clamp((n - 0.5) * 2.4 + 0.5, 0.0, 1.0);
        double span = System.Math.Max(1.0, calib.MaxHeight - calib.MinHeight);
        double alt = System.Math.Clamp((surfaceY - calib.MinHeight) / span, 0.0, 1.0);
        double mix = System.Math.Clamp(spread * 0.6 + alt * 0.4, 0.0, 0.9999);
        int idx = (int)(mix * count);
        return idx < 0 ? 0 : (idx >= count ? count - 1 : idx);
    }

    /// <summary>Places aquatic flora in one submerged column: a seabed plant — a kelp/seagrass stalk that grows
    /// up a few cells (leaving the top open water) or a single coral clump on the bed — and, separately, an
    /// occasional lily pad on the surface. Per-column + deterministic from the seed, so no cross-chunk margin
    /// is needed (unlike trees). Density is generous so a lake reads as visibly planted, not bare.</summary>
    private void StampWaterFlora(ChunkData chunk, Vector3i origin, int lx, int lz, long seed,
        int worldX, int worldZ, int surfaceY, int fluidLevel, BlockId kelpId, BlockId lilyId,
        BlockId coralId, BlockId seagrassId, double floraDensity)
    {
        int columnDepth = fluidLevel - surfaceY; // water cells above the seabed (>= 1 here)
        double roll = Noise.Value01(seed + 9007, WorldConstants.WrapX(worldX, _circumference), 11, Wz(worldZ));

        // The seabed plant for this column: pick deterministically among the active seabed archetypes, then
        // place it if the planting roll lands in this column's (generous) density band. Coral sits as a single
        // clump on the bed (shallow-friendly); kelp/seagrass need a little depth and grow up a stalk.
        var stalkOptions = new System.Collections.Generic.List<BlockId>(2);
        if (_kelpActive && !kelpId.IsAir) stalkOptions.Add(kelpId);
        if (_seagrassActive && !seagrassId.IsAir) stalkOptions.Add(seagrassId);
        bool coral = _coralActive && !coralId.IsAir;

        // A coherent patch field decides WHICH seabed plant dominates here (not per-cell salt-and-pepper).
        double pick = FbmT(seed + 0x5EA6, worldX, worldZ, 14.0, octaves: 2);

        if ((stalkOptions.Count > 0 || coral) && roll < floraDensity * 2.4)
        {
            // Prefer a stalk where there's room; fall back to a coral clump in shallow water.
            if (stalkOptions.Count > 0 && columnDepth >= 2)
            {
                var stalk = stalkOptions[System.Math.Min(stalkOptions.Count - 1, (int)(pick * stalkOptions.Count))];
                int height = 2 + (int)(roll * 997) % 3; // 2..4 cells
                int top = System.Math.Min(fluidLevel - 1, surfaceY + height);
                for (int wy = surfaceY + 1; wy <= top; wy++)
                {
                    int sly = wy - origin.Y;
                    if (sly >= 0 && sly < WorldConstants.ChunkSize)
                    {
                        chunk.Set(lx, sly, lz, stalk);
                    }
                }

                return;
            }

            if (coral)
            {
                int bed = (surfaceY + 1) - origin.Y; // the bottom water cell, sitting on the seabed
                if (bed >= 0 && bed < WorldConstants.ChunkSize)
                {
                    chunk.Set(lx, bed, lz, coralId);
                }

                return;
            }
        }

        // Separately, an occasional lily pad floating on the topmost water cell (if the lily archetype is active).
        if (_lilyActive && !lilyId.IsAir && roll > 1.0 - floraDensity * 0.9)
        {
            int lily = fluidLevel - origin.Y;
            if (lily >= 0 && lily < WorldConstants.ChunkSize)
            {
                chunk.Set(lx, lily, lz, lilyId);
            }
        }
    }

    // The planet key the resolved flora state below belongs to (null = not yet resolved). Flora is a
    // PER-PLANET subset (FloraGenerator.GenerateRoster XORs the planet key into the seed), and this one
    // generator instance serves every body in the save — so the state must be re-resolved whenever the
    // requested planet changes, or the first-visited planet's flora would contaminate all others (and the
    // baseline would depend on visit order instead of the seed).
    private string? _floraResolvedFor;
    private long _floraResolvedSalt; // the body salt the pools were resolved under (#478 — per-body rosters)
    private bool _kelpActive, _lilyActive; // whether the seabed kelp / surface lily archetypes grow on this world
    private bool _coralActive, _seagrassActive; // the other two seabed archetypes (coral reefs / seagrass)
    // surface block id -> the pool of (this world's active) flora that may grow on it.
    private readonly System.Collections.Generic.Dictionary<ushort, BlockId[]> _floraBySurface = new();
    // flora block id -> its climate tags (for theme-weighted, patchy species selection).
    private readonly System.Collections.Generic.Dictionary<ushort, FloraTag> _floraTagByBlock = new();

    /// <summary>Resolves this world's active flora subset (once): builds the per-surface land-flora pools from
    /// only the archetypes <see cref="FloraGenerator"/> activated for this world, and records whether the two
    /// aquatic archetypes are active. Different worlds activate different forms (coverage is kept, so no host
    /// surface or the seas ever go bare).</summary>
    private void ResolveFlora(PlanetType planet)
    {
        if (_floraResolvedFor == planet.Key && _floraResolvedSalt == _locationSalt)
        {
            return;
        }

        _floraResolvedFor = planet.Key;
        _floraResolvedSalt = _locationSalt;
        _floraBySurface.Clear(); // re-resolving for a different planet/body: drop the previous pools
        _floraTagByBlock.Clear();

        var active = new System.Collections.Generic.HashSet<string>();
        foreach (var fs in FloraGenerator.GenerateRoster(planet, RosterSeed))
        {
            if (fs.Active)
            {
                active.Add(fs.BlockKey);
            }
        }

        _kelpActive = active.Contains("flora_kelp");
        _lilyActive = active.Contains("flora_lily");
        _coralActive = active.Contains("flora_coral");
        _seagrassActive = active.Contains("flora_seagrass");

        var acc = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<BlockId>>();
        foreach (var sp in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.All)
        {
            if (sp.Aquatic || !active.Contains(sp.Key) || _content.GetBlock(sp.Key) is not { } flora)
            {
                continue; // aquatic flora are placed in submerged columns; inactive forms don't grow here
            }

            _floraTagByBlock[flora.NumericId.Value] = sp.Tags;
            foreach (var hostKey in sp.Hosts)
            {
                if (_content.GetBlock(hostKey) is { } host)
                {
                    if (!acc.TryGetValue(host.NumericId.Value, out var list))
                    {
                        acc[host.NumericId.Value] = list = new System.Collections.Generic.List<BlockId>();
                    }

                    list.Add(flora.NumericId);
                }
            }
        }

        foreach (var kv in acc)
        {
            _floraBySurface[kv.Key] = kv.Value.ToArray();
        }
    }

    /// <summary>
    /// Picks the flora block for a biome's surface (Air = none). Selection is PATCHY (a low-frequency noise,
    /// not per-cell white noise) so one species dominates a contiguous patch — a fern glade here, a flower
    /// meadow there — instead of a salt-and-pepper mix; and it is THEME-WEIGHTED so the biome's preferred
    /// climate species fill most of the patches while off-theme ones still turn up for variety.
    /// </summary>
    private BlockId FloraForSurface(PlanetType planet, BiomeResolved biome, long seed, int worldX, int worldZ)
    {
        ResolveFlora(planet);
        if (!_floraBySurface.TryGetValue(biome.Surface.Value, out var pool) || pool.Length == 0)
        {
            return BlockId.Air;
        }

        if (pool.Length == 1)
        {
            return pool[0];
        }

        // Theme weights: preferred species count more, so a patch is most likely one of the biome's signature
        // plants. Total is small (pools are a handful of species) so recomputing per column is cheap.
        int total = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            total += _floraTagByBlock.TryGetValue(pool[i].Value, out var tag)
                ? FloraThemes.PickWeight(biome.Theme, tag) : 1;
        }

        // A low-frequency patch field selects WITHIN the weighted distribution; nearby columns share a value,
        // so the chosen species changes only at patch boundaries (coherent fields, not per-cell noise).
        double t = FbmT(seed + 9101, worldX, worldZ, 18.0, octaves: 2);
        int target = (int)(t * total);
        if (target >= total)
        {
            target = total - 1;
        }

        int acc = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            acc += _floraTagByBlock.TryGetValue(pool[i].Value, out var tag)
                ? FloraThemes.PickWeight(biome.Theme, tag) : 1;
            if (target < acc)
            {
                return pool[i];
            }
        }

        return pool[pool.Length - 1];
    }

    /// <summary>The per-column surface-flora density: the world/biome base scaled by a vegetation-richness
    /// mask (lush thickets vs sparse open ground) and the per-biome density, capped so even the lushest
    /// patch leaves some bare ground.</summary>
    private double LocalFloraDensity(PlanetType planet, BiomeResolved biome, double baseDensity, long seed, int wx, int wz)
    {
        double d = baseDensity * biome.FloraMul * biome.Theme.DensityMul * VegetationRichness(planet, seed, wx, wz);
        return d > 0.95 ? 0.95 : d;
    }

    /// <summary>0.45..2.2 vegetation-richness multiplier per column. Couples undergrowth to the SAME forest
    /// mask the trees cluster in (so woods get a carpeted floor, not bare ground under the trunks) plus an
    /// independent meadow mask, so treeless biomes also break into lush thickets and sparse clearings.</summary>
    private double VegetationRichness(PlanetType planet, long seed, int wx, int wz)
    {
        double forest = FbmT(seed + 0xF07E57, wx, wz, planet.TerrainScale * 2.0, octaves: 3); // matches StampTrees' grove mask
        double meadow = FbmT(seed + 0x9E2D07, wx, wz, planet.TerrainScale * 1.6, octaves: 2); // independent lush/sparse patches
        double m = forest > meadow ? forest : meadow; // a wood OR a meadow makes a column lush
        return m > 0.62 ? 2.2 : m > 0.52 ? 1.5 : m > 0.40 ? 1.0 : 0.45;
    }

    private BlockId ResolveBlock(string key)
    {
        var def = _content.GetBlock(key)
                  ?? throw new InvalidOperationException($"World generation references unknown block '{key}'.");
        return def.NumericId;
    }
}
