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
    // #3058: the decal pool, the face gathering and mesh building, the cell classification and the GPU resources.
    public sealed partial class FxDecals
    {
        private const float Lift = 0.005f; // the quads sit 5 mm proud of the faces (plus the shader's polygon offset)

        private static readonly int TileId = Shader.PropertyToID("_Tile");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int EmberId = Shader.PropertyToID("_Ember");
        private static readonly int EmberColorId = Shader.PropertyToID("_EmberColor");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

        /// <summary>Where a small mark looks for its surface when the faces under it are gone: one block behind (the block
        /// was mined), one in front (a block was placed over it), then two.</summary>
        private static readonly float[] FollowSteps = { -1f, 1f, -2f, 2f };

        private sealed class Decal
        {
            public GameObject Root;
            public MeshRenderer Soot;
            public MeshRenderer Ember;
            public Mesh Mesh;
            public Spec Spec;
            public bool Live;
            public float Born;
            public float RebuildAt = -1f;     // Time.time of the pending rebuild (-1 = none)
            public float ShownStrength = -1f; // the values last pushed to the renderers
            public float ShownEmber = -1f;
            public Vector3Int Min, Max;       // the scene cells the faces were gathered from
        }

        private readonly Decal[] _pool = new Decal[MaxDecals];
        private int _liveCount;
        private readonly List<DecalFace> _faces = new List<DecalFace>(256);
        private readonly List<Vector3> _verts = new List<Vector3>(1024);
        private readonly List<Vector2> _uvs = new List<Vector2>(1024);
        private readonly List<Color32> _colors = new List<Color32>(1024);
        private readonly List<int> _tris = new List<int>(1536);
        private readonly float[] _corners = new float[12];
        private System.Func<int, int, int, DecalCell> _cellOf;
        private System.Func<int, int, int, bool> _blocks;
        private MaterialPropertyBlock _mpb;

        private GameObject _container; // parent of every mark — hidden while the space view owns the camera
        private bool _broken;
        private Texture2D _atlas;
        private Material _sootMat;
        private Material _emberMat;

        private GameContent _classContent;
        private DecalCell[] _class;

        // ---------------------------------------------------------------- placing, ageing, rebuilding

        private void Place(Spec spec)
        {
            if (!EnsureResources())
            {
                return;
            }

            var cam = Camera.main;
            if (cam != null && (spec.Center - cam.transform.position).sqrMagnitude > MaxDistance * MaxDistance)
            {
                return;
            }

            if (Renew(spec) || !Gather(ref spec, follow: false))
            {
                return; // the same spot struck again (renewed instead of stacked), or nothing to lie on (glass, open air)
            }

            var d = Rent();
            d.Spec = spec;
            d.Born = Time.time;
            d.RebuildAt = -1f;
            d.ShownStrength = -1f;
            d.ShownEmber = -1f;
            BuildMesh(d);
            d.Live = true;
            _liveCount++;
            d.Root.SetActive(true);
            Show(d, 0f);
        }

        /// <summary>Renews a live mark of the same kind at (nearly) the same spot — rapid fire into one place must not stack
        /// marks until the wall turns black.</summary>
        private bool Renew(in Spec spec)
        {
            foreach (var d in _pool)
            {
                if (d != null && d.Live && d.Spec.Tile == spec.Tile && Vector3.Dot(d.Spec.Normal, spec.Normal) > 0.9f
                    && (d.Spec.Center - spec.Center).sqrMagnitude < spec.Radius * spec.Radius * 0.25f)
                {
                    d.Born = Time.time;
                    d.Spec.EmberTime = Mathf.Max(d.Spec.EmberTime, spec.EmberTime);
                    return true;
                }
            }

            return false;
        }

        private void Tick(float now)
        {
            foreach (var d in _pool)
            {
                if (d == null || !d.Live)
                {
                    continue;
                }

                float age = now - d.Born;
                if (age >= d.Spec.Life + d.Spec.Fade)
                {
                    Release(d);
                    continue;
                }

                if (d.RebuildAt >= 0f && now >= d.RebuildAt)
                {
                    d.RebuildAt = -1f;
                    if (!Gather(ref d.Spec, follow: true))
                    {
                        Release(d); // every face it lay on is gone (blasted away, built over)
                        continue;
                    }

                    BuildMesh(d);
                }

                Show(d, age);
            }
        }

        /// <summary>Pushes the mark's strength (full, then fading) and ember glow (dying down) to its renderers when they
        /// changed visibly.</summary>
        private void Show(Decal d, float age)
        {
            var s = d.Spec;
            float strength = s.Strength * (age <= s.Life ? 1f : Mathf.Clamp01(1f - (age - s.Life) / s.Fade));
            float k = s.EmberTime > 0f ? Mathf.Clamp01(1f - age / s.EmberTime) : 0f;
            float ember = s.EmberPeak * k * k;
            bool glowing = ember > 0.01f;
            if (Mathf.Abs(strength - d.ShownStrength) < 0.004f && Mathf.Abs(ember - d.ShownEmber) < 0.01f && glowing == d.Ember.enabled)
            {
                return;
            }

            d.ShownStrength = strength;
            d.ShownEmber = ember;
            var b = _mpb ??= new MaterialPropertyBlock();
            b.Clear();
            b.SetVector(TileId, new Vector4(DecalPatterns.TileU(s.Tile), DecalPatterns.TileV(s.Tile), 0.5f, 0.5f / DecalPatterns.AtlasSize));
            b.SetVector(TintId, s.Tint); // already linear (FxKit.Lin): uploaded raw
            b.SetFloat(StrengthId, strength);
            b.SetFloat(EmberId, ember);
            d.Soot.SetPropertyBlock(b);
            d.Ember.enabled = glowing;
            if (glowing)
            {
                d.Ember.SetPropertyBlock(b);
            }
        }

        /// <summary>Gathers the mark's faces into <see cref="_faces"/>. With <paramref name="follow"/> a small mark whose
        /// surface is gone looks one or two blocks behind and in front of it and moves there.</summary>
        private bool Gather(ref Spec s, bool follow)
        {
            if (Collect(s, s.Center) > 0)
            {
                return true;
            }

            if (!follow || !s.Follows)
            {
                return false;
            }

            foreach (float step in FollowSteps)
            {
                var c = s.Center + s.Normal * step;
                if (Collect(s, c) > 0)
                {
                    s.Center = c;
                    return true;
                }
            }

            return false;
        }

        private int Collect(in Spec s, Vector3 c)
        {
            var box = new DecalBox(c.x, c.y, c.z, s.Normal.x, s.Normal.y, s.Normal.z, s.Radius, s.Depth, s.MinDot, s.FacingLift);
            return DecalFaces.Collect(_cellOf, box, _faces);
        }

        /// <summary>One quad per gathered face (pulled in along the mesher's bevelled edges), vertices relative to the mark's
        /// centre, UVs = the corners projected into the mark's own rotated frame, vertex alpha = the fade off its plane.</summary>
        private void BuildMesh(Decal d)
        {
            var s = d.Spec;
            d.Root.transform.SetPositionAndRotation(s.Center, Quaternion.identity);
            Frame(s, out var tan, out var bit);
            float inv = 0.5f / s.Radius;
            _verts.Clear();
            _uvs.Clear();
            _colors.Clear();
            _tris.Clear();
            foreach (var face in _faces)
            {
                DecalFaces.Corners(face, Lift, ChunkMesher.BevelAmount, _corners);
                int first = _verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    var rel = new Vector3(_corners[k * 3], _corners[k * 3 + 1], _corners[k * 3 + 2]) - s.Center;
                    _verts.Add(rel);
                    _uvs.Add(new Vector2(Vector3.Dot(rel, tan) * inv + 0.5f, Vector3.Dot(rel, bit) * inv + 0.5f));
                    float off = Mathf.InverseLerp(s.Depth * 0.5f, s.Depth, Mathf.Abs(Vector3.Dot(rel, s.Normal)));
                    _colors.Add(new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * (1f - Mathf.SmoothStep(0f, 1f, off)))));
                }

                _tris.Add(first);
                _tris.Add(first + 1);
                _tris.Add(first + 2);
                _tris.Add(first);
                _tris.Add(first + 2);
                _tris.Add(first + 3);
            }

            d.Mesh.Clear();
            d.Mesh.SetVertices(_verts);
            d.Mesh.SetUVs(0, _uvs);
            d.Mesh.SetColors(_colors);
            d.Mesh.SetTriangles(_tris, 0, true);
            int reach = DecalFaces.Reach(s.Radius, s.Depth);
            var cell = Vector3Int.FloorToInt(s.Center);
            d.Min = cell - new Vector3Int(reach, reach, reach);
            d.Max = cell + new Vector3Int(reach, reach, reach);
        }

        /// <summary>The mark's texture axes: any two perpendicular to its normal, turned by its random angle.</summary>
        private static void Frame(in Spec s, out Vector3 tan, out Vector3 bit)
        {
            var n = s.Normal;
            var t = Vector3.Cross(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
            var b = Vector3.Cross(n, t);
            float c = Mathf.Cos(s.Angle), sn = Mathf.Sin(s.Angle);
            tan = t * c + b * sn;
            bit = b * c - t * sn;
        }

        // ---------------------------------------------------------------- pool

        private Decal Rent()
        {
            Decal oldest = null;
            for (int i = 0; i < _pool.Length; i++)
            {
                var d = _pool[i];
                if (d == null)
                {
                    return _pool[i] = CreateDecal(i);
                }

                if (!d.Live)
                {
                    return d;
                }

                if (oldest == null || d.Born < oldest.Born)
                {
                    oldest = d;
                }
            }

            Release(oldest); // full: the oldest mark makes way
            return oldest;
        }

        private Decal CreateDecal(int index)
        {
            var root = new GameObject("FxDecal" + index);
            root.SetActive(false);
            root.transform.SetParent(_container.transform, false);
            var mesh = new Mesh { name = "FxDecal" + index };
            mesh.MarkDynamic();
            var soot = AddRenderer(root, mesh, _sootMat);
            var glow = new GameObject("Embers");
            glow.transform.SetParent(root.transform, false);
            var ember = AddRenderer(glow, mesh, _emberMat);
            ember.enabled = false;
            return new Decal { Root = root, Soot = soot, Ember = ember, Mesh = mesh };
        }

        private static MeshRenderer AddRenderer(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return mr;
        }

        private void Release(Decal d)
        {
            if (d == null || !d.Live)
            {
                return;
            }

            d.Live = false;
            _liveCount--;
            d.RebuildAt = -1f;
            d.Ember.enabled = false;
            d.Root.SetActive(false);
            d.Mesh.Clear();
        }

        /// <summary>Drops every mark (world reset, hyperjump, the switch turned off).</summary>
        private void ClearAll()
        {
            foreach (var d in _pool)
            {
                Release(d);
            }
        }

        // ---------------------------------------------------------------- cells

        /// <summary>What a scene cell is to a mark. Unloaded counts as open — a mark is never laid against terrain that has
        /// not streamed in.</summary>
        private DecalCell CellAt(int x, int y, int z)
        {
            var world = Game != null ? Game.World : null;
            if (world == null || !world.TryGetBlock(x, y, z, out var id) || id.IsAir)
            {
                return DecalCell.Open;
            }

            var kind = ClassOf(id);
            // A shaped block (slab, stair, …) never seals the cube beside it (as in the mesher), and takes no mark itself.
            return kind != DecalCell.Open && !ShapeCode.IsCube(world.GetShape(x, y, z)) ? DecalCell.Open : kind;
        }

        private DecalCell ClassOf(BlockId id)
        {
            var content = Game.Content;
            if (content == null)
            {
                return DecalCell.Open;
            }

            if (!ReferenceEquals(content, _classContent))
            {
                BuildClassTable(content);
            }

            return id.Value < _class.Length ? _class[id.Value] : DecalCell.Closed;
        }

        private void BuildClassTable(GameContent content)
        {
            int size = 1;
            foreach (var b in content.Blocks.Values)
            {
                size = Mathf.Max(size, b.NumericId.Value + 1);
            }

            var solidFlora = new HashSet<string>(FloraCatalog.SolidKeys());
            _class = new DecalCell[size];
            for (int i = 0; i < size; i++)
            {
                _class[i] = Classify(content, new BlockId((ushort)i), solidFlora);
            }

            _classContent = content;
        }

        /// <summary>Open = the mesher draws a neighbouring cube's face against it (air, glass, water, plants, crowns, torches,
        /// ladders — <see cref="ChunkMesher.NeighbourExposesOpaqueFace"/>); Closed = opaque but no ground for a mark (lava, oil,
        /// a cactus or pumpkin body, a flower pot); Surface = every other opaque cube.</summary>
        private static DecalCell Classify(GameContent content, BlockId id, HashSet<string> solidFlora)
        {
            if (id.IsAir || ChunkMesher.NeighbourExposesOpaqueFace(content, id))
            {
                return DecalCell.Open;
            }

            var def = content.BlockById(id);
            bool closed = def == null || def.Liquid || def.Key == "lava" || def.Key == "flower_pot" || def.Key == ChunkMesher.SaguaroKey
                          || solidFlora.Contains(def.Key) || FloraCatalog.PaletteOf(def.Key) != null;
            return closed ? DecalCell.Closed : DecalCell.Surface;
        }

        // ---------------------------------------------------------------- resources

        /// <summary>The shader, the atlas and the two materials, made once on first use. False (for good) when the shader is
        /// missing from the build or unsupported — then the marks simply never appear.</summary>
        private bool EnsureResources()
        {
            if (_sootMat != null)
            {
                return true;
            }

            if (_broken)
            {
                return false;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/Decal");
            if (shader == null || !shader.isSupported)
            {
                _broken = true;
                Debug.LogWarning("[FxDecals] BlocksBeyondTheStars/Decal is missing or unsupported — no scorch marks this session.");
                return false;
            }

            _atlas = new Texture2D(DecalPatterns.AtlasSize, DecalPatterns.AtlasSize, TextureFormat.RGBA32, true, true)
            {
                name = "FxDecalAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            _atlas.SetPixelData(DecalPatterns.BuildAtlas(), 0);
            _atlas.Apply(true, true);
            _sootMat = MakeMaterial(shader, "FxDecalSoot", BlendMode.DstColor, BlendMode.Zero, 0f);
            _emberMat = MakeMaterial(shader, "FxDecalEmber", BlendMode.One, BlendMode.One, 1f);
            _emberMat.renderQueue = shader.renderQueue + 1; // the glow is added after the face was darkened
            return true;
        }

        private Material MakeMaterial(Shader shader, string name, BlendMode src, BlendMode dst, float mode)
        {
            var m = new Material(shader) { name = name, mainTexture = _atlas };
            m.SetFloat(SrcBlendId, (float)src);
            m.SetFloat(DstBlendId, (float)dst);
            m.SetFloat(ModeId, mode);
            m.SetVector(EmberColorId, new Vector4(1f, 0.34f, 0.07f, 1f)); // linear orange; _Ember lifts it into HDR
            return m;
        }

        private void DestroyResources()
        {
            foreach (var d in _pool)
            {
                if (d?.Mesh != null)
                {
                    Destroy(d.Mesh);
                }
            }

            if (_sootMat != null)
            {
                Destroy(_sootMat);
            }

            if (_emberMat != null)
            {
                Destroy(_emberMat);
            }

            if (_atlas != null)
            {
                Destroy(_atlas);
            }
        }
    }
}
