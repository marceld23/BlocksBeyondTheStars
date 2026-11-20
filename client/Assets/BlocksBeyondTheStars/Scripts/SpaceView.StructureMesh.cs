// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3615: how the flight view meshes its voxel structures — chunk by chunk. A giant wreck is up to 256 blocks a side, so
    /// <list type="bullet">
    /// <item>a structure is a set of 16³ chunk meshes (with their colliders), and a mined or placed block re-meshes only the
    /// chunks its change can reach (<see cref="StructureChunkMath"/>): its own and the 26 around it (faces across a border,
    /// corner shading), the reach of the lamps near it (and its own, when it is a lamp), and — when it turned between
    /// light-stopping and open — the chunks whose skylight reads its column;</item>
    /// <item>the meshing runs in a queue under a small time budget per frame (<see cref="StructMeshBudgetMs"/>), nearest
    /// chunks first, so a big hull arriving never freezes a frame — edits jump the queue;</item>
    /// <item>a BIG station hull (a giant wreck, or any station <see cref="LodMinExtent"/> blocks across) is drawn from afar as
    /// a coarse skin (<see cref="HullSilhouette"/> — on a worker thread when the design arrives; in the browser, which has no
    /// threads, inline the first time the hull is seen from afar) instead of its chunks. Up close the chunks build; the skin
    /// covers the hull only while it is built from nothing, never over chunks already on screen.</item>
    /// </list>
    /// The doors and a derelict's broken-look extras live under the structure's <c>Extras</c> child, built once per design.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Milliseconds of chunk meshing per frame (at least one chunk is always meshed).</summary>
        private const float StructMeshBudgetMs = 3f;

        /// <summary>A station hull this many blocks across (or a giant wreck of any size) gets the far skin.</summary>
        private const int LodMinExtent = 96;

        /// <summary>Distance to the hull's box under which the chunks take over from the skin…</summary>
        private const float LodNearEnter = 160f;

        /// <summary>…and beyond which the skin replaces them again (the gap keeps them from flapping at the edge).</summary>
        private const float LodFarLeave = 200f;

        /// <summary>Closer than this the skin never covers chunks that are still building (it would wall in the camera).</summary>
        private const float LodHideNear = 32f;

        private static readonly System.Diagnostics.Stopwatch StructMeshClock = new System.Diagnostics.Stopwatch();
        private readonly HashSet<ChunkCoord> _chunkScratch = new HashSet<ChunkCoord>();
        private readonly List<ChunkCoord> _chunkGone = new List<ChunkCoord>();
        private Material[] _structChunkMats;
        private Material _hullLodMat;

        /// <summary>One meshed chunk of a structure: its object and the two meshes ChunkMesher made for it (Unity does not free
        /// a mesh with its GameObject, so they are kept to be destroyed with it).</summary>
        private sealed class StructChunk
        {
            public GameObject Go;
            public Mesh Render;
            public Mesh Collider;
        }

        // item 20 S3: voxel structures — asteroids, stations, wrecks, debris — static bodies at their flight positions.
        private sealed class VoxStruct
        {
            public Dictionary<Vector3i, BlockId> Cells;
            public Vector3 Centre;
            public Vector3 Pos;     // root-local position of the structure
            public GameObject Root; // null until (re)built
            public bool MeshDirty;  // every chunk must be (re)built — on a new design, a new root, a return from afar

            public string Kind;     // "asteroid" | "station" | "wreck" | "debris"
            public Dictionary<Vector3i, (int Tint, int Glow)> Mods; // authored dye/glow (a station's blue solar cells)
            public Dictionary<Vector3i, int> Shapes;                // authored shapes (slabs, furniture seen through a window)
            public Vector3 Half;    // half the cells' box, centred on Pos

            // #1917: a generated station's hangar mouth — root-local centre + the direction pointing out of it.
            public bool HasDock;
            public Vector3 Dock;
            public Vector3 DockOut;

            // #2607: a player station's doors (drawn as doors, aimed at on a spacewalk) and its owner (the door preview
            // shows only where the suit may build: the owner and their allies).
            public List<SpaceDoors.Door> Doors = new();
            public HashSet<Vector3i> DoorCells = new();
            public string OwnerId = string.Empty;
            public string Purpose = string.Empty; // the lively places: a station's purpose ("" = none)

            // #2676: a derelict station's hull gets the broken-look extras (sparks, tumbling pieces, a flickering beacon).
            public string Id = string.Empty;
            public bool Derelict;

            // #3409: the flight instance this design arrived for (the server sends the SpaceState before the designs), so
            // the in-flight rebuild of a jump (#1677) keeps the new system's designs instead of wiping them with the old.
            public string Instance = string.Empty;

            // #3615: the chunk meshes, the queue of chunks waiting for a (re)mesh and the hull's lamps (null = gather anew).
            public Transform Chunks;
            public readonly Dictionary<ChunkCoord, StructChunk> ChunkObjects = new();
            public readonly HashSet<ChunkCoord> Dirty = new();
            public readonly List<ChunkCoord> Queue = new(); // popped from the END: the nearest chunk, or an edit, comes last
            public HashSet<ChunkCoord> Occupied = new();    // the chunks that hold (or held) a cell — what an edit may re-mesh
            public bool Building;                           // the chunks are built from nothing (none on screen yet)
            public List<(Vector3i Pos, int Rgb)> Lights;
            public System.Func<BlockId, Color> Paint;      // a derelict's grime (null = plain)

            // The doors and a derelict's decor (rebuilt once per design, never per mined block).
            public Transform Extras;
            public readonly List<Mesh> ExtraMeshes = new();
            public bool ExtrasDirty = true;

            // #3615: the far skin of a big station hull.
            public bool BigHull;
            public bool Far;
            public System.Threading.Tasks.Task<HullSilhouette> LodTask;
            public BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign LodSource; // the browser builds the skin from it lazily
            public HullSilhouette LodData;
            public GameObject Lod;
            public Mesh LodMesh;

            public bool IsStation => Kind == "station";
        }

        /// <summary>A root for a structure that came into range (its meshes are built from the queue).</summary>
        private void EnsureStructRoot(VoxStruct vs)
        {
            if (vs.Root != null)
            {
                return;
            }

            vs.Root = new GameObject(vs.IsStation ? "StationHull" : "Asteroid");
            vs.Root.transform.SetParent(_root.transform, false);
            vs.Root.transform.localPosition = vs.Pos;
            vs.Chunks = new GameObject("Chunks").transform;
            vs.Chunks.SetParent(vs.Root.transform, false);
            vs.MeshDirty = true;
            vs.ExtrasDirty = true;
        }

        /// <summary>Frees every mesh of a structure and its root; the cells stay (it is rebuilt when it comes back near).</summary>
        private void DropStructMeshes(VoxStruct vs)
        {
            DropStructChunks(vs);
            DropStructExtras(vs);
            DropHullLod(vs);
            if (vs.Root != null)
            {
                Destroy(vs.Root);
            }

            vs.Root = null;
            vs.Chunks = null;
            vs.MeshDirty = true;
            vs.ExtrasDirty = true;
        }

        /// <summary>Frees the chunk meshes and forgets the queue (the far skin stands in, or the structure goes).</summary>
        private void DropStructChunks(VoxStruct vs)
        {
            foreach (var chunk in vs.ChunkObjects.Values)
            {
                FreeStructChunk(chunk);
            }

            vs.ChunkObjects.Clear();
            vs.Dirty.Clear();
            vs.Queue.Clear();
            vs.Building = false;
            vs.MeshDirty = true;
        }

        private void FreeStructChunk(StructChunk chunk)
        {
            if (chunk.Render != null) Destroy(chunk.Render);
            if (chunk.Collider != null) Destroy(chunk.Collider);
            if (chunk.Go != null) Destroy(chunk.Go);
        }

        /// <summary>Queues every chunk that holds a cell, nearest to the viewer first; a chunk that holds none any more goes at
        /// once. Chunks already built stay on screen until their new mesh replaces them.</summary>
        private void QueueAllChunks(VoxStruct vs, Vector3 viewer)
        {
            vs.MeshDirty = false;
            vs.Lights = null;
            var occupied = StructureChunkMath.Occupied(vs.Cells.Keys);
            vs.Occupied = occupied;
            _chunkGone.Clear();
            foreach (var kv in vs.ChunkObjects)
            {
                if (!occupied.Contains(kv.Key))
                {
                    _chunkGone.Add(kv.Key);
                }
            }

            foreach (var coord in _chunkGone)
            {
                FreeStructChunk(vs.ChunkObjects[coord]);
                vs.ChunkObjects.Remove(coord);
            }

            // From nothing — or a build from nothing that a re-sent design interrupted — the skin covers it; an adopted rebuild of
            // a finished hull keeps its old chunks on screen, so no skin goes over them.
            vs.Building |= vs.ChunkObjects.Count == 0;
            var eye = vs.Root != null ? ToDesign(vs.Root.transform, vs.Centre, viewer) : viewer - vs.Pos + vs.Centre;
            var order = StructureChunkMath.NearestFirst(occupied, eye.x, eye.y, eye.z);
            vs.Dirty.Clear();
            vs.Queue.Clear();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                vs.Dirty.Add(order[i]);
                vs.Queue.Add(order[i]); // farthest first in the list: the nearest is popped first
            }
        }

        /// <summary>Puts one chunk at the end of the queue (built next) unless it already waits.</summary>
        private static void QueueChunk(VoxStruct vs, ChunkCoord coord)
        {
            if (vs.Dirty.Add(coord))
            {
                vs.Queue.Add(coord);
            }
        }

        /// <summary>A cell of a built structure changed (<see cref="StructureChunkMath"/>): its own chunk and the 26 around it,
        /// the reach of the lamps near it (and its own reach when the cell gained, lost or changed a light —
        /// <paramref name="lampChanged"/>), and, when it turned between light-stopping and open (<paramref name="skyChanged"/>),
        /// the chunks whose skylight reads its column. Only chunks that hold cells are re-meshed. Nothing happens while the
        /// structure is far, unbuilt or waiting for a full rebuild anyway.</summary>
        private void MarkStructCellChanged(VoxStruct vs, Vector3i cell, bool lampChanged, bool skyChanged)
        {
            if (vs.Root == null || vs.Far || vs.MeshDirty)
            {
                return;
            }

            var own = StructureChunkMath.ChunkOf(cell);
            if (vs.Cells.ContainsKey(cell))
            {
                vs.Occupied.Add(own); // a block placed into a chunk that held none
            }

            if (vs.Lights == null && Game.Content != null)
            {
                vs.Lights = ShipMeshBuilder.LightSources(Game.Content, vs.Cells, vs.Mods); // the lamps after the change
            }

            _chunkScratch.Clear();
            StructureChunkMath.NeighbourChunks(cell, _chunkScratch);
            StructureChunkMath.LightChunks(cell, lampChanged, vs.Lights, _chunkScratch);
            if (skyChanged)
            {
                StructureChunkMath.SkyChunks(cell, _chunkScratch);
            }

            foreach (var coord in _chunkScratch)
            {
                if (!coord.Equals(own) && (vs.Occupied.Contains(coord) || vs.ChunkObjects.ContainsKey(coord)))
                {
                    QueueChunk(vs, coord);
                }
            }

            // The changed cell's own chunk last, so it is popped (and drawn) first.
            vs.Dirty.Remove(own);
            vs.Queue.Remove(own);
            QueueChunk(vs, own);
        }

        /// <summary>Meshes queued chunks until this frame's budget is spent (always at least one chunk). A big hull's far skin
        /// goes once its queue is empty (<see cref="UpdateHullLod"/>).</summary>
        private void MeshQueuedChunks()
        {
            StructMeshClock.Restart();
            bool meshed = false;
            foreach (var vs in _structs.Values)
            {
                while (vs.Queue.Count > 0 && vs.Root != null && !vs.Far)
                {
                    if (meshed && StructMeshClock.Elapsed.TotalMilliseconds >= StructMeshBudgetMs)
                    {
                        return;
                    }

                    int last = vs.Queue.Count - 1;
                    var coord = vs.Queue[last];
                    vs.Queue.RemoveAt(last);
                    if (vs.Dirty.Remove(coord))
                    {
                        BuildStructChunk(vs, coord);
                        meshed = true;
                    }
                }

                if (vs.Queue.Count == 0)
                {
                    vs.Building = false; // every chunk is on screen: the far skin may go
                }
            }
        }

        /// <summary>(Re)builds one chunk of a structure: its render mesh and its collider, with the hull's lamps, dyes and
        /// shapes; an empty chunk just loses its old mesh.</summary>
        private void BuildStructChunk(VoxStruct vs, ChunkCoord coord)
        {
            if (vs.ChunkObjects.TryGetValue(coord, out var old))
            {
                FreeStructChunk(old);
                vs.ChunkObjects.Remove(coord);
            }

            var content = Game.Content;
            if (content == null || Game.ChunkMaterial == null || vs.Chunks == null)
            {
                return;
            }

            var chunk = FillStructChunk(vs, coord);
            if (chunk == null)
            {
                return; // no cell left in it
            }

            var cells = vs.Cells;
            var shapes = vs.Shapes;
            BlockId WorldBlock(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;
            // The hull's shapes: a full cube next to a slab/ramp keeps the face the shaped cell leaves open (#420 M12).
            System.Func<int, int, int, int> worldShape = shapes == null ? null
                : (x, y, z) => shapes.TryGetValue(new Vector3i(x, y, z), out var s) ? s : 0;
            vs.Lights ??= ShipMeshBuilder.LightSources(content, vs.Cells, vs.Mods); // the whole hull's lamps (#776)
            if (vs.Derelict && vs.Paint == null)
            {
                vs.Paint = DerelictGrime();
            }

            var (mesh, collider) = ChunkMesher.Build(chunk, content, WorldBlock, Game.Atlas, paintTint: vs.Paint, lights: vs.Lights, worldShape: worldShape);
            if (mesh.vertexCount == 0)
            {
                Destroy(mesh);
                Destroy(collider);
                return;
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            var go = new GameObject($"VoxChunk {coord.X},{coord.Y},{coord.Z}");
            go.transform.SetParent(vs.Chunks, false);
            go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - vs.Centre;
            go.AddComponent<MeshCollider>().sharedMesh = collider; // S2/S3: hull/ore collision — added FIRST, see LandedShipView
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = StructChunkMaterials();
            vs.ChunkObjects[coord] = new StructChunk { Go = go, Render = mesh, Collider = collider };
        }

        /// <summary>The chunk's cells as chunk data (dye, glow and shape applied), or null when it holds none.</summary>
        private static ChunkData FillStructChunk(VoxStruct vs, ChunkCoord coord)
        {
            int cs = WorldConstants.ChunkSize;
            var origin = WorldConstants.ChunkOrigin(coord);
            ChunkData chunk = null;
            for (int lx = 0; lx < cs; lx++)
            {
                for (int ly = 0; ly < cs; ly++)
                {
                    for (int lz = 0; lz < cs; lz++)
                    {
                        var wc = new Vector3i(origin.X + lx, origin.Y + ly, origin.Z + lz);
                        if (!vs.Cells.TryGetValue(wc, out var b) || b.IsAir)
                        {
                            continue;
                        }

                        chunk ??= new ChunkData(coord);
                        chunk.Set(lx, ly, lz, b);
                        ShipMeshBuilder.ApplyMods(chunk, lx, ly, lz, wc, vs.Mods, vs.Shapes);
                    }
                }
            }

            return chunk;
        }

        /// <summary>The chunk materials (opaque, then see-through) — one array shared by every structure chunk, made anew only
        /// when the game swapped a material.</summary>
        private Material[] StructChunkMaterials()
        {
            var transparent = Game.ChunkMaterialTransparent;
            bool stale = _structChunkMats == null || _structChunkMats[0] != Game.ChunkMaterial
                || (transparent != null ? _structChunkMats.Length != 2 || _structChunkMats[1] != transparent : _structChunkMats.Length != 1);
            if (stale)
            {
                _structChunkMats = transparent != null ? new[] { Game.ChunkMaterial, transparent } : new[] { Game.ChunkMaterial };
            }

            return _structChunkMats;
        }

        // ---------------------------------------------------------------- doors and decor

        /// <summary>The doors (#2607) and a derelict's broken-look extras (#2676) under the structure's Extras child — once per
        /// design, not per mined block.</summary>
        private void BuildStructExtras(VoxStruct vs)
        {
            vs.ExtrasDirty = false;
            DropStructExtras(vs);
            if (vs.Root == null)
            {
                return;
            }

            vs.Extras = new GameObject("Extras").transform;
            vs.Extras.SetParent(vs.Root.transform, false);
            AddPlacedDoors(vs.Extras, vs.Doors, vs.Centre, vs.ExtraMeshes);
            if (vs.Derelict)
            {
                AddDerelictDecor(vs);
            }
            else if (vs.IsStation && !vs.Id.StartsWith("pstation:", System.StringComparison.Ordinal)) // a player's own build stays theirs
            {
                AddStationLife(vs); // the lively places: position lights and a turning dish (SpaceView.StationLife.cs)
            }
        }

        private void DropStructExtras(VoxStruct vs)
        {
            foreach (var mesh in vs.ExtraMeshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }

            vs.ExtraMeshes.Clear();
            if (vs.Extras != null)
            {
                Destroy(vs.Extras.gameObject);
            }

            vs.Extras = null;
        }

        /// <summary>A fresh design of a structure already drawn keeps the old meshes on screen until the new ones replace
        /// them chunk by chunk (a player station's grown box, a giant's full resend) — no hole, no flash.</summary>
        private void AdoptStructMeshes(VoxStruct vs, VoxStruct old)
        {
            if (old == null || old.Root == null)
            {
                if (old != null)
                {
                    DropStructMeshes(old);
                }

                return;
            }

            vs.Root = old.Root;
            vs.Chunks = old.Chunks;
            vs.Root.transform.localPosition = vs.Pos;
            foreach (var kv in old.ChunkObjects)
            {
                vs.ChunkObjects[kv.Key] = kv.Value;
                var origin = WorldConstants.ChunkOrigin(kv.Key);
                if (kv.Value.Go != null)
                {
                    kv.Value.Go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - vs.Centre; // the box may have moved
                }
            }

            vs.Extras = old.Extras;
            vs.ExtraMeshes.AddRange(old.ExtraMeshes); // freed by the extras rebuild
            vs.Building = old.Building; // a hull still built from nothing stays covered by its skin
            if (vs.BigHull)
            {
                vs.Lod = old.Lod;
                vs.LodMesh = old.LodMesh;
                vs.LodData = old.LodData; // shown until the new design's skin is ready
                vs.Far = old.Far;
                if (vs.Lod != null && vs.LodData != null)
                {
                    vs.Lod.transform.localPosition = new Vector3(vs.LodData.MinX, vs.LodData.MinY, vs.LodData.MinZ) - vs.Centre;
                }
            }
            else
            {
                DropHullLod(old); // it shrank below the far skin's size
                vs.Far = false;
            }

            vs.MeshDirty = true;
            vs.ExtrasDirty = true;
        }
    }
}
