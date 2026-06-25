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
