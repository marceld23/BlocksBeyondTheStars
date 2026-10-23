// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The voxel structures of the flight scene (item 20 S3): asteroids, stations, the wreck and debris — their
    /// designs, block edits, mining progress and chunk meshes, the doors built into them, and a derelict's broken
    /// look (#2676).
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>#2119: the doors built into a ship (#2607: or a player station), drawn closed in their doorways (the
        /// cells are air). The flight view never opens them — you board through the hatch or dock. #2607: a closed ENERGY
        /// door shows its blue field in a glowing frame out here (decision 5: it read as a grey slab before); a sliding, a
        /// hinge or a wooden door keeps its closed panels. The meshes are children of <paramref name="parent"/>; each is
        /// listed in <paramref name="meshes"/> when given, so the caller can free them with the structure.</summary>
        private void AddPlacedDoors(Transform parent, IReadOnlyList<SpaceDoors.Door> doors, Vector3 centre, List<Mesh> meshes = null)
        {
            if (doors == null || doors.Count == 0)
            {
                return;
            }

            _doorPanelMat ??= Lit(new Color(0.62f, 0.68f, 0.76f));
            _doorRimMat ??= Unlit(new Color(0.2f, 0.85f, 1f));
            _doorFieldMat ??= EnergyField(new Color(0.35f, 0.80f, 1f, 0.6f));
            var frame = new List<DoorGeometry.Box>();
            var field = new List<DoorGeometry.Box>();
            foreach (var door in doors)
            {
                var at = new Vector3(door.Cell.X + 0.5f, door.Cell.Y, door.Cell.Z + 0.5f) - centre;
                var boxes = DoorGeometry.Closed(door.Kind, 1f);
                if (!SpaceDoors.ShowsFieldInSpace(door.Kind))
                {
                    AddDoorPart(parent, "PlacedDoor", boxes, door.AxisX, at, _doorPanelMat, withField: false, meshes);
                    continue;
                }

                frame.Clear();
                field.Clear();
                foreach (var box in boxes)
                {
                    if (box.Part == DoorGeometry.Part.Field)
                    {
                        field.Add(box);
                    }
                    else if (box.Part == DoorGeometry.Part.PostMinus || box.Part == DoorGeometry.Part.PostPlus)
                    {
                        frame.Add(box);
                    }
                }

                AddDoorPart(parent, "PlacedDoorFrame", frame, door.AxisX, at, _doorRimMat, withField: false, meshes);
                AddDoorPart(parent, "PlacedDoorField", field, door.AxisX, at, _doorFieldMat, withField: true, meshes);
            }
        }

        /// <summary>One mesh of a door drawn in space: the given boxes, turned for the wall axis, at a parent-local spot.</summary>
        private static void AddDoorPart(Transform parent, string name, IReadOnlyList<DoorGeometry.Box> boxes, bool axisX, Vector3 at,
            Material mat, bool withField, List<Mesh> meshes)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            DoorMesh.Append(verts, cols, tris, boxes, axisX, Vector3.zero, _ => Color.white, withField);
            if (verts.Count == 0)
            {
                return;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes?.Add(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>#685: remembers the server's mining progress on a structure cell (EVA asteroid mining), so
        /// the aim marker can show how close the aimed ore block is to breaking.</summary>
        private void OnStructureMiningProgress(BlocksBeyondTheStars.Networking.Messages.StructureMiningProgress m)
        {
            _evaMineProgressStructId = m.StructureId;
            _evaMineProgressCell = new Vector3Int(m.X, m.Y, m.Z);
            _evaMineProgressFraction = Mathf.Clamp01(m.Fraction);
            _evaMineProgressAt = Time.time;

            // #2155: chips fly off the struck block on an EVA too (planet mining had them, space had nothing).
            if (_active && StructCellWorld(m.StructureId, new Vector3i(m.X, m.Y, m.Z), out var at, out var block))
            {
                var colors = Game?.Views.Mining != null ? Game.Views.Mining.BlockColors(block) : new[] { new Color(0.5f, 0.45f, 0.4f) };
                var toEye = _root != null ? (_root.transform.TransformPoint(_evaPos) - at).normalized : Vector3.up;
                FxKit.Burst(FxKit.Kind.DebrisFloat, at + toEye * 0.5f, 3, toEye, 70f, 1f, 3f, 0.05f, 0.1f, 0.8f, 1.3f, colors[0], colors[colors.Length - 1]);
                FxKit.Burst(FxKit.Kind.Motes, at + toEye * 0.5f, 3, toEye, 60f, 1.5f, 4f, 0.04f, 0.07f, 0.2f, 0.4f, new Color(1f, 0.8f, 0.5f));
            }
        }

        /// <summary>World position + current block of a cell of the own ship or a voxel structure (asteroid, wreck).</summary>
        private bool StructCellWorld(string structureId, Vector3i cell, out Vector3 world, out BlockId block)
        {
            world = default;
            block = BlockId.Air;
            var local = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            if (structureId == _shipStructureId && _shipCells != null && _ship != null)
            {
                _shipCells.TryGetValue(cell, out block);
                world = _ship.transform.TransformPoint(local - _shipCentre);
                return true;
            }

            if (_structs.TryGetValue(structureId, out var vs) && vs.Cells != null && vs.Root != null)
            {
                vs.Cells.TryGetValue(cell, out block);
                world = vs.Root.transform.TransformPoint(local - vs.Centre);
                return true;
            }

            return false;
        }

        /// <summary>Applies a server structure edit to the local voxel grid (ship or asteroid) + re-meshes.</summary>
        private void OnStructureBlockChanged(BlocksBeyondTheStars.Networking.Messages.StructureBlockChanged m)
        {
            var key = new Vector3i(m.X, m.Y, m.Z);

            // #2155: a mined-out cell breaks into weightless chunks in its colours (before the grid forgets it).
            if (m.Block == 0 && _active && StructCellWorld(m.StructureId, key, out var at, out var old) && old.Value != 0)
            {
                var colors = Game?.Views.Mining != null ? Game.Views.Mining.BlockColors(old) : new[] { new Color(0.5f, 0.45f, 0.4f) };
                float s = m.StructureId == _shipStructureId ? FlightShipScale : 1f;
                FxKit.Burst(FxKit.Kind.DebrisFloat, at, 6, Vector3.zero, 0f, 0.8f, 2.6f, 0.2f * s, 0.34f * s, 1f, 1.6f, colors[0], colors[colors.Length - 1]);
                FxKit.Burst(FxKit.Kind.Smoke, at, 2, Vector3.zero, 0f, 0.2f, 0.8f, 0.4f * s, 0.7f * s, 0.8f, 1.2f, Color.Lerp(colors[0], Color.white, 0.3f));
                FxKit.Flash(at, Color.Lerp(colors[0], Color.white, 0.6f), 1.1f * s, 0.12f);
            }
            if (m.StructureId == _shipStructureId && _shipCells != null)
            {
                if (m.Block == 0) { _shipCells.Remove(key); }
                else { _shipCells[key] = new BlockId(m.Block); }

                RebuildShipVoxels();
                return;
            }

            if (_structs.TryGetValue(m.StructureId, out var vs) && vs.Cells != null)
            {
                if (m.Block == 0) { vs.Cells.Remove(key); }
                else { vs.Cells[key] = new BlockId(m.Block); }

                vs.MeshDirty = true;
            }
        }

        /// <summary>A voxel structure design arrived (item 20 S3 — asteroids). The own ship (Kind "ship") is
        /// handled via Game.ShipDesign; other kinds become static voxel bodies at their world position.</summary>
        private void OnStructureDesign(BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign m)
        {
            // Whitelist, not blacklist (#954): "ship" is the own hull (Game.ShipDesign), "ship_remote"
            // only feeds the remote avatar's hull — building either here spawned a static, unscaled,
            // collider-less ghost copy at the design position (players: the scene origin). A "wreck" (#1664)
            // is the system's derelict: a static voxel hull like an asteroid.
            if (m.Kind != "asteroid" && m.Kind != "station" && m.Kind != "wreck" && m.Kind != "debris")
            {
                return; // "debris" (#2353/#2356): a wreckage fragment — a static voxel hull like an asteroid
            }

            var cells = CellsFromDesign(m, out var centre);
            if (_structs.TryGetValue(m.Id, out var existing) && existing.Root != null)
            {
                Destroy(existing.Root); // a fresh design replaces the old mesh
            }

            var vs = new VoxStruct
            {
                Cells = cells, Centre = centre, Pos = new Vector3(m.PosX, m.PosY, m.PosZ), Root = null, MeshDirty = true, Kind = m.Kind,
                Doors = SpaceDoors.DoorsOf(m), // #2607: a player station's doors are doors out here too
                OwnerId = m.OwnerId ?? string.Empty,
                Id = m.Id ?? string.Empty,
                Derelict = m.Kind == "station" && BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsGeneratedDerelictId(m.Id), // #2676 (not The Long Quiet)
            };
            vs.DoorCells = SpaceDoors.DoorwayCells(vs.Doors);

            // Authored dye/glow + shapes ride along, so a station's blue solar wings and its shaped cells mesh as designed.
            bool hasTint = m.Tint != null && m.Tint.Length == m.Block.Length;
            bool hasGlow = m.Glow != null && m.Glow.Length == m.Block.Length;
            bool hasShape = m.Shape != null && m.Shape.Length == m.Block.Length;
            if (hasTint || hasGlow || hasShape)
            {
                vs.Mods = new Dictionary<Vector3i, (int, int)>();
                vs.Shapes = new Dictionary<Vector3i, int>();
                for (int i = 0; i < m.Block.Length; i++)
                {
                    var key = new Vector3i(m.X[i], m.Y[i], m.Z[i]);
                    int tint = hasTint ? m.Tint[i] : 0, glow = hasGlow ? m.Glow[i] : 0;
                    if (tint != 0 || glow != 0) vs.Mods[key] = (tint, glow);
                    if (hasShape && m.Shape[i] != 0) vs.Shapes[key] = m.Shape[i];
                }
            }

            if (cells.Count > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
                foreach (var c in cells.Keys)
                {
                    if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                    if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
                }

                vs.Half = new Vector3((maxX - minX + 1) * 0.5f, (maxY - minY + 1) * 0.5f, (maxZ - minZ + 1) * 0.5f);
            }

            if (m.HasDock)
            {
                vs.HasDock = true;
                vs.Dock = vs.Pos + new Vector3(m.DockX, m.DockY, m.DockZ) - centre;
                vs.DockOut = new Vector3(m.DockOutX, 0f, m.DockOutZ);
            }

            _structs[m.Id] = vs;
        }

        /// <summary>#1917: a station's distance from a root-local point — to its hull's box when the flight view holds its
        /// voxel hull (a generated station or a player's build), else to its centre. The server measures boarding the same way.</summary>
        private float StationDistance(BlocksBeyondTheStars.Networking.Messages.NetCombatEntity e, Vector3 point)
        {
            var centre = new Vector3(e.X, e.Y, e.Z);
            if (_structs.TryGetValue(e.Id, out var vs) && vs.IsStation && vs.Cells != null && vs.Cells.Count > 0)
            {
                Vector3 d = point - vs.Pos;
                var outside = new Vector3(
                    Mathf.Max(0f, Mathf.Abs(d.x) - vs.Half.x),
                    Mathf.Max(0f, Mathf.Abs(d.y) - vs.Half.y),
                    Mathf.Max(0f, Mathf.Abs(d.z) - vs.Half.z));
                return outside.magnitude;
            }

            return (centre - point).magnitude;
        }

        /// <summary>#1917: where a ship lines up in front of a station's hangar mouth — far enough out to clear the hull box —
        /// or null when the station has no known mouth.</summary>
        private Vector3? StationApproachPoint(string stationId, float extra)
        {
            if (!_structs.TryGetValue(stationId, out var vs) || !vs.HasDock)
            {
                return null;
            }

            // The mouth may sit deep in the hull's box: go out past the box face on the mouth's side, then some.
            Vector3 fromCentre = vs.Dock - vs.Pos;
            float recess = vs.DockOut.x != 0f
                ? vs.Half.x - Mathf.Abs(fromCentre.x)
                : vs.Half.z - Mathf.Abs(fromCentre.z);
            return vs.Dock + vs.DockOut * (Mathf.Max(0f, recess) + extra);
        }

        /// <summary>A space entity was destroyed — if it was a voxel asteroid body, drop its mesh (item 20 S3).</summary>
        private void OnStructEntityDestroyed(BlocksBeyondTheStars.Networking.Messages.SpaceEntityDestroyed m)
        {
            if (_structs.TryGetValue(m.Id, out var vs))
            {
                _structRemove.Add(m.Id);

                // #2156: an asteroid (or a salvaged-out wreck) breaks up instead of vanishing.
                if (_active && vs.Root != null)
                {
                    var rs = vs.Root.GetComponentsInChildren<Renderer>(false);
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds;
                        for (int i = 1; i < rs.Length; i++)
                        {
                            b.Encapsulate(rs[i].bounds);
                        }

                        SpaceFx.AsteroidBreak(b.center, b.size.magnitude * 0.5f, new Color(0.5f, 0.44f, 0.38f));
                    }
                }
            }
        }

        /// <summary>Builds/updates/removes the asteroid voxel bodies' GameObjects (item 20 S3). Runs while the
        /// flight view is active so designs that arrived before the scene existed get built once it does.</summary>
        private void ReconcileStructs()
        {
            if (_root == null)
            {
                return;
            }

            if (_structRemove.Count > 0)
            {
                foreach (var id in _structRemove)
                {
                    if (_structs.TryGetValue(id, out var vs))
                    {
                        if (vs.Root != null) { Destroy(vs.Root); }
                        _structs.Remove(id);
                    }
                }

                _structRemove.Clear();
            }

            // S5: unload far voxel bodies (keep their data; rebuild when you come back near) so dozens of
            // structures don't all carry live meshes. The reference is the suit on an EVA, else the ship.
            Vector3 viewer = _eva ? _evaPos : (_ship != null ? _ship.transform.localPosition : Vector3.zero);
            float farSq = FarStructUnload * FarStructUnload;
            float stationReach = 0f;
            foreach (var vs in _structs.Values)
            {
                bool near;
                if (vs.IsStation)
                {
                    // #1917: a station hull is a landmark — kept across the whole flight zone, measured to its box, and
                    // the flight clamp grows so every hull (and the space around it) stays reachable.
                    Vector3 d = viewer - vs.Pos;
                    var outside = new Vector3(Mathf.Max(0f, Mathf.Abs(d.x) - vs.Half.x), Mathf.Max(0f, Mathf.Abs(d.y) - vs.Half.y), Mathf.Max(0f, Mathf.Abs(d.z) - vs.Half.z));
                    near = outside.sqrMagnitude <= FarStationUnload * FarStationUnload;
                    stationReach = Mathf.Max(stationReach, vs.Pos.magnitude + vs.Half.magnitude + StationReachMargin);
                }
                else
                {
                    near = (vs.Pos - viewer).sqrMagnitude <= farSq;
                }

                if (near && vs.Root == null)
                {
                    vs.MeshDirty = true; // came back into range → rebuild below
                }
                else if (!near && vs.Root != null)
                {
                    Destroy(vs.Root);
                    vs.Root = null; // out of range → drop the mesh, keep the data
                }
            }

            _stationReach = stationReach;

            bool stationBuilt = false;
            foreach (var vs in _structs.Values)
            {
                if (!vs.MeshDirty)
                {
                    continue;
                }

                if (vs.IsStation && stationBuilt)
                {
                    continue; // a station hull is thousands of cells — mesh one per frame so entering space doesn't stall
                }

                if (vs.Root == null)
                {
                    vs.Root = new GameObject(vs.IsStation ? "StationHull" : "Asteroid");
                    vs.Root.transform.SetParent(_root.transform, false);
                    vs.Root.transform.localPosition = vs.Pos;
                }

                BuildVoxChunks(vs.Root.transform, vs.Cells, vs.Centre, vs.Derelict ? DerelictGrime() : null, vs.Mods, vs.Shapes);
                AddPlacedDoors(vs.Root.transform, vs.Doors, vs.Centre); // #2607 (the next rebuild clears them with the chunks)
                if (vs.Derelict)
                {
                    AddDerelictDecor(vs); // #2676 (a child of the root — the next rebuild clears it with the chunks, then re-adds it)
                }

                vs.MeshDirty = false;
                stationBuilt |= vs.IsStation;
            }
        }

        /// <summary>Tears down all asteroid voxel bodies (on leaving the flight view).</summary>
        private void ClearStructs()
        {
            foreach (var vs in _structs.Values)
            {
                if (vs.Root != null) { Destroy(vs.Root); }
            }

            _structs.Clear();
            _structRemove.Clear();
        }

        /// <summary>Clears <paramref name="parent"/> and (re)builds voxel chunk meshes + colliders from a sparse
        /// block grid, centred on <paramref name="centre"/> — shared by the ship (S1/S2, with the hull paint
        /// resolver) and asteroids (S3, unpainted).</summary>
        private void BuildVoxChunks(Transform parent, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            System.Func<BlockId, Color> paint = null,
            Dictionary<Vector3i, (int Tint, int Glow)> mods = null, Dictionary<Vector3i, int> shapes = null)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                DestroyVoxChunk(parent.GetChild(i).gameObject); // free the child's fresh meshes, not just the GameObject
            }

            if (cells == null || cells.Count == 0)
            {
                return;
            }

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in cells.Keys)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            BlockId WorldBlock(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;
            // Feed the mesher the hull's shapes so a full cube next to a slab/ramp/sphere keeps the face the
            // shaped cell doesn't cover — otherwise it's culled and you see through the hull into the ship (#420 M12).
            System.Func<int, int, int, int> WorldShape = shapes == null ? null
                : (x, y, z) => shapes.TryGetValue(new Vector3i(x, y, z), out var s) ? s : 0;

            // The whole hull's lamps, so lighting carries across this hull's chunk seams (#776).
            var lights = ShipMeshBuilder.LightSources(Game.Content, cells, mods);
            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };

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
                        ShipMeshBuilder.ApplyMods(chunk, lx, ly, lz, wc, mods, shapes);
                    }
                }

                var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, WorldBlock, Game.Atlas, paintTint: paint, lights: lights, worldShape: WorldShape);
                if (mesh.vertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject($"VoxChunk {cx},{cy},{cz}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - centre;
                go.AddComponent<MeshCollider>().sharedMesh = collider; // S2/S3: hull/ore collision (suit + future) — added FIRST, see LandedShipView
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
        }

        /// <summary>Destroys a vox-chunk GameObject AND the fresh render + collision meshes ChunkMesher.Build
        /// allocated for it. Unity does not free a MeshFilter/MeshCollider's sharedMesh with the GameObject, so
        /// without this every ship/asteroid re-mesh (each edit, each hull repaint) leaks two meshes per chunk.</summary>
        private void DestroyVoxChunk(GameObject go)
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

        /// <summary>Builds a cell dict + its centre from a structure design message (shared by ship + asteroid).</summary>
        private static Dictionary<Vector3i, BlockId> CellsFromDesign(BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d, out Vector3 centre)
        {
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

            centre = cells.Count == 0 ? Vector3.zero
                : new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            return cells;
        }

        private Material _asteroidMat; // shared stone material for the field's asteroids (rebuilt per view)

        private Material AsteroidMat()
            => _asteroidMat ??= Lit(new Color(0.62f, 0.60f, 0.57f), LoadTex("asteroid_rock") ?? LoadTex("stone"), new Vector2(1.5f, 1.5f));

        // ---------------- #2676: a derelict looks broken from outside ----------------

        private Material _derelictDebrisMat; // the tumbling pieces' plating (rebuilt per view, like the asteroid material)

        /// <summary>#2676: a derelict hull's grime — its iron plating meshed a little darker and warmer through the mesher's
        /// hull-paint stream (the ship-colour path, item 32): baked into the mesh's vertex data, so neither a material
        /// instance nor a MaterialPropertyBlock touches the chunk renderers — the SRP batcher keeps batching them.</summary>
        private System.Func<BlockId, Color> DerelictGrime()
            => Game.Content != null ? ShipMeshBuilder.HullPaint(Game.Content, new Color(0.72f, 0.67f, 0.62f)) : null;

        /// <summary>
        /// #2676: adds the broken-look extras to a derelict's freshly built hull, planned once from its cells
        /// (<see cref="BlocksBeyondTheStars.Client.Core.DerelictDecorPlan"/> — no protocol change) and then only animated
        /// (<see cref="DerelictDecor"/>): three to six small pieces tumbling slowly beside the hull (no collider), a small
        /// unlit cube on the dead beacon that flickers now and then (no real light) and a rare shower of sparks at a ragged
        /// edge (the pooled FX). The voxel hull itself is never moved or rotated. The holder is a child of the hull root, so
        /// the next hull rebuild (a mined block) clears it with the chunks and this adds it afresh.
        /// </summary>
        private void AddDerelictDecor(VoxStruct vs)
        {
            if (vs.Root == null || vs.Cells == null || vs.Cells.Count == 0)
            {
                return;
            }

            var content = Game.Content;
            var plan = BlocksBeyondTheStars.Client.Core.DerelictDecorPlan.Build(vs.Cells, vs.Id,
                id => content != null && content.BlockById(id)?.Key == "light_white_off"); // the beta wreck's dead beacon
            var holder = new GameObject("DerelictDecor");
            holder.transform.SetParent(vs.Root.transform, false);
            Vector3 Local(Vector3i c) => new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - vs.Centre;

            GameObject beacon = null;
            if (plan.BeaconCell is { } lamp)
            {
                beacon = Cube("Beacon", holder.transform, Local(lamp) + new Vector3(0f, 0.75f, 0f), Vector3.one * 0.45f,
                    FxKit.Unlit(new Color(1f, 0.82f, 0.35f))); // the old model's yellow, dying
                beacon.SetActive(false);
            }

            var mat = _derelictDebrisMat != null ? _derelictDebrisMat : (_derelictDebrisMat = LitTex("iron_wall", new Color(0.5f, 0.47f, 0.44f), 1f));
            var pieces = new Transform[plan.DebrisHomes.Count];
            for (int i = 0; i < pieces.Length; i++)
            {
                float size = Random.Range(0.35f, 0.8f);
                var piece = Cube("Debris", holder.transform, Local(plan.DebrisHomes[i]),
                    new Vector3(size, size * Random.Range(0.35f, 1f), size * Random.Range(0.6f, 1.3f)), mat);
                piece.transform.localRotation = Random.rotation;
                pieces[i] = piece.transform;
            }

            var sparks = new Vector3[plan.SparkCells.Count];
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = Local(plan.SparkCells[i]);
            }

            holder.AddComponent<DerelictDecor>().Configure(beacon, pieces, sparks);
        }

        /// <summary>#2676: animates a derelict's broken-look extras (see <see cref="AddDerelictDecor"/>) — a handful of
        /// transforms, one toggled cube and now and then a few pooled particles per frame; nothing is meshed or lit.</summary>
        private sealed class DerelictDecor : MonoBehaviour
        {
            private GameObject _beacon;
            private Transform[] _pieces = System.Array.Empty<Transform>();
            private Vector3[] _home, _spinAxis, _drift, _sparks = System.Array.Empty<Vector3>();
            private float[] _spinSpeed, _phase, _rate;
            private float _nextSpark, _nextFlicker;
            private int _flickers;

            public void Configure(GameObject beacon, Transform[] pieces, Vector3[] sparks)
            {
                _beacon = beacon;
                _pieces = pieces;
                _sparks = sparks;
                int n = pieces.Length;
                _home = new Vector3[n];
                _spinAxis = new Vector3[n];
                _drift = new Vector3[n];
                _spinSpeed = new float[n];
                _phase = new float[n];
                _rate = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _home[i] = pieces[i].localPosition;
                    _spinAxis[i] = Random.onUnitSphere;
                    _spinSpeed[i] = Random.Range(6f, 22f); // degrees per second: a slow tumble
                    _drift[i] = Random.onUnitSphere * Random.Range(0.3f, 0.8f);
                    _phase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _rate[i] = Random.Range(0.15f, 0.35f);
                }

                _nextSpark = Time.time + Random.Range(1f, 4f);
                _nextFlicker = Time.time + Random.Range(0.5f, 2f);
            }

            private void Update()
            {
                float t = Time.time, dt = Time.deltaTime;
                for (int i = 0; i < _pieces.Length; i++)
                {
                    var p = _pieces[i];
                    if (p == null)
                    {
                        continue;
                    }

                    p.Rotate(_spinAxis[i], _spinSpeed[i] * dt, Space.Self);
                    p.localPosition = _home[i] + _drift[i] * Mathf.Sin(t * _rate[i] + _phase[i]); // drifting to and fro
                }

                if (_beacon != null && t >= _nextFlicker)
                {
                    // A dying lamp: dark for a while, then a short stutter of blinks (an even count, so it ends dark).
                    if (_flickers > 0)
                    {
                        _beacon.SetActive(!_beacon.activeSelf);
                        _flickers--;
                        _nextFlicker = t + Random.Range(0.04f, 0.18f);
                    }
                    else
                    {
                        _beacon.SetActive(false);
                        _flickers = 2 * Random.Range(1, 4);
                        _nextFlicker = t + Random.Range(1.2f, 4f);
                    }
                }

                if (_sparks.Length > 0 && t >= _nextSpark)
                {
                    // A few weightless sparks and a pop of light at a ragged edge (pooled particles, no light).
                    var at = transform.TransformPoint(_sparks[Random.Range(0, _sparks.Length)]);
                    FxKit.Burst(FxKit.Kind.Motes, at, 6, Vector3.zero, 0f, 1.2f, 3.5f, 0.05f, 0.11f, 0.25f, 0.6f,
                        new Color(1f, 0.78f, 0.35f), new Color(1f, 0.95f, 0.75f));
                    FxKit.Flash(at, new Color(1f, 0.85f, 0.5f), 0.7f, 0.08f);
                    _nextSpark = t + Random.Range(1.5f, 5f);
                }
            }
        }
    }
}
