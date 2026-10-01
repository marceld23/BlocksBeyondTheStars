// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Space dust (#2157) — the speed cue of flight. Before this the starfield and the nebula were locked at infinity
    /// and nothing nearby moved past the ship, so cruising and drifting looked the same. One mesh of quads around the
    /// camera, wrapped and stretched along the ship's velocity entirely in the vertex shader (<c>FxSpaceDust</c>): idle
    /// → faint motes, cruising → short streaks, a hyperjump (<see cref="WarpBoost"/>) → long star lines. One draw call,
    /// no per-frame CPU work beyond two uniforms. The flight view owns it (<see cref="Show"/>/<see cref="Hide"/>).
    /// </summary>
    public sealed class FxSpaceDust : MonoBehaviour
    {
        private static FxSpaceDust _instance;

        /// <summary>The ship's velocity in world units/s (set every frame by the flight view).</summary>
        public static Vector3 Velocity;

        /// <summary>0..1 hyperjump boost (set by <see cref="HyperspaceWarp"/>): streaks grow into long star lines.</summary>
        public static float WarpBoost;

        private MeshRenderer _renderer;
        private Material _material;
        private Transform _follow;
        private static readonly int VelId = Shader.PropertyToID("_DustVel");
        private static readonly int ParamsId = Shader.PropertyToID("_DustParams");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Shows the dust around <paramref name="camera"/> (creates it on first use).</summary>
        public static void Show(Transform camera)
        {
            if (_instance == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/FxSpaceDust");
                if (shader == null)
                {
                    return;
                }

                var go = new GameObject("FxSpaceDust");
                _instance = go.AddComponent<FxSpaceDust>();
                _instance._material = new Material(shader) { name = "FxSpaceDust" };
                go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(Mathf.RoundToInt(900 * Mathf.Max(0.3f, FxKit.Density)));
                _instance._renderer = go.AddComponent<MeshRenderer>();
                _instance._renderer.sharedMaterial = _instance._material;
                _instance._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _instance._renderer.receiveShadows = false;
            }

            _instance._follow = camera;
            _instance.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                _instance.gameObject.SetActive(false);
            }

            Velocity = Vector3.zero;
            WarpBoost = 0f;
        }

        private void LateUpdate()
        {
            if (_follow == null)
            {
                gameObject.SetActive(false);
                return;
            }

            // The mesh's bounds are huge and centred on it, so keeping it on the camera keeps it from being culled.
            transform.position = _follow.position;
            float boost = Mathf.Clamp01(WarpBoost);
            var vel = Velocity;
            if (boost > 0f)
            {
                var fwd = _follow.forward;
                vel = Vector3.Lerp(vel, fwd * 260f, boost);
            }

            float streak = Mathf.Lerp(0.05f, 0.22f, boost);
            _material.SetVector(VelId, new Vector4(vel.x, vel.y, vel.z, streak));
            _material.SetVector(ParamsId, new Vector4(70f, 0.07f, 0.22f, 45f));
            float strength = FxKit.ReducedEffects ? 0.55f : 1f;
            _material.SetColor(ColorId, FxKit.Lin(new Color(0.75f, 0.85f, 1f)) * new Color(1f, 1f, 1f, strength * (1f + boost)));
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary><paramref name="count"/> quads, each with one random point in the unit cube (POSITION, shared by its
        /// four corners) and its corner in UV (-1..1); the shader places and stretches them.</summary>
        private static Mesh BuildMesh(int count)
        {
            var rng = new System.Random(1337);
            var verts = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var cols = new Color[count * 4];
            var tris = new int[count * 6];
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                float bright = 0.4f + 0.6f * (float)rng.NextDouble();
                for (int k = 0; k < 4; k++)
                {
                    verts[i * 4 + k] = p;
                    uvs[i * 4 + k] = corners[k];
                    cols[i * 4 + k] = new Color(1f, 1f, 1f, bright);
                }

                tris[i * 6] = i * 4;
                tris[i * 6 + 1] = i * 4 + 1;
                tris[i * 6 + 2] = i * 4 + 2;
                tris[i * 6 + 3] = i * 4;
                tris[i * 6 + 4] = i * 4 + 2;
                tris[i * 6 + 5] = i * 4 + 3;
            }

            var mesh = new Mesh { name = "FxSpaceDust" }; // ≤ 1 000 quads → 16-bit indices are plenty
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }
    }
}
