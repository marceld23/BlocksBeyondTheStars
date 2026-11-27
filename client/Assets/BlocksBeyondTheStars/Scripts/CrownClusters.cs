// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One crown cell the mesher recorded for <see cref="CrownClusters"/> (staged crowns): where it
    /// is, what it looks like and how it is lit — everything a chunk-mesh vertex of a micro-block crown carried.</summary>
    public struct CrownCell
    {
        public Vector3 Local;     // the cell's min corner, chunk-local
        public int Hash;          // the world cell's hash: picks the cluster and its turn
        public bool Inner;        // no open neighbour: a sparse cluster
        public Color Tint;        // species tint or dye (black = the world's flora hue)
        public float TintMode;    // the mesher's tint mode (1 flora, 3 dye, …)
        public float Sky;         // skylight 0..1
        public Vector3 Light;     // the placed block light reaching the cell
        public Vector3 LightDir;  // its dominant direction
        public Vector2 Tile;      // the centre of the leaf block's atlas tile
        public float Metal;
        public float Emission;
        public byte Form;         // BlockShapeGeometry.CrownForm*: 0 = a cube cluster, else a bough, a spire, a frond, a rosette
        public byte Variant;      // the form's variant (a bough's reach, a frond's level piece or drooping end)
        public float Yaw;         // degrees about Y that point the form's +X away from the tree's axis
    }

    /// <summary>
    /// Staged tree crowns (<see cref="GameLook.StagedCrowns"/>). The chunk mesh keeps every
    /// crown cell as a closed cube with a painted cube pattern (the far picture). Near the camera this component draws
    /// the real micro-block clusters on top: GPU instances of a few shared cluster meshes, one draw per cluster kind and
    /// chunk, with the block atlas shader's instanced variant — so near and far are lit by the same code. The shader
    /// cross-fades the two by a screen-door dither over the last metres of the range and shrinks the cubes right in
    /// front of the eye away. Modelled on <see cref="GrassShells"/>: the mesher records points, nothing here is in the
    /// chunk mesh, and a chunk out of range costs nothing.
    /// </summary>
    public sealed class CrownClusters : MonoBehaviour
    {
        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard cap per call
        private const int OuterSlots = 4;   // cluster kinds one chunk uses for its outer cells (× 4 turns)
        private const int InnerSlots = 2;
        private const int LeafSlots = OuterSlots + InnerSlots;
        private const int BoughSlot = LeafSlots;       // three reaches
        private const int TipSlot = BoughSlot + 3;
        private const int FrondSlot = TipSlot + 1;     // a level piece, a drooping end
        private const int RosetteSlot = FrondSlot + 2;
        private const int Slots = RosetteSlot + 1;

        private static readonly int TileId = Shader.PropertyToID("_CcTile");
        private static readonly int TintId = Shader.PropertyToID("_CcTint");
        private static readonly int LightId = Shader.PropertyToID("_CcLight");
        private static readonly int LightDirId = Shader.PropertyToID("_CcLightDir");

        private static readonly Mesh[] OuterMeshes = new Mesh[BlockShapeGeometry.CrownOuterVariants];
        private static readonly Mesh[] InnerMeshes = new Mesh[BlockShapeGeometry.CrownInnerVariants];
        private static readonly Mesh[] FormMeshes = new Mesh[Slots];
        private static Material _material;
        private static Material _source;
        private static int _materialFrame = -1;
        private static Camera _cam;
        private static int _camFrame = -1;
        private static Vector3 _camPos;
        private static float _range;
        private static float _shadowRange;
        private static int _frameClusters;

        private readonly List<CrownCell> _cells = new List<CrownCell>();
        private readonly List<Batch>[] _batches = new List<Batch>[Slots];
        private readonly int[] _used = new int[Slots];
        private readonly Mesh[] _slotMesh = new Mesh[Slots];
        private bool _dirty = true;
        private Vector3Int _origin;
        private Vector3 _bakedPos;
        private MeshRenderer _chunkRenderer;

        /// <summary>Takes a chunk's crown cells (copied — the caller's list goes back to the mesh pool) and the chunk's
        /// canonical world origin (it picks the chunk's cluster kinds). Empty input disables the component.</summary>
        public void Setup(List<CrownCell> cells, Vector3Int origin)
        {
            _cells.Clear();
            if (cells != null)
            {
                _cells.AddRange(cells);
            }

            _origin = origin;
            _dirty = true;
            HasData = GameLook.StagedCrowns && _cells.Count > 0;
        }

        /// <summary>True while the chunk holds something for this drawer; the detail pass (GameBootstrap.Detail.cs) skips
        /// the chunk otherwise.</summary>
        public bool HasData { get; private set; }

        /// <summary>The once-a-frame step (camera, the range the last frame's count steers) and how far the near crowns
        /// reach this frame — 0 without a camera or on a preset without them.</summary>
        public static float FrameReach() => TickFrame() ? _range : 0f;

        /// <summary>The off-screen preview (edit mode: no Awake, no Update): submits this chunk's clusters once, as a
        /// frame of the game would.</summary>
        public void PreviewDraw()
        {
            if (_chunkRenderer == null)
            {
                Awake();
            }

            Draw();
        }

        private void Awake()
        {
            _chunkRenderer = GetComponent<MeshRenderer>();
            for (int i = 0; i < Slots; i++)
            {
                _batches[i] = new List<Batch>();
            }
        }

        /// <summary>Gives the batches' buffers on the graphics card back with the chunk (#4046 part b).</summary>
        private void OnDestroy()
        {
            foreach (var list in _batches)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var batch in list)
                {
                    batch.Release();
                }
            }
        }

        /// <summary>Submits this chunk's clusters for the current frame. Called by the detail pass for chunks inside the
        /// reach (#4046) — no <c>Update()</c> of its own, which ran on every loaded chunk every frame.</summary>
        public void Draw()
        {
            if (!TickFrame() || _range <= 0f || _chunkRenderer == null || !_chunkRenderer.enabled)
            {
                return; // no camera, a preset without near crowns, or a chunk the cull hides
            }

            var material = MaterialFor(_chunkRenderer.sharedMaterial);
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

            // The clusters cast their own shadows near the eye only; beyond, the closed far cube casts (the shader
            // switches at the same distance). A chunk drawn for its shadow only (behind rock for the eye, but its
            // shadow may reach the view) casts only.
            bool shadowsOnly = _chunkRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
            bool castsNear = distSq < (_shadowRange + 1f) * (_shadowRange + 1f);
            if (shadowsOnly && !castsNear)
            {
                return;
            }

            if (pos != _bakedPos)
            {
                // First draw, or the chunk moved across the world's seam: the instance matrices are in scene space.
                _bakedPos = pos;
                _dirty = true;
            }

            if (_dirty)
            {
                Build();
            }

            var cast = shadowsOnly ? ShadowCastingMode.ShadowsOnly : castsNear ? ShadowCastingMode.On : ShadowCastingMode.Off;
            int layer = gameObject.layer;
            for (int slot = 0; slot < Slots; slot++)
            {
                var mesh = _slotMesh[slot];
                for (int b = 0; b < _used[slot]; b++)
                {
                    var batch = _batches[slot][b];
                    batch.Draw(mesh, material, cast, layer);
                    _frameClusters += batch.Count;
                }
            }
        }

        /// <summary>The once-per-frame work shared by every chunk: the camera and the range of the near crowns.</summary>
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

                GameLook.AdaptCrownRange(_frameClusters); // what the last frame submitted steers this frame's range
                _frameClusters = 0;
                _range = GameLook.CrownRangeNow;
                _shadowRange = GameLook.CrownShadowNow;
            }

            return _cam != null;
        }

        /// <summary>The instancing twin of the chunks' block atlas material: the same shader and textures with GPU
        /// instancing on. Re-synced now and then, so a normal or height atlas bound later arrives here too.</summary>
        private static Material MaterialFor(Material chunkMaterial)
        {
            if (chunkMaterial == null)
            {
                return null;
            }

            if (_material == null || _source != chunkMaterial)
            {
                if (_material != null)
                {
                    Destroy(_material);
                }

                _source = chunkMaterial;
                _material = new Material(chunkMaterial) { name = "CrownClusters", enableInstancing = true };
                _materialFrame = Time.frameCount;
            }
            else if (Time.frameCount - _materialFrame > 120)
            {
                _materialFrame = Time.frameCount;
                _material.CopyPropertiesFromMaterial(chunkMaterial);
                _material.enableInstancing = true;
            }

            return _material;
        }

        /// <summary>Squared distance from a point to the chunk's box.</summary>
        private static float DistanceSqToChunk(Vector3 p, Vector3 origin)
        {
            float n = WorldConstants.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(origin.x - p.x, p.x - (origin.x + n)));
            float dy = Mathf.Max(0f, Mathf.Max(origin.y - p.y, p.y - (origin.y + n)));
            float dz = Mathf.Max(0f, Mathf.Max(origin.z - p.z, p.z - (origin.z + n)));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        /// <summary>One instance per crown cell, sorted into the chunk's cluster kinds: four of the outer clusters and
        /// two of the inner ones (picked by the chunk's place, so neighbouring chunks differ), each in one of four
        /// turns — sixteen outer looks per chunk for six draws.</summary>
        private void Build()
        {
            _dirty = false;
            int chunkHash = unchecked((_origin.x * 73856093) ^ (_origin.y * 19349663) ^ (_origin.z * 83492791));
            int outerStart = (int)((uint)chunkHash % BlockShapeGeometry.CrownOuterVariants);
            int innerStart = (int)(((uint)chunkHash >> 8) % BlockShapeGeometry.CrownInnerVariants);
            for (int slot = 0; slot < LeafSlots; slot++)
            {
                bool inner = slot >= OuterSlots;
                int variant = inner
                    ? (innerStart + (3 * (slot - OuterSlots))) % BlockShapeGeometry.CrownInnerVariants
                    : (outerStart + (3 * slot)) % BlockShapeGeometry.CrownOuterVariants;
                _slotMesh[slot] = ClusterMesh(variant, inner);
                _used[slot] = 0;
            }

            for (int slot = LeafSlots; slot < Slots; slot++)
            {
                _slotMesh[slot] = FormMesh(slot); // the forms are the same in every chunk
                _used[slot] = 0;
            }

            var current = new Batch[Slots];
            var half = new Vector3(0.5f, 0.5f, 0.5f);
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                uint hash = (uint)cell.Hash;
                int slot = cell.Inner ? OuterSlots + (int)(hash % InnerSlots) : (int)(hash % OuterSlots);
                bool formed = cell.Form != BlockShapeGeometry.CrownFormLeaf;
                if (formed)
                {
                    slot = FormSlot(cell.Form, cell.Variant);
                }

                var batch = current[slot];
                if (batch == null || batch.Count == BatchSize)
                {
                    batch?.Commit();
                    batch = current[slot] = NextBatch(slot);
                }

                // A form on the tree's axis (a spire, a rosette) has no outward side: it takes a turn by its cell.
                bool axial = cell.Form == BlockShapeGeometry.CrownFormNeedleTip || cell.Form == BlockShapeGeometry.CrownFormFrondRosette;
                var turn = Quaternion.Euler(0f, formed && !axial ? cell.Yaw : 90f * ((hash >> 9) & 3), 0f);
                batch.Add(Matrix4x4.TRS(_bakedPos + cell.Local + half, turn, Vector3.one),
                    new Vector4(cell.Tile.x, cell.Tile.y, cell.Emission, cell.Metal),
                    new Vector4(cell.Tint.r, cell.Tint.g, cell.Tint.b, cell.TintMode),
                    new Vector4(cell.Light.x, cell.Light.y, cell.Light.z, cell.Sky),
                    new Vector4(cell.LightDir.x, cell.LightDir.y, cell.LightDir.z, 0f));
            }

            for (int slot = 0; slot < Slots; slot++)
            {
                current[slot]?.Commit();
            }
        }

        private Batch NextBatch(int slot)
        {
            var list = _batches[slot];
            if (_used[slot] == list.Count)
            {
                list.Add(new Batch());
            }

            var batch = list[_used[slot]++];
            batch.Begin();
            return batch;
        }

        /// <summary>The shared mesh of one cluster kind, centred on its cell (the instance matrix turns it about the
        /// cell's middle). Every vertex carries what the mesher wrote for a micro-block crown: the cube's class as k/255
        /// in the gloss channel (the flutter phase), its shade, its tone in TEXCOORD2.yzw (× the instance's tint), the
        /// crown cutout code — and in TEXCOORD5 the middle of its cube, toward which the shader shrinks it.</summary>
        private static Mesh ClusterMesh(int variant, bool inner)
        {
            var cache = inner ? InnerMeshes : OuterMeshes;
            if (cache[variant] == null)
            {
                // the cell plus the sway's reach
                cache[variant] = MeshOf(BlockShapeGeometry.CrownCluster(variant, inner, 0), $"CrownCluster {(inner ? "inner" : "outer")} {variant}", 1.3f);
            }

            return cache[variant];
        }

        /// <summary>The slot of a crown form's instances (and of its mesh).</summary>
        private static int FormSlot(int form, int variant) => form switch
        {
            BlockShapeGeometry.CrownFormNeedleBough => BoughSlot + Mathf.Clamp(variant, 0, 2),
            BlockShapeGeometry.CrownFormNeedleTip => TipSlot,
            BlockShapeGeometry.CrownFormFrond => FrondSlot + Mathf.Clamp(variant, 0, 1),
            _ => RosetteSlot,
        };

        /// <summary>The shared mesh of a form slot (crown forms: boughs, the spire, fronds, the rosette). A form
        /// may reach past its cell, so its bounds are three cells wide.</summary>
        private static Mesh FormMesh(int slot)
        {
            if (FormMeshes[slot] == null)
            {
                int form = slot >= RosetteSlot ? BlockShapeGeometry.CrownFormFrondRosette
                    : slot >= FrondSlot ? BlockShapeGeometry.CrownFormFrond
                    : slot >= TipSlot ? BlockShapeGeometry.CrownFormNeedleTip : BlockShapeGeometry.CrownFormNeedleBough;
                int variant = slot >= RosetteSlot ? 0 : slot >= FrondSlot ? slot - FrondSlot : slot >= TipSlot ? 0 : slot - BoughSlot;
                FormMeshes[slot] = MeshOf(BlockShapeGeometry.CrownForm(form, variant), $"CrownForm {form}/{variant}", 3.2f);
            }

            return FormMeshes[slot];
        }

        /// <summary>A mesh out of a list of boxes (six faces each), centred on the cell, with what the crown shader
        /// reads per vertex.</summary>
        private static Mesh MeshOf(List<BlockShapeGeometry.Face> faces, string name, float size)
        {
            var verts = new List<Vector3>(faces.Count * 4);
            var normals = new List<Vector3>(faces.Count * 4);
            var tangents = new List<Vector4>(faces.Count * 4);
            var colors = new List<Color>(faces.Count * 4);
            var leaf = new List<Vector4>(faces.Count * 4);
            var cube = new List<Vector3>(faces.Count * 4);
            var tris = new List<int>(faces.Count * 6);
            var half = new Vector3(0.5f, 0.5f, 0.5f);

            // A cluster is a list of boxes, six faces each (BlockShapeGeometry.Box): the cube's middle is the middle of
            // its six faces' corners.
            for (int first = 0; first + 5 < faces.Count; first += 6)
            {
                Vector3 middle = BoxMiddle(faces, first, half);
                for (int f = first; f < first + 6; f++)
                {
                    var face = faces[f];
                    Vector3 a = face.A - half, b = face.B - half, c = face.C - half, d = face.D - half;
                    Vector3 edge1 = b - a, edge2 = d - a;
                    Vector3 nrm = Vector3.Cross(edge1, edge2).normalized;
                    CrownFaceLook(face, nrm, edge1, edge2, out float tone, out Color col, out Vector4 tan);

                    int baseIdx = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                    for (int i = 0; i < 4; i++)
                    {
                        normals.Add(nrm);
                        tangents.Add(tan);
                        colors.Add(col);
                        leaf.Add(new Vector4(ChunkMesher.LeafCodeCrown, tone, tone, tone));
                        cube.Add(middle);
                    }

                    tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(2, leaf);
            mesh.SetUVs(5, cube);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, size, size));
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>The middle of the box whose six faces start at <paramref name="first"/> (BlockShapeGeometry.Box): the
        /// middle of its faces' corners, centred on the cell (less <paramref name="half"/>).</summary>
        private static Vector3 BoxMiddle(List<BlockShapeGeometry.Face> faces, int first, Vector3 half)
        {
            Vector3 min = faces[first].A, max = faces[first].A;
            for (int f = first; f < first + 6; f++)
            {
                var face = faces[f];
                min = Vector3.Min(min, Vector3.Min(Vector3.Min(face.A, face.B), Vector3.Min(face.C, face.D)));
                max = Vector3.Max(max, Vector3.Max(Vector3.Max(face.A, face.B), Vector3.Max(face.C, face.D)));
            }

            return ((min + max) * 0.5f) - half;
        }

        /// <summary>What a crown face carries per vertex besides its position and normal: its tone, its colour (the
        /// cube's class as k/255 and its shade; a twig darker) and its tangent.</summary>
        private static void CrownFaceLook(BlockShapeGeometry.Face face, Vector3 nrm, Vector3 edge1, Vector3 edge2, out float tone,
            out Color col, out Vector4 tan)
        {
            float shade = Mathf.Clamp(0.76f + (0.24f * nrm.y), 0.5f, 1f);
            int cls = (int)face.Part;
            bool twig = face.Part == BlockShapeGeometry.CrownTwig;
            tone = twig ? 0.5f : 1.14f - (0.07f * cls);
            col = new Color(cls / 255f, 0f, Mathf.Clamp01(shade * (twig ? 0.7f : 1f - (0.03f * cls))), 0f);
            Vector3 tanDir = edge2.normalized;
            float hand = Vector3.Dot(Vector3.Cross(nrm, tanDir), edge1) < 0f ? -1f : 1f;
            tan = new Vector4(tanDir.x, tanDir.y, tanDir.z, hand);
        }

        /// <summary>One instanced draw: up to <see cref="BatchSize"/> matrices and four per-instance vectors. Reused across
        /// rebuilds (grown, never shrunk).</summary>
        private sealed class Batch
        {
            private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
            private readonly List<Vector4> _tile = new List<Vector4>();
            private readonly List<Vector4> _tint = new List<Vector4>();
            private readonly List<Vector4> _light = new List<Vector4>();
            private readonly List<Vector4> _lightDir = new List<Vector4>();
            private MaterialPropertyBlock _props;
            private int _propsLength;
            private GpuInstances _gpu; // #4046: the same instances on the graphics card

            public int Count => _matrices.Count;

            public void Begin()
            {
                _matrices.Clear();
                _tile.Clear();
                _tint.Clear();
                _light.Clear();
                _lightDir.Clear();
            }

            public void Add(Matrix4x4 matrix, Vector4 tile, Vector4 tint, Vector4 light, Vector4 lightDir)
            {
                _matrices.Add(matrix);
                _tile.Add(tile);
                _tint.Add(tint);
                _light.Add(light);
                _lightDir.Add(lightDir);
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
                _props.SetVectorArray(LightDirId, _lightDir);
                if (GpuInstances.Active)
                {
                    (_gpu ??= new GpuInstances()).Upload(_matrices, _tile, _tint, _light, _lightDir);
                }
            }

            public void Draw(Mesh mesh, Material material, ShadowCastingMode cast, int layer)
            {
                if (Count == 0 || _props == null || mesh == null)
                {
                    return;
                }

                // #4046: the instances are on the graphics card — a frame names the buffer instead of handing the arrays over.
                if (GpuInstances.Active && _gpu != null && _gpu.Count == Count)
                {
                    _gpu.Draw(mesh, material, cast, layer);
                    return;
                }

                Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, _props, cast, true, layer, null, LightProbeUsage.Off);
            }

            /// <summary>Gives the batch's buffer on the graphics card back (the chunk is going).</summary>
            public void Release()
            {
                _gpu?.Release();
                _gpu = null;
            }
        }
    }
}
