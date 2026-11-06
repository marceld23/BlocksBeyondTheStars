// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3258: where the transition cinematics' camera may go. A pad in a hall or a courtyard used to put the camera inside the
    /// buildings (a Physics spot check that missed uncooked colliders, an unchecked fallback), and the chunk visibility walk
    /// (#1823) rightly hid everything around it. Now the shot is planned up front against the voxel world
    /// (<see cref="TransitionShotPlan"/>, the samplers of the VEGA prologue: unknown cells are blocked) and whatever no outside
    /// spot can see plays from the cockpit — one cell behind the helm, riding with the hull (<see cref="TransitionCockpit"/>).
    /// A cheap per-frame safety net cuts inside at once when the outside view gets blocked after all (a block placed, a
    /// chunk streamed in); a way between the eyes and the helm that crosses the ship's own cells becomes a short blink.
    /// </summary>
    public sealed partial class TransitionDirector
    {
        private const float BlinkSeconds = 0.25f;
        private const int SafetyNetEveryFrames = 4;
        private const string CockpitStationType = "cockpit";
        private const string HelmBlockKey = "ship_helm";

        private enum ShotMode { Outside, Cockpit }

        private TransitionShotPlan _plan;
        private ShotMode _mode;
        private bool _stayInside;          // the safety net took the shot inside — it does not come out again
        private bool _homing;              // the touchdown's way home (glide or blink) has begun
        private Vector3 _cockpitLocal;     // the cockpit camera in the hull's own frame (rides with the stand-in / adopted hull)
        private Vector3 _cockpitDir = Vector3.forward;
        private bool _cockpitAtHelm;
        private bool _cabinGlide;          // the straight way helm ↔ eyes crosses no cell of the ship
        private Vector3 _eye;              // the player's eyes when the cinematic began
        private CinematicStageScan.ClearSampler _pathSampler;
        private CinematicStageScan.ClearSampler _spotSampler;
        private int _netFrame;
        private float _blinkTime = -1f;
        private System.Action _blinkSwitch;
        private CinematicFrame _blinkFrame;

        /// <summary>Plans the shot of a cinematic that is about to take the camera: the cockpit rig, the samplers, the outside
        /// spot and its stretch — and logs it. Needs <see cref="_hullBox"/>, <see cref="_restPos"/> and (touchdown)
        /// <see cref="_drop"/>; must run before <see cref="TakeCamera"/> (the camera still sits at the eyes).</summary>
        private void PlanShot(TransitionShotStage stage, LandedShipModel model, float distance, float ground)
        {
            _eye = Camera.transform.position;
            _mode = ShotMode.Cockpit;
            _stayInside = false;
            _homing = false;
            _netFrame = Time.frameCount;
            PlaceCockpit(model);
            BuildSamplers(model);
            _plan = null;
            if (_pathSampler != null)
            {
                var input = new TransitionShotInput(_pathSampler, _spotSampler)
                {
                    CenterX = _hullBox.center.x, CenterY = _hullBox.center.y, CenterZ = _hullBox.center.z,
                    ExtentX = _hullBox.extents.x, ExtentY = _hullBox.extents.y, ExtentZ = _hullBox.extents.z,
                    EyeX = _eye.x, EyeY = _eye.y, EyeZ = _eye.z,
                    Distance = distance, GroundHeight = ground, Drop = _drop,
                };
                _plan = stage == TransitionShotStage.Touchdown ? TransitionShotPlan.ForTouchdown(input) : TransitionShotPlan.ForLiftoff(input);
                _spot = new Vector3(_plan.SpotX, _plan.SpotY, _plan.SpotZ);
            }

            string plan = _plan != null ? _plan.Describe(_cabinGlide) : "no world to plan against — cockpit all the way";
            Debug.Log($"[Transition] shot plan: {plan}; cockpit camera {(_cockpitAtHelm ? "behind the helm" : "at the eyes")}.");
        }

        /// <summary>The voxel samplers of the shot (the VEGA prologue's, #777/#2461): unknown cells are blocked, except open sky
        /// above everything the server streams over the pad. The line of sight reads the own hull as air (every look target
        /// sits inside it) but another parked ship as solid; the camera spot avoids every hull.</summary>
        private void BuildSamplers(LandedShipModel own)
        {
            _pathSampler = _spotSampler = null;
            var world = Game.World;
            if (world == null)
            {
                return;
            }

            var game = Game;
            int sky = SkyFloor(world, _hullBox.center);
            CinematicStageScan.ClearSampler terrain = (x, y, z) => world.TryGetBlock(x, y, z, out var b) ? b.IsAir : y >= sky;
            _pathSampler = (x, y, z) => terrain(x, y, z) && (game.LandedShipBlockAt(x, y, z, out var ship, out _).IsAir || ship == own);
            _spotSampler = (x, y, z) => terrain(x, y, z) && game.LandedShipBlockAt(x, y, z, out _, out _).IsAir;
        }

        /// <summary>The bottom of the first chunk above the pad the server has not streamed: the near column reaches a few
        /// chunks over the player, and the stand-in starts higher than that — above it is open sky (a sky island or an arch
        /// over the pad streams with its column). A pad whose own chunk is unknown assumes nothing.</summary>
        private static int SkyFloor(ClientWorld world, Vector3 at)
        {
            int x = Mathf.FloorToInt(at.x), z = Mathf.FloorToInt(at.z);
            int cs = WorldConstants.ChunkSize;
            int y = Mathf.FloorToInt(at.y / cs) * cs;
            if (!world.TryGetBlock(x, y, z, out _))
            {
                return int.MaxValue;
            }

            for (int i = 0; i < 16 && world.TryGetBlock(x, y, z, out _); i++)
            {
                y += cs;
            }

            return y;
        }

        /// <summary>Places the cockpit camera in the hull's own frame: behind the helm (the ship's "cockpit" station, else a
        /// <c>ship_helm</c> cell) looking out along the nose (the engines' centroid → the helm), or — without either — at the
        /// player's eyes turned toward the nose. Also decides whether the way helm ↔ eyes is open for a glide.</summary>
        private void PlaceCockpit(LandedShipModel model)
        {
            var engines = new List<(int X, int Y, int Z)>();
            Vector3i? helm = model != null ? HelmCell(model, engines) : null;
            var eye = _eye - _restPos;
            System.Func<int, int, int, bool> occupied = (x, y, z) => model != null && model.Cells.ContainsKey(new Vector3i(x, y, z));
            CockpitPose pose;
            if (helm is { } h)
            {
                pose = TransitionCockpit.AtHelm(h.X, h.Y, h.Z, TransitionCockpit.Nose(engines, h.X + 0.5f, h.Z + 0.5f), occupied);
            }
            else
            {
                var mid = _hullBox.center - _restPos;
                pose = TransitionCockpit.AtEye(eye.x, eye.y, eye.z, TransitionCockpit.Nose(engines, mid.x, mid.z));
            }

            _cockpitLocal = new Vector3(pose.X, pose.Y, pose.Z);
            _cockpitDir = new Vector3(pose.DirX, pose.DirY, pose.DirZ);
            _cockpitAtHelm = pose.AtHelm;
            _cabinGlide = TransitionCockpit.CabinPathClear(occupied, pose.X, pose.Y, pose.Z, eye.x, eye.y, eye.z);
        }

        /// <summary>The helm's cell in the ship's frame (the stock ships' cockpit console is a station, a self-built ship's a
        /// <c>ship_helm</c> block); collects the engine cells on the way.</summary>
        private Vector3i? HelmCell(LandedShipModel model, List<(int X, int Y, int Z)> engines)
        {
            Vector3i? helm = CockpitStationCell(model);
            var content = Game.Content;
            foreach (var kv in model.Cells)
            {
                string key = content?.BlockById(kv.Value)?.Key;
                if (ShipExhausts.IsEngineBlock(key))
                {
                    engines.Add((kv.Key.X, kv.Key.Y, kv.Key.Z));
                }
                else if (!helm.HasValue && key == HelmBlockKey)
                {
                    helm = kv.Key;
                }
            }

            return helm;
        }

        /// <summary>The own ship's "cockpit" station as a cell of <paramref name="model"/> (null when the list holds none
        /// that lands on one of its cells).</summary>
        private Vector3i? CockpitStationCell(LandedShipModel model)
        {
            var stations = Game.Stations;
            if (stations == null)
            {
                return null;
            }

            foreach (var s in stations)
            {
                if (s == null || s.Type != CockpitStationType)
                {
                    continue;
                }

                var local = new Vector3i(
                    WorldConstants.WrapDeltaX(Mathf.FloorToInt(s.X) - model.Origin.X, Game.Circumference),
                    Mathf.FloorToInt(s.Y + 0.01f) - model.Origin.Y,
                    WorldConstants.WrapDeltaZ(Mathf.FloorToInt(s.Z) - model.Origin.Z, Game.Circumference));
                if (model.Cells.ContainsKey(local))
                {
                    return local;
                }
            }

            return null;
        }

        /// <summary>
        /// #3258: true while the own ship flies in a transition cinematic — <paramref name="hull"/> is the flying stand-in or
        /// adopted hull, <paramref name="rest"/> where it stands at rest. The ship's room decor (<see cref="StationDecorView"/>:
        /// the cockpit console and its holo map, the heal tank) rides along, so the cockpit camera sees it in place instead of
        /// left behind on the pad.
        /// </summary>
        public bool TryOwnShipRide(out Transform hull, out Vector3 rest)
        {
            bool riding = _stand != null && (_stage == Stage.Touchdown || (_stage == Stage.Liftoff && _liftStarted));
            hull = riding ? _stand.transform : null;
            rest = _restPos;
            return riding;
        }

        /// <summary>The cockpit camera's world pose this frame: it rides with the flying hull (sinks, rises, sways).</summary>
        private void CockpitWorldPose(out Vector3 pos, out Quaternion rot)
        {
            var t = _stand.transform;
            pos = t.TransformPoint(_cockpitLocal);
            rot = t.rotation * Quaternion.LookRotation(_cockpitDir, Vector3.up);
        }

        /// <summary>Takes the shot into the cockpit — a cut, or a glide from wherever the camera is over
        /// <paramref name="seconds"/>.</summary>
        private void GoInside(float seconds)
        {
            if (Camera == null)
            {
                return;
            }

            _fromPos = Camera.transform.position;
            _fromRot = Camera.transform.rotation;
            _mode = ShotMode.Cockpit;
            _toEye = false;
            _blendSeconds = Mathf.Max(0.0001f, seconds);
            _blend = seconds <= 0f ? 1f : 0f;
        }

        /// <summary>Holds the camera at the player's eyes (a blink's first half before it switches).</summary>
        private void HoldEye()
        {
            _toEye = true;
            _blend = 1f;
        }

        /// <summary>The per-frame safety net (every few frames, like the prologue's): true when the outside shot's spot or
        /// its line of sight to the hull is blocked now — a block placed, a chunk streamed in after the plan.</summary>
        private bool OutsideBlocked(Vector3 centre)
        {
            if (_pathSampler == null || _toEye || _blend < 1f || Time.frameCount - _netFrame < SafetyNetEveryFrames)
            {
                return false;
            }

            _netFrame = Time.frameCount;
            return !CinematicStageScan.CameraClear(_pathSampler, _spotSampler, centre.x, centre.y, centre.z, _shotPos.x, _shotPos.y, _shotPos.z);
        }

        /// <summary>True when the straight glide from the outside shot into the player's eyes crosses nothing (checked live:
        /// a skip may start it from another pose than planned).</summary>
        private bool EyeGlideClear()
        {
            if (_pathSampler == null || Camera == null)
            {
                return false;
            }

            var parent = Camera.transform.parent;
            var eye = parent != null ? parent.TransformPoint(_restLocalPos) : _eye;
            return CinematicStageScan.PathClear(_pathSampler, _shotPos.x, _shotPos.y, _shotPos.z, eye.x, eye.y, eye.z);
        }

        /// <summary>A short black blink (<see cref="BlinkSeconds"/>): <paramref name="atDark"/> runs when it is darkest — the
        /// camera jumps where a glide would cross walls.</summary>
        private void Blink(System.Action atDark)
        {
            if (_blinkFrame == null)
            {
                _blinkFrame = CinematicFrame.Create("TransitionBlink", 73); // under the transition cover (74)
            }

            _blinkTime = 0f;
            _blinkSwitch = atDark;
        }

        private void UpdateBlink(float dt)
        {
            if (_blinkTime < 0f)
            {
                return;
            }

            _blinkTime += dt;
            if (_blinkSwitch != null && _blinkTime >= BlinkSeconds * 0.5f)
            {
                var atDark = _blinkSwitch;
                _blinkSwitch = null;
                atDark();
            }

            bool done = _blinkTime >= BlinkSeconds;
            float k = Mathf.Clamp01(_blinkTime / BlinkSeconds);
            if (_blinkFrame != null)
            {
                _blinkFrame.SetFade(done ? 0f : 1f - Mathf.Abs(2f * k - 1f));
            }

            if (done)
            {
                _blinkTime = -1f;
            }
        }

        /// <summary>Forgets the shot of the cinematic that ended (a running blink fades out on its own).</summary>
        private void EndShot()
        {
            _blinkSwitch = null;
            _plan = null;
            _pathSampler = _spotSampler = null;
            _mode = ShotMode.Outside;
        }

        private void OnDestroy()
        {
            if (_blinkFrame != null)
            {
                Destroy(_blinkFrame.gameObject);
            }
        }
    }
}
