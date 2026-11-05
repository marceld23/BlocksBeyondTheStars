// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The own lift-off on the surface (#3141): the camera glides out of the cockpit to a spot beside the ship; the engines
    /// sputter and catch, the dust starts to swirl; the ship rises heavily, hovers, lifts its nose; then full burn — the
    /// vapour cone and its boom — and it climbs away into the sky until it punches into the clouds (or the glare of an
    /// airless world), where the flight view takes over. The server removed the parked hull the moment the flight opened;
    /// <see cref="LandedShipView"/> hands that hull here (<see cref="AdoptDepartingShip"/>) instead of dropping it, so the
    /// very ship the player stood in is the one that flies.
    /// </summary>
    public sealed partial class TransitionDirector
    {
        private const float IgniteAt = 0.35f;
        private const float LiftAt = 1.6f;
        private const float ClimbAt = 2.8f;
        private const float BoomAt = 3.3f;
        private const float CloudsAt = 4.1f;
        private const float WaitForHull = 0.35f;

        private GameObject _adopted;
        private LandedShipModel _adoptedModel;
        private float _adoptedAt;
        private bool _liftStarted;
        private bool _ignited, _lifted, _climbing, _boomed;
        private Vector3 _nose = Vector3.forward;

        /// <summary>
        /// <see cref="LandedShipView"/> hands over the local player's own parked hull when the server removed it (a launch,
        /// or a ship swap): its colliders go, its meshes stay. A launch claims it within a moment; a hull nobody claims is
        /// dropped after two seconds.
        /// </summary>
        public void AdoptDepartingShip(GameObject root, LandedShipModel model)
        {
            if (root == null)
            {
                return;
            }

            if (_adopted != null && _adopted != root && _stand != _adopted)
            {
                ShipCinematicFx.DestroyWithMeshes(_adopted);
            }

            ShipCinematicFx.StripColliders(root);

            // #3159: the hull carries no door of its own (its doors are DoorView entities, going with the ship) — it lifts off
            // with copies of them, and with its hatch shut by the plan when they are already gone.
            var doorBox = ShipCinematicFx.RenderBounds(root);
            doorBox.Expand(1.5f);
            if (Game?.Views.Doors?.CloneDoorsWithin(doorBox, root.transform) == null && model != null)
            {
                ShipHatchDoor.Build(root.transform, ShipHatchDoor.PlanFor(model), Vector3.zero);
            }

            ShipCinematicFx.SetRenderersEnabled(root, false); // unseen until a lift-off claims it (a world switch never shows it)
            root.transform.SetParent(null, true);
            _adopted = root;
            _adoptedModel = model;
            _adoptedAt = Time.time;
        }

        private bool BeginPlanetDeparture()
        {
            _liftStarted = false;
            _emergence = default;
            HoldScreen(true);
            SetStage(Stage.Liftoff);
            return true;
        }

        private void UpdateLiftoff(float dt)
        {
            if (!Game.InSpace)
            {
                EndCinematic(); // the flight closed again before the view opened — nothing to show
                Cover.Open(0.5f, 1f);
                SetStage(Stage.Idle);
                return;
            }

            if (!_liftStarted)
            {
                if (_adopted != null && Camera != null)
                {
                    StartLiftoff();
                }
                else if (_stageTime > WaitForHull)
                {
                    CloseDepartureCover(0.6f); // no own hull here (an observer, a fleet ship elsewhere): just the clouds
                }

                return;
            }

            if (SkipRequested() && _stageTime < CloudsAt)
            {
                _stageTime = CloudsAt;
            }

            LiftoffBeats();
            HideRealHatch(true);
            FlyHull(dt);
            AimLiftoff();
            if (_stageTime >= CloudsAt)
            {
                CloseDepartureCover(0.55f, ownStage: false); // the hull climbs on into the closing clouds
            }

            if (Cover.Closing && Cover.Shut)
            {
                EndCinematic();
                SetStage(Stage.Handoff);
            }
        }

        private void StartLiftoff()
        {
            _liftStarted = true;
            _stand = _adopted;
            _hiddenHull = null;
            _adopted = null;
            ShipCinematicFx.SetRenderersEnabled(_stand, true);
            _hullBox = ShipCinematicFx.RenderBounds(_stand);
            HideRealHatch(true); // #3159: a hatch door the server has not taken away yet stays out of the shot
            _restPos = _stand.transform.position;
            _exhausts = _adoptedModel != null ? ShipMeshBuilder.ExhaustPoints(Game.Content, _adoptedModel.Cells, Vector3.zero) : null;
            _dust = _adoptedModel != null ? ShipCinematicFx.GroundColor(Game, CellBelow(_adoptedModel)) : new Color(0.62f, 0.57f, 0.5f);
            _light = FxLights.Attach(_stand.transform, _hullBox.center - _restPos + Vector3.down * (_hullBox.extents.y + 1.5f),
                new Color(1f, 0.62f, 0.3f), 0f, 18f, flicker: true);
            _ignited = _lifted = _climbing = _boomed = false;
            _nose = Vector3.forward;
            _spot = ShotSpot(_hullBox, Mathf.Max(11f, _hullBox.extents.magnitude * 1.8f), 1.8f);
            TakeCamera();
            AimShot(_spot, _hullBox.center, 0.9f);
            _stageTime = 0f;
        }

        /// <summary>The one-off moments of the lift-off: ignition, the lift, the climb, the boom.</summary>
        private void LiftoffBeats()
        {
            var ground = new Vector3(_hullBox.center.x, _hullBox.min.y, _hullBox.center.z);
            if (!_ignited && _stageTime >= IgniteAt)
            {
                _ignited = true;
                ClientAudio.Instance?.Cue("engine_ignite", 0.95f);
            }

            if (!_lifted && _stageTime >= LiftAt)
            {
                _lifted = true;
                ShipCinematicFx.DustBurst(ground, _hullBox.extents.magnitude + 1f, _dust);
                FxCamera.AddTrauma(0.22f);
                PadRumble.Trauma(0.4f);
            }

            if (!_climbing && _stageTime >= ClimbAt)
            {
                _climbing = true;
                ClientAudio.Instance?.Cue("ship_launch", 0.9f);
                FxCamera.FovPunch(6f);
                _emergence.RoarPlayed = true;
            }

            if (!_boomed && _stageTime >= BoomAt)
            {
                _boomed = true;
                var centre = _stand.transform.position + (_hullBox.center - _restPos);
                ShipCinematicFx.VapourCone(centre, (_nose + Vector3.up * 2f).normalized, _hullBox.extents.magnitude);
                if (!(ClientAudio.Instance?.WorldAirless ?? false))
                {
                    ClientAudio.Instance?.Cue("sonic_boom", 0.9f); // #3241: no air, no boom
                }

                FxCamera.AddTrauma(0.3f);
            }
        }

        /// <summary>Moves the hull: still on the pad while the engines catch, a heavy rise and a hover with the nose coming
        /// up, then an accelerating climb along the nose.</summary>
        private void FlyHull(float dt)
        {
            float t = _stageTime;
            float rise = t < LiftAt ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - LiftAt) / (ClimbAt - LiftAt))) * 5f;
            float climbT = Mathf.Max(0f, t - ClimbAt);
            float pitch = Mathf.SmoothStep(0f, 16f, Mathf.Clamp01((t - LiftAt - 0.4f) / 1.2f));
            _nose = Quaternion.Euler(-pitch, 0f, 0f) * Vector3.forward;
            var climb = (_nose * 0.6f + Vector3.up).normalized * (climbT * climbT * 14f);
            float wobble = t > LiftAt && t < ClimbAt + 0.5f ? 0.8f : 0.2f;
            var shake = new Vector3(Mathf.Sin(t * 13f), 0f, Mathf.Cos(t * 11f)) * 0.05f * wobble;
            _stand.transform.SetPositionAndRotation(_restPos + Vector3.up * rise + climb + shake,
                Quaternion.Euler(-pitch, 0f, Mathf.Sin(t * 2.3f) * 1.5f * wobble));

            float power = t < IgniteAt ? 0f : t < LiftAt ? Mathf.Lerp(0.2f, 0.75f, (t - IgniteAt) / (LiftAt - IgniteAt)) : 1f;
            var fallback = _hullBox.center - _restPos + Vector3.down * _hullBox.extents.y;
            var push = t < ClimbAt ? Vector3.down : (-_nose * 0.5f + Vector3.down).normalized;
            ShipCinematicFx.EngineFire(_stand.transform, _exhausts, fallback, push, power);
            FxLights.Set(_light, new Color(1f, 0.62f, 0.3f), 3.4f * power);
            var ground = new Vector3(_hullBox.center.x, _hullBox.min.y, _hullBox.center.z);
            float height = rise + climb.y;
            ShipCinematicFx.DustSwirl(ground, _hullBox.extents.magnitude + 3f, power * Mathf.Clamp01(1f - height / 18f), _dust);
            if (power > 0.1f)
            {
                FxCamera.AddTrauma(0.1f * power * dt * Mathf.Clamp01(1f - height / 60f) * 4f, rumble: false);
            }
        }

        /// <summary>The camera keeps the hull in frame: low beside it while it lifts, tilting up and rising a little after
        /// it as it climbs away.</summary>
        private void AimLiftoff()
        {
            var centre = _stand.transform.position + (_hullBox.center - _restPos);
            float follow = Mathf.Clamp01((_stageTime - ClimbAt) / 1.5f);
            var spot = _spot + Vector3.up * (Mathf.Max(0f, centre.y - _hullBox.center.y) * 0.25f * follow);
            MoveShot(spot, centre);
        }

        private void DropUnclaimedHull()
        {
            if (_adopted != null && _stage != Stage.Liftoff && Time.time - _adoptedAt > 2f)
            {
                ShipCinematicFx.DestroyWithMeshes(_adopted);
                _adopted = null;
                _adoptedModel = null;
            }
        }
    }
}
