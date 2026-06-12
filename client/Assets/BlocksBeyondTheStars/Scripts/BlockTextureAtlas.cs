using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
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
        // 16x16 = 256 tile slots (1024x1024 atlas). data/blocks.json already has 80 blocks; the old 8x8 = 64
        // slots silently left every block with id >= 64 (the newer flora + doors) untextured — a grey, alpha-
        // less tile, which also broke their cutout leaves. Keep this comfortably above the block count.
        public const int Cols = 16;
        public const int Rows = 16;

        public Texture2D Texture { get; }

        /// <summary>A tangent-space normal map derived from the colour atlas (Sobel on luminance), so flat
        /// block faces catch the light with micro-relief (cracks, rivets, grain). Same tile layout as
        /// <see cref="Texture"/>; the block shader samples it for per-pixel lighting.</summary>
        public Texture2D NormalTexture { get; private set; }

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
            BuildNormalAtlas();
        }

        /// <summary>Derives the normal atlas from the finished colour atlas: per pixel, the luminance
        /// gradient (Sobel) becomes a surface normal (treating brighter = higher), encoded in RGB.</summary>
        private void BuildNormalAtlas()
        {
            int w = Cols * Tile, h = Rows * Tile;
            // linear: normals are data, not colour — without this flag a Linear-color-space build would
            // sRGB-decode the encoded normals on sample and break the per-pixel lighting on every block.
            NormalTexture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var src = Texture.GetPixels();
            var dst = new Color[w * h];
            const float strength = 1.6f;

            float Lum(int x, int y)
            {
                x = x < 0 ? 0 : (x >= w ? w - 1 : x);
                y = y < 0 ? 0 : (y >= h ? h - 1 : y);
                var c = src[y * w + x];
                return c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = Lum(x + 1, y) - Lum(x - 1, y);
                    float dy = Lum(x, y + 1) - Lum(x, y - 1);
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    dst[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            NormalTexture.SetPixels(dst);
            NormalTexture.Apply(updateMipmaps: true);
        }

        private readonly System.Collections.Generic.Dictionary<ushort, Color> _avgColor = new();

        /// <summary>Average opaque colour of a block's atlas tile (cached) — "what this ground looks
        /// like from far away", used for the orbital planet-sphere colours.</summary>
        public Color AverageColor(ushort id)
        {
            if (_avgColor.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var px = Texture.GetPixels(id % Cols * Tile, id / Cols * Tile, Tile, Tile);
            float r = 0f, g = 0f, b = 0f;
            int n = 0;
            for (int i = 0; i < px.Length; i += 4) // every 4th pixel is plenty for an average
            {
                if (px[i].a < 0.5f)
                {
                    continue;
                }

                r += px[i].r; g += px[i].g; b += px[i].b; n++;
            }

            var avg = n > 0 ? new Color(r / n, g / n, b / n) : new Color(0.5f, 0.5f, 0.5f);
            _avgColor[id] = avg;
            return avg;
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
            if (!TryPaintFromAsset(key, ox, oy))
            {
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

            // Water is semi-transparent (alpha < 1) so you can see down into a sea while swimming; the
            // transparent block shader keys off this tile alpha to render water clear-blue (vs frosted glass).
            if (key == "water")
            {
                FadeTileAlpha(ox, oy, 0.62f);
            }
        }

        /// <summary>Sets a uniform alpha across a tile (used to make water see-through in the atlas).</summary>
        private void FadeTileAlpha(int ox, int oy, float alpha)
        {
            for (int px = 0; px < Tile; px++)
            {
                for (int py = 0; py < Tile; py++)
                {
                    var c = Texture.GetPixel(ox + px, oy + py);
                    c.a = alpha;
                    Texture.SetPixel(ox + px, oy + py, c);
                }
            }
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

                case "force_field":
                    // Energy curtain: bright horizontal scan-bands + a few drifting sparks over the cyan base.
                    for (int yy = 0; yy < Tile; yy++)
                    {
                        if (yy % 3 != 0) continue;
                        for (int xx = 0; xx < Tile; xx++)
                        {
                            Texture.SetPixel(ox + xx, oy + yy, new Color(0.65f, 0.96f, 1f));
                        }
                    }

                    Speckle(ox, oy, rng, new Color(0.85f, 1f, 1f), 8);
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

                case "flora_fern":
                    // Many fine fronds, deeper green than a plain plant.
                    PaintBlades(ox, oy, rng, 12, Tile - 6,
                        new Color(0.12f, 0.42f, 0.16f), new Color(0.38f, 0.72f, 0.34f));
                    break;

                case "flora_flower":
                    // Slim stems with bright blossoms speckled across the top.
                    PaintBlades(ox, oy, rng, 6, Tile - 8,
                        new Color(0.18f, 0.50f, 0.22f), new Color(0.40f, 0.78f, 0.38f));
                    Speckle(ox, oy + Tile / 2, rng, new Color(0.96f, 0.62f, 0.78f), 9);
                    Speckle(ox, oy + Tile / 2, rng, new Color(0.98f, 0.86f, 0.36f), 6);
                    break;

                case "flora_bush":
                    // Dense low foliage with red berries.
                    PaintBlades(ox, oy, rng, 14, Tile / 2 + 6,
                        new Color(0.14f, 0.40f, 0.16f), new Color(0.34f, 0.66f, 0.30f));
                    Speckle(ox, oy, rng, new Color(0.82f, 0.18f, 0.20f), 10);
                    break;

                case "flora_vine":
                    // Tall full-height strands.
                    PaintBlades(ox, oy, rng, 7, Tile - 2,
                        new Color(0.13f, 0.38f, 0.18f), new Color(0.30f, 0.60f, 0.30f));
                    break;

                case "flora_mushroom":
                    // Capped mushrooms, warm brown stems + red caps.
                    PaintMushrooms(ox, oy, rng, new Color(0.86f, 0.84f, 0.74f),
                        new Color(0.74f, 0.22f, 0.18f), 4);
                    break;

                case "flora_cactus":
                    // Thick upright columns, desert green.
                    PaintColumns(ox, oy, rng, new Color(0.20f, 0.46f, 0.24f), new Color(0.34f, 0.62f, 0.34f), 3);
                    Speckle(ox, oy, rng, new Color(0.92f, 0.94f, 0.70f), 6); // spines
                    break;

                case "flora_dryshrub":
                    // Sparse brittle olive/tan twigs.
                    PaintBlades(ox, oy, rng, 7, Tile / 2 + 4,
                        new Color(0.42f, 0.36f, 0.18f), new Color(0.62f, 0.54f, 0.30f));
                    break;

                case "flora_reed":
                    // Tall thin blue-green reeds.
                    PaintBlades(ox, oy, rng, 8, Tile - 2,
                        new Color(0.16f, 0.44f, 0.34f), new Color(0.40f, 0.74f, 0.58f));
                    break;

                case "flora_glowcap":
                    // Bioluminescent mushrooms with cyan caps + glow specks.
                    PaintMushrooms(ox, oy, rng, new Color(0.70f, 0.78f, 0.82f),
                        new Color(0.30f, 0.86f, 0.92f), 4);
                    Speckle(ox, oy, rng, new Color(0.55f, 0.95f, 1f), 12);
                    break;

                case "flora_frostflower":
                    // Pale icy crystal bloom.
                    PaintCrystals(ox, oy, rng, new Color(0.82f, 0.94f, 1f), new Color(0.46f, 0.62f, 0.82f));
                    Speckle(ox, oy, rng, new Color(0.92f, 0.98f, 1f), 12);
                    break;

                case "flora_emberbloom":
                    // Charred stems with glowing ember blossoms.
                    PaintBlades(ox, oy, rng, 6, Tile / 2 + 6,
                        new Color(0.18f, 0.12f, 0.10f), new Color(0.40f, 0.22f, 0.16f));
                    Speckle(ox, oy + Tile / 3, rng, new Color(0.98f, 0.52f, 0.18f), 10);
                    Speckle(ox, oy + Tile / 3, rng, new Color(1f, 0.82f, 0.34f), 6);
                    break;

                case "light_white": LightLens(ox, oy, new Color(0.95f, 0.97f, 1f)); break;
                case "light_red": LightLens(ox, oy, new Color(1f, 0.22f, 0.22f)); break;
                case "light_green": LightLens(ox, oy, new Color(0.24f, 1f, 0.36f)); break;
            }
        }

        /// <summary>A glowing lamp lens: a coloured tile with a dark rim and a bright hot centre.</summary>
        private void LightLens(int ox, int oy, Color color)
        {
            Color rim = color * 0.45f;
            float c = (Tile - 1) * 0.5f;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    Color px = d > 0.9f ? rim : Color.Lerp(Color.Lerp(Color.white, color, 0.5f), color, Mathf.Clamp01(d * 1.4f));
                    Texture.SetPixel(ox + x, oy + y, px);
                }
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

        /// <summary>A few capped mushrooms: a short pale stem with a domed cap on top.</summary>
        private void PaintMushrooms(int ox, int oy, System.Random rng, Color stem, Color cap, int count)
        {
            for (int s = 0; s < count; s++)
            {
                int mx = 6 + rng.Next(Tile - 12);
                int stemTop = Tile / 3 + rng.Next(Tile / 4);
                for (int y = 3; y < stemTop; y++)
                {
                    Texture.SetPixel(ox + mx, oy + y, stem);
                    Texture.SetPixel(ox + mx + 1, oy + y, stem);
                }

                int r = 3 + rng.Next(2);
                for (int dx = -r; dx <= r + 1; dx++)
                {
                    for (int dy = 0; dy <= r; dy++)
                    {
                        if (dx * dx + dy * dy <= (r + 1) * (r + 1))
                        {
                            int px = mx + dx, py = stemTop + dy;
                            if (px >= 0 && px < Tile && py >= 0 && py < Tile)
                            {
                                Texture.SetPixel(ox + px, oy + py, cap);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>A few thick upright columns (e.g. cactus): a lit face and a shaded side.</summary>
        private void PaintColumns(int ox, int oy, System.Random rng, Color shade, Color lit, int count)
        {
            for (int s = 0; s < count; s++)
            {
                int cx = 8 + rng.Next(Tile - 16);
                int top = Tile - 4 - rng.Next(8);
                int w = 3 + rng.Next(2);
                for (int y = 2; y < top; y++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        Texture.SetPixel(ox + cx + dx, oy + y, dx == 0 ? shade : lit);
                    }
                }
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
            "glass" => new Color(0.82f, 0.91f, 0.95f), // milky/frosted, not clear (you can tell it's glass)
            "force_field" => new Color(0.35f, 0.80f, 1f),
            "iron_wall" => new Color(0.55f, 0.57f, 0.62f),
            "water" => new Color(0.20f, 0.42f, 0.85f),
            "lava" => new Color(0.90f, 0.35f, 0.10f),
            "fire" => new Color(1.00f, 0.45f, 0.12f),  // bright flame (glows via emission, alpha-blended)
            "ash" => new Color(0.17f, 0.16f, 0.16f),   // charred remains
            "sand" => new Color(0.85f, 0.78f, 0.52f),
            "mud" => new Color(0.36f, 0.28f, 0.18f),
            "grass" => new Color(0.32f, 0.62f, 0.28f),
            "wood_log" => new Color(0.42f, 0.29f, 0.16f),   // brown bark
            "tree_leaves" => new Color(0.22f, 0.50f, 0.20f), // forest-green canopy
            "crystal" => new Color(0.55f, 0.75f, 0.95f),
            "flora_plant" => new Color(0.25f, 0.70f, 0.30f),
            "flora_crystal" => new Color(0.60f, 0.85f, 1f),
            "flora_kelp" => new Color(0.15f, 0.45f, 0.32f),  // deep sea-green stalk
            "flora_lily" => new Color(0.30f, 0.62f, 0.34f),  // lily-pad green
            _ => new Color(0.6f, 0.6f, 0.62f),
        };
    }
}
