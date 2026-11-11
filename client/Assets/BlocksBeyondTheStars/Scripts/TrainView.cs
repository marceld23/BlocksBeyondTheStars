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
    /// arc (the cab's arc from the server, extrapolated by speed and direction between updates — bouncing off a line's
    /// ends like the server — and eased to each new one, see <see cref="TrainDrawArc"/>; the wagons hang behind it by the
    /// train's spacing and its fixed facing, #2741, so a reversal moves no wagon); a wagon's transform IS the rider's
    /// frame: local X across, Y up from the floor, Z toward the cab — the player controller parents itself to it while
    /// aboard and the remote avatars are placed with <see cref="Transform.TransformPoint"/>. It runs early in the frame (#2129) so the wagons are posed before
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
            public int DrawDirection = 1; // the drawing's own direction of travel (#2741: it bounces like the server's)
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

        /// <summary>The train's drawn arc: the server's last arc run on by its speed (#3444: the velocity it sends) and
        /// direction, eased toward each update.</summary>
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
                // #3444: the speed the server runs it at right now (it eases in and brakes into stops); an older server
                // sends none, and its running train is drawn at its setting's old fixed speed.
                float speed = TrainDrawArc.PredictSpeed(t.Velocity, t.Halted, t.Autopilot, t.Speed);
                float age = (float)System.Math.Max(0.0, now - Game.TrainsReceivedAt);

                // #2741: the wagons are posed by the train's fixed facing (an older server sends none: its direction), so a
                // reversal at a line end only turns the direction round — the drawing bounces there like the server does.
                int facing = RailRules.PoseFacing(t.Facing, t.Direction);
                float span = RailRules.TrainSpan(t.Wagons?.Length ?? 0);
                var (drawArc, drawDirection) = TrainDrawArc.Step(
                    view.HasArc, view.DrawArc, view.DrawDirection,
                    t.Arc, t.Direction, facing, speed, age,
                    Time.deltaTime, spline.TotalArc, span, spline.Closed);
                view.DrawArc = drawArc;
                view.DrawDirection = drawDirection;
                view.HasArc = true;

                for (int i = 0; i < view.Wagons.Count; i++)
                {
                    var root = view.Wagons[i].Root;
                    if (root == null)
                    {
                        continue;
                    }

                    float arc = spline.NormalizeArc(RailRules.WagonArc(view.DrawArc, i, facing));
                    var (pos, yaw) = spline.PoseAt(arc, facing);
                    root.transform.position = Game.ScenePos(pos.X, pos.Y, pos.Z);
                    root.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), Vector3.up);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // The wagon meshes
        // ------------------------------------------------------------------------------------------------

        // The grid and the furnishing are shared data (RailWagonLayout): the seats the server hands out sit on these cells.

        private void BuildWagonMesh(GameObject root, string item, bool cab)
        {
            if (Game.ChunkMaterial == null || Game.Atlas == null || Game.Content == null)
            {
                return;
            }

            var cells = new Dictionary<Vector3i, (BlockId Block, int Shape)>();
            foreach (var c in RailWagonLayout.Cells(item, cab))
            {
                var id = Id(c.Block);
                if (!id.IsAir)
                {
                    cells[new Vector3i(c.X, c.Y, c.Z)] = (id, c.Shape);
                }
            }

            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };
            var paint = ShipMeshBuilder.HullPaint(Game.Content, cab ? new Color(0.55f, 0.72f, 0.92f) : new Color(0.82f, 0.85f, 0.9f));
            BlockId CellAt(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b.Block : BlockId.Air;
            // #3471: the benches and berths are shaped cells — the mesher must see their forms, or the floor under a bench
            // stays culled as if a full cube stood on it (the hole the seated eye looked through).
            int ShapeAt(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b.Shape : 0;
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            foreach (var kv in cells)
            {
                chunk.Set(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value.Block);
                if (kv.Value.Shape != 0)
                {
                    chunk.SetShape(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value.Shape);
                }
            }

            var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas, paintTint: paint, worldShape: ShapeAt);
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
            var origin = RailWagonLayout.GridOrigin;
            go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z);
            if (collider != null)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = collider;
            }

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<OwnedProceduralMesh>();
        }

        private BlockId Id(string key) => Game.Content.GetBlock(key)?.NumericId ?? BlockId.Air;
    }
}
