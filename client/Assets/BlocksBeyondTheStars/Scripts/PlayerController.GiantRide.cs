// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Riding on a giant's back (#3467). The player's capsule collides with the giants' bodies (layer
    /// <see cref="CreatureView.GiantLayer"/>), so a player can land on a sky giant — but the CharacterController has no
    /// moving-platform support, and the body slid out from under the feet. Now, every frame before the player's own move:
    /// <list type="number">
    /// <item>the body part stood on last frame carries the player by its change of pose — its position and its turn about +Y
    /// (<see cref="GiantRideRules.Carry"/>; pitch and roll left out, the rider stays upright), the player's view turning
    /// along;</item>
    /// <item>a short sphere cast under the feet against the giants' layer finds the part stood on now, and the poses of every
    /// part of that giant are kept for the next frame (walking from one segment to the next carries on without a hitch);</item>
    /// <item>leaving the giant in mid-air keeps part of its speed (<see cref="GiantRideRules.LeaveMomentum"/>); the colossus,
    /// the arachnid and the Soundrak shake a rider off after a few seconds (<see cref="GiantRideRules.ShakesOff"/>).</item>
    /// </list>
    /// The trains are ridden differently (#2113: the body is parented to the wagon and the wire speaks the wagon's frame):
    /// a giant's segments are re-posed and rescaled every frame, and a child of a scaled transform scales with it. Nothing goes
    /// on the wire — the server sees world positions as when walking; other players may see a rider slip a little.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>How far below the feet a giant's body still counts as stood on.</summary>
        private const float RideProbe = 0.3f;

        /// <summary>After a shake-off the giant cannot be ridden for this long — the rider is in the air, being thrown.</summary>
        private const float RideCooldownSeconds = 1.5f;

        private string _rideGiant;             // the giant stood on, null when none
        private Transform _ridePart;           // the body part stood on
        private float _rideSeconds;            // how long on this giant
        private float _rideCooldownUntil;
        private Vector3 _rideVelocity;          // the carry per second, smoothed — what leaving in mid-air keeps

        /// <summary>The giant left last, when, and how long it had been ridden — a hop on its back is no new ride (the
        /// shake-off's clock runs on: jumping must not keep a rider on a colossus).</summary>
        private string _rideLastGiant;
        private float _rideLastEnd = -10f;
        private float _rideLastSeconds;
        private const float RideResumeSeconds = 2f;

        /// <summary>The poses (position, yaw) of the giant's body parts at the end of the last frame.</summary>
        private readonly Dictionary<Transform, (Vector3 Pos, float Yaw)> _ridePoses = new Dictionary<Transform, (Vector3, float)>();

        private static int GiantMask => 1 << CreatureView.GiantLayer;

        /// <summary>The giant ride's step (call once per frame on foot, before the player's own move).</summary>
        private void StepGiantRide()
        {
            if (_controller == null || !_controller.enabled || _flying || _climbing || Time.time < _rideCooldownUntil)
            {
                EndGiantRide(keepMomentum: false);
                return;
            }

            if (_ridePart != null)
            {
                Physics.SyncTransforms(); // the giant was re-posed this frame (CreatureView runs first); autoSyncTransforms is off
                CarryOnGiant();
            }

            var part = GiantPartUnderFeet(out string giant);
            if (part == null)
            {
                EndGiantRide(keepMomentum: !_controller.isGrounded);
                return;
            }

            if (giant != _rideGiant)
            {
                bool resumed = giant == _rideLastGiant && Time.time - _rideLastEnd < RideResumeSeconds;
                _rideSeconds = resumed ? _rideLastSeconds : 0f;
                _rideVelocity = Vector3.zero;
            }

            _rideGiant = giant;
            _ridePart = part;
            _rideSeconds += Time.deltaTime;
            RememberGiantPoses(giant);
            if (_rideSeconds >= GiantRideRules.ShakeOffSeconds && GiantRideRules.ShakesOff(CreatureView.GiantPlanFor(giant, out var root)))
            {
                ShakeOffGiant(root);
            }
        }

        /// <summary>Moves the player with the part stood on (its pose last frame → now) and turns the view along.</summary>
        private void CarryOnGiant()
        {
            if (!_ridePoses.TryGetValue(_ridePart, out var before))
            {
                return;
            }

            var p = transform.position;
            var now = _ridePart.position;
            var (delta, turn) = GiantRideRules.Carry(new Vector3f(p.x, p.y, p.z), new Vector3f(before.Pos.x, before.Pos.y, before.Pos.z),
                before.Yaw, new Vector3f(now.x, now.y, now.z), YawOf(_ridePart));
            var move = new Vector3(delta.X, delta.Y, delta.Z);
            if (move.sqrMagnitude > 1e-8f)
            {
                _controller.Move(move); // collides on the way: a cliff or a tree the giant brushes past stops the rider
            }

            if (Mathf.Abs(turn) > 1e-4f)
            {
                transform.Rotate(0f, turn, 0f); // the view turns with the giant
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            _rideVelocity = Vector3.Lerp(_rideVelocity, move / dt, 1f - Mathf.Exp(-8f * dt));
        }

        /// <summary>The giant body part right under the feet (a short sphere cast against the giants' layer only), or null.</summary>
        private Transform GiantPartUnderFeet(out string giant)
        {
            giant = null;
            float r = _controller.radius * 0.9f;
            var bottom = transform.TransformPoint(_controller.center) - (Vector3.up * ((_controller.height * 0.5f) - _controller.radius));
            var origin = bottom + (Vector3.up * 0.1f);
            if (!Physics.SphereCast(origin, r, Vector3.down, out var hit, RideProbe + 0.1f + (_controller.radius - r), GiantMask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < 0.5f)
            {
                return null;
            }

            giant = CreatureView.GiantIdFor(hit.collider);
            return giant != null ? hit.collider.transform : null;
        }

        /// <summary>Keeps every body part's pose of the ridden giant for the next frame's carry.</summary>
        private void RememberGiantPoses(string giant)
        {
            _ridePoses.Clear();
            foreach (var col in CreatureView.GiantCollidersFor(giant))
            {
                if (col != null)
                {
                    var t = col.transform;
                    _ridePoses[t] = (t.position, YawOf(t));
                }
            }
        }

        /// <summary>The ride ends: off the giant, or no riding now. Leaving it in mid-air keeps part of its speed.</summary>
        private void EndGiantRide(bool keepMomentum)
        {
            if (_rideGiant == null)
            {
                return;
            }

            if (keepMomentum)
            {
                var push = GiantRideRules.LeaveMomentum(new Vector3f(_rideVelocity.x, _rideVelocity.y, _rideVelocity.z));
                if (push.X != 0f || push.Z != 0f)
                {
                    ApplyKnock(new Vector3(push.X, 0f, push.Z));
                }
            }

            _rideLastGiant = _rideGiant;
            _rideLastEnd = Time.time;
            _rideLastSeconds = _rideSeconds;
            _rideGiant = null;
            _ridePart = null;
            _rideSeconds = 0f;
            _rideVelocity = Vector3.zero;
            _ridePoses.Clear();
        }

        /// <summary>A colossus, an arachnid or a Soundrak tolerates no rider: a push away from its body with a hop, the camera shakes.</summary>
        private void ShakeOffGiant(Transform root)
        {
            var away = root != null ? transform.position - root.position : Vector3.zero;
            var impulse = GiantRideRules.ShakeOffImpulse(new Vector3f(away.x, 0f, away.z));
            EndGiantRide(keepMomentum: false);
            ApplyKnock(new Vector3(impulse.X, impulse.Y, impulse.Z));
            AddCameraShake(0.5f);
            _rideCooldownUntil = Time.time + RideCooldownSeconds;
        }

        private static float YawOf(Transform t)
        {
            var f = t.forward;
            return GiantRideRules.YawOf(new Vector3f(f.x, f.y, f.z));
        }
    }
}
