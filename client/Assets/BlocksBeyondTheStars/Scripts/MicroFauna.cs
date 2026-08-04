// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How a critter moves through the world — picks its motion model in <see cref="MicroFaunaView"/>.</summary>
    public enum CritterMotion
    {
        Fly,    // airborne flutter/bob above the ground (butterflies, flies, fireflies…)
        Crawl,  // hugs the surface, slow wander (beetles, worms, snails…)
        Swim,   // stays inside a water volume, schools/darts (small fish, tadpoles…)
        Cling,  // mostly still, clinging near a cave ceiling with a faint glow (glow-worms)
        Hop,    // parabolic ground hops with pauses (ash-hoppers, ember-mites…)
        Drift,  // very slow airborne float with a strong bob — balloon / spore feel (gas-bags…)
    }

    /// <summary>When a critter is active across the day/night cycle.</summary>
    public enum CritterTime { Day, Night, Any }

    /// <summary>
    /// One kind of "Kleinstlebewesen" (micro-fauna) archetype: a tiny, purely-cosmetic critter that makes a
    /// planet feel alive. Defined entirely client-side (no server/netcode) — the world VFX views own these the
    /// same way <see cref="AmbientParticles"/> owns drifting dust. Each kind maps to one sprite tile in the
    /// runtime <see cref="MicroFaunaAtlas"/> and carries its motion model, day/night window, size and a small
    /// colour palette. They never attack and aren't synced between players (like the ambient motes).
    /// </summary>
    public sealed class CritterKind
    {
        public string Key;
        public int Tile;             // index into the micro-fauna atlas grid
        public CritterMotion Motion;
        public CritterTime Time;
        public bool Glow;            // rendered additively (fireflies / glow-worms) instead of alpha-blended
        public float Size;           // world-space half-extent of the billboard (~0.08..0.30)
        public float Speed;          // cruise speed, blocks/s
        public bool Groups;          // tends to appear in swarms / schools rather than singly
        public Color[] Palette;      // per-instance tint is chosen from here

        public CritterKind(string key, int tile, CritterMotion motion, CritterTime time, bool glow,
            float size, float speed, bool groups, Color[] palette)
        {
            Key = key; Tile = tile; Motion = motion; Time = time; Glow = glow;
            Size = size; Speed = speed; Groups = groups; Palette = palette;
        }
    }

    /// <summary>
    /// The fixed catalogue of micro-fauna archetypes plus the per-biome / day-night / habitat selection rules.
    /// Pure data + helpers, no Unity scene state — <see cref="MicroFaunaView"/> reads it to decide what to spawn.
    /// </summary>
    public static class MicroFauna
    {
        // Atlas tile indices == position in this array. Keep in sync with MicroFaunaAtlas (Cols×Rows must cover).
        public static readonly CritterKind[] Kinds =
        {
            // --- flying (surface, by day) ---
            new("butterfly", 0, CritterMotion.Fly, CritterTime.Day, false, 0.26f, 1.9f, true,
                new[] { C(0xF2A33C), C(0x4C7BD9), C(0xE25C8A), C(0xF2D24C), C(0x8E5BD9), C(0xE8714C) }),
            new("bee", 4, CritterMotion.Fly, CritterTime.Day, false, 0.14f, 2.4f, true,
                new[] { C(0xF2C541), C(0xE8A93C) }),
            new("dragonfly", 5, CritterMotion.Fly, CritterTime.Day, false, 0.28f, 3.2f, false,
                new[] { C(0x4CC6C2), C(0x6CD24C), C(0x4C92E8) }),
            new("fly", 3, CritterMotion.Fly, CritterTime.Day, false, 0.10f, 2.2f, true,
                new[] { C(0x2C2E33), C(0x3A3D44) }),
            // --- flying (surface, by night) ---
            new("moth", 1, CritterMotion.Fly, CritterTime.Night, false, 0.22f, 1.7f, false,
                new[] { C(0xC9BBA0), C(0x9E907A), C(0xB7A98C) }),
            new("firefly", 2, CritterMotion.Fly, CritterTime.Night, true, 0.12f, 1.3f, true,
                new[] { C(0xE9F27A), C(0xC8F26A) }),
            // --- crawling (surface) ---
            new("beetle", 6, CritterMotion.Crawl, CritterTime.Any, false, 0.16f, 0.7f, false,
                new[] { C(0x2E2622), C(0x3A4A2E), C(0x5B3A2E) }),
            new("ant", 7, CritterMotion.Crawl, CritterTime.Any, false, 0.08f, 0.9f, true,
                new[] { C(0x2A211C), C(0x4A2A1C) }),
            new("caterpillar", 8, CritterMotion.Crawl, CritterTime.Day, false, 0.14f, 0.45f, false,
                new[] { C(0x7AC24C), C(0xC2A24C), C(0x4CB0C2) }),
            new("worm", 9, CritterMotion.Crawl, CritterTime.Any, false, 0.12f, 0.5f, false,
                new[] { C(0xD08C8C), C(0xB07A6A) }),
            new("snail", 10, CritterMotion.Crawl, CritterTime.Any, false, 0.16f, 0.35f, false,
                new[] { C(0xB29A6A), C(0x9A7A5A) }),
            new("spider", 11, CritterMotion.Crawl, CritterTime.Any, false, 0.14f, 1.0f, false,
                new[] { C(0x24201E), C(0x3A2A24) }),
            // --- aquatic (inside water) ---
            new("fish", 12, CritterMotion.Swim, CritterTime.Any, false, 0.18f, 2.0f, true,
                new[] { C(0xE2904C), C(0x4CA6E2), C(0xE2C84C), C(0xD24C6A) }),
            new("tadpole", 13, CritterMotion.Swim, CritterTime.Any, false, 0.10f, 1.4f, true,
                new[] { C(0x2E2A26), C(0x3A352E) }),
            new("waterbeetle", 14, CritterMotion.Swim, CritterTime.Any, false, 0.14f, 1.6f, false,
                new[] { C(0x243028), C(0x2E3A30) }),
            new("strider", 15, CritterMotion.Swim, CritterTime.Any, false, 0.14f, 1.8f, true,
                new[] { C(0x2A2622), C(0x3A322A) }),
            // --- cave (underground glow) ---
            new("glowworm", 16, CritterMotion.Cling, CritterTime.Night, true, 0.12f, 0.0f, true,
                new[] { C(0x6CF2C8), C(0x8CF29A), C(0x6CC8F2) }),
            // --- alien kinds (exotic biomes get their own signature species) ---
            new("prismwing", 17, CritterMotion.Fly, CritterTime.Day, true, 0.24f, 1.6f, true,
                new[] { C(0x6CE8F2), C(0xE86CF2), C(0x9A6CF2), C(0x6CF2B8) }),
            new("crystalbeetle", 18, CritterMotion.Crawl, CritterTime.Any, false, 0.16f, 0.6f, false,
                new[] { C(0x7AE2E8), C(0xE87AD0), C(0xA88AF0), C(0x8AF0C8) }),
            new("embermite", 19, CritterMotion.Hop, CritterTime.Night, true, 0.10f, 1.2f, true,
                new[] { C(0xF2914C), C(0xF25C3C), C(0xF2C44C) }),
            new("ashhopper", 20, CritterMotion.Hop, CritterTime.Day, false, 0.14f, 1.6f, false,
                new[] { C(0x8A8580), C(0xA89A80), C(0x6A6660) }),
            new("frostmite", 21, CritterMotion.Crawl, CritterTime.Any, false, 0.09f, 0.5f, true,
                new[] { C(0xCFE4F2), C(0xE8F0F6), C(0xA8C8E2) }),
            new("gasbag", 22, CritterMotion.Drift, CritterTime.Any, false, 0.22f, 0.4f, true,
                new[] { C(0xC8A8E8), C(0x9AD8D2), C(0xE8A8C4), C(0xB8D89A) }),
            new("sporedrifter", 23, CritterMotion.Drift, CritterTime.Night, true, 0.13f, 0.3f, true,
                new[] { C(0xA8F26A), C(0x6AF2C4), C(0xC4F26A) }),
            new("skyray", 24, CritterMotion.Fly, CritterTime.Day, false, 0.40f, 1.1f, false,
                new[] { C(0x8AA2C2), C(0xC2B08A), C(0xB08AC2) }),
            new("sandskimmer", 25, CritterMotion.Fly, CritterTime.Day, false, 0.16f, 3.4f, true,
                new[] { C(0xD2B27A), C(0xC29A5A), C(0xE2C48A) }),
            new("glowplankton", 26, CritterMotion.Swim, CritterTime.Night, true, 0.09f, 0.8f, true,
                new[] { C(0x6AE8F2), C(0x8AF2C8), C(0x6AC0F2) }),
            new("cavemoth", 27, CritterMotion.Fly, CritterTime.Any, false, 0.20f, 1.5f, false,
                new[] { C(0xB0A8C2), C(0x9A94A8), C(0xC2BCD0) }),
        };

        private static readonly Dictionary<string, int> IndexByKey = BuildIndex();

        private static Dictionary<string, int> BuildIndex()
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < Kinds.Length; i++) d[Kinds[i].Key] = i;
            return d;
        }

        public static int Index(string key) => IndexByKey.TryGetValue(key, out int i) ? i : 0;

        /// <summary>0 = no surface micro-fauna here, up to ~1.3 = lush. Drives the target population per biome.</summary>
        public static float Richness(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest":
                case "tropical": return 1.3f;
                case "swamp":
                case "wetland": return 1.2f;
                case "temperate":
                case "grass":
                case "plains":
                case "savanna": return 1.0f;
                case "ocean": return 0.7f;
                case "desert": return 0.6f;
                case "crystal": return 0.65f;
                case "alpine": return 0.5f;
                case "rock":
                case "barren": return 0.45f;
                case "lava":
                case "ashen":
                case "volcanic": return 0.5f;
                case "ice":
                case "frozen":
                case "tundra":
                case "snow": return 0.4f;
                default: return 0.8f;
            }
        }

        /// <summary>Weighted surface (fly+crawl+hop+drift) kinds for a biome, already filtered to the active
        /// day/night window. Repeated entries == higher spawn weight. <paramref name="wet"/> (rain/storm)
        /// grounds most flyers and brings out the worms and snails instead. Exotic biome keys (crystal, lava,
        /// ice, desert…) add their own alien signature species on top of the coarse lush/cold/hot buckets.
        /// Returns indices into <see cref="Kinds"/>.</summary>
        public static void SurfaceKinds(string biome, bool night, bool wet, List<int> into)
        {
            into.Clear();
            string b = (biome ?? string.Empty).ToLowerInvariant();
            bool lush = b is "jungle" or "forest" or "tropical" or "swamp" or "wetland";
            bool cold = b is "ice" or "frozen" or "tundra" or "snow" or "alpine";
            bool hot = b is "desert" or "lava" or "ashen" or "volcanic";
            bool crystal = b is "crystal";
            bool ember = b is "lava" or "ashen" or "volcanic";
            bool desert = b is "desert";

            void Add(string key, int w) { int i = Index(key); for (int k = 0; k < w; k++) into.Add(i); }
            // Flyers thin out heavily while it rains; drifting gas-bags don't mind the wet.
            void AddFly(string key, int w) { if (wet) w = w >= 3 ? 1 : 0; Add(key, w); }

            if (night)
            {
                if (!cold) AddFly("moth", 2);
                if (lush) AddFly("firefly", 4); else if (!hot && !cold && !crystal) AddFly("firefly", 1);
                if (lush) { Add("sporedrifter", 3); Add("gasbag", 1); }
                if (ember) Add("embermite", 4);
                if (crystal) Add("crystalbeetle", 2);
                if (cold) Add("frostmite", 2);
                // crawlers also roam at night
                Add("beetle", cold ? 1 : 2);
                if (!cold) Add("spider", 1);
                if (lush || wet) Add("worm", wet ? 3 : 1);
                if (wet && !cold) Add("snail", 2);
            }
            else
            {
                if (!cold && !crystal) AddFly("butterfly", lush ? 4 : 2);
                if (lush) { AddFly("dragonfly", 2); AddFly("bee", 2); }
                else if (!hot && !cold && !crystal) { AddFly("bee", 1); }
                AddFly("fly", hot ? 3 : (cold ? 0 : 2));
                if (lush) { AddFly("skyray", 1); Add("gasbag", 1); }
                else if (!hot && !cold && !crystal) AddFly("skyray", 1);
                if (crystal) { AddFly("prismwing", 4); Add("crystalbeetle", 2); }
                if (ember) Add("ashhopper", 3);
                if (desert) { AddFly("sandskimmer", 3); Add("ashhopper", 2); }
                if (cold) Add("frostmite", 3);
                Add("beetle", 2);
                Add("ant", cold ? 1 : 3);
                if (lush) { Add("caterpillar", 2); Add("snail", 1); }
                if (!cold) Add("spider", 1);
                if (lush || wet) Add("worm", wet ? 3 : 1);
                if (wet && !cold) Add("snail", 2);
            }

            if (into.Count == 0) Add("beetle", 1); // never empty — a lone beetle beats nothing
        }

        /// <summary>Aquatic kinds (always available wherever a water volume is found near the player). At
        /// night luminous plankton joins the roster, so lush shorelines glitter after dark.</summary>
        public static void WaterKinds(bool night, List<int> into)
        {
            into.Clear();
            into.Add(Index("fish")); into.Add(Index("fish"));
            into.Add(Index("tadpole"));
            into.Add(Index("waterbeetle"));
            into.Add(Index("strider"));
            if (night) { Add3(into, Index("glowplankton")); }

            static void Add3(List<int> l, int i) { l.Add(i); l.Add(i); l.Add(i); }
        }

        /// <summary>Deterministic FNV-1a string hash for per-planet cosmetic seeds. Never use
        /// <see cref="string.GetHashCode"/> here — it differs between runtimes/runs.</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                for (int i = 0; i < (s?.Length ?? 0); i++) h = (h ^ s[i]) * 16777619;
                return h;
            }
        }

        private static Color C(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>
    /// Builds the single micro-fauna sprite atlas (one texture → one draw material, so a hundred critters batch
    /// cheaply). Prefers generated sprites bundled as <c>Resources/textures/microfauna_&lt;key&gt;.bytes</c> (raw
    /// 64×64 RGBA32 with alpha, decoded via <see cref="Texture2D.LoadRawTextureData(byte[])"/> like the block
    /// atlas), and falls back to a procedurally-painted little silhouette per kind so the system works even
    /// before any art is generated. Mirrors <see cref="BlockTextureAtlas"/>'s asset-or-procedural approach.
    /// </summary>
    public static class MicroFaunaAtlas
    {
        public const int Tile = 64;
        public const int Cols = 6;
        public const int Rows = 5; // 30 tile slots ≥ Kinds.Length

        private static Texture2D _tex;

        public static Texture2D Texture => _tex != null ? _tex : (_tex = Build());

        /// <summary>UV rect (in 0..1) of a kind's tile in the atlas, for baking into its billboard quad.</summary>
        public static Rect UvRect(int tile)
        {
            int cx = tile % Cols, cy = tile / Cols;
            float w = 1f / Cols, h = 1f / Rows;
            return new Rect(cx * w, cy * h, w, h);
        }

        private static Texture2D Build()
        {
            var tex = new Texture2D(Cols * Tile, Rows * Tile, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MicroFaunaAtlas",
            };

            // Transparent background everywhere first.
            var clear = new Color[tex.width * tex.height];
            tex.SetPixels(clear);

            foreach (var k in MicroFauna.Kinds)
            {
                int ox = (k.Tile % Cols) * Tile, oy = (k.Tile / Cols) * Tile;
                if (!TryBlitAsset(k.Key, tex, ox, oy))
                {
                    PaintFallback(k, tex, ox, oy);
                }
            }

            tex.Apply();
            return tex;
        }

        private static bool TryBlitAsset(string key, Texture2D dst, int ox, int oy)
        {
            var asset = Resources.Load<TextAsset>("textures/microfauna_" + key);
            if (asset == null || asset.bytes.Length != Tile * Tile * 4)
            {
                return false;
            }

            var src = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
            src.LoadRawTextureData(asset.bytes);
            src.Apply();
            dst.SetPixels(ox, oy, Tile, Tile, src.GetPixels());
            Object.Destroy(src);
            return true;
        }

        /// <summary>A simple recognisable silhouette on a transparent tile when no sprite is bundled: a body
        /// blob, plus wings for flyers, a tail for swimmers, or a soft radial glow for the glowing kinds.</summary>
        private static void PaintFallback(CritterKind k, Texture2D dst, int ox, int oy)
        {
            const float c = (Tile - 1) * 0.5f;
            Color body = Color.white; // tinted per-instance via vertex colour at render time

            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float a = 0f;

                if (k.Glow)
                {
                    // Soft round glow that fades to the rim (additive at render time).
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                }
                else if (k.Motion == CritterMotion.Fly)
                {
                    // Slim central body + two rounded wings.
                    float bodyShape = (dx * dx) / 0.05f + (dy * dy) / 0.5f;     // vertical capsule
                    float wL = ((dx + 0.42f) * (dx + 0.42f)) / 0.10f + (dy * dy) / 0.22f;
                    float wR = ((dx - 0.42f) * (dx - 0.42f)) / 0.10f + (dy * dy) / 0.22f;
                    if (bodyShape < 1f) a = 1f;
                    else if (wL < 1f || wR < 1f) a = 0.9f;
                }
                else if (k.Motion == CritterMotion.Swim)
                {
                    // Teardrop body + a small tail fin to the left.
                    float bodyShape = (dx * dx) / 0.34f + (dy * dy) / 0.16f;
                    float tail = ((dx + 0.55f) * (dx + 0.55f)) / 0.05f + (dy * dy) / 0.05f;
                    if (bodyShape < 1f || tail < 1f) a = 1f;
                }
                else if (k.Motion == CritterMotion.Drift)
                {
                    // Round balloon dome up top + a few thin dangling tendrils below.
                    float dome = (dx * dx) / 0.22f + ((dy - 0.25f) * (dy - 0.25f)) / 0.22f;
                    if (dome < 1f && dy > 0.05f) a = 1f;
                    else if (dy <= 0.1f && dy > -0.75f)
                    {
                        float t0 = Mathf.Abs(dx), t1 = Mathf.Abs(dx + 0.22f), t2 = Mathf.Abs(dx - 0.22f);
                        if (t0 < 0.035f || t1 < 0.035f || t2 < 0.035f) a = 0.85f;
                    }
                }
                else // Crawl + Hop
                {
                    // Horizontal oval body.
                    float bodyShape = (dx * dx) / 0.4f + (dy * dy) / 0.14f;
                    if (bodyShape < 1f) a = 1f;
                }

                if (a > 0.001f)
                {
                    dst.SetPixel(ox + x, oy + y, new Color(body.r, body.g, body.b, a));
                }
            }
        }
    }
}
