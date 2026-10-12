// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Flies the cutscene director's camera (#2468): the shot <see cref="CutsceneStaging"/> picked — an orbit (ping-pong
    /// across the open arc), a crane rise, a static framing with a slow push-in, or a look up into the sky; since #2477 also
    /// the moves (a <c>dolly</c> eased along its straight line, a <c>pan</c> turning in place and back, a <c>flyover</c> along
    /// its high arc — <see cref="CutsceneMoves"/>) and the synthetic stages (<c>space_vista</c>, <c>diorama</c>), which it
    /// shows through <see cref="CutsceneStage"/> with that stage's own camera instead of moving the player's. It writes the
    /// player camera in <c>LateUpdate</c> at execution order 211, after every gameplay writer and the prologue's rig (210),
    /// and only while it holds the camera; <see cref="GameBootstrap.CinematicCameraActive"/> freezes the on-foot controls
    /// meanwhile, and the moment it lets go <see cref="PlayerController"/> re-asserts the eye pose. The prologue's
    /// per-frame safety net is kept: a pose whose line of sight a late-streamed chunk blocks is not used — the last clear
    /// pose holds. "Camera motion" off (<see cref="FxKit.CameraMotion"/>) turns the moves into still framings. Nothing in
    /// the per-frame path allocates.
    /// </summary>
    [DefaultExecutionOrder(211)]
    public sealed class CutsceneCameraRig : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float OrbitDegPerSecond = 8f;
        private const float CraneDegPerSecond = 3f;
        private const float SkyDegPerSecond = 2f;
        private const float SkyPitch = -32f; // looking up (Unity pitch: negative = up)

        private StagedCamera _shot;
        private CinematicStageScan.ClearSampler _path;
        private CinematicStageScan.ClearSampler _spot;
        private bool _active;
        private float _angle, _radius, _height, _orbitDir = 1f;
        private float _elapsed, _duration;
        private Vector3 _skyEye;
        private Vector3 _lastPos;
        private Quaternion _lastRot;
        private bool _hasLastPose;
        private Viewmodel _viewmodel;
        private CutsceneStage _stage;
        private bool _synthetic;

        /// <summary>True while the rig holds the player camera (a world shot or a synthetic stage; the chart and the player's
        /// view do not).</summary>
        public bool Holding => _active;

        /// <summary>Takes the camera for <paramref name="shot"/> (or lets it go for a shot that needs none). The samplers are
        /// the ones the staging used, for the per-frame safety net; <paramref name="duration"/> is the shot's length, over
        /// which a move travels (#2477).</summary>
        public void Apply(StagedCamera shot, CinematicStageScan.ClearSampler path, CinematicStageScan.ClearSampler spot, float duration = 0f)
        {
            bool moves = shot != null && (shot.Kind == StagedCameraKind.Orbit || shot.Kind == StagedCameraKind.Crane
                                          || shot.Kind == StagedCameraKind.Static || shot.Kind == StagedCameraKind.Sky
                                          || shot.Moves);
            if (!moves || Camera == null)
            {
                Release();
                return;
            }

            LeaveSynthetic(); // a world shot after a synthetic one: the world camera draws again
            _shot = shot;
            _path = path;
            _spot = spot;
            _angle = shot.StartAngle;
            _radius = shot.StartRadius;
            _height = shot.StartHeight;
            _orbitDir = 1f;
            _elapsed = 0f;
            _duration = duration;
            _hasLastPose = false;
            _skyEye = Camera.transform.position;
            Take();
        }

        /// <summary>#2477: shows a synthetic stage (<paramref name="set"/>) for <paramref name="shot"/> — its own camera draws
        /// over the world's, which keeps its place. <paramref name="memory"/>: the shot's memory/flashback grade. False when
        /// the stage cannot be built here; nothing changes then and the director stages the shot without it.</summary>
        public bool ApplySynthetic(StagedCamera shot, SyntheticSet set, bool memory, float duration)
        {
            if (shot == null || Camera == null)
            {
                return false;
            }

            _stage ??= new CutsceneStage(Game, Camera);
            if (!_stage.Show(set, memory))
            {
                return false;
            }

            _synthetic = true;
            _shot = shot;
            _path = null;
            _spot = null;
            _elapsed = 0f;
            _duration = duration;
            _hasLastPose = false;
            Take();
            return true;
        }

        /// <summary>Gives the camera back (the shot ended, the scene ended, the multiplayer cap ran out).</summary>
        public void Release()
        {
            LeaveSynthetic();
            if (!_active)
            {
                return;
            }

            _active = false;
            _shot = null;
            _path = null;
            _spot = null;
            if (Game != null)
            {
                Game.CinematicCameraActive = false;
            }

            if (_viewmodel != null)
            {
                var pc = Camera != null ? Camera.GetComponentInParent<PlayerController>() : null;
                _viewmodel.SetVisible(pc == null || !pc.ThirdPerson);
                _viewmodel = null;
            }
        }

        private void Take()
        {
            if (!_active)
            {
                _viewmodel = Camera.GetComponent<Viewmodel>();
                _viewmodel?.SetVisible(false); // a drill floating through an exterior shot breaks it
            }

            _active = true;
            if (Game != null)
            {
                Game.CinematicCameraActive = true;
            }
        }

        private void LeaveSynthetic()
        {
            if (_synthetic)
            {
                _synthetic = false;
                _stage?.Hide();
            }
        }

        private void LateUpdate()
        {
            if (!_active || _shot == null || Camera == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool motion = FxKit.CameraMotion;
            _elapsed += dt;
            if (_synthetic)
            {
                // The stage's own camera; the world camera stays where it is. Still framing: the middle of the path.
                _stage.Tick(motion ? CutsceneMoves.Eased(_elapsed, _duration) : 0.5f, _elapsed);
                return;
            }

            if (_shot.Moves)
            {
                FlyMove(motion);
                return;
            }

            if (_shot.Kind == StagedCameraKind.Sky)
            {
                if (motion)
                {
                    _angle += SkyDegPerSecond * dt;
                }

                Camera.transform.SetPositionAndRotation(_skyEye, Quaternion.Euler(SkyPitch, _angle, 0f));
                return;
            }

            switch (_shot.Kind)
            {
                case StagedCameraKind.Orbit:
                    if (motion)
                    {
                        AdvanceOrbit(dt);
                    }

                    break;
                case StagedCameraKind.Crane:
                    if (motion)
                    {
                        _angle += CraneDegPerSecond * dt;
                    }

                    break;
            }

            float k = motion ? 1f - Mathf.Exp(-0.9f * dt) : 1f; // still framings jump straight to the settled pose
            _radius = Mathf.Lerp(_radius, _shot.Radius, k);
            _height = Mathf.Lerp(_height, _shot.Height, k);

            var center = new Vector3(_shot.CenterX, _shot.CenterY, _shot.CenterZ);
            var look = center + new Vector3(0f, _shot.LookHeight, 0f);
            float rad = _angle * Mathf.Deg2Rad;
            var pos = center + new Vector3(Mathf.Sin(rad) * _radius, _height, Mathf.Cos(rad) * _radius);
            if (!Clear(look, pos))
            {
                if (_hasLastPose)
                {
                    Camera.transform.SetPositionAndRotation(_lastPos, _lastRot); // never into a late-streamed hill
                }

                return;
            }

            var rot = Quaternion.LookRotation(look - pos);
            Camera.transform.SetPositionAndRotation(pos, rot);
            _lastPos = pos;
            _lastRot = rot;
            _hasLastPose = true;
        }

        /// <summary>#2477: a dolly or a flyover travels its path looking at the anchor (the same safety net as the orbit); a
        /// pan turns in place at its pivot (checked at staging — a turn cannot run into anything).</summary>
        private void FlyMove(bool motion)
        {
            float k = CutsceneMoves.Progress(_shot, _elapsed, _duration, motion);
            CutsceneMoves.Position(_shot, k, out float x, out float y, out float z);
            var pos = new Vector3(x, y, z);
            Quaternion rot;
            if (_shot.Kind == StagedCameraKind.Pan)
            {
                rot = Quaternion.Euler(_shot.Pitch, CutsceneMoves.Yaw(_shot, k), 0f);
            }
            else
            {
                var look = new Vector3(_shot.CenterX, _shot.CenterY + _shot.LookHeight, _shot.CenterZ);
                if (!Clear(look, pos))
                {
                    if (_hasLastPose)
                    {
                        Camera.transform.SetPositionAndRotation(_lastPos, _lastRot); // never into a late-streamed hill
                    }

                    return;
                }

                rot = Quaternion.LookRotation(look - pos);
            }

            Camera.transform.SetPositionAndRotation(pos, rot);
            _lastPos = pos;
            _lastRot = rot;
            _hasLastPose = true;
        }

        private void AdvanceOrbit(float dt)
        {
            float step = OrbitDegPerSecond * dt;
            if (!_shot.ArcLimited)
            {
                _angle += step;
                return;
            }

            _angle += step * _orbitDir;
            float half = Mathf.Max(CutsceneStaging.ArcEdgeMargin, _shot.ArcHalf - CutsceneStaging.ArcEdgeMargin);
            float rel = Mathf.DeltaAngle(_shot.ArcCenter, _angle);
            if (rel > half)
            {
                _orbitDir = -1f;
            }
            else if (rel < -half)
            {
                _orbitDir = 1f;
            }
        }

        private bool Clear(Vector3 look, Vector3 pos)
            => _path == null || _spot == null
               || CinematicStageScan.CameraClear(_path, _spot, look.x, look.y, look.z, pos.x, pos.y, pos.z);

        private void OnDestroy()
        {
            Release();
            _stage?.Dispose();
            _stage = null;
        }
    }
}
