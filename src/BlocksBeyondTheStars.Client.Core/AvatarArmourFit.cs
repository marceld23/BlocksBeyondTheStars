// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One box of the player figure: its centre and its size in the frame of the pivot it hangs on (the body root,
    /// a hip, a knee). Metres; +z is the figure's front, +y up.</summary>
    public readonly struct AvatarBox
    {
        public AvatarBox(float x, float y, float z, float width, float height, float depth)
        {
            X = x;
            Y = y;
            Z = z;
            Width = width;
            Height = height;
            Depth = depth;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public float Width { get; }

        public float Height { get; }

        public float Depth { get; }

        public float Left => X - (Width * 0.5f);

        public float Right => X + (Width * 0.5f);

        public float Bottom => Y - (Height * 0.5f);

        public float Top => Y + (Height * 0.5f);

        public float Back => Z - (Depth * 0.5f);

        public float Front => Z + (Depth * 0.5f);

        /// <summary>The same box seen from the frame its pivot hangs in (the pivot sits at the given offset there, unturned).</summary>
        public AvatarBox From(float pivotX, float pivotY, float pivotZ = 0f)
            => new AvatarBox(X + pivotX, Y + pivotY, Z + pivotZ, Width, Height, Depth);
    }

    /// <summary>
    /// How worn armour sits on the player figure (#3752 — Justus, 2026-11-17: "the worn armour goes through the skin, sticks
    /// in you and flickers"). The chest plate's back lay in exactly the plane of the body's back, and the leg plates' front
    /// and back in exactly the planes of the pelvis: two faces in one plane, facing the same way, flicker against each other
    /// (z-fighting) — the skin's colour flashed through the plate on the back and round the hips. The suit's green status
    /// light also stuck 5 mm out of the chest plate; the figure hides the suit's chest panel and that light under a chest
    /// piece now (<c>PlayerAvatar.ArmourFit.cs</c>).
    /// <para>The boxes are here, Unity-free, so a test can hold the rule for every set: <b>no face of a plate lies in the
    /// plane of a face of the body part it covers</b> (<see cref="ShareAFace"/>). All four chest pieces (classic, guardian,
    /// obsidian, Ressaw) and all four pairs of leg plates draw <see cref="ChestPlate"/> and <see cref="LegPlate"/> — since
    /// #3861 with <see cref="ArmPlate"/>, <see cref="Bracer"/> and <see cref="WaistPlate"/> on the chest piece and
    /// <see cref="ShinPlate"/> on the leg piece; the figure itself draws the body boxes named here.</para>
    /// </summary>
    public static class AvatarArmourFit
    {
        /// <summary>The smallest gap between a plate's face and the body face under it, or a neighbouring plate's.</summary>
        public const float Clearance = 0.004f;

        /// <summary>The hip pivots sit this far left and right of the middle …</summary>
        public const float HipX = 0.13f;

        /// <summary>… at this height; the knee pivots hang <see cref="KneeDrop"/> below them.</summary>
        public const float HipY = 0.92f;

        /// <summary>The knee pivot's height in the hip frame.</summary>
        public const float KneeDrop = -0.46f;

        // --- The body under the plates (the root frame; the leg boxes in the hip frame). ---

        public static readonly AvatarBox Pelvis = new AvatarBox(0f, 0.97f, 0f, 0.46f, 0.22f, 0.28f);

        public static readonly AvatarBox Abdomen = new AvatarBox(0f, 1.18f, 0f, 0.46f, 0.26f, 0.30f);

        public static readonly AvatarBox Chest = new AvatarBox(0f, 1.45f, 0f, 0.58f, 0.40f, 0.34f);

        /// <summary>The right shoulder block; the left one mirrors it.</summary>
        public static readonly AvatarBox Shoulder = new AvatarBox(0.30f, 1.55f, 0f, 0.18f, 0.18f, 0.30f);

        /// <summary>The upper leg, in its hip's frame.</summary>
        public static readonly AvatarBox UpperLeg = new AvatarBox(0f, -0.23f, 0f, 0.22f, 0.46f, 0.22f);

        // --- The plates. ---

        /// <summary>Every chest piece's plate. Its front stays at 0.21 (the sets' bands, seams and the disc sit on it); its
        /// back reaches 2 cm behind the body's back instead of ending in its plane.</summary>
        public static readonly AvatarBox ChestPlate = new AvatarBox(0f, 1.45f, 0.01f, 0.62f, 0.42f, 0.40f);

        /// <summary>Every leg plate, in its hip's frame: half a centimetre deeper than the pelvis in front and behind, and
        /// still half a centimetre behind the front of a knee pad.</summary>
        public static readonly AvatarBox LegPlate = new AvatarBox(0f, -0.22f, 0f, 0.28f, 0.46f, 0.29f);

        /// <summary>A set's knee pad, in its knee's frame.</summary>
        public static readonly AvatarBox KneePad = new AvatarBox(0f, -0.02f, 0.12f, 0.22f, 0.16f, 0.06f);

        // --- #3861 (Justus, 2026-11-20: "an armour covers only 2/4 of the body instead of 3/4"): the chest piece also covers
        // the arms and the waist down to the hips, the leg piece the shins. ---

        /// <summary>The shoulder pivots sit this far left and right of the middle …</summary>
        public const float ShoulderX = 0.32f;

        /// <summary>… at this height; the elbow pivots hang <see cref="ElbowDrop"/> below them.</summary>
        public const float ShoulderY = 1.5f;

        /// <summary>The elbow pivot's height in the shoulder frame.</summary>
        public const float ElbowDrop = -0.42f;

        /// <summary>The upper arm, in its shoulder's frame.</summary>
        public static readonly AvatarBox UpperArm = new AvatarBox(0f, -0.21f, 0f, 0.16f, 0.42f, 0.16f);

        /// <summary>The forearm, in its elbow's frame.</summary>
        public static readonly AvatarBox LowerArm = new AvatarBox(0f, -0.21f, 0f, 0.15f, 0.42f, 0.15f);

        /// <summary>The lower leg, in its knee's frame.</summary>
        public static readonly AvatarBox LowerLeg = new AvatarBox(0f, -0.21f, 0f, 0.20f, 0.42f, 0.20f);

        /// <summary>The plate over the upper arm, in its shoulder's frame: from just under the shoulder block to above the
        /// elbow, two centimetres clear of the arm all round.</summary>
        public static readonly AvatarBox ArmPlate = new AvatarBox(0f, -0.19f, 0f, 0.20f, 0.28f, 0.20f);

        /// <summary>The bracer over the forearm, in its elbow's frame — above the wrist, so a glove cuff stays free.</summary>
        public static readonly AvatarBox Bracer = new AvatarBox(0f, -0.25f, 0f, 0.19f, 0.24f, 0.19f);

        /// <summary>The plate round the waist and the hips, in the root frame: from the hip pivots up to the chest plate's
        /// lower edge, its front a centimetre and a half before the belly.</summary>
        public static readonly AvatarBox WaistPlate = new AvatarBox(0f, 1.08f, 0.005f, 0.49f, 0.32f, 0.32f);

        /// <summary>The plate over the shin, in its knee's frame (the Ressaw suit's shin since #3337, now every set's).</summary>
        public static readonly AvatarBox ShinPlate = new AvatarBox(0f, -0.20f, 0f, 0.25f, 0.36f, 0.25f);

        // --- #3885 ("NPCs have no elbows and no knees, do they?" — they had, but the joints did not read): a joint block at each
        // knee and elbow, centred on the pivot and turning with the lower limb. Half a centimetre proud of the limb boxes at
        // its sides (and of the shin in front), so it reads as a joint and fills the notch a bend opens; inside every plate
        // and pad, and within the limb's paint box, so a painting runs on over it unchanged. ---

        /// <summary>The knee's joint block, in its knee's frame.</summary>
        public static readonly AvatarBox KneeJoint = new AvatarBox(0f, 0f, 0f, 0.23f, 0.13f, 0.21f);

        /// <summary>The elbow's joint block, in its elbow's frame.</summary>
        public static readonly AvatarBox ElbowJoint = new AvatarBox(0f, 0f, 0f, 0.17f, 0.12f, 0.17f);

        /// <summary>Whether a face of <paramref name="a"/> and a face of <paramref name="b"/> (both in one frame, unturned)
        /// lie in one plane — closer than <paramref name="clearance"/> — facing the same way, and overlap there: the two
        /// would flicker against each other.</summary>
        public static bool ShareAFace(AvatarBox a, AvatarBox b, float clearance = Clearance)
        {
            bool overX = Overlap(a.Left, a.Right, b.Left, b.Right);
            bool overY = Overlap(a.Bottom, a.Top, b.Bottom, b.Top);
            bool overZ = Overlap(a.Back, a.Front, b.Back, b.Front);
            return (overY && overZ && (Near(a.Left, b.Left, clearance) || Near(a.Right, b.Right, clearance)))
                || (overX && overZ && (Near(a.Bottom, b.Bottom, clearance) || Near(a.Top, b.Top, clearance)))
                || (overX && overY && (Near(a.Back, b.Back, clearance) || Near(a.Front, b.Front, clearance)));
        }

        private static bool Near(float a, float b, float clearance) => Math.Abs(a - b) < clearance - 0.0001f;

        private static bool Overlap(float a0, float a1, float b0, float b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > 0.0005f;
    }
}
