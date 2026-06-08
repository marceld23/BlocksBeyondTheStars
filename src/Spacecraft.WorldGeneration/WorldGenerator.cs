using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;

namespace Spacecraft.WorldGeneration;

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

    /// <summary>Computes the surface height (world Y) of a column for a planet.</summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        long seed = PlanetSeed(planet);
        double n = Noise.FbmCylX(seed, worldX, worldZ, _circumference, planet.TerrainScale, octaves: 4);
        double h = (n - 0.5) * 2.0; // [-1, 1] base rolling terrain

        // Airless moons + landable asteroids (item 33): mostly flat regolith (a gentle undulation only — no
        // hills/mountains/canyons) pocked with round impact craters carved on top.
        if (planet.Cratered || _crateredWorld)
        {
            double flat = h * 0.30 * planet.Amplitude;
            return planet.BaseHeight + (int)System.Math.Round(flat + CraterCarve(seed, worldX, worldZ, planet));
        }

        // Regional terrain character: a large-scale field selects how rugged this area is (a blend across the
        // world's archetype subset), so the surface varies between flat plains, hills, mountains and canyons.
        var (amp, ridged) = TerrainProfile(planet, seed, worldX, worldZ);
        if (ridged > 0.0)
        {
            double r = (1.0 - System.Math.Abs(h)) * 2.0 - 1.0; // ridged: turn smooth swells into sharp valleys/ridges
            h = h * (1.0 - ridged) + r * ridged;
        }

        return planet.BaseHeight + (int)System.Math.Round(h * planet.Amplitude * amp);
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
        double mask = Noise.FbmCylX(seed + 0x6A17, worldX, worldZ, _circumference, planet.TerrainScale * 1.7, octaves: 3);
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
        double region = Noise.FbmCylX(seed + 0x51A2, worldX, worldZ, _circumference, planet.TerrainScale * 3.5, octaves: 2);
        if (region < CraterMetalRegion)
        {
            return null; // this crater holds no metal
        }

        // Within a metal-bearing crater, a small-scale clump mask scatters a few lumps (high freq → tiny clumps).
        double clump = Noise.FbmCylX(seed + 0x51A3, worldX, worldZ, _circumference, planet.TerrainScale * 0.22, octaves: 2);
        if (clump < CraterMetalThreshold)
        {
            return null;
        }

        int pick = (int)(Noise.Value01(seed + 0x51A4, WorldConstants.WrapX(worldX, _circumference), 5, worldZ)
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
        double rug = Noise.FbmCylX(s, worldX, worldZ, _circumference, planet.TerrainScale * 6.0, octaves: 3);
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

    private const int WorldFloorDepth = 80;   // blocks below the surface where the unmineable bedrock floor sits (B46)
    private const int LavaFloorThickness = 3; // a lava band this thick sits just above the bedrock on real planets

    private const int PondMaxDepth = 5;     // deepest carve at a pond's centre (≥2 is swimmable)
    private const double PondBand = 0.10;   // mask range from "rim" (depth 0) to "centre" (full depth)
    private const int PondMaxSlope = 4;     // only carve on flat ground (Δheight over ±2 in x+z) so water sits level

    /// <summary>Carve depth (0 = none) for an upland pond at this column: a low-frequency mask scatters ponds
    /// (sized by its peaks → small pools + occasional lakes), gated to flat ground so the water surface stays
    /// level. Deterministic — pure noise. The caller fills the carved bowl with water up to the original
    /// surface, so a pond reads as a swimmable pool flush with the surrounding terrain (B7).</summary>
    private int PondDepthAt(PlanetType planet, long seed, int worldX, int worldZ, double threshold)
    {
        double mask = Noise.FbmCylX(seed + 0x7A11, worldX, worldZ, _circumference, planet.TerrainScale * 4.0, octaves: 3);
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

    /// <summary>True if this surface column is under water — beneath the global water sea, or inside an upland
    /// pond/lake (B7). A lava sea is not "water" here. Used to keep ship landings out of the water (B36).</summary>
    public bool IsSurfaceWater(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        if (seaFluid == waterId && !waterId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel)
        {
            return true; // beneath the global sea
        }

        return SurfacePondDepth(planet, worldX, worldZ) > 0; // inside an upland pond
    }

    /// <summary>True if this surface column is under a LAVA sea — so a ship landing avoids it too (B54), the
    /// same way it avoids water.</summary>
    public bool IsSurfaceLava(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        return seaFluid == lavaId && !lavaId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel;
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

        // World floor (B46): an unmineable bedrock layer bounds the dig depth so a player can't fall forever.
        // On real planets a band of lava sits just above it; airless moons + asteroids get solid rock instead.
        var bedrockId = _content.GetBlock("bedrock")?.NumericId ?? deepId;
        var lavaFloorId = _content.GetBlock("lava")?.NumericId ?? bedrockId;
        bool airlessBody = planet.Cratered || _crateredWorld;

        // Surface seas: water fills terrain basins on worlds with an atmosphere; lava fills them on
        // volcanic / airless worlds (never both). A higher abundance raises the sea level so more low
        // ground floods — the basin's depth + any rises become shallow water / deep water / islands.
        var (fluidLevel, fluidId) = ResolveSeaFluid(planet);

        // Trees: multi-block trunk + leaf crown on grass/earth ground (a small auto density on flora worlds).
        double treeDensity = planet.TreeDensity ?? (flora ? 0.012 : 0.0);
        var logId = _content.GetBlock("wood_log")?.NumericId ?? BlockId.Air;
        var leafId = _content.GetBlock("tree_leaves")?.NumericId ?? BlockId.Air;
        bool trees = treeDensity > 0.0 && !logId.IsAir && !leafId.IsAir;

        // Aquatic flora: kelp stalks rooted on the seabed + lily pads on the surface, only where the sea is
        // water (never lava). World gen places them directly in the submerged columns below.
        var kelpId = _content.GetBlock("flora_kelp")?.NumericId ?? BlockId.Air;
        var lilyId = _content.GetBlock("flora_lily")?.NumericId ?? BlockId.Air;
        var seaWaterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        ResolveFlora(planet); // pick this world's active flora subset (sets _kelpActive / _lilyActive)
        bool waterFlora = flora && !kelpId.IsAir && !lilyId.IsAir && fluidId == seaWaterId && !seaWaterId.IsAir
            && (_kelpActive || _lilyActive);

        // Upland ponds/lakes (B7): scattered, swimmable water ABOVE the sea on flat ground. Frequency derives
        // from the world's WaterAbundance — the same property that sets the sea level — so wet worlds get more
        // (and larger) ponds, dry worlds almost none, and lava/airless worlds get none (their sea isn't water).
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        bool ponds = pondAbundance > 0.15 && fluidId == seaWaterId && !seaWaterId.IsAir;
        // The mask is FBM noise (∈[0,1], clustered around 0.5), so the bar sits in its upper tail; a wetter
        // world lowers it for more/larger ponds. The flat-ground gate keeps them scattered (not everywhere).
        double pondThreshold = 0.70 - pondAbundance * 0.12;

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
            if (ponds && surfaceY > fluidLevel)
            {
                int pondDepth = PondDepthAt(planet, seed, worldX, worldZ, pondThreshold);
                if (pondDepth > 0)
                {
                    seabedY = surfaceY - pondDepth;
                    waterTop = surfaceY;
                    columnFluid = seaWaterId;
                }
            }

            // Per-column biome → surface/sub-surface blocks (single-biome worlds use index 0).
            int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(seed, worldX, worldZ, biomes.Count, _circumference);
            var surfaceId = biomes[biomeIndex].Surface;
            var subSurfaceId = biomes[biomeIndex].Sub;

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

                    continue; // else air above the surface
                }

                int depth = seabedY - worldY;

                // Unmineable world floor (B46): solid bedrock at the very bottom (no caves carved through it),
                // with a lava band just above on real planets — so digging down ends in lava/rock, never a void.
                if (depth >= WorldFloorDepth)
                {
                    chunk.Set(lx, ly, lz, bedrockId);
                    continue;
                }

                if (!airlessBody && depth >= WorldFloorDepth - LavaFloorThickness)
                {
                    chunk.Set(lx, ly, lz, lavaFloorId);
                    continue;
                }

                // Carve caves below the surface layer.
                if (planet.CaveThreshold > 0 && depth > 1)
                {
                    double cave = Noise.ValueCylX(seed + 7777, worldX, worldY, worldZ, _circumference, 22.0, 16.0, 22.0);
                    if (cave > planet.CaveThreshold)
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
                    block = deepId;
                    block = SelectOre(planet, seed, worldX, worldY, worldZ, depth, fallback: block);

                    if (block == deepId && planet.DataCacheRarity > 0 && !dataCacheId.IsAir)
                    {
                        double r = Noise.Value01(seed + 4242, WorldConstants.WrapX(worldX, _circumference), worldY, worldZ);
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
                var floraId = FloraForSurface(planet, surfaceId, seed, worldX, worldZ);
                int fy = seabedY + 1;
                int fly = fy - origin.Y;
                if (!floraId.IsAir && fly >= 0 && fly < WorldConstants.ChunkSize
                    && Noise.Value01(seed + 9001, WorldConstants.WrapX(worldX, _circumference), 7, worldZ) < planet.FloraDensity)
                {
                    chunk.Set(lx, fly, lz, floraId);
                }
            }
            else if (waterFlora && seabedY + 1 <= waterTop)
            {
                // Submerged column — the sea or an upland pond grows kelp/lily pads instead of land plants.
                StampWaterFlora(chunk, origin, lx, lz, seed, worldX, worldZ, seabedY, waterTop,
                    kelpId, lilyId, planet.FloraDensity);
            }
        }

        if (trees)
        {
            StampTrees(planet, seed, chunk, coord, biomes, logId, leafId, treeDensity, fluidLevel);
        }

        return chunk;
    }

    /// <summary>Stamps multi-block trees (a wood trunk + a rounded leaf crown) on grass/earth columns. Scans a
    /// margin around the chunk so a tree straddling a chunk edge generates the same from either chunk; the
    /// per-column roll wraps in X so the longitude seam matches too. Deterministic from the seed.</summary>
    private void StampTrees(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<(BlockId Surface, BlockId Sub)> biomes, BlockId logId, BlockId leafId, double density, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int crown = 2;
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;

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

        for (int wx = origin.X - crown; wx < origin.X + cs + crown; wx++)
        for (int wz = origin.Z - crown; wz < origin.Z + cs + crown; wz++)
        {
            if (Noise.Value01(seed + 5150, WorldConstants.WrapX(wx, _circumference), 11, wz) >= density)
            {
                continue;
            }

            var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(seed, wx, wz, biomes.Count, _circumference)].Surface;
            if (surf != grassId && surf != dirtId && surf != mudId)
            {
                continue; // trees only on grassy / earthy ground
            }

            int sy = SurfaceHeight(planet, wx, wz);
            if (sy + 1 <= fluidLevel)
            {
                continue; // not in the sea
            }

            if (SurfacePondDepth(planet, wx, wz) > 0)
            {
                continue; // B35: an upland pond/lake here — a tree would stand in the water
            }

            int height = 4 + (int)(Noise.Value01(seed + 5151, WorldConstants.WrapX(wx, _circumference), 13, wz) * 3.99); // 4..7
            int topY = sy + height;

            for (int ty = sy + 1; ty <= topY; ty++)
            {
                SetCell(wx, ty, wz, logId, overwrite: true);
            }

            for (int dy = -1; dy <= 2; dy++)
            for (int dx = -crown; dx <= crown; dx++)
            for (int dz = -crown; dz <= crown; dz++)
            {
                if (dx * dx + dz * dz + dy * dy > crown * crown + 1)
                {
                    continue; // a roughly spherical canopy
                }

                SetCell(wx + dx, topY + dy, wz + dz, leafId, overwrite: false);
            }
        }
    }

    private BlockId SelectOre(PlanetType planet, long seed, int x, int y, int z, int depth, BlockId fallback)
    {
        for (int i = 0; i < planet.Ores.Count; i++)
        {
            var ore = planet.Ores[i];
            if (depth < ore.MinDepth || depth > ore.MaxDepth)
            {
                continue;
            }

            // Coarse 3D noise produces vein-like clusters; rarity is the fraction kept.
            double n = Noise.ValueCylX(seed + 100 + i * 31, x, y, z, _circumference, 9.0, 9.0, 9.0);
            if (n > 1.0 - ore.Rarity)
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

    /// <summary>
    /// Resolves the (surface, sub-surface) block ids the planet actually uses. A multi-biome
    /// planet lists a *pool* of biomes; how many of them this world uses is randomised per world
    /// from the seed (2..pool), so each multi-biome world differs. Single-biome → one entry.
    /// </summary>
    private List<(BlockId Surface, BlockId Sub)> ResolveBiomes(PlanetType planet)
    {
        var list = new List<(BlockId, BlockId)>();
        if (planet.Biomes.Count <= 0)
        {
            list.Add((ResolveBlock(planet.SurfaceBlock), ResolveBlock(planet.SubSurfaceBlock)));
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
            list.Add((ResolveBlock(b.SurfaceBlock), ResolveBlock(b.SubSurfaceBlock)));
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
        double n = Noise.FbmCylX(seed ^ 0x0B10E, worldX, worldZ, circumference, 360.0, octaves: 3);
        int idx = (int)(n * count);
        return idx < 0 ? 0 : (idx >= count ? count - 1 : idx);
    }

    /// <summary>Places aquatic flora in one submerged column: a short kelp stalk rooted on the seabed (a few
    /// water cells turned to kelp, leaving the top open water) and, less often, a lily pad on the surface.
    /// Per-column + deterministic from the seed, so no cross-chunk margin is needed (unlike trees).</summary>
    private void StampWaterFlora(ChunkData chunk, Vector3i origin, int lx, int lz, long seed,
        int worldX, int worldZ, int surfaceY, int fluidLevel, BlockId kelpId, BlockId lilyId, double floraDensity)
    {
        int columnDepth = fluidLevel - surfaceY; // water cells above the seabed (>= 1 here)
        double roll = Noise.Value01(seed + 9007, WorldConstants.WrapX(worldX, _circumference), 11, worldZ);

        // Kelp needs at least a little depth; it grows from the seabed up a few cells, capped just below the
        // surface so the top of the column stays open water. Only if this world activated the kelp archetype.
        if (_kelpActive && columnDepth >= 2 && roll < floraDensity * 1.6)
        {
            int height = 2 + (int)(roll * 997) % 3; // 2..4 cells
            int top = System.Math.Min(fluidLevel - 1, surfaceY + height);
            for (int wy = surfaceY + 1; wy <= top; wy++)
            {
                int kly = wy - origin.Y;
                if (kly >= 0 && kly < WorldConstants.ChunkSize)
                {
                    chunk.Set(lx, kly, lz, kelpId);
                }
            }

            return;
        }

        // Otherwise an occasional lily pad floating on the topmost water cell (if the lily archetype is active).
        if (_lilyActive && roll > 1.0 - floraDensity * 0.6)
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
    // surface block id -> the pool of (this world's active) flora that may grow on it.
    private readonly System.Collections.Generic.Dictionary<ushort, BlockId[]> _floraBySurface = new();

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

        var acc = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<BlockId>>();
        foreach (var sp in Spacecraft.Shared.Definitions.FloraCatalog.All)
        {
            if (sp.Aquatic || !active.Contains(sp.Key) || _content.GetBlock(sp.Key) is not { } flora)
            {
                continue; // aquatic flora are placed in submerged columns; inactive forms don't grow here
            }

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
    /// Picks the flora block for a given surface (Air = none). Each surface has a pool of this world's active,
    /// biome-appropriate species; a per-column noise roll selects one so a planet shows a mix rather than a
    /// single plant everywhere.
    /// </summary>
    private BlockId FloraForSurface(PlanetType planet, BlockId surface, long seed, int worldX, int worldZ)
    {
        ResolveFlora(planet);
        if (_floraBySurface.TryGetValue(surface.Value, out var pool) && pool.Length > 0)
        {
            int idx = (int)(Noise.Value01(seed + 9101, WorldConstants.WrapX(worldX, _circumference), 3, worldZ) * pool.Length);
            if (idx >= pool.Length)
            {
                idx = pool.Length - 1;
            }

            return pool[idx < 0 ? 0 : idx];
        }

        return BlockId.Air;
    }

    private BlockId ResolveBlock(string key)
    {
        var def = _content.GetBlock(key)
                  ?? throw new InvalidOperationException($"World generation references unknown block '{key}'.");
        return def.NumericId;
    }
}
