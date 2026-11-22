// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The forms of the ship-station decor (<c>StationDecorView</c>): the
    /// cockpit console, the small terminal of the lab and the ship console, and the base and cap of the medbay's heal
    /// capsule, each a handful of chunky boxes in sizes of 1/8 and 1/16 of a block instead of one flat placeholder box.
    /// <para>
    /// Boxes live in the decor's frame: x and z run −0.5..0.5 across the cell above the station's marker block, y = 0 is
    /// the marker's top face. The stations carry no orientation, so the pilot is taken to stand on the +Z side — the side
    /// the screens have always faced (they lean back, top toward −Z): decks step down toward +Z and the screens face it.
    /// A tilt turns a box about its own centre around X the way Unity's <c>Quaternion.Euler(tilt, 0, 0)</c> does.
    /// </para>
    /// Unity-free so the headless suite pins that every box stays in its cell, rests on what is under it and that the
    /// screens face the pilot; the client only rounds the boxes and turns them into meshes.
    /// </summary>
    public static class StationDecorForms
    {
        /// <summary>Which station a form belongs to.</summary>
        public enum Kind
        {
            Cockpit,
            Terminal,
            Medbay,
        }

        /// <summary>The surface a box wears: the dark metal housing, the control deck or the glowing screen.</summary>
        public enum Surface
        {
            Housing,
            Deck,
            Screen,
        }

        private const float Eighth = 0.125f, Sixteenth = 0.0625f;

        /// <summary>How far a tilted frame sinks into what it stands on, so no gap shows under its front edge.</summary>
        private const float Sink = 1f / 128f;

        /// <summary>One box of a form: centre and full size in the decor frame, its tilt about X in degrees, its surface,
        /// whether the client may round its edges. How its texture is laid on follows from the surface (see
        /// <see cref="FlatCube"/>).</summary>
        public readonly struct FormBox
        {
            public FormBox(Surface surface, Vector3 centre, Vector3 size, float tiltX, bool rounded)
            {
                Surface = surface;
                Centre = centre;
                Size = size;
                TiltX = tiltX;
                Rounded = rounded;
            }

            public Surface Surface { get; }

            public Vector3 Centre { get; }

            public Vector3 Size { get; }

            public float TiltX { get; }

            public bool Rounded { get; }
        }

        private static FormBox Housing(float cx, float cy, float cz, float sx, float sy, float sz, float tilt = 0f)
            => new FormBox(Surface.Housing, new Vector3(cx, cy, cz), new Vector3(sx, sy, sz), tilt, true);

        private static FormBox Deck(float cx, float cy, float cz, float sx, float sy, float sz)
            => new FormBox(Surface.Deck, new Vector3(cx, cy, cz), new Vector3(sx, sy, sz), 0f, true);

        /// <summary>The centre height that lets a box of <paramref name="size"/>, tilted by <paramref name="tilt"/>, stand
        /// with its lowest edge on <paramref name="restY"/> (sunk by <see cref="Sink"/>).</summary>
        public static float RestOn(float restY, Vector3 size, float tilt)
        {
            double t = Math.Abs(tilt) * Math.PI / 180.0;
            return restY - Sink + (float)((size.Y * 0.5 * Math.Cos(t)) + (size.Z * 0.5 * Math.Sin(t)));
        }

        /// <summary>A screen frame standing on <paramref name="restY"/> at depth <paramref name="z"/>, and the screen on its
        /// front face (the face toward +Z), tilted with it.</summary>
        private static void Framed(Vector3 frameSize, float z, float restY, float tilt, float screenW, float screenH, out FormBox frame, out FormBox screen)
        {
            frame = Housing(0f, RestOn(restY, frameSize, tilt), z, frameSize.X, frameSize.Y, frameSize.Z, tilt);
            const float screenD = 1f / 64f;
            var onFace = Tilt(new Vector3(0f, 0f, (frameSize.Z * 0.5f) + (screenD * 0.5f) + 0.0005f), tilt);
            screen = new FormBox(Surface.Screen, frame.Centre + onFace, new Vector3(screenW, screenH, screenD), tilt, false);
        }

        private static readonly FormBox[] CockpitBoxes = BuildCockpit();
        private static readonly FormBox[] TerminalBoxes = BuildTerminal();
        private static readonly FormBox[] MedbayBoxes = BuildMedbay();

        /// <summary>The cockpit console: a recessed base, the body, a riser at the back, two side cheeks, a low deck at the
        /// front and a high deck on the riser (it steps down toward the pilot), and the tilted screen frame at the back.
        /// The decks are 2/8 deep and 6/8 wide, so the deck tile's 2/8 buttons show whole (3 × 1 per deck).
        /// The holographic map floats above it as before (not a box).</summary>
        private static FormBox[] BuildCockpit()
        {
            const float tilt = -18f; // today's cockpit screen tilt
            Framed(new Vector3(6 * Eighth, 3 * Eighth, Sixteenth), -2 * Eighth, 4 * Eighth, tilt, 5 * Eighth, 0.28125f, out var frame, out var screen);
            return new[]
            {
                Housing(0f, Sixteenth, -Sixteenth, 5 * Eighth, Eighth, 4 * Eighth),                 // base, y 0..1/8
                Housing(0f, 2 * Eighth, 0f, 6 * Eighth, 2 * Eighth, 6 * Eighth),                    // body, y 1/8..3/8
                Housing(0f, 3.5f * Eighth, -Eighth, 6 * Eighth, Eighth, 4 * Eighth),                // riser, y 3/8..4/8, z -3/8..1/8
                Housing(-0.40625f, 0.34375f, -Sixteenth, Sixteenth, 7 * Sixteenth, 5 * Eighth),     // left cheek
                Housing(0.40625f, 0.34375f, -Sixteenth, Sixteenth, 7 * Sixteenth, 5 * Eighth),      // right cheek
                frame,
                Deck(0f, 0.40625f, 2 * Eighth, 6 * Eighth, Sixteenth, 2 * Eighth),                  // low deck, front, z 1/8..3/8
                Deck(0f, 0.53125f, 0f, 6 * Eighth, Sixteenth, 2 * Eighth),                          // high deck, on the riser, z -1/8..1/8
                screen,
            };
        }

        /// <summary>The terminal of the lab and the ship console: the cockpit's console in small — a body, a little deck
        /// at the front, a block at the back carrying the tilted screen frame.</summary>
        private static FormBox[] BuildTerminal()
        {
            const float tilt = -12f; // today's terminal screen tilt
            Framed(new Vector3(4 * Eighth, 5 * Sixteenth, Sixteenth), -Eighth, 3 * Eighth, tilt, 7 * Sixteenth, 2 * Eighth, out var frame, out var screen);
            return new[]
            {
                Housing(0f, Eighth, 0f, 4 * Eighth, 2 * Eighth, 3 * Eighth),            // body, y 0..2/8
                Housing(0f, 2.5f * Eighth, -Eighth, 4 * Eighth, Eighth, Eighth),        // back block, y 2/8..3/8
                frame,
                Deck(0f, 0.28125f, Sixteenth, 4 * Eighth, Sixteenth, 2 * Eighth),       // deck, y 2/8..5/16, z -1/16..3/16
                screen,
            };
        }

        /// <summary>The heal capsule's device: a plinth and a collar round its foot, a lip and a cap on its top. The
        /// capsule itself (radius 0.275, y 0..1.5) stays the client's translucent glow.</summary>
        private static FormBox[] BuildMedbay()
        {
            return new[]
            {
                Housing(0f, Sixteenth, 0f, 6 * Eighth, Eighth, 6 * Eighth),                    // plinth, y 0..1/8
                Housing(-0.3125f, 1.5f * Eighth, 0f, Sixteenth, Eighth, 11 * Sixteenth),       // collar, four bars round the capsule
                Housing(0.3125f, 1.5f * Eighth, 0f, Sixteenth, Eighth, 11 * Sixteenth),
                Housing(0f, 1.5f * Eighth, -0.3125f, 9 * Sixteenth, Eighth, Sixteenth),
                Housing(0f, 1.5f * Eighth, 0.3125f, 9 * Sixteenth, Eighth, Sixteenth),
                Housing(0f, 1.40625f, 0f, 5 * Eighth, Sixteenth, 5 * Eighth),                  // cap lip, y 1.375..1.4375
                Housing(0f, 1.484375f, 0f, 4 * Eighth, 3f / 32f, 4 * Eighth),                  // cap, y 1.4375..1.53125
            };
        }

        /// <summary>The boxes of a station's form.</summary>
        public static FormBox[] BoxesOf(Kind kind) => kind switch
        {
            Kind.Cockpit => CockpitBoxes,
            Kind.Terminal => TerminalBoxes,
            _ => MedbayBoxes,
        };

        /// <summary>Turns <paramref name="v"/> about X by <paramref name="degrees"/> (Unity's <c>Quaternion.Euler(d, 0, 0)</c>).</summary>
        public static Vector3 Tilt(Vector3 v, float degrees)
        {
            if (degrees == 0f)
            {
                return v;
            }

            double t = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(t), s = (float)Math.Sin(t);
            return new Vector3(v.X, (v.Y * c) - (v.Z * s), (v.Y * s) + (v.Z * c));
        }

        /// <summary>A point of the box's unit cube (−0.5..0.5) in the decor frame.</summary>
        public static Vector3 PointToCell(in FormBox box, Vector3 unit) => box.Centre + Tilt(unit * box.Size, box.TiltX);

        /// <summary>A normal of the box's unit cube in the decor frame. A rounded cube stores its normals pre-scaled for the
        /// unit cube (the normal times the size, normalised); dividing by the size turns them back, and leaves a face
        /// normal as it is.</summary>
        public static Vector3 NormalToCell(in FormBox box, Vector3 unitNormal)
            => Vector3.Normalize(Tilt(Vector3.Normalize(unitNormal / box.Size), box.TiltX));

        /// <summary>True for the face of <paramref name="box"/> that shows the screen picture: a screen box's front face
        /// (outward normal +Z in the box's own frame, <paramref name="face"/> 0..5 in <see cref="TiledCube"/>'s order).</summary>
        public static bool IsScreenFace(in FormBox box, TiledCube.Data cube, int face)
            => box.Surface == Surface.Screen && cube.Normals[(face * 4 * 3) + 2] > 0.5f;

        /// <summary>
        /// The sharp unit cube of a box (the corner layout of <see cref="TiledCube"/>, which the client's rounding reads)
        /// with its texture laid on per face. On each face u runs to the right and v up as seen from outside (the top as
        /// seen by the pilot from +Z). Every face is mapped BY SIZE: a face w × h blocks big shows w × h tiles (64 px per
        /// block, the blocks' own texel density, the tile repeating), starting at the face's lower left edge — so a box
        /// sized in eighths shows whole plates and buttons of the device tiles. Only a screen's front face is mapped
        /// whole: the complete tile, 0..1 on both axes (<see cref="IsScreenFace"/>).
        /// </summary>
        public static TiledCube.Data FlatCube(in FormBox box)
        {
            var cube = TiledCube.Build(1, 1, 1, pinV: false);
            var uvs = new float[cube.Uvs.Length];
            for (int face = 0; face < 6; face++)
            {
                float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    int i = (face * 4) + k;
                    var unit = new Vector3(cube.Positions[i * 3], cube.Positions[(i * 3) + 1], cube.Positions[(i * 3) + 2]);
                    var n = new Vector3(cube.Normals[i * 3], cube.Normals[(i * 3) + 1], cube.Normals[(i * 3) + 2]);
                    FaceUv(unit * box.Size, n, out float u, out float v);
                    uvs[i * 2] = u;
                    uvs[(i * 2) + 1] = v;
                    uMin = Math.Min(uMin, u);
                    uMax = Math.Max(uMax, u);
                    vMin = Math.Min(vMin, v);
                    vMax = Math.Max(vMax, v);
                }

                bool whole = IsScreenFace(box, cube, face);
                float uScale = whole ? 1f / Math.Max(1e-6f, uMax - uMin) : 1f;
                float vScale = whole ? 1f / Math.Max(1e-6f, vMax - vMin) : 1f;
                for (int k = 0; k < 4; k++)
                {
                    int i = (face * 4) + k;
                    uvs[i * 2] = (uvs[i * 2] - uMin) * uScale;
                    uvs[(i * 2) + 1] = (uvs[(i * 2) + 1] - vMin) * vScale;
                }
            }

            return new TiledCube.Data(cube.Positions, cube.Normals, uvs, cube.Triangles);
        }

        /// <summary>The face coordinates (metres) of <paramref name="r"/> on the face with normal <paramref name="n"/>:
        /// u to the right and v up as seen from outside (Unity's left-handed frame: looking along −n with +Y up, right
        /// is +Y × −n). Top: seen from +Z, so u = −x and v = −z.</summary>
        private static void FaceUv(Vector3 r, Vector3 n, out float u, out float v)
        {
            if (Math.Abs(n.X) > 0.5f)
            {
                u = n.X > 0f ? r.Z : -r.Z;
                v = r.Y;
            }
            else if (Math.Abs(n.Z) > 0.5f)
            {
                u = n.Z > 0f ? -r.X : r.X;
                v = r.Y;
            }
            else
            {
                u = n.Y > 0f ? -r.X : r.X;
                v = n.Y > 0f ? -r.Z : r.Z;
            }
        }

        /// <summary>
        /// The screen tile made ready to glow (Unity-free so it is tested): its brightness as grey, stretched so the
        /// brightest texel is white and lifted to at least <paramref name="floor"/>, with alpha 0 everywhere — the model
        /// shader adds alpha-0 texels as glow. The station's screen colour is multiplied over it, so one tile serves the
        /// cyan cockpit, the green lab and the cyan console. Null for a tile of the wrong size.
        /// </summary>
        public static byte[]? GlowScreenPixels(byte[]? tile, float floor)
        {
            if (tile == null || tile.Length == 0 || tile.Length % 4 != 0)
            {
                return null;
            }

            float max = 1f;
            for (int o = 0; o < tile.Length; o += 4)
            {
                max = Math.Max(max, Luma(tile, o));
            }

            floor = Math.Max(0f, Math.Min(1f, floor));
            var glow = new byte[tile.Length];
            for (int o = 0; o < tile.Length; o += 4)
            {
                float g = floor + ((1f - floor) * Luma(tile, o) / max);
                byte b = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(g * 255f)));
                glow[o] = b;
                glow[o + 1] = b;
                glow[o + 2] = b;
                glow[o + 3] = 0;
            }

            return glow;
        }

        private static float Luma(byte[] t, int o) => (0.2126f * t[o]) + (0.7152f * t[o + 1]) + (0.0722f * t[o + 2]);
    }
}
