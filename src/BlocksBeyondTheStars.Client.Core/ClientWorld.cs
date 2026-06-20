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

        // World positions of coloured light sources (placed glow blocks + dedicated light blocks) → light
        // colour (0xRRGGBB). Lets the chunk mesher pull in nearby lights ACROSS chunk seams so a placed lamp's
        // colour propagates into neighbouring chunks, not just its own.
        private readonly Dictionary<Vector3i, int> _lightSources = new Dictionary<Vector3i, int>();

        // The inherent light colour of a block id (0 = not a light block), supplied once from GameContent.
        private System.Func<ushort, int> _blockLightColor = _ => 0;

        public IReadOnlyDictionary<ChunkCoord, ChunkData> Chunks => _chunks;

        /// <summary>Sets the world circumference (per-body size) so the wrap matches the server.</summary>
        public void SetCircumference(int circumference)
            => _circumference = circumference > 0 ? circumference : WorldConstants.Circumference;

        /// <summary>Provides the block-id → inherent-light-colour lookup (from the content registry) used to
        /// index dedicated light blocks as light sources.</summary>
        public void SetBlockLightResolver(System.Func<ushort, int> resolver) => _blockLightColor = resolver;

        // Round worlds: chunks are cached by canonical chunk coordinate (a chunk a lap away — east OR
        // north — is the same chunk), and block lookups canonicalize X AND Z so an unbounded player
        // coordinate still resolves after laps in any direction.
        public void StoreChunk(ChunkCoord coord, ushort[] blocks, int[]? modIndex = null, int[]? modTint = null, int[]? modGlow = null,
            int[]? shapeIndex = null, int[]? shapeData = null)
        {
            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            var chunk = ChunkData.FromRaw(coord, blocks);

            // Restore the dyed/glowing cells that came with the chunk (sparse parallel arrays).
            if (modIndex != null)
            {
                for (int i = 0; i < modIndex.Length; i++)
                {
                    int t = modTint != null && i < modTint.Length ? modTint[i] : 0;
                    int g = modGlow != null && i < modGlow.Length ? modGlow[i] : 0;
                    chunk.SetModifierLocal(modIndex[i], t, g);
                }
            }

            // Restore the shaped (non-cube) cells that came with the chunk (its own sparse parallel array).
            if (shapeIndex != null)
            {
                for (int i = 0; i < shapeIndex.Length; i++)
                {
                    int s = shapeData != null && i < shapeData.Length ? shapeData[i] : 0;
                    chunk.SetShapeLocal(shapeIndex[i], s);
                }
            }

            _chunks[coord] = chunk;
            ScanChunkLightSources(coord, chunk);
        }

        /// <summary>Drops all cached chunks (used when travelling to another world).</summary>
        public void Clear()
        {
            _chunks.Clear();
            _lightSources.Clear();
        }

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

        /// <summary>The packed shape descriptor (non-cube building form + orientation; 0 = plain cube) at a
        /// world cell — handed to the mesher so a cube next to a shaped neighbour still draws the face between
        /// them, across chunk seams. Unknown/unloaded cells are treated as plain cubes (0).</summary>
        public int GetShape(int wx, int wy, int wz)
        {
            var pos = WorldConstants.CanonicalBlock(new Vector3i(wx, wy, wz), _circumference);
            var coord = WorldConstants.WorldToChunk(pos);
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return 0;
            }

            var local = WorldConstants.WorldToLocal(pos);
            return chunk.GetShape(local.X, local.Y, local.Z);
        }

        /// <summary>Applies a single authoritative block change (no colour modifier) from the server.</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, out ChunkCoord affected)
            => ApplyBlockChange(wx, wy, wz, block, 0, 0, 0, out affected);

        /// <summary>Applies a single authoritative block change with a colour modifier but no shape (back-compat).</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, int tint, int glow, out ChunkCoord affected)
            => ApplyBlockChange(wx, wy, wz, block, tint, glow, 0, out affected);

        /// <summary>Applies a single authoritative block change, carrying the placed cell's colour modifier
        /// (dyed surface tint / glow light colour; 0 = none) and its shape descriptor (0 = plain cube), and
        /// keeps the light-source registry in sync.</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, int tint, int glow, int shape, out ChunkCoord affected)
        {
            var pos = WorldConstants.CanonicalBlock(new Vector3i(wx, wy, wz), _circumference);
            affected = WorldConstants.WorldToChunk(pos);
            if (!_chunks.TryGetValue(affected, out var chunk))
            {
                return false;
            }

            var local = WorldConstants.WorldToLocal(pos);
            chunk.Set(local.X, local.Y, local.Z, new BlockId(block)); // clears any old modifier/shape when set to air
            chunk.SetModifier(local.X, local.Y, local.Z, tint, glow);
            chunk.SetShape(local.X, local.Y, local.Z, shape);

            int rgb = glow != 0 ? glow : (block != BlockId.AirValue && _blockLightColor != null ? _blockLightColor(block) : 0);
            if (rgb != 0)
            {
                _lightSources[pos] = rgb;
            }
            else
            {
                _lightSources.Remove(pos);
            }

            return true;
        }

        /// <summary>Light sources within <paramref name="radius"/> blocks of a chunk's box — handed to the
        /// mesher so a placed lamp's colour floods across chunk seams, not just its own chunk.</summary>
        public List<(Vector3i Pos, int Rgb)> LightSourcesNear(ChunkCoord coord, int radius)
        {
            var result = new List<(Vector3i, int)>();
            if (_lightSources.Count == 0)
            {
                return result;
            }

            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            var origin = WorldConstants.ChunkOrigin(coord);
            int nsz = WorldConstants.ChunkSize;
            int loX = origin.X - radius, hiX = origin.X + nsz + radius;
            int loY = origin.Y - radius, hiY = origin.Y + nsz + radius;
            int loZ = origin.Z - radius, hiZ = origin.Z + nsz + radius;
            foreach (var kv in _lightSources)
            {
                var p = kv.Key;
                if (p.X < loX || p.X > hiX || p.Y < loY || p.Y > hiY || p.Z < loZ || p.Z > hiZ)
                {
                    continue;
                }

                result.Add((p, kv.Value));
            }

            return result;
        }

        /// <summary>Re-indexes a chunk's light sources (placed glow blocks + dedicated light blocks).</summary>
        private void ScanChunkLightSources(ChunkCoord coord, ChunkData chunk)
        {
            var origin = WorldConstants.ChunkOrigin(coord);
            int nsz = WorldConstants.ChunkSize;
            for (int x = 0; x < nsz; x++)
            for (int y = 0; y < nsz; y++)
            for (int z = 0; z < nsz; z++)
            {
                var id = chunk.Get(x, y, z);
                var pos = new Vector3i(origin.X + x, origin.Y + y, origin.Z + z);
                int rgb = 0;
                if (!id.IsAir)
                {
                    var (_, glow) = chunk.GetModifier(x, y, z);
                    rgb = glow != 0 ? glow : (_blockLightColor != null ? _blockLightColor(id.Value) : 0);
                }

                if (rgb != 0)
                {
                    _lightSources[pos] = rgb;
                }
                else
                {
                    _lightSources.Remove(pos);
                }
            }
        }
    }
}
