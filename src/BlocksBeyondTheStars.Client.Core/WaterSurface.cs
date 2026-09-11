// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a water SURFACE cell looks like, as weights the mesher averages over block corners and the
    /// transparent shader blends — never a class it branches on (#1749).</summary>
    public readonly struct WaterSurfaceData
    {
        /// <summary>1 on a wide body (gentle waves, sun glint, shore foam), 0 on a bounded basin.</summary>
        public readonly float Open;

        /// <summary>1 right at the waterline, fading to 0 three blocks out.</summary>
        public readonly float Foam;

        /// <summary>Brook weight along the X axis (fast ripples + streaks running along X).</summary>
        public readonly float FlowX;

        /// <summary>Brook weight along the Z axis.</summary>
        public readonly float FlowZ;

        /// <summary>How much of a narrow channel this is: <see cref="FlowX"/> + <see cref="FlowZ"/>.</summary>
        public float Channel => FlowX + FlowZ;

        public WaterSurfaceData(float open, float foam, float flowX, float flowZ)
        {
            Open = open;
            Foam = foam;
            FlowX = flowX;
            FlowZ = flowZ;
        }
    }

    /// <summary>
    /// Purely local, client-side classification of a water surface from the blocks around it — no worldgen
    /// or network data, so it works on every world and old saves. Shore runs in the four directions give the
    /// body's spans at the cell; from those come CONTINUOUS weights (open water, brook along X or Z, calm
    /// basin = what is left) plus a foam factor. The old version returned one of three modes and the shader
    /// branched on it, so a body whose width crosses a threshold — a hand-dug moat, a swamp full of reeds —
    /// flipped from cell to cell and drew a mosaic of ripple directions and brightness steps (#1749). Lives
    /// in Client.Core so the rule is covered by plain .NET tests; the mesher and the audio only call it.
    /// </summary>
    public static class WaterSurface
    {
        /// <summary>How far the shore probes look in each direction. Caps the cost per surface cell and
        /// sets the "open water" scale: a body wider than ~2× this in one axis gets waves.</summary>
        public const int ScanCap = 12;

        /// <summary>Total width (either axis) at or below which a body is fully a brook.</summary>
        public const int RiverMaxWidth = 5;

        /// <summary>Width at or above which nothing of the brook look remains; a linear ramp lies between.</summary>
        public const int RiverFadeWidth = 9;

        /// <summary>One axis this wide (the cap hit both ways) → fully open water.</summary>
        public const int OpenWaterSpan = 2 * ScanCap;

        /// <summary>Longest span at or below which nothing of the open look remains; a ramp lies between.</summary>
        public const int OpenFadeSpan = ScanCap;

        /// <param name="loaded">Optional "is this cell's chunk actually streamed in?" test — a cell we don't hold
        /// must not end a shore run, or the streamed region's edge grows a phantom coastline (foam + brook
        /// classification) in the middle of an ocean (#987). Null = everything is loaded.</param>
        /// <param name="passable">Optional "this block does not bound a body of water" test — reeds, kelp, a torch
        /// post standing in the water. Such a cell neither ends the run nor counts as shore; before #1749 every
        /// plant in a swamp lake cut the spans around it and the surface classified differently cell by cell.</param>
        public static WaterSurfaceData Classify(Func<int, int, int, BlockId> worldBlock, BlockId waterId, int wx, int wy, int wz,
            Func<int, int, int, bool>? loaded = null, Func<int, int, int, bool>? passable = null)
        {
            int px = Run(worldBlock, waterId, wx, wy, wz, 1, 0, loaded, passable);
            int nx = Run(worldBlock, waterId, wx, wy, wz, -1, 0, loaded, passable);
            int pz = Run(worldBlock, waterId, wx, wy, wz, 0, 1, loaded, passable);
            int nz = Run(worldBlock, waterId, wx, wy, wz, 0, -1, loaded, passable);
            int spanX = px + nx + 1;
            int spanZ = pz + nz + 1;

            // Distance to the nearest shore (1 = right at the waterline) → foam band, full at the line,
            // gone three blocks out. Mid-ocean cells hit the scan cap everywhere and get no foam.
            int shoreDist = Math.Min(Math.Min(px, nx), Math.Min(pz, nz)) + 1;
            float foam = Clamp01((4f - shoreDist) / 3f);

            // A narrow channel is a brook; the look fades out as the channel widens past RiverMaxWidth.
            int spanMin = Math.Min(spanX, spanZ);
            float channel = 1f - Clamp01((spanMin - RiverMaxWidth) / (float)(RiverFadeWidth - RiverMaxWidth));

            // The flow runs along the LONG axis. Near-square spans share the weight instead of flipping.
            float ratio = spanX / (float)(spanX + spanZ);
            float xShare = Clamp01((ratio - 0.5f) * 4f + 0.5f);

            // Wide in one axis → open water; a brook is never open, whatever its length.
            int spanMax = Math.Max(spanX, spanZ);
            float open = Clamp01((spanMax - OpenFadeSpan) / (float)(OpenWaterSpan - OpenFadeSpan)) * (1f - channel);

            return new WaterSurfaceData(open, foam, channel * xShare, channel * (1f - xShare));
        }

        /// <summary>Consecutive same-water cells beyond (wx,wz) in one direction, capped at <see cref="ScanCap"/>.
        /// Anything else — bank, beach, air over a lower pool (a waterfall lip), other fluids — ends the run,
        /// except a block the caller declares passable (a plant standing in the water).</summary>
        private static int Run(Func<int, int, int, BlockId> worldBlock, BlockId waterId, int wx, int wy, int wz, int dx, int dz,
            Func<int, int, int, bool>? loaded, Func<int, int, int, bool>? passable)
        {
            for (int d = 1; d <= ScanCap; d++)
            {
                int sx = wx + dx * d, sz = wz + dz * d;
                if (loaded != null && !loaded(sx, wy, sz))
                {
                    return ScanCap; // past the streamed edge — assume the body carries on rather than inventing a shore
                }

                if (worldBlock(sx, wy, sz).Value != waterId.Value && (passable == null || !passable(sx, wy, sz)))
                {
                    return d - 1;
                }
            }

            return ScanCap;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
