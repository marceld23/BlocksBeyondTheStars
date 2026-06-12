using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a player ship's 1:1 voxel mesh from the server's <c>SpaceShipDesign</c> — using the same block
    /// atlas + <see cref="ChunkMesher"/> the flight view uses — so the in-menu ship preview shows the REAL ship
    /// the player flies (and other players see) instead of a placeholder silhouette. Mirrors SpaceView's voxel
    /// build, minus the flight-only FX + collision bookkeeping (a preview needs neither). Static + self-contained.
    /// </summary>
    public static class ShipMeshBuilder
    {
        /// <summary>True if a design carries any blocks to mesh.</summary>
        public static bool HasDesign(BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d)
            => d != null && d.Block != null && d.Block.Length > 0;

        /// <summary>Per-block paint resolver for ship meshes (item 32): the ship's hull colour for the
        /// paintable hull block (<c>iron_wall</c>), black (= unpainted) for every other block. The colour is
        /// converted at this boundary (<see cref="ShaderColor.Srgb"/>) because the mesher writes it raw into
        /// the mesh's tint stream.</summary>
        public static System.Func<BlockId, Color> HullPaint(GameContent content, Color hull)
        {
            var tint = ShaderColor.Srgb(hull);
            return id => content.BlockById(id)?.Key == "iron_wall" ? tint : Color.black;
        }

        /// <summary>Builds the centred voxel ship under <paramref name="parent"/> and reports its largest
        /// dimension via <paramref name="extent"/> (so a preview can frame its camera to any ship size). When
        /// <paramref name="hull"/> is an opaque colour the hull blocks are painted with it (item 32). Returns
        /// null when the design or the block atlas/material isn't ready (caller falls back to a placeholder).</summary>
        public static GameObject BuildVoxelShip(GameBootstrap game, Transform parent,
            BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d, out float extent, Color hull = default)
        {
            extent = 2f;
            if (game == null || game.ChunkMaterial == null || game.Atlas == null || game.Content == null || !HasDesign(d))
            {
                return null;
            }

            var cells = new Dictionary<Vector3i, BlockId>(d.Block.Length);
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < d.Block.Length; i++)
            {
                int bx = d.X[i], by = d.Y[i], bz = d.Z[i];
                cells[new Vector3i(bx, by, bz)] = new BlockId(d.Block[i]);
                if (bx < minX) minX = bx; if (by < minY) minY = by; if (bz < minZ) minZ = bz;
                if (bx > maxX) maxX = bx; if (by > maxY) maxY = by; if (bz > maxZ) maxZ = bz;
            }

            var centre = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            extent = Mathf.Max(maxX - minX + 1, Mathf.Max(maxY - minY + 1, maxZ - minZ + 1));

            var ship = new GameObject("VoxelShip");
            ship.transform.SetParent(parent, false);
            BuildVoxChunks(game, ship.transform, cells, centre, hull.a > 0f ? HullPaint(game.Content, hull) : null);
            return ship;
        }

        /// <summary>Meshes a sparse block grid into chunk meshes under <paramref name="parent"/>, centred on
        /// <paramref name="centre"/> (mirrors SpaceView.BuildVoxChunks; no colliders — display only).</summary>
        private static void BuildVoxChunks(GameBootstrap game, Transform parent, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            System.Func<BlockId, Color> paint)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in cells.Keys)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            BlockId WorldBlock(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;

            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            var mats = game.ChunkMaterialTransparent != null
                ? new[] { game.ChunkMaterial, game.ChunkMaterialTransparent }
                : new[] { game.ChunkMaterial };

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
                    var b = WorldBlock(origin.X + lx, origin.Y + ly, origin.Z + lz);
                    if (!b.IsAir)
                    {
                        chunk.Set(lx, ly, lz, b);
                    }
                }

                var (mesh, _) = ChunkMesher.Build(chunk, game.Content, WorldBlock, game.Atlas, paintTint: paint);
                if (mesh.vertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject($"VoxChunk {cx},{cy},{cz}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - centre;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
        }
    }
}
