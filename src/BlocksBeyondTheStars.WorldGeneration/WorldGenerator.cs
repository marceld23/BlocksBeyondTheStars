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

    /// <summary>Sets the world circumference for subsequent generation/queries (the server calls this when the
    /// active world changes, so terrain, surface-height and flora all wrap at the right size).</summary>
    public void SetCircumference(int circumference) => _circumference = circumference;

    // True when the active body is an airless moon (item 33): its terrain is cratered even though its planet
    // TYPE may carry an atmosphere on a full-size planet. The asteroid type carries Cratered in data instead,
    // so it craters everywhere (incl. standalone queries). Set beside SetCircumference at world-load.
    private bool _crateredWorld;

    /// <summary>Marks the active world as cratered regardless of its planet type (used for airless moons).</summary>
    public void SetCratered(bool cratered) => _crateredWorld = cratered;

    /// <summary>The current cratered-world flag (so a caller can save/restore it around a transient query
    /// for a different body, e.g. computing another body's landing pads).</summary>
    public bool Cratered => _crateredWorld;

    // The active world's planned landing pads. Pad terrain is FLATTENED at generation time (the landed
    // ship is a placed structure object, not stamped blocks — it needs level, clear ground). Set beside
    // SetCircumference whenever the active world changes; empty = no flattening (void worlds, tests).
    private IReadOnlyList<LandingPadFlatten> _landingPads = System.Array.Empty<LandingPadFlatten>();

    /// <summary>Sets the active world's landing pads so <see cref="Generate"/> levels their terrain.</summary>
    public void SetLandingPads(IReadOnlyList<LandingPadFlatten> pads)
        => _landingPads = pads ?? System.Array.Empty<LandingPadFlatten>();

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
    private void FlattenLandingPads(ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, long seed)
    {
        if (_landingPads.Count == 0)
        {
            return;
        }

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

            int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(seed, worldX, worldZ, biomes.Count, _circumference);
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

    private long PlanetSeed(PlanetType planet) => _worldSeed ^ StableHash(planet.Key);

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

    /// <summary>Computes the surface height (world Y) of a column for a planet.</summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
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

    /// <summary>The world's surface sea: which fluid fills its basins and up to what world-Y level. Water on
    /// worlds with an atmosphere, lava on volcanic / airless worlds (never both); a higher abundance raises
    /// the level so more low ground floods. Returns (int.MinValue, Air) for a dry world.</summary>
    private (int Level, BlockId Fluid) ResolveSeaFluid(PlanetType planet)
    {
        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";

        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        double lavaAb = planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0); // B19: more lava (higher pool level) on volcanic worlds

        // Sea level sits BELOW the average surface (BaseHeight) so only genuine low ground floods (valleys,
        // canyon floors, basins) — not half the world. A higher abundance raises it toward more water.
        if (waterAb > 0.0 && _content.GetBlock("water") is { } water)
        {
            int level = planet.BaseHeight + (int)System.Math.Round((waterAb - 0.95) * planet.Amplitude);
            return (level, water.NumericId);
        }

        // Watery worlds get no surface lava; only dry volcanic/airless worlds pool lava in their basins. The
        // level sits around the average surface so lava is actually VISIBLE across the low + mid terrain — not
        // hidden in a few deep pits — with mountains poking out as basalt islands (B54).
        if (waterAb <= 0.0 && lavaAb > 0.0 && _content.GetBlock("lava") is { } lava)
        {
            int level = planet.BaseHeight + (int)System.Math.Round((lavaAb - 0.7) * planet.Amplitude);
            return (level, lava.NumericId);
        }

        return (int.MinValue, BlockId.Air);
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

    private const double RiverHalfWidth = 0.04; // |river-line noise − 0.5| under this is in-channel (narrow, winding)
    private const int RiverMaxDepth = 4;        // channel depth at the river centre, tapering to the banks (item 21 V2)

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

        // Flat-ground gate — sampled lazily, only inside the pond mask, so it doesn't cost on every column.
        int slope = System.Math.Abs(SurfaceHeight(planet, worldX + 2, worldZ) - SurfaceHeight(planet, worldX - 2, worldZ))
                  + System.Math.Abs(SurfaceHeight(planet, worldX, worldZ + 2) - SurfaceHeight(planet, worldX, worldZ - 2));
        if (slope > PondMaxSlope)
        {
            return 0;
        }

        return (int)System.Math.Round(System.Math.Min(1.0, strength) * PondMaxDepth);
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

    /// <summary>River carve depth (0 = none) at a surface column — the same winding-channel gate
    /// <see cref="Generate"/> applies, resolved internally so callers (tree/prop placement, ship landing,
    /// aquatic life) can find/avoid river water without duplicating the rule. Rivers only on the wetter worlds
    /// (WaterAbundance ≥ 0.4), above the global sea, on low/mid terrain, and not where a pond already sits.</summary>
    public int SurfaceRiverDepth(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        if (pondAbundance < 0.4 || seaFluid != waterId || waterId.IsAir)
        {
            return 0; // rivers only on the wetter worlds (matches Generate's `rivers` gate)
        }

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        int riverMaxY = planet.BaseHeight + (int)(planet.Amplitude * 0.5);
        if (surfaceY <= seaLevel || surfaceY > riverMaxY)
        {
            return 0; // below the global sea (the sea fills it) or up on the high terrain (rivers stay low/mid)
        }

        if (SurfacePondDepth(planet, worldX, worldZ) > 0)
        {
            return 0; // a pond already claims this column (matches Generate's pond-first precedence)
        }

        double rl = FbmT(PlanetSeed(planet) + 0x817E12, worldX, worldZ, planet.TerrainScale * 2.5, octaves: 2);
        double rv = System.Math.Abs(rl - 0.5);
        if (rv >= RiverHalfWidth)
        {
            return 0; // outside the winding channel band
        }

        int depth = (int)System.Math.Round(RiverMaxDepth * (1.0 - rv / RiverHalfWidth));
        return depth >= 1 ? depth : 0;
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

    /// <summary>True if this surface column is under a LAVA sea — so a ship landing avoids it too (B54), the
    /// same way it avoids water.</summary>
    public bool IsSurfaceLava(PlanetType planet, int worldX, int worldZ)
    {
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

        // Upland pond or river: a carved bowl/channel filled flush to the original surface. SurfaceRiverDepth
        // already yields 0 where a pond sits, so taking the max keeps Generate's pond-first precedence.
        int carve = System.Math.Max(SurfacePondDepth(planet, worldX, worldZ), SurfaceRiverDepth(planet, worldX, worldZ));
        if (carve > 0)
        {
            waterTopY = surfaceY;
            seabedY = surfaceY - carve;
            return true;
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

        // Per-world interior variety (item 21): cave frequency + ore richness + a deep basalt mantle all vary
        // per world, so two worlds of the same type differ underground, not just on the surface.
        double caveThreshold = PerWorldCaveThreshold(planet, seed);
        double oreRichness = PerWorldOreRichness(seed) * _oreFactor;
        int mantleDepth = PerWorldMantle(seed, floorDepth, out var mantleId);

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
            || string.Equals(planet.Key, "ashen", System.StringComparison.OrdinalIgnoreCase);
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

        // Rivers (item 21 V2): wet worlds get winding water channels carved flush like a long pond. Kept to
        // low/mid terrain (rivers don't climb mountaintops).
        bool rivers = ponds && pondAbundance >= 0.4;
        int riverMaxY = planet.BaseHeight + (int)(planet.Amplitude * 0.5);

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

            // Rivers: a winding river-line noise band carves a channel (deepest at the centre, tapering to the
            // banks) and fills it with water flush to the surface — a meandering river across low/mid terrain.
            // (Skipped where a pond already claimed the column. The old guard compared columnFluid to the sea
            // fluid, which on a water world equals the column's default fluid — so rivers never carved.)
            if (rivers && !pondHere && surfaceY > fluidLevel && surfaceY <= riverMaxY)
            {
                double rl = FbmT(seed + 0x817E12, worldX, worldZ, planet.TerrainScale * 2.5, octaves: 2);
                double rv = System.Math.Abs(rl - 0.5);
                if (rv < RiverHalfWidth)
                {
                    int depth = (int)System.Math.Round(RiverMaxDepth * (1.0 - rv / RiverHalfWidth));
                    if (depth >= 1)
                    {
                        seabedY = surfaceY - depth;
                        waterTop = surfaceY;
                        columnFluid = seaWaterId;
                    }
                }
            }

            // Per-column biome → surface/sub-surface blocks (single-biome worlds use index 0).
            int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(seed, worldX, worldZ, biomes.Count, _circumference);
            var biome = biomes[biomeIndex];
            var surfaceId = biome.Surface;
            var subSurfaceId = biome.Sub;

            // Floating islands (item 21 V5): a per-column sky-island slab high above the surface — a grass-topped
            // deck on a tapered rocky underbelly, scattered by a region mask, drifting in the air.
            int islandTop = int.MinValue, islandBottom = int.MaxValue;
            if (floatingIslands)
            {
                double im = FbmT(seed + 0x15A4D, worldX, worldZ, planet.TerrainScale * 1.4, octaves: 3);
                if (im > 0.60) // a little more island coverage — these worlds are ABOUT the sky islands
                {
                    double t = (im - 0.60) / 0.40;       // 0..1 toward an island's centre
                    // Per-island altitude varies ±12 on a broad band, so the sky reads as layered drifting
                    // islands instead of one flat shelf at a single height.
                    double alt = FbmT(seed + 0x15A4E, worldX, worldZ, planet.TerrainScale * 3.0, octaves: 2);
                    int center = planet.BaseHeight + 28 + (int)((alt - 0.5) * 24.0);
                    int half = 2 + (int)(t * 8.0);       // 2..10 thick
                    islandTop = center + half;
                    islandBottom = center - half - (int)(t * 6.0); // tapered rocky underside
                }
            }

            // Crater-floor metal clumps (item 33): on a cratered world, the top cells of a metal-bearing deep
            // crater floor are exposed rare ore instead of regolith (only some craters, a few clumps each).
            BlockId? craterMetal = (planet.Cratered || _crateredWorld)
                ? CraterFloorMetal(planet, seed, worldX, worldZ) : (BlockId?)null;

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

                // Carve caves below the surface layer (per-world cave frequency, item 21).
                if (caveThreshold > 0.0 && depth > 1)
                {
                    double cave = ValueT(seed + 7777, worldX, worldY, worldZ, 22.0, 16.0, 22.0);
                    if (cave > caveThreshold)
                    {
                        continue; // cave => air
                    }
                }

                BlockId block;
                if (craterMetal.HasValue && depth <= 1)
                {
                    block = craterMetal.Value; // a rare-metal clump on the crater floor (top two cells)
                }
                else if (depth < planet.SurfaceDepth)
                {
                    block = depth == 0 ? surfaceId : subSurfaceId;
                }
                else
                {
                    // Deep crust turns to a dark basalt mantle below this world's mantle depth (item 21), so the
                    // interior isn't one uniform stone column on every world. Ores still vein through it.
                    var rock = depth >= mantleDepth ? mantleId : deepId;
                    block = SelectOre(planet, seed, worldX, worldY, worldZ, depth, fallback: rock, oreRichness);

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
                double localFloraDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ);
                if (!floraId.IsAir && fly >= 0 && fly < WorldConstants.ChunkSize
                    && Noise.Value01(seed + 9001, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < localFloraDensity)
                {
                    chunk.Set(lx, fly, lz, floraId);
                }
            }
            else if (waterFlora && seabedY + 1 <= waterTop)
            {
                // Submerged column — the sea or an upland pond grows seabed plants / lily pads, not land flora.
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
        FlattenLandingPads(chunk, coord, biomes, seed);

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

        for (int wx = origin.X - maxCapR; wx < origin.X + cs + maxCapR; wx++)
        for (int wz = origin.Z - maxCapR; wz < origin.Z + cs + maxCapR; wz++)
        {
            if (Noise.Value01(seed + 0x5340, WorldConstants.WrapX(wx, _circumference), 17, Wz(wz)) >= density)
            {
                continue;
            }

            var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(seed, wx, wz, biomes.Count, _circumference)].Surface;
            if (surf != myceliumId)
            {
                continue; // only on mycelium ground
            }

            int sy = SurfaceHeight(planet, wx, wz);
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

        for (int wx = origin.X - maxCrown; wx < origin.X + cs + maxCrown; wx++)
        for (int wz = origin.Z - maxCrown; wz < origin.Z + cs + maxCrown; wz++)
        {
            var biome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(seed, wx, wz, biomes.Count, _circumference)];

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

            int sy = SurfaceHeight(planet, wx, wz);
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
                case TreeKind.Palm:    BuildPalm(wx, sy, wz, sizeF, hJit, cJit, logId, palmId, SetCell); break;
                case TreeKind.Jungle:  BuildJungle(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
                case TreeKind.Dead:    BuildDead(wx, sy, wz, sizeF, hJit, logId, SetCell); break;
                default:               BuildBroadleaf(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
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

    /// <summary>This world's effective cave threshold (lower = MORE caves) — the planet's base value jittered
    /// per world by ±0.06, so cave frequency underground varies world to world. 0 keeps caves disabled.</summary>
    private static double PerWorldCaveThreshold(PlanetType planet, long seed)
    {
        if (planet.CaveThreshold <= 0.0)
        {
            return 0.0;
        }

        double j = ((double)((ulong)(seed ^ 0x0CA7EL) % 1000UL) / 1000.0 - 0.5) * 0.10; // ±0.05
        double t = planet.CaveThreshold + j;
        return t < 0.60 ? 0.60 : (t > 0.90 ? 0.90 : t);
    }

    /// <summary>This world's ore-richness multiplier (0.7×..1.4× the planet's vein rarities) — some worlds are
    /// rich strikes, others lean, so the interior payoff varies even on the same planet type.</summary>
    private static double PerWorldOreRichness(long seed)
        => 0.7 + (double)((ulong)(seed ^ 0x0670EL) % 1000UL) / 1000.0 * 0.7;

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

    private BlockId SelectOre(PlanetType planet, long seed, int x, int y, int z, int depth, BlockId fallback, double richness)
    {
        for (int i = 0; i < planet.Ores.Count; i++)
        {
            var ore = planet.Ores[i];
            if (depth < ore.MinDepth || depth > ore.MaxDepth)
            {
                continue;
            }

            // Coarse 3D noise produces vein-like clusters; rarity is the fraction kept (scaled by this world's
            // richness so some worlds strike rich and others lean).
            double n = ValueT(seed + 100 + i * 31, x, y, z, 9.0, 9.0, 9.0);
            if (n > 1.0 - System.Math.Clamp(ore.Rarity * richness, 0.0, 0.95))
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

    /// <summary>The biome index at a world position (large regions), for per-biome systems like weather.</summary>
    public int BiomeIndexAt(PlanetType planet, int worldX, int worldZ)
    {
        int count = ResolveBiomes(planet).Count;
        return count <= 1 ? 0 : BiomeIndex(PlanetSeed(planet), worldX, worldZ, count, _circumference);
    }

    /// <summary>How many distinct biomes this planet's world uses.</summary>
    public int BiomeCount(PlanetType planet) => ResolveBiomes(planet).Count;

    /// <summary>Broad low-frequency noise picks a biome per column (multi-biome worlds). The scale is
    /// large so each biome is a big contiguous region (so per-biome weather covers a meaningful area).</summary>
    private static int BiomeIndex(long seed, int worldX, int worldZ, int count, int circumference)
    {
        double n = Noise.FbmTorus(seed ^ 0x0B10E, worldX, worldZ, circumference,
            WorldConstants.LatitudePeriodFor(circumference), 360.0, octaves: 3);
        int idx = (int)(n * count);
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

    private bool _floraResolved;
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
        if (_floraResolved)
        {
            return;
        }

        _floraResolved = true;

        var active = new System.Collections.Generic.HashSet<string>();
        foreach (var fs in FloraGenerator.GenerateRoster(planet, _worldSeed))
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
