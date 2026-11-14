// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3615: the far skin of a big station hull (a giant wreck): <see cref="HullSilhouette"/> builds a coarse model of the
    /// design (one cell per 4³ blocks, only the outside skin) — on a worker thread as the design arrives; in the browser
    /// (no threads there) inline, and only the first time the hull is seen from afar, so arriving next to one never stalls a
    /// frame — and this draws it in the atlas colours of its blocks, the faces shaded like the chunk mesher shades them.
    /// Beyond <see cref="LodFarLeave"/> it stands in for the chunks (whose meshes are freed); inside <see cref="LodNearEnter"/>
    /// the chunks build, nearest first, and the skin covers the hull only while it is built from nothing
    /// (<c>VoxStruct.Building</c>) — never over chunks already on screen (a mined block, a re-sent design).
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Whether a design is a hull big enough for the far skin: a giant wreck, or a station this many blocks
        /// across. Asteroids, debris and the small stations keep their chunks at every distance.</summary>
        private static bool IsBigHull(VoxStruct vs)
            => vs.IsStation && (GiantShipRules.IsGiantWreckId(vs.Id) || 2f * Mathf.Max(vs.Half.x, Mathf.Max(vs.Half.y, vs.Half.z)) >= LodMinExtent);

        /// <summary>Starts building the far skin of a big hull from the design's own arrays (never changed after arrival, so
        /// a worker may read them while the frame goes on).</summary>
        private static void StartHullLod(VoxStruct vs, BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign m)
        {
            if (!vs.BigHull)
            {
                return;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                vs.LodSource = m; // no worker threads in the browser: built when first seen from afar (UpdateHullLod)
                return;
            }

            int[] xs = m.X, ys = m.Y, zs = m.Z, tints = m.Tint;
            ushort[] blocks = m.Block;
            vs.LodTask = System.Threading.Tasks.Task.Run(() => HullSilhouette.Build(xs, ys, zs, blocks, tints));
        }

        /// <summary>The browser's skin: built inline from the kept design, once the hull is far (never on arrival).</summary>
        private static void BuildPendingHullLod(VoxStruct vs)
        {
            if (vs.LodSource == null || !vs.Far)
            {
                return;
            }

            var m = vs.LodSource;
            vs.LodSource = null;
            vs.LodTask = System.Threading.Tasks.Task.FromResult(HullSilhouette.Build(m.X, m.Y, m.Z, m.Block, m.Tint));
        }

        /// <summary>Per frame for a big hull in range: switches between skin and chunks by the distance to the hull's box,
        /// builds the browser's skin once the hull is far, takes a finished skin, and shows it while far — and while the hull's
        /// chunks are built from nothing.</summary>
        private void UpdateHullLod(VoxStruct vs, float boxDistance)
        {
            bool far = vs.Far ? boxDistance > LodNearEnter : boxDistance > LodFarLeave;
            if (far != vs.Far)
            {
                vs.Far = far;
                if (far)
                {
                    DropStructChunks(vs); // the skin stands in: free the chunk meshes
                }
                else
                {
                    vs.MeshDirty = true;
                    vs.Building = true; // no chunk is on screen yet: the skin covers the hull until they all are
                }
            }

            BuildPendingHullLod(vs); // the browser's: only now that the distance is known
            TakeFinishedHullLod(vs);

            // While the hull is built from nothing, the skin covers it — but not right at it: there the nearest chunks (built
            // first) must not hide behind a coarse wall around the camera. A single edit or a re-sent design keeps the old
            // chunks on screen meanwhile, so the skin never pops over them.
            bool show = vs.Far || (vs.Building && boxDistance > LodHideNear);
            if (show && vs.Lod == null && vs.LodData != null && vs.LodData.Mesh.VertexCount > 0)
            {
                CreateHullLodObject(vs);
            }

            if (vs.Lod != null && vs.Lod.activeSelf != show)
            {
                vs.Lod.SetActive(show);
            }
        }

        /// <summary>Takes a finished skin (dropping the old one's object and mesh) or logs why it failed.</summary>
        private void TakeFinishedHullLod(VoxStruct vs)
        {
            if (vs.LodTask == null || !vs.LodTask.IsCompleted)
            {
                return;
            }

            if (vs.LodTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                DropHullLod(vs);
                vs.LodData = vs.LodTask.Result;
            }
            else
            {
                Debug.LogWarning($"[SpaceView] The far skin of {vs.Id} failed: {vs.LodTask.Exception?.GetBaseException().Message}");
            }

            vs.LodTask = null;
        }

        private void CreateHullLodObject(VoxStruct vs)
        {
            vs.LodMesh = HullLodMesh(vs.LodData, vs.Id);
            vs.Lod = new GameObject("HullSkin");
            vs.Lod.transform.SetParent(vs.Root.transform, false);
            vs.Lod.transform.localPosition = new Vector3(vs.LodData.MinX, vs.LodData.MinY, vs.LodData.MinZ) - vs.Centre;
            vs.Lod.AddComponent<MeshFilter>().sharedMesh = vs.LodMesh;
            var renderer = vs.Lod.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = HullLodMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Frees the skin's object and mesh (the skin's data stays for the next time it is needed).</summary>
        private void DropHullLod(VoxStruct vs)
        {
            if (vs.Lod != null)
            {
                Destroy(vs.Lod);
            }

            if (vs.LodMesh != null)
            {
                Destroy(vs.LodMesh);
            }

            vs.Lod = null;
            vs.LodMesh = null;
        }

        private Material HullLodMaterial()
        {
            if (_hullLodMat == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/VertexColorOpaque") ?? Shader.Find("Unlit/Color");
                _hullLodMat = new Material(shader) { name = "HullSkin", enableInstancing = false };
            }

            return _hullLodMat;
        }

        /// <summary>The skin as a Unity mesh: every vertex coloured with its cell's block (the atlas's average colour, a dye
        /// mixed in like the landmark silhouettes do) and its face's shade (the vertex-colour shader does not shade by the
        /// normal).</summary>
        private Mesh HullLodMesh(HullSilhouette lod, string id)
        {
            var geo = lod.Mesh;
            int n = geo.VertexCount;
            var positions = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color32[n];
            var palette = HullLodPalette(lod.Model);
            for (int i = 0; i < n; i++)
            {
                positions[i] = new Vector3(geo.Positions[i * 3], geo.Positions[(i * 3) + 1], geo.Positions[(i * 3) + 2]);
                var normal = new Vector3(geo.Normals[i * 3], geo.Normals[(i * 3) + 1], geo.Normals[(i * 3) + 2]);
                normals[i] = normal;
                int index = geo.Palette[i] - 1;
                Color c = index >= 0 && index < palette.Length ? palette[index] : new Color(0.45f, 0.44f, 0.42f);
                float shade = normal.y > 0.5f ? 1f : normal.y < -0.5f ? 0.55f : Mathf.Abs(normal.x) > 0.5f ? 0.8f : 0.68f;
                colors[i] = new Color32((byte)(Mathf.Clamp01(c.r * shade) * 255f), (byte)(Mathf.Clamp01(c.g * shade) * 255f),
                    (byte)(Mathf.Clamp01(c.b * shade) * 255f), 255);
            }

            var mesh = new Mesh { name = "HullSkin " + id, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(geo.Triangles, 0, calculateBounds: false);
            var size = new Vector3(lod.Model.Width, lod.Model.Height, lod.Model.Length) * lod.Step;
            mesh.bounds = new Bounds(size * 0.5f, size);
            return mesh;
        }

        private Color[] HullLodPalette(BlocksBeyondTheStars.Networking.Messages.NetLandmark model)
        {
            var blocks = model.Blocks ?? System.Array.Empty<ushort>();
            var tints = model.Tints ?? System.Array.Empty<int>();
            var colors = new Color[blocks.Length];
            for (int i = 0; i < blocks.Length; i++)
            {
                Color c = Game.Atlas != null && blocks[i] != 0 ? Game.Atlas.AverageColor(blocks[i]) : new Color(0.45f, 0.44f, 0.42f);
                int tint = i < tints.Length ? tints[i] : 0;
                if (tint != 0)
                {
                    float lum = (c.r * 0.299f) + (c.g * 0.587f) + (c.b * 0.114f);
                    var dye = new Color(((tint >> 16) & 0xFF) / 255f, ((tint >> 8) & 0xFF) / 255f, (tint & 0xFF) / 255f);
                    c = Color.Lerp(c, dye * Mathf.Lerp(0.6f, 1.4f, lum), 0.85f);
                }

                colors[i] = c;
            }

            return colors;
        }
    }
}
