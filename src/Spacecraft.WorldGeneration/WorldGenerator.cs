using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
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

    /// <summary>Computes the surface height (world Y) of a column for a planet.</summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        long seed = PlanetSeed(planet);
        double n = Noise.Fbm2D(seed, worldX / planet.TerrainScale, worldZ / planet.TerrainScale, octaves: 4);
        return planet.BaseHeight + (int)System.Math.Round((n - 0.5) * 2.0 * planet.Amplitude);
    }

    public ChunkData Generate(PlanetType planet, ChunkCoord coord)
    {
        var chunk = new ChunkData(coord);
        long seed = PlanetSeed(planet);

        var biomes = ResolveBiomes(planet);
        var deepId = ResolveBlock(planet.DeepBlock);
        var dataCacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;

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
                    continue; // air above the surface
                }

                int depth = surfaceY - worldY;

                // Carve caves below the surface layer.
                if (planet.CaveThreshold > 0 && depth > 1)
                {
                    double cave = Noise.Value3D(seed + 7777, worldX / 22.0, worldY / 16.0, worldZ / 22.0);
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
                        double r = Noise.Value01(seed + 4242, worldX, worldY, worldZ);
                        if (r < planet.DataCacheRarity)
                        {
                            block = dataCacheId;
                        }
                    }
                }

                chunk.Set(lx, ly, lz, block);
            }
        }

        return chunk;
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
            double n = Noise.Value3D(seed + 100 + i * 31, x / 9.0, y / 9.0, z / 9.0);
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

    /// <summary>Broad low-frequency noise picks a biome per column (multi-biome worlds).</summary>
    private static int BiomeIndex(long seed, int worldX, int worldZ, int count)
    {
        double n = Noise.Fbm2D(seed ^ 0x0B10E, worldX / 140.0, worldZ / 140.0, octaves: 3);
        int idx = (int)(n * count);
        return idx < 0 ? 0 : (idx >= count ? count - 1 : idx);
    }

    private BlockId ResolveBlock(string key)
    {
        var def = _content.GetBlock(key)
                  ?? throw new InvalidOperationException($"World generation references unknown block '{key}'.");
        return def.NumericId;
    }
}
