// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders every hover speeder on the current world as a small voxel OBJECT (the same chunk-meshed look the
    /// ships use, painted in the owner's hull colour), positions it (a parked speeder at its authoritative spot,
    /// a driven one following its driver), and adds the hover dust + engine glow plus the deploy/destruction
    /// bursts. Render-only — the speeders are server-authoritative; the client just shows what the
    /// <see cref="GameBootstrap.Speeders"/> snapshot describes. A PARKED vehicle is solid (#1662: you used to walk
    /// straight through a 3×5 hull and stand in the middle of it); the collider is switched off while the vehicle
    /// is driven, so it never fights the driver's character controller, and while the local player is inside
    /// its bounds, so a body that is somehow in there can step out instead of being held.
    /// </summary>
    public sealed class SpeederView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Obj
        {
            public GameObject Root;
            public Transform Glow0;
            public Transform Glow1;
            public MeshCollider Collider;        // the parked hull's collider (off while driven / while the local player is inside)
            public int HullColor = int.MinValue; // forces the first build
            public Vector3 LastPos;
            public float DustTimer;
            public bool HasPos;
            public bool Boat;                    // built from BoatCells(): wake instead of dust, no engine glow (#1215)
        }

        private readonly Dictionary<string, Obj> _objs = new();
        private WeaponFx _fx;
        private Material _glowMat;

        // The centred mesh offset (the design spans x:0..2, z:0..4) so the seat sits on the root (the driver).
        private static readonly Vector3 MeshOffset = new Vector3(-1f, -0.2f, -2f);
        private static readonly Vector3 BoatMeshOffset = new Vector3(-1f, -0.8f, -2f);

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (_fx == null)
            {
                _fx = Object.FindAnyObjectByType<WeaponFx>();
            }

            Reconcile();
            DrainFx();
        }

        // Reused across frames — Reconcile runs every Update; fresh sets/lists there are steady GC churn.
        private readonly HashSet<string> _liveScratch = new HashSet<string>();
        private readonly List<string> _staleScratch = new List<string>();

        private void Reconcile()
        {
            // Build the set of ids the server currently reports.
            var live = _liveScratch;
            live.Clear();
            foreach (var s in Game.Speeders)
            {
                if (s != null && !string.IsNullOrEmpty(s.Id))
                {
                    live.Add(s.Id);
                }
            }

            // Drop speeders that are gone (packed up, destroyed, owner left, world switch).
            var stale = _staleScratch;
            stale.Clear();
            foreach (var kv in _objs)
            {
                if (!live.Contains(kv.Key))
                {
                    stale.Add(kv.Key);
                }
            }

            foreach (var id in stale)
            {
                if (_objs[id].Root != null)
                {
                    Destroy(_objs[id].Root);
                }

                _objs.Remove(id);
            }

            // (Re)build + position the rest.
            foreach (var s in Game.Speeders)
            {
                if (s == null || string.IsNullOrEmpty(s.Id))
                {
                    continue;
                }

                if (!_objs.TryGetValue(s.Id, out var obj) || obj.Root == null)
                {
                    obj = new Obj { Root = new GameObject($"Speeder {s.OwnerId}") };
                    obj.Root.transform.SetParent(transform, false);
                    _objs[s.Id] = obj;
                }

                if (obj.HullColor != s.HullColor)
                {
                    obj.HullColor = s.HullColor;
                    BuildMesh(s, obj);
                }

                PositionAndAnimate(s, obj);
            }
        }

        /// <summary>Places the speeder: the local player's driven speeder rides on the player pose (smooth, local);
        /// every other speeder eases toward its authoritative pose (parked, or a remote driver's broadcast pose).</summary>
        private void PositionAndAnimate(NetSpeeder s, Obj obj)
        {
            bool localDriven = !string.IsNullOrEmpty(s.DriverId) && s.DriverId == Game.LocalPlayerId;

            Vector3 target;
            float yaw;
            if (localDriven)
            {
                target = Game.PlayerPosition;
                yaw = Game.PlayerYaw;
            }
            else
            {
                target = Game.ScenePos(s.X, s.Y, s.Z);
                yaw = s.Yaw;
            }

            var t = obj.Root.transform;
            if (!obj.HasPos)
            {
                t.position = target;
                obj.LastPos = target;
                obj.HasPos = true;
            }
            else
            {
                // Local pose is exact; remote/parked poses lerp so the ~2.5 Hz broadcast looks smooth.
                t.position = localDriven ? target : Vector3.Lerp(t.position, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            }

            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, yaw, 0f), 1f - Mathf.Exp(-12f * Time.deltaTime));

            // Solid only while parked (#1662). A driven hull would collide with its own driver's capsule (and a
            // remote driver's body), and a local player standing inside the bounds — dismounted before the
            // server stepped them off, or walked in while it was disabled — must be able to walk out. "Inside"
            // is the hull box itself shrunk by the capsule radius (VehicleHull, #1669): the old root-centred
            // box overhung the hull on two sides (walk-through) and counted the hull's roof as inside (fall-through).
            if (obj.Collider != null)
            {
                bool driven = !string.IsNullOrEmpty(s.DriverId);
                Vector3 local = t.InverseTransformPoint(Game.PlayerPosition);
                Vector3 off = obj.Boat ? BoatMeshOffset : MeshOffset;
                bool localInside = !driven && VehicleHull.Encloses(local.x, local.y, local.z, off.x, off.y, off.z);
                bool solid = !driven && !localInside;
                if (obj.Collider.enabled != solid)
                {
                    obj.Collider.enabled = solid;
                }
            }

            // Hover dust while moving, scaled by ground speed — a boat throws a white wake off the stern instead.
            float speed = (t.position - obj.LastPos).magnitude / Mathf.Max(1e-4f, Time.deltaTime);
            obj.LastPos = t.position;
            obj.DustTimer -= Time.deltaTime;
            if (_fx != null && obj.DustTimer <= 0f)
            {
                if (obj.Boat && speed > 1.5f)
                {
                    obj.DustTimer = 0.12f;
                    _fx.Sparks(t.position + Vector3.down * 0.35f - t.forward * 1.4f, new Color(0.78f, 0.9f, 1f), 3);
                }
                else if (!obj.Boat && speed > 2.5f)
                {
                    obj.DustTimer = 0.09f;
                    _fx.Dust(t.position + Vector3.down * 0.15f - t.forward * 0.6f, 2);
                }
            }

            // Engine glow pulses with movement (idle dim, cruising bright).
            float glow = Mathf.Clamp01(0.25f + speed * 0.06f);
            float s0 = 0.12f + 0.10f * glow + 0.03f * Mathf.Sin(Time.time * 18f);
            if (obj.Glow0 != null) obj.Glow0.localScale = Vector3.one * s0;
            if (obj.Glow1 != null) obj.Glow1.localScale = Vector3.one * s0;
        }

        /// <summary>Meshes the speeder hull (one small voxel grid → one ChunkMesher pass) painted in the hull
        /// colour, plus two cyan engine-glow cubes at the rear. No collider (render-only).</summary>
        private void BuildMesh(NetSpeeder s, Obj obj)
        {
            var root = obj.Root;
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(root.transform.GetChild(i).gameObject);
            }

            obj.Glow0 = obj.Glow1 = null;
            obj.Collider = null;

            if (Game.ChunkMaterial == null || Game.Atlas == null || Game.Content == null)
            {
                return;
            }

            obj.Boat = s.Kind == "boat";
            var cells = obj.Boat ? BoatCells() : SpeederCells();
            if (cells.Count == 0)
            {
                return;
            }

            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };

            // Unpainted default: the speeder's silver-blue, the boat's warm wood.
            int hull = s.HullColor != 0 ? s.HullColor : (obj.Boat ? 0xB07A46 : 0x9FB6D6);
            var paint = ShipMeshBuilder.HullPaint(Game.Content,
                new Color(((hull >> 16) & 0xFF) / 255f, ((hull >> 8) & 0xFF) / 255f, (hull & 0xFF) / 255f));

            BlockId CellAt(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;

            var coord = new ChunkCoord(0, 0, 0);
            var chunk = new ChunkData(coord);
            foreach (var kv in cells)
            {
                chunk.Set(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value);
            }

            // No worldShape delegate (unlike the ship paths in #420 M12): the speeder hull is a fixed procedural
            // cube grid built by SpeederCells() with no shaped (slab/ramp/…) cells, so no cube face can border a
            // shaped neighbour and there are no see-through holes to rescue.
            var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas, paintTint: paint);

            if (mesh.vertexCount > 0)
            {
                var go = new GameObject(obj.Boat ? "BoatHull" : "SpeederHull");
                go.transform.SetParent(root.transform, false);
                // The boat hull hangs lower so it sits IN the water (the driver floats 0.35 above the waterline,
                // the hull bottom ~0.45 below it) instead of perching on top like a hovering sled.
                go.transform.localPosition = obj.Boat ? BoatMeshOffset : MeshOffset;
                if (collider != null)
                {
                    // The collider FIRST (same order as the landed ship, #1528): a MeshCollider added after the
                    // MeshFilter adopts the non-readable render mesh and logs a cook error. Parked = solid (#1662);
                    // PositionAndAnimate switches it off while driven.
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = collider;
                    mc.enabled = string.IsNullOrEmpty(s.DriverId);
                    obj.Collider = mc;
                }

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                go.AddComponent<OwnedProceduralMesh>(); // freed by the child teardown on the next recolor/rebuild
            }
            else
            {
                Destroy(mesh); // empty grid — free the mesh instead of orphaning it
                if (collider != null)
                {
                    Destroy(collider);
                }
            }

            if (obj.Boat)
            {
                return; // no engine glow on a boat — the wake is its motion cue
            }

            // Engine glow at the rear (design z=0 → root-local z = -2 after centring).
            obj.Glow0 = MakeGlow(root.transform, new Vector3(-0.7f, 0.45f, -1.95f));
            obj.Glow1 = MakeGlow(root.transform, new Vector3(0.7f, 0.45f, -1.95f));
        }

        private Transform MakeGlow(Transform parent, Vector3 localPos)
        {
            if (_glowMat == null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
                _glowMat = new Material(shader) { color = ShaderColor.Srgb(new Color(0.35f, 0.85f, 1f)) };
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "EngineGlow";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.16f;
            go.GetComponent<Renderer>().sharedMaterial = _glowMat;
            return go.transform;
        }

        /// <summary>Plays the queued one-shot speeder effects (deploy shimmer / destruction burst) at their spot.</summary>
        private void DrainFx()
        {
            if (Game.PendingSpeederFx.Count == 0 || _fx == null)
            {
                return;
            }

            foreach (var fx in Game.PendingSpeederFx)
            {
                var at = Game.ScenePos(fx.X, fx.Y, fx.Z) + Vector3.up * 0.5f;
                if (fx.Kind == "explode")
                {
                    _fx.Flash(at, new Color(1f, 0.6f, 0.2f), 1.4f);
                    _fx.Sparks(at, new Color(1f, 0.55f, 0.18f), 22);
                    _fx.Sparks(at, new Color(0.3f, 0.3f, 0.3f), 14);
                }
                else if (fx.Kind == "splash") // a boat set onto the water (#1215)
                {
                    _fx.Sparks(at, new Color(0.72f, 0.88f, 1f), 18);
                    _fx.Pulse(at, new Color(0.5f, 0.8f, 1f));
                }
                else // "deploy"
                {
                    _fx.Pulse(at, new Color(0.4f, 0.9f, 1f));
                    _fx.Flash(at, new Color(0.5f, 0.95f, 1f), 0.7f);
                }
            }

            Game.PendingSpeederFx.Clear();
        }

        /// <summary>The hand-authored voxel design of the speeder: a small open sled — chassis floor, side rails,
        /// a backrest and a glass windscreen. Built from blocks that always exist; a missing key is skipped.</summary>
        private Dictionary<Vector3i, BlockId> SpeederCells()
        {
            var cells = new Dictionary<Vector3i, BlockId>();
            var hull = BlockKey("iron_wall") ?? BlockKey("metal_panel") ?? BlockKey("stone");
            var glass = BlockKey("glass") ?? hull;
            if (hull == null)
            {
                return cells;
            }

            // Floor (y=0): a 3×5 chassis.
            for (int x = 0; x <= 2; x++)
            for (int z = 0; z <= 4; z++)
            {
                cells[new Vector3i(x, 0, z)] = hull.Value;
            }

            // Side rails (y=1, x=0 and x=2) along the middle.
            for (int z = 1; z <= 3; z++)
            {
                cells[new Vector3i(0, 1, z)] = hull.Value;
                cells[new Vector3i(2, 1, z)] = hull.Value;
            }

            // Backrest (y=1, z=0).
            for (int x = 0; x <= 2; x++)
            {
                cells[new Vector3i(x, 1, 0)] = hull.Value;
            }

            // Glass windscreen at the front (y=1, z=4).
            for (int x = 0; x <= 2; x++)
            {
                cells[new Vector3i(x, 1, 4)] = glass.Value;
            }

            return cells;
        }

        /// <summary>The hand-authored voxel design of the boat (#1215): a 3×5 wooden hull with a pointed bow, low
        /// gunwales, a transom and a small metal outboard motor at the stern. Same footprint as the speeder so the
        /// centring offset and the driver seat (the root) line up; no glass, no glow. Wood first, metal fallback.</summary>
        private Dictionary<Vector3i, BlockId> BoatCells()
        {
            var cells = new Dictionary<Vector3i, BlockId>();
            var wood = BlockKey("wood_log") ?? BlockKey("wood_crate") ?? BlockKey("iron_wall") ?? BlockKey("stone");
            var metal = BlockKey("iron_wall") ?? BlockKey("metal_panel") ?? wood;
            if (wood == null)
            {
                return cells;
            }

            // Hull floor (y=0): full 3 wide from the stern to z=3, a single centre block as the bow tip.
            for (int x = 0; x <= 2; x++)
            for (int z = 0; z <= 3; z++)
            {
                cells[new Vector3i(x, 0, z)] = wood.Value;
            }

            cells[new Vector3i(1, 0, 4)] = wood.Value;

            // Gunwales (y=1) along both sides, meeting at the bow post.
            for (int z = 1; z <= 3; z++)
            {
                cells[new Vector3i(0, 1, z)] = wood.Value;
                cells[new Vector3i(2, 1, z)] = wood.Value;
            }

            cells[new Vector3i(1, 1, 4)] = wood.Value;

            // Transom (y=1, z=0) with the outboard motor in the middle.
            cells[new Vector3i(0, 1, 0)] = wood.Value;
            cells[new Vector3i(2, 1, 0)] = wood.Value;
            cells[new Vector3i(1, 1, 0)] = metal!.Value;

            return cells;
        }

        private BlockId? BlockKey(string key)
        {
            var def = Game.Content?.GetBlock(key);
            return def != null ? def.NumericId : (BlockId?)null;
        }
    }
}
