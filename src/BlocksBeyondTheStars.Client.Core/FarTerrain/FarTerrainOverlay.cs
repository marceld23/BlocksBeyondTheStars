// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>The highest persisted build in one 4×4-block cell (#1821).</summary>
    public readonly struct FarEditTop
    {
        /// <summary>World Y of the top face (the top block's Y + 1).</summary>
        public readonly int Top;
        public readonly ushort Block;
        public readonly int Tint;

        public FarEditTop(int top, ushort block, int tint)
        {
            Top = top;
            Block = block;
            Tint = tint;
        }
    }

    /// <summary>
    /// What the server told the far view about builds (#1821): tiles of 4×4-block cells holding the top non-air edit.
    /// Keyed on the canonical tile grid; lookups take unwrapped scene coordinates and wrap them. Also plans which tiles
    /// to ask for next (nearest first, never twice) and remembers which tiles changed so the view can rebuild the
    /// patches under them.
    /// </summary>
    public sealed class FarTerrainOverlay
    {
        private readonly Dictionary<(int Tx, int Tz), Dictionary<int, FarEditTop>> _tiles = new Dictionary<(int, int), Dictionary<int, FarEditTop>>();
        private readonly Dictionary<(int Tx, int Tz), int> _versions = new Dictionary<(int, int), int>();
        private readonly HashSet<(int Tx, int Tz)> _requested = new HashSet<(int, int)>();
        private readonly List<(int Tx, int Tz)> _changed = new List<(int, int)>();

        public int Circumference { get; private set; } = WorldConstants.Circumference;

        /// <summary>Tiles received so far (diagnostics / tests).</summary>
        public int TileCount => _tiles.Count;

        /// <summary>Forgets everything (a new world).</summary>
        public void Reset(int circumference)
        {
            Circumference = circumference > 0 ? circumference : WorldConstants.Circumference;
            _tiles.Clear();
            _versions.Clear();
            _requested.Clear();
            _changed.Clear();
        }

        /// <summary>Takes a tile from the server. Returns true when it changed what the far view shows.</summary>
        public bool Apply(FarTerrainTile tile)
        {
            var key = (tile.TileX, tile.TileZ);
            _requested.Add(key);
            if (_versions.TryGetValue(key, out int have) && have >= tile.Version)
            {
                return false; // a stale re-send
            }

            _versions[key] = tile.Version;
            bool hadAny = _tiles.TryGetValue(key, out var old) && old.Count > 0;
            int n = Math.Min(tile.Cells?.Length ?? 0, Math.Min(tile.TopY?.Length ?? 0, Math.Min(tile.Blocks?.Length ?? 0, tile.Tints?.Length ?? 0)));
            if (n == 0)
            {
                _tiles.Remove(key);
                if (hadAny)
                {
                    _changed.Add(key);
                }

                return hadAny;
            }

            var cells = new Dictionary<int, FarEditTop>(n);
            for (int i = 0; i < n; i++)
            {
                cells[tile.Cells![i]] = new FarEditTop(tile.TopY![i] + 1, tile.Blocks![i], tile.Tints![i]);
            }

            _tiles[key] = cells;
            _changed.Add(key);
            return true;
        }

        /// <summary>The build in the 4×4 cell holding a (scene) column, if any.</summary>
        public bool TryGetTop(int worldX, int worldZ, out FarEditTop top)
        {
            top = default;
            if (_tiles.Count == 0)
            {
                return false;
            }

            var canon = WorldConstants.CanonicalBlock(new Vector3i(worldX, 0, worldZ), Circumference);
            var key = (canon.X >> 6, canon.Z >> 6);
            if (!_tiles.TryGetValue(key, out var cells))
            {
                return false;
            }

            int cx = (canon.X - (key.Item1 << 6)) >> 2;
            int cz = (canon.Z - (key.Item2 << 6)) >> 2;
            return cells.TryGetValue(cz * FarTerrainTile.CellsPerSide + cx, out top);
        }

        /// <summary>The highest build over a square area of (scene) columns, stepping 4 blocks; false when none.</summary>
        public bool TryGetTopInArea(int minX, int minZ, int size, out FarEditTop best)
        {
            best = default;
            if (_tiles.Count == 0)
            {
                return false;
            }

            bool any = false;
            for (int x = minX; x < minX + size; x += FarTerrainTile.CellBlocks)
                for (int z = minZ; z < minZ + size; z += FarTerrainTile.CellBlocks)
                {
                    if (TryGetTop(x, z, out var top) && (!any || top.Top > best.Top))
                    {
                        best = top;
                        any = true;
                    }
                }

            return any;
        }

        /// <summary>Up to <paramref name="max"/> not-yet-requested tiles within <paramref name="range"/> of a scene
        /// position, nearest first, as flat (tx, tz) pairs — marked requested.</summary>
        public int[] NextRequest(float sceneX, float sceneZ, int range, int max)
        {
            if (range <= 0 || max <= 0)
            {
                return Array.Empty<int>();
            }

            int t = FarTerrainTile.TileBlocks;
            int r = (range + t - 1) / t;
            int cx = (int)Math.Floor(sceneX / t);
            int cz = (int)Math.Floor(sceneZ / t);
            var candidates = new List<(int Tx, int Tz, int D)>();
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int d = dx * dx + dz * dz;
                    if (d > r * r)
                    {
                        continue;
                    }

                    var canon = WorldConstants.CanonicalBlock(new Vector3i((cx + dx) * t, 0, (cz + dz) * t), Circumference);
                    var key = (canon.X >> 6, canon.Z >> 6);
                    if (!_requested.Contains(key))
                    {
                        candidates.Add((key.Item1, key.Item2, d));
                    }
                }

            candidates.Sort((a, b) => a.D.CompareTo(b.D));
            var pairs = new List<int>(Math.Min(max, candidates.Count) * 2);
            foreach (var c in candidates)
            {
                if (pairs.Count / 2 >= max)
                {
                    break;
                }

                if (_requested.Add((c.Tx, c.Tz)))
                {
                    pairs.Add(c.Tx);
                    pairs.Add(c.Tz);
                }
            }

            return pairs.ToArray();
        }

        /// <summary>Tiles whose content changed since the last call (canonical tile indices).</summary>
        public List<(int Tx, int Tz)> DrainChanged()
        {
            var copy = new List<(int, int)>(_changed);
            _changed.Clear();
            return copy;
        }
    }
}
