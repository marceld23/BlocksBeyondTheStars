// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Makes a flora SPAWN/REGROW source recognizable. When a harvested plant is scheduled to regrow the
    /// server sends <see cref="FloraRegrowStarted"/>; this spawns a small green sprout at that cell and grows
    /// it in over the regrow delay, so the otherwise-bare cell visibly reads as "something is growing here"
    /// rather than the plant popping back from nothing. When the real plant returns (a <see cref="BlockChanged"/>
    /// at the cell) or the timer elapses, the sprout is removed. Purely cosmetic + render-only — the server
    /// stays authoritative over the actual block; a missed message just means no sprout, never a gameplay change.
    ///
    /// Code-built on the always-included Unlit shader (no assets, no new shader), mirroring <see cref="MiningFx"/>.
    /// This also subsumes the original "tint the host block" idea, which is impossible: the host's top face is
    /// culled under the plant, so it never reaches the mesh — a cell marker is the only workable cue (and it
    /// also covers the bare regrow window, which a host tint never could).
    /// </summary>
    public sealed class FloraGrowthFx : MonoBehaviour
    {
        public GameBootstrap Game;

        // The minimum / full sprout size (local units) and the start fraction so it is visible the instant it
        // appears rather than growing up from an invisible speck.
        private const float Width = 0.42f;
        private const float Height = 0.62f;
        private const float StartFraction = 0.12f;

        private readonly Dictionary<Vector3Int, Sprout> _sprouts = new();
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.FloraRegrowStartedReceived += OnRegrowStarted;
                Game.Network.BlockChanged += OnBlockChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.FloraRegrowStartedReceived -= OnRegrowStarted;
                Game.Network.BlockChanged -= OnBlockChanged;
            }
        }

        private void OnRegrowStarted(FloraRegrowStarted m)
        {
            var cell = new Vector3Int(m.X, m.Y, m.Z);
            string key = Game?.Content?.BlockById(new BlockId(m.Block))?.Key;
            if (_sprouts.TryGetValue(cell, out var existing) && existing != null)
            {
                existing.PlantKey = key;
                existing.Restart(Mathf.Max(0.1f, m.Seconds));
                return;
            }

            // A small version of the plant itself where the regrow marker shows plant bodies; else the plain sprout.
            var sprout = (GameLook.RegrowBodies ? SpawnPlant(cell, new BlockId(m.Block), key, Mathf.Max(0.1f, m.Seconds)) : null)
                ?? Sprout.Spawn(cell, Mathf.Max(0.1f, m.Seconds), Width, Height, StartFraction);
            sprout.PlantKey = key;
            sprout.OnFinished = () => _sprouts.Remove(cell);
            _sprouts[cell] = sprout;
        }

        /// <summary>The real plant (or any block) arrived on a tracked cell — the sprout has served its purpose,
        /// drop it so it doesn't overlap the freshly meshed flora. #2532: when a cell turns into air or a fluid, the
        /// sprout standing on it (and a hanging plant's sprout under it) has lost its ground — the server cancels
        /// that regrow, so the sprout goes too instead of floating over the hole until its timer runs out.</summary>
        private void OnBlockChanged(BlockChanged m)
        {
            var cell = new Vector3Int(m.X, m.Y, m.Z);
            if (m.Block != 0)
            {
                RemoveSprout(cell);
            }

            if (_sprouts.Count == 0 || (m.Block != 0 && !IsFluid(m.Block)))
            {
                return;
            }

            RemoveIfRootedIn(cell + Vector3Int.up, m.Block, hanging: false);
            RemoveIfRootedIn(cell + Vector3Int.down, m.Block, hanging: true);
        }

        /// <summary>#2532: drops the sprout in <paramref name="cell"/> when its plant rooted in the neighbour that just
        /// lost its block — a standing plant above it, a hanging one (#1759) below it. A lily keeps its sprout over
        /// water: that fluid is its ground (the server's rule, <c>FloraRootsIn</c>).</summary>
        private void RemoveIfRootedIn(Vector3Int cell, ushort hostBlock, bool hanging)
        {
            if (!_sprouts.TryGetValue(cell, out var sprout) || sprout == null)
            {
                return;
            }

            string key = sprout.PlantKey;
            bool plantHangs = key != null && BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.IsHanging(key);
            if (plantHangs != hanging)
            {
                return;
            }

            if (hostBlock != 0 && key != null
                && BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.Find(key) is { } species
                && System.Array.IndexOf(species.Hosts, Game?.Content?.BlockById(new BlockId(hostBlock))?.Key) >= 0)
            {
                return; // rooted in this very fluid
            }

            RemoveSprout(cell);
        }

        private void RemoveSprout(Vector3Int cell)
        {
            if (_sprouts.TryGetValue(cell, out var sprout))
            {
                _sprouts.Remove(cell);
                if (sprout != null)
                {
                    Destroy(sprout.gameObject);
                }
            }
        }

        /// <summary>Water, lava or a still liquid such as oil (#2106) — what a plant cannot stand on.</summary>
        private bool IsFluid(ushort block)
        {
            var def = Game?.Content?.BlockById(new BlockId(block));
            return def != null && (def.Key is "water" or "lava" || def.Liquid);
        }

        // ---------------------------------------------------------------- The plant regrows as itself
        //
        // The plain marker is two crossed plain green quads — a green rectangle that looks like nothing in the world.
        // With GameLook.RegrowBodies the cell shows the plant that
        // regrows there, small, and it grows over the regrow delay from PlantFrom to PlantTo of its size; then the real
        // plant takes its place (a small step instead of a plant out of nothing). Where plants are drawn as bodies
        // (Medium and High on a desktop) it is the species' body in its colours on this world, with its wind, light
        // and glow; elsewhere (Low, Potato, the browser) the species' crossed picture — or, for a solid form such as a
        // cactus, a slim column wearing its tile — on the chunk material, so it is tinted, lit and swayed like the plant.

        private const float PlantFrom = 0.15f;
        private const float PlantTo = 0.85f;

        private Sprout SpawnPlant(Vector3Int cell, BlockId block, string key, float seconds)
        {
            var def = Game?.Content?.BlockById(block);
            if (def == null || key == null)
            {
                return null;
            }

            int mode = ChunkMesher.WorldTintMode(Game.Content, block);
            Color tint = mode == 1 ? Game.WorldTintOf(block) : Color.black;
            bool own = mode != 1 || tint.r + tint.g + tint.b <= 0.01f; // the mesher's rule for a plant's own colours
            float glow = ChunkMesher.EmissionFor(Game.Content, block);
            bool hanging = BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.IsHanging(key);
            int hash = unchecked((cell.x * 92837111) ^ (cell.y * 689287499) ^ (cell.z * 283923481)); // the mesher's plant hash

            Mesh owned = null;
            GameObject body = null;
            if (GameLook.PlantRangeNow > 0f)
            {
                int accent = def.Accent && !own ? Mathf.RoundToInt(ChunkMesher.AccentCodeAt(cell.x, cell.z) * 10f) : 0;
                body = PlantBodies.CreateStandIn(block, hash, tint, own, accent, 1f, glow);
            }

            body ??= PictureStandIn(Game.Content, Game.Atlas, Game.ChunkMaterial, block, own ? Color.black : tint, own ? 0f : 1f, glow, out owned);
            if (body == null)
            {
                return null;
            }

            var root = new GameObject("FloraSprout");
            var at = Game.ScenePos(cell.x + 0.5f, cell.y + (hanging ? 1f : 0f), cell.z + 0.5f);
            root.transform.position = at;
            if (hanging)
            {
                root.transform.rotation = Quaternion.Euler(180f, 0f, 0f); // it grows down from the cell's ceiling
            }

            body.transform.SetParent(root.transform, false);
            var sprout = root.AddComponent<Sprout>();
            sprout.OwnedMesh = owned;
            sprout.Grow(Vector3.one * PlantFrom, Vector3.one * PlantTo, seconds);
            return sprout;
        }

        /// <summary>The stand-in where plants are pictures: the species' tile on two crossed planes (both sides — the chunk
        /// shader culls back faces), or on a slim column for a tile without holes (a solid plant form). In the chunk
        /// shader's channels: full skylight and the tint mode, the plant's cutout code and world colour, its glow.</summary>
        public static GameObject PictureStandIn(BlocksBeyondTheStars.Shared.Content.GameContent content, BlockTextureAtlas atlas,
            Material chunkMaterial, BlockId block, Color tint, float tintMode, float glow, out Mesh mesh)
        {
            mesh = null;
            if (atlas == null || chunkMaterial == null || content == null)
            {
                return null;
            }

            bool cutout = ChunkMesher.IsCutoutPlant(content, block);
            var geometry = new PictureGeometry(atlas.TileUv(block.Value));
            if (cutout)
            {
                geometry.CrossedPlanes();
            }
            else
            {
                geometry.Column();
            }

            mesh = geometry.ToMesh(content, block, cutout, tint, tintMode, glow);
            var go = new GameObject("PlantPicture");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = chunkMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>The buffers of a picture stand-in: quads wearing the species' tile (its rectangle in the atlas), then
        /// the finished mesh in the chunk shader's channels.</summary>
        private sealed class PictureGeometry
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _tris = new List<int>();
            private readonly Rect _uv;

            public PictureGeometry(Rect uv)
            {
                _uv = uv;
            }

            /// <summary>Two crossed planes, each drawn from both sides (the chunk shader culls back faces).</summary>
            public void CrossedPlanes()
            {
                const float w = 0.42f, h = 0.9f;
                for (int plane = 0; plane < 2; plane++)
                {
                    Vector3 across = plane == 0 ? Vector3.right : Vector3.forward;
                    Vector3 a = -across * w, b = across * w, up = Vector3.up * h;
                    Quad(a, b, b + up, a + up);
                    Quad(b, a, a + up, b + up);
                }
            }

            /// <summary>A slim column with a lid, for a tile without holes (a solid plant form).</summary>
            public void Column()
            {
                const float r = 0.2f, h = 0.8f;
                var corners = new[] { new Vector3(-r, 0f, -r), new Vector3(r, 0f, -r), new Vector3(r, 0f, r), new Vector3(-r, 0f, r) };
                var up = Vector3.up * h;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 a = corners[(side + 1) % 4], b = corners[side];
                    Quad(a, b, b + up, a + up);
                }

                Quad(corners[3] + up, corners[2] + up, corners[1] + up, corners[0] + up); // the lid
            }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                // a, b = the lower corners, c, d = the upper ones above b and a; the tile runs root (v = 0) to tip.
                int i = _verts.Count;
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
                for (int k = 0; k < 4; k++)
                {
                    _normals.Add(n);
                }

                _uvs.Add(new Vector2(_uv.xMin, _uv.yMin)); _uvs.Add(new Vector2(_uv.xMax, _uv.yMin));
                _uvs.Add(new Vector2(_uv.xMax, _uv.yMax)); _uvs.Add(new Vector2(_uv.xMin, _uv.yMax));
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
            }

            /// <summary>The mesh, in the chunk shader's channels: full skylight and the tint mode, the plant's cutout code
            /// and world colour, its material and glow.</summary>
            public Mesh ToMesh(BlocksBeyondTheStars.Shared.Content.GameContent content, BlockId block, bool cutout, Color tint,
                float tintMode, float glow)
            {
                var material = ChunkMesher.MaterialFor(content, block);
                int count = _verts.Count;
                var sky = new List<Vector2>(count);
                var leaf = new List<Vector4>(count);
                var zero = new List<Vector4>(count);
                var colors = new List<Color>(count);
                var tangents = new List<Vector4>(count);
                for (int i = 0; i < count; i++)
                {
                    sky.Add(new Vector2(1f, tintMode));
                    leaf.Add(new Vector4(cutout ? ChunkMesher.LeafCodePlant : 0f, tint.r, tint.g, tint.b));
                    zero.Add(Vector4.zero);
                    colors.Add(new Color(material.x, material.y, 1f, glow));
                    tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                var mesh = new Mesh { name = "FloraSprout picture" };
                mesh.SetVertices(_verts);
                mesh.SetNormals(_normals);
                mesh.SetTangents(tangents);
                mesh.SetUVs(0, _uvs);
                mesh.SetUVs(1, sky);
                mesh.SetUVs(2, leaf);
                mesh.SetUVs(3, zero);
                mesh.SetUVs(4, zero);
                mesh.SetColors(colors);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Material _mat;

        private static Material SproutMaterial()
        {
            if (_mat != null)
            {
                return _mat;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            _mat = new Material(shader) { color = ShaderColor.Srgb(new Color(0.36f, 0.62f, 0.26f)) };
            return _mat;
        }

        /// <summary>One growing sprout: two crossed quads on a base-pivoted root that scales up from a seedling
        /// to full height over the regrow delay, then self-removes (the real plant takes over).</summary>
        private sealed class Sprout : MonoBehaviour
        {
            public System.Action OnFinished;

            /// <summary>The block key of the plant that regrows here (#2532: tells a hanging plant from a standing one).</summary>
            public string PlantKey;

            /// <summary>A mesh built for this sprout alone (the picture stand-in); freed with it.</summary>
            public Mesh OwnedMesh;

            private float _seconds;
            private float _t;
            private Vector3 _from = Vector3.one; // the root's scale at the start …
            private Vector3 _to = Vector3.one;   // … and when the plant is due

            public static Sprout Spawn(Vector3Int cell, float seconds, float width, float height, float startFraction)
            {
                // The cell is the air cell above the host, so the sprout's base sits on the ground at cell.Y.
                var root = new GameObject("FloraSprout");
                root.transform.position = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);

                var mat = SproutMaterial();
                for (int i = 0; i < 2; i++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    var col = quad.GetComponent<Collider>();
                    if (col != null)
                    {
                        Destroy(col);
                    }

                    quad.transform.SetParent(root.transform, false);
                    quad.transform.localPosition = new Vector3(0f, 0.5f, 0f); // base at the root origin (the ground)
                    quad.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                    quad.GetComponent<Renderer>().sharedMaterial = mat;
                    quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                var sprout = root.AddComponent<Sprout>();
                sprout.Grow(new Vector3(width, height * startFraction, width), new Vector3(width, height, width), seconds);
                return sprout;
            }

            /// <summary>Grows the root from one scale to another over <paramref name="seconds"/>.</summary>
            public void Grow(Vector3 from, Vector3 to, float seconds)
            {
                _from = from;
                _to = to;
                Restart(seconds);
            }

            private void OnDestroy()
            {
                if (OwnedMesh != null)
                {
                    Destroy(OwnedMesh);
                }
            }

            public void Restart(float seconds)
            {
                _seconds = seconds;
                _t = 0f;
                Apply();
            }

            private void Update()
            {
                _t += Time.deltaTime;
                Apply();
                if (_t >= _seconds)
                {
                    OnFinished?.Invoke();
                    Destroy(gameObject);
                }
            }

            private void Apply()
            {
                transform.localScale = Vector3.Lerp(_from, _to, _seconds > 0f ? Mathf.Clamp01(_t / _seconds) : 1f);
            }
        }
    }
}
