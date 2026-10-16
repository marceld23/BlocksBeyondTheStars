// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cut-out cards a torch or a lantern is drawn from (#2683, "when it is placed on a wall it should hang on the
    /// wall!"), in the cell's local frame (0..1 on every axis), for each mount <see cref="PropShapes.LightMountOf(int, bool)"/>
    /// can read from a cell:
    /// <list type="bullet">
    /// <item><b>standing</b> (and every unstamped light): the three-plane rosette the mesher's cross-billboard path has
    /// always drawn a light with — the mesher keeps drawing that one itself, this copy is for the placement ghost;</item>
    /// <item><b>a wall torch</b>: a slightly shorter rosette whose stick foot touches the wall and which leans away from it,
    /// flame in front, held by a small wooden plate and arm;</item>
    /// <item><b>a wall lantern</b>: the lantern hanging from a short iron arm on a wall plate;</item>
    /// <item><b>a hanging lantern</b>: the lantern near the top of its cell on a short chain up to the ceiling.</item>
    /// </list>
    /// Each <see cref="Card"/> is one quad with the slice of the light's own tile it shows; the mesher draws it two-sided
    /// with the light's flame/dye mode and its rigid cutout code, the placement ghost draws the same cards translucently —
    /// one list, so the preview and the placed light cannot drift apart. The numbers follow the tile pictures: the torch
    /// stick is the middle tenth of its tile, the lantern's iron frame the dark band a little above its middle.
    /// Unity-free (System.Numerics) so the headless suite pins the shapes.
    /// </summary>
    public static class LightMountGeometry
    {
        /// <summary>Height of a standing torch / lantern rosette — the mesher's cross-billboard numbers.</summary>
        public const float TorchHeight = 0.8f, LanternHeight = 0.55f;

        /// <summary>Half the width of a standing rosette plane (the cross-billboard's clamped minimum).</summary>
        public const float StandingHalfWidth = 0.18f;

        /// <summary>The wall torch: a little shorter than the standing one, its foot <see cref="WallTorchFootY"/> above
        /// the cell floor and <see cref="WallTorchGap"/> out from the wall, leaning <see cref="WallTorchTiltDegrees"/>
        /// away from it.</summary>
        public const float WallTorchHeight = 0.68f, WallTorchFootY = 0.2f, WallTorchGap = 0.06f, WallTorchTiltDegrees = 28f;

        /// <summary>A lantern on a wall or on a chain is a little smaller than a standing one.</summary>
        public const float MountedLanternHeight = 0.5f;

        /// <summary>The wall lantern's body axis lies this far out from the wall; its bottom sits at
        /// <see cref="WallLanternBottomY"/>, under an arm at <see cref="WallLanternArmY"/>.</summary>
        public const float WallLanternGap = 0.21f, WallLanternBottomY = 0.28f, WallLanternArmY = 0.85f;

        /// <summary>The hanging lantern's bottom; the chain runs from its handle up to the ceiling.</summary>
        public const float HangingLanternBottomY = 0.3f;

        /// <summary>How far a plate against a wall or ceiling stands off that face (no z-fighting with it).</summary>
        public const float PlateStandOff = 0.012f;

        // Tile slices (fractions of the light's own tile). The torch stick: the middle tenth, below the flame. The
        // lantern's dark iron: a uniform band of the frame a little above the tile's middle.
        private const float StickU0 = 29f / 64f, StickU1 = 35f / 64f, StickV0 = 8f / 64f, StickV1 = 30f / 64f;
        private const float IronU0 = 22f / 64f, IronU1 = 42f / 64f, IronV0 = 36.3f / 64f, IronV1 = 38.7f / 64f;

        /// <summary>The topmost opaque row of the lantern tile (its handle), as a fraction of the tile height.</summary>
        public const float LanternHandleV = 61f / 64f;

        private static readonly float[] PlaneAngles = { 0f, 60f, 120f };

        /// <summary>One quad: corners <see cref="A"/> (bottom left), <see cref="B"/> (bottom right), <see cref="C"/> (top
        /// right), <see cref="D"/> (top left) in the cell's local frame, showing the tile slice
        /// [<see cref="U0"/>, <see cref="U1"/>] × [<see cref="V0"/>, <see cref="V1"/>] (A = (U0, V0), C = (U1, V1)).</summary>
        public readonly struct Card
        {
            public Card(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float v0, float u1, float v1)
            {
                A = a;
                B = b;
                C = c;
                D = d;
                U0 = u0;
                V0 = v0;
                U1 = u1;
                V1 = v1;
            }

            public Vector3 A { get; }

            public Vector3 B { get; }

            public Vector3 C { get; }

            public Vector3 D { get; }

            public float U0 { get; }

            public float V0 { get; }

            public float U1 { get; }

            public float V1 { get; }
        }

        // Built once (the mesher calls this from its worker threads): [lantern ? 1 : 0][mount + 1], mount -1..5.
        private static readonly Card[][] Cache = BuildAll();

        /// <summary>
        /// The cards of a torch (<paramref name="lantern"/> false) or lantern mounted as <paramref name="mount"/> — the
        /// up-face <see cref="PropShapes.LightMountOf(int, bool)"/> reads: 0 standing, 1 hanging from the ceiling (a lantern;
        /// a torch never hangs and stands instead), 2..5 on the wall that lies opposite that face, anything else (-1 =
        /// unstamped) standing.
        /// </summary>
        public static IReadOnlyList<Card> Build(bool lantern, int mount)
        {
            int m = mount >= -1 && mount <= 5 ? mount : -1;
            return Cache[(lantern ? 7 : 0) + m + 1];
        }

        private static Card[][] BuildAll()
        {
            var all = new Card[14][];
            for (int l = 0; l < 2; l++)
            {
                for (int m = -1; m <= 5; m++)
                {
                    all[(l * 7) + m + 1] = Compute(l == 1, m).ToArray();
                }
            }

            return all;
        }

        private static List<Card> Compute(bool lantern, int mount)
        {
            var cards = new List<Card>(8);
            if (mount >= 2 && mount <= 5)
            {
                if (lantern)
                {
                    WallLantern(cards, mount);
                }
                else
                {
                    WallTorch(cards, mount);
                }
            }
            else if (mount == 1 && lantern)
            {
                HangingLantern(cards);
            }
            else
            {
                // Standing — the cross-billboard the mesher draws: three planes through the cell centre, 60° apart.
                Rosette(cards, new Vector3(0.5f, 0f, 0.5f), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ,
                    lantern ? LanternHeight : TorchHeight, StandingHalfWidth);
            }

            return cards;
        }

        /// <summary>The wall's outward normal for a wall mount (pointing from the wall into the light's cell), the
        /// horizontal direction along that wall, and the point on the wall face at the cell's floor, centred.</summary>
        private static void WallFrame(int mount, out Vector3 normal, out Vector3 along, out Vector3 wallFoot)
        {
            var (x, _, z) = ShapeCode.FaceDirection(mount);
            normal = new Vector3(x, 0f, z);
            along = new Vector3(-z, 0f, x);
            wallFoot = new Vector3(0.5f, 0f, 0.5f) - (normal * 0.5f);
        }

        private static void WallTorch(List<Card> cards, int mount)
        {
            WallFrame(mount, out var n, out var t, out var wall);
            float tilt = WallTorchTiltDegrees * (MathF.PI / 180f);
            float sin = MathF.Sin(tilt), cos = MathF.Cos(tilt);
            var axis = (Vector3.UnitY * cos) + (n * sin);   // the stick leans away from the wall
            var outward = (n * cos) - (Vector3.UnitY * sin); // across the stick, in the plane of wall normal and up
            var foot = wall + (n * WallTorchGap) + (Vector3.UnitY * WallTorchFootY);
            float half = StandingHalfWidth * (WallTorchHeight / TorchHeight); // the same tile proportions, just smaller
            Rosette(cards, foot, axis, t, outward, WallTorchHeight, half);

            // The holder: a small wooden plate on the wall and an arm from it to the stick.
            var plate = wall + (n * PlateStandOff) + (Vector3.UnitY * (WallTorchFootY + 0.07f));
            cards.Add(Quad(plate, t * 0.045f, Vector3.UnitY * 0.075f, StickU0, StickV0, StickU1, StickV1));
            float armY = WallTorchFootY + 0.1f;
            float reach = WallTorchGap + (0.1f * (sin / cos)) + 0.01f; // the stick's axis at the arm's height
            var armLo = wall + (n * PlateStandOff) + (Vector3.UnitY * (armY - 0.025f));
            var armHi = wall + (n * reach) + (Vector3.UnitY * (armY - 0.025f));
            cards.Add(new Card(armLo, armHi, armHi + (Vector3.UnitY * 0.05f), armLo + (Vector3.UnitY * 0.05f),
                StickU0, StickV0, StickU1, StickV1));
        }

        private static void WallLantern(List<Card> cards, int mount)
        {
            WallFrame(mount, out var n, out var t, out var wall);
            float half = StandingHalfWidth * (MountedLanternHeight / LanternHeight);
            var bottom = wall + (n * WallLanternGap) + (Vector3.UnitY * WallLanternBottomY);
            Rosette(cards, bottom, Vector3.UnitY, t, n, MountedLanternHeight, half);

            // The iron bracket: a plate on the wall, an arm out over the lantern, and a short rod down to its handle.
            var plate = wall + (n * PlateStandOff) + (Vector3.UnitY * WallLanternArmY);
            cards.Add(Quad(plate, t * 0.05f, Vector3.UnitY * 0.07f, IronU0, IronV0, IronU1, IronV1));
            var armLo = wall + (n * PlateStandOff) + (Vector3.UnitY * (WallLanternArmY - 0.02f));
            var armHi = wall + (n * (WallLanternGap + 0.03f)) + (Vector3.UnitY * (WallLanternArmY - 0.02f));
            cards.Add(new Card(armLo, armHi, armHi + (Vector3.UnitY * 0.04f), armLo + (Vector3.UnitY * 0.04f),
                IronU0, IronV0, IronU1, IronV1));
            float handle = WallLanternBottomY + (MountedLanternHeight * LanternHandleV);
            var rodFoot = wall + (n * WallLanternGap) + (Vector3.UnitY * (handle - 0.02f));
            Rod(cards, rodFoot, t, n, WallLanternArmY - (handle - 0.02f));
        }

        private static void HangingLantern(List<Card> cards)
        {
            float half = StandingHalfWidth * (MountedLanternHeight / LanternHeight);
            var bottom = new Vector3(0.5f, HangingLanternBottomY, 0.5f);
            Rosette(cards, bottom, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, MountedLanternHeight, half);

            // A chain from the handle to the ceiling, and a little plate where it is fixed.
            float handle = HangingLanternBottomY + (MountedLanternHeight * LanternHandleV);
            var chainFoot = new Vector3(0.5f, handle - 0.02f, 0.5f);
            Rod(cards, chainFoot, Vector3.UnitX, Vector3.UnitZ, 1f - chainFoot.Y);
            var plate = new Vector3(0.5f, 1f - PlateStandOff, 0.5f);
            cards.Add(Quad(plate, Vector3.UnitX * 0.05f, Vector3.UnitZ * 0.05f, IronU0, IronV0, IronU1, IronV1));
        }

        /// <summary>Three planes through <paramref name="foot"/> along <paramref name="axis"/>, 60° apart about it, the first
        /// along <paramref name="first"/> (the second and third turn towards <paramref name="second"/>), each showing the
        /// whole tile.</summary>
        private static void Rosette(List<Card> cards, Vector3 foot, Vector3 axis, Vector3 first, Vector3 second, float height, float half)
        {
            var up = axis * height;
            foreach (float deg in PlaneAngles)
            {
                float rad = deg * (MathF.PI / 180f);
                var w = ((first * MathF.Cos(rad)) + (second * MathF.Sin(rad))) * half;
                var a = foot - w;
                var b = foot + w;
                cards.Add(new Card(a, b, b + up, a + up, 0f, 0f, 1f, 1f));
            }
        }

        /// <summary>A thin vertical rod (two crossed quads) from <paramref name="foot"/> up by <paramref name="length"/>,
        /// in the lantern's iron.</summary>
        private static void Rod(List<Card> cards, Vector3 foot, Vector3 across1, Vector3 across2, float length)
        {
            var up = Vector3.UnitY * length;
            foreach (var across in new[] { across1, across2 })
            {
                var a = foot - (across * 0.015f);
                var b = foot + (across * 0.015f);
                cards.Add(new Card(a, b, b + up, a + up, IronU0, IronV0, IronU1, IronV1));
            }
        }

        /// <summary>A rectangle centred on <paramref name="centre"/> spanning ±<paramref name="halfU"/> and
        /// ±<paramref name="halfV"/>.</summary>
        private static Card Quad(Vector3 centre, Vector3 halfU, Vector3 halfV, float u0, float v0, float u1, float v1)
            => new Card(centre - halfU - halfV, centre + halfU - halfV, centre + halfU + halfV, centre - halfU + halfV, u0, v0, u1, v1);
    }
}
