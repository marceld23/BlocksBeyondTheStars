// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws and moves the monorail trains (#2113). Every wagon is a hand-authored voxel grid (like the speeder hull) meshed
    /// once by <see cref="ChunkMesher"/> — a floor, low side walls with wide door openings, a roof on posts, the cab's
    /// windscreen and the seats of a seat wagon — with a <see cref="MeshCollider"/> that is always solid: riders stand on
    /// the floor and walk between the walls. Each frame every wagon is posed on the shared <see cref="RailSpline"/> at its
    /// arc (the cab's arc from the server, extrapolated by speed and direction between updates and eased to each new one,
    /// the wagons behind it by the train's spacing); a wagon's transform IS the rider's frame: local X across, Y up from
    /// the floor, Z toward the cab — the player controller parents itself to it while aboard and the remote avatars are
    /// placed with <see cref="Transform.TransformPoint"/>. It runs early in the frame (#2129) so the wagons are posed before
    /// the player controller syncs physics and moves a rider standing in one.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class TrainView : MonoBehaviour
    {
        public GameBootstrap Game;
        public RailView Rails;

        private sealed class Wagon
        {
            public GameObject Root;
            public string Item;
        }

        private sealed class Train
        {
            public GameObject Root;
            public readonly List<Wagon> Wagons = new List<Wagon>();
            public float DrawArc;
            public bool HasArc;
            public string Key = string.Empty; // the wagon list, to notice a coupling
        }

        private readonly Dictionary<string, Train> _trains = new Dictionary<string, Train>();
        private readonly HashSet<string> _live = new HashSet<string>();
        private readonly List<string> _stale = new List<string>();

        /// <summary>The wagon transform of a frame id (train:wagon), for the local player and the remote avatars.</summary>
        public bool TryGetWagon(string frameId, out Transform wagon)
        {
            wagon = null;
            if (!RailRules.TryParseFrame(frameId, out string trainId, out int index) || !_trains.TryGetValue(trainId, out var t))
            {
                return false;
            }

            if (index < 0 || index >= t.Wagons.Count || t.Wagons[index].Root == null)
            {
                return false;
            }

            wagon = t.Wagons[index].Root.transform;
            return true;
        }

        /// <summary>The nearest wagon of any train to a scene point (its train, its index, the distance), or null.</summary>
        public NetTrain NearestWagon(Vector3 scenePos, out int wagon, out float distance)
        {
            wagon = 0;
            distance = float.MaxValue;
            NetTrain best = null;
            if (Game?.Trains == null)
            {
                return null;
            }

            foreach (var t in Game.Trains)
            {
                if (t == null || !_trains.TryGetValue(t.Id, out var view))
                {
                    continue;
                }

                for (int i = 0; i < view.Wagons.Count; i++)
                {
                    var root = view.Wagons[i].Root;
                    if (root == null)
                    {
                        continue;
                    }

                    // The wagon's box, not its centre: a rider at a door is beside the wall, not at the middle.
                    var local = root.transform.InverseTransformPoint(scenePos);
                    float dx = Mathf.Max(0f, Mathf.Abs(local.x) - RailRules.WagonWidth * 0.5f);
                    float dz = Mathf.Max(0f, Mathf.Abs(local.z) - RailRules.WagonLength * 0.5f);
                    float dy = Mathf.Max(0f, Mathf.Abs(local.y - 1.2f) - 1.6f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < distance)
                    {
                        distance = d;
                        best = t;
                        wagon = i;
                    }
                }
            }

            return best;
        }

        /// <summary>#2129: the train whose wagon holds the feet at <paramref name="scenePos"/> — inside the walls, from just
        /// under the floor to below the roof — or null. Standing on the roof or beside the train is not inside. With an
        /// <paramref name="endSlack"/> a point up to that far past a wagon's open end still counts (the gap between two
        /// coupled wagons); the wagon it overshoots least wins.</summary>
        public NetTrain WagonAt(Vector3 scenePos, out int wagon, float endSlack = 0f)
        {
            wagon = 0;
            NetTrain best = null;
            float bestOver = float.MaxValue;
            if (Game?.Trains == null)
            {
                return null;
            }

            foreach (var t in Game.Trains)
            {
                if (t == null || !_trains.TryGetValue(t.Id, out var view) || view.Root == null || !view.Root.activeSelf)
                {
                    continue;
                }

                for (int i = 0; i < view.Wagons.Count; i++)
                {
                    var root = view.Wagons[i].Root;
                    if (root == null)
                    {
                        continue;
                    }

                    var local = root.transform.InverseTransformPoint(scenePos);
                    float over = Mathf.Max(0f, Mathf.Abs(local.z) - (RailRules.WagonLength * 0.5f));
                    if (Mathf.Abs(local.x) <= (RailRules.WagonWidth * 0.5f) - 0.1f
                        && over <= endSlack && over < bestOver
                        && local.y >= -0.3f && local.y <= RailRules.WagonHeight - 0.4f)
                    {
                        best = t;
                        bestOver = over;
                        wagon = i;
                    }
                }
            }

            return best;
        }

        private void Update()
        {
            if (Game == null || Game.Trains == null || Rails == null)
            {
                return;
            }

            Reconcile();
            Pose();
        }

        private void Reconcile()
        {
            _live.Clear();
            foreach (var t in Game.Trains)
            {
                if (t != null && !string.IsNullOrEmpty(t.Id))
                {
                    _live.Add(t.Id);
                }
            }

            _stale.Clear();
            foreach (var kv in _trains)
            {
                if (!_live.Contains(kv.Key))
                {
                    _stale.Add(kv.Key);
                }
            }

            foreach (var id in _stale)
            {
                if (_trains[id].Root != null)
                {
                    Destroy(_trains[id].Root);
                }

                _trains.Remove(id);
            }

            foreach (var t in Game.Trains)
            {
                if (t == null || string.IsNullOrEmpty(t.Id))
                {
                    continue;
                }

                if (!_trains.TryGetValue(t.Id, out var view) || view.Root == null)
                {
                    view = new Train { Root = new GameObject("Train " + t.Id) };
                    view.Root.transform.SetParent(transform, false);
                    _trains[t.Id] = view;
                }

                string key = string.Join("+", t.Wagons ?? System.Array.Empty<string>());
                if (key != view.Key)
                {
                    view.Key = key;
                    RebuildWagons(view, t);
                }
            }
        }

        private void RebuildWagons(Train view, NetTrain t)
        {
            foreach (var w in view.Wagons)
            {
                if (w.Root != null)
                {
                    Destroy(w.Root);
                }
            }

            view.Wagons.Clear();
            var wagons = t.Wagons ?? System.Array.Empty<string>();
            for (int i = 0; i < wagons.Length; i++)
            {
                var root = new GameObject("Wagon " + i + " " + wagons[i]);
                root.transform.SetParent(view.Root.transform, false);
                BuildWagonMesh(root, wagons[i], i == 0);
                view.Wagons.Add(new Wagon { Root = root, Item = wagons[i] });
            }
        }

        /// <summary>The train's drawn arc: the server's last arc run on by speed and direction, eased toward each update.</summary>
        private void Pose()
        {
            double now = Time.timeAsDouble;
            foreach (var t in Game.Trains)
            {
                if (t == null || !_trains.TryGetValue(t.Id, out var view))
                {
                    continue;
                }

                var spline = Rails.SplineOf(t.LineId);
                if (spline == null)
                {
                    view.Root.SetActive(false);
                    continue;
                }

                view.Root.SetActive(true);
                float speed = t.Halted ? 0f : RailRules.SpeedTable[Mathf.Clamp(t.Speed, 1, 3) - 1];
                float age = (float)System.Math.Max(0.0, now - Game.TrainsReceivedAt);
                float target = spline.NormalizeArc(t.Arc + t.Direction * speed * Mathf.Min(age, 1.0f));
                if (!view.HasArc)
                {
                    view.DrawArc = target;
                    view.HasArc = true;
                }
                else
                {
                    // Run on at the train's own speed, eased toward the server's clock (a loop wraps: take the short way).
                    float run = view.DrawArc + t.Direction * speed * Time.deltaTime;
                    float delta = target - run;
                    if (spline.Closed && spline.TotalArc > 0f)
                    {
                        float total = spline.TotalArc;
                        delta = ((delta % total) + total) % total;
                        if (delta > total * 0.5f)
                        {
                            delta -= total;
                        }
                    }

                    view.DrawArc = spline.NormalizeArc(run + delta * Mathf.Clamp01(Time.deltaTime * 4f));
                }

                for (int i = 0; i < view.Wagons.Count; i++)
                {
                    var root = view.Wagons[i].Root;
                    if (root == null)
                    {
                        continue;
                    }

                    float arc = spline.NormalizeArc(RailRules.WagonArc(view.DrawArc, i, t.Direction));
                    var (pos, yaw) = spline.PoseAt(arc, t.Direction);
                    root.transform.position = Game.ScenePos(pos.X, pos.Y, pos.Z);
                    root.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), Vector3.up);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // The wagon meshes
        // ------------------------------------------------------------------------------------------------

        // The grid: x 0..2 across (3 wide), y 0..4 (floor at 0, roof at 4), z 0..5 along (5 = the front).
        private const int W = 3, H = 5, L = 6;

        private void BuildWagonMesh(GameObject root, string item, bool cab)
        {
            if (Game.ChunkMaterial == null || Game.Atlas == null || Game.Content == null)
            {
                return;
            }

            var cells = WagonCells(item, cab);
            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };
            var paint = ShipMeshBuilder.HullPaint(Game.Content, cab ? new Color(0.55f, 0.72f, 0.92f) : new Color(0.82f, 0.85f, 0.9f));
            BlockId CellAt(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            foreach (var kv in cells)
            {
                chunk.Set(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value);
            }

            var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas, paintTint: paint);
            if (mesh.vertexCount == 0)
            {
                Destroy(mesh);
                if (collider != null)
                {
                    Destroy(collider);
                }

                return;
            }

            var go = new GameObject("Hull");
            go.transform.SetParent(root.transform, false);
            // Centre the grid on the wagon pose: the floor's top face is the frame's y = 0, the middle of the width its x = 0.
            go.transform.localPosition = new Vector3(-W * 0.5f, -1f, -L * 0.5f);
            if (collider != null)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = collider;
            }

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<OwnedProceduralMesh>();
        }

        /// <summary>The authored wagon: a floor, side walls with a door opening in the middle, a roof on posts; the cab adds a
        /// closed front with a windscreen, a seat wagon its benches, a sleeper its bunks, a bar its counter.</summary>
        private Dictionary<Vector3i, BlockId> WagonCells(string item, bool cab)
        {
            var cells = new Dictionary<Vector3i, BlockId>();
            var floor = Id("steel_floor");
            var wall = Id("metal_panel");
            var glass = Id("glass");
            var bench = Id("metal_panel");
            void Set(int x, int y, int z, BlockId id)
            {
                if (!id.IsAir)
                {
                    cells[new Vector3i(x, y, z)] = id;
                }
            }

            for (int x = 0; x < W; x++)
            {
                for (int z = 0; z < L; z++)
                {
                    Set(x, 0, z, floor);          // the floor
                    Set(x, H - 1, z, wall);       // the roof
                }
            }

            // Side walls: a low sill everywhere, glass above it, the door opening (z 2..3) open to the sill.
            for (int z = 0; z < L; z++)
            {
                bool door = z == 2 || z == 3;
                foreach (int x in new[] { 0, W - 1 })
                {
                    if (!door)
                    {
                        Set(x, 1, z, wall);
                        Set(x, 2, z, glass);
                        Set(x, 3, z, glass);
                    }
                    else
                    {
                        Set(x, 3, z, wall); // the lintel over the door
                    }
                }
            }

            // The ends: the cab's front is closed with a windscreen; every other end stays open for the walk-through.
            if (cab)
            {
                for (int x = 0; x < W; x++)
                {
                    Set(x, 1, L - 1, wall);
                    Set(x, 2, L - 1, glass);
                    Set(x, 3, L - 1, glass);
                }

                Set(1, 1, L - 2, wall); // the console
            }

            switch (item)
            {
                case "wagon_seats":
                    foreach (int z in new[] { 1, 3, 4 })
                    {
                        Set(0, 1, z, bench);
                        Set(W - 1, 1, z, bench);
                    }

                    break;
                case "wagon_sleeper":
                    foreach (int z in new[] { 1, 4 })
                    {
                        Set(0, 1, z, bench);
                        Set(W - 1, 1, z, bench);
                        Set(0, 2, z, bench);
                        Set(W - 1, 2, z, bench);
                    }

                    break;
                case "wagon_bar":
                    Set(0, 1, 4, bench);
                    Set(1, 1, 4, bench);
                    Set(W - 1, 1, 4, bench);
                    break;
            }

            return cells;
        }

        private BlockId Id(string key) => Game.Content.GetBlock(key)?.NumericId ?? BlockId.Air;
    }
}
