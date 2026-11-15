// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Showcase
{
    /// <summary>
    /// A plain RGBA32 image (straight alpha) and the few operations the logo and the poster capture need (#3706): stamping
    /// one image over another, cropping, area-averaged downscaling and a separable box blur. Unity-free, so the poster
    /// pipeline is tested without the engine; the row order is whatever the caller uses (the game's frames are bottom-up)
    /// as long as both images of an operation agree.
    /// </summary>
    public sealed class RgbaImage
    {
        public RgbaImage(int width, int height)
            : this(width, height, new byte[Math.Max(1, width) * Math.Max(1, height) * 4])
        {
        }

        public RgbaImage(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "An image needs a positive size.");
            }

            if (rgba == null || rgba.Length < width * height * 4)
            {
                throw new ArgumentException("The pixel buffer is smaller than width × height × 4.", nameof(rgba));
            }

            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>The pixels, row-major, four bytes each.</summary>
        public byte[] Rgba { get; }

        /// <summary>Sets one pixel; outside the image it does nothing.</summary>
        public void Put(int x, int y, byte r, byte g, byte b, byte a)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            int p = (y * Width + x) * 4;
            Rgba[p] = r;
            Rgba[p + 1] = g;
            Rgba[p + 2] = b;
            Rgba[p + 3] = a;
        }

        /// <summary>Reverses the row order in place (bottom-up ↔ top-down).</summary>
        public void FlipRows()
        {
            int stride = Width * 4;
            var tmp = new byte[stride];
            for (int top = 0, bottom = Height - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(Rgba, top * stride, tmp, 0, stride);
                Buffer.BlockCopy(Rgba, bottom * stride, Rgba, top * stride, stride);
                Buffer.BlockCopy(tmp, 0, Rgba, bottom * stride, stride);
            }
        }

        /// <summary>Stamps <paramref name="over"/> onto this image with its corner at (<paramref name="x"/>, <paramref name="y"/>)
        /// in this image's own row order, blending by the stamp's alpha. The result stays opaque where this image was.</summary>
        public void DrawOver(RgbaImage over, int x, int y)
        {
            if (over == null)
            {
                return;
            }

            for (int oy = 0; oy < over.Height; oy++)
            {
                int ty = y + oy;
                if (ty < 0 || ty >= Height)
                {
                    continue;
                }

                for (int ox = 0; ox < over.Width; ox++)
                {
                    int tx = x + ox;
                    if (tx < 0 || tx >= Width)
                    {
                        continue;
                    }

                    int s = (oy * over.Width + ox) * 4;
                    int a = over.Rgba[s + 3];
                    if (a == 0)
                    {
                        continue;
                    }

                    int d = (ty * Width + tx) * 4;
                    int da = Rgba[d + 3];
                    int outA = a + da * (255 - a) / 255;
                    for (int c = 0; c < 3; c++)
                    {
                        int top = over.Rgba[s + c] * a;
                        int under = Rgba[d + c] * da * (255 - a) / 255;
                        Rgba[d + c] = (byte)(outA == 0 ? 0 : Math.Min(255, (top + under + outA / 2) / outA));
                    }

                    Rgba[d + 3] = (byte)outA;
                }
            }
        }

        /// <summary>A copy of the rectangle (<paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/>,
        /// <paramref name="height"/>), clamped to the image.</summary>
        public RgbaImage Crop(int x, int y, int width, int height)
        {
            x = Math.Max(0, Math.Min(x, Width - 1));
            y = Math.Max(0, Math.Min(y, Height - 1));
            width = Math.Max(1, Math.Min(width, Width - x));
            height = Math.Max(1, Math.Min(height, Height - y));
            var result = new RgbaImage(width, height);
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(Rgba, ((y + row) * Width + x) * 4, result.Rgba, row * width * 4, width * 4);
            }

            return result;
        }

        /// <summary>The centred crop with the aspect ratio <paramref name="aspect"/> (width / height).</summary>
        public RgbaImage CropToAspect(double aspect)
        {
            if (aspect <= 0)
            {
                return Crop(0, 0, Width, Height);
            }

            int w = Width, h = Height;
            if ((double)w / h > aspect)
            {
                w = Math.Max(1, (int)Math.Round(h * aspect));
            }
            else
            {
                h = Math.Max(1, (int)Math.Round(w / aspect));
            }

            return Crop((Width - w) / 2, (Height - h) / 2, w, h);
        }

        /// <summary>The image scaled to <paramref name="width"/> × <paramref name="height"/> by area averaging (sharp and
        /// alias-free when shrinking; a plain nearest-neighbour pick when growing).</summary>
        public RgbaImage Resize(int width, int height)
        {
            var result = new RgbaImage(width, height);
            double sx = (double)Width / width, sy = (double)Height / height;
            for (int y = 0; y < height; y++)
            {
                int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(Height, (int)Math.Ceiling((y + 1) * sy)));
                for (int x = 0; x < width; x++)
                {
                    int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(Width, (int)Math.Ceiling((x + 1) * sx)));
                    long r = 0, g = 0, b = 0, a = 0;
                    int n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int p = (yy * Width + xx) * 4;
                            r += Rgba[p];
                            g += Rgba[p + 1];
                            b += Rgba[p + 2];
                            a += Rgba[p + 3];
                            n++;
                        }
                    }

                    int d = (y * width + x) * 4;
                    result.Rgba[d] = (byte)(r / n);
                    result.Rgba[d + 1] = (byte)(g / n);
                    result.Rgba[d + 2] = (byte)(b / n);
                    result.Rgba[d + 3] = (byte)(a / n);
                }
            }

            return result;
        }

        /// <summary>One separable box-blur pass (horizontal then vertical) of a float mask with the given radius.</summary>
        public static float[] BoxBlur(float[] src, int width, int height, int radius)
        {
            if (src == null || radius <= 0)
            {
                return src ?? Array.Empty<float>();
            }

            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float norm = 1f / (radius * 2 + 1);
            for (int y = 0; y < height; y++)
            {
                BlurLine(src, tmp, y * width, 1, width, radius, norm);
            }

            for (int x = 0; x < width; x++)
            {
                BlurLine(tmp, dst, x, width, height, radius, norm);
            }

            return dst;
        }

        private static void BlurLine(float[] src, float[] dst, int start, int step, int count, int radius, float norm)
        {
            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
            {
                sum += Sample(src, start, step, count, i);
            }

            for (int i = 0; i < count; i++)
            {
                dst[start + i * step] = sum * norm;
                sum += Sample(src, start, step, count, i + radius + 1) - Sample(src, start, step, count, i - radius);
            }
        }

        private static float Sample(float[] src, int start, int step, int count, int i)
            => i < 0 || i >= count ? 0f : src[start + i * step];
    }
}
