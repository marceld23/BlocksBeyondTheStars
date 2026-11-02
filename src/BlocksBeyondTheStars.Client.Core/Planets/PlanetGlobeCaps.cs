// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Polar caps for the planet globe (#3128). A planet's surface is a torus: it wraps east–west and north–south
    /// (<c>WorldConstants.LatitudePeriodFor</c>), so it has no real poles, and any picture of it on a sphere has to
    /// squeeze somewhere. The equirect map (<see cref="PlanetMapData"/>) spreads the whole latitude period over its rows,
    /// so its first and last row — an ordinary strip of terrain — each shrink into one point at a pole, where they showed
    /// as streaks converging into the pole. The caps blend the rows from <see cref="BlendStartDeg"/> latitude toward the
    /// world's own average colour around its poles (<see cref="PolarAverage"/>), fully from <see cref="FullCapDeg"/>:
    /// about a twentieth of the globe, nothing invented. The polar average rather than the surface block's colour
    /// (<see cref="PlanetMapData.GroundRgb"/>), so a gas giant's beige sea or a lava world's glow gets a cap of its own
    /// colour instead of a grey stone one.
    /// <para>Applied on the CPU to a copy for the globe only (<c>WorldMinimap.RequestGlobe</c>) — the landing map keeps
    /// the raw map, and its coarse preview shares the orbit bake's cache entry. The cloud shell fades out over the same
    /// band (<see cref="Fade"/>), so the weather layer does not pinch either. No shader does it: the cloud shader is
    /// shared by many other effects, and the CPU needs no new per-material switch.</para>
    /// </summary>
    public static class PlanetGlobeCaps
    {
        /// <summary>The latitude (degrees, either hemisphere) where the blend toward the cap colour starts.</summary>
        public const float BlendStartDeg = 62f;

        /// <summary>The latitude from which a row is the cap colour.</summary>
        public const float FullCapDeg = 72f;

        /// <summary>The latitude (degrees, −90 south .. +90 north) of the centre of row <paramref name="y"/> of an
        /// equirect map <paramref name="height"/> rows tall, row 0 the south edge.</summary>
        public static float RowLatitude(int y, int height) => ((y + 0.5f) / Math.Max(1, height) - 0.5f) * 180f;

        /// <summary>How far a latitude lies in the cap: 0 below <see cref="BlendStartDeg"/>, 1 from
        /// <see cref="FullCapDeg"/>, a smoothstep between — the same in both hemispheres.</summary>
        public static float Fade(float latitudeDeg)
        {
            float t = (Math.Abs(latitudeDeg) - BlendStartDeg) / (FullCapDeg - BlendStartDeg);
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            return t * t * (3f - 2f * t);
        }

        /// <summary>A copy of an RGBA32 map (row-major, row 0 the south edge) with the polar caps in
        /// <paramref name="groundRgb"/> (0xRRGGBB). Rows below the blend band are copied untouched, alpha is kept;
        /// <paramref name="rgba"/> itself is never written.</summary>
        public static byte[] Apply(byte[] rgba, int width, int height, int groundRgb)
        {
            if (rgba is null)
            {
                throw new ArgumentNullException(nameof(rgba));
            }

            if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            {
                throw new ArgumentException($"A {width}×{height} RGBA map needs {width * height * 4} bytes, got {rgba.Length}.", nameof(rgba));
            }

            var capped = (byte[])rgba.Clone();
            byte r = (byte)((groundRgb >> 16) & 0xFF), g = (byte)((groundRgb >> 8) & 0xFF), b = (byte)(groundRgb & 0xFF);
            for (int y = 0; y < height; y++)
            {
                float f = Fade(RowLatitude(y, height));
                if (f <= 0f)
                {
                    continue;
                }

                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int o = row + x * 4;
                    capped[o] = Blend(capped[o], r, f);
                    capped[o + 1] = Blend(capped[o + 1], g, f);
                    capped[o + 2] = Blend(capped[o + 2], b, f);
                }
            }

            return capped;
        }

        /// <summary>The average colour (0xRRGGBB) of the rows the caps cover (from <see cref="BlendStartDeg"/> on, both
        /// hemispheres) — what the caps fade into, so the band blends into a colour already there.
        /// <paramref name="fallbackRgb"/> when the map has no such rows.</summary>
        public static int PolarAverage(byte[] rgba, int width, int height, int fallbackRgb)
        {
            if (rgba is null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            {
                return fallbackRgb;
            }

            long r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < height; y++)
            {
                if (Fade(RowLatitude(y, height)) <= 0f)
                {
                    continue;
                }

                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int o = row + x * 4;
                    r += rgba[o];
                    g += rgba[o + 1];
                    b += rgba[o + 2];
                    n++;
                }
            }

            return n == 0 ? fallbackRgb : (int)(((r / n) << 16) | ((g / n) << 8) | (b / n));
        }

        private static byte Blend(byte from, byte to, float f)
            => f >= 1f ? to : (byte)Math.Max(0, Math.Min(255, (int)(from + (to - from) * f + 0.5f)));
    }
}
