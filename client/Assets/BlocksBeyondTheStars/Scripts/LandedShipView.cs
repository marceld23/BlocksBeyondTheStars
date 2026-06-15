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

        /// <summary>Builds/rebuilds/destroys the ship objects to match the bootstrap's model registry.</summary>
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
                    Destroy(_roots[id]);
                }

                _roots.Remove(id);
            }

            // (Re)build the rest. Ships are small voxel grids — a full re-mesh per change is cheap.
            foreach (var m in Game.LandedShips.Values)
            {
                if (!_roots.TryGetValue(m.StructureId, out var root) || root == null)
                {
                    root = new GameObject($"LandedShip {m.OwnerId}");
                    root.transform.SetParent(transform, false);
                    _roots[m.StructureId] = root;
                }

                root.transform.position = new Vector3(Game.SceneX(m.Origin.X), m.Origin.Y, Game.SceneZ(m.Origin.Z));
                BuildShip(m, root);
            }
        }

        /// <summary>Meshes one parked ship under its root: the same ChunkMesher + block atlas the world and
        /// flight view use, with the owner's hull colour painted into the mesh (item 32) and a MeshCollider
        /// per voxel chunk so walking, standing inside and the settle-freeze ground probe all work.</summary>
        private void BuildShip(LandedShipModel m, GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(root.transform.GetChild(i).gameObject);
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

            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            for (int cx = FloorDiv(minX, cs); cx <= FloorDiv(maxX, cs); cx++)
            for (int cy = FloorDiv(minY, cs); cy <= FloorDiv(maxY, cs); cy++)
            for (int cz = FloorDiv(minZ, cs); cz <= FloorDiv(maxZ, cs); cz++)
            {
                var coord = new ChunkCoord(cx, cy, cz);
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

                var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas, paintTint: paint);
                if (mesh.vertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject($"ShipChunk {cx},{cy},{cz}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                go.AddComponent<MeshCollider>().sharedMesh = collider; // walk on the wings, stand in the cabin
            }
        }
    }
}
