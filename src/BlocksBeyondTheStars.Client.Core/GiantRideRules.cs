// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Riding on a giant's back (#3467), the Unity-free rules. A player standing on a giant's body part is carried by that
    /// part's change of pose every frame — its position and its turn about the vertical axis, never its pitch or roll, so the
    /// rider stays upright (<see cref="Carry"/>). Leaving it in mid-air keeps part of its speed (<see cref="LeaveMomentum"/>).
    /// The sky giant, the leviathan and the sandworm carry a rider as long as they like (a diving worm or leviathan drops
    /// them: its colliders are gone); the colossus, the arachnid and the Soundrak shake a rider off after
    /// <see cref="ShakeOffSeconds"/> (<see cref="ShakesOff"/>, <see cref="ShakeOffImpulse"/>) — their backs must not be a safe
    /// spot from their own attacks. Yaw follows Unity's convention: degrees about +Y, clockwise seen from above, forward = +Z.
    /// </summary>
    public static class GiantRideRules
    {
        /// <summary>A body part that jumps farther than this in one frame was re-placed (a resync, the world seam), not moved:
        /// no carry that frame.</summary>
        public const float MaxCarryStep = 20f;

        /// <summary>The share of the giant's horizontal speed a rider keeps when leaving it in mid-air …</summary>
        public const float LeaveFactor = 0.8f;

        /// <summary>… at most this fast (blocks/s), and nothing below <see cref="MinLeaveSpeed"/>.</summary>
        public const float LeaveCap = 12f;
        public const float MinLeaveSpeed = 1f;

        /// <summary>How long a shaking giant tolerates a rider (seconds).</summary>
        public const float ShakeOffSeconds = 4f;

        /// <summary>The shake-off's push away from the body (blocks/s) and its hop (blocks/s up).</summary>
        public const float ShakeOffPush = 9f;
        public const float ShakeOffHop = 7f;

        /// <summary>The yaw (degrees) of a forward vector: 0 along +Z, 90 along +X.</summary>
        public static float YawOf(Vector3f forward)
            => (float)(Math.Atan2(forward.X, forward.Z) * 180.0 / Math.PI);

        /// <summary>A vector turned by <paramref name="degrees"/> about +Y (Unity's <c>Quaternion.Euler(0, degrees, 0) * v</c>).</summary>
        public static Vector3f RotateYaw(Vector3f v, float degrees)
        {
            double r = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vector3f((v.X * c) + (v.Z * s), v.Y, (-v.X * s) + (v.Z * c));
        }

        /// <summary>The shortest signed turn from <paramref name="from"/> to <paramref name="to"/> (degrees, −180 … 180].</summary>
        public static float DeltaYaw(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f)
            {
                d -= 360f;
            }
            else if (d <= -180f)
            {
                d += 360f;
            }

            return d;
        }

        /// <summary>How far a rider at <paramref name="player"/> is carried, and by how much they turn, when the body part under
        /// them moved from <paramref name="prevPos"/> / <paramref name="prevYaw"/> to <paramref name="curPos"/> /
        /// <paramref name="curYaw"/>: the rider keeps their spot in the part's (yaw-only) frame. Nothing when the part jumped
        /// farther than <see cref="MaxCarryStep"/> or a value is not finite.</summary>
        public static (Vector3f Delta, float YawDelta) Carry(Vector3f player, Vector3f prevPos, float prevYaw, Vector3f curPos, float curYaw)
        {
            var step = new Vector3f(curPos.X - prevPos.X, curPos.Y - prevPos.Y, curPos.Z - prevPos.Z);
            if (!Finite(step) || !Finite(player) || float.IsNaN(prevYaw) || float.IsNaN(curYaw)
                || (step.X * step.X) + (step.Y * step.Y) + (step.Z * step.Z) > MaxCarryStep * MaxCarryStep)
            {
                return (new Vector3f(0f, 0f, 0f), 0f);
            }

            float turn = DeltaYaw(prevYaw, curYaw);
            var offset = new Vector3f(player.X - prevPos.X, player.Y - prevPos.Y, player.Z - prevPos.Z);
            var turned = RotateYaw(offset, turn);
            var now = new Vector3f(curPos.X + turned.X, curPos.Y + turned.Y, curPos.Z + turned.Z);
            return (new Vector3f(now.X - player.X, now.Y - player.Y, now.Z - player.Z), turn);
        }

        /// <summary>The push a rider keeps when leaving the giant in mid-air: <see cref="LeaveFactor"/> of its horizontal
        /// velocity, capped at <see cref="LeaveCap"/>; zero for a giant slower than <see cref="MinLeaveSpeed"/>.</summary>
        public static Vector3f LeaveMomentum(Vector3f velocity)
        {
            float speed = (float)Math.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));
            if (!(speed >= MinLeaveSpeed))
            {
                return new Vector3f(0f, 0f, 0f);
            }

            float keep = Math.Min(speed * LeaveFactor, LeaveCap) / speed;
            return new Vector3f(velocity.X * keep, 0f, velocity.Z * keep);
        }

        /// <summary>Whether a giant of this body plan shakes a rider off (the colossus, the arachnid, the Soundrak).</summary>
        public static bool ShakesOff(string? bodyPlan)
            => bodyPlan == "Colossus" || bodyPlan == "Arachnid" || bodyPlan == "Soundrak";

        /// <summary>The shake-off's impulse for a rider <paramref name="away"/> from the body's middle (horizontal; any length):
        /// <see cref="ShakeOffPush"/> outward plus the <see cref="ShakeOffHop"/>. Straight above the middle the push goes along +X.</summary>
        public static Vector3f ShakeOffImpulse(Vector3f away)
        {
            float len = (float)Math.Sqrt((away.X * away.X) + (away.Z * away.Z));
            float x = len > 1e-3f ? away.X / len : 1f, z = len > 1e-3f ? away.Z / len : 0f;
            return new Vector3f(x * ShakeOffPush, ShakeOffHop, z * ShakeOffPush);
        }

        private static bool Finite(Vector3f v) => !float.IsNaN(v.X) && !float.IsInfinity(v.X) && !float.IsNaN(v.Y)
                                                   && !float.IsInfinity(v.Y) && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);
    }
}
