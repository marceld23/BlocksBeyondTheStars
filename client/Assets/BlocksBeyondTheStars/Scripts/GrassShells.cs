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
    /// The High preset's meadow on one chunk (#3056): the shell-grass carpet over every open grass, alien-grass and mycelium
    /// top (the mesher records them on every preset — ChunkMesher.HighDetails.cs) and the meadow flowers on about one grass
    /// top in nine (GrassShells.Flowers.cs). Render-only and GPU-instanced: one draw per kind and chunk, culled per chunk by
    /// distance, and the shaders thin both out toward the end of their range, so nothing pops. Runs only while
    /// <see cref="HighDetails.Grass"/> or <see cref="HighDetails.Flowers"/> is on (High on a desktop client) and follows the
    /// switches live: the tops are kept on every preset, the instance data is built the first time a chunk is drawn.
    /// </summary>
    public sealed partial class GrassShells : MonoBehaviour
    {
        /// <summary>The carpet's range in metres; GrassShellCommon.hlsl flattens it over the last ones (BBTS_SHELL_FADE_END).
        /// #3078: 16, was 20 — the strands of the last metres were only pixel noise.</summary>
        public const float CarpetRange = 16f;

        /// <summary>The flowers' range in metres; MeadowFlowerCommon.hlsl sinks them over the last ones (BBTS_FLOWER_FADE_END).</summary>
        public const float FlowerRange = 28f;

        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard cap per call

        private static readonly List<GrassShells> Live = new List<GrassShells>();
        private static readonly int ShellTileId = Shader.PropertyToID("_ShellTile");
        private static readonly int ShellInfoId = Shader.PropertyToID("_ShellInfo");
        private static readonly int ShellLightId = Shader.PropertyToID("_ShellLight");
        private static bool _subscribed;
        private static BlockTextureAtlas _atlas;
        private static int _atlasVersion;
        private static Camera _cam;
        private static int _camFrame = -1;
        private static Vector3 _camPos;

        private readonly List<Vector4> _tops = new List<Vector4>();
        private readonly List<Vector3> _light = new List<Vector3>();
        private readonly List<Batch> _carpet = new List<Batch>();
        private int _carpetBatches;
        private int _carpetAtlasVersion = -1;
        private bool _carpetDirty = true;
        private Vector3Int _origin;
        private Vector3 _bakedPos;
        private MeshRenderer _chunkRenderer;

        private static bool Wanted => HighDetails.Grass || HighDetails.Flowers;

        /// <summary>The world's block atlas: the carpet's colour source (bound as the shell material's texture — a texture pack
        /// repaints it in place, so the carpet follows on its own) and the tile rects of the tops. A new atlas (another content
        /// snapshot) re-bakes every chunk's tile rects.</summary>
        public static void UseAtlas(BlockTextureAtlas atlas)
        {
            if (ReferenceEquals(atlas, _atlas))
            {
                return;
            }

            _atlas = atlas;
            _atlasVersion++;
            var material = MeadowAssets.ShellMaterial;
            if (material != null && atlas != null)
            {
                material.mainTexture = atlas.Texture;
            }
        }

        /// <summary>Takes a chunk's carpet tops (the mesher's <c>GrassTops</c>: chunk-local top centre, w = block id + skylight
        /// fraction) and the chunk's canonical world origin (the strand and flower picks hash the world cell, so every client
        /// grows the same meadow). <paramref name="light"/> is the placed block light per top, parallel to
        /// <paramref name="tops"/> (null = none). The lists are copied — the caller's buffers go back to the mesh pool — and
        /// empty input disables the component.</summary>
        public void Setup(List<Vector4> tops, Vector3Int origin, List<Vector3> light = null)
        {
            _tops.Clear();
            _light.Clear();
            if (tops != null)
            {
                _tops.AddRange(tops);
                if (light != null && light.Count == tops.Count)
                {
                    _light.AddRange(light);
                }
            }

            _origin = origin;
            _carpetDirty = true;
            _flowersDirty = true;
            enabled = Wanted && _tops.Count > 0;
        }

        private void Awake()
        {
            _chunkRenderer = GetComponent<MeshRenderer>();
            Live.Add(this);
            if (!_subscribed)
            {
                _subscribed = true;
                HighDetails.Changed += OnHighDetailsChanged;
            }
        }

        private void OnDestroy() => Live.Remove(this);

        /// <summary>The player flipped the preset or a switch: every chunk's meadow starts or stops at once (no remesh).</summary>
        private static void OnHighDetailsChanged()
        {
            bool wanted = Wanted;
            foreach (var shells in Live)
            {
                if (shells != null)
                {
                    shells.enabled = wanted && shells._tops.Count > 0;
                }
            }
        }

        private void Update()
        {
            if (!Wanted)
            {
                enabled = false;
                return;
            }

            // A chunk the visibility walk or the distance cull hides (or one drawn for its shadow only) shows no meadow.
            if (_chunkRenderer != null && (!_chunkRenderer.enabled || _chunkRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly))
            {
                return;
            }

            if (!TickFrame())
            {
                return;
            }

            var pos = transform.position;
            if (pos != _bakedPos)
            {
                // First draw, or the chunk moved across the world's seam (RepositionChunks): re-bake the instance matrices.
                _bakedPos = pos;
                _carpetDirty = true;
                _flowersDirty = true;
            }

            float distSq = DistanceSqToChunk(_camPos, pos);
            if (HighDetails.Grass && distSq < CarpetRange * CarpetRange)
            {
                DrawCarpet();
            }

            if (HighDetails.Flowers && distSq < FlowerRange * FlowerRange)
            {
                DrawFlowers();
            }
        }

        /// <summary>The once-per-frame work shared by every chunk: the camera (Camera.main is a tagged search) and the
        /// season's flower density. False without a camera.</summary>
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

                UpdateFlowerDensity();
                DiagnosticTick();
            }

            return _cam != null;
        }

        /// <summary>Squared distance from a point to the chunk's box (one block taller: a lid cell's carpet stands on it).</summary>
        private static float DistanceSqToChunk(Vector3 p, Vector3 origin)
        {
            float n = WorldConstants.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(origin.x - p.x, p.x - (origin.x + n)));
            float dy = Mathf.Max(0f, Mathf.Max(origin.y - p.y, p.y - (origin.y + n + 1f)));
            float dz = Mathf.Max(0f, Mathf.Max(origin.z - p.z, p.z - (origin.z + n)));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        private void DrawCarpet()
        {
            var material = MeadowAssets.ShellMaterial;
            if (material == null || _atlas == null)
            {
                return;
            }

            if (_carpetDirty || _carpetAtlasVersion != _atlasVersion)
            {
                BuildCarpet();
            }

            var mesh = MeadowAssets.ShellMesh;
            for (int i = 0; i < _carpetBatches; i++)
            {
                _carpet[i].Draw(mesh, material);
            }
        }

        /// <summary>One instance per top: a plain translation (the shells must cover the cell exactly), the top face's tile
        /// as the mesher drew it, the skylight, the strand seed and the block light.</summary>
        private void BuildCarpet()
        {
            _carpetDirty = false;
            _carpetAtlasVersion = _atlasVersion;
            _carpetBatches = 0;
            Batch batch = null;
            for (int i = 0; i < _tops.Count; i++)
            {
                if (batch == null || batch.Count == BatchSize)
                {
                    batch?.Commit(ShellTileId, ShellInfoId, ShellLightId);
                    batch = NextBatch(_carpet, ref _carpetBatches, Mathf.Min(BatchSize, _tops.Count - i));
                }

                var p = _tops[i];
                ushort id = (ushort)p.w;
                var cell = WorldCell(p);
                var (rect, turn) = TopTile(id, cell);
                var bl = i < _light.Count ? _light[i] : Vector3.zero;
                batch.Add(Matrix4x4.Translate(_bakedPos + new Vector3(p.x, p.y, p.z)),
                    new Vector4(rect.xMin, rect.yMin, rect.width, rect.height),
                    new Vector4(p.w - id, turn, MeadowField.CellSeed(cell.x, cell.y, cell.z), 0f),
                    new Vector4(bl.x, bl.y, bl.z, 0f));
            }

            batch?.Commit(ShellTileId, ShellInfoId, ShellLightId);
        }

        /// <summary>The atlas rect and UV turns of a top face exactly as the mesher draws it — the natural-block variation
        /// (variant tile + 90° turns, MeadowField.TopVariant) and a block's own top tile — so a strand takes the colour of
        /// the texel it grows out of.</summary>
        private static (Rect Rect, int Turn) TopTile(ushort id, Vector3Int cell)
        {
            int turn = 0;
            ushort slot = id;
            if (_atlas.AnimationCode(id) == 0f && _atlas.TryGetVariants(id, out var variants))
            {
                var (pick, t) = MeadowField.TopVariant(cell.x, cell.y, cell.z, variants.Length);
                slot = pick > 0 ? variants[pick - 1] : id;
                turn = t;
            }

            if (_atlas.TryGetCapTile(id, out ushort cap))
            {
                slot = cap;
            }

            return (_atlas.TileUv(slot), turn);
        }

        /// <summary>The canonical world cell of the block under a top (the top centre sits at local x + 0.5, y + 1, z + 0.5).</summary>
        private Vector3Int WorldCell(Vector4 top)
            => new Vector3Int(_origin.x + (int)top.x, _origin.y + (int)top.y - 1, _origin.z + (int)top.z);

        private static Batch NextBatch(List<Batch> batches, ref int used, int capacity)
        {
            if (used == batches.Count)
            {
                batches.Add(new Batch());
            }

            var batch = batches[used++];
            batch.Begin(capacity);
            return batch;
        }

        /// <summary>One instanced draw: up to <see cref="BatchSize"/> matrices plus up to three per-instance vectors. Reused
        /// across rebuilds (grown, never shrunk), so a remesh of a drawn chunk allocates nothing.</summary>
        private sealed class Batch
        {
            public readonly List<Vector4> A = new List<Vector4>();
            public readonly List<Vector4> B = new List<Vector4>();
            public readonly List<Vector4> C = new List<Vector4>();
            private Matrix4x4[] _matrices = System.Array.Empty<Matrix4x4>();
            private MaterialPropertyBlock _props;
            private int _propsLength;

            public int Count { get; private set; }

            public void Begin(int capacity)
            {
                if (_matrices.Length < capacity)
                {
                    _matrices = new Matrix4x4[capacity];
                }

                Count = 0;
                A.Clear();
                B.Clear();
                C.Clear();
            }

            public void Add(Matrix4x4 matrix, Vector4 a, Vector4 b, Vector4 c)
            {
                _matrices[Count++] = matrix;
                A.Add(a);
                B.Add(b);
                C.Add(c);
            }

            /// <summary>Uploads the per-instance vectors (the third only when <paramref name="cId"/> is given). A property
            /// block fixes an array's length on its first upload, so a batch that grew past it gets a fresh block.</summary>
            public void Commit(int aId, int bId, int? cId)
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

                _props.SetVectorArray(aId, A);
                _props.SetVectorArray(bId, B);
                if (cId.HasValue)
                {
                    _props.SetVectorArray(cId.Value, C);
                }
            }

            public void Draw(Mesh mesh, Material material)
            {
                if (Count == 0 || _props == null)
                {
                    return;
                }

                Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, Count, _props, ShadowCastingMode.Off, true, 0, null,
                    LightProbeUsage.Off);
                CountInstances(Count);
            }
        }
    }
}
