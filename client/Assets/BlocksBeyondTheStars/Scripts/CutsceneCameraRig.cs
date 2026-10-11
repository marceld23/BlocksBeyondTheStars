// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Flies the cutscene director's camera (#2468): the shot <see cref="CutsceneStaging"/> picked — an orbit (ping-pong
    /// across the open arc), a crane rise, a static framing with a slow push-in, or a look up into the sky. It writes the
    /// player camera in <c>LateUpdate</c> at execution order 211, after every gameplay writer and the prologue's rig (210),
    /// and only while it holds the camera; <see cref="GameBootstrap.CinematicCameraActive"/> freezes the on-foot controls
    /// meanwhile, and the moment it lets go <see cref="PlayerController"/> re-asserts the eye pose. The prologue's
    /// per-frame safety net is kept: a pose whose line of sight a late-streamed chunk blocks is not used — the last clear
    /// pose holds. "Camera motion" off (<see cref="FxKit.CameraMotion"/>) turns the moves into still framings.
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
        private Vector3 _skyEye;
        private Vector3 _lastPos;
        private Quaternion _lastRot;
        private bool _hasLastPose;
        private Viewmodel _viewmodel;

        /// <summary>True while the rig holds the player camera (a world shot; the chart and the player's view do not).</summary>
        public bool Holding => _active;

        /// <summary>Takes the camera for <paramref name="shot"/> (or lets it go for a shot that needs none). The samplers are
        /// the ones the staging used, for the per-frame safety net.</summary>
        public void Apply(StagedCamera shot, CinematicStageScan.ClearSampler path, CinematicStageScan.ClearSampler spot)
        {
            bool moves = shot != null && (shot.Kind == StagedCameraKind.Orbit || shot.Kind == StagedCameraKind.Crane
                                          || shot.Kind == StagedCameraKind.Static || shot.Kind == StagedCameraKind.Sky);
            if (!moves || Camera == null)
            {
                Release();
                return;
            }

            _shot = shot;
            _path = path;
            _spot = spot;
            _angle = shot.StartAngle;
            _radius = shot.StartRadius;
            _height = shot.StartHeight;
            _orbitDir = 1f;
            _hasLastPose = false;
            _skyEye = Camera.transform.position;
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

        /// <summary>Gives the camera back (the shot ended, the scene ended, the multiplayer cap ran out).</summary>
        public void Release()
        {
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

        private void LateUpdate()
        {
            if (!_active || _shot == null || Camera == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool motion = FxKit.CameraMotion;
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

        private void OnDestroy() => Release();
    }
}
