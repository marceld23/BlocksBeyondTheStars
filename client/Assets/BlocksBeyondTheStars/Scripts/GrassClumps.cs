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
    /// Grass as dense clumps (<see cref="GameLook.GrassClumps"/>). On every open grass top
    /// near the camera stand one or more clumps of slim, pointed blades — real small bodies that bend in the wind — and
    /// along an edge where the ground drops a fringe of blades hangs over, so the hard edge of a grass block is gone.
    /// GPU instances of a few shared meshes, one draw per kind and chunk (the pattern of <see cref="GrassShells"/> and
    /// <see cref="CrownClusters"/>); the mesher's carpet tops are the points, so nothing here is in the chunk mesh.
    /// <para>
    /// Form without colour: the meshes carry a brightness and colour-slot weights per vertex
    /// and no colour. The palette comes from the world — the ground's own tile, the world's grass-tuft colour and one of
    /// its flower colours (GrassClump.shader).
    /// </para>
    /// </summary>
    public sealed class GrassClumps : MonoBehaviour
    {
        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard cap per call
        private const int ClumpKinds = 4;
        private const int TuftKinds = 2, PebbleKinds = 2;      // ground cover on bare ground: dry tufts, round pebbles
        private const int FirstTuft = ClumpKinds + 1;          // after the edge fringe
        private const int FirstPebble = FirstTuft + TuftKinds;
        private const int Kinds = FirstPebble + PebbleKinds;

        private static readonly int TileId = Shader.PropertyToID("_GcTile");
        private static readonly int LightId = Shader.PropertyToID("_GcLight");
        private static readonly int AccentId = Shader.PropertyToID("_GcAccent");

        private static readonly Mesh[] Meshes = new Mesh[Kinds];
        private static readonly Dictionary<int, Vector2> TileCentres = new Dictionary<int, Vector2>();
        private static Color[] _palette = System.Array.Empty<Color>();
        private static int _paletteVersion;
        private static BlockTextureAtlas _atlas;
        private static int _atlasVersion;
        private static Material _material;
        private static Camera _cam;
        private static int _camFrame = -1;
        private static Vector3 _camPos;
        private static float _range;

        private readonly List<Vector4> _tops = new List<Vector4>();
        private readonly List<Vector3> _light = new List<Vector3>();
        private readonly List<int> _edges = new List<int>();
        private readonly List<Vector4> _scrub = new List<Vector4>();
        private readonly List<Vector4> _scrubLight = new List<Vector4>();
        private readonly List<Batch>[] _batches = new List<Batch>[Kinds];
        private readonly int[] _used = new int[Kinds];
        private bool _dirty = true;
        private int _bakedPalette = -1;
        private int _bakedAtlas = -1;
        private Vector3Int _origin;
        private Vector3 _bakedPos;
        private MeshRenderer _chunkRenderer;

        /// <summary>The world's block atlas: the clumps read the ground's colour from it (bound as the material's texture)
        /// and take each top tile's place in it. A new atlas re-bakes every chunk.</summary>
        public static void UseAtlas(BlockTextureAtlas atlas)
        {
            if (ReferenceEquals(atlas, _atlas))
            {
                return;
            }

            _atlas = atlas;
            _atlasVersion++;
            TileCentres.Clear();
        }

        /// <summary>The world's flower colours (shader-ready) — the accent slot of a clump in bloom. Set with the world's
        /// flora tints; every chunk re-bakes on its next draw.</summary>
        public static void SetPalette(Color[] flowers)
        {
            _palette = flowers != null ? (Color[])flowers.Clone() : System.Array.Empty<Color>();
            _paletteVersion++;
        }

        /// <summary>Takes a chunk's carpet tops (the mesher's <c>GrassTops</c>: chunk-local top centre, w = block id +
        /// skylight fraction), the placed light and the open-edge mask per top, and the chunk's canonical world origin.
        /// The lists are copied; empty input disables the component.</summary>
        public void Setup(List<Vector4> tops, List<Vector3> light, List<int> edges, Vector3Int origin,
            List<Vector4> scrub = null, List<Vector4> scrubLight = null)
        {
            _tops.Clear();
            _light.Clear();
            _edges.Clear();
            _scrub.Clear();
            _scrubLight.Clear();
            if (!GameLook.RecordGrassClumps)
            {
                // A preset without clumps (Low, Potato, the browser): the carpet tops are the High carpet's, kept nowhere here.
                _origin = origin;
                HasData = false;
                return;
            }

            if (scrub != null && scrubLight != null && scrub.Count == scrubLight.Count)
            {
                // The ground cover of bare ground (the mesher's ScrubTops): a dry tuft or a few pebbles per entry.
                _scrub.AddRange(scrub);
                _scrubLight.AddRange(scrubLight);
            }

            if (tops != null)
            {
                _tops.AddRange(tops);
                if (light != null && light.Count == tops.Count)
                {
                    _light.AddRange(light);
                }

                if (edges != null && edges.Count == tops.Count)
                {
                    _edges.AddRange(edges);
                }
            }

            _origin = origin;
            _dirty = true;
            HasData = GameLook.GrassClumps && (_tops.Count > 0 || _scrub.Count > 0);
        }

        /// <summary>True while the chunk holds something for this drawer; the detail pass (GameBootstrap.Detail.cs) skips
        /// the chunk otherwise.</summary>
        public bool HasData { get; private set; }

        /// <summary>The once-a-frame step (camera, range) and how far the clumps reach this frame — 0 without a camera,
        /// without an atlas or on a preset without them.</summary>
        public static float FrameReach() => TickFrame() && _atlas != null ? _range : 0f;

        private void Awake()
        {
            _chunkRenderer = GetComponent<MeshRenderer>();
            for (int i = 0; i < Kinds; i++)
            {
                _batches[i] = new List<Batch>();
            }
        }

        /// <summary>Submits this chunk's clumps for the current frame. Called by the detail pass for chunks inside the
        /// reach (#4046) — no <c>Update()</c> of its own.</summary>
        public void Draw()
        {
            // A chunk the visibility walk or the distance cull hides (or one drawn for its shadow only) shows no grass.
            if (!TickFrame() || _range <= 0f || _atlas == null || _chunkRenderer == null || !_chunkRenderer.enabled
                || _chunkRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                return;
            }

            var material = ClumpMaterial();
            if (material == null)
            {
                return;
            }

            var pos = transform.position;
            if (DistanceSqToChunk(_camPos, pos) > (_range + 1f) * (_range + 1f))
            {
                return;
            }

            if (pos != _bakedPos)
            {
                _bakedPos = pos; // first draw, or the chunk moved across the world's seam
                _dirty = true;
            }

            if (_dirty || _bakedPalette != _paletteVersion || _bakedAtlas != _atlasVersion)
            {
                Build();
            }

            int layer = gameObject.layer;
            for (int kind = 0; kind < Kinds; kind++)
            {
                var mesh = MeshOf(kind);
                for (int b = 0; b < _used[kind]; b++)
                {
                    _batches[kind][b].Draw(mesh, material, layer);
                }
            }
        }

        /// <summary>The once-per-frame work shared by every chunk: the camera and the clumps' range.</summary>
        private static bool TickFrame()
        {
            if (_camFrame != Time.frameCount)
            {
                _camFrame = Time.frameCount;
                _cam = Camera.main;
                if (_cam != null)
                {
                    _camPos = _cam.transform.position;
                }

                _range = GameLook.GrassRangeNow;
            }

            return _cam != null;
        }

        private static Material ClumpMaterial()
        {
            if (_material == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/GrassClump");
                if (shader == null)
                {
                    return null;
                }

                _material = new Material(shader) { name = "GrassClump", enableInstancing = true };
            }

            if (_atlas != null && _material.mainTexture != _atlas.Texture)
            {
                _material.mainTexture = _atlas.Texture;
            }

            return _material;
        }

        /// <summary>Squared distance from a point to the chunk's box (one block taller: a lid cell's grass stands on it).</summary>
        private static float DistanceSqToChunk(Vector3 p, Vector3 origin)
        {
            float n = WorldConstants.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(origin.x - p.x, p.x - (origin.x + n)));
            float dy = Mathf.Max(0f, Mathf.Max(origin.y - p.y, p.y - (origin.y + n + 1f)));
            float dz = Mathf.Max(0f, Mathf.Max(origin.z - p.z, p.z - (origin.z + n)));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        /// <summary>Per top: one clump per density step (its own kind, quarter turn and size from the world cell's hash, so
        /// every client grows the same meadow) and a fringe on every open edge.</summary>
        private void Build()
        {
            _dirty = false;
            _bakedPalette = _paletteVersion;
            _bakedAtlas = _atlasVersion;
            for (int kind = 0; kind < Kinds; kind++)
            {
                _used[kind] = 0;
            }

            var current = new Batch[Kinds];
            int density = Mathf.Clamp(Mathf.RoundToInt(GameLook.GrassDensity), 1, 3);
            for (int i = 0; i < _tops.Count; i++)
            {
                var p = _tops[i];
                int id = (int)p.w;
                float sky = p.w - id;
                var bl = i < _light.Count ? _light[i] : Vector3.zero;
                int wx = _origin.x + (int)p.x, wy = _origin.y + (int)p.y - 1, wz = _origin.z + (int)p.z;
                uint hash = unchecked((uint)((wx * 73856093) ^ (wy * 19349663) ^ (wz * 83492791)));
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;

                Vector2 centre = TileCentre(id);
                Color accent = _palette.Length > 0 ? _palette[(int)(hash % (uint)_palette.Length)] : Color.white;
                bool bloom = _palette.Length > 0 && ((hash >> 8) % 3u) == 0u;
                var tile = new Vector4(centre.x, centre.y, sky, bloom ? 1f : 0f);
                var light = new Vector4(bl.x, bl.y, bl.z, 0f);
                var accentV = new Vector4(accent.r, accent.g, accent.b, 0f);
                var at = _bakedPos + new Vector3(p.x, p.y, p.z);
                AddTop(current, density, hash, i < _edges.Count ? _edges[i] : 0, at, tile, light, accentV);
            }

            BuildScrub(current);
            for (int kind = 0; kind < Kinds; kind++)
            {
                current[kind]?.Commit();
            }
        }

        /// <summary>One grass top's instances: <paramref name="density"/> clumps (kind, quarter turn and size from the
        /// cell's <paramref name="hash"/>) and a fringe on every open edge of the mesher's <paramref name="edges"/> mask.</summary>
        private void AddTop(Batch[] current, int density, uint hash, int edges, Vector3 at, Vector4 tile, Vector4 light, Vector4 accentV)
        {
            // Bit 4 of the mesher's mask: a plant with leaves near the ground stands on this top — the grass stays low
            // under it, so no blade pokes through the plant.
            float low = (edges & 16) != 0 ? 0.5f : 1f;
            edges &= 15;
            for (int c = 0; c < density; c++)
            {
                uint h = hash + (uint)c * 0x9e3779b9u;
                h ^= h >> 16;
                int kind = (int)(h % ClumpKinds);
                var turn = Quaternion.Euler(0f, 90f * ((h >> 4) & 3u), 0f);
                float size = (0.85f + (0.35f * (((h >> 8) & 0xFFu) / 255f))) * low;
                Add(current, kind, Matrix4x4.TRS(at, turn, new Vector3(1f, size, 1f)), tile, light, accentV);
            }

            for (int side = 0; side < 4 && edges != 0; side++)
            {
                if ((edges & (1 << side)) != 0)
                {
                    // The fringe mesh hangs over +Z; side 0 = +X, 1 = -X, 2 = +Z, 3 = -Z (the mesher's mask).
                    float yaw = side == 0 ? 90f : side == 1 ? 270f : side == 2 ? 0f : 180f;
                    Add(current, ClumpKinds, Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one), tile, light, accentV);
                }
            }
        }

        /// <summary>The ground cover of bare ground: per entry a dry tuft or a group of pebbles, turned any way and set a
        /// little off the cell's middle (by the world cell's hash), in the ground's own colour.</summary>
        private void BuildScrub(Batch[] current)
        {
            var white = new Vector4(1f, 1f, 1f, 0f);
            for (int i = 0; i < _scrub.Count; i++)
            {
                var p = _scrub[i];
                int id = (int)p.w;
                float sky = p.w - id;
                var sl = _scrubLight[i];
                int wx = _origin.x + (int)p.x, wy = _origin.y + (int)p.y - 1, wz = _origin.z + (int)p.z;
                uint hash = unchecked((uint)((wx * 73856093) ^ (wy * 19349663) ^ (wz * 83492791)));
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;

                Vector2 centre = TileCentre(id);
                var tile = new Vector4(centre.x, centre.y, sky, 0f);
                var light = new Vector4(sl.x, sl.y, sl.z, 0f);
                bool pebbles = sl.w > 0.5f;
                int kind = pebbles ? FirstPebble + (int)(hash % PebbleKinds) : FirstTuft + (int)(hash % TuftKinds);
                float yaw = ((hash >> 4) & 0xFFu) * (360f / 256f);
                float size = 0.8f + (0.5f * (((hash >> 12) & 0xFFu) / 255f));
                float ox = ((((hash >> 20) & 0xFu) / 15f) - 0.5f) * 0.4f, oz = ((((hash >> 24) & 0xFu) / 15f) - 0.5f) * 0.4f;
                var at = _bakedPos + new Vector3(p.x + ox, p.y, p.z + oz);
                var scale = pebbles ? Vector3.one * size : new Vector3(1f, size, 1f);
                Add(current, kind, Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), scale), tile, light, white);
            }
        }

        private void Add(Batch[] current, int kind, Matrix4x4 matrix, Vector4 tile, Vector4 light, Vector4 accent)
        {
            var batch = current[kind];
            if (batch == null || batch.Count == BatchSize)
            {
                batch?.Commit();
                var list = _batches[kind];
                if (_used[kind] == list.Count)
                {
                    list.Add(new Batch());
                }

                batch = current[kind] = list[_used[kind]++];
                batch.Begin();
            }

            batch.Add(matrix, tile, light, accent);
        }

        /// <summary>The centre of a block's top tile in the atlas (its cap tile when it has one) — the clump reads the
        /// tile's average colour there.</summary>
        private static Vector2 TileCentre(int id)
        {
            if (!TileCentres.TryGetValue(id, out var centre))
            {
                ushort slot = (ushort)id;
                if (_atlas.TryGetCapTile(slot, out ushort cap))
                {
                    slot = cap;
                }

                centre = _atlas.TileUv(slot).center;
                TileCentres[id] = centre;
            }

            return centre;
        }

        private static Mesh MeshOf(int kind)
        {
            if (Meshes[kind] == null)
            {
                Meshes[kind] = kind < ClumpKinds ? BuildClump(kind)
                    : kind == ClumpKinds ? BuildFringe()
                    : kind < FirstPebble ? BuildDryTuft(kind - FirstTuft) : BuildPebbles(kind - FirstPebble);
            }

            return Meshes[kind];
        }

        // --- The shapes. No colour anywhere: brightness, slot weights and the bend only. ---

        private sealed class Shape
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Color> Tones = new List<Color>();   // r brightness, g second weight, b accent weight, a bend
            public readonly List<Vector2> Phase = new List<Vector2>();
            public readonly List<Vector2> Axis = new List<Vector2>();
            public readonly List<int> Tris = new List<int>();

            public Mesh ToMesh(string name, Bounds bounds)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetColors(Tones);
                mesh.SetUVs(0, Phase);
                mesh.SetUVs(1, Axis);
                mesh.SetTriangles(Tris, 0);
                mesh.bounds = bounds;
                mesh.UploadMeshData(true);
                return mesh;
            }
        }

        /// <summary>One blade: a slim four-sided body from a square root over a narrower middle ring to a point. The three
        /// stations (root, middle, tip) are free points, so a blade can stand, lean or hang over an edge.</summary>
        private static void AddBlade(Shape s, Vector3 root, Vector3 middle, Vector3 tip, float halfWidth, float second,
            float phase, float bendScale)
        {
            float height = Mathf.Max(0.02f, Mathf.Abs(tip.y - root.y) + new Vector2(tip.x - root.x, tip.z - root.z).magnitude);
            int first = s.Verts.Count;

            // Three rings: root, middle and a BLUNT tip a third as wide as the root. A blade that ran out into a point
            // was thinner than a pixel at its end and flickered in the wind.
            for (int ring = 0; ring < 3; ring++)
            {
                Vector3 c = ring == 0 ? root : ring == 1 ? middle : tip;
                float w = ring == 0 ? halfWidth : ring == 1 ? halfWidth * 0.7f : halfWidth * 0.34f;
                float along = ring == 0 ? 0f : ring == 1 ? 0.55f : 1f;
                for (int k = 0; k < 4; k++)
                {
                    float sx = k == 0 || k == 3 ? -1f : 1f, sz = k < 2 ? -1f : 1f;
                    s.Verts.Add(new Vector3(c.x + (sx * w), c.y, c.z + (sz * w)));
                    float tone = (ring == 0 ? 0.40f : ring == 1 ? 0.72f : 0.97f) + ((k & 1) == 0 ? 0.03f : -0.03f);
                    float slot = second * (ring == 0 ? 0.25f : ring == 1 ? 0.8f : 1f);
                    s.Tones.Add(new Color(tone, slot, 0f, along * along * bendScale * (height / 0.32f)));
                    s.Phase.Add(new Vector2(phase, 0f));
                    s.Axis.Add(new Vector2(c.x, c.z));
                }
            }

            for (int ring = 0; ring < 2; ring++)
            {
                int lower = first + (ring * 4), upper = lower + 4;
                for (int k = 0; k < 4; k++)
                {
                    int a = lower + k, b = lower + ((k + 1) % 4), c = upper + ((k + 1) % 4), d = upper + k;
                    s.Tris.Add(a); s.Tris.Add(b); s.Tris.Add(c);
                    s.Tris.Add(a); s.Tris.Add(c); s.Tris.Add(d);
                }
            }

            int top = first + 8; // the cap of the blunt tip
            s.Tris.Add(top); s.Tris.Add(top + 1); s.Tris.Add(top + 2);
            s.Tris.Add(top); s.Tris.Add(top + 2); s.Tris.Add(top + 3);
        }

        /// <summary>A seed head: a small upright body on a blade's tip (taller than wide), all accent slot. It widens with
        /// distance only half as much as a blade — at full rate the heads read as confetti over the meadow.</summary>
        private static void AddHead(Shape s, Vector3 centre, float half, float phase, float bend)
        {
            int first = s.Verts.Count;
            for (int k = 0; k < 8; k++)
            {
                float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f, sz = (k & 4) == 0 ? -1f : 1f;
                var at = new Vector3(centre.x + (sx * half), centre.y + (sy * half * 1.8f), centre.z + (sz * half));
                s.Verts.Add(at);
                s.Tones.Add(new Color(sy > 0f ? 0.95f : 0.7f, 0f, 1f, bend));
                s.Phase.Add(new Vector2(phase, 0f));
                s.Axis.Add(new Vector2((centre.x + at.x) * 0.5f, (centre.z + at.z) * 0.5f));
            }

            int[] quads = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
            for (int q = 0; q < quads.Length; q += 4)
            {
                s.Tris.Add(first + quads[q]); s.Tris.Add(first + quads[q + 1]); s.Tris.Add(first + quads[q + 2]);
                s.Tris.Add(first + quads[q]); s.Tris.Add(first + quads[q + 2]); s.Tris.Add(first + quads[q + 3]);
            }
        }

        /// <summary>A clump for one cell: 13 to 16 blades of mixed height strewn over the cell, most short, each leaning
        /// its own way; about a third carry the second colour toward the tip, one or two a seed head.</summary>
        private static Mesh BuildClump(int kind)
        {
            var rng = new System.Random(7001 + (kind * 131));
            float R() => (float)rng.NextDouble();
            var s = new Shape();
            int blades = 13 + rng.Next(4);
            int heads = kind % 2 == 0 ? 1 : 0; // every other kind carries one seed head
            for (int b = 0; b < blades; b++)
            {
                var root = new Vector3((R() * 0.86f) - 0.43f, 0f, (R() * 0.86f) - 0.43f);
                float tall = R();
                float height = Mathf.Lerp(0.11f, 0.32f, tall * tall); // most blades short, a few tall
                float half = Mathf.Lerp(0.017f, 0.026f, R());
                float angle = R() * Mathf.PI * 2f;
                float lean = Mathf.Lerp(0.02f, 0.10f, R()) * (height / 0.3f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var middle = root + (dir * (lean * 0.35f)) + new Vector3(0f, height * 0.55f, 0f);
                var tip = root + (dir * lean) + new Vector3(0f, height, 0f);
                float second = R() < 0.32f ? Mathf.Lerp(0.5f, 1f, R()) : 0f;
                float phase = R();
                AddBlade(s, root, middle, tip, half, second, phase, 1f);
                if (heads > 0 && height > 0.2f)
                {
                    heads--;
                    AddHead(s, tip + new Vector3(0f, 0.02f, 0f), 0.016f, phase, height / 0.32f);
                }
            }

            return s.ToMesh($"GrassClump {kind}", new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(1.5f, 0.8f, 1.5f)));
        }

        /// <summary>A dry tuft for bare ground: a tight bunch of long blades splaying out of one root — the tussocks of
        /// the concept pictures' savanna.</summary>
        private static Mesh BuildDryTuft(int kind)
        {
            var rng = new System.Random(8101 + (kind * 97));
            float R() => (float)rng.NextDouble();
            var s = new Shape();
            int blades = 8 + rng.Next(4);
            for (int b = 0; b < blades; b++)
            {
                float phi = R() * 6.2832f, from = R() * 0.06f;
                var away = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var root = away * from;
                float height = Mathf.Lerp(0.2f, 0.46f, R()), lean = Mathf.Lerp(0.12f, 0.5f, R());
                var middle = root + (away * (height * lean * 0.5f)) + new Vector3(0f, height * 0.55f, 0f);
                var tip = root + (away * (height * lean * 1.3f)) + new Vector3(0f, height * (1f - (0.25f * lean)), 0f);
                AddBlade(s, root, middle, tip, Mathf.Lerp(0.013f, 0.02f, R()), R() < 0.4f ? 0.35f : 0.1f, R(), 0.8f);
            }

            return s.ToMesh("DryTuft" + kind, new Bounds(new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.8f, 1.1f)));
        }

        /// <summary>Two or three round pebbles: low domes with a slightly uneven outline, lighter on top and darker at
        /// the foot (the shader lights a clump without normals, so the roundness is in the tones). They do not move.</summary>
        private static Mesh BuildPebbles(int kind)
        {
            var rng = new System.Random(9311 + (kind * 53));
            float R() => (float)rng.NextDouble();
            var s = new Shape();
            int stones = 2 + kind;
            for (int i = 0; i < stones; i++)
            {
                var centre = new Vector3((R() - 0.5f) * 0.5f, 0f, (R() - 0.5f) * 0.5f);
                float radius = i == 0 ? Mathf.Lerp(0.075f, 0.11f, R()) : Mathf.Lerp(0.04f, 0.075f, R());
                AddPebble(s, centre, radius, radius * Mathf.Lerp(0.7f, 1.05f, R()), rng);
            }

            return s.ToMesh("Pebbles" + kind, new Bounds(new Vector3(0f, 0.08f, 0f), new Vector3(1f, 0.4f, 1f)));
        }

        private static void AddPebble(Shape s, Vector3 centre, float radius, float height, System.Random rng)
        {
            const int Segments = 7;
            float[] ringRadius = { 0.94f, 1.03f, 0.7f };
            float[] ringHeight = { -0.12f, 0.42f, 0.84f };
            float[] ringTone = { 0.38f, 0.62f, 0.84f };
            var bulge = new float[Segments];
            for (int j = 0; j < Segments; j++)
            {
                bulge[j] = 0.9f + ((float)rng.NextDouble() * 0.2f);
            }

            int first = s.Verts.Count;
            for (int ring = 0; ring < 3; ring++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    float a = j * 6.2832f / Segments;
                    var at = centre + new Vector3(Mathf.Cos(a) * radius * ringRadius[ring] * bulge[j], height * ringHeight[ring],
                        Mathf.Sin(a) * radius * ringRadius[ring] * bulge[j]);
                    s.Verts.Add(at);
                    s.Tones.Add(new Color(ringTone[ring] + (0.07f * Mathf.Cos(a - 0.9f)), 0f, 0f, 0f)); // one side a little lighter
                    s.Phase.Add(Vector2.zero);
                    s.Axis.Add(new Vector2(at.x, at.z)); // its own axis: a pebble does not widen with distance
                }
            }

            int top = s.Verts.Count;
            s.Verts.Add(centre + new Vector3(0f, height, 0f));
            s.Tones.Add(new Color(0.92f, 0f, 0f, 0f));
            s.Phase.Add(Vector2.zero);
            s.Axis.Add(new Vector2(centre.x, centre.z));
            for (int ring = 0; ring < 2; ring++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = first + (ring * Segments) + j, b = first + (ring * Segments) + ((j + 1) % Segments);
                    int c = b + Segments, d = a + Segments;
                    s.Tris.Add(a); s.Tris.Add(b); s.Tris.Add(c);
                    s.Tris.Add(a); s.Tris.Add(c); s.Tris.Add(d);
                }
            }

            for (int j = 0; j < Segments; j++)
            {
                s.Tris.Add(first + (2 * Segments) + j);
                s.Tris.Add(first + (2 * Segments) + ((j + 1) % Segments));
                s.Tris.Add(top);
            }
        }

        /// <summary>The fringe of one open edge (+Z): blades rooted just inside the edge that arch out over it and hang
        /// down a little — the grass block's hard edge disappears under them.</summary>
        private static Mesh BuildFringe()
        {
            var rng = new System.Random(9173);
            float R() => (float)rng.NextDouble();
            var s = new Shape();
            const int Blades = 9;
            for (int b = 0; b < Blades; b++)
            {
                float x = Mathf.Lerp(-0.45f, 0.45f, (b + R()) / Blades);
                var root = new Vector3(x, 0f, 0.44f + (R() * 0.04f));
                var middle = new Vector3(x + ((R() - 0.5f) * 0.04f), 0.05f + (R() * 0.04f), 0.55f + (R() * 0.03f));
                var tip = new Vector3(x + ((R() - 0.5f) * 0.08f), -0.03f - (R() * 0.09f), 0.62f + (R() * 0.07f));
                AddBlade(s, root, middle, tip, Mathf.Lerp(0.017f, 0.025f, R()), R() < 0.3f ? 0.7f : 0f, R(), 0.35f);
            }

            return s.ToMesh("GrassFringe", new Bounds(new Vector3(0f, 0f, 0.3f), new Vector3(1.4f, 0.6f, 1.2f)));
        }

        /// <summary>One instanced draw: up to <see cref="BatchSize"/> matrices and three per-instance vectors. Reused across
        /// rebuilds (grown, never shrunk).</summary>
        private sealed class Batch
        {
            private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
            private readonly List<Vector4> _tile = new List<Vector4>();
            private readonly List<Vector4> _light = new List<Vector4>();
            private readonly List<Vector4> _accent = new List<Vector4>();
            private MaterialPropertyBlock _props;
            private int _propsLength;

            public int Count => _matrices.Count;

            public void Begin()
            {
                _matrices.Clear();
                _tile.Clear();
                _light.Clear();
                _accent.Clear();
            }

            public void Add(Matrix4x4 matrix, Vector4 tile, Vector4 light, Vector4 accent)
            {
                _matrices.Add(matrix);
                _tile.Add(tile);
                _light.Add(light);
                _accent.Add(accent);
            }

            /// <summary>Uploads the per-instance vectors. A property block fixes an array's length on its first upload,
            /// so a batch that grew past it gets a fresh block.</summary>
            public void Commit()
            {
                if (Count == 0)
                {
                    return;
                }

                if (_props == null || Count > _propsLength)
                {
                    _props = new MaterialPropertyBlock();
                    _propsLength = Count;
                }

                _props.SetVectorArray(TileId, _tile);
                _props.SetVectorArray(LightId, _light);
                _props.SetVectorArray(AccentId, _accent);
            }

            public void Draw(Mesh mesh, Material material, int layer)
            {
                if (Count == 0 || _props == null || mesh == null)
                {
                    return;
                }

                Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, _props, ShadowCastingMode.Off, true, layer, null,
                    LightProbeUsage.Off);
            }
        }
    }
}
