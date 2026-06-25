// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Game-independent procedural textures for the sun disc + god-ray fan, shared by the in-world <see cref="Sky"/>
    /// (which drives them from the live <c>WorldEnvironment</c>) and the <see cref="MenuBackground"/> attract scene
    /// (which shows the same sun/god-rays without a running game). Pure generators — no scene/Game state — so the
    /// "build the look" half is reusable while each owner keeps its own placement/fade logic.
    /// </summary>
    public static class SkyVisuals
    {
        /// <summary>A soft glowing sun disc: a tight bright core fading into a wide halo (tinted by the sun
        /// colour through the material's <c>_Color</c> at runtime).</summary>
        public static Texture2D GlowTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float core = Mathf.Clamp01(1f - d * 4f);              // tight bright disc
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f); // soft surrounding glow
                    float a = Mathf.Clamp01(core * 0.8f + halo * 0.6f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A radial god-ray texture: soft bright spokes fanning out from the centre, fading outward — the
        /// "shafts" that get occluded by foreground terrain via the depth-tested SunRays billboard.</summary>
        public static Texture2D RayTexture()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            var rng = new System.Random(20260618);
            // A few dozen spokes at random angles + widths so the fan looks natural, not a clean star.
            const int spokes = 28;
            var ang = new float[spokes];
            var wid = new float[spokes];
            var amp = new float[spokes];
            for (int s = 0; s < spokes; s++)
            {
                ang[s] = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                wid[s] = 18f + (float)rng.NextDouble() * 60f; // angular sharpness
                amp[s] = 0.35f + (float)rng.NextDouble() * 0.65f;
            }

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx);
                float ray = 0f;
                for (int s = 0; s < spokes; s++)
                {
                    float da = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ang[s] * Mathf.Rad2Deg));
                    ray += amp[s] * Mathf.Exp(-da * da / (2f * (180f / wid[s]) * (180f / wid[s])));
                }

                float radial = Mathf.Clamp01(1f - r);            // fade to the rim
                float core = Mathf.Pow(Mathf.Clamp01(1f - r * 1.6f), 2f); // a soft bright hub
                float v = Mathf.Clamp01(Mathf.Clamp01(ray) * radial * 0.8f + core * 0.25f);
                px[y * n + x] = new Color(1f, 1f, 1f, 1f) * v; // white; tinted by _Color (sun colour) at runtime
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
