// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The numbers of the chunk mesher's skylight (0 = sealed off from the sky … 1 = open sky), the value every block face
    /// carries into the shaders (TEXCOORD1.x) — <see cref="SkylightField"/> applies them to the blocks around a chunk.
    /// <list type="bullet">
    ///   <item>Open straight up: 1 — unless the cell sits at the bottom of a narrow well (a shaft or trench one block wide),
    ///   whose floor sees only a slit of sky (<see cref="WellLight"/>, #3265).</item>
    ///   <item>Covered: the open share of the 5×5 columns around it (a cave mouth feathers), but at least a shade floor
    ///   (#1608): <see cref="ShadeFloor"/> × max(thin cover, near opening). A tree crown or a one-block roof keeps the full
    ///   shade floor; a cell under three or more light-stopping blocks only keeps what the nearest opening at its own
    ///   height lets in, so a dug tunnel or a cave darkens from its mouth to nothing eight blocks in (#3265).</item>
    /// </list>
    /// </summary>
    public static class SkylightRules
    {
        /// <summary>The shade floor (#1608): a covered cell lit like tree shade. The shaders' <c>BBTS_SHADE_FLOOR</c> and
        /// the ambient ramp <c>BbtsSkyAmbient</c> key on the same value (Reports20261103WiringTests pins the pair).</summary>
        public const float ShadeFloor = 0.55f;

        /// <summary>Depth below the column top (1 = right under it) to which a thin cover keeps the full shade floor.</summary>
        public const int ShadeDepthFull = 6;

        /// <summary>Depth at which a thin cover's shade has faded to nothing (and below which nothing is counted).</summary>
        public const int ShadeDepthNone = 14;

        /// <summary>Light-stopping blocks overhead from which the cover counts as solid rock: no shade floor of its own.</summary>
        public const int CoverSolid = 3;

        /// <summary>Radius of the soft kernel (5×5 columns) whose open share feathers a cave mouth.</summary>
        public const int Kernel = 2;

        /// <summary>Columns in the kernel.</summary>
        public const int KernelCells = (2 * Kernel + 1) * (2 * Kernel + 1);

        /// <summary>An open column this close (Chebyshev blocks) or closer lets in the full shade floor.</summary>
        public const int OpeningFull = 2;

        /// <summary>…and the light from an opening is gone at this distance — the probes stop one short of it.</summary>
        public const int OpeningNone = 8;

        /// <summary>The furthest opening probe (blocks along the eight compass directions).</summary>
        public const int ProbeReach = OpeningNone - 1;

        /// <summary>Of the eight neighbours of an open cell, this many walls (light-stopping blocks at its height) make it a
        /// well: a 1×1 shaft (8), a one-block-wide trench (6). A wider shaft, an inside corner or open ground has fewer.</summary>
        public const int WellWalls = 6;

        /// <summary>The darkest a well's floor gets (blocks of wall above it ≥ <see cref="WellDepthNone"/>).</summary>
        public const float WellFloor = 0.25f;

        /// <summary>Wall height above a well cell at which it reaches <see cref="WellFloor"/>.</summary>
        public const int WellDepthNone = 8;

        /// <summary>How much of the shade floor a thin cover keeps: 0–1 light-stopping blocks overhead (a roof, a crown —
        /// leaves, plants, glass and water never count) → all of it, 2 → half, <see cref="CoverSolid"/> or more → none.</summary>
        public static float ThinCover(int stoppingBlocks)
            => stoppingBlocks <= 1 ? 1f : stoppingBlocks < CoverSolid ? 0.5f : 0f;

        /// <summary>The #1608 depth fade of a thin cover: full within <see cref="ShadeDepthFull"/> blocks of the column top,
        /// gone by <see cref="ShadeDepthNone"/> — a canopy far overhead shades less than one just above.</summary>
        public static float DepthFade(int depth)
            => depth <= ShadeDepthFull ? 1f : Clamp01((ShadeDepthNone - depth) / (float)(ShadeDepthNone - ShadeDepthFull));

        /// <summary>How much of the shade floor the nearest open column at the cell's own height lets in, by its distance
        /// (Chebyshev blocks): ≤ <see cref="OpeningFull"/> → all of it, falling linearly to nothing at
        /// <see cref="OpeningNone"/>. <c>int.MaxValue</c> = no opening in reach.</summary>
        public static float NearOpening(int distance)
            => distance <= OpeningFull ? 1f : Clamp01((OpeningNone - distance) / (float)(OpeningNone - OpeningFull));

        /// <summary>The shade floor of a covered cell: <see cref="ShadeFloor"/> × max(thin cover × depth fade, opening),
        /// where <paramref name="opening"/> is the near-opening share already scaled by that opening's own skylight.</summary>
        public static float CoveredFloor(int depth, int stoppingBlocks, float opening)
            => ShadeFloor * Math.Max(ThinCover(stoppingBlocks) * DepthFade(depth), Clamp01(opening));

        /// <summary>The skylight of a covered cell: the kernel's open share or the shade floor, whichever is brighter.</summary>
        public static float Covered(int kernelOpen, float floor)
            => Math.Max(kernelOpen / (float)KernelCells, floor);

        /// <summary>The skylight at the bottom of a well, by the height of its lowest wall above the cell (1 = the cell
        /// is level with the surrounding ground): 1 → full sky, 3 → the shade floor, <see cref="WellDepthNone"/> and deeper
        /// → <see cref="WellFloor"/>. A hole one block deep stays fully lit; a deep 1×1 shaft's floor is dim, not open sky.</summary>
        public static float WellLight(int wallDepth)
        {
            if (wallDepth <= 1)
            {
                return 1f;
            }

            if (wallDepth <= 3)
            {
                return Lerp(1f, ShadeFloor, (wallDepth - 1) / 2f);
            }

            return Lerp(ShadeFloor, WellFloor, Clamp01((wallDepth - 3) / (float)(WellDepthNone - 3)));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
