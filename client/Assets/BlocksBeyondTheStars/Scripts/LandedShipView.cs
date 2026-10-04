// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Client model of one ship parked on the current world (ship-as-object): the structure-local sparse
    /// cell grid + its world anchor and owner hull colour. Kept on <see cref="GameBootstrap"/> so aiming,
    /// weather and the view all read the same data; <see cref="LandedShipView"/> renders it.
    /// </summary>
    public sealed class LandedShipModel
    {
        public string StructureId = string.Empty;
        public string OwnerId = string.Empty;
        public Vector3i Origin;       // world cell of the structure-local origin (0,0,0)
        public int Hull;              // owner hull paint (0xRRGGBB; 0 = default steel)
        public int Width, Height, Length;
        public readonly Dictionary<Vector3i, BlockId> Cells = new();

        /// <summary>Authored per-voxel dye/glow (0xRRGGBB each) + packed shape+orientation, parallel to
        /// <see cref="Cells"/> (empty for plain hulls). Lets a designed ship show its colour + form.</summary>
        public readonly Dictionary<Vector3i, (int Tint, int Glow)> Mods = new();
        public readonly Dictionary<Vector3i, int> Shapes = new();

        /// <summary>#2255: cells changed since the view last meshed this ship — only their chunks are re-meshed.</summary>
        public readonly HashSet<Vector3i> DirtyCells = new();

        public BlockId Get(Vector3i local) => Cells.TryGetValue(local, out var b) ? b : BlockId.Air;

        public void Set(Vector3i local, BlockId block)
        {
            if (block.IsAir)
            {
                Cells.Remove(local);
                Mods.Remove(local);
                Shapes.Remove(local);
            }
            else
            {
                Cells[local] = block;
            }
        }
    }

    /// <summary>
    /// Renders every ship parked on the current world as a real voxel OBJECT (ship-as-object): the same
    /// chunk-meshed look the flight view uses, painted in the owner's hull colour, with MeshColliders so
    /// the player walks on/in it like terrain. The hull is NOT part of the world block grid — placement,
    /// removal and cell edits arrive as LandedShipState / StructureBlockChanged messages and re-mesh the
    /// (small) object. Positions are seam-aware on the torus worlds like the world chunks.
    /// </summary>
    public sealed class LandedShipView : MonoBehaviour
    {
        public GameBootstrap Game;

        private readonly Dictionary<string, GameObject> _roots = new();
        private readonly Dictionary<string, LandedShipModel> _built = new();                         // the model each root was meshed from
        private readonly Dictionary<string, Dictionary<ChunkCoord, GameObject>> _chunks = new();     // #2255: each ship's chunk objects
        private bool _subscribed;
        private bool _dirty;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!_subscribed)
            {
                Game.LandedShipsChanged += () => _dirty = true;
                _subscribed = true;
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                Reconcile();
            }

            // Round worlds: keep each parked ship drawn at the copy nearest the player (same seam handling
            // as the world chunks; cheap — a handful of objects).
            foreach (var kv in _roots)
            {
                if (kv.Value != null && Game.LandedShips.TryGetValue(kv.Key, out var m))
                {
                    kv.Value.transform.position = new Vector3(Game.SceneX(m.Origin.X), m.Origin.Y, Game.SceneZ(m.Origin.Z));
                }
            }
        }

        /// <summary>Builds/rebuilds/destroys the ship objects to match the bootstrap's model registry. A ship that arrived
        /// as a whole (a new model) is meshed as a whole; a ship whose cells changed (#2255: a lamp the Crystal Net switched,
        /// a block built into the cabin) re-meshes only the chunks those cells reach — every other ship stays as it is.</summary>
        private void Reconcile()
        {
            // Drop ships that left (launch, owner logout, world switch).
            var stale = new List<string>();
            foreach (var kv in _roots)
            {
                if (!Game.LandedShips.ContainsKey(kv.Key))
                {
                    stale.Add(kv.Key);
                }
            }

            foreach (var id in stale)
            {
                if (_roots[id] != null)
                {
                    var root = _roots[id];
                    for (int i = root.transform.childCount - 1; i >= 0; i--)
                    {
                        DestroyShipChunk(root.transform.GetChild(i).gameObject); // free the ship's chunk meshes
                    }

                    Destroy(root);
                }

                _roots.Remove(id);
                _built.Remove(id);
                _chunks.Remove(id);
            }

            foreach (var m in Game.LandedShips.Values)
            {
                if (!_roots.TryGetValue(m.StructureId, out var root) || root == null)
                {
                    root = new GameObject($"LandedShip {m.OwnerId}");
                    root.transform.SetParent(transform, false);
                    _roots[m.StructureId] = root;
                }

                root.transform.position = new Vector3(Game.SceneX(m.Origin.X), m.Origin.Y, Game.SceneZ(m.Origin.Z));
                if (!_built.TryGetValue(m.StructureId, out var was) || !ReferenceEquals(was, m))
                {
                    _built[m.StructureId] = m;
                    m.DirtyCells.Clear();
                    BuildShip(m, root, null);
                }
                else if (m.DirtyCells.Count > 0)
                {
                    BuildShip(m, root, AffectedChunks(m.DirtyCells));
                    m.DirtyCells.Clear();
                }
            }
        }

        /// <summary>#2255: the chunks a set of changed cells reaches — its own and every chunk within a lamp's reach of it
        /// (a lamp switched on or off lights its neighbours, and a face at a chunk border changes the chunk next door).</summary>
        private static HashSet<ChunkCoord> AffectedChunks(HashSet<Vector3i> cells)
        {
            int cs = WorldConstants.ChunkSize, r = ChunkMesher.LightRadius;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            var result = new HashSet<ChunkCoord>();
            foreach (var c in cells)
            {
                for (int cx = FloorDiv(c.X - r, cs); cx <= FloorDiv(c.X + r, cs); cx++)
                for (int cy = FloorDiv(c.Y - r, cs); cy <= FloorDiv(c.Y + r, cs); cy++)
                for (int cz = FloorDiv(c.Z - r, cs); cz <= FloorDiv(c.Z + r, cs); cz++)
                {
                    result.Add(new ChunkCoord(cx, cy, cz));
                }
            }

            return result;
        }

        /// <summary>Destroys a ship-chunk GameObject AND the fresh render + collision meshes ChunkMesher.Build
        /// allocated for it — Unity does not free a MeshFilter/MeshCollider's sharedMesh with the GameObject, so
        /// each ship re-mesh or removal would otherwise leak two meshes per chunk.</summary>
        private void DestroyShipChunk(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Destroy(mf.sharedMesh);
            }

            var mc = go.GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != null)
            {
                Destroy(mc.sharedMesh);
            }

            Destroy(go);
        }

        /// <summary>Meshes one parked ship under its root: the same ChunkMesher + block atlas the world and
        /// flight view use, with the owner's hull colour painted into the mesh (item 32) and a MeshCollider
        /// per voxel chunk so walking, standing inside and the settle-freeze ground probe all work.
        /// <paramref name="only"/> limits the work to those chunks (#2255); null meshes the whole ship.</summary>
        private void BuildShip(LandedShipModel m, GameObject root, HashSet<ChunkCoord> only)
        {
            if (!_chunks.TryGetValue(m.StructureId, out var chunks))
            {
                chunks = new Dictionary<ChunkCoord, GameObject>();
                _chunks[m.StructureId] = chunks;
            }

            if (only == null)
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    DestroyShipChunk(root.transform.GetChild(i).gameObject); // free the child's fresh meshes too
                }

                chunks.Clear();
            }
            else
            {
                foreach (var coord in only)
                {
                    if (chunks.TryGetValue(coord, out var old))
                    {
                        DestroyShipChunk(old);
                        chunks.Remove(coord);
                    }
                }
            }

            if (m.Cells.Count == 0 || Game.ChunkMaterial == null || Game.Atlas == null || Game.Content == null)
            {
                return;
            }

            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };

            int hull = m.Hull != 0 ? m.Hull : 0xD1D6E0;
            var paint = ShipMeshBuilder.HullPaint(Game.Content,
                new Color(((hull >> 16) & 0xFF) / 255f, ((hull >> 8) & 0xFF) / 255f, (hull & 0xFF) / 255f));

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in m.Cells.Keys)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            BlockId CellAt(int x, int y, int z) => m.Get(new Vector3i(x, y, z));
            // Hand the mesher this ship's shapes so a hull cube next to a slab/ramp/sphere doesn't cull the face
            // the shaped cell leaves uncovered — that gap is a see-through hole into the parked ship (#420 M12).
            System.Func<int, int, int, int> WorldShape = m.Shapes == null ? null
                : (x, y, z) => m.Shapes.TryGetValue(new Vector3i(x, y, z), out var s) ? s : 0;

            // The whole hull's lamps, so interior lighting carries across this ship's chunk seams (#776).
            var lights = ShipMeshBuilder.LightSources(Game.Content, m.Cells, m.Mods);
            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            for (int cx = FloorDiv(minX, cs); cx <= FloorDiv(maxX, cs); cx++)
            for (int cy = FloorDiv(minY, cs); cy <= FloorDiv(maxY, cs); cy++)
            for (int cz = FloorDiv(minZ, cs); cz <= FloorDiv(maxZ, cs); cz++)
            {
                var coord = new ChunkCoord(cx, cy, cz);
                if (only != null && !only.Contains(coord))
                {
                    continue;
                }

                var origin = WorldConstants.ChunkOrigin(coord);
                var chunk = new ChunkData(coord);
                for (int lx = 0; lx < cs; lx++)
                for (int ly = 0; ly < cs; ly++)
                for (int lz = 0; lz < cs; lz++)
                {
                    var wc = new Vector3i(origin.X + lx, origin.Y + ly, origin.Z + lz);
                    var b = CellAt(wc.X, wc.Y, wc.Z);
                    if (!b.IsAir)
                    {
                        chunk.Set(lx, ly, lz, b);
                        ShipMeshBuilder.ApplyMods(chunk, lx, ly, lz, wc, m.Mods, m.Shapes);
                    }
                }

                var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas, paintTint: paint, lights: lights, worldShape: WorldShape);
                if (mesh.vertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject($"ShipChunk {cx},{cy},{cz}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z);
                // The collider FIRST: a MeshCollider added after the MeshFilter adopts the render mesh and tries to
                // cook it — since #1528 that mesh is uploaded non-readable, so the cook logs an error per chunk
                // (playtest 2026-09-05) before the real collider mesh replaces it.
                go.AddComponent<MeshCollider>().sharedMesh = collider; // walk on the wings, stand in the cabin
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                chunks[coord] = go;
            }
        }
    }
}
