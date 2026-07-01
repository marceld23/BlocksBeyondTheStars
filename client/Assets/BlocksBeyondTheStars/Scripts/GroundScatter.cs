// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Ground-detail scatter (T0 "organic look"): draws tiny grass tufts + pebbles strewn across open ground,
    /// GPU-instanced so a whole chunk's decoration is a couple of draw calls. Render-only (no colliders, not part
    /// of the chunk mesh) — the points are collected deterministically by <see cref="ChunkMesher"/> and handed to
    /// <see cref="Setup"/>. Distance-culled per chunk and quality-gated (Medium+), so it costs nothing on low-end
    /// / at range. Deliberately conservative density + neutral colours; the look is meant to be tuned once eyeballed.
    /// </summary>
    public sealed class GroundScatter : MonoBehaviour
    {
        /// <summary>Master toggle (e.g. an accessibility / perf setting) — off draws nothing.</summary>
        public static bool Enabled = true;

        /// <summary>Only draw scatter within this range of the camera (per chunk); tiny detail is pointless far off.</summary>
        public static float MaxDistance = 28f;

        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard cap per call

        private static Mesh _tuftMesh;
        private static Mesh _pebbleMesh;
        private static Material _material;

        // Cache Camera.main to one lookup per frame across every chunk's scatter (Camera.main is a tagged search).
        private static Camera _cam;
        private static int _camFrame = -1;

        private Matrix4x4[] _tufts;
        private Matrix4x4[] _pebbles;
        private Vector3 _worldCenter;
        private bool _ready;

        /// <summary>Rebuilds the instance matrices from a chunk's scatter points (local chunk space; xyz = cell-top
        /// position, w = type 0 tuft / 1 pebble). Each point gets a deterministic yaw + scale + jitter from its
        /// position, so the field looks natural yet is stable across remeshes. Empty input disables the component.</summary>
        public void Setup(List<Vector4> points)
        {
            EnsureResources();
            if (points == null || points.Count == 0 || _material == null)
            {
                _tufts = null;
                _pebbles = null;
                _ready = false;
                enabled = false;
                return;
            }

            var tufts = new List<Matrix4x4>();
            var pebbles = new List<Matrix4x4>();
            var l2w = transform.localToWorldMatrix;
            foreach (var p in points)
            {
                // Deterministic per-point variation from the local cell position.
                int h = unchecked((int)p.x * 73856093 ^ (int)p.z * 19349663 ^ Mathf.RoundToInt(p.y) * 83492791);
                var rng = new System.Random(h);
                float yaw = (float)rng.NextDouble() * 360f;
                float scale = 0.7f + (float)rng.NextDouble() * 0.6f;
                float jx = ((float)rng.NextDouble() - 0.5f) * 0.5f;
                float jz = ((float)rng.NextDouble() - 0.5f) * 0.5f;
                var local = Matrix4x4.TRS(
                    new Vector3(p.x + jx, p.y, p.z + jz),
                    Quaternion.Euler(0f, yaw, 0f),
                    Vector3.one * scale);
                var world = l2w * local;
                if (p.w < 0.5f)
                {
                    tufts.Add(world);
                }
                else
                {
                    pebbles.Add(world);
                }
            }

            _tufts = tufts.ToArray();
            _pebbles = pebbles.ToArray();
            _worldCenter = transform.TransformPoint(new Vector3(
                WorldConstants.ChunkSize * 0.5f, WorldConstants.ChunkSize * 0.5f, WorldConstants.ChunkSize * 0.5f));
            _ready = true;
            enabled = true;
        }

        private void Update()
        {
            if (!_ready || !Enabled || _material == null)
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

            // Per-chunk distance cull: a chunk half-diagonal (~14) beyond MaxDistance is safely out of view.
            float cull = MaxDistance + WorldConstants.ChunkSize;
            if ((_cam.transform.position - _worldCenter).sqrMagnitude > cull * cull)
            {
                return;
            }

            DrawBatched(_tuftMesh, _tufts);
            DrawBatched(_pebbleMesh, _pebbles);
        }

        private void DrawBatched(Mesh mesh, Matrix4x4[] matrices)
        {
            if (mesh == null || matrices == null || matrices.Length == 0)
            {
                return;
            }

            if (matrices.Length <= BatchSize)
            {
                Graphics.DrawMeshInstanced(mesh, 0, _material, matrices, matrices.Length);
                return;
            }

            for (int i = 0; i < matrices.Length; i += BatchSize)
            {
                int count = Mathf.Min(BatchSize, matrices.Length - i);
                Graphics.DrawMeshInstanced(mesh, 0, _material, Slice(matrices, i, count), count);
            }
        }

        // Fast path: a single batch draws the whole array directly (no copy). Only chunks with >1023 scatter
        // points (never, at this density) would ever allocate a slice.
        private static Matrix4x4[] Slice(Matrix4x4[] src, int start, int count)
        {
            if (start == 0 && count == src.Length)
            {
                return src;
            }

            var dst = new Matrix4x4[count];
            System.Array.Copy(src, start, dst, 0, count);
            return dst;
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

            _tuftMesh ??= BuildTuft(new Color(0.28f, 0.52f, 0.22f));
            _pebbleMesh ??= BuildPebble(new Color(0.42f, 0.42f, 0.45f));
        }

        /// <summary>Two small crossed vertical quads (grass tuft), base at y=0, lit flat (normals up) so it reads
        /// like the ground it sits on. Cull Off in the shader shows both faces.</summary>
        private static Mesh BuildTuft(Color color)
        {
            const float w = 0.16f, h = 0.34f;
            var verts = new[]
            {
                new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f),
                new Vector3(0f, 0f, -w), new Vector3(0f, 0f, w), new Vector3(0f, h, w), new Vector3(0f, h, -w),
            };
            var tris = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            return BuildMesh(verts, tris, Vector3.up, color);
        }

        /// <summary>A small flattened box (pebble), base at y=0, with per-face normals for proper shading.</summary>
        private static Mesh BuildPebble(Color color)
        {
            const float r = 0.14f, hgt = 0.11f;
            // 6 faces, 4 verts each (per-face normals) → 24 verts, 12 tris.
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int bi = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(nrm); n.Add(nrm); n.Add(nrm); n.Add(nrm);
                t.Add(bi); t.Add(bi + 1); t.Add(bi + 2); t.Add(bi); t.Add(bi + 2); t.Add(bi + 3);
            }

            Vector3 p000 = new(-r, 0f, -r), p100 = new(r, 0f, -r), p101 = new(r, 0f, r), p001 = new(-r, 0f, r);
            Vector3 t000 = new(-r, hgt, -r), t100 = new(r, hgt, -r), t101 = new(r, hgt, r), t001 = new(-r, hgt, r);
            Face(t000, t001, t101, t100, Vector3.up);
            Face(p000, p100, p101, p001, Vector3.down);
            Face(p001, p101, t101, t001, Vector3.forward);
            Face(p100, p000, t000, t100, Vector3.back);
            Face(p101, p100, t100, t101, Vector3.right);
            Face(p000, p001, t001, t000, Vector3.left);
            var mesh = new Mesh { name = "ScatterPebble" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            var colors = new Color[v.Count];
            for (int i = 0; i < colors.Length; i++) { colors[i] = color; }
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildMesh(Vector3[] verts, int[] tris, Vector3 normal, Color color)
        {
            var mesh = new Mesh { name = "ScatterTuft" };
            mesh.SetVertices(verts);
            var normals = new Vector3[verts.Length];
            var colors = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++) { normals[i] = normal; colors[i] = color; }
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
