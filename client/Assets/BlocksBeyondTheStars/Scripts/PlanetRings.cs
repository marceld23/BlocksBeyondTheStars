// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Planetary rings (#596): shared geometry/texture/style bits for every view that shows a ringed
    /// planet — the space-flight view and the surface sky both attach the same flat annulus (as a child
    /// of the body sphere, so it inherits position and scale) and the on-planet <see cref="RingBand"/>
    /// derives its sky arc from the same style values. Everything is a pure function of the body's
    /// RingSeed (0 = no rings), so all clients and all views agree on tilt, bands and hue.
    /// </summary>
    internal static class PlanetRings
    {
        // The parent body is a unit sphere primitive (mesh radius 0.5 before the diameter scale), so in
        // the same local space the annulus spans 1.24..2.30 planet radii — Saturn-ish proportions.
        internal const float InnerRadius = 0.62f;
        internal const float OuterRadius = 1.15f;
        private const int Segments = 96;

        private static Mesh _annulus;
        private static readonly Dictionary<int, Texture2D> _bandCache = new();

        /// <summary>Ring-plane tilt away from the orbital plane, seeded 8..32° with a random sign — the
        /// bodies themselves never rotate, so the ring alone defines the planet's perceived axial tilt.</summary>
        public static float TiltDegrees(int ringSeed)
        {
            var rng = new System.Random(ringSeed);
            float tilt = 8f + (float)rng.NextDouble() * 24f;
            return rng.NextDouble() < 0.5 ? -tilt : tilt;
        }

        /// <summary>Azimuth of the tilt axis, seeded 0..360° so two ringed planets never look cloned.</summary>
        public static float AzimuthDegrees(int ringSeed)
        {
            var rng = new System.Random(ringSeed * 31 + 7);
            return (float)rng.NextDouble() * 360f;
        }

        /// <summary>The ring's base colour: mostly pale ice-grey (real rings are water ice) pulled a
        /// little toward the given body/star hue, with a small seeded warm/cool drift per planet.</summary>
        public static Color TintFor(int ringSeed, Color bodyHue)
        {
            var rng = new System.Random(ringSeed * 17 + 3);
            var pale = new Color(0.93f, 0.90f, 0.85f);
            var c = Color.Lerp(pale, bodyHue, 0.28f);
            float drift = ((float)rng.NextDouble() - 0.5f) * 0.12f;
            return new Color(Mathf.Clamp01(c.r + drift), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b - drift));
        }

        /// <summary>Attaches the ring disc under a body sphere. The material's colour stays caller-owned
        /// (the surface sky re-tints it every frame for the daytime sky wash); the initial colour is the
        /// seeded tint at the given alpha.</summary>
        public static GameObject Attach(Transform bodySphere, int ringSeed, Color hue, float alpha, int renderQueue, out Material mat)
        {
            var go = new GameObject("PlanetRing");
            go.transform.SetParent(bodySphere, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation =
                Quaternion.AngleAxis(AzimuthDegrees(ringSeed), Vector3.up)
                * Quaternion.AngleAxis(TiltDegrees(ringSeed), Vector3.right);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = AnnulusMesh();

            var shader = Shader.Find("BlocksBeyondTheStars/PlanetRing") ?? Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            mat = new Material(shader) { mainTexture = BandTexture(ringSeed), renderQueue = renderQueue };
            var tint = TintFor(ringSeed, hue);
            tint.a = alpha;
            mat.color = ShaderColor.Srgb(tint);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>The shared flat unit annulus in the XZ plane; u runs radially across the ring (the
        /// band texture's axis), v around it. Single-sided geometry — the shader culls off.</summary>
        public static Mesh AnnulusMesh()
        {
            if (_annulus != null)
            {
                return _annulus;
            }

            var verts = new Vector3[(Segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[Segments * 6];
            for (int s = 0; s <= Segments; s++)
            {
                float a = s / (float)Segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                verts[s * 2] = new Vector3(cos * InnerRadius, 0f, sin * InnerRadius);
                verts[s * 2 + 1] = new Vector3(cos * OuterRadius, 0f, sin * OuterRadius);
                uvs[s * 2] = new Vector2(0f, s / (float)Segments);
                uvs[s * 2 + 1] = new Vector2(1f, s / (float)Segments);
                if (s < Segments)
                {
                    int t = s * 6, i = s * 2;
                    tris[t] = i; tris[t + 1] = i + 2; tris[t + 2] = i + 1;
                    tris[t + 3] = i + 1; tris[t + 4] = i + 2; tris[t + 5] = i + 3;
                }
            }

            _annulus = new Mesh { name = "PlanetRingAnnulus", vertices = verts, uv = uvs, triangles = tris };
            _annulus.RecalculateBounds();
            return _annulus;
        }

        /// <summary>The seeded concentric band pattern: 3..6 bright bands separated by Cassini-style
        /// gaps, soft edge fades, slight per-sample brightness grain. u = radial position; cached per
        /// seed (the same texture serves every view of that planet).</summary>
        public static Texture2D BandTexture(int ringSeed)
        {
            if (_bandCache.TryGetValue(ringSeed, out var cached) && cached != null)
            {
                return cached;
            }

            const int n = 256;
            var rng = new System.Random(ringSeed);
            int gaps = 2 + rng.Next(4); // 3..6 bands
            var gapCenter = new float[gaps];
            var gapWidth = new float[gaps];
            for (int g = 0; g < gaps; g++)
            {
                gapCenter[g] = 0.14f + (float)rng.NextDouble() * 0.72f;
                gapWidth[g] = 0.018f + (float)rng.NextDouble() * 0.06f;
            }

            float phase = (float)rng.NextDouble() * 20f;
            var px = new Color[n * 2];
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1);
                // Base density with a gentle large-scale ripple, then carve the gaps and fade both edges.
                float a = 0.78f + 0.14f * Mathf.Sin(u * 9f + phase) * Mathf.Sin(u * 23f + phase * 1.7f);
                for (int g = 0; g < gaps; g++)
                {
                    float d = Mathf.Abs(u - gapCenter[g]);
                    a *= Mathf.SmoothStep(0.12f, 1f, Mathf.Clamp01(d / gapWidth[g]));
                }

                a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.07f));
                a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.09f));
                float v = 0.9f + 0.1f * Mathf.Sin(u * 47f + phase * 2.3f);
                var c = new Color(v, v, v, Mathf.Clamp01(a));
                px[x] = c;
                px[n + x] = c;
            }

            var tex = new Texture2D(n, 2, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(px);
            tex.Apply(true);
            _bandCache[ringSeed] = tex;
            return tex;
        }
    }
}
