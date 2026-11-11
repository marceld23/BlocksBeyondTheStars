// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The skylight of the air cells one chunk build looks into (#1608, #3265) — <see cref="SkylightRules"/> applied to the
    /// blocks around the chunk. Column tops and finished cell values are cached in dense windows around the chunk (#1528:
    /// arrays indexed by the offset from the window origin instead of hashed keys), stamped with a build generation so a
    /// new build never clears them. One instance per meshing thread (the chunk mesher keeps it thread-static), reused build
    /// after build through <see cref="Begin"/>; Unity-free, so the headless tests run it on hand-built scenes.
    /// </summary>
    public sealed class SkylightField
    {
        /// <summary>How far past the chunk the column window reaches: the face cells one block outside it, the furthest
        /// opening probe, and the kernel that probe's opening is measured with. A column outside is computed uncached.</summary>
        public const int Margin = 1 + SkylightRules.ProbeReach + SkylightRules.Kernel;

        /// <summary>The column scan reaches this far above the chunk's top and below its bottom (blocks).</summary>
        public const int ScanAbove = 48, ScanBelow = 16;

        // The eight compass directions the opening probes and the well walls look along (dx, dz pairs).
        private static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly int _n;
        private readonly int _colSize;
        private readonly int[] _top;
        private readonly int[] _topGen;
        private readonly int _cellSize;
        private readonly float[] _cell;
        private readonly int[] _cellGen;
        private Func<int, int, int, BlockId>? _block;
        private Func<BlockId, bool>? _stopsLight;
        private int _gen;
        private int _ox, _oy, _oz;

        /// <summary>A field for chunks of <paramref name="chunkSize"/> blocks per side.</summary>
        public SkylightField(int chunkSize)
        {
            _n = chunkSize;
            _colSize = chunkSize + 2 * Margin;
            _top = new int[_colSize * _colSize];
            _topGen = new int[_colSize * _colSize];
            _cellSize = chunkSize + 2; // the cells the faces of the chunk look into: one block past each side
            _cell = new float[_cellSize * _cellSize * _cellSize];
            _cellGen = new int[_cellSize * _cellSize * _cellSize];
        }

        /// <summary>
        /// Starts a build of the chunk whose minimum corner is (<paramref name="originX"/>, <paramref name="originY"/>,
        /// <paramref name="originZ"/>). <paramref name="block"/> reads the world (air where nothing is loaded);
        /// <paramref name="stopsLight"/> says whether a block stops light — the block-light flood's rule: air, glass, water,
        /// plants, leaves and slim props let it through.
        /// </summary>
        public void Begin(Func<int, int, int, BlockId> block, Func<BlockId, bool> stopsLight, int originX, int originY, int originZ)
        {
            _block = block ?? throw new ArgumentNullException(nameof(block));
            _stopsLight = stopsLight ?? throw new ArgumentNullException(nameof(stopsLight));
            _ox = originX;
            _oy = originY;
            _oz = originZ;
            if (++_gen == int.MaxValue)
            {
                Array.Clear(_topGen, 0, _topGen.Length);
                Array.Clear(_cellGen, 0, _cellGen.Length);
                _gen = 1;
            }
        }

        /// <summary>Drops the world accessors of the last build, so the reused field does not keep its chunks alive.</summary>
        public void End()
        {
            _block = null;
            _stopsLight = null;
        }

        /// <summary>The highest non-air block of the column (<paramref name="wx"/>, <paramref name="wz"/>) within the scan
        /// range around the chunk, or <c>int.MinValue</c> when the column is empty there.</summary>
        public int Top(int wx, int wz)
        {
            int kx = wx - (_ox - Margin), kz = wz - (_oz - Margin);
            int key = (uint)kx < (uint)_colSize && (uint)kz < (uint)_colSize ? kz * _colSize + kx : -1;
            if (key >= 0 && _topGen[key] == _gen)
            {
                return _top[key];
            }

            var block = _block ?? throw new InvalidOperationException("SkylightField.Begin was not called.");
            int top = int.MinValue;
            for (int yy = _oy + _n + ScanAbove; yy >= _oy - ScanBelow; yy--)
            {
                if (!block(wx, yy, wz).IsAir)
                {
                    top = yy;
                    break;
                }
            }

            if (key >= 0)
            {
                _top[key] = top;
                _topGen[key] = _gen;
            }

            return top;
        }

        /// <summary>The skylight 0..1 of the cell (<paramref name="wx"/>, <paramref name="wy"/>, <paramref name="wz"/>) a
        /// face looks into (see <see cref="SkylightRules"/>).</summary>
        public float At(int wx, int wy, int wz)
        {
            int ix = wx - _ox + 1, iy = wy - _oy + 1, iz = wz - _oz + 1;
            int key = (uint)ix < (uint)_cellSize && (uint)iy < (uint)_cellSize && (uint)iz < (uint)_cellSize
                ? (iy * _cellSize + iz) * _cellSize + ix : -1;
            if (key >= 0 && _cellGen[key] == _gen)
            {
                return _cell[key];
            }

            float value = Compute(wx, wy, wz);
            if (key >= 0)
            {
                _cell[key] = value;
                _cellGen[key] = _gen;
            }

            return value;
        }

        private bool OpenAt(int wx, int wy, int wz) => wy > Top(wx, wz);

        /// <summary>#3478 (Lyxette, building under water): whether the daylight reaches the cell — open to the sky, or covered
        /// only by see-through blocks (water, leaves, glass) within <see cref="SkylightRules.ShadeDepthFull"/>. A lake has no
        /// open column below its surface, so a cell under a few logs standing in it found no opening at all and went black
        /// (skylight 0): the faces looking into it drew as flat dark quads among the lit lake floor. As an opening such a cell
        /// lends its own light (the shade floor); it never asks for an opening itself — its thin cover is full — so the
        /// search cannot recurse.</summary>
        private bool LightReaches(int wx, int wy, int wz)
        {
            int top = Top(wx, wz);
            return wy > top || (top - wy < SkylightRules.ShadeDepthFull && CoverCount(wx, wy, wz, top) == 0);
        }

        private float Compute(int wx, int wy, int wz)
        {
            int top = Top(wx, wz);
            if (wy > top)
            {
                return OpenSky(wx, wy, wz);
            }

            int open = KernelOpen(wx, wy, wz);
            int depth = top - wy + 1;
            int cover = depth <= SkylightRules.ShadeDepthNone ? CoverCount(wx, wy, wz, top) : SkylightRules.CoverSolid;
            float thin = SkylightRules.ThinCover(cover) * SkylightRules.DepthFade(depth);
            float opening = thin >= 1f ? 0f : NearOpening(wx, wy, wz);
            return SkylightRules.Covered(open, SkylightRules.CoveredFloor(depth, cover, opening));
        }

        /// <summary>An open cell: full sky, unless it is the bottom of a narrow well (see <see cref="SkylightRules.WellWalls"/>).</summary>
        private float OpenSky(int wx, int wy, int wz)
        {
            var stopsLight = _stopsLight!;
            var block = _block!;
            int walls = 0, wallDepth = int.MaxValue;
            for (int d = 0; d < DirX.Length; d++)
            {
                int nx = wx + DirX[d], nz = wz + DirZ[d];
                int nTop = Top(nx, nz);
                if (wy > nTop || !stopsLight(block(nx, wy, nz)))
                {
                    if (d - walls >= DirX.Length - SkylightRules.WellWalls)
                    {
                        return 1f; // too many open sides for a well — early out on open ground
                    }

                    continue;
                }

                walls++;
                wallDepth = Math.Min(wallDepth, nTop - wy + 1);
            }

            return walls >= SkylightRules.WellWalls ? SkylightRules.WellLight(wallDepth) : 1f;
        }

        /// <summary>How many of the kernel's columns are open at this height.</summary>
        private int KernelOpen(int wx, int wy, int wz)
        {
            int open = 0;
            for (int ox = -SkylightRules.Kernel; ox <= SkylightRules.Kernel; ox++)
            {
                for (int oz = -SkylightRules.Kernel; oz <= SkylightRules.Kernel; oz++)
                {
                    if (OpenAt(wx + ox, wy, wz + oz))
                    {
                        open++;
                    }
                }
            }

            return open;
        }

        /// <summary>Light-stopping blocks between the cell and its column top, counted up to <see cref="SkylightRules.CoverSolid"/>.</summary>
        private int CoverCount(int wx, int wy, int wz, int top)
        {
            var stopsLight = _stopsLight!;
            var block = _block!;
            int count = 0;
            for (int y = wy + 1; y <= top && count < SkylightRules.CoverSolid; y++)
            {
                if (stopsLight(block(wx, y, wz)))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The near-opening share (0..1): the nearest open column at the cell's height — the kernel's rings first,
        /// then the eight compass directions out to <see cref="SkylightRules.ProbeReach"/> — scaled by that opening's own
        /// skylight, so the tunnel at the foot of a deep shaft is never brighter than the shaft's floor.</summary>
        private float NearOpening(int wx, int wy, int wz)
        {
            // #3478: the rings run even when no kernel column is open to the sky — under water a lit neighbour is covered
            // only by water (LightReaches), which the open-sky kernel count never sees.
            for (int ring = 1; ring <= SkylightRules.Kernel; ring++)
            {
                float best = RingOpening(wx, wy, wz, ring);
                if (best >= 0f)
                {
                    return SkylightRules.NearOpening(ring) * best;
                }
            }

            for (int dist = SkylightRules.Kernel + 1; dist <= SkylightRules.ProbeReach; dist++)
            {
                float best = -1f;
                for (int d = 0; d < DirX.Length; d++)
                {
                    int px = wx + DirX[d] * dist, pz = wz + DirZ[d] * dist;
                    if (LightReaches(px, wy, pz))
                    {
                        best = Math.Max(best, At(px, wy, pz));
                    }
                }

                if (best >= 0f)
                {
                    return SkylightRules.NearOpening(dist) * best;
                }
            }

            return 0f;
        }

        /// <summary>The brightest open cell on the square ring of the given Chebyshev radius at this height, or -1 if none.</summary>
        private float RingOpening(int wx, int wy, int wz, int ring)
        {
            float best = -1f;
            for (int ox = -ring; ox <= ring; ox++)
            {
                for (int oz = -ring; oz <= ring; oz++)
                {
                    if (Math.Max(Math.Abs(ox), Math.Abs(oz)) == ring && LightReaches(wx + ox, wy, wz + oz))
                    {
                        best = Math.Max(best, At(wx + ox, wy, wz + oz));
                    }
                }
            }

            return best;
        }
    }
}
