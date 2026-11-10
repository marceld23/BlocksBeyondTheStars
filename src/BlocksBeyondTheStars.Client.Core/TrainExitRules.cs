// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Leaving a monorail train (#3458/#3459), the Unity-free rules the player's body follows:
    /// <list type="bullet">
    /// <item><b>No settle freeze</b> (<see cref="SnapToServer"/>): a rider who walked out keeps their own spot when the
    /// server's set-down point is close to where they stepped off; otherwise (an F set-down, a far disagreement) the body is
    /// moved there plainly — never with the "Stabilisiere Position" hold of a teleport, the surroundings are streamed.</item>
    /// <item><b>Walking out of the door of a halted train</b> (<see cref="PressingIntoDoor"/>, <see cref="Stalled"/>,
    /// <see cref="StepDoorPush"/>): pressing outward against the door opening — stuck there, not walking out — for
    /// <see cref="DoorPushSeconds"/> leaves through the server's standing-spot
    /// search — a platform step the body cannot climb (the floor hovers off the block grid) no longer traps anybody.</item>
    /// <item><b>Jumping off a moving train</b> (<see cref="JumpPush"/>): leaving keeps half the wagon's speed (at most
    /// <see cref="JumpCap"/>) plus a small hop, as a push that eases off — "like in the Wild West". Fall damage counts the
    /// vertical landing speed only, so the train's speed never hurts.</item>
    /// </list>
    /// </summary>
    public static class TrainExitRules
    {
        /// <summary>A walk-off keeps the body's own spot when the server's point is at most this far from where it stepped off.</summary>
        public const float KeepOwnSpotTolerance = 1.5f;

        /// <summary>The share of the wagon's speed a jump-off keeps.</summary>
        public const float JumpFactor = 0.5f;

        /// <summary>The most horizontal speed a jump-off keeps (blocks/s).</summary>
        public const float JumpCap = 12f;

        /// <summary>The jump-off's upward pop (blocks/s).</summary>
        public const float JumpHop = 4f;

        /// <summary>A wagon slower than this (blocks/s) stands: leaving it gives no push.</summary>
        public const float MinJumpSpeed = 1f;

        /// <summary>The door opening spans this far along the wagon either side of its middle (the grid's z 2..3).</summary>
        public const float DoorHalf = 1f;

        /// <summary>A rider this close to the side wall (local X) stands in the door opening.</summary>
        public const float DoorWallBand = 0.3f;

        /// <summary>How long a rider presses outward in the door of a halted train before leaving.</summary>
        public const float DoorPushSeconds = 0.2f;

        /// <summary>The stick/keys must push at least this hard, and mostly outward.</summary>
        public const float DoorPushInput = 0.3f;

        /// <summary>How long a walk-off's report may take to reach the server (seconds): the wagon runs on meanwhile, so the
        /// server's set-down point trails a jump off a moving train by up to its speed × this.</summary>
        public const float WalkOffLagSeconds = 0.5f;

        /// <summary>How far the server's set-down point may lie from where a walk-off stepped off before the body is moved
        /// there: <see cref="KeepOwnSpotTolerance"/>, plus how far the wagon (at <paramref name="wagonSpeed"/>) runs on while the
        /// report travels.</summary>
        public static float WalkOffTolerance(float wagonSpeed)
            => KeepOwnSpotTolerance + (float.IsNaN(wagonSpeed) ? 0f : Math.Max(0f, wagonSpeed) * WalkOffLagSeconds);

        /// <summary>Whether the body must be moved to the server's set-down point: always after an F set-down
        /// (<paramref name="walkedOff"/> false); after a walk-off only when the server's point lies farther than
        /// <paramref name="tolerance"/> (<see cref="WalkOffTolerance"/>; <see cref="KeepOwnSpotTolerance"/> when not given)
        /// from where the body stepped off (<paramref name="leftAt"/>). Longitude and latitude wrap.</summary>
        public static bool SnapToServer(bool walkedOff, Vector3f leftAt, Vector3f server, int circumference, float tolerance = KeepOwnSpotTolerance)
        {
            if (!walkedOff)
            {
                return true;
            }

            float dx = server.X - leftAt.X, dz = server.Z - leftAt.Z;
            if (circumference > 0)
            {
                dx = (float)WorldConstants.WrapDeltaX((double)dx, circumference);
                dz = (float)WorldConstants.WrapDeltaZ((double)dz, circumference);
            }

            float dy = server.Y - leftAt.Y;
            return (dx * dx) + (dy * dy) + (dz * dz) > tolerance * tolerance;
        }

        /// <summary>The push of a jump off a wagon moving at <paramref name="wagonVelocity"/> (world blocks/s): half its
        /// horizontal speed, capped at <see cref="JumpCap"/>, and the <see cref="JumpHop"/> pop. Zero for a standing wagon
        /// (slower than <see cref="MinJumpSpeed"/>).</summary>
        public static Vector3f JumpPush(Vector3f wagonVelocity)
        {
            float speed = (float)Math.Sqrt((wagonVelocity.X * wagonVelocity.X) + (wagonVelocity.Z * wagonVelocity.Z));
            if (!(speed >= MinJumpSpeed))
            {
                return new Vector3f(0f, 0f, 0f);
            }

            float keep = Math.Min(speed * JumpFactor, JumpCap) / speed;
            return new Vector3f(wagonVelocity.X * keep, JumpHop, wagonVelocity.Z * keep);
        }

        /// <summary>Whether a standing rider at wagon-local <paramref name="local"/> presses into the door opening: in the
        /// door's span along the wagon, against the side wall, and the move input (wagon-local X across, Z along) points
        /// mostly outward through that wall.</summary>
        public static bool PressingIntoDoor(Vector3f local, float moveX, float moveZ)
        {
            float push = (float)Math.Sqrt((moveX * moveX) + (moveZ * moveZ));
            return Math.Abs(local.Z) <= DoorHalf
                   && Math.Abs(local.X) >= RailRules.RiderHalfWidth - DoorWallBand
                   && push >= DoorPushInput
                   && Math.Sign(moveX) == Math.Sign(local.X)
                   && Math.Abs(moveX) >= Math.Abs(moveZ);
        }

        /// <summary>A body moving outward faster than this (blocks/s across the wagon) is walking out, not stuck in the door.</summary>
        public const float DoorStallSpeed = 0.5f;

        /// <summary>Whether the body is stuck in the door: its distance from the wagon's middle (<paramref name="absX"/>, last
        /// frame <paramref name="lastAbsX"/>) grew slower than <see cref="DoorStallSpeed"/> over <paramref name="dt"/>. A body
        /// walking freely through the door is not — it walks off on its own, wherever it was heading.</summary>
        public static bool Stalled(float lastAbsX, float absX, float dt)
            => !(dt > 0f) || (absX - lastAbsX) / dt < DoorStallSpeed;

        /// <summary>The door-push timer: grows while the rider presses into the door of a halted train, back to 0 otherwise.</summary>
        public static float StepDoorPush(float held, bool pressing, bool halted, float dt)
            => pressing && halted ? held + Math.Max(0f, dt) : 0f;

        /// <summary>Whether the door push has lasted long enough to leave.</summary>
        public static bool DoorPushDone(float held) => held >= DoorPushSeconds;
    }
}
