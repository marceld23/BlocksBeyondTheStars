// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Unity-free geometry of a guardian giant's model (#3676): where the lying body sits (so the model that rises starts
    /// exactly on the blocks the server just removed, and the one that falls ends exactly where it stamps them), the arcs of a
    /// leap and a thrown rock, the camera shake of an impact and the walk's footfalls. Rotations follow Unity's convention
    /// (left-handed, Y up, forward +Z): <see cref="RotateX"/> is <c>Quaternion.Euler(degrees, 0, 0) * p</c> and
    /// <see cref="RotateY"/> is <c>Quaternion.Euler(0, degrees, 0) * p</c>, so the client applies the very same angles to its
    /// transforms.
    /// </summary>
    public static class GuardianGiantPose
    {
        /// <summary>The pitch (Unity Euler X, degrees) that lays the standing body on its back about its root:
        /// <c>Quaternion.Euler(-90, 0, 0)</c> maps a standing point (x, y, z) to (x, z, −y) — <see cref="GuardianGiantBody.ToLying"/>.</summary>
        public const float LyingPitch = -90f;

        /// <summary>The length of a leg from the hip joint to the sole (blocks) — what a kneel lowers the root by.</summary>
        public const float LegLength = 6f;

        /// <summary>The yaw (Unity Euler Y, degrees) of a lying body's facing: a quarter turn per step, 0 … 270.</summary>
        public static float LyingYaw(int facing) => (((facing % 4) + 4) % 4) * 90f;

        /// <summary>A point turned by <paramref name="degrees"/> about +X, Unity's <c>Quaternion.Euler(degrees, 0, 0) * p</c>
        /// (+90 tips +Y onto +Z).</summary>
        public static Vector3f RotateX(Vector3f p, float degrees)
        {
            double r = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vector3f(p.X, (p.Y * c) - (p.Z * s), (p.Y * s) + (p.Z * c));
        }

        /// <summary>A point turned by <paramref name="degrees"/> about +Y, Unity's <c>Quaternion.Euler(0, degrees, 0) * p</c>
        /// (+90 turns +Z onto +X).</summary>
        public static Vector3f RotateY(Vector3f p, float degrees)
        {
            double r = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vector3f((p.X * c) + (p.Z * s), p.Y, (-p.X * s) + (p.Z * c));
        }

        /// <summary>A standing body-local point in the lying pose, relative to the anchor's corner:
        /// <c>Quaternion.Euler(0, LyingYaw(facing), 0) * Quaternion.Euler(LyingPitch, 0, 0) * p</c>. The root (0, 0, 0) is the
        /// anchor cell's minimum corner, so a standing cell centre lands on the centre of its <see cref="GuardianGiantBody.Lying"/>
        /// cell.</summary>
        public static Vector3f ToLying(Vector3f standing, int facing)
            => RotateY(RotateX(standing, LyingPitch), LyingYaw(facing));

        /// <summary>The world point of the root in the lying pose: the anchor cell's minimum corner.</summary>
        public static Vector3f LyingRoot(int anchorX, int anchorY, int anchorZ) => new Vector3f(anchorX, anchorY, anchorZ);

        /// <summary>Where the root stands after rising when the server's position is not known yet: on the ground under the
        /// anchor — the row the lying back rests in (<see cref="GuardianGiantBody.LyingBottom"/>).</summary>
        public static Vector3f StandingRootGuess(int anchorX, int anchorY, int anchorZ)
            => new Vector3f(anchorX, anchorY + GuardianGiantBody.LyingBottom, anchorZ);

        /// <summary>A point of a thrown arc: straight from <paramref name="from"/> to <paramref name="to"/> plus a parabola that
        /// is <paramref name="peak"/> blocks high halfway (<paramref name="u"/> 0 … 1, clamped).</summary>
        public static Vector3f Arc(Vector3f from, Vector3f to, float u, float peak)
        {
            float t = Clamp01(u);
            float lift = 4f * peak * t * (1f - t);
            return new Vector3f(
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t) + lift,
                from.Z + ((to.Z - from.Z) * t));
        }

        /// <summary>The height of an arc over a distance: a third of it, never below <paramref name="min"/> — a far leap flies
        /// higher than a short one.</summary>
        public static float ArcPeak(float distance, float min)
            => Math.Max(min, Math.Abs(distance) * 0.35f);

        /// <summary>The camera shake (0 … <see cref="GuardianGiantRules.ShakeStrength"/>) a player feels at
        /// <paramref name="distance"/> from an impact whose wave reaches <paramref name="radius"/>: full strength at the centre,
        /// falling off linearly to nothing at twice the radius.</summary>
        public static float Shake(float distance, float radius)
        {
            if (!(radius > 0f) || float.IsNaN(distance))
            {
                return 0f;
            }

            return GuardianGiantRules.ShakeStrength * Clamp01(1f - (Math.Abs(distance) / (2f * radius)));
        }

        /// <summary>The push a shock wave (<see cref="GuardianGiantRules.WaveFx"/>) gives a player at <paramref name="offset"/>
        /// from its centre: away from it across the ground at <see cref="GuardianGiantRules.KnockSpeed"/> plus
        /// <see cref="GuardianGiantRules.KnockLift"/> up, both falling off like the damage
        /// (<see cref="GuardianGiantRules.WaveDamage"/>: full at the centre, <see cref="GuardianGiantRules.EdgeShare"/> at
        /// <paramref name="radius"/>); nothing beyond the radius or more than <see cref="GuardianGiantRules.WaveHeight"/> above or
        /// below (the server's rule for the damage). Right at the centre the push goes along +X.</summary>
        public static Vector3f WavePush(Vector3f offset, float radius)
        {
            float flat = (float)Math.Sqrt((offset.X * offset.X) + (offset.Z * offset.Z));
            float share = GuardianGiantRules.WaveDamage(1f, radius, flat);
            if (!(share > 0f) || Math.Abs(offset.Y) > GuardianGiantRules.WaveHeight)
            {
                return new Vector3f(0f, 0f, 0f);
            }

            float x = flat > 1e-3f ? offset.X / flat : 1f, z = flat > 1e-3f ? offset.Z / flat : 0f;
            float speed = GuardianGiantRules.KnockSpeed * share;
            return new Vector3f(x * speed, GuardianGiantRules.KnockLift * share, z * speed);
        }

        /// <summary>The walk's footfalls between two readings of the gait phase (<paramref name="prevWalk"/> ≤
        /// <paramref name="walk"/>, in strides, not wrapped): bit 0 = the left foot planted, bit 1 = the right one. The legs are
        /// a biped's walk (<see cref="CreatureGait.PhaseOffset"/>: left at 0, right half a stride later) and a foot plants
        /// when its own phase wraps to 0.</summary>
        public static int Footfalls(float prevWalk, float walk)
        {
            if (!(walk > prevWalk))
            {
                return 0;
            }

            if (walk - prevWalk >= 1f)
            {
                return 3; // a whole stride in one reading (a long frame): both feet came down
            }

            int mask = 0;
            float left = CreatureGait.PhaseOffset(Gait.Walk, 0, 0, 1);
            float right = CreatureGait.PhaseOffset(Gait.Walk, 1, 0, 1);
            if (Math.Floor(prevWalk + left) != Math.Floor(walk + left))
            {
                mask |= 1;
            }

            if (Math.Floor(prevWalk + right) != Math.Floor(walk + right))
            {
                mask |= 2;
            }

            return mask;
        }

        /// <summary>Smoothstep of <paramref name="t"/> from <paramref name="from"/> to <paramref name="to"/> (0 before, 1 after).</summary>
        public static float Smooth(float from, float to, float t)
        {
            if (to <= from)
            {
                return t >= to ? 1f : 0f;
            }

            float u = Clamp01((t - from) / (to - from));
            return u * u * (3f - (2f * u));
        }

        /// <summary>A bump that rises from 0 at <paramref name="from"/> to 1 halfway and back to 0 at <paramref name="to"/>.</summary>
        public static float Bump(float from, float to, float t)
        {
            if (to <= from || t <= from || t >= to)
            {
                return 0f;
            }

            return (float)Math.Sin((t - from) / (to - from) * Math.PI);
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
