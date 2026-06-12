using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Client-side cache of chunks received from the server. This is a *view* of the
    /// authoritative world, not the source of truth: edits arrive as server messages.
    /// </summary>
    public sealed class ClientWorld
    {
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

        // This world's circumference (set from WorldEnvironment) — chunk/block X wrap at the right size.
        private int _circumference = WorldConstants.Circumference;

        public IReadOnlyDictionary<ChunkCoord, ChunkData> Chunks => _chunks;

        /// <summary>Sets the world circumference (per-body size) so the wrap matches the server.</summary>
        public void SetCircumference(int circumference)
            => _circumference = circumference > 0 ? circumference : WorldConstants.Circumference;

        // Round worlds: chunks are cached by canonical chunk coordinate (a chunk a lap away — east OR
        // north — is the same chunk), and block lookups canonicalize X AND Z so an unbounded player
        // coordinate still resolves after laps in any direction.
        public void StoreChunk(ChunkCoord coord, ushort[] blocks)
        {
            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            _chunks[coord] = ChunkData.FromRaw(coord, blocks);
        }

        /// <summary>Drops all cached chunks (used when travelling to another world).</summary>
        public void Clear() => _chunks.Clear();

        public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk)
            => _chunks.TryGetValue(WorldConstants.CanonicalChunk(coord, _circumference), out chunk);

        public BlockId GetBlock(int wx, int wy, int wz)
        {
            var pos = WorldConstants.CanonicalBlock(new Vector3i(wx, wy, wz), _circumference);
            var coord = WorldConstants.WorldToChunk(pos);
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return BlockId.Air;
            }

            var local = WorldConstants.WorldToLocal(pos);
            return chunk.Get(local.X, local.Y, local.Z);
        }

        /// <summary>Applies a single authoritative block change from the server.</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, out ChunkCoord affected)
        {
            var pos = WorldConstants.CanonicalBlock(new Vector3i(wx, wy, wz), _circumference);
            affected = WorldConstants.WorldToChunk(pos);
            if (!_chunks.TryGetValue(affected, out var chunk))
            {
                return false;
            }

            var local = WorldConstants.WorldToLocal(pos);
            chunk.Set(local.X, local.Y, local.Z, new BlockId(block));
            return true;
        }
    }
}
