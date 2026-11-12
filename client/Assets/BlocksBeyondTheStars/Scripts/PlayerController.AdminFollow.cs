// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Admin;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The admin view's Follow (#3505), client-side: while the invisible admin (an observer) follows a player, its flight is
    /// steered each frame to a point behind and above that player's drawn avatar (<see cref="RemotePlayers.TryGetPose"/>),
    /// facing them; the position goes to the server like any observer move. Following waits for the first sighting after the
    /// jump, and ends when the player is gone from this body (<see cref="AdminFollowTracker"/>, which raises
    /// <see cref="AdminFollowLost"/>) or the admin moves by hand.
    /// </summary>
    public sealed partial class PlayerController
    {
        private const float AdminFollowEase = 4f;           // 1/s: how quickly the chase point and the look are caught up
        private const float AdminFollowSnapDistance = 48f;  // further away than this (the first sighting) the observer snaps
        private const float AdminFollowHeadHeight = 1.6f;   // where on the followed body the view aims

        private readonly AdminFollowTracker _adminFollow = new AdminFollowTracker();

        /// <summary>Following ended because the player left this body (or never showed up); the argument is their name.</summary>
        public event System.Action<string> AdminFollowLost;

        /// <summary>The followed player's id, or empty.</summary>
        public string AdminFollowTargetId => _adminFollow.TargetId;

        /// <summary>The followed player's name, or empty.</summary>
        public string AdminFollowTargetName => _adminFollow.TargetName;

        /// <summary>Starts following (the panel has sent the jump to the player already).</summary>
        public void StartAdminFollow(string playerId, string playerName)
            => _adminFollow.Start(playerId, playerName, Time.realtimeSinceStartupAsDouble);

        /// <summary>Stops following.</summary>
        public void StopAdminFollow() => _adminFollow.Stop();

        /// <summary>The observer's motion this frame while following; false when nothing is followed (or following just
        /// ended) and the normal observer flight takes the frame.</summary>
        private bool StepAdminFollow(float h, float v)
        {
            if (!_adminFollow.Active)
            {
                return false;
            }

            if (Mathf.Abs(h) + Mathf.Abs(v) > 0.1f || InputMap.JumpHeld() || InputMap.CrouchHeld())
            {
                _adminFollow.Stop(); // any manual movement takes the flight back
                return false;
            }

            Vector3 feet = default;
            float yaw = 0f;
            bool seen = Remotes != null && Remotes.TryGetPose(_adminFollow.TargetId, out feet, out yaw);
            var step = _adminFollow.Observe(Time.realtimeSinceStartupAsDouble, seen);
            if (step == AdminFollowStep.Lost)
            {
                string name = _adminFollow.TargetName;
                _adminFollow.Stop();
                AdminFollowLost?.Invoke(name);
                return false;
            }

            if (!seen)
            {
                SpectatorMove(0f, 0f); // the jump is on its way, or one presence packet is late: hold still
                return true;
            }

            ChaseAdminTarget(feet, yaw);
            return true;
        }

        /// <summary>Eases the observer towards the chase point behind the followed player and turns the view onto them.</summary>
        private void ChaseAdminTarget(Vector3 feet, float targetYaw)
        {
            if (_controller.enabled)
            {
                _controller.enabled = false; // still the observer's noclip flight
            }

            _verticalVelocity = 0f;
            _moving = false;
            var (cx, cy, cz) = AdminFollowTracker.ChasePoint(feet.x, feet.y, feet.z, targetYaw);
            var chase = new Vector3(cx, cy, cz);
            float k = 1f - Mathf.Exp(-AdminFollowEase * Time.deltaTime);
            Vector3 pos = transform.position;
            transform.position = (chase - pos).sqrMagnitude > AdminFollowSnapDistance * AdminFollowSnapDistance
                ? chase
                : Vector3.Lerp(pos, chase, k);

            Vector3 eye = Camera != null ? Camera.transform.position : transform.position + (Vector3.up * AdminFollowHeadHeight);
            var (lookYaw, lookPitch) = AdminFollowTracker.LookAt(eye.x, eye.y, eye.z, feet.x, feet.y + AdminFollowHeadHeight, feet.z);
            transform.eulerAngles = new Vector3(0f, Mathf.LerpAngle(transform.eulerAngles.y, lookYaw, k), 0f);
            _pitch = Mathf.Clamp(Mathf.Lerp(_pitch, lookPitch, k), -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }
    }
}
