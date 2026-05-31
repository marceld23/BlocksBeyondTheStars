using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Procedurally generated UI icons (M27) — small <see cref="Texture2D"/> glyphs drawn in
    /// code (no image assets): a red cross (health), a cyan ring (oxygen) and a yellow diamond
    /// (energy). Cached on first use. Hand-authored icons can replace these later.
    /// </summary>
    public static class IconFactory
    {
        private const int Size = 16;

        private static Texture2D _health, _oxygen, _energy, _generic;

        public static Texture2D Health => _health ??= BuildCross(new Color(0.9f, 0.2f, 0.2f));
        public static Texture2D Oxygen => _oxygen ??= BuildRing(new Color(0.3f, 0.8f, 1f));
        public static Texture2D Energy => _energy ??= BuildDiamond(new Color(1f, 0.82f, 0.2f));
        public static Texture2D Generic => _generic ??= BuildBox(new Color(0.6f, 0.62f, 0.7f));

        private static Texture2D New()
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color[Size * Size];
            t.SetPixels(clear);
            return t;
        }

        private static Texture2D BuildCross(Color c)
        {
            var t = New();
            for (int i = 3; i < Size - 3; i++)
            {
                for (int w = 6; w <= 9; w++)
                {
                    t.SetPixel(i, w, c);  // horizontal bar
                    t.SetPixel(w, i, c);  // vertical bar
                }
            }

            t.Apply();
            return t;
        }

        private static Texture2D BuildRing(Color c)
        {
            var t = New();
            const float cx = 7.5f, cy = 7.5f;
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d >= 4.2f && d <= 6.2f)
                    {
                        t.SetPixel(x, y, c);
                    }
                }
            }

            t.Apply();
            return t;
        }

        private static Texture2D BuildDiamond(Color c)
        {
            var t = New();
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    if (Mathf.Abs(x - 7.5f) + Mathf.Abs(y - 7.5f) <= 6f)
                    {
                        t.SetPixel(x, y, c);
                    }
                }
            }

            t.Apply();
            return t;
        }

        private static Texture2D BuildBox(Color c)
        {
            var t = New();
            for (int x = 2; x < Size - 2; x++)
            {
                for (int y = 2; y < Size - 2; y++)
                {
                    bool edge = x == 2 || y == 2 || x == Size - 3 || y == Size - 3;
                    t.SetPixel(x, y, edge ? c : c * 0.6f);
                }
            }

            t.Apply();
            return t;
        }
    }
}
