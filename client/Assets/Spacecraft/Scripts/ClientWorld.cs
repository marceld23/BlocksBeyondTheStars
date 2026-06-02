using System.Collections.Generic;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;

namespace Spacecraft.Client
{
    /// <summary>
    /// Client-side cache of chunks received from the server. This is a *view* of the
    /// authoritative world, not the source of truth: edits arrive as server messages.
    /// </summary>
    public sealed class ClientWorld
    {
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

        public IReadOnlyDictionary<ChunkCoord, ChunkData> Chunks => _chunks;

        public void StoreChunk(ChunkCoord coord, ushort[] blocks)
            => _chunks[coord] = ChunkData.FromRaw(coord, blocks);

        /// <summary>Drops all cached chunks (used when travelling to another world).</summary>
        public void Clear() => _chunks.Clear();

        public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk) => _chunks.TryGetValue(coord, out chunk);

        public BlockId GetBlock(int wx, int wy, int wz)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(wx, wy, wz));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return BlockId.Air;
            }

            var local = WorldConstants.WorldToLocal(new Vector3i(wx, wy, wz));
            return chunk.Get(local.X, local.Y, local.Z);
        }

        /// <summary>Applies a single authoritative block change from the server.</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, out ChunkCoord affected)
        {
            affected = WorldConstants.WorldToChunk(new Vector3i(wx, wy, wz));
            if (!_chunks.TryGetValue(affected, out var chunk))
            {
                return false;
            }

            var local = WorldConstants.WorldToLocal(new Vector3i(wx, wy, wz));
            chunk.Set(local.X, local.Y, local.Z, new BlockId(block));
            return true;
        }
    }
}
