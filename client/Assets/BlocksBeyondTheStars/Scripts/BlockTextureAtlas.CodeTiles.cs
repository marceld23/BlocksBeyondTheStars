// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // The code-painted tiles (#2906): the curated base colours and, per block key, the look a tile without a bundled
    // one gets over its grain and edge — the CodeTiles table, the painters it composes and the bespoke ones.
    public sealed partial class BlockTextureAtlas
    {
        /// <summary>Paints one block's look into the tile at the pixel origin (<paramref name="ox"/>, <paramref name="oy"/>),
        /// drawing from the tile's own <paramref name="rng"/> (seeded from the key, shared with the base grain).</summary>
        private delegate void TilePainter(BlockTextureAtlas atlas, int ox, int oy, System.Random rng);

        /// <summary>Runs the painters one after the other on the same tile and random stream.</summary>
        private static TilePainter Steps(params TilePainter[] steps) => (a, ox, oy, rng) =>
        {
            foreach (var step in steps)
            {
                step(a, ox, oy, rng);
            }
        };

        private static TilePainter Speckled(Color c, int count, int dy = 0) => (a, ox, oy, rng) => a.Speckle(ox, oy + dy, rng, c, count);

        private static TilePainter Blades(int count, int maxTop, Color blade, Color tip)
            => (a, ox, oy, rng) => a.PaintBlades(ox, oy, rng, count, maxTop, blade, tip);

        private static TilePainter Mushrooms(Color stem, Color cap, int count) => (a, ox, oy, rng) => a.PaintMushrooms(ox, oy, rng, stem, cap, count);

        private static TilePainter Crystals(Color facet, Color shade) => (a, ox, oy, rng) => a.PaintCrystals(ox, oy, rng, facet, shade);

        private static TilePainter Columns(Color shade, Color lit, int count) => (a, ox, oy, rng) => a.PaintColumns(ox, oy, rng, shade, lit, count);

        private static TilePainter Filled(Color c) => (a, ox, oy, rng) => a.FillTile(ox, oy, rng, c);

        /// <summary>A method rather than a delegate field, so the table below never depends on static initialisation order.</summary>
        private static void Cleared(BlockTextureAtlas a, int ox, int oy, System.Random rng) => a.ClearTile(ox, oy);

        private static TilePainter Threads(int count, Color strand, Color bead) => (a, ox, oy, rng) => a.PaintHangingThreads(ox, oy, rng, count, strand, bead);

        private static TilePainter Petals(Color petal, Color gap) => (a, ox, oy, _) => a.PaintPetalRays(ox, oy, petal, gap);

        private static TilePainter Lens(Color c) => (a, ox, oy, _) => a.LightLens(ox, oy, c);

        private static TilePainter Band(Color band) => (a, ox, oy, _) => a.PaintLightBand(ox, oy, band);

        /// <summary>
        /// The look a code-painted tile gets over its base colour, grain and edge, per block key (#2906: one switch case
        /// per key in <c>Decorate</c> before). A key without an entry keeps the plain tile. Most entries are dormant
        /// fallbacks behind a bundled tile — they paint only when no texture layer has one. Every entry is pinned pixel
        /// for pixel by <c>BlockAtlasLooksGoldenEditModeTests</c>.
        /// </summary>
        private static readonly Dictionary<string, TilePainter> CodeTiles = new Dictionary<string, TilePainter>
        {
            ["obsidian"] = (a, ox, oy, rng) => a.PaintObsidianGlints(ox, oy, rng),
            ["ancient_brick"] = (a, ox, oy, _) => a.PaintMasonry(ox, oy),
            ["rune_stone"] = (a, ox, oy, _) => a.PaintRuneGlyph(ox, oy),
            ["iron_wall"] = (a, ox, oy, _) => a.PaintRivetedPanel(ox, oy),
            ["strip_light_cyan"] = Band(new Color(0.62f, 0.95f, 1f)),
            ["strip_light_warm"] = Band(new Color(1f, 0.85f, 0.55f)),
            ["medbay_panel"] = (a, ox, oy, _) => a.PaintMedbayPanel(ox, oy),
            ["lab_panel"] = (a, ox, oy, _) => a.PaintLabPanel(ox, oy),
            ["cargo_floor"] = (a, ox, oy, _) => a.PaintHazardStripes(ox, oy),
            ["engine_nozzle"] = (a, ox, oy, _) => a.PaintNozzleRing(ox, oy),
            ["engine_panel"] = (a, ox, oy, _) => a.PaintEnginePanel(ox, oy),
            ["iron_ore"] = Speckled(new Color(0.75f, 0.55f, 0.40f), 26),
            ["copper_ore"] = Speckled(new Color(0.85f, 0.50f, 0.30f), 26),
            ["titanium_ore"] = Speckled(new Color(0.80f, 0.82f, 0.88f), 26),
            ["silicate"] = Speckled(new Color(0.95f, 0.92f, 0.75f), 18),
            ["data_cache"] = (a, ox, oy, _) => a.PaintCircuitGrid(ox, oy),
            ["ice"] = SheenStreak,
            ["glass"] = SheenStreak,
            ["force_field"] = (a, ox, oy, rng) => a.PaintForceField(ox, oy, rng),
            ["water"] = (a, ox, oy, _) => a.PaintWaveCrests(ox, oy),
            ["lava"] = (a, ox, oy, rng) => a.PaintLavaCrust(ox, oy, rng),

            // A few short blades poking up from the soil edge → grassy top read.
            ["grass"] = Blades(9, Tile / 3,
                new Color(0.20f, 0.50f, 0.18f), new Color(0.48f, 0.82f, 0.40f)),

            // Leafy blades rising from the base + lighter leaf tips + scattered highlights.
            ["flora_plant"] = Steps(
                Blades(8, Tile - 4,
                    new Color(0.16f, 0.52f, 0.20f), new Color(0.50f, 0.88f, 0.42f)),
                Speckled(new Color(0.50f, 0.88f, 0.42f), 10)),

            // Faceted shards: a bright spine with a shaded side + a tip glint, then sparkle.
            ["flora_crystal"] = Steps(
                Crystals(new Color(0.88f, 0.96f, 1f), new Color(0.30f, 0.45f, 0.72f)),
                Speckled(new Color(0.88f, 0.96f, 1f), 14)),

            // Many fine fronds, deeper green than a plain plant.
            ["flora_fern"] = Blades(12, Tile - 6,
                new Color(0.12f, 0.42f, 0.16f), new Color(0.38f, 0.72f, 0.34f)),

            // Slim stems with bright blossoms speckled across the top.
            ["flora_flower"] = Steps(
                Blades(6, Tile - 8,
                    new Color(0.18f, 0.50f, 0.22f), new Color(0.40f, 0.78f, 0.38f)),
                Speckled(new Color(0.96f, 0.62f, 0.78f), 9, Tile / 2),
                Speckled(new Color(0.98f, 0.86f, 0.36f), 6, Tile / 2)),

            // Dense low foliage with red berries.
            ["flora_bush"] = Steps(
                Blades(14, Tile / 2 + 6,
                    new Color(0.14f, 0.40f, 0.16f), new Color(0.34f, 0.66f, 0.30f)),
                Speckled(new Color(0.82f, 0.18f, 0.20f), 10)),

            // Tall full-height strands.
            ["flora_vine"] = Blades(7, Tile - 2,
                new Color(0.13f, 0.38f, 0.18f), new Color(0.30f, 0.60f, 0.30f)),

            // Capped mushrooms, warm brown stems + red caps.
            ["flora_mushroom"] = Mushrooms(new Color(0.86f, 0.84f, 0.74f),
                new Color(0.74f, 0.22f, 0.18f), 4),

            // Thick upright columns, desert green.
            ["flora_cactus"] = Steps(
                Columns(new Color(0.20f, 0.46f, 0.24f), new Color(0.34f, 0.62f, 0.34f), 3),
                Speckled(new Color(0.92f, 0.94f, 0.70f), 6)), // spines

            // Sparse brittle olive/tan twigs.
            ["flora_dryshrub"] = Blades(7, Tile / 2 + 4,
                new Color(0.42f, 0.36f, 0.18f), new Color(0.62f, 0.54f, 0.30f)),

            // Tall thin blue-green reeds.
            ["flora_reed"] = Blades(8, Tile - 2,
                new Color(0.16f, 0.44f, 0.34f), new Color(0.40f, 0.74f, 0.58f)),

            // Bioluminescent mushrooms with cyan caps + glow specks.
            ["flora_glowcap"] = Steps(
                Mushrooms(new Color(0.70f, 0.78f, 0.82f),
                    new Color(0.30f, 0.86f, 0.92f), 4),
                Speckled(new Color(0.55f, 0.95f, 1f), 12)),

            // Generation 11, cave flora — code-painted fallbacks behind the bundled AI tiles (used when a texture pack or
            // the official-only mode leaves a key without one). Every wild plant
            // is re-coloured by the world's (or, for the prism bloom, the plant's own) hue in the shader, so these
            // tiles only carry the light/dark pattern: the dome of a cap, a moss carpet, hanging threads, petals.

            // The dark cave mushroom: a mottled cap surface (the dome form wears it) with pale gill spots.
            ["flora_cavecap"] = Steps(
                Filled(new Color(0.30f, 0.26f, 0.28f)),
                Speckled(new Color(0.52f, 0.46f, 0.48f), 26),
                Speckled(new Color(0.74f, 0.70f, 0.66f), 8)),

            // A low glowing carpet (cutout billboard): short pale tufts with bright tips.
            ["flora_glowmoss"] = Steps(
                Cleared,
                Blades(18, Tile / 3 + 4, new Color(0.62f, 0.78f, 0.66f), new Color(0.95f, 1f, 0.95f))),

            // Threads hanging from a cave ceiling (cutout billboard, drawn root-at-top like the hanging kelp):
            // thin strands of varied length, each ending in a bright bead.
            ["flora_glowthread"] = Steps(
                Cleared,
                Threads(9, new Color(0.70f, 0.80f, 0.84f), new Color(1f, 1f, 1f))),

            // The rainbow class: near-white petals around a bright core (the sphere form wears it), so the
            // plant's own colour comes through at full strength.
            ["flora_prismbloom"] = Steps(
                Filled(new Color(0.86f, 0.86f, 0.88f)),
                Petals(new Color(0.98f, 0.98f, 1f), new Color(0.66f, 0.66f, 0.70f))),

            // Pale icy crystal bloom.
            ["flora_frostflower"] = Steps(
                Crystals(new Color(0.82f, 0.94f, 1f), new Color(0.46f, 0.62f, 0.82f)),
                Speckled(new Color(0.92f, 0.98f, 1f), 12)),

            // Charred stems with glowing ember blossoms.
            ["flora_emberbloom"] = Steps(
                Blades(6, Tile / 2 + 6,
                    new Color(0.18f, 0.12f, 0.10f), new Color(0.40f, 0.22f, 0.16f)),
                Speckled(new Color(0.98f, 0.52f, 0.18f), 10, Tile / 3),
                Speckled(new Color(1f, 0.82f, 0.34f), 6, Tile / 3)),

            // A farmed berry bush (#627): fuller foliage than the wild bush, and the fruit is deliberately
            // dense + ripe-red — this tile has to read as FOOD across a greenhouse aisle. Unlike wild flora
            // it never takes the world hue (see GameBootstrap.RebuildFloraTints), so what is painted here
            // is what the player sees on every world.
            ["flora_cropberry"] = Steps(
                Blades(16, Tile / 2 + 10,
                    new Color(0.13f, 0.42f, 0.17f), new Color(0.36f, 0.70f, 0.32f)),
                Speckled(new Color(0.86f, 0.14f, 0.18f), 16),
                Speckled(new Color(0.98f, 0.44f, 0.42f), 6)), // highlight on the ripe side of a berry

            // Farmed cereal (#1204): tall stalks under a band of golden ears. The other un-tinted crop, so
            // it has to read as grain rather than one more green bush from across the aisle.
            ["flora_cropgrain"] = Steps(
                Blades(14, Tile - 4,
                    new Color(0.30f, 0.46f, 0.16f), new Color(0.62f, 0.66f, 0.28f)),
                Speckled(new Color(0.86f, 0.70f, 0.24f), 18, Tile / 2),
                Speckled(new Color(0.98f, 0.88f, 0.46f), 8, Tile / 2)),

            // Farmed mushrooms (#1204): plump tan caps on pale stems, no glow — the edible kind.
            ["flora_cropshroom"] = Mushrooms(new Color(0.88f, 0.84f, 0.74f),
                new Color(0.66f, 0.50f, 0.32f), 5),

            ["hydro_tray"] = (a, ox, oy, rng) => a.PaintHydroTray(ox, oy, rng),
            ["light_white"] = Lens(new Color(0.95f, 0.97f, 1f)),
            ["light_red"] = Lens(new Color(1f, 0.22f, 0.22f)),
            ["light_green"] = Lens(new Color(0.24f, 1f, 0.36f)),
        };

        /// <summary>Ice and glass share one look (a method, like <see cref="Cleared"/>, so the table needs no field order).</summary>
        private static void SheenStreak(BlockTextureAtlas a, int ox, int oy, System.Random rng) => a.PaintSheenStreak(ox, oy);

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

        /// <summary>Makes a whole tile transparent — the cutout background a code-painted leafy plant is drawn onto.</summary>
        private void ClearTile(int ox, int oy)
        {
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < Tile; x++)
            {
                for (int y = 0; y < Tile; y++)
                {
                    Texture.SetPixel(ox + x, oy + y, clear);
                }
            }
        }

        /// <summary>Fills a tile with a colour and a light per-pixel grain (no tiled edge — for plant surfaces).</summary>
        private void FillTile(int ox, int oy, System.Random rng, Color color)
        {
            for (int x = 0; x < Tile; x++)
            {
                for (int y = 0; y < Tile; y++)
                {
                    float n = 0.88f + 0.24f * (float)rng.NextDouble();
                    Texture.SetPixel(ox + x, oy + y, new Color(color.r * n, color.g * n, color.b * n, 1f));
                }
            }
        }

        /// <summary>Strands hanging down from the tile's top row, each of its own length, ending in a bright bead.</summary>
        private void PaintHangingThreads(int ox, int oy, System.Random rng, int count, Color strand, Color bead)
        {
            for (int s = 0; s < count; s++)
            {
                int tx = 2 + rng.Next(Tile - 4);
                int end = Tile / 5 + rng.Next(Tile / 2);
                for (int y = Tile - 1; y > end; y--)
                {
                    Texture.SetPixel(ox + tx, oy + y, strand);
                }

                PutDot(ox + System.Math.Min(tx, Tile - 2), oy + end, bead);
            }
        }

        /// <summary>Radial petal rays around a bright centre, darker gaps between them.</summary>
        private void PaintPetalRays(int ox, int oy, Color petal, Color gap)
        {
            float c = (Tile - 1) * 0.5f;
            for (int x = 0; x < Tile; x++)
            {
                for (int y = 0; y < Tile; y++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    float ray = Mathf.Abs(Mathf.Sin(Mathf.Atan2(dy, dx) * 4f)); // eight petals
                    Color px = d < 0.22f ? petal : Color.Lerp(gap, petal, Mathf.Clamp01(ray * 1.3f - d * 0.35f));
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

        private void PaintObsidianGlints(int ox, int oy, System.Random rng)
        {
            // Sparse violet glints — the glassy sheen that tells obsidian apart from plain basalt.
            for (int i = 0; i < 5; i++)
            {
                int gx = ox + 2 + rng.Next(Tile - 4);
                int gy = oy + 2 + rng.Next(Tile - 4);
                Texture.SetPixel(gx, gy, new Color(0.45f, 0.30f, 0.65f));
                Texture.SetPixel(gx + 1, gy, new Color(0.28f, 0.18f, 0.42f));
            }
        }

        private void PaintMasonry(int ox, int oy)
        {
            // Coursed masonry: mortar lines every quarter tile, joints offset row to row.
            var mortar = new Color(0.44f, 0.41f, 0.34f);
            int course = Tile / 4;
            for (int row = 0; row * course < Tile; row++)
            {
                int y = row * course;
                for (int x = 0; x < Tile; x++)
                {
                    Texture.SetPixel(ox + x, oy + y, mortar);
                }

                int joint = (row % 2 == 0 ? Tile / 4 : 3 * Tile / 4);
                for (int y2 = y + 1; y2 < y + course && y2 < Tile; y2++)
                {
                    Texture.SetPixel(ox + joint, oy + y2, mortar);
                }
            }
        }

        private void PaintRuneGlyph(int ox, int oy)
        {
            // A carved glyph: strokes cut into the slate, with a lighter chisel edge beside them so
            // the sign reads at block scale (the emissive glow is a per-voxel modifier, not the tile).
            var cut = new Color(0.26f, 0.28f, 0.33f);
            var edge = new Color(0.66f, 0.69f, 0.74f);
            void Stroke(int x0, int y0, int x1, int y1)
            {
                int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int s = 0; s <= steps; s++)
                {
                    int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, steps == 0 ? 0f : s / (float)steps));
                    int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, steps == 0 ? 0f : s / (float)steps));
                    Texture.SetPixel(ox + Mathf.Clamp(x, 0, Tile - 1), oy + Mathf.Clamp(y, 0, Tile - 1), cut);
                    Texture.SetPixel(ox + Mathf.Clamp(x + 1, 0, Tile - 1), oy + Mathf.Clamp(y, 0, Tile - 1), edge);
                }
            }

            int m = Tile / 8;
            Stroke(Tile / 2, m, Tile / 2, Tile - m);           // the stem
            Stroke(Tile / 2, Tile - m * 2, Tile - m * 2, Tile - m); // upper arm
            Stroke(Tile / 2, Tile / 2, m * 2, Tile / 2 + m);        // lower arm
            Stroke(m * 2, m * 2, Tile - m * 2, m * 2);              // base bar
        }

        private void PaintRivetedPanel(int ox, int oy)
        {
            // Panel rivets near the corners + a central seam.
            PutDot(ox + 4, oy + 4, new Color(0.30f, 0.31f, 0.34f));
            PutDot(ox + Tile - 5, oy + 4, new Color(0.30f, 0.31f, 0.34f));
            PutDot(ox + 4, oy + Tile - 5, new Color(0.30f, 0.31f, 0.34f));
            PutDot(ox + Tile - 5, oy + Tile - 5, new Color(0.30f, 0.31f, 0.34f));
            for (int x = 2; x < Tile - 2; x++) Texture.SetPixel(ox + x, oy + Tile / 2, new Color(0.42f, 0.44f, 0.49f));
        }

        /// <summary>The strip lights' band; the caller passes the band colour (cyan or warm).</summary>
        private void PaintLightBand(int ox, int oy, Color band)
        {
            // A bright horizontal light band through the dark housing — the emission term makes
            // the band glow in its colour while the housing stays a dark trim.
            for (int x = 2; x < Tile - 2; x++)
            {
                for (int yb = Tile / 2 - 5; yb <= Tile / 2 + 5; yb++)
                {
                    float core = 1f - Mathf.Abs(yb - Tile / 2) / 6f; // brightest at the centre line
                    var c = Color.Lerp(band * 0.45f, band, core);
                    Texture.SetPixel(ox + x, oy + yb, new Color(c.r, c.g, c.b, 1f));
                }
            }
        }

        private void PaintMedbayPanel(int ox, int oy)
        {
            // Clean panel: thin seam border + a small medical cross at the centre.
            for (int i = 1; i < Tile - 1; i++)
            {
                Texture.SetPixel(ox + i, oy + 1, new Color(0.70f, 0.80f, 0.86f));
                Texture.SetPixel(ox + i, oy + Tile - 2, new Color(0.70f, 0.80f, 0.86f));
                Texture.SetPixel(ox + 1, oy + i, new Color(0.70f, 0.80f, 0.86f));
                Texture.SetPixel(ox + Tile - 2, oy + i, new Color(0.70f, 0.80f, 0.86f));
            }

            for (int i = -7; i <= 7; i++)
            {
                for (int w = -2; w <= 2; w++)
                {
                    Texture.SetPixel(ox + Tile / 2 + i, oy + Tile / 2 + w, new Color(0.25f, 0.65f, 0.85f));
                    Texture.SetPixel(ox + Tile / 2 + w, oy + Tile / 2 + i, new Color(0.25f, 0.65f, 0.85f));
                }
            }
        }

        private void PaintLabPanel(int ox, int oy)
        {
            // Cool tech panel: a faint seam grid (reads as console housing).
            for (int i = 8; i < Tile; i += 16)
            {
                for (int j = 2; j < Tile - 2; j++)
                {
                    Texture.SetPixel(ox + i, oy + j, new Color(0.55f, 0.70f, 0.80f));
                    Texture.SetPixel(ox + j, oy + i, new Color(0.55f, 0.70f, 0.80f));
                }
            }
        }

        private void PaintHazardStripes(int ox, int oy)
        {
            // Black diagonal hazard stripes over the amber base.
            for (int x = 0; x < Tile; x++)
            {
                for (int yb = 0; yb < Tile; yb++)
                {
                    if (((x + yb) / 8) % 2 == 0)
                    {
                        Texture.SetPixel(ox + x, oy + yb, new Color(0.13f, 0.13f, 0.14f));
                    }
                }
            }
        }

        private void PaintNozzleRing(int ox, int oy)
        {
            // A glowing nozzle-throat ring — with the block's emission this is the faint idle
            // glow that keeps a landed ship's engines from looking dead.
            float c2 = (Tile - 1) * 0.5f;
            for (int x = 0; x < Tile; x++)
            {
                for (int yb = 0; yb < Tile; yb++)
                {
                    float d = Mathf.Sqrt((x - c2) * (x - c2) + (yb - c2) * (yb - c2));
                    if (d >= 18f && d <= 24f)
                    {
                        float k = 1f - Mathf.Abs(d - 21f) / 3f;
                        Texture.SetPixel(ox + x, oy + yb, Color.Lerp(new Color(0.25f, 0.16f, 0.10f), new Color(1f, 0.55f, 0.20f), k));
                    }
                }
            }
        }

        private void PaintEnginePanel(int ox, int oy)
        {
            // Dark industrial panel with orange warning corners + a centre seam.
            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10 - i; j++)
                {
                    Texture.SetPixel(ox + 2 + j, oy + 2 + i, new Color(0.95f, 0.55f, 0.12f));
                    Texture.SetPixel(ox + Tile - 3 - j, oy + Tile - 3 - i, new Color(0.95f, 0.55f, 0.12f));
                }
            }

            for (int x = 2; x < Tile - 2; x++)
            {
                Texture.SetPixel(ox + x, oy + Tile / 2, new Color(0.34f, 0.35f, 0.38f));
            }
        }

        private void PaintCircuitGrid(int ox, int oy)
        {
            // Faint circuit grid.
            for (int i = 4; i < Tile; i += 6)
            {
                for (int j = 2; j < Tile - 2; j++)
                {
                    Texture.SetPixel(ox + i, oy + j, new Color(0.45f, 0.95f, 1f));
                    Texture.SetPixel(ox + j, oy + i, new Color(0.30f, 0.80f, 0.95f));
                }
            }
        }

        private void PaintSheenStreak(int ox, int oy)
        {
            // Diagonal sheen streak.
            for (int d = 0; d < Tile; d++)
            {
                int x = d, y = (d + 6) % Tile;
                Texture.SetPixel(ox + x, oy + y, new Color(0.95f, 0.98f, 1f));
            }
        }

        private void PaintForceField(int ox, int oy, System.Random rng)
        {
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
        }

        private void PaintWaveCrests(int ox, int oy)
        {
            // Horizontal wave crests (a few lighter bands that ripple along x).
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

        private void PaintLavaCrust(int ox, int oy, System.Random rng)
        {
            // Dark cooled crust with glowing cracks/veins winding through it + hot flecks.
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

        private void PaintHydroTray(int ox, int oy, System.Random rng)
        {
            // A shallow nutrient tray: a metal rim around a pool of faintly glowing growth gel.
            var rim = new Color(0.34f, 0.38f, 0.42f);
            var gel = new Color(0.24f, 0.62f, 0.48f);
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    bool border = x < 4 || y < 4 || x >= Tile - 4 || y >= Tile - 4;
                    Texture.SetPixel(ox + x, oy + y, border ? rim : gel * (0.88f + 0.24f * (float)rng.NextDouble()));
                }
            }

            Speckle(ox, oy, rng, new Color(0.52f, 0.88f, 0.72f), 10); // nutrient bubbles catching the light
        }

        /// <summary>Fallback tint for a block that has neither a bundled tile, a palette entry nor an authored colour.</summary>
        private static readonly Color DefaultTint = new Color(0.6f, 0.6f, 0.62f);

        /// <summary>Curated procedural tint per key; null when the key has no palette entry (the caller
        /// then falls back to the authored blocks.json colour, then <see cref="DefaultTint"/>).</summary>
        private static Color? BaseColor(string key) => key switch
        {
            "stone" => new Color(0.55f, 0.55f, 0.57f),
            "dirt" => new Color(0.45f, 0.32f, 0.20f),
            "basalt" => new Color(0.24f, 0.24f, 0.27f),
            "obsidian" => new Color(0.10f, 0.07f, 0.14f), // glassy black with a violet cast (#477)
            "ice" => new Color(0.70f, 0.85f, 0.95f),
            "iron_ore" => new Color(0.58f, 0.50f, 0.46f),
            "copper_ore" => new Color(0.60f, 0.46f, 0.38f),
            "silicate" => new Color(0.80f, 0.78f, 0.60f),
            "carbon" => new Color(0.16f, 0.16f, 0.18f),
            "titanium_ore" => new Color(0.58f, 0.60f, 0.66f),
            "data_cache" => new Color(0.18f, 0.40f, 0.55f),
            "glass" => new Color(0.82f, 0.91f, 0.95f), // milky/frosted, not clear (you can tell it's glass)
            "glass_clear" => new Color(0.90f, 0.96f, 0.99f), // the clear exception (#1274) — near-white; the shader drops its alpha
            "force_field" => new Color(0.35f, 0.80f, 1f),
            "iron_wall" => new Color(0.55f, 0.57f, 0.62f),
            "strip_light_cyan" => new Color(0.22f, 0.26f, 0.30f), // dark housing; the bright band is painted in Decorate
            "strip_light_warm" => new Color(0.28f, 0.24f, 0.20f),
            "medbay_panel" => new Color(0.91f, 0.95f, 0.97f),     // clean white-blue (medbay)
            "lab_panel" => new Color(0.75f, 0.85f, 0.91f),        // cool tech blue (lab/cockpit/console)
            "cargo_floor" => new Color(0.85f, 0.63f, 0.10f),      // hazard amber (cargo)
            "engine_panel" => new Color(0.23f, 0.23f, 0.25f),     // dark industrial (workshop/engine)
            "matter_forge" => new Color(0.42f, 0.30f, 0.55f),     // violet sci-fi housing (transmuter; the core glows via emission)
            "engine_nozzle" => new Color(0.17f, 0.17f, 0.20f),    // dark housing; the throat ring glows (Decorate)
            "water" => new Color(0.20f, 0.42f, 0.85f),
            "lava" => new Color(0.90f, 0.35f, 0.10f),
            "fire" => new Color(1.00f, 0.45f, 0.12f),  // bright flame (glows via emission, alpha-blended)
            "ash" => new Color(0.17f, 0.16f, 0.16f),   // charred remains
            "sand" => new Color(0.85f, 0.78f, 0.52f),
            "mud" => new Color(0.36f, 0.28f, 0.18f),
            "grass" => new Color(0.32f, 0.62f, 0.28f),
            "wood_log" => new Color(0.42f, 0.29f, 0.16f),   // brown bark
            "tree_leaves" => new Color(0.22f, 0.50f, 0.20f), // forest-green canopy
            "giant_log" => new Color(0.34f, 0.21f, 0.12f),   // #1783: darker, older bark
            "giant_leaves" => new Color(0.16f, 0.42f, 0.18f), // #1783: a deeper canopy green
            // #2851: the tree-variety blocks, for a build whose bundled tile is missing (the crowns are re-tinted anyway).
            "tree_leaves_b" => new Color(0.30f, 0.55f, 0.22f),   // leaf slot B: a lighter canopy green
            "tree_leaves_c" => new Color(0.18f, 0.45f, 0.26f),   // leaf slot C: a bluish canopy green
            "blossom_leaves" => new Color(0.88f, 0.62f, 0.74f),  // the blossom crown keeps its painted pink
            "wood_log_b" => new Color(0.82f, 0.80f, 0.74f),      // pale birch-like bark
            "saguaro" => new Color(0.30f, 0.52f, 0.30f),         // the cactus body's green (the lantern fruit has a data colour)
            "crystal" => new Color(0.55f, 0.75f, 0.95f),
            "ancient_brick" => new Color(0.60f, 0.56f, 0.47f), // weathered sandy masonry
            "rune_stone" => new Color(0.48f, 0.50f, 0.55f),    // cold grey slate, carved (glyphs in Decorate)
            "flora_plant" => new Color(0.25f, 0.70f, 0.30f),
            "flora_crystal" => new Color(0.60f, 0.85f, 1f),
            "flora_kelp" => new Color(0.15f, 0.45f, 0.32f),  // deep sea-green stalk
            "flora_lily" => new Color(0.30f, 0.62f, 0.34f),  // lily-pad green
            "flora_cropberry" => new Color(0.22f, 0.58f, 0.24f), // farmed bush (#627): leafy green, berries in Decorate
            "flora_cropgrain" => new Color(0.72f, 0.60f, 0.24f), // farmed cereal (#1204): golden ears
            "flora_cropshroom" => new Color(0.70f, 0.58f, 0.42f), // farmed mushroom bed (#1204): tan caps
            "hydro_tray" => new Color(0.28f, 0.34f, 0.38f),      // dark tray housing; the nutrient gel glows in Decorate
            _ => null,
        };
    }
}
