using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The authoritative runtime world for the active planet. Chunks are generated on demand
/// from the seed and have persisted player edits applied on top; only edits are stored
/// (technical requirements §11). Loaded chunks are cached and unloaded when far from all
/// players.
/// </summary>
public sealed class ServerWorld
{
    private readonly GameContent _content;
    private readonly WorldGenerator _generator;
    private readonly IWorldRepository _repo;
    private readonly Dictionary<ChunkCoord, ChunkData> _loaded = new();

    public PlanetType Planet { get; }
    public string PlanetKey { get; }

    /// <summary>
    /// The persistence key for this world's edits/landing-zones/containers. It is the celestial
    /// <b>body id</b> (e.g. "sys0-p1"), not the planet <i>type</i> — so two different bodies of the
    /// same type keep separate edits, and revisiting a planet restores what you left there.
    /// </summary>
    public string LocationId { get; }

    /// <summary>This world's walkable east–west circumference (longitude wrap + the noise circular domain).
    /// Varies by body size (asteroids small, planets large); the seam-free generation + chunk keys all use it.</summary>
    public int Circumference { get; }

    public ServerWorld(GameContent content, WorldGenerator generator, IWorldRepository repo, PlanetType planet, string locationId, int circumference)
    {
        _content = content;
        _generator = generator;
        _repo = repo;
        Planet = planet;
        PlanetKey = planet.Key;
        LocationId = string.IsNullOrEmpty(locationId) ? planet.Key : locationId;
        Circumference = circumference > 0 ? circumference : WorldConstants.Circumference;
    }

    public int LoadedChunkCount => _loaded.Count;

    public ChunkData GetOrLoadChunk(ChunkCoord coord)
    {
        // Longitude wraps: a chunk a whole lap away is the same chunk. Canonicalize X so the cache and
        // persistence stay coherent across the seam (terrain itself is generated seam-free, so generating
        // at the canonical coord yields the identical blocks).
        coord = WorldConstants.CanonicalChunk(coord, Circumference);
        if (_loaded.TryGetValue(coord, out var cached))
        {
            return cached;
        }

        _generator.SetCircumference(Circumference); // generate this world at its own size
        var chunk = _generator.Generate(Planet, coord);
        foreach (var edit in _repo.LoadChunkEdits(LocationId, coord))
        {
            var local = WorldConstants.WorldToLocal(edit.WorldPosition);
            chunk.Set(local.X, local.Y, local.Z, new BlockId(edit.Block));
        }

        _loaded[coord] = chunk;
        return chunk;
    }

    public BlockId GetBlock(Vector3i world)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference); // longitude wraps
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        return chunk.Get(local.X, local.Y, local.Z);
    }

    /// <summary>Sets a block, persists the edit, and returns the previous block id.</summary>
    public BlockId SetBlock(Vector3i world, BlockId block)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference); // longitude wraps: store/cache the canonical column
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        var previous = chunk.Get(local.X, local.Y, local.Z);
        chunk.Set(local.X, local.Y, local.Z, block);
        _repo.SetBlock(LocationId, world, block.Value);
        return previous;
    }

    public BlockDefinition? Definition(BlockId id) => _content.BlockById(id);

    /// <summary>Unloads cached chunks that are not within <paramref name="keep"/> of any anchor.</summary>
    public int UnloadFarChunks(IReadOnlyCollection<ChunkCoord> anchors, int keepRadius)
    {
        if (anchors.Count == 0)
        {
            return 0;
        }

        int keepSq = keepRadius * keepRadius;
        var toRemove = new List<ChunkCoord>();
        foreach (var coord in _loaded.Keys)
        {
            bool near = false;
            foreach (var anchor in anchors)
            {
                if (coord.DistanceSquared(anchor) <= keepSq)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                toRemove.Add(coord);
            }
        }

        foreach (var coord in toRemove)
        {
            _loaded.Remove(coord);
        }

        return toRemove.Count;
    }
}
