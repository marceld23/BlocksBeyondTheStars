// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One frame of a guardian giant's pose (#3676), as <see cref="GuardianGiantMotion"/> evaluates it and the client's view
    /// applies it to the six limbs of <c>GuardianGiantBody</c>. Angles are degrees in Unity's Euler convention, each about the
    /// limb's joint: a positive pitch leans the torso forward, nods the head down and swings an arm's or a leg's far end
    /// BACKWARD (so an arm raised forward has a negative pitch); a positive roll swings an arm's hand toward +X (the left arm
    /// goes out with a negative roll, the right one with a positive one); a positive yaw turns to the right.
    /// </summary>
    public struct GuardianGiantFrame
    {
        /// <summary>The root's blend: 0 = the lying pose at the status anchor (exactly where the block body lies),
        /// 1 = standing at the entity's position and heading.</summary>
        public float Rise;

        /// <summary>Blocks the root is raised (+) or lowered (−) on top of the standing position (a kneel, a hop).</summary>
        public float RootY;

        /// <summary>The torso about the pelvis.</summary>
        public float TorsoPitch;

        /// <summary>See <see cref="TorsoPitch"/>.</summary>
        public float TorsoYaw;

        /// <summary>See <see cref="TorsoPitch"/>.</summary>
        public float TorsoRoll;

        /// <summary>The head about the neck.</summary>
        public float HeadPitch;

        /// <summary>See <see cref="HeadPitch"/>.</summary>
        public float HeadYaw;

        /// <summary>The arms about the shoulders.</summary>
        public float ArmLPitch;

        /// <summary>See <see cref="ArmLPitch"/>.</summary>
        public float ArmLRoll;

        /// <summary>See <see cref="ArmLPitch"/>.</summary>
        public float ArmRPitch;

        /// <summary>See <see cref="ArmLPitch"/>.</summary>
        public float ArmRRoll;

        /// <summary>The legs about the hips.</summary>
        public float LegLPitch;

        /// <summary>See <see cref="LegLPitch"/>.</summary>
        public float LegRPitch;

        /// <summary>Blocks a hip is hiked to lift its (rigid) leg clear of the ground while it swings.</summary>
        public float LegLLift;

        /// <summary>See <see cref="LegLLift"/>.</summary>
        public float LegRLift;

        /// <summary>A leap's progress 0 … 1 (the view flies the root from the take-off point to the aim along an arc), or −1
        /// on the ground.</summary>
        public float Air;

        /// <summary>0 … 1: how hard the body shudders (the view adds a small jitter; less under reduced motion).</summary>
        public float Shudder;

        /// <summary>0 … 1: how brightly the lights flare beyond their blink.</summary>
        public float Flare;

        /// <summary>0 … 1: the visor's glow (the laser's warning and beam).</summary>
        public float Visor;

        /// <summary>0 … 1: the search light's strength.</summary>
        public float SearchLight;

        /// <summary>0 … 1: how far the head turns to look at the aim point.</summary>
        public float LookAtAim;

        /// <summary>Standing still, every limb at rest.</summary>
        public static GuardianGiantFrame Standing => new GuardianGiantFrame { Rise = 1f, Air = -1f };

        /// <summary>Lying at the anchor, every limb at rest — the block body's pose.</summary>
        public static GuardianGiantFrame Lying => new GuardianGiantFrame { Rise = 0f, Air = -1f };

        /// <summary>The blend of two frames (<paramref name="t"/> 0 = <paramref name="a"/>, 1 = <paramref name="b"/>); the
        /// leap's progress switches over halfway.</summary>
        public static GuardianGiantFrame Lerp(in GuardianGiantFrame a, in GuardianGiantFrame b, float t)
        {
            float k = GuardianGiantPose.Clamp01(t);
            return new GuardianGiantFrame
            {
                Rise = L(a.Rise, b.Rise, k),
                RootY = L(a.RootY, b.RootY, k),
                TorsoPitch = L(a.TorsoPitch, b.TorsoPitch, k),
                TorsoYaw = L(a.TorsoYaw, b.TorsoYaw, k),
                TorsoRoll = L(a.TorsoRoll, b.TorsoRoll, k),
                HeadPitch = L(a.HeadPitch, b.HeadPitch, k),
                HeadYaw = L(a.HeadYaw, b.HeadYaw, k),
                ArmLPitch = L(a.ArmLPitch, b.ArmLPitch, k),
                ArmLRoll = L(a.ArmLRoll, b.ArmLRoll, k),
                ArmRPitch = L(a.ArmRPitch, b.ArmRPitch, k),
                ArmRRoll = L(a.ArmRRoll, b.ArmRRoll, k),
                LegLPitch = L(a.LegLPitch, b.LegLPitch, k),
                LegRPitch = L(a.LegRPitch, b.LegRPitch, k),
                LegLLift = L(a.LegLLift, b.LegLLift, k),
                LegRLift = L(a.LegRLift, b.LegRLift, k),
                Air = k < 0.5f ? a.Air : b.Air,
                Shudder = L(a.Shudder, b.Shudder, k),
                Flare = L(a.Flare, b.Flare, k),
                Visor = L(a.Visor, b.Visor, k),
                SearchLight = L(a.SearchLight, b.SearchLight, k),
                LookAtAim = L(a.LookAtAim, b.LookAtAim, k),
            };
        }

        private static float L(float a, float b, float t) => a + ((b - a) * t);
    }
}
