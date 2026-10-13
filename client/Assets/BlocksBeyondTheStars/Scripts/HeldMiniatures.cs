// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The held miniatures of #2534 ("vehicles should show a mini vehicle in the hand, like blocks do — plants and items
    /// too"): the real look of the thing, scaled down into the hand, for the first-person <see cref="Viewmodel"/> and every
    /// avatar's hand alike (both go through <see cref="HeldItem.Build"/>).
    /// <list type="bullet">
    /// <item><b>Vehicles</b> — the speeder / boat hull from the very voxel design the parked vehicle uses
    /// (<see cref="SpeederView.HullCells"/>), chunk-meshed with the world's block atlas and chunk materials.</item>
    /// <item><b>Plants and slim props</b> — the placed block meshed exactly as the world meshes it (a plant's crossed
    /// cut-out cards, a torch, a crown's leaf shell), in the chunk material that already clips the cut-outs. The wind sway
    /// is switched off (it is in metres, and would whip a 25 cm plant around).</item>
    /// <item><b>Every other item without a model</b> — its icon, extruded into pixel voxels (<see cref="HeldIconVoxels"/>),
    /// vertex-coloured in the always-included <c>VertexColorOpaque</c> shader. The icons are imported unreadable and
    /// compressed, so the pixels come back from the GPU once per item (a blit + <see cref="GpuReadback"/> — a synchronous
    /// ReadPixels on desktop and WebGL 2, the async readback on WebGPU).</item>
    /// </list>
    /// Each mesh is built once per vehicle kind / block / item and shared by every hand that holds it. A held object leases
    /// its mesh (<see cref="HeldMiniatureLease"/>), so the cache only ever drops meshes nobody shows; past
    /// <see cref="Capacity"/> entries the least recently used free ones go. <see cref="ReleaseAll"/> frees everything at
    /// session teardown (through <see cref="HeldItem.ReleasePartMaterials"/>).
    /// </summary>
    internal static class HeldMiniatures
    {
        /// <summary>The name of the scaled node under a held holder; <see cref="HeldItem.MuzzleOf"/> skips it.</summary>
        public const string NodeName = "Miniature";

        /// <summary>Cached meshes kept at most (only unleased ones are ever dropped).</summary>
        private const int Capacity = 64;

        // Sizes along the longest side, in the held frame (metres before the viewmodel's ItemScale of 0.72, so a vehicle
        // is ~0.26 m in first person). A placed block in the hand is 0.22.
        private const float VehicleSize = 0.36f;
        private const float PlantSize = 0.30f;
        private const float IconSize = 0.30f;

        // Where each sits in the held frame (+Z forward from the hand), and how it is turned: the vehicle points ahead and
        // turns a little to show its flank, the plant stands upright on its root, the icon is held like a card at a slant
        // so its pixel thickness shows.
        private static readonly Vector3 VehicleAnchor = new Vector3(0f, 0.02f, 0.18f);
        private static readonly Quaternion VehicleTurn = Quaternion.Euler(0f, 25f, 0f);
        private static readonly Vector3 PlantRoot = new Vector3(0f, -0.08f, 0.16f);
        private static readonly Vector3 IconAnchor = new Vector3(0f, 0.04f, 0.15f);
        private static readonly Quaternion IconTurn = Quaternion.Euler(0f, -20f, 0f);

        /// <summary>Engine-glow colour of the held speeder (the parked one's cyan).</summary>
        private static readonly Color EngineGlow = new Color(0.35f, 0.85f, 1f);

        private sealed class Entry
        {
            public Mesh Mesh;
            public bool Pending;   // the icon's pixels are still on their way back from the GPU
            public int Refs;
            public int LastUse;
            public List<(MeshFilter Filter, System.Action<Transform, Mesh> Place)> Waiters;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        private static readonly List<string> EvictScratch = new List<string>();
        private static int _clock;
        private static int _generation;
        private static Material _iconMaterial;

        /// <summary>Puts the held vehicle under <paramref name="holder"/>; false when the vehicle or the world's block
        /// atlas is not available (the caller falls back to the gadget model).</summary>
        public static bool AttachVehicle(Transform holder, string itemKey)
        {
            var kit = HeldItem.MeshKitResolver?.Invoke();
            if (kit == null || itemKey == null)
            {
                return false;
            }

            var (content, atlas, opaque, transparent) = kit.Value;
            string kind = content?.GetItem(itemKey)?.Vehicle?.Kind;
            if (kind == null || atlas == null || opaque == null)
            {
                return false;
            }

            bool boat = kind == VehicleVoxels.BoatKind;
            var mesh = GetOrBuild("v:" + kind, () =>
            {
                var cells = SpeederView.HullCells(content, boat);
                int hull = SpeederView.DefaultHullColor(boat);
                var paint = ShipMeshBuilder.HullPaint(content,
                    new Color(((hull >> 16) & 0xFF) / 255f, ((hull >> 8) & 0xFF) / 255f, (hull & 0xFF) / 255f));
                return MeshCells(cells, content, atlas, paint, still: false);
            });
            if (mesh == null)
            {
                return false;
            }

            var node = NewNode(holder, transparent != null ? new[] { opaque, transparent } : new[] { opaque });
            Lease(node, "v:" + kind);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            Place(node.transform, mesh, VehicleSize, VehicleAnchor, VehicleTurn, root: false);
            if (!boat)
            {
                // The two engine glows at the stern, in the hull's own (design) frame — the node carries the scale.
                foreach (var glow in SpeederView.EngineGlows)
                {
                    HeldItem.GlowPart(node.transform, glow - SpeederView.SpeederMeshOffset, Vector3.one * 0.18f, EngineGlow);
                }
            }

            return true;
        }

        /// <summary>Puts the held plant / slim prop <paramref name="blockKey"/> under <paramref name="holder"/>; false when
        /// the block or the atlas is not available or it meshes to nothing (the caller falls back to the block cube).</summary>
        public static bool AttachPlant(Transform holder, string blockKey)
        {
            var kit = HeldItem.MeshKitResolver?.Invoke();
            if (kit == null || blockKey == null)
            {
                return false;
            }

            var (content, atlas, opaque, transparent) = kit.Value;
            var def = content?.GetBlock(blockKey);
            if (def == null || atlas == null || opaque == null || def.NumericId.Value == 0)
            {
                return false;
            }

            var mesh = GetOrBuild("p:" + blockKey, () =>
            {
                var cells = new Dictionary<Vector3i, BlockId>
                {
                    [new Vector3i(0, 0, 0)] = def.NumericId,
                };
                return MeshCells(cells, content, atlas, null, still: true);
            });
            if (mesh == null)
            {
                return false;
            }

            var node = NewNode(holder, transparent != null ? new[] { opaque, transparent } : new[] { opaque });
            Lease(node, "p:" + blockKey);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            Place(node.transform, mesh, PlantSize, PlantRoot, Quaternion.identity, root: true);
            return true;
        }

        /// <summary>Puts the extruded icon of <paramref name="itemKey"/> under <paramref name="holder"/>; false when the
        /// item has no icon (the caller shows the bare hand). Where the GPU answers later (WebGPU), the node fills in when
        /// the pixels arrive.</summary>
        public static bool AttachIcon(Transform holder, string itemKey)
        {
            var tex = IconResolver.ItemTexture(itemKey);
            var material = IconMaterial();
            if (tex == null || material == null)
            {
                return false;
            }

            string key = "i:" + BlocksBeyondTheStars.Shared.State.ItemKey.Base(itemKey); // the icon is filed under the base key
            var node = NewNode(holder, new[] { material });
            var filter = node.GetComponent<MeshFilter>();
            Lease(node, key, out var entry, out bool created);
            System.Action<Transform, Mesh> place = (t, m) => Place(t, m, IconSize, IconAnchor, IconTurn, root: false);
            if (entry.Pending)
            {
                entry.Waiters ??= new List<(MeshFilter, System.Action<Transform, Mesh>)>();
                entry.Waiters.Add((filter, place));
            }
            else if (entry.Mesh != null)
            {
                filter.sharedMesh = entry.Mesh;
                place(node.transform, entry.Mesh);
            }

            if (created)
            {
                int generation = _generation;
                ReadIcon(tex, mesh => Complete(entry, mesh, generation));
            }

            return true;
        }

        /// <summary>Drops every cached mesh and the icon material (session teardown). Leases of meshes freed here are
        /// ignored when their holders go.</summary>
        public static void ReleaseAll()
        {
            foreach (var entry in Cache.Values)
            {
                if (entry.Mesh != null)
                {
                    Object.Destroy(entry.Mesh);
                }
            }

            Cache.Clear();
            _generation++;
            if (_iconMaterial != null)
            {
                Object.Destroy(_iconMaterial);
                _iconMaterial = null;
            }
        }

        /// <summary>A holder that showed the mesh of <paramref name="key"/> is gone.</summary>
        internal static void Release(string key, int generation)
        {
            if (generation == _generation && key != null && Cache.TryGetValue(key, out var entry) && entry.Refs > 0)
            {
                entry.Refs--;
            }
        }

        private static Mesh GetOrBuild(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var entry))
            {
                entry.LastUse = ++_clock;
                return entry.Mesh;
            }

            Trim();
            Cache[key] = new Entry { Mesh = build(), LastUse = ++_clock }; // a null mesh is remembered too: no rebuild per swap
            return Cache[key].Mesh;
        }

        private static void Lease(GameObject node, string key) => Lease(node, key, out _, out _);

        private static void Lease(GameObject node, string key, out Entry entry, out bool created)
        {
            created = false;
            if (!Cache.TryGetValue(key, out entry))
            {
                Trim();
                entry = new Entry { Pending = true };
                Cache[key] = entry;
                created = true;
            }

            entry.Refs++;
            entry.LastUse = ++_clock;
            var lease = node.AddComponent<HeldMiniatureLease>();
            lease.Key = key;
            lease.Generation = _generation;
        }

        /// <summary>Drops the least recently used meshes nobody holds until there is room for one more.</summary>
        private static void Trim()
        {
            while (Cache.Count >= Capacity)
            {
                string oldest = null;
                int oldestUse = int.MaxValue;
                foreach (var kv in Cache)
                {
                    if (kv.Value.Refs == 0 && !kv.Value.Pending && kv.Value.LastUse < oldestUse)
                    {
                        oldest = kv.Key;
                        oldestUse = kv.Value.LastUse;
                    }
                }

                if (oldest == null)
                {
                    return; // everything is in a hand right now — grow past the cap rather than pull a shown mesh
                }

                if (Cache[oldest].Mesh != null)
                {
                    Object.Destroy(Cache[oldest].Mesh);
                }

                Cache.Remove(oldest);
            }
        }

        private static void Complete(Entry entry, Mesh mesh, int generation)
        {
            if (generation != _generation)
            {
                // The session that asked is gone (ReleaseAll ran meanwhile): nobody will free this mesh.
                if (mesh != null)
                {
                    Object.Destroy(mesh);
                }

                return;
            }

            entry.Mesh = mesh;
            entry.Pending = false;
            if (entry.Waiters == null)
            {
                return;
            }

            foreach (var (filter, place) in entry.Waiters)
            {
                if (filter != null && mesh != null)
                {
                    filter.sharedMesh = mesh;
                    place(filter.transform, mesh);
                }
            }

            entry.Waiters = null;
        }

        private static GameObject NewNode(Transform holder, Material[] materials)
        {
            var node = new GameObject(NodeName);
            node.transform.SetParent(holder, false);
            node.AddComponent<MeshFilter>();
            node.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return node;
        }

        /// <summary>Scales <paramref name="node"/> so the mesh's longest side is <paramref name="size"/>, turns it and puts
        /// its centre — or, for a plant, the middle of its foot — on <paramref name="anchor"/>.</summary>
        private static void Place(Transform node, Mesh mesh, float size, Vector3 anchor, Quaternion turn, bool root)
        {
            var b = mesh.bounds;
            float extent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = extent > 1e-4f ? size / extent : 1f;
            var pivot = root ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
            node.localScale = Vector3.one * s;
            node.localRotation = turn;
            node.localPosition = anchor - (turn * (pivot * s));
        }

        /// <summary>Meshes a few cells the way the world meshes them (one chunk, nothing around). <paramref name="still"/>
        /// takes the wind out of plants: every swaying cut-out code becomes the rigid prop's (still clipped), which the
        /// shader never moves.</summary>
        private static Mesh MeshCells(Dictionary<Vector3i, BlockId> cells,
            BlocksBeyondTheStars.Shared.Content.GameContent content, BlockTextureAtlas atlas, System.Func<BlockId, Color> paint, bool still)
        {
            if (cells == null || cells.Count == 0)
            {
                return null;
            }

            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            foreach (var kv in cells)
            {
                chunk.Set(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value);
            }

            BlockId CellAt(int x, int y, int z)
                => cells.TryGetValue(new Vector3i(x, y, z), out var id) ? id : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, CellAt, atlas, paintTint: paint);
            try
            {
                if (data.Verts.Count == 0)
                {
                    return null;
                }

                if (still)
                {
                    bool changed = false;
                    for (int i = 0; i < data.LeafUv.Count; i++)
                    {
                        var leaf = data.LeafUv[i];
                        float tintMode = data.SkyUv[i].y % 16f;
                        bool sways = (leaf.x > 0.5f && leaf.x < 1.5f)                              // a crown face
                                     || leaf.x > 2.5f                                               // a plant / aquatic plant
                                     || (leaf.x < 0.5f && tintMode > 0.5f && tintMode < 1.5f);    // a solid (rounded) plant
                        if (sways)
                        {
                            leaf.x = ChunkMesher.LeafCodeRigid;
                            data.LeafUv[i] = leaf;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        data.Pack(); // the vertex stream was packed from the lists inside BuildGeometry
                    }
                }

                var (render, _) = data.ToMeshes(buildCollider: false);
                render.name = "HeldMiniature";
                return render;
            }
            finally
            {
                data.Release();
            }
        }

        private static Material IconMaterial()
        {
            if (_iconMaterial == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/VertexColorOpaque"); // always included
                if (shader == null)
                {
                    return null;
                }

                _iconMaterial = new Material(shader) { name = "HeldIcon" };
            }

            return _iconMaterial;
        }

        /// <summary>Gets the icon's pixels to the CPU and builds its voxel mesh: straight from the texture when it is
        /// readable (a texture-pack override), otherwise through a same-size blit into an sRGB render texture read back
        /// with <see cref="GpuReadback"/> (the bytes come back sRGB-encoded, like the PNG).</summary>
        private static void ReadIcon(Texture2D tex, System.Action<Mesh> done)
        {
            if (tex.isReadable)
            {
                Mesh direct = null;
                try
                {
                    direct = IconMesh(tex.GetPixels32(), tex.width, tex.height);
                }
                catch (UnityException e)
                {
                    Debug.LogWarning($"[HeldMiniatures] icon {tex.name} unreadable: {e.Message}");
                }

                done(direct);
                return;
            }

            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var active = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = active; // Blit leaves its target active — it must not be when it is released below
            GpuReadback.Read(rt, TextureFormat.RGBA32, copy =>
            {
                RenderTexture.ReleaseTemporary(rt);
                Mesh mesh = null;
                if (copy != null)
                {
                    mesh = IconMesh(copy.GetPixels32(), copy.width, copy.height);
                    Object.Destroy(copy);
                }

                done(mesh);
            });
        }

        private static Mesh IconMesh(Color32[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length < width * height)
            {
                return null;
            }

            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                var p = pixels[i];
                rgba[i * 4] = p.r;
                rgba[(i * 4) + 1] = p.g;
                rgba[(i * 4) + 2] = p.b;
                rgba[(i * 4) + 3] = p.a;
            }

            var grid = HeldIconVoxels.Downsample(rgba, width, height);
            if (grid == null)
            {
                return null;
            }

            float cell = 1f / grid.Size; // a unit square; Place scales it to the hand
            var data = HeldIconVoxels.BuildMesh(grid, cell, cell);
            int n = data.Positions.Count;
            var verts = new List<Vector3>(n);
            var normals = new List<Vector3>(n);
            var colors = new List<Color32>(n);
            for (int i = 0; i < n; i++)
            {
                var p = data.Positions[i];
                var nr = data.Normals[i];
                var c = data.Colors[i];
                verts.Add(new Vector3(p.X, p.Y, p.Z));
                normals.Add(new Vector3(nr.X, nr.Y, nr.Z));
                // The pixels are sRGB; the shader takes vertex colours as they come, so convert like every colour stream.
                colors.Add(ShaderColor.Srgb(new Color32(c.R, c.G, c.B, 255)));
            }

            var mesh = new Mesh
            {
                name = "HeldIcon",
                indexFormat = n <= ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt16 : UnityEngine.Rendering.IndexFormat.UInt32,
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }

    /// <summary>Marks a held object as showing a cached miniature mesh (#2534); gives the lease back when it goes, so the
    /// cache knows the mesh is free to drop. Never destroys the mesh itself — other hands may share it.</summary>
    public sealed class HeldMiniatureLease : MonoBehaviour
    {
        internal string Key;
        internal int Generation;

        private void OnDestroy() => HeldMiniatures.Release(Key, Generation);
    }
}
