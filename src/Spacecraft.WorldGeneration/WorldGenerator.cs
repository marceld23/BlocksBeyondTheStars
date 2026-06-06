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

    public WorldGenerator(long worldSeed, GameContent content)
    {
        _worldSeed = worldSeed;
        _content = content;
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
        double n = Noise.FbmCylX(seed, worldX, worldZ, WorldConstants.Circumference, planet.TerrainScale, octaves: 4);
        double h = (n - 0.5) * 2.0; // [-1, 1] base rolling terrain

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
        double rug = Noise.FbmCylX(s, worldX, worldZ, WorldConstants.Circumference, planet.TerrainScale * 6.0, octaves: 3);
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
        double lavaAb = planet.LavaAbundance ?? (volcanic ? 0.55 : 0.0);

        // Sea level sits BELOW the average surface (BaseHeight) so only genuine low ground floods (valleys,
        // canyon floors, basins) — not half the world. A higher abundance raises it toward more water.
        if (waterAb > 0.0 && _content.GetBlock("water") is { } water)
        {
            int level = planet.BaseHeight + (int)System.Math.Round((waterAb - 0.95) * planet.Amplitude);
            return (level, water.NumericId);
        }

        // Watery worlds get no surface lava; only dry volcanic/airless worlds pool lava in their basins.
        if (waterAb <= 0.0 && lavaAb > 0.0 && _content.GetBlock("lava") is { } lava)
        {
            int level = planet.BaseHeight + (int)System.Math.Round((lavaAb - 0.8) * planet.Amplitude);
            return (level, lava.NumericId);
        }

        return (int.MinValue, BlockId.Air);
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
        bool waterFlora = flora && !kelpId.IsAir && !lilyId.IsAir && fluidId == seaWaterId && !seaWaterId.IsAir;

        var origin = WorldConstants.ChunkOrigin(coord);

        for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
        for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
        {
            int worldX = origin.X + lx;
            int worldZ = origin.Z + lz;
            int surfaceY = SurfaceHeight(planet, worldX, worldZ);

            // Per-column biome → surface/sub-surface blocks (single-biome worlds use index 0).
            int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(seed, worldX, worldZ, biomes.Count);
            var surfaceId = biomes[biomeIndex].Surface;
            var subSurfaceId = biomes[biomeIndex].Sub;

            for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
            {
                int worldY = origin.Y + ly;
                if (worldY > surfaceY)
                {
                    if (worldY <= fluidLevel)
                    {
                        chunk.Set(lx, ly, lz, fluidId); // sea fill in a basin below the world's sea level
                    }

                    continue; // else air above the surface
                }

                int depth = surfaceY - worldY;

                // Carve caves below the surface layer.
                if (planet.CaveThreshold > 0 && depth > 1)
                {
                    double cave = Noise.ValueCylX(seed + 7777, worldX, worldY, worldZ, WorldConstants.Circumference, 22.0, 16.0, 22.0);
                    if (cave > planet.CaveThreshold)
                    {
                        continue; // cave => air
                    }
                }

                BlockId block;
                if (depth < planet.SurfaceDepth)
                {
                    block = depth == 0 ? surfaceId : subSurfaceId;
                }
                else
                {
                    block = deepId;
                    block = SelectOre(planet, seed, worldX, worldY, worldZ, depth, fallback: block);

                    if (block == deepId && planet.DataCacheRarity > 0 && !dataCacheId.IsAir)
                    {
                        double r = Noise.Value01(seed + 4242, WorldConstants.WrapX(worldX), worldY, worldZ);
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
            if (flora && surfaceY + 1 > fluidLevel)
            {
                var floraId = FloraForSurface(surfaceId, seed, worldX, worldZ);
                int fy = surfaceY + 1;
                int fly = fy - origin.Y;
                if (!floraId.IsAir && fly >= 0 && fly < WorldConstants.ChunkSize
                    && Noise.Value01(seed + 9001, WorldConstants.WrapX(worldX), 7, worldZ) < planet.FloraDensity)
                {
                    chunk.Set(lx, fly, lz, floraId);
                }
            }
            else if (waterFlora && surfaceY + 1 <= fluidLevel)
            {
                StampWaterFlora(chunk, origin, lx, lz, seed, worldX, worldZ, surfaceY, fluidLevel,
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
            if (Noise.Value01(seed + 5150, WorldConstants.WrapX(wx), 11, wz) >= density)
            {
                continue;
            }

            var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(seed, wx, wz, biomes.Count)].Surface;
            if (surf != grassId && surf != dirtId && surf != mudId)
            {
                continue; // trees only on grassy / earthy ground
            }

            int sy = SurfaceHeight(planet, wx, wz);
            if (sy + 1 <= fluidLevel)
            {
                continue; // not in the sea
            }

            int height = 4 + (int)(Noise.Value01(seed + 5151, WorldConstants.WrapX(wx), 13, wz) * 3.99); // 4..7
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
            double n = Noise.ValueCylX(seed + 100 + i * 31, x, y, z, WorldConstants.Circumference, 9.0, 9.0, 9.0);
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
        return count <= 1 ? 0 : BiomeIndex(PlanetSeed(planet), worldX, worldZ, count);
    }

    /// <summary>How many distinct biomes this planet's world uses.</summary>
    public int BiomeCount(PlanetType planet) => ResolveBiomes(planet).Count;

    /// <summary>Broad low-frequency noise picks a biome per column (multi-biome worlds). The scale is
    /// large so each biome is a big contiguous region (so per-biome weather covers a meaningful area).</summary>
    private static int BiomeIndex(long seed, int worldX, int worldZ, int count)
    {
        double n = Noise.FbmCylX(seed ^ 0x0B10E, worldX, worldZ, WorldConstants.Circumference, 360.0, octaves: 3);
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
        double roll = Noise.Value01(seed + 9007, WorldConstants.WrapX(worldX), 11, worldZ);

        // Kelp needs at least a little depth; it grows from the seabed up a few cells, capped just below the
        // surface so the top of the column stays open water.
        if (columnDepth >= 2 && roll < floraDensity * 1.6)
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

        // Otherwise an occasional lily pad floating on the topmost water cell.
        if (roll > 1.0 - floraDensity * 0.6)
        {
            int lily = fluidLevel - origin.Y;
            if (lily >= 0 && lily < WorldConstants.ChunkSize)
            {
                chunk.Set(lx, lily, lz, lilyId);
            }
        }
    }

    private bool _floraResolved;
    // surface block id -> the pool of flora that may grow on it (from FloraCatalog).
    private readonly System.Collections.Generic.Dictionary<ushort, BlockId[]> _floraBySurface = new();

    /// <summary>
    /// Picks the flora block for a given surface (Air = none). Each surface has a pool of
    /// biome-appropriate species (<see cref="Spacecraft.Shared.Definitions.FloraCatalog"/>); a per-column
    /// noise roll selects one so a planet shows a mix rather than a single plant everywhere.
    /// </summary>
    private BlockId FloraForSurface(BlockId surface, long seed, int worldX, int worldZ)
    {
        if (!_floraResolved)
        {
            _floraResolved = true;
            var acc = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<BlockId>>();
            foreach (var sp in Spacecraft.Shared.Definitions.FloraCatalog.All)
            {
                if (sp.Aquatic || _content.GetBlock(sp.Key) is not { } flora)
                {
                    continue; // aquatic flora are placed directly in submerged columns, not on land surfaces
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

        if (_floraBySurface.TryGetValue(surface.Value, out var pool) && pool.Length > 0)
        {
            int idx = (int)(Noise.Value01(seed + 9101, WorldConstants.WrapX(worldX), 3, worldZ) * pool.Length);
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
