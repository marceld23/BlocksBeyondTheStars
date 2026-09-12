// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Threading;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The far terrain (#1820/#1821): a low-resolution horizon beyond the streamed chunks. Each world sends its exact
    /// generator settings (<see cref="FarTerrainWorldInfo"/>); the view samples the same generator in patches around the
    /// player (<see cref="FarTerrainLayout"/>), raises what the server reports as built (<see cref="FarTerrainOverlay"/>)
    /// and draws each patch as one small mesh. Where real chunks are drawn, a column mask tells the shader to discard
    /// the far terrain, so the streamed world always wins and a chunk not arrived yet shows far terrain, not a hole.
    /// <para>Desktop samples on a background thread (a new world's first generator queries can take a few hundred
    /// milliseconds); the browser build has no C# threads and samples a row at a time within a frame budget.</para>
    /// </summary>
    public sealed class FarTerrainView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>The player's far-view setting in blocks (0 = off).</summary>
        public int RangeSetting;

        private const int MaskSize = 64;                 // chunk columns per side of the column mask
        private const float PlanMoveBlocks = 24f;        // re-plan the patch set after this much movement
        private const float MaskRefreshSeconds = 0.25f;
        private const float TileRequestSeconds = 0.5f;
        private const int MaxPatches = 400;

        private static readonly int MaskTexId = Shader.PropertyToID("_Sc_FarMask");
        private static readonly int MaskParamsId = Shader.PropertyToID("_Sc_FarMaskParams");
        private static readonly int CenterId = Shader.PropertyToID("_Sc_FarCenter");

        private sealed class Patch
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Dirty;
        }

        private readonly Dictionary<FarPatchKey, Patch> _patches = new Dictionary<FarPatchKey, Patch>();
        private readonly HashSet<FarPatchKey> _desiredSet = new HashSet<FarPatchKey>();
        private List<FarPatchKey> _desired = new List<FarPatchKey>();
        private readonly List<FarPatchKey> _dropScratch = new List<FarPatchKey>();
        private readonly FarTerrainOverlay _overlay = new FarTerrainOverlay();

        private FarTerrainSource _source;      // sampled by the worker (or inline in the browser)
        private FarTerrainSource _maskSource;  // main-thread queries (column mask)
        private FarTerrainWorldInfo _info;
        private int _epoch;
        private bool _subscribed;
        private Material _material;
        private Texture2D _mask;
        private readonly byte[] _maskBytes = new byte[MaskSize * MaskSize];
        private Transform _root;
        private Vector3 _plannedAt = new Vector3(float.MaxValue, 0f, float.MaxValue);
        private int _plannedRange = -1;
        private float _maskTimer;
        private float _requestTimer;
        private float _originalFarClip = -1f;
        private readonly Dictionary<ushort, Color> _blockColors = new Dictionary<ushort, Color>();
        private Color _floraWash;
        private float _floraWeight;

        // Background sampling (desktop): one builder at a time goes to the worker, finished ones come back.
        private Thread _worker;
        private readonly object _lock = new object();
        private FarPatchBuilder _workerJob;
        private FarPatchBuilder _workerDone;
        private bool _stopping;
        private FarPatchBuilder _inlineJob; // browser: sampled here, a row at a time

        private readonly System.Diagnostics.Stopwatch _budget = new System.Diagnostics.Stopwatch();

        /// <summary>The far-view range in effect on this world (0 when off, not configured yet, or a void world).</summary>
        public int ActiveRange { get; private set; }

        /// <summary>Patches currently built (diagnostics).</summary>
        public int PatchCount => _patches.Count;

        private static bool Inline => Application.platform == RuntimePlatform.WebGLPlayer;
        private static float BudgetMs => BrowserDevice.IsMobileBrowser ? 1.5f : Application.platform == RuntimePlatform.WebGLPlayer ? 2.5f : 3f;

        private void Start()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/FarTerrain");
            if (shader == null)
            {
                Debug.LogWarning("[FarTerrain] Shader BlocksBeyondTheStars/FarTerrain missing — the far view stays off.");
                enabled = false;
                return;
            }

            _material = new Material(shader) { name = "FarTerrain", enableInstancing = false };
            _mask = new Texture2D(MaskSize, MaskSize, TextureFormat.R8, mipChain: false, linear: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "FarTerrainMask",
            };
            Shader.SetGlobalTexture(MaskTexId, _mask);
            _root = new GameObject("FarTerrain").transform;
            _root.SetParent(transform, false);

            if (!Inline)
            {
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "far-terrain", Priority = System.Threading.ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        /// <summary>Forgets the current world (GameBootstrap.OnWorldReset) — the next world's info and tiles follow.</summary>
        public void ResetWorld()
        {
            _epoch++;
            _info = null;
            _source = null;
            _maskSource = null;
            ActiveRange = 0;
            _inlineJob = null;
            lock (_lock)
            {
                _workerJob = null;
                _workerDone = null;
            }

            ClearPatches();
            _overlay.Reset(WorldConstants.Circumference);
            _plannedRange = -1;
        }

        /// <summary>The settings changed the range live (pause menu).</summary>
        public void SetRange(int blocks)
        {
            RangeSetting = blocks;
            ApplyInfo();
        }

        private void OnWorldInfo(FarTerrainWorldInfo info)
        {
            _info = info;
            _epoch++;
            ClearPatches();
            _overlay.Reset(info.Circumference);
            _plannedRange = -1;
            ApplyInfo();
        }

        private void ApplyInfo()
        {
            if (_info == null || Game == null || Game.Content == null)
            {
                ActiveRange = 0;
                return;
            }

            if (_source == null || _source.WorldId != _info.WorldId)
            {
                _source = FarTerrainSource.Create(Game.Content, Game.WorldSeed, _info);
                _maskSource = FarTerrainSource.Create(Game.Content, Game.WorldSeed, _info);
                _blockColors.Clear();
                PrepareFloraWash();
            }

            ActiveRange = _source == null ? 0 : FarViewRange.ClampToWorld(RangeSetting, _source.Circumference, _source.LatitudePeriod);
            _plannedRange = -1;
            if (ActiveRange == 0)
            {
                ClearPatches();
            }
        }

        private void PrepareFloraWash()
        {
            var planet = _source?.Planet;
            if (planet == null)
            {
                return;
            }

            _floraWeight = Mathf.Min(0.45f, Mathf.Clamp01((float)planet.FloraDensity) * 0.8f);
            var (r, g, b) = FloraTints.For(Game.WorldSeed, Game.LocationName ?? string.Empty,
                planet.SurfaceBlock == "mycelium" ? "mushroom_cap" : "tree_leaves");
            _floraWash = new Color(r, g, b);
        }

        private void Subscribe()
        {
            if (_subscribed || Game?.Network == null)
            {
                return;
            }

            Game.Network.FarTerrainWorldInfoReceived += OnWorldInfo;
            Game.Network.FarTerrainTileReceived += OnTile;
            _subscribed = true;
        }

        private void OnTile(FarTerrainTile tile)
        {
            if (_info != null && (tile.WorldId == 0 || tile.WorldId == _info.WorldId))
            {
                _overlay.Apply(tile);
            }
        }

        private void Update()
        {
            Subscribe();
            bool show = ActiveRange > 0 && _source != null && Game != null && !Game.SpaceViewActive;
            if (_root != null && _root.gameObject.activeSelf != show)
            {
                _root.gameObject.SetActive(show);
            }

            Shader.SetGlobalVector(MaskParamsId, Vector4.zero); // off unless refreshed below
            if (!show)
            {
                return;
            }

            var p = Game.PlayerPosition;
            Shader.SetGlobalVector(CenterId, new Vector4(p.x, p.y, p.z, ActiveRange));
            EnsureFarClip();
            Plan(p);
            MarkChangedTiles();
            RefreshMask(p);
            RequestTiles(p);
            Build(p);
        }

        private void EnsureFarClip()
        {
            if (Camera == null)
            {
                return;
            }

            if (_originalFarClip < 0f)
            {
                _originalFarClip = Camera.farClipPlane;
            }

            float needed = ActiveRange * 1.25f + 128f;
            if (Camera.farClipPlane < needed)
            {
                Camera.farClipPlane = needed;
            }
        }

        private void Plan(Vector3 p)
        {
            float dx = p.x - _plannedAt.x, dz = p.z - _plannedAt.z;
            if (_plannedRange == ActiveRange && dx * dx + dz * dz < PlanMoveBlocks * PlanMoveBlocks)
            {
                return;
            }

            _plannedAt = p;
            _plannedRange = ActiveRange;
            _desired = FarTerrainLayout.Desired(p.x, p.z, ActiveRange);
            _desiredSet.Clear();
            foreach (var k in _desired)
            {
                _desiredSet.Add(k);
            }

            _dropScratch.Clear();
            foreach (var k in _patches.Keys)
            {
                if (!_desiredSet.Contains(k))
                {
                    _dropScratch.Add(k);
                }
            }

            foreach (var k in _dropScratch)
            {
                DestroyPatch(_patches[k]);
                _patches.Remove(k);
            }
        }

        /// <summary>Tiles whose builds changed mark the patches over them for a rebuild (the old mesh stays until then).</summary>
        private void MarkChangedTiles()
        {
            var changed = _overlay.DrainChanged();
            if (changed.Count == 0 || _source == null)
            {
                return;
            }

            var set = new HashSet<(int, int)>(changed);
            foreach (var kv in _patches)
            {
                if (kv.Value.Dirty)
                {
                    continue;
                }

                int size = FarTerrainLayout.PatchSize(kv.Key.Level);
                for (int x = kv.Key.OriginX; x < kv.Key.OriginX + size && !kv.Value.Dirty; x += FarTerrainTile.TileBlocks)
                    for (int z = kv.Key.OriginZ; z < kv.Key.OriginZ + size; z += FarTerrainTile.TileBlocks)
                    {
                        var c = WorldConstants.CanonicalBlock(new Shared.Geometry.Vector3i(x, 0, z), _source.Circumference);
                        if (set.Contains((c.X >> 6, c.Z >> 6)))
                        {
                            kv.Value.Dirty = true;
                            break;
                        }
                    }
            }
        }

        private void RequestTiles(Vector3 p)
        {
            _requestTimer -= Time.deltaTime;
            if (_requestTimer > 0f || Game.Network == null)
            {
                return;
            }

            _requestTimer = TileRequestSeconds;
            var pairs = _overlay.NextRequest(p.x, p.z, ActiveRange, FarTerrainTile.MaxTilesPerRequest / 2, Time.unscaledTimeAsDouble);
            if (pairs.Length > 0)
            {
                Game.Network.SendFarTerrainTileRequest(pairs);
            }
        }

        /// <summary>Marks every chunk column where a real chunk holding the surface is drawn.</summary>
        private void RefreshMask(Vector3 p)
        {
            _maskTimer -= Time.deltaTime;
            int originCx = Mathf.FloorToInt(p.x / WorldConstants.ChunkSize) - MaskSize / 2;
            int originCz = Mathf.FloorToInt(p.z / WorldConstants.ChunkSize) - MaskSize / 2;
            Shader.SetGlobalVector(MaskParamsId, new Vector4(originCx * WorldConstants.ChunkSize, originCz * WorldConstants.ChunkSize,
                1f / (MaskSize * WorldConstants.ChunkSize), 1f));
            if (_maskTimer > 0f && _maskOriginCx == originCx && _maskOriginCz == originCz)
            {
                return;
            }

            _maskTimer = MaskRefreshSeconds;
            _maskOriginCx = originCx;
            _maskOriginCz = originCz;
            System.Array.Clear(_maskBytes, 0, _maskBytes.Length);
            var src = _maskSource;
            Game.ForEachDrawnChunk((sceneX, sceneZ, y) =>
            {
                int cx = Mathf.FloorToInt(sceneX / WorldConstants.ChunkSize) - originCx;
                int cz = Mathf.FloorToInt(sceneZ / WorldConstants.ChunkSize) - originCz;
                if ((uint)cx >= MaskSize || (uint)cz >= MaskSize || src == null)
                {
                    return;
                }

                // The column counts as covered when this chunk holds its visible surface (terrain or sea top).
                var s = src.Sample(Mathf.FloorToInt(sceneX) + 8, Mathf.FloorToInt(sceneZ) + 8, detail: false);
                int surface = s.Top - 1;
                if (surface >= y && surface < y + WorldConstants.ChunkSize)
                {
                    _maskBytes[cz * MaskSize + cx] = 255;
                }
            });
            _mask.SetPixelData(_maskBytes, 0);
            _mask.Apply(false);
        }

        private int _maskOriginCx = int.MinValue;
        private int _maskOriginCz = int.MinValue;

        private void Build(Vector3 p)
        {
            // Collect a finished background build.
            FarPatchBuilder done = null;
            lock (_lock)
            {
                if (_workerDone != null)
                {
                    done = _workerDone;
                    _workerDone = null;
                }
            }

            if (done != null)
            {
                Finish(done);
            }

            if (Inline)
            {
                _budget.Restart();
                while (_budget.Elapsed.TotalMilliseconds < BudgetMs)
                {
                    if (_inlineJob == null)
                    {
                        _inlineJob = NextBuilder();
                        if (_inlineJob == null)
                        {
                            break;
                        }
                    }

                    _inlineJob.SampleRow();
                    if (_inlineJob.Sampled)
                    {
                        var job = _inlineJob;
                        _inlineJob = null;
                        Finish(job);
                    }
                }

                return;
            }

            lock (_lock)
            {
                if (_workerJob == null && _workerDone == null)
                {
                    _workerJob = NextBuilder();
                    if (_workerJob != null)
                    {
                        Monitor.Pulse(_lock);
                    }
                }
            }
        }

        /// <summary>The nearest desired patch that is missing or dirty.</summary>
        private FarPatchBuilder NextBuilder()
        {
            if (_source == null)
            {
                return null;
            }

            foreach (var key in _desired)
            {
                if (!_patches.TryGetValue(key, out var patch) || patch.Dirty)
                {
                    if (patch == null && _patches.Count >= MaxPatches)
                    {
                        return null;
                    }

                    return new FarPatchBuilder(key, ActiveRange, _source, _epoch);
                }
            }

            return null;
        }

        private void WorkerLoop()
        {
            while (true)
            {
                FarPatchBuilder job;
                lock (_lock)
                {
                    while (!_stopping && _workerJob == null)
                    {
                        Monitor.Wait(_lock);
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    job = _workerJob;
                }

                try
                {
                    while (!job.Sampled)
                    {
                        job.SampleRow();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FarTerrain] Sampling {job.Key} failed: {e.Message}");
                }

                lock (_lock)
                {
                    if (_workerJob == job)
                    {
                        _workerJob = null;
                        _workerDone = job;
                    }
                }
            }
        }

        private void Finish(FarPatchBuilder job)
        {
            if (job.Epoch != _epoch || !_desiredSet.Contains(job.Key) || job.Range != ActiveRange || !job.Sampled)
            {
                return; // the world, the range or the player moved on
            }

            var geo = job.BuildGeometry(_overlay, ColorOf);
            if (!_patches.TryGetValue(job.Key, out var patch))
            {
                var go = new GameObject($"Far {job.Key}");
                go.transform.SetParent(_root, false);
                patch = new Patch { Go = go, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
                patch.Renderer.sharedMaterial = _material;
                patch.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                patch.Renderer.receiveShadows = false;
                patch.Renderer.lightProbeUsage = LightProbeUsage.Off;
                patch.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _patches[job.Key] = patch;
            }

            patch.Go.transform.position = new Vector3(job.Key.OriginX, 0f, job.Key.OriginZ);
            var stale = patch.Filter.sharedMesh;
            patch.Filter.sharedMesh = ToMesh(geo, job.Key);
            patch.Dirty = false;
            if (stale != null)
            {
                Destroy(stale);
            }
        }

        private static Mesh ToMesh(FarPatchGeometry geo, FarPatchKey key)
        {
            int n = geo.VertexCount;
            var positions = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color32[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                positions[i] = new Vector3(geo.Positions[i * 3], geo.Positions[i * 3 + 1], geo.Positions[i * 3 + 2]);
                normals[i] = new Vector3(geo.Normals[i * 3], geo.Normals[i * 3 + 1], geo.Normals[i * 3 + 2]);
                uint c = geo.Colors[i];
                colors[i] = new Color32((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF), (byte)((c >> 24) & 0xFF));
                uv[i] = new Vector2(geo.InnerRadius, key.Level);
            }

            var mesh = new Mesh { name = $"Far {key}", indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(geo.Indices, 0, calculateBounds: false);
            int size = FarTerrainLayout.PatchSize(key.Level);
            float minY = geo.MinY, maxY = Mathf.Max(geo.MaxY, geo.MinY + 1f);
            mesh.bounds = new Bounds(new Vector3(size * 0.5f, (minY + maxY) * 0.5f, size * 0.5f), new Vector3(size, maxY - minY, size));
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Vertex colour of a column: the build's block where one stands taller, otherwise the surface block with
        /// the world's vegetation wash, or the sea.</summary>
        private uint ColorOf(FarSample sample, bool edited, FarEditTop edit)
        {
            Color c;
            if (edited)
            {
                c = BlockColor(edit.Block);
                if (edit.Tint != 0)
                {
                    var tint = new Color(((edit.Tint >> 16) & 0xFF) / 255f, ((edit.Tint >> 8) & 0xFF) / 255f, (edit.Tint & 0xFF) / 255f);
                    c = Color.Lerp(c, tint, 0.7f);
                }
            }
            else
            {
                switch (sample.Surface)
                {
                    case FarSurface.Water:
                        c = new Color(0.16f, 0.34f, 0.55f);
                        break;
                    case FarSurface.Lava:
                        c = new Color(1.4f, 0.45f, 0.12f); // HDR-ish: the shader lets lava glow through the haze a little
                        break;
                    default:
                        c = BlockColor(sample.Block);
                        if (_floraWeight > 0.01f)
                        {
                            c = Color.Lerp(c, _floraWash * 0.9f, _floraWeight);
                        }

                        break;
                }
            }

            var c32 = (Color32)new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), sample.Surface == FarSurface.Lava && !edited ? 1f : 0f);
            return (uint)(c32.a << 24 | c32.b << 16 | c32.g << 8 | c32.r);
        }

        private Color BlockColor(ushort id)
        {
            if (!_blockColors.TryGetValue(id, out var c))
            {
                c = Game?.Atlas != null && id != 0 ? Game.Atlas.AverageColor(id) : new Color(0.45f, 0.44f, 0.42f);
                _blockColors[id] = c;
            }

            return c;
        }

        private void ClearPatches()
        {
            foreach (var patch in _patches.Values)
            {
                DestroyPatch(patch);
            }

            _patches.Clear();
            _desired.Clear();
            _desiredSet.Clear();
        }

        private static void DestroyPatch(Patch patch)
        {
            if (patch?.Filter != null && patch.Filter.sharedMesh != null)
            {
                Destroy(patch.Filter.sharedMesh);
            }

            if (patch?.Go != null)
            {
                Destroy(patch.Go);
            }
        }

        private void OnDestroy()
        {
            lock (_lock)
            {
                _stopping = true;
                Monitor.PulseAll(_lock);
            }

            if (_subscribed && Game?.Network != null)
            {
                Game.Network.FarTerrainWorldInfoReceived -= OnWorldInfo;
                Game.Network.FarTerrainTileReceived -= OnTile;
            }

            ClearPatches();
            if (_material != null)
            {
                Destroy(_material);
            }

            if (_mask != null)
            {
                Destroy(_mask);
            }

            Shader.SetGlobalVector(MaskParamsId, Vector4.zero);
            if (Camera != null && _originalFarClip > 0f)
            {
                Camera.farClipPlane = _originalFarClip;
            }
        }
    }
}
