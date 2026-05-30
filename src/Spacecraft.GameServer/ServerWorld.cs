using Spacecraft.Persistence;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

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

    public ServerWorld(GameContent content, WorldGenerator generator, IWorldRepository repo, PlanetType planet)
    {
        _content = content;
        _generator = generator;
        _repo = repo;
        Planet = planet;
        PlanetKey = planet.Key;
    }

    public int LoadedChunkCount => _loaded.Count;

    public ChunkData GetOrLoadChunk(ChunkCoord coord)
    {
        if (_loaded.TryGetValue(coord, out var cached))
        {
            return cached;
        }

        var chunk = _generator.Generate(Planet, coord);
        foreach (var edit in _repo.LoadChunkEdits(PlanetKey, coord))
        {
            var local = WorldConstants.WorldToLocal(edit.WorldPosition);
            chunk.Set(local.X, local.Y, local.Z, new BlockId(edit.Block));
        }

        _loaded[coord] = chunk;
        return chunk;
    }

    public BlockId GetBlock(Vector3i world)
    {
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        return chunk.Get(local.X, local.Y, local.Z);
    }

    /// <summary>Sets a block, persists the edit, and returns the previous block id.</summary>
    public BlockId SetBlock(Vector3i world, BlockId block)
    {
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        var previous = chunk.Get(local.X, local.Y, local.Z);
        chunk.Set(local.X, local.Y, local.Z, block);
        _repo.SetBlock(PlanetKey, world, block.Value);
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
