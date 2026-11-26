// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#4027: where the on-foot third-person camera sits — the player's choice in the settings.</summary>
    public enum ThirdPersonView
    {
        /// <summary>The default: the camera orbits a raised pivot, so the figure stays in the lower third at every pitch.</summary>
        High = 0,

        /// <summary>The view before #4027: a fixed point behind and just above the head; the pitch only turns the camera.</summary>
        Behind = 1,

        /// <summary>Over the right shoulder: the figure stands to the left of the crosshair.</summary>
        Shoulder = 2,
    }

    /// <summary>
    /// #4027: the third-person camera rig's numbers, Unity-free so the suite can hold them. The old view sat 0.3 m above the
    /// head in the body's yaw frame and the pitch only turned the camera in place: the head landed just under the crosshair
    /// and the figure covered half the screen or more (FOV 80), looking down put the crosshair on the feet. The
    /// <see cref="ThirdPersonView.High"/> view orbits the first-person eye (<see cref="PivotHeight"/>) instead: the arm
    /// (<see cref="HighArm"/>) turns with the look pitch, clamped to <see cref="OrbitPitchMin"/>…<see cref="OrbitPitchMax"/>,
    /// while the camera itself keeps the full pitch — so at a level look the top of the head sits about a third up from
    /// the bottom of the screen. All vectors are in the body's yaw frame (x right, y up, z forward), metres over the feet;
    /// pitch is in degrees with positive looking down, like the camera's own.
    /// <para>The spring arm (#1460) casts from the pivot to the wanted place and stops in front of a wall
    /// (<see cref="AllowedBoom"/>, <see cref="EaseBoom"/>). The interaction ray starts at the camera, so in third person it
    /// runs the boom's length further (<see cref="AimRayLength"/>) and only takes a cell within reach of the body
    /// (<see cref="CellWithinBodyReach"/>) — the reach in front of the figure is the first person's; the server's own
    /// reach rule (8 + 1 from the body) is unchanged.</para>
    /// </summary>
    public static class ThirdPersonRig
    {
        /// <summary>How many views the settings row cycles through.</summary>
        public const int ViewCount = 3;

        /// <summary>The pivot every view casts its spring arm from: the first-person eye over the feet.</summary>
        public const float PivotHeight = 1.6f;

        /// <summary>The <see cref="ThirdPersonView.High"/> arm from the pivot at a level look, before the orbit turns it.</summary>
        public static Vector3 HighArm => new Vector3(0f, 1.2f, -3.5f);

        /// <summary>The <see cref="ThirdPersonView.Behind"/> eye over the feet — exactly the view before #4027.</summary>
        public static Vector3 BehindEye => new Vector3(0f, 1.9f, -3.5f);

        /// <summary>The <see cref="ThirdPersonView.Shoulder"/> eye over the feet: right of the head, a little higher and closer.</summary>
        public static Vector3 ShoulderEye => new Vector3(0.6f, 2.2f, -3.0f);

        /// <summary>The orbit's pitch range (degrees): looking further up keeps the camera near the ground instead of in it,
        /// looking further down keeps it from standing straight over the head.</summary>
        public const float OrbitPitchMin = -25f;

        /// <inheritdoc cref="OrbitPitchMin"/>
        public const float OrbitPitchMax = 75f;

        /// <summary>The spring arm's shortest length (m) — closer than this the camera would sit inside the head.</summary>
        public const float BoomMinimum = 0.35f;

        /// <summary>How far in front of a hit wall the camera stops (m).</summary>
        public const float BoomWallGap = 0.05f;

        /// <summary>How fast (m/s) the arm grows back once the wall is gone — it pulls in at once.</summary>
        public const float BoomExtendSpeed = 6f;

        /// <summary>Under a low ceiling the arm pulls in so short that the figure would fill the screen: below this length
        /// (m) the own figure is hidden.</summary>
        public const float HideAvatarBelow = 0.8f;

        /// <summary>The body's height (m) over the feet that the interaction reach is measured against.</summary>
        public const float BodyHeight = 1.8f;

        /// <summary>The pivot over the feet (the first-person eye).</summary>
        public static Vector3 Pivot => new Vector3(0f, PivotHeight, 0f);

        /// <summary>A stored setting as a view; anything unknown (a hand-edited file) is the default.</summary>
        public static ThirdPersonView Normalize(int stored)
            => stored >= 0 && stored < ViewCount ? (ThirdPersonView)stored : ThirdPersonView.High;

        /// <summary>The next view in <paramref name="dir"/> (±1), wrapping — the settings row's arrows.</summary>
        public static ThirdPersonView Step(ThirdPersonView view, int dir)
        {
            int next = ((int)Normalize((int)view) + dir) % ViewCount;
            return (ThirdPersonView)(next < 0 ? next + ViewCount : next);
        }

        /// <summary>The look pitch the orbit follows: the look clamped to <see cref="OrbitPitchMin"/>…<see cref="OrbitPitchMax"/>.</summary>
        public static float OrbitPitch(float pitch) => Math.Max(OrbitPitchMin, Math.Min(OrbitPitchMax, pitch));

        /// <summary><paramref name="arm"/> turned about the body's right axis by <paramref name="pitch"/> degrees (positive
        /// tips the forward axis down, as the camera's pitch does).</summary>
        public static Vector3 Orbit(Vector3 arm, float pitch)
        {
            double a = pitch * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new Vector3(arm.X, (arm.Y * c) - (arm.Z * s), (arm.Y * s) + (arm.Z * c));
        }

        /// <summary>Where the camera wants to sit over the feet for <paramref name="view"/> at the look
        /// <paramref name="pitch"/>, before the spring arm pulls it in front of a wall.</summary>
        public static Vector3 Eye(ThirdPersonView view, float pitch) => view switch
        {
            ThirdPersonView.Behind => BehindEye,
            ThirdPersonView.Shoulder => ShoulderEye,
            _ => Pivot + Orbit(HighArm, OrbitPitch(pitch)),
        };

        /// <summary>The arm's length allowed this frame: the full <paramref name="full"/> length, or — when the cast from the
        /// pivot hit something <paramref name="hitDistance"/> metres out — just in front of it, never under
        /// <see cref="BoomMinimum"/>.</summary>
        public static float AllowedBoom(float full, float? hitDistance)
            => hitDistance is { } hit ? Math.Max(BoomMinimum, Math.Min(full, hit - BoomWallGap)) : full;

        /// <summary>The arm's length after a frame of <paramref name="dt"/> seconds: in at once to <paramref name="allowed"/>
        /// (a wall must never be seen from inside), out again at <see cref="BoomExtendSpeed"/>. A negative
        /// <paramref name="current"/> (just switched on) takes the allowed length at once.</summary>
        public static float EaseBoom(float current, float allowed, float dt)
        {
            if (current < 0f || allowed < current)
            {
                return allowed;
            }

            return Math.Min(allowed, current + (Math.Max(0f, dt) * BoomExtendSpeed));
        }

        /// <summary>Whether the own figure is hidden for an arm of <paramref name="boom"/> metres (negative: not measured yet).</summary>
        public static bool HidesAvatar(float boom) => boom >= 0f && boom < HideAvatarBelow;

        /// <summary>How far the interaction ray runs from the camera: the reach plus the arm, so it still gets
        /// <paramref name="reach"/> metres past the figure.</summary>
        public static float AimRayLength(float reach, float boom) => reach + Math.Max(0f, boom);

        /// <summary>Whether the cell at (<paramref name="cx"/>, <paramref name="cy"/>, <paramref name="cz"/>) lies within
        /// <paramref name="reach"/> of the body standing at the feet (<paramref name="feetX"/>, <paramref name="feetY"/>,
        /// <paramref name="feetZ"/>): the nearest point of the cell to the body's upright segment, like the server measures
        /// (which adds a metre of slack on top).</summary>
        public static bool CellWithinBodyReach(int cx, int cy, int cz, float feetX, float feetY, float feetZ, float reach)
        {
            float dx = Gap(feetX, feetX, cx, cx + 1f);
            float dy = Gap(feetY, feetY + BodyHeight, cy, cy + 1f);
            float dz = Gap(feetZ, feetZ, cz, cz + 1f);
            return (dx * dx) + (dy * dy) + (dz * dz) <= reach * reach;
        }

        /// <summary>The gap between the spans [<paramref name="a0"/>, <paramref name="a1"/>] and [<paramref name="b0"/>,
        /// <paramref name="b1"/>] (0 when they overlap).</summary>
        private static float Gap(float a0, float a1, float b0, float b1)
            => b0 > a1 ? b0 - a1 : a0 > b1 ? a0 - b1 : 0f;
    }
}
