// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's chunk streaming (#2912): the chunk views and their bookkeeping, chunk arrival (OnChunk), the
    /// dirty / deferred / water-reach queues, mesh dispatch and drain, collider bakes, chunk repositioning across the
    /// seam and the server's block changes (OnBlockChanged). The per-frame budgets, the build order and the visibility
    /// culling live in GameBootstrap.ChunkPipeline.cs.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>A loaded chunk's GameObject plus its cached components, so the per-block-move reposition pass
        /// (RepositionChunks) never calls GetComponent on every loaded chunk — at a large view distance that loop
        /// runs over ~1700 chunks each time the player crosses a block, and GetComponent there was a real cost.
        /// <see cref="ColliderEnabled"/>/<see cref="RendererEnabled"/> cache the current toggle state so we only
        /// flip a component when it actually changes (toggling a MeshCollider adds/removes it from the PhysX
        /// broadphase — doing that needlessly per move is exactly the hitch we're avoiding).</summary>
        private sealed class ChunkView
        {
            public GameObject Go = null!;
            public MeshFilter Filter = null!;
            public MeshRenderer Renderer = null!;
            public MeshCollider Collider = null!;
            public bool ColliderEnabled = true;
            public bool RendererEnabled = true;
            public Vector3 ScenePos;      // last position written to the transform (#1515: write only on change)
            public bool ScenePosKnown;
            public ulong ColliderHash;    // #1529: hash of the collider geometry the chunk carries (or holds pending)
            public Mesh PendingCollider;  // #1529: built but not cooked — the chunk was beyond collider range
            public int PendingColliderGen;
            public bool DistanceVisible = true; // #1823: inside the renderer cull distance (RepositionChunks)
            public bool ShadowsOnly;            // #1823: hidden by the visibility walk but still casting
        }

        private readonly Dictionary<ChunkCoord, ChunkView> _chunkObjects = new Dictionary<ChunkCoord, ChunkView>();
        private readonly HashSet<ChunkCoord> _dirty = new HashSet<ChunkCoord>();

        // #1529: neighbour re-dirties raised by a chunk ARRIVAL are parked here (coord → time parked) instead of
        // entering _dirty at once. With nearest-first streaming the dirty set drains within a few frames while the
        // neighbours keep arriving for seconds, so an already-meshed chunk was rebuilt 3–5× per fresh load — each
        // time with a new render mesh and a collider cook. PromoteDeferredDirty releases an entry once the
        // arrivals settle, all six neighbours are in, it is within reach of the player, or a hard cap elapses.
        // Block edits keep their immediate path (OnBlockChanged marks _dirty directly).
        private readonly Dictionary<ChunkCoord, float> _deferredDirty = new Dictionary<ChunkCoord, float>();
        private readonly List<ChunkCoord> _deferredScratch = new List<ChunkCoord>();
        private const float DeferredDirtySettleSeconds = 0.15f;
        private const float DeferredDirtyMaxSeconds = 1.0f;

        // #1903: chunks whose water surface (shore foam, open-water waves) was changed by a water edit a few blocks
        // AWAY from them. They only need a cosmetic refresh, so a spreading flood — the fluid simulation writes a
        // cell per tick — parks them here and rebuilds each once after WaterReachSettleSeconds instead of every tick.
        private readonly Dictionary<ChunkCoord, float> _waterReachDirty = new Dictionary<ChunkCoord, float>();
        private readonly HashSet<ChunkCoord> _waterReachScratch = new HashSet<ChunkCoord>();
        private const float WaterReachSettleSeconds = 0.5f;

        // When the last chunk arrived from the server. The loading veil uses "no new chunk for a moment" (plus a
        // drained mesh queue) as the "initial view is fully streamed + meshed" signal so it doesn't lift mid-fill
        // (#390). During streaming the ≥1-chunk-per-tick guarantee keeps this fresh; the gap only opens once the
        // server has nothing left to send for the (frozen) spawn view.
        private float _lastChunkArrivalTime;

        /// <summary>Seconds since the last chunk streamed in — grows only once the server has finished the view.</summary>
        public float TimeSinceLastChunk => Time.time - _lastChunkArrivalTime;

        /// <summary>Chunks still queued to be (re)meshed — the client-side mesh backlog.</summary>
        public int PendingMeshCount => _dirty.Count + _deferredDirty.Count + _waterReachDirty.Count;

        // Performance (P1): cap how many chunk meshes are (re)built per frame so a burst of chunks arriving
        // while moving fast spreads over several frames instead of stalling one. Nearest chunks build first;
        // the rest stay queued in _dirty. Tunable — raise for less pop-in, lower for smoother frame times.
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL has no worker threads, so each build + collider cook runs synchronously on the main thread
        // (see DispatchChunkBuild / ApplyChunkMesh). Cap dispatches lower so a burst of arriving chunks spreads
        // over more frames instead of spiking a single one.
        public int MeshChunksPerFrame = 2;
#else
        public int MeshChunksPerFrame = 4;
