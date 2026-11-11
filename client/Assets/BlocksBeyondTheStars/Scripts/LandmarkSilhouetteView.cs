// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3497: the far silhouette of the world's landmarks. Beyond the streamed chunks a build is otherwise only a
    /// height blob per 8- or 32-block cell, so a tower, a gate and a bridge all look like a bump. The server sends a
    /// coarse model of each landmark (<see cref="GameBootstrap.Landmarks"/>); this view meshes it once
    /// (<see cref="LandmarkSilhouetteMesh"/>), colours it from the block atlas like the far terrain colours its patches,
    /// and draws it with the far terrain's shader — lit and hazed like the horizon it stands on.
    /// <para>Near the player the real blocks take over: the shader cuts the silhouette away in a sphere
    /// (<c>_Sc_LandmarkClip</c>, <see cref="LandmarkSilhouetteMesh.NearCut"/>) one chunk inside the nearer of the chunk
    /// draw distance and the sphere the server lifts a landmark's columns to. What stands above that sphere — the top of
    /// a tall tower in the browser's short view — stays drawn. The global is set every frame (a material switch set
    /// once does not reach WebGPU).</para>
    /// </summary>
    public sealed class LandmarkSilhouetteView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>How far away a silhouette is still drawn when the far view is switched off (blocks).</summary>
        private const float RangeWithoutFarView = 512f;

        private static readonly int ClipId = Shader.PropertyToID("_Sc_LandmarkClip");

        private sealed class Entry
        {
            public NetLandmark Model;
            public GameObject Go;
            public Mesh Mesh;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Material _material;
        private int _version = -1;
        private bool _missingShaderLogged;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (_version != Game.LandmarksVersion)
            {
                _version = Game.LandmarksVersion;
                Rebuild();
            }

            if (_entries.Count == 0)
            {
                return;
            }

            bool show = !Game.SpaceViewActive && !Game.InSpace && string.IsNullOrEmpty(Game.StationName);
            var p = Game.PlayerPosition;
            float cut = LandmarkSilhouetteMesh.NearCut(Game.ChunkDrawDistanceNow, Game.StreamViewDistanceChunks, Shared.World.WorldConstants.ChunkSize);
            Shader.SetGlobalVector(ClipId, new Vector4(p.x, p.y, p.z, cut));
            float range = Mathf.Max(RangeWithoutFarView, Game.FarViewBlocks);
            foreach (var e in _entries)
            {
                // The copy nearest the player across the world's seam, like every other world object.
                var corner = Game.ScenePos(e.Model.X, e.Model.Y, e.Model.Z);
                e.Go.transform.position = corner;
                float dx = corner.x + (e.Model.Width * e.Model.Step * 0.5f) - p.x, dz = corner.z + (e.Model.Length * e.Model.Step * 0.5f) - p.z;
                float reach = range + (Mathf.Max(e.Model.Width, e.Model.Length) * e.Model.Step);
                bool visible = show && (dx * dx) + (dz * dz) <= reach * reach;
                if (e.Go.activeSelf != visible)
                {
                    e.Go.SetActive(visible);
                }
            }
        }

        private void Rebuild()
        {
            Clear();
            var models = Game.Landmarks;
            if (models == null || models.Length == 0 || !EnsureMaterial())
            {
                return;
            }

            foreach (var model in models)
            {
                var geo = LandmarkSilhouetteMesh.Build(model);
                if (geo.VertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject("LandmarkSilhouette " + model.Key);
                go.transform.SetParent(transform, false);
                var mesh = ToMesh(geo, model);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _entries.Add(new Entry { Model = model, Go = go, Mesh = mesh });
            }
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/FarTerrain");
            if (shader == null)
            {
                if (!_missingShaderLogged)
                {
                    _missingShaderLogged = true;
                    Debug.LogWarning("[Landmark] Shader BlocksBeyondTheStars/FarTerrain missing — no far silhouettes.");
                }

                return false;
            }

            _material = new Material(shader) { name = "LandmarkSilhouette", enableInstancing = false };
            return true;
        }

        /// <summary>The Unity mesh: the far terrain's vertex layout — colour = the block's average atlas colour times its
        /// dye, alpha 0 (land), TEXCOORD0.x = −1 (the shader's "landmark" mark: cut by the sphere, never by the column mask).</summary>
        private Mesh ToMesh(LandmarkSilhouetteMesh geo, NetLandmark model)
        {
            int n = geo.VertexCount;
            var positions = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color32[n];
            var uv = new Vector2[n];
            var palette = PaletteColors(model);
            for (int i = 0; i < n; i++)
            {
                positions[i] = new Vector3(geo.Positions[i * 3], geo.Positions[(i * 3) + 1], geo.Positions[(i * 3) + 2]);
                normals[i] = new Vector3(geo.Normals[i * 3], geo.Normals[(i * 3) + 1], geo.Normals[(i * 3) + 2]);
                int index = geo.Palette[i] - 1;
                colors[i] = index >= 0 && index < palette.Length ? palette[index] : new Color32(115, 112, 107, 0);
                uv[i] = new Vector2(-1f, 0f);
            }

            var mesh = new Mesh { name = "LandmarkSilhouette " + model.Key, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(geo.Triangles, 0, calculateBounds: false);
            var size = new Vector3(model.Width, model.Height, model.Length) * model.Step;
            mesh.bounds = new Bounds(size * 0.5f, size);
            return mesh;
        }

        private Color32[] PaletteColors(NetLandmark model)
        {
            var blocks = model.Blocks ?? System.Array.Empty<ushort>();
            var tints = model.Tints ?? System.Array.Empty<int>();
            var colors = new Color32[blocks.Length];
            for (int i = 0; i < blocks.Length; i++)
            {
                Color c = Game.Atlas != null && blocks[i] != 0 ? Game.Atlas.AverageColor(blocks[i]) : new Color(0.45f, 0.44f, 0.42f);
                int tint = i < tints.Length ? tints[i] : 0;
                if (tint != 0)
                {
                    // A dyed block shows its dye: the atlas colour's brightness, the dye's hue.
                    float lum = (c.r * 0.299f) + (c.g * 0.587f) + (c.b * 0.114f);
                    var dye = new Color(((tint >> 16) & 0xFF) / 255f, ((tint >> 8) & 0xFF) / 255f, (tint & 0xFF) / 255f);
                    c = Color.Lerp(c, dye * Mathf.Lerp(0.6f, 1.4f, lum), 0.85f);
                }

                colors[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 0);
            }

            return colors;
        }

        private void Clear()
        {
            foreach (var e in _entries)
            {
                if (e.Go != null)
                {
                    Destroy(e.Go);
                }

                if (e.Mesh != null)
                {
                    Destroy(e.Mesh);
                }
            }

            _entries.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
