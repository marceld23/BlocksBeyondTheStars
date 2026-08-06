// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
            => BuildVoxelShip(game?.Content, game?.Atlas, game?.ChunkMaterial, game?.ChunkMaterialTransparent,
                parent, d, out extent, hull);

        /// <summary>Game-less overload: builds the voxel ship straight from the block atlas + chunk materials, so the
        /// menu attract scene (which has no <see cref="GameBootstrap"/>) can render the SAME real voxel ship the
        /// flight view does. The in-game overload forwards here. Returns null when any piece is missing.</summary>
        public static GameObject BuildVoxelShip(GameContent content, BlockTextureAtlas atlas,
            Material chunkMat, Material chunkMatTransparent, Transform parent,
            BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d, out float extent, Color hull = default)
        {
            extent = 2f;
            if (content == null || atlas == null || chunkMat == null || !HasDesign(d))
            {
                return null;
            }

            var cells = new Dictionary<Vector3i, BlockId>(d.Block.Length);
            // Authored per-voxel dye/glow + shape ride parallel arrays (empty for plain hulls).
            var mods = new Dictionary<Vector3i, (int Tint, int Glow)>();
            var shapes = new Dictionary<Vector3i, int>();
            bool hasTint = d.Tint != null && d.Tint.Length == d.Block.Length;
            bool hasGlow = d.Glow != null && d.Glow.Length == d.Block.Length;
            bool hasShape = d.Shape != null && d.Shape.Length == d.Block.Length;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < d.Block.Length; i++)
            {
                int bx = d.X[i], by = d.Y[i], bz = d.Z[i];
                var key = new Vector3i(bx, by, bz);
                cells[key] = new BlockId(d.Block[i]);
                int tint = hasTint ? d.Tint[i] : 0, glow = hasGlow ? d.Glow[i] : 0;
                if (tint != 0 || glow != 0) mods[key] = (tint, glow);
                if (hasShape && d.Shape[i] != 0) shapes[key] = d.Shape[i];
                if (bx < minX) minX = bx; if (by < minY) minY = by; if (bz < minZ) minZ = bz;
                if (bx > maxX) maxX = bx; if (by > maxY) maxY = by; if (bz > maxZ) maxZ = bz;
            }

            var centre = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            extent = Mathf.Max(maxX - minX + 1, Mathf.Max(maxY - minY + 1, maxZ - minZ + 1));

            var ship = new GameObject("VoxelShip");
            ship.transform.SetParent(parent, false);
            BuildVoxChunks(content, atlas, chunkMat, chunkMatTransparent, ship.transform, cells, centre,
                hull.a > 0f ? HullPaint(content, hull) : null, mods, shapes);
            return ship;
        }

        /// <summary>Every light-emitting cell of a sparse ship/structure grid, as the mesher's light-source
        /// list. Ship hulls are meshed one 16³ chunk at a time, and without this list <see cref="ChunkMesher"/>
        /// falls back to scanning only the chunk it is currently meshing — so a lamp stopped dead at whatever
        /// chunk seam it happened to sit next to, cutting a big hull's lighting along arbitrary planes (#776).
        /// The planet path solves the same problem with ClientWorld.LightSourcesNear. Shared by every ship
        /// voxel-build path. Range culling happens inside the mesher, so the whole hull's list is fine.</summary>
        public static List<(Vector3i Pos, int Rgb)> LightSources(GameContent content,
            IEnumerable<KeyValuePair<Vector3i, BlockId>> cells,
            Dictionary<Vector3i, (int Tint, int Glow)> mods = null)
        {
            var lights = new List<(Vector3i Pos, int Rgb)>();
            if (content == null || cells == null)
            {
                return lights;
            }

            foreach (var kv in cells)
            {
                int glow = mods != null && mods.TryGetValue(kv.Key, out var m) ? m.Glow : 0;
                int rgb = ChunkMesher.BlockLightColor(content, kv.Value, glow);
                if (rgb != 0)
                {
                    lights.Add((kv.Key, rgb));
                }
            }

            return lights;
        }

        /// <summary>Writes a cell's authored dye/glow + shape into a chunk so the shared mesher renders them
        /// (no-op for cells without modifiers). Shared by every ship/structure voxel-build path.</summary>
        public static void ApplyMods(ChunkData chunk, int lx, int ly, int lz, Vector3i worldCell,
            Dictionary<Vector3i, (int Tint, int Glow)> mods, Dictionary<Vector3i, int> shapes)
        {
            if (mods != null && mods.TryGetValue(worldCell, out var m))
            {
                chunk.SetModifier(lx, ly, lz, m.Tint, m.Glow);
            }

            if (shapes != null && shapes.TryGetValue(worldCell, out var sh) && sh != 0)
            {
                chunk.SetShape(lx, ly, lz, sh);
            }
        }

        /// <summary>Meshes a sparse block grid into chunk meshes under <paramref name="parent"/>, centred on
        /// <paramref name="centre"/> (mirrors SpaceView.BuildVoxChunks; no colliders — display only).</summary>
        private static void BuildVoxChunks(GameContent content, BlockTextureAtlas atlas, Material chunkMat, Material chunkMatTransparent,
            Transform parent, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            System.Func<BlockId, Color> paint,
            Dictionary<Vector3i, (int Tint, int Glow)> mods = null, Dictionary<Vector3i, int> shapes = null)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in cells.Keys)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            BlockId WorldBlock(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;
            // A cube face bordering a shaped (slab/ramp/…) neighbour must not be culled — the shaped cell doesn't
            // fill its volume, so culling leaves a see-through hole into the hull (#420 M12). The mesher rescues
            // that face only when handed a worldShape delegate reading the same shapes dict the planet uses.
            System.Func<int, int, int, int> WorldShape = shapes == null ? null
                : (x, y, z) => shapes.TryGetValue(new Vector3i(x, y, z), out var s) ? s : 0;

            var lights = LightSources(content, cells, mods);
            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            var mats = chunkMatTransparent != null
                ? new[] { chunkMat, chunkMatTransparent }
                : new[] { chunkMat };

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
                    var b = WorldBlock(wc.X, wc.Y, wc.Z);
                    if (!b.IsAir)
                    {
                        chunk.Set(lx, ly, lz, b);
                        ApplyMods(chunk, lx, ly, lz, wc, mods, shapes);
                    }
                }

                var (mesh, collider) = ChunkMesher.Build(chunk, content, WorldBlock, atlas, paintTint: paint, lights: lights, worldShape: WorldShape);
                if (collider != null)
                {
                    Object.Destroy(collider); // display-only build — the cooked collider mesh is never used (#423)
                }

                if (mesh.vertexCount == 0)
                {
                    Object.Destroy(mesh); // all-air chunk cell — free the empty mesh instead of orphaning it
                    continue;
                }

                var go = new GameObject($"VoxChunk {cx},{cy},{cz}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - centre;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                go.AddComponent<OwnedProceduralMesh>(); // the mesh dies with the chunk object (paint re-mesh, transit end, …)
            }
        }
    }

    /// <summary>Destroys the <see cref="MeshFilter"/>'s procedural mesh together with its GameObject. Unity
    /// never garbage-collects Mesh assets, so display meshers (ship previews, transits, speeder hulls) attach
    /// this to every object whose mesh they own — teardown paths only destroy GameObjects, which would leak
    /// one mesh per rebuild otherwise (#423). Attach ONLY where the mesh is exclusive to this object — never
    /// on primitives, whose sharedMesh is the shared built-in asset.</summary>
    public sealed class OwnedProceduralMesh : MonoBehaviour
    {
        private void OnDestroy()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Destroy(filter.sharedMesh);
            }
        }
    }
}
