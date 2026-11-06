// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The marker-block index (#3275): the cells of a few registered block kinds (the geyser vents) kept per chunk, like
    /// the light sources — filled when a chunk is stored, kept in step by every block change, dropped with the chunk. A
    /// view asks <see cref="MarkersNear"/> instead of walking the block grid around the player (the old geyser view read
    /// ~2 000 cells every 0.75 s and still saw vents only within 7 blocks).
    /// </summary>
    public sealed partial class ClientWorld
    {
        private readonly Dictionary<ChunkCoord, Dictionary<Vector3i, ushort>> _markers = new Dictionary<ChunkCoord, Dictionary<Vector3i, ushort>>();
        private bool[]? _markerIds;
        private ushort[]? _markerList;

        /// <summary>Bumped whenever a marker cell appears or disappears — a view can skip its refresh while it holds still.</summary>
        public int MarkerVersion { get; private set; }

        /// <summary>The number of indexed marker cells (diagnostics, tests).</summary>
        public int MarkerCount
        {
            get
            {
                int n = 0;
                foreach (var bucket in _markers.Values)
                {
                    n += bucket.Count;
                }

                return n;
            }
        }

        /// <summary>Registers the block ids to index (null or empty clears the index) and indexes the chunks already stored,
        /// so a view may register late. Calling it again with the same ids is cheap.</summary>
        public void SetMarkerBlocks(IReadOnlyCollection<ushort>? ids)
        {
            if (SameMarkerIds(ids))
            {
                return;
            }

            _markerIds = ids == null || ids.Count == 0 ? null : IdFlags(ids);
            _markerList = _markerIds == null ? null : new List<ushort>(ids!).ToArray();
            ClearMarkers();
            if (_markerIds == null)
            {
                return;
            }

            foreach (var kv in _chunks)
            {
                ScanChunkMarkers(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// The marker cells within <paramref name="radius"/> blocks horizontally of world column (<paramref name="x"/>,
        /// <paramref name="z"/>) at any height, appended to <paramref name="result"/> in that column's frame (the short way
        /// round both wrap seams, like <see cref="LightSourcesNear(ChunkCoord, int)"/>). Visits every indexed marker:
        /// markers are rare landmarks, so the list is short.
        /// </summary>
        public void MarkersNear(int x, int z, int radius, List<(Vector3i Pos, ushort Block)> result)
        {
            long reach = (long)radius * radius;
            foreach (var bucket in _markers.Values)
            {
                foreach (var kv in bucket)
                {
                    var p = kv.Key;
                    int dx = WorldConstants.WrapDeltaX(p.X - x, _circumference);
                    int dz = WorldConstants.WrapDeltaZ(p.Z - z, _circumference);
                    if ((long)dx * dx + (long)dz * dz <= reach)
                    {
                        result.Add((new Vector3i(x + dx, p.Y, z + dz), kv.Value));
                    }
                }
            }
        }

        private bool SameMarkerIds(IReadOnlyCollection<ushort>? ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return _markerList == null;
            }

            if (_markerList == null || _markerList.Length != ids.Count)
            {
                return false;
            }

            int i = 0;
            foreach (ushort id in ids)
            {
                if (_markerList[i++] != id)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsMarker(ushort id) => _markerIds != null && id < _markerIds.Length && _markerIds[id];

        /// <summary>Indexes a freshly stored chunk's marker cells into its own bucket (built fresh per store).</summary>
        private void ScanChunkMarkers(ChunkCoord coord, ChunkData chunk)
        {
            if (_markerIds == null)
            {
                return;
            }

            bool had = _markers.Remove(coord);
            Dictionary<Vector3i, ushort>? bucket = null;
            var raw = chunk.RawBlocks;
            var origin = WorldConstants.ChunkOrigin(coord);
            int nsz = WorldConstants.ChunkSize;
            for (int x = 0; x < nsz; x++)
                for (int y = 0; y < nsz; y++)
                    for (int z = 0; z < nsz; z++)
                    {
                        ushort id = raw[WorldConstants.LocalIndex(x, y, z)];
                        if (id != 0 && IsMarker(id))
                        {
                            bucket ??= new Dictionary<Vector3i, ushort>();
                            bucket[new Vector3i(origin.X + x, origin.Y + y, origin.Z + z)] = id;
                        }
                    }

            if (bucket != null)
            {
                _markers[coord] = bucket;
            }

            if (had || bucket != null)
            {
                MarkerVersion++;
            }
        }

        /// <summary>Keeps one changed cell's entry in step (a vent mined away, or placed back).</summary>
        private void IndexMarker(ChunkCoord coord, Vector3i pos, ushort block)
        {
            if (_markerIds == null)
            {
                return;
            }

            if (IsMarker(block))
            {
                if (!_markers.TryGetValue(coord, out var bucket))
                {
                    bucket = new Dictionary<Vector3i, ushort>();
                    _markers[coord] = bucket;
                }

                if (!bucket.TryGetValue(pos, out ushort old) || old != block)
                {
                    bucket[pos] = block;
                    MarkerVersion++;
                }
            }
            else if (_markers.TryGetValue(coord, out var bucket) && bucket.Remove(pos))
            {
                if (bucket.Count == 0)
                {
                    _markers.Remove(coord);
                }

                MarkerVersion++;
            }
        }

        private void RemoveChunkMarkers(ChunkCoord coord)
        {
            if (_markers.Remove(coord))
            {
                MarkerVersion++;
            }
        }

        private void ClearMarkers()
        {
            if (_markers.Count > 0)
            {
                _markers.Clear();
                MarkerVersion++;
            }
        }
    }
}
