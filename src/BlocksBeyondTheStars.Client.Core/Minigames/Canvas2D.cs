namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>A 32-bit RGBA colour (Unity-free, so it lives in Client.Core). Maps byte-for-byte onto a Unity
    /// <c>TextureFormat.RGBA32</c> texture.</summary>
    public readonly struct Rgba
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public Rgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static Rgba Rgb(int r, int g, int b) => new Rgba((byte)r, (byte)g, (byte)b);

        /// <summary>An HSL colour (h in degrees 0..360, s/l in 0..1). Some web games coloured by <c>hsl(...)</c>
        /// (e.g. the asteroid brick gradient) — this reproduces that without per-game maths.</summary>
        public static Rgba Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            double c = (1 - System.Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - System.Math.Abs((h / 60.0 % 2) - 1));
            double m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return new Rgba((byte)System.Math.Round((r + m) * 255), (byte)System.Math.Round((g + m) * 255), (byte)System.Math.Round((b + m) * 255));
        }

        public static readonly Rgba Clear = new Rgba(0, 0, 0, 0);
        public static readonly Rgba Black = new Rgba(0, 0, 0);
        public static readonly Rgba White = new Rgba(255, 255, 255);
    }

    /// <summary>
    /// A tiny software 2D raster surface for the data-cube minigames (Stream D Arcade): a flat RGBA byte buffer
    /// with rect / line / circle primitives. Pure (no UnityEngine) so the rendering is unit-tested headless and
    /// the minigame logic can run off the engine; the Unity host uploads <see cref="Rgba"/> straight into a
    /// <c>Texture2D</c> via <c>LoadRawTextureData</c>. The web minigames drew into an HTML canvas — this is the
    /// equivalent surface so porting them stays mechanical.
    ///
    /// Row 0 is the TOP of the image (y grows downward), matching the canvas convention; the Unity host flips V
    /// when it shows the texture. All primitives are bounds-safe (out-of-range pixels are clipped, never throw).
    /// </summary>
    public sealed class Canvas2D
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>The raw RGBA pixels, row-major, 4 bytes per pixel (R,G,B,A). Length = Width*Height*4.</summary>
        public byte[] Rgba { get; }

        public Canvas2D(int width, int height)
        {
            Width = width < 1 ? 1 : width;
            Height = height < 1 ? 1 : height;
            Rgba = new byte[Width * Height * 4];
        }

        public void SetPixel(int x, int y, Rgba c)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                return;
            }

            int i = (y * Width + x) * 4;
            Rgba[i] = c.R;
            Rgba[i + 1] = c.G;
            Rgba[i + 2] = c.B;
            Rgba[i + 3] = c.A;
        }

        public void Clear(Rgba c)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width + x) * 4;
                    Rgba[i] = c.R;
                    Rgba[i + 1] = c.G;
                    Rgba[i + 2] = c.B;
                    Rgba[i + 3] = c.A;
                }
            }
        }

        public void FillRect(int x, int y, int w, int h, Rgba c)
        {
            int x0 = x < 0 ? 0 : x;
            int y0 = y < 0 ? 0 : y;
            int x1 = x + w > Width ? Width : x + w;
            int y1 = y + h > Height ? Height : y + h;
            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++)
                {
                    int i = (py * Width + px) * 4;
                    Rgba[i] = c.R;
                    Rgba[i + 1] = c.G;
                    Rgba[i + 2] = c.B;
                    Rgba[i + 3] = c.A;
                }
            }
        }

        /// <summary>A 1px rectangle outline.</summary>
        public void DrawRect(int x, int y, int w, int h, Rgba c)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            FillRect(x, y, w, 1, c);
            FillRect(x, y + h - 1, w, 1, c);
            FillRect(x, y, 1, h, c);
            FillRect(x + w - 1, y, 1, h, c);
        }

        public void FillCircle(int cx, int cy, int r, Rgba c)
        {
            if (r < 0)
            {
                return;
            }

            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int dx2 = r2 - dy * dy;
                if (dx2 < 0)
                {
                    continue;
                }

                int dx = (int)System.Math.Sqrt(dx2);
                FillRect(cx - dx, cy + dy, dx * 2 + 1, 1, c);
            }
        }

        /// <summary>Width in pixels that <see cref="DrawText"/> would occupy for <paramref name="text"/> at the
        /// given scale (the trailing inter-char gap is included).</summary>
        public static int TextWidth(string text, int scale = 1)
            => string.IsNullOrEmpty(text) ? 0 : text.Length * BitmapFont.Advance * (scale < 1 ? 1 : scale);

        /// <summary>Draw <paramref name="text"/> with the built-in 5×7 <see cref="BitmapFont"/>, top-left at
        /// (<paramref name="x"/>,<paramref name="y"/>). The C# stand-in for the web games' <c>ctx.fillText</c>.</summary>
        public void DrawText(int x, int y, string text, Rgba c, int scale = 1)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (scale < 1)
            {
                scale = 1;
            }

            int penX = x;
            foreach (char ch in text)
            {
                if (BitmapFont.TryGet(ch, out var rows))
                {
                    for (int ry = 0; ry < BitmapFont.GlyphHeight; ry++)
                    {
                        byte bits = rows[ry];
                        for (int rx = 0; rx < BitmapFont.GlyphWidth; rx++)
                        {
                            if ((bits & (1 << (BitmapFont.GlyphWidth - 1 - rx))) != 0)
                            {
                                FillRect(penX + rx * scale, y + ry * scale, scale, scale, c);
                            }
                        }
                    }
                }

                penX += BitmapFont.Advance * scale;
            }
        }

        /// <summary>Draw <paramref name="text"/> horizontally centred on <paramref name="cx"/> (top at
        /// <paramref name="y"/>).</summary>
        public void DrawTextCentered(int cx, int y, string text, Rgba c, int scale = 1)
            => DrawText(cx - TextWidth(text, scale) / 2, y, text, c, scale);

        /// <summary>A 1px circle outline (midpoint algorithm).</summary>
        public void DrawCircle(int cx, int cy, int r, Rgba c)
        {
            if (r < 0)
            {
                return;
            }

            int x = r, y = 0, err = 1 - r;
            while (x >= y)
            {
                SetPixel(cx + x, cy + y, c);
                SetPixel(cx + y, cy + x, c);
                SetPixel(cx - y, cy + x, c);
                SetPixel(cx - x, cy + y, c);
                SetPixel(cx - x, cy - y, c);
                SetPixel(cx - y, cy - x, c);
                SetPixel(cx + y, cy - x, c);
                SetPixel(cx + x, cy - y, c);
                y++;
                if (err < 0)
                {
                    err += 2 * y + 1;
                }
                else
                {
                    x--;
                    err += 2 * (y - x) + 1;
                }
            }
        }

        /// <summary>A filled triangle (scanline). Used for the ship/probe sprites the web games drew as a rotated
        /// canvas path.</summary>
        public void FillTriangle(int ax, int ay, int bx, int by, int cx, int cy, Rgba col)
        {
            int minY = System.Math.Max(0, Min3(ay, by, cy));
            int maxY = System.Math.Min(Height - 1, Max3(ay, by, cy));
            for (int y = minY; y <= maxY; y++)
            {
                // Intersect the scanline with each edge, collect crossing x's, fill between the extremes.
                int? lo = null, hi = null;
                EdgeX(ax, ay, bx, by, y, ref lo, ref hi);
                EdgeX(bx, by, cx, cy, y, ref lo, ref hi);
                EdgeX(cx, cy, ax, ay, y, ref lo, ref hi);
                if (lo.HasValue && hi.HasValue)
                {
                    FillRect(lo.Value, y, hi.Value - lo.Value + 1, 1, col);
                }
            }
        }

        private static void EdgeX(int x0, int y0, int x1, int y1, int y, ref int? lo, ref int? hi)
        {
            if (y0 == y1 || y < System.Math.Min(y0, y1) || y > System.Math.Max(y0, y1))
            {
                return;
            }

            int x = x0 + (x1 - x0) * (y - y0) / (y1 - y0);
            if (!lo.HasValue || x < lo.Value)
            {
                lo = x;
            }

            if (!hi.HasValue || x > hi.Value)
            {
                hi = x;
            }
        }

        private static int Min3(int a, int b, int c) => System.Math.Min(a, System.Math.Min(b, c));

        private static int Max3(int a, int b, int c) => System.Math.Max(a, System.Math.Max(b, c));

        /// <summary>A 1px line (Bresenham).</summary>
        public void DrawLine(int x0, int y0, int x1, int y1, Rgba c)
        {
            int dx = System.Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -System.Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixel(x0, y0, c);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
