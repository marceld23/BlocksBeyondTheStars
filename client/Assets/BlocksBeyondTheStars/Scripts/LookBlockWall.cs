// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's "as in the world" picture of a block tile: a small wall of 3 × 3 blocks (a
    /// plant: three in a row) meshed by the REAL chunk mesher on a tiny chunk and drawn with the block shader, so it
    /// shows what the world does to the tile — the ground kinds' turned and toned variants, the relief from the tile, the
    /// soft edges and joints, the fine grain, the texture calm, and for plant, leaf and bark tiles the world's colour
    /// (three example worlds to click through, <see cref="LookPreviewCells.ExampleTint"/>).
    /// <para>
    /// The tile being painted shows live: the wall draws from its own <see cref="LookPreviewAtlas"/>, whose cell of the
    /// block holds the editor's current pixels and whose variant cells are derived from them; the other cells the wall
    /// uses (a log's end grain, a face tile) are copied from the world atlas. The world atlas itself is never written.
    /// An animated tile shows the frame being painted. A block the mesher draws see-through (water, glass) or not at all
    /// reports false, and the caller keeps its classic preview.
    /// </para>
    /// </summary>
    internal sealed class LookBlockWall
    {
        /// <summary>Seconds between two looks at the editor's pixels while painting (each change re-uploads the preview
        /// atlas: 2 × 22 MB on a desktop, 2 × 5.5 MB elsewhere).</summary>
        private const float RefreshEvery = 0.35f;

        private readonly GameObject _go;
        private readonly MeshFilter _filter;
        private readonly MeshRenderer _renderer;
        private Material _material;
        private LookPreviewAtlas _atlas;
        private BlockTextureAtlas _world;
        private Mesh _mesh;
        private ushort _blockId;
        private readonly List<int> _slots = new List<int>();
        private byte[] _shownPixels;
        private float _nextLook;

        public LookBlockWall(Transform parent)
        {
            _go = new GameObject("LookBlockWall", typeof(MeshFilter), typeof(MeshRenderer));
            _go.transform.SetParent(parent, false);
            _filter = _go.GetComponent<MeshFilter>();
            _renderer = _go.GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _go.SetActive(false);
        }

        /// <summary>True while the wall is shown.</summary>
        public bool Shown => _go != null && _go.activeSelf;

        /// <summary>True when the wall shows faces the world tints (plants, crowns, bark): the example colours apply.</summary>
        public bool WorldTinted { get; private set; }

        /// <summary>The example world (0..2) whose colour the tinted faces show.</summary>
        public int Example { get; private set; }

        /// <summary>The orthographic half height that frames the wall.</summary>
        public float FrameSize { get; private set; } = 2.3f;

        /// <summary>The mip of the preview atlas that holds one texel per cell (for <see cref="LookPreviewLight.MeanMip"/>).</summary>
        public float MeanMip => _atlas != null ? _atlas.MeanMip : 6f;

        /// <summary>The world colour plants show where their faces carry none (<see cref="LookPreviewLight.FloraTint"/>).</summary>
        public Vector4 FloraTint
        {
            get
            {
                var (r, g, b) = LookPreviewCells.ExampleTint(Example, "flora_plant");
                Color c = ShaderColor.Srgb(new Color(r, g, b, 1f));
                return new Vector4(c.r, c.g, c.b, 1f);
            }
        }

        /// <summary>Builds the wall for <paramref name="entry"/> from the editor's <paramref name="live"/> pixels. False
        /// (and hidden) when the entry is no block tile the wall can show — the caller keeps its classic preview.</summary>
        public bool Show(TextureEntry entry, GameContent content, Texture2D live)
        {
            Hide();
            if (entry == null || entry.Block == null || entry.IsIcon || content == null || live == null
                || entry.Preview == TexturePreviewKind.ShapedPicture)
            {
                return false;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (shader == null)
            {
                return false; // stripped from this build: the classic preview stays
            }

            EnsureResources(content, shader);
            _blockId = entry.Block.NumericId.Value;
            if (!BuildMesh(content, entry.Preview == TexturePreviewKind.Billboard))
            {
                return false;
            }

            _shownPixels = null;
            WriteCells(Pixels(live), all: true);
            _go.SetActive(true);
            return true;
        }

        /// <summary>Moves on to the next example world colour; the caller shows the wall again (the colour rides in the
        /// mesh, so it is rebuilt).</summary>
        public void NextExample() => Example = (Example + 1) % LookPreviewCells.ExampleCount;

        /// <summary>Looks at the editor's pixels now and then and re-uploads the block's cells when they changed.</summary>
        public void Tick(Texture2D live)
        {
            if (!Shown || live == null || Time.unscaledTime < _nextLook)
            {
                return;
            }

            _nextLook = Time.unscaledTime + RefreshEvery;
            var pixels = Pixels(live);
            if (_shownPixels != null && SameBytes(pixels, _shownPixels))
            {
                return;
            }

            WriteCells(pixels, all: false);
        }

        public void Hide()
        {
            if (_go != null)
            {
                _go.SetActive(false);
            }
        }

        public void Destroy()
        {
            EditorSceneKit.Discard(_mesh);
            EditorSceneKit.Discard(_material);
            _atlas?.Destroy();
            _world?.Release();
            _mesh = null;
            _material = null;
            _atlas = null;
            _world = null;
        }

        private void EnsureResources(GameContent content, Shader shader)
        {
            if (_world == null || _world.Content != content)
            {
                _world?.Release();
                _world = BlockTextureAtlas.Acquire(content); // shared and already built where the editor runs
            }

            _atlas ??= new LookPreviewAtlas();
            if (_material == null)
            {
                _material = new Material(shader) { name = "LookBlockWall", mainTexture = _atlas.Colour };
                _material.SetTexture("_NormalTex", _atlas.Normal);
                if (_atlas.Height != null)
                {
                    _material.SetTexture("_HeightTex", _atlas.Height);
                }

                _renderer.sharedMaterials = new[] { _material, _material, _material };
            }
        }

        /// <summary>The wall (or row of plants) through the chunk mesher, against the world atlas's slots; animation codes
        /// stripped, the slots it uses noted. False when nothing opaque came out.</summary>
        private bool BuildMesh(GameContent content, bool plant)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            var id = new BlockId(_blockId);
            int rows = plant ? 1 : 3;
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    chunk.Set(x, y, 0, id);
                }
            }

            const int n = WorldConstants.ChunkSize;
            BlockId World(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < n && y < n && z < n ? chunk.Get(x, y, z) : BlockId.Air;
            Color Tint(BlockId block)
            {
                var def = content.BlockById(block);
                if (def == null || !LookPreviewCells.TakesWorldTint(def.Key))
                {
                    return Color.black; // the mesher's "no species colour": the tile keeps its own (or the global flora hue)
                }

                var (r, g, b) = LookPreviewCells.ExampleTint(Example, def.Key);
                return ShaderColor.Srgb(new Color(r, g, b, 1f)); // linear, as GameBootstrap.RebuildFloraTints hands it over
            }

            var data = ChunkMesher.BuildGeometry(chunk, content, World, _world, floraTint: Tint);
            bool ok = data.OpaqueTris.Count > 0 && data.TransparentTris.Count == 0;
            if (ok)
            {
                NoteSlotsAndStill(data);
                data.Pack(); // again: the tint modes changed after the mesher packed
                EditorSceneKit.Discard(_mesh);
                _mesh = data.ToMeshes(buildCollider: false).Render;
                _mesh.name = "LookBlockWall";
                _filter.sharedMesh = _mesh;
                var centre = new Vector3(1.5f, rows * 0.5f, 0.5f);
                _go.transform.localPosition = -centre;
                FrameSize = plant ? 1.6f : 2.3f;
            }

            data.Release();
            return ok;
        }

        private void NoteSlotsAndStill(ChunkMeshData data)
        {
            _slots.Clear();
            var seen = new HashSet<int>();
            WorldTinted = false;
            for (int i = 0; i < data.Uvs.Count; i++)
            {
                Vector2 uv = data.Uvs[i];
                int slot = LookPreviewCells.SlotOf(uv.x, uv.y);
                if (seen.Add(slot))
                {
                    _slots.Add(slot);
                }

                Vector2 sky = data.SkyUv[i];
                float mode = LookPreviewCells.StillTintMode(sky.y);
                data.SkyUv[i] = new Vector2(sky.x, mode);
                Vector4 leaf = data.LeafUv[i];
                WorldTinted |= (mode > 0.5f && mode < 1.5f) || (mode > 3.5f && mode < 4.5f && (leaf.y + leaf.z + leaf.w) > 0.01f);
            }
        }

        /// <summary>Writes the block's own cell from <paramref name="live"/>, its variants derived from it the way the world
        /// atlas derives them, and (once, <paramref name="all"/>) every other cell the wall uses from the world atlas.</summary>
        private void WriteCells(byte[] live, bool all)
        {
            if (_atlas == null || _world == null || live == null)
            {
                return;
            }

            bool calm = _world.PreviewIsCalmSlot(_blockId);
            // What is painted here will be saved as a painted tile, and a painted tile keeps its picture
            // in the world — its ground variants are the painting with a tone step, not mirrored and without cracks.
            bool painted = true;
            foreach (int slot in _slots)
            {
                byte[] tile;
                int variant = _world.PreviewVariantIndex(_blockId, slot, out bool flora);
                if (slot == _blockId)
                {
                    tile = live;
                }
                else if (variant >= 0)
                {
                    // A plant's variants are painted along its silhouette in the world; here they wear the live tile as it
                    // is (the small darker / lighter step is left out), a ground block's are derived exactly.
                    tile = flora ? live : LookPreviewCells.GroundVariant(live, _blockId, variant, calm, GameLook.TileToneNow, painted);
                }
                else if (all)
                {
                    tile = _world.PreviewSlotPixels(slot);
                }
                else
                {
                    continue;
                }

                _atlas.Write(slot, tile);
            }

            _atlas.Apply();
            _shownPixels = live;
        }

        private static byte[] Pixels(Texture2D live)
        {
            var px = live.GetPixels32();
            var raw = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                raw[i * 4] = px[i].r;
                raw[(i * 4) + 1] = px[i].g;
                raw[(i * 4) + 2] = px[i].b;
                raw[(i * 4) + 3] = px[i].a;
            }

            return raw;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
