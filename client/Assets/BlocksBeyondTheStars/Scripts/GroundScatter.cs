// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Ground-detail scatter (T0 "organic look"): draws small grass tufts strewn across natural open ground,
    /// GPU-instanced so a whole chunk's decoration is one draw call. Render-only (no colliders, not part of the chunk
    /// mesh) — the points are collected deterministically by <see cref="ChunkMesher"/> (where: <see cref="GroundScatterRules"/>)
    /// and handed to <see cref="Setup"/>. Distance-culled per chunk and quality-gated (Medium+), so it costs nothing on
    /// low-end / at range. #3120: each tuft carries the skylight and placed block light of its cell, so it shades like
    /// the ground under it (no black tufts beside a torch at night), and is a handful of thin blades instead of two
    /// solid quads; the pebbles (flat boxes that read as "sprouts") are gone.
    /// </summary>
    public sealed class GroundScatter : MonoBehaviour
    {
        /// <summary>Master toggle — the "Ground details" graphics setting (#3121, <see cref="ClientSettings.GroundDetails"/>)
        /// and the perf probe's <c>scatter</c> knob; off draws nothing.</summary>
        public static bool Enabled = true;

        /// <summary>Only draw scatter within this range of the camera (per chunk); tiny detail is pointless far off.</summary>
        public static float MaxDistance = 28f;

        // Graphics.DrawMeshInstanced hard cap per call. A chunk never reaches it: tufts grow only where the cell above sees
        // the sky straight up (#3119), so at most one per column — 256 per chunk, a seventh of them in practice.
        private const int BatchSize = 1023;

        private static readonly int ScatterLightId = Shader.PropertyToID("_ScatterLight");

        private static Mesh _tuftMesh;
        private static Material _material;

        // Cache Camera.main to one lookup per frame across every chunk's scatter (Camera.main is a tagged search).
        private static Camera _cam;
        private static int _camFrame = -1;

        // #1551: Setup ran per chunk mesh with a System.Random per point (~250 B each, ~2000 allocs/s while
        // walking) — the variation now comes from a hash mix, and the build lists are shared scratch.
        private static readonly List<Matrix4x4> _tuftScratch = new List<Matrix4x4>();
        private static readonly List<Vector4> _lightScratch = new List<Vector4>();

        private Matrix4x4[] _tufts;
        private MaterialPropertyBlock _props;
        private int _propsLength;
        private Vector3 _worldCenter;
        private Vector3 _bakedPos; // the chunk position the world matrices were baked at
        private bool _ready;

        /// <summary>Rebuilds the instance matrices from a chunk's scatter points (local chunk space; xyz = cell-top
        /// position, w = type) and their light (<paramref name="light"/>, same index: rgb = placed block light, a = skylight;
        /// missing entries read as open sky without block light). Each point gets a deterministic yaw + scale + jitter from
        /// its position, so the field looks natural yet is stable across remeshes. Empty input disables the component.</summary>
        public void Setup(List<Vector4> points, List<Vector4> light = null)
        {
            EnsureResources();
            if (points == null || points.Count == 0 || _material == null)
            {
                _tufts = null;
                _ready = false;
                enabled = false;
                return;
            }

            var tufts = _tuftScratch;
            var lights = _lightScratch;
            Collect(points, light, transform.localToWorldMatrix, tufts, lights);
            if (tufts.Count == 0)
            {
                _tufts = null;
                _ready = false;
                enabled = false;
                return;
            }

            _tufts = tufts.ToArray();

            // A property block fixes an array's length on its first upload, so a chunk that grew past it gets a fresh block.
            if (_props == null || lights.Count > _propsLength)
            {
                _props = new MaterialPropertyBlock();
                _propsLength = lights.Count;
            }

            _props.SetVectorArray(ScatterLightId, lights);
            tufts.Clear();
            lights.Clear();
            _worldCenter = transform.TransformPoint(new Vector3(
                WorldConstants.ChunkSize * 0.5f, WorldConstants.ChunkSize * 0.5f, WorldConstants.ChunkSize * 0.5f));
            _bakedPos = transform.position;
            _ready = true;
            enabled = true;
        }

        /// <summary>The world matrix and light of every tuft point (capped at one instanced draw).</summary>
        private static void Collect(List<Vector4> points, List<Vector4> light, Matrix4x4 l2w, List<Matrix4x4> tufts,
            List<Vector4> lights)
        {
            tufts.Clear();
            lights.Clear();
            for (int i = 0; i < points.Count && tufts.Count < BatchSize; i++)
            {
                var p = points[i];
                if (p.w >= 0.5f)
                {
                    continue; // not a tuft (the retired pebble type)
                }

                // Deterministic per-point variation from the local cell position: four hash draws.
                uint h = unchecked((uint)((int)p.x * 73856093 ^ (int)p.z * 19349663 ^ Mathf.RoundToInt(p.y) * 83492791));
                float yaw = Draw(ref h) * 360f;
                float scale = 0.7f + Draw(ref h) * 0.6f;
                float jx = (Draw(ref h) - 0.5f) * 0.5f;
                float jz = (Draw(ref h) - 0.5f) * 0.5f;
                var local = Matrix4x4.TRS(
                    new Vector3(p.x + jx, p.y, p.z + jz),
                    Quaternion.Euler(0f, yaw, 0f),
                    Vector3.one * scale);
                tufts.Add(l2w * local);
                lights.Add(light != null && i < light.Count ? light[i] : new Vector4(0f, 0f, 0f, 1f));
            }
        }

        /// <summary>One uniform [0,1) draw from a 32-bit state (xorshift step + a multiplicative mix).</summary>
        private static float Draw(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            uint mixed = unchecked(state * 2654435761u);
            return (mixed >> 8) * (1f / 16777216f);
        }

        private void Update()
        {
            if (!_ready || !Enabled || _material == null || _tufts == null)
            {
                return;
            }

            // Quality gate: skip on the Low preset (matches the other cost-bearing look effects).
            if (UrpScenePost.Instance != null && UrpScenePost.Instance.Preset < QualityPreset.Medium)
            {
                return;
            }

            if (_camFrame != Time.frameCount)
            {
                _camFrame = Time.frameCount;
                _cam = Camera.main;
            }

            if (_cam == null)
            {
                return;
            }

            // The chunk moved across the world's seam (RepositionChunks moves it without a remesh): the baked world
            // matrices follow it, or the tufts would stay behind on the far side of the world.
            var pos = transform.position;
            if (pos != _bakedPos)
            {
                Shift(_tufts, pos - _bakedPos);
                _worldCenter += pos - _bakedPos;
                _bakedPos = pos;
            }

            // Per-chunk distance cull: a chunk half-diagonal (~14) beyond MaxDistance is safely out of view.
            float cull = MaxDistance + WorldConstants.ChunkSize;
            if ((_cam.transform.position - _worldCenter).sqrMagnitude > cull * cull)
            {
                return;
            }

            // #3063: tufts receive the sun's shadow but cast none (ScatterLit has no ShadowCaster pass), so they stay out of
            // the shadow pass's culling altogether.
            Graphics.DrawMeshInstanced(_tuftMesh, 0, _material, _tufts, _tufts.Length, _props, ShadowCastingMode.Off, true);
        }

        private static void Shift(Matrix4x4[] matrices, Vector3 delta)
        {
            if (matrices == null)
            {
                return;
            }

            for (int i = 0; i < matrices.Length; i++)
            {
                matrices[i].m03 += delta.x;
                matrices[i].m13 += delta.y;
                matrices[i].m23 += delta.z;
            }
        }

        private static void EnsureResources()
        {
            if (_material == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/ScatterLit");
                if (shader != null)
                {
                    _material = new Material(shader) { enableInstancing = true };
                }
            }

            // Vertex alpha 1: the tuft takes the world's grass tint (#2835); the blades darken toward the root.
            _tuftMesh ??= BuildTuft(new Color(0.22f, 0.42f, 0.17f), new Color(0.36f, 0.62f, 0.28f));
        }

        // The tuft's blades: (height, outward lean) each, fanned evenly around the root with a turn of their own.
        private static readonly Vector2[] Blades =
        {
            new Vector2(0.34f, 0.07f), new Vector2(0.24f, 0.10f), new Vector2(0.30f, 0.05f),
            new Vector2(0.20f, 0.11f), new Vector2(0.28f, 0.08f),
        };

        /// <summary>#3120: a tuft of thin tapered blades (one triangle each) fanning out from a common root, base at y = 0,
        /// lit flat (normals up) so it reads like the ground it sits on. Cull Off in the shader shows both faces.</summary>
        private static Mesh BuildTuft(Color root, Color tip)
        {
            const float halfWidth = 0.022f, rootRadius = 0.025f;
            int n = Blades.Length;
            var verts = new Vector3[n * 3];
            var colors = new Color[n * 3];
            var normals = new Vector3[n * 3];
            var tris = new int[n * 3];
            for (int b = 0; b < n; b++)
            {
                float a = (b * 360f / n + 17f * b) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = new Vector3(-outward.z, 0f, outward.x) * halfWidth;
                var basePos = outward * rootRadius;
                int v = b * 3;
                verts[v] = basePos - side;
                verts[v + 1] = basePos + side;
                verts[v + 2] = basePos + outward * Blades[b].y + Vector3.up * Blades[b].x;
                colors[v] = root;
                colors[v + 1] = root;
                colors[v + 2] = tip;
                tris[v] = v;
                tris[v + 1] = v + 2;
                tris[v + 2] = v + 1;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.up;
            }

            var mesh = new Mesh { name = "ScatterTuft" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
