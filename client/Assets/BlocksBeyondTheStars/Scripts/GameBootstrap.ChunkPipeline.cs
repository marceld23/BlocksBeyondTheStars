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
    /// The Minecraft-inspired chunk-pipeline package (#1815): time budgets on the browser's single thread (#1819),
    /// the build order by view and travel (#1818), chunk visibility culling (#1823) and the hooks the far terrain and
    /// its haze read (#1820/#1822). Kept apart from the (very long) GameBootstrap body so the package reads as one piece.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        // ---------------------------------------------------------------- #1819 budgets

        /// <summary>Whether chunk builds and collider cooks run inline on this thread (the browser build).</summary>
        private static bool InlineChunkWork => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>#1819: milliseconds per frame the browser spends on inline chunk builds (at least one build runs).</summary>
        private static float InlineBuildBudgetMs => BrowserDevice.IsMobileBrowser ? 2f : 4f;

        /// <summary>#1819: milliseconds per frame the browser spends on synchronous collider cooks, separately.</summary>
        private static float InlineCookBudgetMs => BrowserDevice.IsMobileBrowser ? 2f : 3f;

        /// <summary>#1819: upper bound on chunk builds dispatched in one frame when a time budget decides (browser).</summary>
        private const int InlineBuildCap = 12;

        /// <summary>#1819: desktop uploads at most this many finished builds per frame (the rest wait a frame).</summary>
        public int MaxChunkUploadsPerFrame = 8;

        private readonly System.Diagnostics.Stopwatch _frameWork = new System.Diagnostics.Stopwatch();

        /// <summary>#1819: browser cooks waiting for their frame budget, by chunk (value = the bake generation).</summary>
        private readonly Dictionary<ChunkCoord, (Mesh Collider, int Gen)> _syncCooks = new Dictionary<ChunkCoord, (Mesh, int)>();
        private readonly List<ChunkCoord> _syncCookScratch = new List<ChunkCoord>();

        /// <summary>#1819: parks a browser collider cook for <see cref="DrainSyncCooks"/> (replacing an older pending one).</summary>
        private void EnqueueSyncCook(ChunkCoord coord, Mesh collider, int bakeGen)
        {
            if (_syncCooks.TryGetValue(coord, out var old) && old.Collider != null && old.Collider != collider)
            {
                Destroy(old.Collider);
            }

            _syncCooks[coord] = (collider, bakeGen);
        }

        /// <summary>#1819: cooks parked browser colliders nearest first within the frame's cook budget. The chunk under
        /// the player always cooks, so the footing is never held back by the budget.</summary>
        private void DrainSyncCooks()
        {
            if (_syncCooks.Count == 0)
            {
                return;
            }

            _syncCookScratch.Clear();
            _syncCookScratch.AddRange(_syncCooks.Keys);
            var pp = PlayerPosition;
            _syncCookScratch.Sort((a, b) => ChunkDistSqToPlayer(a, pp).CompareTo(ChunkDistSqToPlayer(b, pp)));
            _frameWork.Restart();
            float budget = InlineCookBudgetMs;
            for (int i = 0; i < _syncCookScratch.Count; i++)
            {
                var coord = _syncCookScratch[i];
                bool footing = ChunkDistSqToPlayer(coord, pp) <= 24f * 24f;
                if (i > 0 && !footing && _frameWork.Elapsed.TotalMilliseconds >= budget)
                {
                    break;
                }

                var (collider, gen) = _syncCooks[coord];
                _syncCooks.Remove(coord);
                bool current = _colliderGen.TryGetValue(coord, out var wanted) && wanted == gen
                    && _chunkObjects.TryGetValue(coord, out var view) && view?.Collider != null;
                if (!current)
                {
                    Destroy(collider); // superseded while it waited
                    continue;
                }

                var mcol = _chunkObjects[coord].Collider;
                var previous = mcol.sharedMesh;
                mcol.sharedMesh = collider; // cooks on the spot
                _colliderAppliedGen[coord] = gen;
                if (previous != null && previous != collider)
                {
                    Destroy(previous);
                }
            }
        }

        /// <summary>#1819: whether dispatching another build this frame fits the budget.</summary>
        private bool BuildBudgetLeft(int built, int countBudget)
        {
            if (!InlineChunkWork)
            {
                return built < countBudget;
            }

            return built == 0 || (built < InlineBuildCap && _frameWork.Elapsed.TotalMilliseconds < InlineBuildBudgetMs);
        }

        // ---------------------------------------------------------------- #1818 build order

        private Camera _viewCamera;
        private Vector3 _lastMotionSample;
        private float _motionSampleAge = -1f;
        private Vector2 _horizontalVelocity;

        /// <summary>#1818: seconds of the player's current horizontal velocity the build order looks ahead.</summary>
        private const float BuildLookAheadSeconds = 1.25f;

        /// <summary>#1818: samples the player's horizontal velocity (smoothed, reset on teleport-sized jumps).</summary>
        private void SampleMotion()
        {
            var p = PlayerPosition;
            if (_motionSampleAge < 0f)
            {
                _lastMotionSample = p;
                _motionSampleAge = 0f;
                return;
            }

            _motionSampleAge += Time.deltaTime;
            if (_motionSampleAge < 0.1f)
            {
                return;
            }

            var d = new Vector2(p.x - _lastMotionSample.x, p.z - _lastMotionSample.z);
            var v = d.sqrMagnitude > 96f * 96f ? Vector2.zero : d / _motionSampleAge;
            _horizontalVelocity = Vector2.Lerp(_horizontalVelocity, Vector2.ClampMagnitude(v, 120f), 0.5f);
            _lastMotionSample = p;
            _motionSampleAge = 0f;
        }

        /// <summary>#1818: the build-order key of a chunk (lower = sooner).</summary>
        private float BuildOrderKey(ChunkCoord coord, Vector3 player)
        {
            var o = WorldConstants.ChunkOrigin(coord);
            const float half = WorldConstants.ChunkSize * 0.5f;
            float dx = SceneX(o.X) + half - player.x;
            float dy = o.Y + half - player.y;
            float dz = SceneZ(o.Z) + half - player.z;
            if (_viewCamera == null)
            {
                _viewCamera = Camera.main;
            }

            Vector3 fwd = _viewCamera != null ? _viewCamera.transform.forward : Vector3.zero;
            var flat = new Vector2(fwd.x, fwd.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                flat.Normalize();
            }

            var ahead = _horizontalVelocity * BuildLookAheadSeconds;
            return ChunkBuildPriority.Key(dx, dy, dz, flat.x, flat.y, ahead.x, ahead.y);
        }

        // ---------------------------------------------------------------- #1823 visibility culling

        /// <summary>#1823: face connectivity per meshed chunk (canonical coordinates).</summary>
        private readonly Dictionary<ChunkCoord, ushort> _connectivity = new Dictionary<ChunkCoord, ushort>();
        private readonly HashSet<ChunkCoord> _walkVisible = new HashSet<ChunkCoord>();
        private readonly List<ChunkCoord> _walkScratch = new List<ChunkCoord>();
        private bool _visibilityDirty = true;
        private ChunkCoord _visibilityCameraChunk = new ChunkCoord(int.MinValue, 0, 0);
        private bool _visibilityWasOpenSky;
        private float _visibilityAge;

        /// <summary>#1823: whether visibility culling currently hides chunks (off under the open sky).</summary>
        public bool VisibilityCullingActive { get; private set; }

        /// <summary>#1823: chunks the last walk hid (diagnostics).</summary>
        public int ChunksHiddenByVisibility { get; private set; }

        private void RecordConnectivity(ChunkCoord coord, ushort connectivity)
        {
            var key = WorldConstants.CanonicalChunk(coord, Circumference);
            if (!_connectivity.TryGetValue(key, out var old) || old != connectivity)
            {
                _connectivity[key] = connectivity;
                _visibilityDirty = true;
            }
        }

        private void ForgetConnectivity(ChunkCoord coord)
        {
            if (_connectivity.Remove(WorldConstants.CanonicalChunk(coord, Circumference)))
            {
                _visibilityDirty = true;
            }
        }

        /// <summary>#1823: re-walks visibility when the camera enters another chunk, the sky exposure flips, or chunk
        /// connectivity changed (at most 5× a second), then applies it to the chunk renderers.</summary>
        private void UpdateVisibility()
        {
            _visibilityAge += Time.deltaTime;
            if (_viewCamera == null)
            {
                _viewCamera = Camera.main;
            }

            Vector3 camPos = _viewCamera != null ? _viewCamera.transform.position : PlayerPosition;
            var camChunk = new ChunkCoord(Mathf.FloorToInt(camPos.x / WorldConstants.ChunkSize),
                Mathf.FloorToInt(camPos.y / WorldConstants.ChunkSize), Mathf.FloorToInt(camPos.z / WorldConstants.ChunkSize));
            bool openSky = ExposedToSky || SpaceViewActive;
            bool changed = camChunk != _visibilityCameraChunk || openSky != _visibilityWasOpenSky;
            if (!changed && !(_visibilityDirty && _visibilityAge >= 0.2f))
            {
                return;
            }

            _visibilityCameraChunk = camChunk;
            _visibilityWasOpenSky = openSky;
            _visibilityDirty = false;
            _visibilityAge = 0f;
            _walkVisible.Clear();

            // Under the open sky the walk would cull little and risks hiding a distant ridge whose streamed bands do not
            // touch; underground and indoors is where it pays. Off → every chunk counts as visible.
            VisibilityCullingActive = !openSky;
            if (VisibilityCullingActive)
            {
                // The walk runs in the camera's scene space; chunk keys are canonical. SceneX/Z map a canonical origin
                // to the copy nearest the player, so a raw coordinate is looked up by canonicalising it.
                int circ = Circumference;
                _walkScratch.Clear();
                ChunkVisibility.Walk(camChunk, raw =>
                {
                    var key = WorldConstants.CanonicalChunk(raw, circ);
                    if (_connectivity.TryGetValue(key, out var conn))
                    {
                        return conn;
                    }

                    return World.TryGetChunk(key, out _) ? ChunkVisibility.AllConnected : (ushort?)null;
                }, 24, 12, _walkScratch);
                foreach (var raw in _walkScratch)
                {
                    _walkVisible.Add(WorldConstants.CanonicalChunk(raw, circ));
                }
            }

            ApplyChunkRendererStates();
        }

        /// <summary>#1823: whether the visibility walk lets a chunk be drawn.</summary>
        private bool VisibleByWalk(ChunkCoord canonical) => !VisibilityCullingActive || _walkVisible.Contains(canonical);

        /// <summary>URP's current shadow distance (0 when shadows are off).</summary>
        private static float CurrentShadowDistance()
            => GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp ? urp.shadowDistance : 0f;

        /// <summary>Sets every chunk renderer from the distance cull (RepositionChunks keeps
        /// <c>ChunkView.DistanceVisible</c>) and the visibility walk. A hidden chunk inside the shadow distance keeps
        /// casting (shadows only), so a hill behind the view still shades the ground in front of it.</summary>
        private void ApplyChunkRendererStates()
        {
            float shadowSq = CurrentShadowDistance();
            shadowSq *= shadowSq;
            var pp = PlayerPosition;
            int hidden = 0;
            foreach (var kv in _chunkObjects)
            {
                if (kv.Value?.Renderer != null && !ApplyRendererState(kv.Key, kv.Value, ChunkDistSqToPlayer(kv.Key, pp), shadowSq))
                {
                    hidden++;
                }
            }

            ChunksHiddenByVisibility = hidden;
        }

        /// <summary>Applies the distance cull + the visibility walk to one chunk renderer; returns false when the walk
        /// hid a chunk the distance cull would have drawn.</summary>
        private bool ApplyRendererState(ChunkCoord coord, ChunkView view, float distSq, float shadowSq)
        {
            bool walk = VisibleByWalk(WorldConstants.CanonicalChunk(coord, Circumference));
            bool shadowsOnly = !walk && view.DistanceVisible && distSq <= shadowSq;
            bool enabled = view.DistanceVisible && (walk || shadowsOnly);
            if (view.RendererEnabled != enabled)
            {
                view.Renderer.enabled = enabled;
                view.RendererEnabled = enabled;
            }

            if (view.ShadowsOnly != shadowsOnly)
            {
                view.Renderer.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                view.ShadowsOnly = shadowsOnly;
            }

            return walk || !view.DistanceVisible;
        }

        // ---------------------------------------------------------------- #1820 / #1822 far view hooks

        /// <summary>#1820: the far-view range in blocks the world was built with (0 = off), from settings.</summary>
        public int FarViewBlocks;

        /// <summary>#1822: whether this world's distance haze is on right now (Sky; false on airless bodies and in space).</summary>
        public bool FogActive { get; set; } = true;

        /// <summary>#1820: calls <paramref name="visit"/> for every chunk that is meshed and drawn by distance — the far
        /// terrain hides itself over these columns. Scene-space chunk origin X/Z and the chunk's Y range.</summary>
        public void ForEachDrawnChunk(System.Action<float, float, int> visit)
        {
            foreach (var kv in _chunkObjects)
            {
                var view = kv.Value;
                if (view?.Go == null || !view.DistanceVisible)
                {
                    continue;
                }

                var o = WorldConstants.ChunkOrigin(kv.Key);
                visit(SceneX(o.X), SceneZ(o.Z), o.Y);
            }
        }

        /// <summary>#1820: the far terrain view of this world (created by WorldRig).</summary>
        public FarTerrainView FarView;
    }
}
