// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Finds the rune gates near the player on OUR side (#3906), so a faint shimmer can stand between their stones while
    /// the crossing disc sits in the worn chest's socket — the player sees where the door is. The server does not list
    /// gates to a player on our side, so the client looks itself, in the chunks it already has: a small box around the
    /// player is read a slab per frame (<see cref="Advance"/>), and the rune stones found are grouped by the server's
    /// rule — three or more within <see cref="GroupRadius"/> blocks of one of them are a gate.
    /// <para>The scan is a hint, never the truth: whether a stone takes the player across is the server's decision.</para>
    /// </summary>
    public sealed class RuneGateFinder
    {
        /// <summary>Blocks around a stone its group is counted in (per axis) — the server's <c>EchoRuneScanRadius</c>.</summary>
        public const int GroupRadius = 8;

        /// <summary>Rune stones a gate needs at least — the server's <c>EchoRuneGroupSize</c>.</summary>
        public const int GroupSize = 3;

        /// <summary>Two groups whose middles lie closer than this are one gate.</summary>
        public const float GateSpacing = 14f;

        /// <summary>Half the scanned box, horizontally (blocks).</summary>
        public const int ReachXZ = 20;

        /// <summary>Half the scanned box, vertically (blocks).</summary>
        public const int ReachY = 10;

        /// <summary>No scan keeps more stones than this (a wall of rune stones is one gate, not a thousand tests).</summary>
        public const int MaxStones = 96;

        private readonly List<Vector3i> _stones = new List<Vector3i>();
        private readonly List<Vector3f> _gates = new List<Vector3f>();
        private int _cx, _cy, _cz;
        private int _slab = int.MaxValue; // no scan running

        /// <summary>The gates of the last finished scan (their middles, in the coordinates the scan read).</summary>
        public IReadOnlyList<Vector3f> Gates => _gates;

        /// <summary>Moves when a scan finished with another result than the one before.</summary>
        public int Version { get; private set; }

        /// <summary>A scan is under way.</summary>
        public bool Scanning => _slab != int.MaxValue;

        /// <summary>Starts a scan of the box around the cell (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).</summary>
        public void Begin(int x, int y, int z)
        {
            _cx = x;
            _cy = y;
            _cz = z;
            _slab = -ReachXZ;
            _stones.Clear();
        }

        /// <summary>
        /// Reads up to <paramref name="slabs"/> slabs of the box (each one column plane, 41 × 21 cells) with
        /// <paramref name="isRuneStone"/>. True when the scan finished with this call — <see cref="Gates"/> is then new.
        /// </summary>
        public bool Advance(Func<int, int, int, bool> isRuneStone, int slabs = 1)
        {
            if (!Scanning)
            {
                return false;
            }

            for (int n = 0; n < slabs && _slab <= ReachXZ; n++, _slab++)
            {
                int x = _cx + _slab;
                for (int dz = -ReachXZ; dz <= ReachXZ; dz++)
                {
                    for (int dy = -ReachY; dy <= ReachY; dy++)
                    {
                        if (_stones.Count < MaxStones && isRuneStone(x, _cy + dy, _cz + dz))
                        {
                            _stones.Add(new Vector3i(x, _cy + dy, _cz + dz));
                        }
                    }
                }
            }

            if (_slab <= ReachXZ)
            {
                return false;
            }

            _slab = int.MaxValue;
            Publish();
            return true;
        }

        /// <summary>Forgets everything (the disc was taken out, a new world).</summary>
        public void Clear()
        {
            _slab = int.MaxValue;
            _stones.Clear();
            if (_gates.Count > 0)
            {
                _gates.Clear();
                Version++;
            }
        }

        private void Publish()
        {
            int before = _gates.Count;
            float sx = 0f, sz = 0f;
            foreach (var g in _gates)
            {
                sx += g.X;
                sz += g.Z;
            }

            _gates.Clear();
            Group(_stones, _gates);
            float nx = 0f, nz = 0f;
            foreach (var g in _gates)
            {
                nx += g.X;
                nz += g.Z;
            }

            if (before != _gates.Count || Math.Abs(sx - nx) > 0.01f || Math.Abs(sz - nz) > 0.01f)
            {
                Version++;
            }
        }

        /// <summary>
        /// The gates among <paramref name="stones"/>: for every stone the stones within <see cref="GroupRadius"/> of it; a
        /// group of <see cref="GroupSize"/> or more is a gate at its middle, unless a gate already stands within
        /// <see cref="GateSpacing"/>. Appends to <paramref name="into"/>.
        /// </summary>
        public static void Group(IReadOnlyList<Vector3i> stones, List<Vector3f> into)
        {
            for (int a = 0; a < stones.Count; a++)
            {
                var anchor = stones[a];
                int count = 0;
                double sx = 0, sy = 0, sz = 0;
                for (int b = 0; b < stones.Count; b++)
                {
                    var p = stones[b];
                    if (Math.Abs(p.X - anchor.X) <= GroupRadius && Math.Abs(p.Y - anchor.Y) <= GroupRadius && Math.Abs(p.Z - anchor.Z) <= GroupRadius)
                    {
                        count++;
                        sx += p.X + 0.5;
                        sy += p.Y;
                        sz += p.Z + 0.5;
                    }
                }

                if (count < GroupSize)
                {
                    continue;
                }

                var centre = new Vector3f((float)(sx / count), (float)(sy / count), (float)(sz / count));
                if (!Near(into, centre))
                {
                    into.Add(centre);
                }
            }
        }

        private static bool Near(List<Vector3f> gates, Vector3f at)
        {
            foreach (var g in gates)
            {
                float dx = g.X - at.X, dy = g.Y - at.Y, dz = g.Z - at.Z;
                if ((dx * dx) + (dy * dy) + (dz * dz) <= GateSpacing * GateSpacing)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
