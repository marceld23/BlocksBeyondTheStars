// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How the client draws an NPC passenger (#3445), Unity-free so the headless tests can check it. The server sends a
    /// passenger's train frame (<c>NetNpc.FrameId</c>, <see cref="RailRules.FrameId"/>), its wagon-local offset and its facing
    /// in the wagon; <c>NpcView</c> places it with the wagon transform as <c>TrainView</c> draws it every frame — exactly like
    /// a remote player aboard (<c>RemotePlayers</c>) — instead of the interpolation buffer, which would trail a moving wagon
    /// by its delay. Boarding, stepping off and moving to another wagon change the frame: a snap, never a glide.
    /// </summary>
    public static class NpcTrainRide
    {
        /// <summary>How far a seated NPC's pelvis drops onto the seat (the same as a seated NPC on a chair and a seated
        /// remote player).</summary>
        public const float SeatDrop = 0.45f;

        /// <summary>Whether a snapshot names a train frame (an older server sends none: the NPC is drawn on foot).</summary>
        public static bool Aboard(string? frameId) => RailRules.TryParseFrame(frameId ?? string.Empty, out _, out _);

        /// <summary>Whether the frame changed between two snapshots — boarded, stepped off or moved wagons: the drawing snaps
        /// to the new snapshot instead of easing toward it.</summary>
        public static bool FrameChanged(string? before, string? after)
            => !string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal);

        /// <summary>The drawn yaw (degrees) of a passenger whose wagon is drawn at <paramref name="wagonYawDegrees"/> and who
        /// faces <paramref name="localYaw"/> radians in it (0 = toward the cab, +π/2 = across to the wagon's right).</summary>
        public static float DrawnYawDegrees(float wagonYawDegrees, float localYaw)
        {
            float yaw = (wagonYawDegrees + (localYaw * (180f / (float)Math.PI))) % 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }
}
