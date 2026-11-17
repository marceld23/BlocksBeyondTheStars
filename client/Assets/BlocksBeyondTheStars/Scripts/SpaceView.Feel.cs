// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship at the helm has mass (<see cref="FlightFeel"/>, "arcade"): the thrust builds up, a let-go stick lets the
    /// ship coast and then it stands, the hull leans into a turn, and the chase camera trails a little. Presentation
    /// only — the heading the ship flies on is the unrolled one, the keep-out, hull and rock rules resolve the position
    /// exactly as before, and what is blocked is taken out of the velocity so the ship never keeps pushing into a wall.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>The chase camera never trails further behind its rigid place than this (flight units).</summary>
        private const float ChaseLagLimit = 4f;

        /// <summary>Extra lean while strafing, degrees at full sideways thrust.</summary>
        private const float StrafeBankDegrees = 8f;

        private Vector3 _flightVel;          // the ship's velocity in the flight space, units per second
        private float _bank;                 // the hull's lean, degrees about its nose (Unity's z: negative = right wing down)
        private float _bankPrevYaw;
        private int _flightFeelFrame = -10;  // the last frame the helm steered (a gap means: start from rest)
        private Vector3 _chasePos;
        private bool _chaseValid;

        /// <summary>The step the ship flies this frame for the helm's input: toward the wanted velocity at the spool
        /// rate, toward a stop at the coast rate. A frame without the helm (a menu, a transition) starts from rest.</summary>
        private Vector3 FlightStep(Quaternion heading, float fwd, float strafe)
        {
            float dt = Time.deltaTime;
            if (Time.frameCount - _flightFeelFrame > 1)
            {
                _flightVel = Vector3.zero;
                _bankPrevYaw = _yaw;
            }

            _flightFeelFrame = Time.frameCount;
            float top = MoveSpeed * _shipSpeedMul * BoostFactor; // #3754: faster while the boost burns
            var want = heading * ((Vector3.forward * fwd) + (Vector3.right * strafe)) * top;
            bool thrusting = Mathf.Abs(fwd) + Mathf.Abs(strafe) > 0.05f;
            _flightVel = Vector3.MoveTowards(_flightVel, want, top * FlightFeel.SpeedStep(dt, thrusting));

            // The lean: into the turn, plus a little into a sideways thrust.
            float yawRate = dt > 0f ? Mathf.DeltaAngle(_bankPrevYaw, _yaw) / dt : 0f;
            _bankPrevYaw = _yaw;
            float wantBank = -(FlightFeel.BankTarget(yawRate) + (strafe * StrafeBankDegrees));
            _bank = Mathf.Lerp(_bank, wantBank, FlightFeel.Follow(FlightFeel.BankFollowRate, dt));
            _thrustVel = _flightVel;
            return (_flightVel * dt) + RollSlide(heading); // #3753: a barrel roll slides the ship sideways
        }

        /// <summary>After the keep-out, hull and rock rules moved the wanted position: what they blocked is no longer
        /// part of the velocity, so the ship slides along a wall instead of pressing into it for a second.</summary>
        private void SettleFlightVelocity(Vector3 from, Vector3 to)
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                _flightVel = (to - from) / dt;
                SettleRollSlide(dt);
            }
        }

        /// <summary>The helm's heading with the hull's lean on top — what the ship model shows.</summary>
        private Quaternion BankedRotation(Quaternion heading) => heading * Quaternion.Euler(0f, 0f, _bank - _roll.Angle);

        /// <summary>The ship stops dead (the autopilot parks it, a transition takes it).</summary>
        private void StopFlightDrift() => _flightVel = Vector3.zero;

        /// <summary>The heading without the lean, for the camera: read back from the ship so every state that owns the
        /// rotation (a hold, a menu) is covered. While the helm is not steering, the lean eases out.</summary>
        private Quaternion CameraHeading()
        {
            var e = _ship.transform.localEulerAngles;
            if (Time.frameCount - _flightFeelFrame > 1 && Mathf.Abs(_bank) > 0.01f)
            {
                _bank = Mathf.MoveTowards(_bank, 0f, 60f * Time.deltaTime);
                _ship.transform.localRotation = Quaternion.Euler(e.x, e.y, _bank);
            }

            return Quaternion.Euler(e.x, e.y, 0f);
        }

        /// <summary>The chase camera's place: it follows its rigid spot softly, so speeding up pulls the ship ahead a
        /// little and a turn swings the view after it. Rigid with "Camera motion" off; snaps after a jump in position.</summary>
        private Vector3 ChaseCamera(Vector3 rigid)
        {
            if (!FxKit.CameraMotion || !_chaseValid || (rigid - _chasePos).sqrMagnitude > 40f * 40f)
            {
                _chasePos = rigid;
                _chaseValid = true;
                return rigid;
            }

            _chasePos = Vector3.Lerp(_chasePos, rigid, FlightFeel.Follow(FlightFeel.CameraFollowRate, Time.deltaTime));
            var lag = _chasePos - rigid;
            if (lag.sqrMagnitude > ChaseLagLimit * ChaseLagLimit)
            {
                _chasePos = rigid + (lag.normalized * ChaseLagLimit);
            }

            return _chasePos;
        }
    }
}
