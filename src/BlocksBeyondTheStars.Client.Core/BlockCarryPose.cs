// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How a block carried over the head looks (#3766): the numbers of the pose, Unity-free so the suite can hold them.
    /// Third person and other players: both arms straight up and the block — drawn bigger than in the palm — centred
    /// above the head. First person (#3828): the block rises to the top middle of the view, level and bigger, and both
    /// suit arms come up from below to its lower corners — the player sees that they hold it up with both hands, and
    /// the aim point between the arms stays free. The rules of carrying and throwing are <c>BlockThrow</c> (Shared).
    /// </summary>
    public static class BlockCarryPose
    {
        /// <summary>Degrees the arms roll outwards-and-up: straight up, a touch inwards so the hands meet at the block.</summary>
        public const float ArmLift = 172f;

        /// <summary>The carried block's centre above the feet of a figure of size 1, and a touch ahead of its axis.</summary>
        public const float BlockHeight = 2.5f;

        /// <inheritdoc cref="BlockHeight"/>
        public const float BlockForward = 0.03f;

        /// <summary>Where the block starts its way up (about the chest), above the feet.</summary>
        public const float BlockStartHeight = 1.7f;

        /// <summary>The carried block's edge, blocks — half a world block, so it reads as "that block" without hiding the head.</summary>
        public const float BlockEdge = 0.5f;

        /// <summary>Seconds the arms take to go up.</summary>
        public const float RaiseSeconds = 0.18f;

        /// <summary>The first-person view (#3828), in the camera's space (right, up, forward): where the held block's holder
        /// sits while it is carried — the top middle of the view. The view's own rest pose is the lower right corner.</summary>
        public static Vector3 ViewBlockPos => new Vector3(0f, 0.27f, 0.50f);

        /// <summary>The carried block's pitch in the view, degrees: level, tipped up a little so its underside shows.</summary>
        public const float ViewBlockPitch = -12f;

        /// <summary>How much bigger the block is drawn in the view while it is up (it is "that block", not a palm cube).</summary>
        public const float ViewBlockScale = 1.9f;

        /// <summary>The right arm's fist in the view, at the block's lower right corner; the left arm mirrors X.</summary>
        public static Vector3 ViewArmPos => new Vector3(0.20f, 0.10f, 0.60f);

        /// <summary>The right arm's angles, degrees (pitch, yaw, roll): the fist up at the block, the forearm running down
        /// and outwards out of the view. The left arm mirrors yaw and roll.</summary>
        public static Vector3 ViewArmEuler => new Vector3(-62f, -8f, 6f);

        /// <summary>The forearm's length under the fist, model units — long enough to leave the view at its lower edge.</summary>
        public const float ViewForearm = 0.9f;

        /// <summary>The factor that brings a held model whose largest edge is <paramref name="heldSize"/> blocks to
        /// <see cref="BlockEdge"/>. A model with no size keeps its scale.</summary>
        public static float ScaleFor(float heldSize) => heldSize > 1e-4f ? BlockEdge / heldSize : 1f;

        /// <summary>The blend 0..1 one frame later: up within <see cref="RaiseSeconds"/>, down twice as fast.</summary>
        public static float Ease(float blend, bool carrying, float dt)
        {
            float step = dt / RaiseSeconds * (carrying ? 1f : 2f);
            return carrying ? Math.Min(1f, blend + step) : Math.Max(0f, blend - step);
        }
    }
}
