using Spacecraft.Shared.Content;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A block texture atlas generated **procedurally in code** (M27) — no bundled image assets.
    /// Each block gets a 32×32 tile painted from its base colour plus per-block detail (grain,
    /// ore speckles, metal panel + rivets, ice/glass streaks, a circuit grid, grass/flora blades,
    /// crystal facets, water wave crests, glowing lava veins), with a darker edge so blocks read
    /// as tiled. The chunk mesher UV-maps faces into this atlas.
    /// </summary>
    public sealed class BlockTextureAtlas
    {
        public const int Tile = 64;
        public const int Cols = 8;
        public const int Rows = 8;

        public Texture2D Texture { get; }

        public BlockTextureAtlas(GameContent content)
        {
            Texture = new Texture2D(Cols * Tile, Rows * Tile, TextureFormat.RGBA32, mipChain: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            foreach (var b in content.Blocks.Values)
            {
                int id = b.NumericId.Value;
                if (id > 0 && id < Cols * Rows)
                {
                    PaintTile(id, b.Key);
                }
            }

            Texture.Apply(updateMipmaps: true);
        }

        /// <summary>UV rect of a block's tile (with a tiny inset to avoid bleeding).</summary>
        public Rect TileUv(ushort id)
        {
            int x = id % Cols, y = id / Cols;
            float w = 1f / Cols, h = 1f / Rows, inset = 0.001f;
            return new Rect(x * w + inset, y * h + inset, w - 2f * inset, h - 2f * inset);
        }

        private void PaintTile(int id, string key)
        {
            int ox = (id % Cols) * Tile, oy = (id / Cols) * Tile;

            // Prefer a generated block texture (Resources/textures/<key>.bytes); fall back to the
            // procedural tile when none is bundled.
            if (TryPaintFromAsset(key, ox, oy))
            {
                return;
            }

            Color baseCol = BaseColor(key);
            var rng = new System.Random(Hash(key));

            for (int px = 0; px < Tile; px++)
            {
                for (int py = 0; py < Tile; py++)
                {
                    float n = 0.86f + 0.28f * (float)rng.NextDouble(); // per-pixel grain
                    var c = new Color(baseCol.r * n, baseCol.g * n, baseCol.b * n, 1f);
                    if (px == 0 || py == 0 || px == Tile - 1 || py == Tile - 1)
                    {
                        c *= 0.65f; // darker edge → tiled look
                    }

                    Texture.SetPixel(ox + px, oy + py, new Color(c.r, c.g, c.b, 1f));
                }
            }

            Decorate(id, key, ox, oy, rng);
        }

        /// <summary>
        /// Blits a generated block texture (bundled as a <c>Resources/textures/&lt;key&gt;.bytes</c> raw
        /// RGBA32 tile, decoded via <see cref="Texture2D.LoadRawTextureData(byte[])"/> from the core
        /// module — avoids LoadImage, which lives in the non-auto-referenced ImageConversionModule and
        /// won't compile from the client asmdef). Returns false if absent or the wrong size.
        /// </summary>
        private bool TryPaintFromAsset(string key, int ox, int oy)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != Tile * Tile * 4)
            {
                return false;
            }

            var src = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
            src.LoadRawTextureData(asset.bytes);
            src.Apply();
            Texture.SetPixels(ox, oy, Tile, Tile, src.GetPixels());
            Object.Destroy(src);
            return true;
        }

        private void Decorate(int id, string key, int ox, int oy, System.Random rng)
        {
            switch (key)
            {
                case "iron_wall":
                    // Panel rivets near the corners + a central seam.
                    PutDot(ox + 4, oy + 4, new Color(0.30f, 0.31f, 0.34f));
                    PutDot(ox + Tile - 5, oy + 4, new Color(0.30f, 0.31f, 0.34f));
                    PutDot(ox + 4, oy + Tile - 5, new Color(0.30f, 0.31f, 0.34f));
                    PutDot(ox + Tile - 5, oy + Tile - 5, new Color(0.30f, 0.31f, 0.34f));
                    for (int x = 2; x < Tile - 2; x++) Texture.SetPixel(ox + x, oy + Tile / 2, new Color(0.42f, 0.44f, 0.49f));
                    break;

                case "iron_ore":
                    Speckle(ox, oy, rng, new Color(0.75f, 0.55f, 0.40f), 26);
                    break;
                case "copper_ore":
                    Speckle(ox, oy, rng, new Color(0.85f, 0.50f, 0.30f), 26);
                    break;
                case "titanium_ore":
                    Speckle(ox, oy, rng, new Color(0.80f, 0.82f, 0.88f), 26);
                    break;
                case "silicate":
                    Speckle(ox, oy, rng, new Color(0.95f, 0.92f, 0.75f), 18);
                    break;

                case "data_cache":
                    // Faint circuit grid.
                    for (int i = 4; i < Tile; i += 6)
                    {
                        for (int j = 2; j < Tile - 2; j++)
                        {
                            Texture.SetPixel(ox + i, oy + j, new Color(0.45f, 0.95f, 1f));
                            Texture.SetPixel(ox + j, oy + i, new Color(0.30f, 0.80f, 0.95f));
                        }
                    }
                    break;

                case "ice":
                case "glass":
                    // Diagonal sheen streak.
                    for (int d = 0; d < Tile; d++)
                    {
                        int x = d, y = (d + 6) % Tile;
                        Texture.SetPixel(ox + x, oy + y, new Color(0.95f, 0.98f, 1f));
                    }
                    break;

                case "water":
                    // Horizontal wave crests (a few lighter bands that ripple along x).
                    {
                        var crest = new Color(0.50f, 0.72f, 1f);
                        var deep = new Color(0.12f, 0.30f, 0.66f);
                        for (int py = 0; py < Tile; py++)
                        {
                            int shift = (int)System.Math.Round(1.6 * System.Math.Sin(py * 0.7));
                            for (int px = 0; px < Tile; px++)
                            {
                                int band = (px + shift) % 8;
                                if (band == 0) Texture.SetPixel(ox + px, oy + py, crest);
                                else if (band == 4) Texture.SetPixel(ox + px, oy + py, deep);
                            }
                        }
                    }
                    break;

                case "lava":
                    // Dark cooled crust with glowing cracks/veins winding through it + hot flecks.
                    {
                        var crust = new Color(0.30f, 0.10f, 0.06f);
                        var glow = new Color(1f, 0.78f, 0.28f);
                        var hot = new Color(1f, 0.45f, 0.12f);
                        Speckle(ox, oy, rng, crust, 44); // cooled patches
                        for (int v = 0; v < 3; v++)
                        {
                            int x = 4 + rng.Next(Tile - 8);
                            for (int y = 2; y < Tile - 2; y++)
                            {
                                Texture.SetPixel(ox + x, oy + y, glow);
                                if (x + 1 < Tile - 1) Texture.SetPixel(ox + x + 1, oy + y, hot);
                                x += rng.Next(3) - 1; // random-walk crack
                                if (x < 1) x = 1; else if (x > Tile - 2) x = Tile - 2;
                            }
                        }

                        Speckle(ox, oy, rng, glow, 12); // bright embers
                    }
                    break;

                case "grass":
                    // A few short blades poking up from the soil edge → grassy top read.
                    PaintBlades(ox, oy, rng, 9, Tile / 3,
                        new Color(0.20f, 0.50f, 0.18f), new Color(0.48f, 0.82f, 0.40f));
                    break;

                case "flora_plant":
                    // Leafy blades rising from the base + lighter leaf tips + scattered highlights.
                    PaintBlades(ox, oy, rng, 8, Tile - 4,
                        new Color(0.16f, 0.52f, 0.20f), new Color(0.50f, 0.88f, 0.42f));
                    Speckle(ox, oy, rng, new Color(0.50f, 0.88f, 0.42f), 10);
                    break;

                case "flora_crystal":
                    // Faceted shards: a bright spine with a shaded side + a tip glint, then sparkle.
                    PaintCrystals(ox, oy, rng, new Color(0.88f, 0.96f, 1f), new Color(0.30f, 0.45f, 0.72f));
                    Speckle(ox, oy, rng, new Color(0.88f, 0.96f, 1f), 14);
                    break;
            }
        }

        private void Speckle(int ox, int oy, System.Random rng, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int x = 2 + rng.Next(Tile - 4);
                int y = 2 + rng.Next(Tile - 4);
                Texture.SetPixel(ox + x, oy + y, color);
            }
        }

        private void PutDot(int x, int y, Color color)
        {
            Texture.SetPixel(x, y, color);
            Texture.SetPixel(x + 1, y, color);
            Texture.SetPixel(x, y + 1, color);
            Texture.SetPixel(x + 1, y + 1, color);
        }

        /// <summary>Vertical blades from the base up to a random height, with a lighter tip.</summary>
        private void PaintBlades(int ox, int oy, System.Random rng, int count, int maxTop, Color blade, Color tip)
        {
            for (int s = 0; s < count; s++)
            {
                int bx = 3 + rng.Next(Tile - 6);
                int top = Tile / 3 + rng.Next(System.Math.Max(2, maxTop - Tile / 3));
                for (int y = 2; y < top; y++)
                {
                    Texture.SetPixel(ox + bx, oy + y, blade);
                }

                Texture.SetPixel(ox + bx, oy + top, tip);
                if (bx + 1 < Tile)
                {
                    Texture.SetPixel(ox + bx + 1, oy + System.Math.Max(2, top - 1), tip);
                }
            }
        }

        /// <summary>A few upright crystal shards: a bright spine, a shaded left side and a tip glint.</summary>
        private void PaintCrystals(int ox, int oy, System.Random rng, Color facet, Color shade)
        {
            for (int s = 0; s < 4; s++)
            {
                int cx = 6 + rng.Next(Tile - 12);
                int top = Tile - 4 - rng.Next(6);
                for (int y = 3; y < top; y++)
                {
                    Texture.SetPixel(ox + cx, oy + y, facet);
                    if (cx - 1 >= 0)
                    {
                        Texture.SetPixel(ox + cx - 1, oy + y, shade);
                    }
                }

                PutDot(ox + cx, oy + System.Math.Min(Tile - 2, top), facet);
            }
        }

        private static int Hash(string key)
        {
            int h = 17;
            foreach (char c in key) h = h * 31 + c;
            return h;
        }

        private static Color BaseColor(string key) => key switch
        {
            "stone" => new Color(0.55f, 0.55f, 0.57f),
            "dirt" => new Color(0.45f, 0.32f, 0.20f),
            "basalt" => new Color(0.24f, 0.24f, 0.27f),
            "ice" => new Color(0.70f, 0.85f, 0.95f),
            "iron_ore" => new Color(0.58f, 0.50f, 0.46f),
            "copper_ore" => new Color(0.60f, 0.46f, 0.38f),
            "silicate" => new Color(0.80f, 0.78f, 0.60f),
            "carbon" => new Color(0.16f, 0.16f, 0.18f),
            "titanium_ore" => new Color(0.58f, 0.60f, 0.66f),
            "data_cache" => new Color(0.18f, 0.40f, 0.55f),
            "glass" => new Color(0.70f, 0.90f, 0.95f),
            "iron_wall" => new Color(0.55f, 0.57f, 0.62f),
            "water" => new Color(0.20f, 0.42f, 0.85f),
            "lava" => new Color(0.90f, 0.35f, 0.10f),
            "sand" => new Color(0.85f, 0.78f, 0.52f),
            "mud" => new Color(0.36f, 0.28f, 0.18f),
            "grass" => new Color(0.32f, 0.62f, 0.28f),
            "crystal" => new Color(0.55f, 0.75f, 0.95f),
            "flora_plant" => new Color(0.25f, 0.70f, 0.30f),
            "flora_crystal" => new Color(0.60f, 0.85f, 1f),
            _ => new Color(0.6f, 0.6f, 0.62f),
        };
    }
}
