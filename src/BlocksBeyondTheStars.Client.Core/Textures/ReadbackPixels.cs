// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The CPU half of a GPU pixel read (#2390, #2845): raw readback bytes in, rows in Unity's
    /// bottom-up order and in the format the caller asked for out. Unity-free so it is tested headless.
    ///
    /// Why it exists: on WebGPU the asynchronous readback hands back four bytes per pixel even when RGB24 was asked
    /// for, and the old code laid those bytes out as three per pixel — every browser screenshot arrived as three
    /// squeezed copies of the frame. The reader therefore always asks for RGBA32 and converts here, and takes the row
    /// length from the size of the data instead of assuming it.
    ///
    /// The second job is the screen capture's <see cref="SentinelR"/> colour: the capture texture is cleared to it before
    /// the frame is copied in, and whatever still holds it afterwards was not covered by the frame (in the browser the
    /// copied back buffer can be smaller than <c>Screen.width × Screen.height</c> says — the frame then filled only the
    /// top-left three quarters and the rest was uninitialised memory). <see cref="TryFindCoveredRect"/> finds the part
    /// the frame did cover, so the screenshot can be cut down to it.
    /// </summary>
    public static class ReadbackPixels
    {
        /// <summary>The capture texture's clear colour — magenta with zero alpha (RGBA 255, 0, 255, 0). Nothing a frame
        /// draws fills a whole row or column with it.</summary>
        public const byte SentinelR = 255, SentinelG = 0, SentinelB = 255, SentinelA = 0;

        /// <summary>Converts raw RGBA32 readback data (<paramref name="height"/> rows, each at least
        /// <paramref name="width"/> × 4 bytes; any row padding is derived from the data length) to tightly packed rows
        /// of <paramref name="dstBytesPerPixel"/> (3 = RGB24, 4 = RGBA32). <paramref name="flipRows"/> reverses the row
        /// order (a top-left-origin API hands its rows top-down; a Texture2D wants them bottom-up). Null when the data is
        /// too short for the size.</summary>
        public static byte[]? FromRgba(byte[] raw, int width, int height, int dstBytesPerPixel, bool flipRows)
        {
            if (raw == null || width <= 0 || height <= 0 || (dstBytesPerPixel != 3 && dstBytesPerPixel != 4))
            {
                return null;
            }

            int tight = width * 4;
            int stride = raw.Length / height;
            if (stride < tight)
            {
                return null;
            }

            var dst = new byte[width * height * dstBytesPerPixel];
            for (int y = 0; y < height; y++)
            {
                int src = (flipRows ? height - 1 - y : y) * stride;
                int o = y * width * dstBytesPerPixel;
                if (dstBytesPerPixel == 4)
                {
                    Buffer.BlockCopy(raw, src, dst, o, tight);
                    continue;
                }

                for (int x = 0; x < width; x++, src += 4, o += 3)
                {
                    dst[o] = raw[src];
                    dst[o + 1] = raw[src + 1];
                    dst[o + 2] = raw[src + 2];
                }
            }

            return dst;
        }

        /// <summary>Whether a packed RGBA32 pixel still holds the capture's clear colour (a little slack for the bilinear
        /// downscale, which blends the cleared border into its neighbours).</summary>
        public static bool IsSentinel(byte[] rgba, int index)
            => rgba[index] >= 250 && rgba[index + 1] <= 6 && rgba[index + 2] >= 250 && rgba[index + 3] <= 6;

        /// <summary>The rectangle of a packed RGBA32 image (rows bottom-up, as a Texture2D holds them) that the captured
        /// frame covered: every row and column that is not ENTIRELY the clear colour. False when the frame covered the
        /// whole image (nothing to cut) or nothing at all (no sense in cutting). When it cut a side, the rectangle loses
        /// one more pixel there — the bilinear seam between frame and clear colour.</summary>
        public static bool TryFindCoveredRect(byte[] rgba, int width, int height, out int x0, out int y0, out int w, out int h)
        {
            x0 = 0;
            y0 = 0;
            w = width;
            h = height;
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            {
                return false;
            }

            int minX = width, maxX = -1, minY = height, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    if (IsSentinel(rgba, row + (x * 4)))
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < 0)
            {
                return false; // nothing but clear colour — keep the image as it is
            }

            if (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1)
            {
                return false; // the frame filled the whole capture
            }

            // Shave the seam on every side that was cut (never below one pixel).
            if (minX > 0 && maxX > minX)
            {
                minX++;
            }

            if (maxX < width - 1 && maxX > minX)
            {
                maxX--;
            }

            if (minY > 0 && maxY > minY)
            {
                minY++;
            }

            if (maxY < height - 1 && maxY > minY)
            {
                maxY--;
            }

            x0 = minX;
            y0 = minY;
            w = maxX - minX + 1;
            h = maxY - minY + 1;
            return true;
        }

        /// <summary>Cuts a rectangle out of packed rows of <paramref name="bytesPerPixel"/>.</summary>
        public static byte[] Crop(byte[] pixels, int width, int bytesPerPixel, int x0, int y0, int w, int h)
        {
            var dst = new byte[w * h * bytesPerPixel];
            for (int y = 0; y < h; y++)
            {
                Buffer.BlockCopy(pixels, (((y0 + y) * width) + x0) * bytesPerPixel, dst, y * w * bytesPerPixel, w * bytesPerPixel);
            }

            return dst;
        }

        /// <summary>Packed RGBA32 to packed RGB24 (rows kept).</summary>
        public static byte[] DropAlpha(byte[] rgba, int pixelCount)
        {
            var dst = new byte[pixelCount * 3];
            for (int i = 0, s = 0, o = 0; i < pixelCount; i++, s += 4, o += 3)
            {
                dst[o] = rgba[s];
                dst[o + 1] = rgba[s + 1];
                dst[o + 2] = rgba[s + 2];
            }

            return dst;
        }
    }
}
