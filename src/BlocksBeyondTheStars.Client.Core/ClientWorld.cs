// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
        // colour (0xRRGGBB), bucketed per canonical chunk (#1515). Lets the chunk mesher pull in nearby lights
        // ACROSS chunk seams so a placed lamp's colour propagates into neighbouring chunks, not just its own.
        // Per-chunk buckets make unloading a chunk one dictionary remove (it used to be 4096 removes per chunk,
        // 200k in one frame for a 50-chunk unload batch) and let LightSourcesNear visit only the neighbouring
        // chunks instead of every light in the world per mesh dispatch.
        private readonly Dictionary<ChunkCoord, Dictionary<Vector3i, int>> _lightSources = new Dictionary<ChunkCoord, Dictionary<Vector3i, int>>();

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

        /// <summary>Drops a single chunk the client has travelled well out of view of, so the client-side chunk
        /// cache (and its light-source index) stays bounded over a long exploration instead of growing for the
        /// whole distance travelled. The server's far-chunk sweep forgets the same chunk, so it re-streams fresh
        /// if the player returns. Returns whether a chunk was actually removed.</summary>
        public bool RemoveChunk(ChunkCoord coord)
        {
            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            if (!_chunks.Remove(coord))
            {
                return false;
            }

            // Clear this chunk's glow-block light sources too: one bucket remove (#1515).
            _lightSources.Remove(coord);
            return true;
        }

        public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk)
            => _chunks.TryGetValue(WorldConstants.CanonicalChunk(coord, _circumference), out chunk);

        public BlockId GetBlock(int wx, int wy, int wz)
        {
            TryGetBlock(wx, wy, wz, out var block);
            return block;
        }

        /// <summary>Like <see cref="GetBlock"/> but reports whether the cell's chunk has actually been
        /// streamed: false = unknown (chunk not loaded), with <paramref name="block"/> = air. For callers
        /// that must not mistake not-yet-streamed terrain for open space (the prologue stage scan).</summary>
        public bool TryGetBlock(int wx, int wy, int wz, out BlockId block)
        {
            var pos = WorldConstants.CanonicalBlock(new Vector3i(wx, wy, wz), _circumference);
            var coord = WorldConstants.WorldToChunk(pos);
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                block = BlockId.Air;
                return false;
            }

            var local = WorldConstants.WorldToLocal(pos);
            block = chunk.Get(local.X, local.Y, local.Z);
            return true;
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

            // Light colour priority (#1126): an explicit glow always wins; otherwise a block that IS a light
            // source (base colour non-zero) casts its DYE colour when dyed — a red-dyed lamp floods red — and
            // its natural colour when plain. A dye on a non-source block never turns it into a lamp.
            int baseRgb = block != BlockId.AirValue && _blockLightColor != null ? _blockLightColor(block) : 0;
            int rgb = glow != 0 ? glow : (baseRgb != 0 && tint != 0 ? tint : baseRgb);
            if (rgb != 0)
            {
                if (!_lightSources.TryGetValue(affected, out var bucket))
                {
                    bucket = new Dictionary<Vector3i, int>();
                    _lightSources[affected] = bucket;
                }

                bucket[pos] = rgb;
            }
            else if (_lightSources.TryGetValue(affected, out var bucket) && bucket.Remove(pos) && bucket.Count == 0)
            {
                _lightSources.Remove(affected);
            }

            return true;
        }

        /// <summary>Light sources within <paramref name="radius"/> blocks of a chunk's box — handed to the
        /// mesher so a placed lamp's colour floods across chunk seams, not just its own chunk.
        /// Round worlds (#428): the sources are stored canonically, but the distance is measured the short way
        /// round BOTH wrap seams and every hit is re-expressed in the chunk's own (un-wrapped) frame — so a
        /// lamp at X = circumference − 2 reaches the chunk at X = 0 as X = −2, and the mesher's flood fill
        /// (which keys cells in that raw frame and canonicalizes block lookups) carries light across the seam
        /// like skylight and AO already do.</summary>
        public List<(Vector3i Pos, int Rgb)> LightSourcesNear(ChunkCoord coord, int radius)
        {
            var result = new List<(Vector3i, int)>();
            LightSourcesNear(coord, radius, result);
            return result;
        }

        /// <summary>Same as <see cref="LightSourcesNear(ChunkCoord, int)"/>, appending into a caller-owned list
        /// (#1550: the mesher dispatch reuses one list per pooled job).</summary>
        public void LightSourcesNear(ChunkCoord coord, int radius, List<(Vector3i Pos, int Rgb)> result)
        {
            if (_lightSources.Count == 0)
            {
                return;
            }

            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            var origin = WorldConstants.ChunkOrigin(coord);
            int nsz = WorldConstants.ChunkSize;
            int lo = -radius, hi = nsz + radius;

            // #1515: only the chunks that can hold a source within `radius` blocks of this chunk's box are
            // visited (the 3×3×3 neighbourhood for the mesher's radius) — the per-source distance test below is
            // unchanged, so the result set is exactly what the world-wide scan produced.
            int span = (radius + nsz - 1) / nsz;
            for (int cx = -span; cx <= span; cx++)
                for (int cy = -span; cy <= span; cy++)
                    for (int cz = -span; cz <= span; cz++)
                    {
                        var neighbour = WorldConstants.CanonicalChunk(new ChunkCoord(coord.X + cx, coord.Y + cy, coord.Z + cz), _circumference);
                        if (!_lightSources.TryGetValue(neighbour, out var bucket))
                        {
                            continue;
                        }

                        foreach (var kv in bucket)
                        {
                            var p = kv.Key;
                            int dy = p.Y - origin.Y;
                            if (dy < lo || dy > hi)
                            {
                                continue;
                            }

                            int dx = WorldConstants.WrapDeltaX(p.X - origin.X, _circumference);
                            int dz = WorldConstants.WrapDeltaZ(p.Z - origin.Z, _circumference);
                            if (dx < lo || dx > hi || dz < lo || dz > hi)
                            {
                                continue;
                            }

                            result.Add((new Vector3i(origin.X + dx, p.Y, origin.Z + dz), kv.Value));
                        }
                    }

        }

        /// <summary>Re-indexes a chunk's light sources (placed glow blocks + dedicated light blocks) into the
        /// chunk's own bucket — built fresh per store, so no per-cell removes for the (usual) all-dark chunk.</summary>
        private void ScanChunkLightSources(ChunkCoord coord, ChunkData chunk)
        {
            var origin = WorldConstants.ChunkOrigin(coord);
            int nsz = WorldConstants.ChunkSize;
            Dictionary<Vector3i, int>? bucket = null;
            for (int x = 0; x < nsz; x++)
                for (int y = 0; y < nsz; y++)
                    for (int z = 0; z < nsz; z++)
                    {
                        var id = chunk.Get(x, y, z);
                        if (id.IsAir)
                        {
                            continue;
                        }

                        // Same priority as ApplyBlockChange (#1126): glow > dye-on-a-light-source > natural.
                        var (tint, glow) = chunk.GetModifier(x, y, z);
                        int baseRgb = _blockLightColor != null ? _blockLightColor(id.Value) : 0;
                        int rgb = glow != 0 ? glow : (baseRgb != 0 && tint != 0 ? tint : baseRgb);
                        if (rgb != 0)
                        {
                            bucket ??= new Dictionary<Vector3i, int>();
                            bucket[new Vector3i(origin.X + x, origin.Y + y, origin.Z + z)] = rgb;
                        }
                    }

            if (bucket != null)
            {
                _lightSources[coord] = bucket;
            }
            else
            {
                _lightSources.Remove(coord);
            }
        }
    }
}
