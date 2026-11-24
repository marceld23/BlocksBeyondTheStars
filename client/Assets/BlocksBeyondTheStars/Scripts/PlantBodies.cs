// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One small plant the mesher recorded for <see cref="PlantBodies"/>: where it stands, which
    /// species it is, its colour and light, and the per-plant rolls the crossed picture uses too.</summary>
    public struct PlantCell
    {
        public Vector3 Local;   // the cell's min corner, chunk-local
        public ushort Block;    // the species' block id
        public int Hash;        // the world cell's hash: picks the individual and its turn
        public Color Tint;      // the species' colour on this world (shader-ready, with the cell's small jitter)
        public int Accent;      // which of the species' three accent colours the plant wears (0 = none)
        public float Size;      // the plant's size roll (1 = the species' own size)
        public float Width;     // a solid plant only: its width factor (Size is its height factor) - the scale of its collider
        public Vector2 Lean;    // how far its top leans (x, z)
        public Vector2 Jitter;  // its offset from the cell's middle (x, z)
        public float Sky;       // skylight 0..1
        public Vector3 Light;   // the placed block light reaching the cell
        public int Column;      // the world column's hash: a stacked strand keeps one turn and one individual up its cells
        public float Emission;  // the plant's own glow (0 = none)
        public byte Flags;      // the bits below

        public const byte Hanging = 1;   // it roots in the block above and grows down (the body is turned over)
        public const byte Aquatic = 2;   // the water surrounds it: it sways as in a current, never sheltered
        public const byte OwnColour = 4; // it keeps the colours of its own tile (a crop, an authored plant) instead of a tint
        public const byte Continues = 8; // the same plant fills the next cell toward its tip (a stacked strand)
        public const byte Exact = 16;    // its size is given, not rolled (a part of a bred plant, the flower in a pot)
        public const byte Solid = 32;    // a solid plant (cactus, cap, crystal): scaled like its collider, never leaned
        public const byte DyedBlossom = 64; // #4012: a dyed flower — its blossom wears the dye (Tint), its leaves the tile's own colour
    }

    /// <summary>
    /// Small plants as bodies (<see cref="GameLook.PlantBodies"/>). The chunk mesh keeps
    /// every plant as its crossed picture (the far picture). Near the camera this component draws the plant's real
    /// body instead — a rosette of leaves, a flower on round stems, a bush of small cubes (<see cref="PlantForms"/>) —
    /// as GPU instances, one draw per species individual and chunk; the shaders cross-fade the two by a screen-door
    /// dither over the last metres of the range. The pattern of <see cref="CrownClusters"/> and
    /// <see cref="GrassClumps"/>: the mesher records points, nothing here is in the chunk mesh.
    /// </summary>
    public sealed partial class PlantBodies : MonoBehaviour
    {
        /// <summary>What the mesher adds to the cutout code of a plant that has a body: the block shader then hides the
        /// crossed picture near the eye (AtmosphereCommon.hlsl, BbtsStagedPlant). Small enough to leave the code's
        /// meaning and the accent pick in its tenths untouched.</summary>
        public const float StagedMark = 0.04f;

        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard cap per call

        private static readonly int TileId = Shader.PropertyToID("_PbTile");
        private static readonly int TintId = Shader.PropertyToID("_PbTint");
        private static readonly int LightId = Shader.PropertyToID("_PbLight");
        private static readonly int AccentId = Shader.PropertyToID("_PbAccent");
        private static readonly int WoodId = Shader.PropertyToID("_Sc_LookWood");

        private static readonly Dictionary<int, Kind> Species = new Dictionary<int, Kind>();
        private static readonly Dictionary<int, Vector2> TileCentres = new Dictionary<int, Vector2>();
        private static readonly Dictionary<int, Palette> Palettes = new Dictionary<int, Palette>();
        private static GameContent _content;
        private static long _worldSeed;
        private static string _location = string.Empty;
        private static int _worldVersion;
        private static BlockTextureAtlas _atlas;
        private static int _atlasVersion;
        private static Material _material;
        private static Camera _cam;
        private static int _camFrame = -1;
        private static Vector3 _camPos;
        private static float _range;
        private static float _shadowRange;
        private static int _frameBodies;

        private readonly List<PlantCell> _cells = new List<PlantCell>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<int, int> _slotOf = new Dictionary<int, int>();
        private bool _dirty = true;
        private int _bakedWorld = -1;
        private int _bakedAtlas = -1;
        private Vector3 _bakedPos;
        private MeshRenderer _chunkRenderer;

        /// <summary>The world the plants grow on: its seed and location roll every species' form, and its wood colour is
        /// the stems' and twigs' (shader-ready). A new world drops every built form; the chunks re-bake on their next draw.</summary>
        public static void UseWorld(GameContent content, long worldSeed, string location, Color wood)
        {
            Shader.SetGlobalVector(WoodId, new Vector4(wood.r, wood.g, wood.b, 1f));
            EnsureFarDriver(); // the far pictures tick on their own: on Low no chunk carries bodies (PlantBodies.FarPictures.cs)
            location ??= string.Empty;
            if (ReferenceEquals(content, _content) && worldSeed == _worldSeed && location == _location)
            {
                return;
            }

            _content = content;
            _worldSeed = worldSeed;
            _location = location;
            _worldVersion++;
            Palettes.Clear(); // a world repaints the tiles of its plant looks
            foreach (var kind in Species.Values)
            {
                for (int i = 0; kind != null && i < kind.Meshes.Length; i++)
                {
                    if (kind.Meshes[i].Mesh != null)
                    {
                        Destroy(kind.Meshes[i].Mesh);
                    }
                }
            }

            Species.Clear();
        }

        /// <summary>The world's block atlas: a body reads the brightness of its species' own tile from it, so it matches
        /// the far picture with every texture pack. A new atlas re-bakes every chunk.</summary>
        public static void UseAtlas(BlockTextureAtlas atlas)
        {
            if (ReferenceEquals(atlas, _atlas))
            {
                return;
            }

            _atlas = atlas;
            _atlasVersion++;
            TileCentres.Clear();
            Palettes.Clear();
        }

        /// <summary>True once bodies can be drawn (the world, the atlas and the shader are there). Until then the range
        /// published to the shaders stays 0, so the crossed pictures are never hidden without a body to take their place.</summary>
        public static bool Ready => _atlas != null && _content != null && BodyMaterial() != null;

        /// <summary>Takes a chunk's plant cells (copied — the caller's list goes back to the mesh pool). Empty input
        /// disables the component.</summary>
        public void Setup(List<PlantCell> cells)
        {
            _cells.Clear();
            if (cells != null)
            {
                _cells.AddRange(cells);
            }

            _dirty = true;
            enabled = GameLook.PlantBodies && _cells.Count > 0;
        }

        private void Awake()
        {
            _chunkRenderer = GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            if (!TickFrame() || _range <= 0f || _atlas == null || _content == null || _chunkRenderer == null || !_chunkRenderer.enabled)
            {
                return; // no camera, a preset without bodies, or a chunk the cull hides
            }

            var material = BodyMaterial();
            if (material == null)
            {
                return;
            }

            var pos = transform.position;
            float distSq = DistanceSqToChunk(_camPos, pos);
            if (distSq > (_range + 1f) * (_range + 1f))
            {
                return;
            }

            // A chunk drawn for its shadow only (behind rock for the eye, but its shadow may reach the view) casts only.
            bool shadowsOnly = _chunkRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
            bool castsNear = distSq < (_shadowRange + 1f) * (_shadowRange + 1f);
            if (shadowsOnly && !castsNear)
            {
                return;
            }

            if (pos != _bakedPos)
            {
                _bakedPos = pos; // first draw, or the chunk moved across the world's seam
                _dirty = true;
            }

            if (_dirty || _bakedWorld != _worldVersion || _bakedAtlas != _atlasVersion)
            {
                Build();
            }

            var cast = shadowsOnly ? ShadowCastingMode.ShadowsOnly : castsNear ? ShadowCastingMode.On : ShadowCastingMode.Off;
            int layer = gameObject.layer;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                for (int b = 0; b < slot.Used; b++)
                {
                    slot.Batches[b].Draw(slot.Mesh, material, cast, layer);
                    _frameBodies += slot.Batches[b].Count;
                }
            }
        }

        /// <summary>The once-per-frame work shared by every chunk: the camera and the bodies' range.</summary>
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

                GameLook.AdaptPlantRange(_frameBodies); // what the last frame submitted steers this frame's range
                _frameBodies = 0;
                _range = Ready ? GameLook.PlantRangeNow : 0f;
                _shadowRange = GameLook.PlantShadowNow;
            }

            return _cam != null;
        }

        private static Material BodyMaterial()
        {
            if (_material == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/PlantBody");
                if (shader == null)
                {
                    return null;
                }

                _material = new Material(shader) { name = "PlantBody", enableInstancing = true };
            }

            if (_atlas != null && _material.mainTexture != _atlas.Texture)
            {
                _material.mainTexture = _atlas.Texture;
            }

            return _material;
        }

        /// <summary>Squared distance from a point to the chunk's box (a tall plant stands up to two blocks over it).</summary>
        private static float DistanceSqToChunk(Vector3 p, Vector3 origin)
        {
            float n = WorldConstants.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(origin.x - p.x, p.x - (origin.x + n)));
            float dy = Mathf.Max(0f, Mathf.Max(origin.y - p.y, p.y - (origin.y + n + 2f)));
            float dz = Mathf.Max(0f, Mathf.Max(origin.z - p.z, p.z - (origin.z + n)));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        /// <summary>One instance per plant, sorted by species individual. Size, lean and offset are the rolls the crossed
        /// picture uses; the turn is the plant's own (a bush keeps to quarter turns, its cubes stay square to the world).</summary>
        private void Build()
        {
            _dirty = false;
            _bakedWorld = _worldVersion;
            _bakedAtlas = _atlasVersion;
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Used = 0;
                _slots[i].Current = null;
            }

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var kind = KindFor(cell.Block);
                if (kind == null)
                {
                    continue;
                }

                uint hash = (uint)cell.Hash;
                uint column = (uint)cell.Column;
                bool hanging = (cell.Flags & PlantCell.Hanging) != 0;
                bool exact = (cell.Flags & PlantCell.Exact) != 0;
                bool solid = (cell.Flags & PlantCell.Solid) != 0;
                bool upright = solid || kind.Straight || kind.Family == PlantForms.Lily; // never rolled in size, never leaned
                int variant = VariantOf(kind, cell, hash, column);
                var built = kind.Meshes[variant];
                if (built.Mesh == null)
                {
                    continue;
                }

                float size = InstanceSize(cell, exact, upright, built.Radius);
                var turn = BodyTurn(kind, cell, hash, column, hanging, upright);
                var root = _bakedPos + cell.Local + new Vector3(0.5f + cell.Jitter.x, 0f, 0.5f + cell.Jitter.y);
                if (hanging)
                {
                    root.y += 1f; // it hangs from the cell's ceiling, turned over
                    turn *= Quaternion.Euler(180f, 0f, 0f);
                }

                float phase = (((hash >> 19) & 0xFFu) / 256f) + ((cell.Flags & PlantCell.Aquatic) != 0 ? 2f : 0f);
                BodyColours(cell, kind, phase, out var tint, out var accent, out float pick);
                var batch = OpenBatch(SlotFor((cell.Block * PlantForms.Variants) + variant, built.Mesh));
                var scale = solid ? new Vector3(cell.Width, cell.Size, cell.Width) : new Vector3(size, size, size);
                batch.Add(Matrix4x4.TRS(root, turn, scale),
                    new Vector4(0f, 0f, cell.Sky, pick), tint,
                    new Vector4(cell.Light.x, cell.Light.y, cell.Light.z, cell.Emission), accent);
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Current?.Commit();
            }
        }

        /// <summary>The individual a plant is drawn as. A stacked strand: individuals 2 and 3 run through the cell, 0 and
        /// 1 end in a tip; one individual and one turn per column, so the segments of a strand join.</summary>
        private static int VariantOf(Kind kind, PlantCell cell, uint hash, uint column)
            => kind.Straight
                ? ((cell.Flags & PlantCell.Continues) != 0 ? 2 : 0) + (int)(column % 2u)
                : (int)(hash % PlantForms.Variants);

        /// <summary>One size for the whole body (balls stay round), small enough to keep the plant inside its cell; an
        /// upright plant (solid, straight strand, lily) keeps its built size.</summary>
        private static float InstanceSize(PlantCell cell, bool exact, bool upright, float radius)
        {
            float size = 1f;
            if (!upright)
            {
                size = exact ? Mathf.Clamp(cell.Size, 0.3f, 1.6f) : Mathf.Clamp(cell.Size, 0.55f, 1.5f);
                size = Mathf.Max(exact ? 0.3f : 0.5f, Mathf.Min(size, (0.56f - cell.Jitter.magnitude) / Mathf.Max(radius, 0.1f)));
            }

            return size;
        }

        /// <summary>The plant's turn about its root: a bush in quarter turns (its cubes stay square to the world), a
        /// straight strand one turn per column (its stacked segments join), every other plant its own turn and, where it
        /// stands free on the ground, its lean.</summary>
        private static Quaternion BodyTurn(Kind kind, PlantCell cell, uint hash, uint column, bool hanging, bool upright)
        {
            Quaternion turn;
            if (kind.Family == PlantForms.Bush)
            {
                turn = Quaternion.Euler(0f, 90f * ((hash >> 9) & 3u), 0f); // its cubes stay square to the world
            }
            else if (kind.Straight)
            {
                turn = Quaternion.Euler(0f, 360f * (((column >> 9) & 0x3FFu) / 1024f), 0f);
            }
            else
            {
                turn = Quaternion.Euler(0f, 360f * (((hash >> 9) & 0x3FFu) / 1024f), 0f);
                if (!hanging && !upright && kind.Family != PlantForms.Cushion && kind.Family != PlantForms.Coral)
                {
                    turn = Quaternion.FromToRotation(Vector3.up, new Vector3(cell.Lean.x, 1f, cell.Lean.y).normalized) * turn;
                }
            }

            return turn;
        }

        /// <summary>The colours: a plant that keeps its own wears its tile's two colours; every other one is tinted, and
        /// then the tile gives only the brightness and hue of its leaves (the picture's recipe needs them).
        /// <paramref name="pick"/> is the accent the shader picks, or 8 for "these are the colours themselves".</summary>
        private static void BodyColours(PlantCell cell, Kind kind, float phase, out Vector4 tint, out Vector4 accent, out float pick)
        {
            pick = cell.Accent;
            var palette = PaletteFor(cell.Block, kind.Family == PlantForms.Fruit);
            if ((cell.Flags & PlantCell.OwnColour) != 0)
            {
                tint = new Vector4(palette.Main.r, palette.Main.g, palette.Main.b, phase);
                accent = new Vector4(palette.Accent.r, palette.Accent.g, palette.Accent.b, 1f);
                pick = 8f; // the shader's mark for "these are the colours themselves"
            }
            else if ((cell.Flags & PlantCell.DyedBlossom) != 0)
            {
                // #4012: a dyed flower keeps the leaves of its own tile and wears the dye on the blossom — given as its own
                // colours, so neither the planet's tint nor the season turns a planted picture.
                var dye = ShaderColor.Srgb(cell.Tint);
                tint = new Vector4(palette.Main.r, palette.Main.g, palette.Main.b, phase);
                accent = new Vector4(dye.r, dye.g, dye.b, 1f);
                pick = 8f;
            }
            else
            {
                tint = new Vector4(cell.Tint.r, cell.Tint.g, cell.Tint.b, phase);
                accent = new Vector4(palette.Main.r, palette.Main.g, palette.Main.b, 0f);
            }
        }

        /// <summary>The batch of <paramref name="slot"/> the next instance goes into: its current one, or — when that is
        /// full or none is open yet — the next one (committing the full one first; batches are reused across rebuilds).</summary>
        private static Batch OpenBatch(Slot slot)
        {
            var batch = slot.Current;
            if (batch == null || batch.Count == BatchSize)
            {
                batch?.Commit();
                if (slot.Used == slot.Batches.Count)
                {
                    slot.Batches.Add(new Batch());
                }

                batch = slot.Current = slot.Batches[slot.Used++];
                batch.Begin();
            }

            return batch;
        }

        private Slot SlotFor(int key, Mesh mesh)
        {
            if (!_slotOf.TryGetValue(key, out int index))
            {
                index = _slots.Count;
                _slots.Add(new Slot());
                _slotOf[key] = index;
            }

            var slot = _slots[index];
            slot.Mesh = mesh;
            return slot;
        }

        /// <summary>A species on this world: its family, its individuals, and whether it is a straight strand.</summary>
        private sealed class Kind
        {
            public int Family;
            public bool Straight;
            public PlantForms.Built[] Meshes;
        }

        /// <summary>The colours an own-colour species wears, read from its tile.</summary>
        private struct Palette
        {
            public Color Main;
            public Color Accent;
        }

        /// <summary>The species of a block on this world (built on first use), or null for a block that has no body. A
        /// species that keeps its own colours (a crop, an authored plant) keeps one form on every world too.</summary>
        private static Kind KindFor(ushort block)
        {
            if (Species.TryGetValue(block, out var kind))
            {
                return kind;
            }

            var def = _content.BlockById(new BlockId(block));
            string key = def?.Key;
            int family = key != null ? PlantForms.FamilyOf(key) : -1;
            if (family >= 0)
            {
                bool fixedForm = FloraCatalog.KeepsOwnColour(key);
                kind = new Kind
                {
                    Family = family,
                    Straight = family == PlantForms.Strand && PlantForms.StandsStraight(key),
                    Meshes = new PlantForms.Built[PlantForms.Variants],
                };
                for (int variant = 0; variant < PlantForms.Variants; variant++)
                {
                    kind.Meshes[variant] = PlantForms.Build(family, key, FloraCatalog.IsTall(key), def.Accent,
                        fixedForm ? 0L : _worldSeed, fixedForm ? "fixed" : _location, variant);
                }
            }

            Species[block] = kind; // null too: asked once
            return kind;
        }

        /// <summary>Regrow marker: one plant body as a GameObject of its own — the species' mesh on this world
        /// (the individual and the turn the plant of that cell gets, by the mesher's <paramref name="hash"/>), on the body
        /// material, with the values a chunk hands every plant: the species' colour on this world or, for a plant that
        /// keeps its own (<paramref name="ownColour"/>), the colours of its tile; which accent colour it wears; the
        /// cell's skylight; its own glow. The caller places, scales and destroys it; the mesh is shared and stays.
        /// Null for a block without a body, and while bodies cannot be drawn.</summary>
        public static GameObject CreateStandIn(BlockId block, int hash, Color speciesTint, bool ownColour, int accent, float sky, float glow)
        {
            if (!Ready)
            {
                return null;
            }

            var kind = KindFor(block.Value);
            if (kind == null)
            {
                return null;
            }

            uint h = (uint)hash;
            var built = kind.Meshes[kind.Straight ? (int)(h & 1u) : (int)(h % PlantForms.Variants)];
            if (built.Mesh == null)
            {
                return null;
            }

            var go = new GameObject("PlantStandIn");
            go.transform.localRotation = kind.Family == PlantForms.Bush
                ? Quaternion.Euler(0f, 90f * ((h >> 9) & 3u), 0f)
                : Quaternion.Euler(0f, 360f * (((h >> 9) & 0x3FFu) / 1024f), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = built.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BodyMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var palette = PaletteFor(block.Value, kind.Family == PlantForms.Fruit);
            float phase = ((h >> 19) & 0xFFu) / 256f;
            var props = new MaterialPropertyBlock();
            if (ownColour)
            {
                props.SetVector(TileId, new Vector4(0f, 0f, sky, 8f)); // 8 = the two colours given are the plant's own
                props.SetVector(TintId, new Vector4(palette.Main.r, palette.Main.g, palette.Main.b, phase));
                props.SetVector(AccentId, new Vector4(palette.Accent.r, palette.Accent.g, palette.Accent.b, 1f));
            }
            else
            {
                props.SetVector(TileId, new Vector4(0f, 0f, sky, accent));
                props.SetVector(TintId, new Vector4(speciesTint.r, speciesTint.g, speciesTint.b, phase));
                props.SetVector(AccentId, new Vector4(palette.Main.r, palette.Main.g, palette.Main.b, 0f));
            }

            props.SetVector(LightId, new Vector4(0f, 0f, 0f, glow));
            renderer.SetPropertyBlock(props);
            return go;
        }

        /// <summary>Debris: the two colours the body of a plant block wears on this world (linear) — its main
        /// colour and its lighter second colour or accent, by the recipe of PlantBody.shader at the middle of a part's
        /// ramp — so the pieces of a broken plant match the body that stood there and not the old crossed picture.
        /// <paramref name="speciesTint"/> is the block's colour on this world, <paramref name="ownColour"/> a plant that
        /// keeps its tile's colours. False for a block without a body, and before a world's atlas is known.</summary>
        public static bool TryBodyColours(BlockId id, Color speciesTint, bool ownColour, out Color main, out Color second)
        {
            main = second = Color.black;
            string key = _atlas != null && _content != null ? _content.BlockById(id)?.Key : null;
            int family = key != null ? PlantForms.FamilyOf(key) : -1;
            if (family < 0)
            {
                return false;
            }

            var palette = PaletteFor(id.Value, family == PlantForms.Fruit);
            var leaf = new Vector3(palette.Main.r, palette.Main.g, palette.Main.b);
            var lumW = new Vector3(0.299f, 0.587f, 0.114f);
            Vector3 a, b;
            if (ownColour)
            {
                a = leaf * 1.1f;
                b = new Vector3(palette.Accent.r, palette.Accent.g, palette.Accent.b);
            }
            else
            {
                var species = new Vector3(speciesTint.r, speciesTint.g, speciesTint.b);
                a = Vector3.Lerp(leaf, Vector3.Dot(leaf, lumW) * species * 1.6f, 0.85f) * 1.1f;
                b = Vector3.Lerp(a, Vector3.Dot(a, lumW) * new Vector3(1.30f, 1.22f, 0.55f), 0.45f) * 1.25f;
            }

            const float ramp = 0.9f; // a part runs from 0.5 in its depth to 1.1 at its tips; the pieces sit above the middle
            main = new Color(a.x * ramp, a.y * ramp, a.z * ramp, 1f);
            second = new Color(b.x * ramp, b.y * ramp, b.z * ramp, 1f);
            return true;
        }

        /// <summary>The colours of a species' own tile, read from its pixels. What the crossed picture shows as blossom or
        /// berry (bright, saturated and not green, the test of FloraAccent.hlsl) is the accent colour; the main colour
        /// is the colour of its LEAVES — the brightest 30 % of the other pixels. The tile's plain average is far darker
        /// than what the eye reads as the plant: twigs, shading and outlines pull it down, and bodies coloured by it
        /// came out nearly black. And the tiles are painted
        /// at very different levels — measured over the 115 plant tiles the leaves run from 2 % (ember bloom, cinder
        /// bush, ash weed, thorn bush, coral) over 25 % (the middle) to 90 % (the snow plants) — so both colours are
        /// brought into a readable band: a body is never darker than a dark green and never blown out. Form without
        /// colour: how dark a tile was painted must not decide whether a plant can be seen. A fruit is one colour, the
        /// brightest part of all of its tile (<paramref name="whole"/>).</summary>
        private static Palette PaletteFor(int block, bool whole)
        {
            int key = (block * 2) + (whole ? 1 : 0);
            if (Palettes.TryGetValue(key, out var palette))
            {
                return palette;
            }

            byte[] px = _atlas.ReadTile((ushort)block);
            int texels = px.Length / 4;
            var colours = new Vector3[texels];
            var leaf = new float[texels];   // how much of the texel is leaf (1) and not blossom (0); -1 = a hole
            var light = new float[texels];
            var sorted = new List<float>(texels);
            Vector3 accent = Vector3.zero;
            float accentWeight = 0f;
            for (int i = 0; i < texels; i++)
            {
                int o = i * 4;
                if (px[o + 3] < 128)
                {
                    leaf[i] = -1f; // a hole of the cutout
                    continue;
                }

                float r = px[o] / 255f, g = px[o + 1] / 255f, b = px[o + 2] / 255f;
                float max = Mathf.Max(r, Mathf.Max(g, b)), min = Mathf.Min(r, Mathf.Min(g, b));
                float saturation = max > 1e-4f ? (max - min) / max : 0f;
                float mask = whole ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.5f, saturation))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.55f, max))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.06f, g - Mathf.Max(r, b))));
                var colour = ShaderColor.Srgb(new Color(r, g, b));
                colours[i] = new Vector3(colour.r, colour.g, colour.b);
                leaf[i] = 1f - mask;
                light[i] = (0.299f * colour.r) + (0.587f * colour.g) + (0.114f * colour.b);
                accent += colours[i] * mask;
                accentWeight += mask;
                if (mask < 0.5f)
                {
                    sorted.Add(light[i]);
                }
            }

            var mainColour = LeafColour(texels, colours, leaf, light, sorted);
            // Too few accent pixels to trust (under 2 % of a tile): the plant has no blossom, its tips are a lighter main.
            var accentColour = accentWeight > 80f ? InBand(accent / accentWeight, 0.22f, 0.8f) : mainColour * 1.35f;
            palette = new Palette
            {
                Main = new Color(mainColour.x, mainColour.y, mainColour.z),
                Accent = new Color(accentColour.x, accentColour.y, accentColour.z),
            };
            Palettes[key] = palette;
            return palette;
        }

        /// <summary>The main colour of a tile, brought into its band: the leaves, the brightest 30 % of the pixels that
        /// are no blossom (<paramref name="sorted"/> holds their brightness and is sorted here), weighted by how much of
        /// each texel is leaf.</summary>
        private static Vector3 LeafColour(int texels, Vector3[] colours, float[] leaf, float[] light, List<float> sorted)
        {
            // The leaves: the brightest 30 % of the pixels that are no blossom.
            sorted.Sort();
            float median = sorted.Count > 0 ? sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.7f))] : 0f;
            Vector3 main = Vector3.zero;
            float mainWeight = 0f;
            for (int i = 0; i < texels; i++)
            {
                if (leaf[i] > 0.5f && light[i] >= median)
                {
                    main += colours[i] * leaf[i];
                    mainWeight += leaf[i];
                }
            }

            return InBand(mainWeight > 0.5f ? main / mainWeight : new Vector3(0.2f, 0.3f, 0.15f), 0.2f, 0.55f);
        }

        /// <summary>A colour scaled so that its brightness lies between <paramref name="low"/> and <paramref name="high"/>
        /// (linear luminance); its hue stays.</summary>
        private static Vector3 InBand(Vector3 colour, float low, float high)
        {
            float light = (0.299f * colour.x) + (0.587f * colour.y) + (0.114f * colour.z);
            if (light < 1e-4f)
            {
                return new Vector3(low, low, low);
            }

            return colour * (Mathf.Clamp(light, low, high) / light);
        }

        /// <summary>The centre of a species' own tile in the atlas — the body reads the tile's average brightness there.</summary>
        private static Vector2 TileCentre(int id)
        {
            if (!TileCentres.TryGetValue(id, out var centre))
            {
                centre = _atlas.TileUv((ushort)id).center;
                TileCentres[id] = centre;
            }

            return centre;
        }

        private sealed class Slot
        {
            public readonly List<Batch> Batches = new List<Batch>();
            public Mesh Mesh;
            public int Used;
            public Batch Current;
        }

        /// <summary>One instanced draw: up to <see cref="BatchSize"/> matrices and three per-instance vectors. Reused across
        /// rebuilds (grown, never shrunk).</summary>
        private sealed class Batch
        {
            private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
            private readonly List<Vector4> _tile = new List<Vector4>();
            private readonly List<Vector4> _tint = new List<Vector4>();
            private readonly List<Vector4> _light = new List<Vector4>();
            private readonly List<Vector4> _accent = new List<Vector4>();
            private MaterialPropertyBlock _props;
            private int _propsLength;

            public int Count => _matrices.Count;

            public void Begin()
            {
                _matrices.Clear();
                _tile.Clear();
                _tint.Clear();
                _light.Clear();
                _accent.Clear();
            }

            public void Add(Matrix4x4 matrix, Vector4 tile, Vector4 tint, Vector4 light, Vector4 accent)
            {
                _matrices.Add(matrix);
                _tile.Add(tile);
                _tint.Add(tint);
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
                _props.SetVectorArray(TintId, _tint);
                _props.SetVectorArray(LightId, _light);
                _props.SetVectorArray(AccentId, _accent);
            }

            public void Draw(Mesh mesh, Material material, ShadowCastingMode cast, int layer)
            {
                if (Count == 0 || _props == null || mesh == null)
                {
                    return;
                }

                Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, _props, cast, true, layer, null, LightProbeUsage.Off);
            }
        }
    }
}