#endif
        private readonly List<ChunkCoord> _dirtyScratch = new List<ChunkCoord>();
        private readonly List<float> _dirtyDistScratch = new List<float>(); // parallel to _dirtyScratch during dispatch (#1515)

        // Distance culling (A4): chunks farther than this (blocks, seam-aware 3D distance) have their renderer
        // disabled — the client never unloads chunks, so without this the accumulated far chunks keep costing
        // draw calls. Generous default so normally-visible chunks are never culled; lower it to trade view
        // distance for fewer draw calls. Re-evaluated in RepositionChunks (throttled to once per block moved).
        public float ChunkDrawDistanceBlocks = 256f;

        // Performance: collision is only needed where the player can actually walk/dig, so chunks farther than
        // this (blocks, seam-aware 3D distance) keep their baked collision mesh but have the MeshCollider DISABLED.
        // A disabled collider is out of the PhysX broadphase, so RepositionChunks moving its transform every
        // block-moved is cheap — at a high view distance, leaving ~1700 colliders active and shoving them through
        // the static broadphase each move was the main on-foot input-lag/stutter source. Re-enabling is instant
        // (the sharedMesh stays assigned, no re-bake). Generous enough to cover footing, jumps, and digging.
        public float ChunkColliderDistanceBlocks = 96f;

        /// <summary>#1583: seconds of the current descent the collider cook gate looks ahead. Since #1529 a chunk
        /// beyond <see cref="ChunkColliderDistanceBlocks"/> keeps its collision mesh un-cooked until it enters range;
        /// a jetpack drop from 120 m arrives at ~70 blocks/s and would otherwise bring a whole ring of chunks into
        /// range at once and rely on their bakes finishing before the feet do. The gate therefore treats every
        /// chunk along the next few seconds of fall as in range, so the ground is cooked before it is reached.</summary>
        private const float ColliderCookAheadSeconds = 3f;

        /// <summary>Cap on the look-ahead drop (blocks), so a long free fall does not cook the whole column below.</summary>
        private const float ColliderCookAheadMaxBlocks = 160f;

        /// <summary>How far below the player the collider cook gate reaches this frame (0 unless descending).</summary>
        private float ColliderCookAheadDrop()
            => Mathf.Min(Mathf.Max(0f, -PlayerVerticalVelocity) * ColliderCookAheadSeconds, ColliderCookAheadMaxBlocks);

        /// <summary>Squared distance from a chunk's centre to the vertical segment from <paramref name="player"/> down
        /// to <paramref name="drop"/> blocks below it — equals <see cref="ChunkDistSqToPlayer"/> for a drop of 0.</summary>
        private float ChunkDistSqToDescent(ChunkCoord coord, Vector3 player, float drop)
        {
            var o = WorldConstants.ChunkOrigin(coord);
            const float half = WorldConstants.ChunkSize * 0.5f;
            float cy = o.Y + half;
            float dx = SceneX(o.X) + half - player.x;
            float dz = SceneZ(o.Z) + half - player.z;
            float dy = cy > player.y ? cy - player.y : cy < player.y - drop ? player.y - drop - cy : 0f;
            return dx * dx + dy * dy + dz * dz;
        }

        // Performance: the client otherwise NEVER unloads a chunk, so a long exploration grows _chunkObjects (and
        // the per-block-move RepositionChunks loop over it) without bound. Chunks farther than this are destroyed —
        // set well beyond the renderer-cull distance so a chunk that could still be visible is never dropped; the
        // server's far-chunk sweep forgets the same chunks, so they re-stream fresh if the player walks back.
        public float ChunkUnloadDistanceBlocks = 384f;
        private readonly List<ChunkCoord> _unloadScratch = new List<ChunkCoord>();

        // Memory (#1438): on a phone/tablet browser the three distances above must scale with the ACTIVE
        // view distance instead of staying at these desktop-sized constants. There the server only streams
        // a VD+1 radius (VD 3 → ~64 blocks), so with a 384-block unload horizon the resident set is bounded
        // by travel, not by view distance — after a long walk, thousands of chunk objects (each a render
        // mesh + CPU-resident collider mesh + voxel copy) pile up in the wasm heap of a 3-4 GB tablet that
        // OOMs (abortOnCannotGrowMemory). Scaled, not capped: a device that runs at a higher view distance
        // gets its horizon back. Unload stays beyond the draw cull so a visible chunk is never dropped;
        // the collider range never exceeds the cull (nothing interactable lives beyond it on mobile).
        // ViewDistanceChunks is the join-time value the server actually streams at (0 = server default 4).
        private (float Cull, float Collider, float Unload) EffectiveChunkDistances()
        {
            int vd = ViewDistanceChunks > 0 ? ViewDistanceChunks : 4;
            if (!BrowserDevice.IsMobileBrowser)
            {
                // Above view distance 14 the fog edge (vd × 16 blocks) reaches the fixed 256-block cull, which
                // would clip the last hazed ring by chunk centre; grow the cull with the view like mobile does
                // (16 → 288), still well inside the 384-block unload.
                float desktopCull = Mathf.Max(ChunkDrawDistanceBlocks, (vd + 2) * WorldConstants.ChunkSize);
                // #1822: with the far view off the haze is at full strength by vd × 16 on every world that has fog —
                // chunks beyond (vd + 2) × 16 were drawn only to be painted over in the sky colour. Airless worlds
                // (no fog) and a far view (haze pushed out, real chunks preferred over the far terrain) keep the reach.
                if (FarViewBlocks <= 0 && FogActive)
                {
                    desktopCull = (vd + 2) * WorldConstants.ChunkSize;
                }

                return (desktopCull, ChunkColliderDistanceBlocks, ChunkUnloadDistanceBlocks);
            }

            float cull = Mathf.Min(ChunkDrawDistanceBlocks, (vd + 2) * WorldConstants.ChunkSize);
            float collider = Mathf.Min(ChunkColliderDistanceBlocks, cull);
            float unload = Mathf.Min(ChunkUnloadDistanceBlocks, (vd + 4) * WorldConstants.ChunkSize);
            return (cull, collider, unload);
        }

        // Performance (P2): assigning MeshCollider.sharedMesh cooks the collision mesh synchronously on the
        // main thread — the single heaviest per-chunk op. Instead we run Physics.BakeMesh on a worker thread
        // and assign the (now-cached) cook back on the main thread in DrainBakedColliders. _colliderGen +
        // WorldEpoch guard against a newer rebuild (or a world change) superseding an in-flight bake.
        private readonly Dictionary<ChunkCoord, int> _colliderGen = new Dictionary<ChunkCoord, int>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(ChunkCoord Coord, Mesh Collider, int Gen, int Epoch)> _bakedColliders
            = new System.Collections.Concurrent.ConcurrentQueue<(ChunkCoord Coord, Mesh Collider, int Gen, int Epoch)>();

        /// <summary>The bake generation each chunk's collider currently CARRIES (#1488) — lags <see cref="_colliderGen"/>
        /// while a cook is in flight, which is the window the falling-out guard's diagnostics ask about.</summary>
        private readonly Dictionary<ChunkCoord, int> _colliderAppliedGen = new Dictionary<ChunkCoord, int>();

        /// <summary>Whether the chunk under a scene position has a collider cook outstanding — a re-bake in flight
        /// (started, not yet assigned) or, since #1529, a collision mesh built beyond collider range and never
        /// cooked (#1583). Diagnostics only (#1488): the falling-out guard logs it when it fires.</summary>
        public bool ColliderBakePendingAt(Vector3 scenePos)
        {
            var coord = ChunkAtScene(scenePos);
            return _colliderGen.TryGetValue(coord, out int wanted)
                && (!_colliderAppliedGen.TryGetValue(coord, out int applied) || applied != wanted);
        }

        /// <summary>Whether the chunk under a scene position carries the collision it should: meshed, and its
        /// collider cooked and assigned (or nothing to cook). False for a chunk that has not been meshed yet or
        /// whose collision mesh is still un-cooked (#1583) — "air below" is then unknown, not open.</summary>
        public bool ChunkColliderReadyAt(Vector3 scenePos)
        {
            var coord = ChunkAtScene(scenePos);
            return _colliderGen.TryGetValue(coord, out int wanted)
                && _colliderAppliedGen.TryGetValue(coord, out int applied) && applied == wanted;
        }

        private ChunkCoord ChunkAtScene(Vector3 scenePos)
            => WorldConstants.WorldToChunk(new Vector3i(
                Mathf.FloorToInt((float)WorldConstants.WrapX(scenePos.x, Circumference)),
                Mathf.FloorToInt(scenePos.y),
                Mathf.FloorToInt((float)WorldConstants.WrapZ(scenePos.z, Circumference))));

        // Performance (A2): build the chunk GEOMETRY off the main thread too (the heavy triple-loop + coloured
        // light flood-fill). DispatchChunkBuild snapshots the chunk neighbourhood on the main thread, runs
        // ChunkMesher.BuildGeometry on a worker, and enqueues the plain ChunkMeshData; DrainBuiltChunks uploads
        // it to a Unity Mesh + assigns it on the main thread (only the cheap Mesh API copy stays on-thread).
        // _meshGen + WorldEpoch discard a build superseded by a newer edit / world change (same pattern as the
        // collider bake above).
        private readonly Dictionary<ChunkCoord, int> _meshGen = new Dictionary<ChunkCoord, int>();

        // #1550: everything a build needs lives in a pooled MeshJob — the 8 KB block copy is rented, the
        // neighbourhood (with its three delegates) and the shape-dictionary copies are recycled, the light
        // list is reused, and the job runs on the thread pool through one static callback. Before this every
        // dispatch allocated the clone, a neighbourhood, five closures/delegates, a Task and its context —
        // ~1.8 MB/s and 2400 objects/s at 141 builds/s while walking.
        private readonly Stack<MeshJob> _jobPool = new Stack<MeshJob>();
        private readonly Stack<ChunkMesher.Neighbourhood> _hoodPool = new Stack<ChunkMesher.Neighbourhood>();
        private readonly Stack<Dictionary<int, int>> _shapeDictPool = new Stack<Dictionary<int, int>>();
        private static readonly System.Threading.WaitCallback RunMeshJob = o => ((MeshJob)o).Run();

        // #1555: a chunk's block array is rented from the shared pool when it arrives and returned once the chunk
        // is unloaded or replaced — after a grace period, because a mesh job in flight may still read the old
        // ChunkData through its neighbourhood (builds take milliseconds; two seconds is far beyond that).
        private readonly Dictionary<ChunkCoord, ushort[]> _chunkArrays = new Dictionary<ChunkCoord, ushort[]>();

        /// <summary>The pool behind every chunk-sized block array on the client (#1555): the stored chunks, the
        /// dispatch copies. <c>ArrayPool.Shared</c> keeps only a few arrays per size bucket, and the stored
        /// chunks hold hundreds of them for minutes, so it ran dry and both callers allocated afresh. Main thread
        /// only. Bounded so a teardown cannot pin more than ~4 MB.</summary>
        private static class ChunkArrayPool
        {
            private const int MaxKept = 512;
            private static readonly Stack<ushort[]> _free = new Stack<ushort[]>();

            public static ushort[] Rent() => _free.Count > 0 ? _free.Pop() : new ushort[WorldConstants.BlocksPerChunk];

            public static void Return(ushort[] array)
            {
                if (array != null && array.Length == WorldConstants.BlocksPerChunk && _free.Count < MaxKept)
                {
                    _free.Push(array);
                }
            }
        }
        private readonly Queue<(ushort[] Array, float RetiredAt)> _retiredChunkArrays = new Queue<(ushort[], float)>();
        private const float ChunkArrayRetireSeconds = 2f;

        private void RetireChunkArray(ChunkCoord canonical)
        {
            if (_chunkArrays.TryGetValue(canonical, out var old))
            {
                _chunkArrays.Remove(canonical);
                _retiredChunkArrays.Enqueue((old, Time.unscaledTime));
            }
        }

        private void ReturnRetiredChunkArrays()
        {
            float now = Time.unscaledTime;
            while (_retiredChunkArrays.Count > 0 && now - _retiredChunkArrays.Peek().RetiredAt >= ChunkArrayRetireSeconds)
            {
                ChunkArrayPool.Return(_retiredChunkArrays.Dequeue().Array);
            }
        }

        /// <summary>Builds dispatched since start-up (PerfProbe reports the per-phase delta).</summary>
        public static int MeshBuildsDispatched { get; private set; }

        private sealed class MeshJob
        {
            public GameBootstrap Owner;
            public ChunkCoord Coord;
            public ChunkData Center;
            public ushort[] Borrowed;
            public ChunkMesher.Neighbourhood Hood;
            public readonly List<(Vector3i Pos, int Rgb)> Lights = new List<(Vector3i Pos, int Rgb)>();
            public Dictionary<ushort, Color> FloraDict;
            public GameContent Content;
            public BlockTextureAtlas Atlas;
            public System.Func<int, Rect?> DesignUv;
            public int Gen, Epoch;
            public ChunkMeshData Data;
            public string Error;
            public readonly System.Func<BlockId, Color> FloraTintFn;

            public MeshJob()
            {
                // Bound once: reads the job's CURRENT flora map (RebuildFloraTints replaces the map wholesale,
                // so the reference captured at dispatch stays immutable for the build).
                FloraTintFn = id => FloraDict != null && FloraDict.TryGetValue(id.Value, out var c) ? c : Color.black;
            }

            public void Run()
            {
                try
                {
                    Data = ChunkMesher.BuildGeometry(Center, Content, Hood.BlockFn, Atlas, FloraTintFn, null, Lights, Hood.ShapeFn, DesignUv, Hood.LoadedFn);
                }
                catch (System.Exception ex)
                {
                    // Carry the fault back to the main thread: DrainBuiltChunks re-dirties the chunk (it left
                    // _dirty at dispatch — swallowing here made a failed chunk a permanent hole, #421 M10) and
                    // logs rate-limited. A teardown race is filtered there by the epoch/gen guards.
                    Data = null;
                    Error = ex.ToString();
                }

                Owner._builtChunks.Enqueue(this);
            }
        }

        private void RecycleJob(MeshJob job)
        {
            if (job.Borrowed != null)
            {
                ChunkArrayPool.Return(job.Borrowed);
                job.Borrowed = null;
            }

            if (job.Hood != null)
            {
                job.Hood.Clear(_shapeDictPool);
                _hoodPool.Push(job.Hood);
                job.Hood = null;
            }

            job.Lights.Clear();
            job.Center = null;
            job.FloraDict = null;
            job.Content = null;
            job.Atlas = null;
            job.DesignUv = null;
            job.Data = null;
            job.Error = null;
            _jobPool.Push(job);
        }
        private readonly System.Collections.Concurrent.ConcurrentQueue<MeshJob> _builtChunks
            = new System.Collections.Concurrent.ConcurrentQueue<MeshJob>();

        // Failed-build retry bookkeeping (#421 M10): a build that faulted on the worker re-enters _dirty (the
        // coord was already removed at dispatch, so dropping it silently would leave a permanent un-meshed,
        // collider-less 16³ hole). Bounded per chunk so a deterministic mesher fault on one chunk's data can't
        // burn the dispatch budget every frame forever; the counter resets when a build for that chunk succeeds.
        private const int MaxMeshBuildRetries = 5;
        private const float MeshFailLogIntervalSeconds = 5f;
        private readonly Dictionary<ChunkCoord, int> _meshFailCounts = new Dictionary<ChunkCoord, int>();
        private float _lastMeshFailLogTime = float.NegativeInfinity;

        // WebGL ships without worker threads (ProjectSettings webGLThreadsSupport:0), so Task.Run bodies never
        // execute there — the two heavy per-chunk steps above (geometry build + Physics.BakeMesh) would never
        // complete, leaving the world un-meshed and un-collided. The player, released from the spawn settle
        // after its grace, then falls straight through into the void. On WebGL we therefore build the geometry
        // and cook the collider synchronously on the main thread (see the #if UNITY_WEBGL branches in
        // DispatchChunkBuild + ApplyChunkMesh); every other platform keeps the off-thread fast path.

        private int _lastReposX = int.MinValue;
        private int _lastReposY = int.MinValue;
        private int _lastReposZ = int.MinValue;

        /// <summary>Seconds between reposition/unload passes while the player does not move (#966).</summary>
        private const float ReposIdleInterval = 5f;
        private float _sinceReposTick;

        /// <summary>Re-places every loaded chunk GameObject at the seam-aware scene position for the player's
        /// current longitude AND latitude (see <see cref="SceneX"/>/<see cref="SceneZ"/>), and distance-culls
        /// far chunks' renderers (A4).</summary>
        private void RepositionChunks()
        {
            var pp = PlayerPosition;
            var (cull, collider, unload) = EffectiveChunkDistances();
            float cullSq = cull * cull;
            float colliderSq = collider * collider;
            float unloadSq = unload * unload;
            float cookDrop = ColliderCookAheadDrop(); // #1583: the descent the cook gate reaches down along
            float shadowSq = CurrentShadowDistance(); // #1823: hidden chunks inside it keep casting
            shadowSq *= shadowSq;
            _unloadScratch.Clear();
            foreach (var kv in _chunkObjects)
            {
                var view = kv.Value;
                if (view == null || view.Go == null)
                {
                    continue;
                }

                var origin = WorldConstants.ChunkOrigin(kv.Key);
                // #1515: only chunks across a wrap seam ever change scene position, yet every chunk's transform
                // was written per block moved (a native call + transform-change notifications to the renderer
                // and an enabled collider, ~1700 of them at VD 8). Write on change only.
                var scenePos = new Vector3(SceneX(origin.X), origin.Y, SceneZ(origin.Z));
                if (!view.ScenePosKnown || view.ScenePos != scenePos)
                {
                    view.Go.transform.position = scenePos;
                    view.ScenePos = scenePos;
                    view.ScenePosKnown = true;
                }

                float distSq = ChunkDistSqToPlayer(kv.Key, pp);

                // Far-chunk unload: a chunk the player has travelled well past (beyond even the renderer cull) is
                // destroyed so the resident set stays bounded. Collected here, removed after the loop (can't mutate
                // the dictionary mid-iteration). The server forgets it too, so it re-streams if the player returns.
                if (distSq > unloadSq)
                {
                    _unloadScratch.Add(kv.Key);
                    continue;
                }

                // Distance culling: disable the renderer of chunks well beyond the draw distance so the
                // accumulated far chunks stop costing draw calls; re-enabled when the player moves back into
                // range. Frustum culling Unity does for free once the per-chunk bounds are correct.
                // #1823: combined with the visibility walk (ApplyRendererState) — a chunk behind solid rock stays hidden
                // even inside the draw distance.
                view.DistanceVisible = distSq <= cullSq;
                if (view.Renderer != null)
                {
                    ApplyRendererState(kv.Key, view, distSq, shadowSq);
                }

                // Near-only colliders: only chunks within reach keep an ACTIVE collider; far ones are disabled so
                // moving their transform each block costs nothing in PhysX (the broadphase ignores them). The
                // baked sharedMesh stays assigned, so coming back into range re-enables collision instantly.
                bool collide = distSq <= colliderSq;
                if (view.ColliderEnabled != collide && view.Collider != null)
                {
                    view.Collider.enabled = collide;
                    view.ColliderEnabled = collide;
                }

                // #1583: the cook gate is the collider range PLUS the next few seconds of a descent — a chunk the
                // feet are about to reach is cooked before they arrive, not when they do. The collider itself is
                // still enabled only within range; the cooked mesh waits on the (disabled) MeshCollider.
                bool cook = collide || (cookDrop > 0f && ChunkDistSqToDescent(kv.Key, pp, cookDrop) <= colliderSq);
                if (cook && view.PendingCollider != null)
                {
                    // #1529: the chunk entered collider range — cook the collider it has been holding un-baked.
                    var pending = view.PendingCollider;
                    view.PendingCollider = null;
                    if (_colliderGen.TryGetValue(kv.Key, out var wanted) && wanted == view.PendingColliderGen)
                    {
                        StartColliderBake(kv.Key, view, pending, view.PendingColliderGen);
                    }
                    else
                    {
                        Destroy(pending); // superseded by a newer rebuild
                    }
                }
            }

            // Process the far-chunk unloads collected above (after iterating, so the dictionary isn't mutated
            // mid-loop). Drop every bit of per-chunk bookkeeping so nothing dangles, and the world data + its
            // light sources, then destroy the GameObject. A re-stream rebuilds it from scratch.
            for (int i = 0; i < _unloadScratch.Count; i++)
            {
                var coord = _unloadScratch[i];
                if (_chunkObjects.TryGetValue(coord, out var view))
                {
                    DestroyChunkView(view); // frees the chunk's meshes too, not just the GameObject
                }

                _chunkObjects.Remove(coord);
                _dirty.Remove(coord);
                _deferredDirty.Remove(coord);
                _waterReachDirty.Remove(coord);
                _colliderGen.Remove(coord);
                _colliderAppliedGen.Remove(coord);
                _meshGen.Remove(coord);
                _meshFailCounts.Remove(coord);
                ForgetConnectivity(coord); // #1823
                if (_syncCooks.TryGetValue(coord, out var parkedCook)) // #1819: a browser cook still waiting
                {
                    Destroy(parkedCook.Collider);
                    _syncCooks.Remove(coord);
                }

                World.RemoveChunk(coord);
                RetireChunkArray(WorldConstants.CanonicalChunk(coord, Circumference)); // #1555
            }
        }

        private void OnChunk(BlocksBeyondTheStars.Networking.Messages.ChunkDataMessage m)
        {
            var coord = new ChunkCoord(m.Cx, m.Cy, m.Cz);

            // Reject a malformed/short chunk payload: ChunkData.FromRaw requires exactly BlocksPerChunk cells and
            // throws otherwise, which would bubble out of the network dispatch. Drop + warn instead, so a bad
            // packet can neither crash the client nor be stored as solid terrain (which the player would read as
            // a floor and then fall through). DecodeBlocks handles both representations the server may send —
            // the RLE payload (usual) or the dense array — and returns null for an undecodable RLE stream, so
            // this only fires on a genuinely truncated/corrupt payload; the warning is the diagnostic to watch.
            int expected = WorldConstants.BlocksPerChunk;
            // #1555: decode straight into a pooled array that the chunk keeps until it unloads.
            var blocks = ChunkArrayPool.Rent();
            if (!m.DecodeBlocksInto(blocks, expected))
            {
                ChunkArrayPool.Return(blocks);

                Debug.LogWarning($"[Chunk] Dropped malformed chunk {coord}: expected {expected} blocks, got "
                    + (m.BlocksRle != null && m.BlocksRle.Length > 0 ? $"an undecodable RLE payload ({m.BlocksRle.Length} entries)" : $"{m.Blocks?.Length ?? 0}") + ".");
                return;
            }

            World.StoreChunk(coord, blocks, m.ModIndex, m.ModTint, m.ModGlow, m.ShapeIndex, m.ShapeData);
            var canonical = WorldConstants.CanonicalChunk(coord, Circumference);
            RetireChunkArray(canonical); // a re-sent chunk replaces its array; the old one goes back after the grace period
            _chunkArrays[canonical] = blocks;
            MarkChunkAndNeighborsDirty(coord);
            DeferLitNeighbours(coord); // #2951: its lamps reach the edge and corner neighbours too
            _lastChunkArrivalTime = Time.time; // feed the view-settle gate (#390)

            // #1113: remember this chunk's map cell for the session — the chunk itself may unload again
            // behind the player, but the planet map's fog stays lifted where they have already been.
            int cell = BlocksBeyondTheStars.Shared.World.ExploredMap.CellIndex(coord.X, coord.Z, Circumference);
            if (cell >= 0)
            {
                SessionExploredCells.Add(cell);
            }
        }

        /// <summary>Marks a chunk AND its six neighbours for re-meshing. A freshly stored/resynced chunk
        /// changes which boundary faces its neighbours must draw (e.g. the stale-chunk resync after a fast
        /// double-mine) — re-meshing only the chunk itself left see-through holes in the neighbours' walls.
        /// Cheap: only chunks that already have a mesh actually rebuild.</summary>
        private void MarkChunkAndNeighborsDirty(ChunkCoord coord)
        {
            _dirty.Add(coord);
            _deferredDirty.Remove(coord);
            float now = Time.time;
            foreach (var d in _faceDirs)
            {
                var n = new ChunkCoord(
                    WorldConstants.CanonicalChunkX(coord.X + d.X, Circumference),
                    coord.Y + d.Y,
                    WorldConstants.CanonicalChunkZ(coord.Z + d.Z, Circumference));
                if (_dirty.Contains(n))
                {
                    continue; // already queued — nothing to defer
                }

                if (_meshGen.ContainsKey(n))
                {
                    // #1529: a neighbour that already has (or is building) a mesh only needs its seam faces
                    // refreshed — parked until the arrivals settle, see PromoteDeferredDirty.
                    if (!_deferredDirty.ContainsKey(n))
                    {
                        _deferredDirty[n] = now;
                    }
                }
                else
                {
                    _dirty.Add(n); // never dispatched: either already pending or not streamed yet (a no-op at dispatch)
                }
            }
        }

        /// <summary>#1529: releases parked neighbour re-dirties into <see cref="_dirty"/> once the streaming burst
        /// has settled (no arrival for <see cref="DeferredDirtySettleSeconds"/>), all six neighbours of the chunk
        /// are present (its seams cannot change again), the chunk is within reach of the player (its seam faces
        /// are in view), or the entry is older than <see cref="DeferredDirtyMaxSeconds"/>.</summary>
        private void PromoteDeferredDirty()
        {
            if (_deferredDirty.Count == 0)
            {
                return;
            }

            float now = Time.time;
            bool settled = now - _lastChunkArrivalTime >= DeferredDirtySettleSeconds;
            var pp = PlayerPosition;
            const float near = 2 * WorldConstants.ChunkSize + 8; // within ~2 chunks of the player
            _deferredScratch.Clear();
            foreach (var kv in _deferredDirty)
            {
                if (settled || now - kv.Value >= DeferredDirtyMaxSeconds
                    || ChunkDistSqToPlayer(kv.Key, pp) <= near * near || AllNeighboursLoaded(kv.Key))
                {
                    _deferredScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _deferredScratch.Count; i++)
            {
                _deferredDirty.Remove(_deferredScratch[i]);
                _dirty.Add(_deferredScratch[i]);
            }
        }

        /// <summary>#1903: releases the water-reach refreshes parked by <see cref="MarkWaterReachDirty"/> once they have
        /// waited <see cref="WaterReachSettleSeconds"/> (measured from the FIRST park, so a long flood still refreshes).</summary>
        private void PromoteWaterReachDirty()
        {
            if (_waterReachDirty.Count == 0)
            {
                return;
            }

            float now = Time.time;
            _deferredScratch.Clear();
            foreach (var kv in _waterReachDirty)
            {
                if (now - kv.Value >= WaterReachSettleSeconds)
                {
                    _deferredScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _deferredScratch.Count; i++)
            {
                _waterReachDirty.Remove(_deferredScratch[i]);
                _dirty.Add(_deferredScratch[i]);
            }
        }

        /// <summary>#1903: a cell turned into or out of water. Every other already-meshed chunk whose shore foam / wave
        /// weights read that cell is parked for a refresh (the edited chunk and its face neighbours rebuild at once).</summary>
        private void MarkWaterReachDirty(int wx, int wy, int wz)
        {
            _waterReachScratch.Clear();
            WaterSurface.ChunksInWaterReach(wx, wy, wz, Circumference, _waterReachScratch);
            float now = Time.time;
            foreach (var c in _waterReachScratch)
            {
                if (!_dirty.Contains(c) && _meshGen.ContainsKey(c) && !_waterReachDirty.ContainsKey(c))
                {
                    _waterReachDirty[c] = now;
                }
            }
        }

        private bool AllNeighboursLoaded(ChunkCoord c)
        {
            foreach (var d in _faceDirs)
            {
                var n = new ChunkCoord(
                    WorldConstants.CanonicalChunkX(c.X + d.X, Circumference), c.Y + d.Y,
                    WorldConstants.CanonicalChunkZ(c.Z + d.Z, Circumference));
                if (!World.TryGetChunk(n, out _))
                {
                    return false;
                }
            }

            return true;
        }
        /// <summary>Destroys a chunk's GameObject AND its procedurally-built render + collision meshes. Unity
        /// does NOT free a MeshFilter/MeshCollider's sharedMesh when the GameObject is destroyed, so without
        /// this every unloaded chunk (far-chunk eviction, world reset) orphans two per-chunk meshes — native
        /// memory that is never reclaimed and grows across a long session.</summary>
        private void DestroyChunkView(ChunkView view)
        {
            if (view == null)
            {
                return;
            }

            if (view.Filter != null && view.Filter.sharedMesh != null)
            {
                Destroy(view.Filter.sharedMesh);
            }

            if (view.Collider != null && view.Collider.sharedMesh != null)
            {
                Destroy(view.Collider.sharedMesh);
            }

            if (view.PendingCollider != null)
            {
                Destroy(view.PendingCollider); // #1529: a collider built but never cooked
                view.PendingCollider = null;
            }

            if (view.Go != null)
            {
                Destroy(view.Go);
            }
        }

        private static readonly (int X, int Y, int Z)[] _faceDirs =
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };

        /// <summary>Raised after a server block change was applied to the local world: position, the block
        /// that WAS in the cell and the block that is there now. The raw network message alone cannot tell
        /// a drained fluid from a mined block — audio needs the transition (#655).</summary>
        public event System.Action<Vector3i, BlockId, BlockId> BlockChangeApplied;

        private void OnBlockChanged(BlocksBeyondTheStars.Networking.Messages.BlockChanged m)
        {
            var oldId = World.GetBlock(m.X, m.Y, m.Z);
            int oldLight = World.LightSourceAt(m.X, m.Y, m.Z), reachBefore = World.LightReachThrough(m.X, m.Y, m.Z); // #2951
            if (!World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, m.Tint, m.Glow, m.Shape, out var coord))
            {
                return;
            }

            BlockChangeApplied?.Invoke(new Vector3i(m.X, m.Y, m.Z), oldId, new BlockId(m.Block));

            // A cell that just became solid where we are standing must lift us out, not swallow us: the server
            // allows placing into your own feet cell (pillar jumping), and mid-fall that used to drop the player
            // straight through the world. Every block change funnels through here, so another player filling the
            // cell is covered too. ONLY blocks that actually collide count: the fluid simulation writes water
            // cells into the swimmer's column (mining/placing near a body wakes it), and lifting on those
            // teleported the diver out of the water onto the surface.
            if (m.Block != BlockId.AirValue && Player != null
                && PlayerController.IsCollidingKey(Content?.BlockById(new BlockId(m.Block))?.Key))
            {
                Player.LiftOutOfBlockAt(m.X, m.Y, m.Z);
            }

            _dirty.Add(coord);
            MarkEditNeighboursDirty(m, coord, oldId, oldLight, reachBefore);
        }

        /// <summary>(A2) Kicks off an OFF-THREAD geometry build for one chunk; returns false (no work, no budget
        /// spent) when the chunk isn't streamed yet. The chunk neighbourhood is SNAPSHOTTED on the main thread so
        /// the worker never races a live edit: the centre chunk is deep-copied; neighbour BLOCK reads go through
        /// ChunkData.Get (atomic ushort reads — a concurrent edit only yields stale data, and the chunk is
        /// re-dirtied/rebuilt anyway), while the sparse SHAPE/modifier dictionaries (which a live edit would
        /// structurally mutate) are deep-copied. _meshGen + WorldEpoch let DrainBuiltChunks discard a build
        /// superseded by a newer edit or a world change. Content/Atlas/the flora-tint map are read-safe: set
        /// once, or replaced wholesale by RebuildFloraTints, never mutated in place.
        /// NEEDS RUNTIME STRESS VERIFICATION — threading correctness is not exercised by a plain build.</summary>
        private bool DispatchChunkBuild(ChunkCoord coord)
        {
            if (!World.TryGetChunk(coord, out var chunk))
            {
                return false;
            }

            int circ = Circumference;
            var job = _jobPool.Count > 0 ? _jobPool.Pop() : new MeshJob { Owner = this };
            job.Coord = coord;

            // Centre chunk: a private deep copy, so the worker reads its blocks/modifiers/shapes consistently.
            // The block array is rented (#1550) and goes back to the pool in DrainBuiltChunks.
            job.Center = CopyChunk(chunk, out job.Borrowed);

            // Block reads reach up to ~±18 blocks (the coloured-light flood-fill) and a vertical band a few chunks
            // tall (the skylight column scan) → capture chunk REFERENCES over a ±2 horizontal / -2..+4 vertical
            // neighbourhood. Reads use ChunkData.Get (atomic, no dictionary), so a live reference is safe. Key by
            // the chunk's own canonical Coord, exactly as ClientWorld stores + looks them up.
            // #1528: a fixed 5×7×5 slot array (ChunkMesher.Neighbourhood) instead of a coord-keyed dictionary —
            // each slot holds the CANONICAL chunk of its offset, and the mesher's block reads resolve with a shift
            // and a mask. Shape reads reach ±1 block (the neighbour-shape face test) → the sparse shape dicts are
            // deep-copied per slot on the main thread (the worker must NOT read a live ChunkData._shapes
            // concurrently with an edit). #1550: the neighbourhood and the dictionary copies are pooled.
            var hood = _hoodPool.Count > 0 ? _hoodPool.Pop() : new ChunkMesher.Neighbourhood(coord);
            hood.Reset(coord);
            job.Hood = hood;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 4; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        ChunkData nch;
                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            nch = job.Center;
                        }
                        else
                        {
                            var nc = new ChunkCoord(
                                WorldConstants.CanonicalChunkX(coord.X + dx, circ), coord.Y + dy,
                                WorldConstants.CanonicalChunkZ(coord.Z + dz, circ));
                            if (!World.TryGetChunk(nc, out nch))
                            {
                                continue;
                            }
                        }

                        Dictionary<int, int> copy = null;
                        if (nch.Shapes is { Count: > 0 } src)
                        {
                            copy = _shapeDictPool.Count > 0 ? _shapeDictPool.Pop() : new Dictionary<int, int>(src.Count);
                            foreach (var s in src)
                            {
                                copy[s.Key] = s.Value;
                            }
                        }

                        hood.Set(dx, dy, dz, nch, copy);
                    }

            World.LightSourcesNear(coord, ChunkMesher.LightRadius, job.Lights);
            job.FloraDict = _floraTintByBlock; // RebuildFloraTints REPLACES this map, so the captured ref is immutable
            job.Gen = (_meshGen.TryGetValue(coord, out var g) ? g : 0) + 1;
            _meshGen[coord] = job.Gen;
            job.Epoch = WorldEpoch;
            job.Content = Content;
            job.Atlas = Atlas;
            // Paint-design lookup snapshot (#819): an immutable id → UV map, safe on the worker thread.
            job.DesignUv = PaintAtlas?.Snapshot();
            job.Data = null;
            job.Error = null;
            MeshBuildsDispatched++;

            if (InlineChunkWork)
            {
                // WebGL: no worker threads — build inline on the main thread (within the #1819 frame budget).
                // DrainBuiltChunks still uploads the result, exactly as it does for the async path elsewhere.
                // A runtime check (not #if) so the PR CI and the desktop build compile both paths.
                job.Run();
            }
            else
            {
                // One static callback + the job as state: no Task, no closure, no execution-context capture.
                System.Threading.ThreadPool.UnsafeQueueUserWorkItem(RunMeshJob, job);
            }

            return true;
        }

        /// <summary>Deep-copies a chunk (block array + the sparse colour-modifier and shape dictionaries) so a
        /// worker thread can read it without racing a live edit on the main thread.</summary>
        private static ChunkData CopyChunk(ChunkData src, out ushort[] borrowed)
        {
            // #1550: the 8 KB block copy comes from the chunk-array pool and goes back in DrainBuiltChunks.
            var blocks = ChunkArrayPool.Rent();
            borrowed = blocks;
            src.RawBlocks.CopyTo(blocks);
            var copy = ChunkData.FromRaw(src.Coord, blocks);
            if (src.Modifiers is { Count: > 0 } mods)
            {
                foreach (var kv in mods)
                {
                    copy.SetModifierLocal(kv.Key, kv.Value.Tint, kv.Value.Glow);
                }
            }

            if (src.Shapes is { Count: > 0 } shapes)
            {
                foreach (var kv in shapes)
                {
                    copy.SetShapeLocal(kv.Key, kv.Value);
                }
            }

            return copy;
        }

        /// <summary>Uploads + assigns finished off-thread chunk builds whose generation/world still match (drops
        /// superseded or world-changed builds). Mirrors <see cref="DrainBakedColliders"/>.</summary>
        private void DrainBuiltChunks()
        {
            // #1819: on desktop the workers can finish a burst between two frames; uploading all of them at once is a
            // spike of its own. Cap the uploads per frame — the rest stay queued (in completion order) for the next.
            int uploads = 0;
            int uploadCap = InlineChunkWork ? int.MaxValue : Mathf.Max(1, MaxChunkUploadsPerFrame);
            while (uploads < uploadCap && _builtChunks.TryDequeue(out var built))
            {
                var job = built; // recycled on every exit path below (#1550)
                if (built.Data == null)
                {
                    // Failed build (#421 M10): the coord already left _dirty at dispatch, so dropping it here
                    // means the chunk never re-meshes — a permanent hole with no collider. Re-dirty it so the
                    // budgeted dispatch loop retries, unless the world changed / a newer build for this chunk is
                    // already in flight (the teardown-race case the old silent drop was guarding against).
                    if (built.Epoch == WorldEpoch
                        && _meshGen.TryGetValue(built.Coord, out var failedGen) && failedGen == built.Gen)
                    {
                        int fails = (_meshFailCounts.TryGetValue(built.Coord, out var f) ? f : 0) + 1;
                        _meshFailCounts[built.Coord] = fails;
                        if (fails <= MaxMeshBuildRetries)
                        {
                            _dirty.Add(built.Coord);
                        }

                        // Rate-limited: one line per interval however many chunks fail, plus one final line for
                        // a chunk we give up on. LogError (not LogException) — diagnostics, not a crash report.
                        if (fails > MaxMeshBuildRetries || Time.unscaledTime - _lastMeshFailLogTime >= MeshFailLogIntervalSeconds)
                        {
                            _lastMeshFailLogTime = Time.unscaledTime;
                            string give = fails > MaxMeshBuildRetries ? $" — giving up after {MaxMeshBuildRetries} retries (permanent hole)" : $" — retrying ({fails}/{MaxMeshBuildRetries})";
                            Debug.LogError($"[Chunk] Mesh build failed for {built.Coord}{give}: {built.Error ?? "unknown"}");
                        }
                    }

                    RecycleJob(job);
                    continue;
                }

                if (built.Epoch != WorldEpoch
                    || !_meshGen.TryGetValue(built.Coord, out var gen) || gen != built.Gen)
                {
                    built.Data.Release(); // superseded by a newer edit / a world change — recycle the buffers
                    RecycleJob(job);
                    continue;
                }

                _meshFailCounts.Remove(built.Coord); // healthy again — a later transient fault gets fresh retries
                ApplyChunkMesh(built.Coord, built.Data);
                uploads++;
                // The upload copied everything into the Unity meshes (+ GroundScatter's matrices), so the
                // pooled geometry buffers can go back for the next build.
                built.Data.Release();
                RecycleJob(job);
            }
        }

        /// <summary>Main-thread upload of a built chunk: turns the <see cref="ChunkMeshData"/> into the render
        /// mesh, (re)creates the chunk GameObject if needed, assigns the mesh, and kicks off the off-thread
        /// collider bake (P2). A null collider (only fluids/air) clears the collider immediately.</summary>
        private void ApplyChunkMesh(ChunkCoord coord, ChunkMeshData data)
        {
            // The render mesh is uploaded non-readable (#966), which rules out the old Clear()+refill reuse — a
            // non-readable mesh cannot be rewritten. So every rebuild gets a fresh mesh and the outgoing one is
            // destroyed below; Unity never collects Mesh objects, and A2's rebuild rate would grow that leak fast.
            bool exists = _chunkObjects.TryGetValue(coord, out var view) && view?.Go != null;
            RecordConnectivity(coord, data.Connectivity); // #1823: the visibility walk reads what this build found
            // #1529: a rebuild whose collider geometry hashes identically to what the chunk already carries (or
            // holds pending) needs no new collision Mesh and no cook — tint/glow/paint edits and neighbour
            // re-dirties are the common case.
            bool colliderUnchanged = exists && data.ColliderHash != 0 && view.ColliderHash == data.ColliderHash
                && ((view.Collider != null && view.Collider.sharedMesh != null) || view.PendingCollider != null);
            var (mesh, collider) = data.ToMeshes(buildCollider: !colliderUnchanged);

            // #1515: an all-air (or all-fluid-free, geometry-less) chunk gets NO GameObject. A large share of the
            // streamed set at a big view distance is sky, and each empty chunk used to carry a GameObject + three
            // components + three materials + an empty mesh with full 16³ bounds — a transform write per block
            // moved and a culling entry each. The chunk re-materialises through the normal path the moment an
            // edit gives it geometry (the rebuild then finds no view and creates one).
            if (!exists && collider == null && (mesh == null || mesh.vertexCount == 0) && (data.Scatter == null || data.Scatter.Count == 0))
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }

                int emptyGen = (_colliderGen.TryGetValue(coord, out var eg) ? eg : 0) + 1;
                _colliderGen[coord] = emptyGen;
                _colliderAppliedGen[coord] = emptyGen; // nothing to cook — no fall-guard "pending" state either
                return;
            }

            if (!exists)
            {
                var go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(transform);
                var origin = WorldConstants.ChunkOrigin(coord);
                var scenePos = new Vector3(SceneX(origin.X), origin.Y, SceneZ(origin.Z)); // seam-aware on both ground axes (torus)
                go.transform.position = scenePos;
                var filter = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                // Submesh 0 → opaque atlas, submesh 1 → see-through atlas (glass/fields), submesh 2 → paint
                // designs. Fall back to fewer materials if a shader is missing (a spare submesh just won't draw).
                mr.sharedMaterials = ChunkMaterialTransparent != null && ChunkMaterialPaint != null
                    ? new[] { ChunkMaterial, ChunkMaterialTransparent, ChunkMaterialPaint }
                    : ChunkMaterialTransparent != null
                        ? new[] { ChunkMaterial, ChunkMaterialTransparent }
                        : new[] { ChunkMaterial };
                var mc = go.AddComponent<MeshCollider>();
                view = new ChunkView { Go = go, Filter = filter, Renderer = mr, Collider = mc, ScenePos = scenePos, ScenePosKnown = true };
                _chunkObjects[coord] = view;
                // #1823: a chunk arriving behind solid rock starts hidden (the next reposition refines the distance cull).
                float shadow = CurrentShadowDistance();
                ApplyRendererState(coord, view, ChunkDistSqToPlayer(coord, PlayerPosition), shadow * shadow);
            }

            var staleMesh = view!.Filter.sharedMesh; // the mesh this rebuild replaces (null on the first build)
            view.Filter.sharedMesh = mesh;
            if (staleMesh != null && staleMesh != mesh)
            {
                Destroy(staleMesh);
            }

            // Ground-detail scatter (T0): (re)build this chunk's instanced tuft/pebble decoration. Render-only,
            // distance-culled + quality-gated inside the component; a chunk with no scatter points disables it.
            var scatterGo = view.Go;
            var scatter = scatterGo.GetComponent<GroundScatter>();
            if (data.Scatter != null && data.Scatter.Count > 0)
            {
                if (scatter == null)
                {
                    scatter = scatterGo.AddComponent<GroundScatter>();
                }

                scatter.Setup(data.Scatter);
            }
            else if (scatter != null)
            {
                scatter.Setup(null);
            }

            // The collider uses the solid-only mesh (fluids excluded) so the player can swim into water/lava.
            // Baking the collision mesh is the heaviest per-chunk step, so do it off the main thread (P2): bump
            // this chunk's bake generation, cook on a worker via Physics.BakeMesh, then assign the cached cook
            // in DrainBakedColliders.
            int bakeGen = (_colliderGen.TryGetValue(coord, out var g) ? g : 0) + 1;
            _colliderGen[coord] = bakeGen;
            var mcol = view!.Collider;
            if (colliderUnchanged)
            {
                // #1529: the cooked collider the chunk carries (or the un-cooked one it holds pending) is still right.
                if (view.PendingCollider != null)
                {
                    // #1583: still un-cooked — the chunk awaits its first cook, so it must NOT count as applied
                    // (ColliderBakePendingAt read "not pending" for a chunk whose MeshCollider had no mesh).
                    view.PendingColliderGen = bakeGen;
                }
                else
                {
                    _colliderAppliedGen[coord] = bakeGen;
                }
            }
            else if (collider == null)
            {
                view.ColliderHash = data.ColliderHash;
                if (view.PendingCollider != null)
                {
                    Destroy(view.PendingCollider);
                    view.PendingCollider = null;
                }

                if (mcol != null && mcol.sharedMesh != null)
                {
                    var stale = mcol.sharedMesh; // chunk became all-fluid/air — free the outgoing collision mesh
                    mcol.sharedMesh = null;
                    Destroy(stale);
                }

                _colliderAppliedGen[coord] = bakeGen; // #1493: nothing to cook — the chunk carries this generation now
            }
            else
            {
                view.ColliderHash = data.ColliderHash;
                if (view.PendingCollider != null)
                {
                    Destroy(view.PendingCollider);
                    view.PendingCollider = null;
                }

                var (_, colliderRange, _) = EffectiveChunkDistances();
                if (ChunkDistSqToDescent(coord, PlayerPosition, ColliderCookAheadDrop()) > colliderRange * colliderRange)
                {
                    // #1529: beyond collider range the MeshCollider is disabled anyway (RepositionChunks) — keep the
                    // un-cooked mesh and cook it when the chunk comes into range. At VD 8 that was ~44 % of all cooks.
                    view.PendingCollider = collider;
                    view.PendingColliderGen = bakeGen;
                }
                else
                {
                    StartColliderBake(coord, view, collider, bakeGen);
                }
            }
        }

        /// <summary>Cooks a chunk's collision mesh: synchronously on WebGL (assigning cooks on the spot), on a
        /// worker via <see cref="Physics.BakeMesh"/> elsewhere (DrainBakedColliders assigns the cached cook).</summary>
        private void StartColliderBake(ChunkCoord coord, ChunkView view, Mesh collider, int bakeGen)
        {
            var mcol = view.Collider;
            if (mcol == null)
            {
                Destroy(collider);
                return;
            }

            // WebGL: no worker threads to bake on, so the mesh is cooked synchronously by assigning it directly —
            // MeshCollider.sharedMesh cooks on the spot. #1819: cooked by DrainSyncCooks within the frame's cook
            // budget, nearest first; the footing chunk always cooks the frame it is drained, so the spawn ground-check
            // raycast still finds its floor. A runtime check (not #if) so the PR CI and the desktop build compile it.
            if (InlineChunkWork)
            {
                EnqueueSyncCook(coord, collider, bakeGen);
                return;
            }

            var meshId = collider.GetEntityId();
            var capturedCoord = coord;
            var capturedCollider = collider;
            int epoch = WorldEpoch;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Physics.BakeMesh(meshId, false);
                }
                catch
                {
                    // Mesh may have been destroyed before the bake ran; DrainBakedColliders drops stale ones.
                }

                _bakedColliders.Enqueue((capturedCoord, capturedCollider, bakeGen, epoch));
            });
        }

        /// <summary>Assigns collision meshes whose off-thread <see cref="Physics.BakeMesh"/> has finished.
        /// Skips and frees any whose chunk was re-meshed since the bake started, whose world changed, or whose
        /// chunk GameObject is gone — so a stale cook never lands on the wrong chunk.</summary>
        private void DrainBakedColliders()
        {
            while (_bakedColliders.TryDequeue(out var baked))
            {
                bool assigned = false;
                if (baked.Epoch == WorldEpoch
                    && _colliderGen.TryGetValue(baked.Coord, out var gen) && gen == baked.Gen
                    && _chunkObjects.TryGetValue(baked.Coord, out var view) && view?.Go != null)
                {
                    var mc = view.Collider;
                    if (mc != null)
                    {
                        var old = mc.sharedMesh; // collider meshes are always freshly cooked (never reused) —
                        mc.sharedMesh = baked.Collider; // cook cached by BakeMesh → cheap assign, no main-thread stall
                        if (old != null && old != baked.Collider)
                        {
                            Destroy(old); // …so the outgoing one is orphaned unless we free it (leak on every remesh)
                        }

                        _colliderAppliedGen[baked.Coord] = baked.Gen; // #1488: the collider now carries this bake
                        assigned = true;
                    }
                }

                if (!assigned && baked.Collider != null)
                {
                    Destroy(baked.Collider); // superseded / world changed / chunk gone → free the throwaway mesh
                }
            }
        }

        /// <summary>Squared distance from the player to a chunk's centre, using the same seam-aware scene
        /// mapping the chunk objects are placed with — so "nearest first" is correct on the wrapping torus.</summary>
        private float ChunkDistSqToPlayer(ChunkCoord coord, Vector3 player)
        {
            var o = WorldConstants.ChunkOrigin(coord);
            const float half = WorldConstants.ChunkSize * 0.5f;
            float dx = SceneX(o.X) + half - player.x;
            float dy = o.Y + half - player.y;
            float dz = SceneZ(o.Z) + half - player.z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>The atlas <c>AnimationVersion</c> the loaded chunks were meshed with (#1957) — a change re-meshes
        /// them in <see cref="OnAtlasRepainted"/>.</summary>
        private int _animationVersionSeen;

        /// <summary>The block atlas repainted tiles in place (a texture layer changed, #1952). Chunk meshes keep
        /// their UVs, so they stay; everything that BAKED a tile's colour or pixels has to go.</summary>
        private void OnAtlasRepainted()
        {
            IconResolver.ClearCache();
            ShapeIconFactory.ClearCache();
            if (FarView != null)
            {
                FarView.InvalidateBlockColors();
            }

            // A repaint changes pixels in place and needs no re-mesh — unless a tile became animated, stopped being
            // animated or changed its frame count (#1957): the faces carry "frames, speed, strip start", so every
            // loaded chunk is rebuilt once. Rare: a world texture with frames arrives, or the player switches packs.
            if (Atlas != null && Atlas.AnimationVersion != _animationVersionSeen)
            {
                _animationVersionSeen = Atlas.AnimationVersion;
                foreach (var c in _chunkObjects.Keys)
                {
                    _dirty.Add(c);
                }
            }
        }

        /// <summary>Start's subscriptions for the chunk stream: chunk payloads and single block changes. Called once
        /// from <see cref="Start"/>, before the transport connects.</summary>
        private void WireChunkStreaming()
        {
            Network.ChunkReceived += OnChunk;

            Network.BlockChanged += OnBlockChanged;
        }

        /// <summary>Update's chunk work: drain finished mesh and collider builds, dispatch the nearest dirty
        /// chunks within the frame budget, keep chunks positioned around the player and update visibility.</summary>
        private void TickChunkStreaming()
        {
            // Upload + assign chunk geometry whose async build finished (A2), then assign collision meshes whose
            // async bake finished (both cheap on the main thread — the heavy work ran on worker threads).
            ReturnRetiredChunkArrays(); // #1555
            DrainBuiltChunks();
            DrainBakedColliders();
            DrainSyncCooks(); // #1819: browser collider cooks within their own frame budget
            SampleMotion();   // #1818: the build order looks ahead along the player's velocity

            // Kick off off-thread (re)builds for chunks that changed, but cap how many we DISPATCH per frame (P1)
            // so a burst of chunks arriving while moving fast spreads over several frames instead of stalling one.
            // Nearest chunks build first; chunks past the budget stay queued for the next frames.
            PromoteDeferredDirty(); // #1529
            PromoteWaterReachDirty(); // #1903
            if (_dirty.Count > 0)
            {
                _dirtyScratch.Clear();
                _dirtyScratch.AddRange(_dirty);
                var pp = PlayerPosition;

                // #1515: nearest-first WITHOUT sorting the whole set — the distances are computed once and the
                // `budget` nearest entries are picked by partial selection (O(n·k), k ≤ MeshChunksPerFrame),
                // instead of an O(n log n) sort with a closure and two seam-aware distances per comparison to
                // take four. Same order as the sort produced.
                _dirtyDistScratch.Clear();
                for (int i = 0; i < _dirtyScratch.Count; i++)
                {
                    _dirtyDistScratch.Add(BuildOrderKey(_dirtyScratch[i], pp)); // #1818: nearest first, then ahead
                }

                int budget = Mathf.Max(1, MeshChunksPerFrame);
                int built = 0;
                _frameWork.Restart(); // #1819: the browser builds inline — a time budget, not a count, decides
                while (BuildBudgetLeft(built, budget) && _dirtyScratch.Count > 0)
                {
                    int best = 0;
                    float bestDist = _dirtyDistScratch[0];
                    for (int i = 1; i < _dirtyScratch.Count; i++)
                    {
                        if (_dirtyDistScratch[i] < bestDist)
                        {
                            bestDist = _dirtyDistScratch[i];
                            best = i;
                        }
                    }

                    var coord = _dirtyScratch[best];
                    int last = _dirtyScratch.Count - 1;
                    _dirtyScratch[best] = _dirtyScratch[last];
                    _dirtyDistScratch[best] = _dirtyDistScratch[last];
                    _dirtyScratch.RemoveAt(last);
                    _dirtyDistScratch.RemoveAt(last);

                    // Neighbours not yet streamed are no-ops — flush them without spending budget; they
                    // re-mark themselves dirty once their own data arrives.
                    _dirty.Remove(coord);
                    if (DispatchChunkBuild(coord))
                    {
                        built++;
                    }
                }
            }

            // Round worlds: keep every loaded chunk drawn at the copy nearest the player as it laps the world
            // in EITHER direction. Near chunks resolve to their canonical position (a no-op write); only
            // chunks across a seam shift by ±Circumference (X) / ±LatitudePeriod (Z). Throttled to once per
            // block of movement on any axis. Y is included because the renderer-cull and near-only-collider
            // toggling in RepositionChunks are 3D distance-based: a straight-down jetpack descent must re-enable
            // the ground colliders below before the player reaches them, or they'd fall through.
            int chunkAnchorX = Mathf.FloorToInt(PlayerPosition.x);
            int chunkAnchorY = Mathf.FloorToInt(PlayerPosition.y);
            int chunkAnchorZ = Mathf.FloorToInt(PlayerPosition.z);
            // …plus a slow heartbeat, because this pass is also the ONLY thing that unloads far chunks
            // (#966): a player who stands still — parked at a station, stuck, AFK — never released a single
            // chunk, however much the world streamed to them while they stood there.
            _sinceReposTick += Time.deltaTime;
            bool moved = chunkAnchorX != _lastReposX || chunkAnchorY != _lastReposY || chunkAnchorZ != _lastReposZ;
            if (moved || _sinceReposTick >= ReposIdleInterval)
            {
                _lastReposX = chunkAnchorX;
                _lastReposY = chunkAnchorY;
                _lastReposZ = chunkAnchorZ;
                _sinceReposTick = 0f;
                RepositionChunks();
            }

            UpdateVisibility(); // #1823: re-walks when the camera changes chunk or chunk connectivity changed
        }
    }
}
