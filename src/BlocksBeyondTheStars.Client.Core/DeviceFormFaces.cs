// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One face of a device form, turned into the cell (eighths): it looks along <see cref="Dir"/> (the chunk mesher's face
    /// order 0 = +Y, 1 = −Y, 2 = +X, 3 = −X, 4 = +Z, 5 = −Z) and covers the flat box <c>X0..X1 × Y0..Y1 × Z0..Z1</c>, whose
    /// extent along <see cref="Dir"/>'s axis is zero (the face's plane). <see cref="Side"/> is the side it looks to in the
    /// FORM's frame (the key of its slot's tiles); <see cref="Boundary"/> = it lies on the cell's own face in its direction,
    /// so a neighbour that seals that cube face hides it.
    /// </summary>
    public readonly struct DeviceFormQuad
    {
        public DeviceFormQuad(int dir, FaceSide side, int slot, bool boundary, int x0, int y0, int z0, int x1, int y1, int z1)
        {
            Dir = dir;
            Side = side;
            Slot = slot;
            Boundary = boundary;
            X0 = x0;
            Y0 = y0;
            Z0 = z0;
            X1 = x1;
            Y1 = y1;
            Z1 = z1;
        }

        public int Dir { get; }
        public FaceSide Side { get; }
        public int Slot { get; }
        public bool Boundary { get; }
        public int X0 { get; }
        public int Y0 { get; }
        public int Z0 { get; }
        public int X1 { get; }
        public int Y1 { get; }
        public int Z1 { get; }
    }

    /// <summary>
    /// Device forms: the faces of a form, turned so its front looks the way the cube shows its front picture.
    /// <para>
    /// <b>Orientation.</b> The form's front is its −Z side. A quarter turn maps a point (x, z) to (8 − z, x) — the turn of
    /// the built-in shapes (<c>BlockShapeGeometry.Yaw</c>) — so −Z goes to +X, +X to +Z, +Z to −X and −X to −Z. The cube
    /// path of the mesher puts a device's front picture on face <c>front</c> (2..5: the face stored in the cell's descriptor,
    /// <see cref="CubeFacing.FrontOf"/>, else <see cref="CubeFacing.DeriveFront"/>), so the turn for a front is
    /// −Z (5) → 0, +X (2) → 1, +Z (4) → 2, −X (3) → 3 (<see cref="YawForFront"/>).
    /// </para>
    /// Faces fully covered by a neighbouring box of the same form (a column's top under its cap) are left out.
    /// </summary>
    public static class DeviceFormFaces
    {
        private const int G = DeviceForms.Grid;

        /// <summary>The direction a face index looks along after one quarter turn (see the class summary).</summary>
        private static readonly int[] TurnOnce = { 0, 1, 4, 5, 3, 2 };

        /// <summary>The quarter turns that put the form's front (−Z) on cube face <paramref name="frontFace"/> (2..5); 0 for
        /// anything else (a device without a front keeps the form unturned).</summary>
        public static int YawForFront(int frontFace) => frontFace switch
        {
            2 => 1,
            4 => 2,
            3 => 3,
            _ => 0,
        };

        /// <summary>The direction face index <paramref name="dir"/> looks along after <paramref name="yaw"/> quarter turns.</summary>
        public static int TurnDir(int dir, int yaw)
        {
            for (int i = 0; i < (yaw & 3); i++)
            {
                dir = TurnOnce[dir];
            }

            return dir;
        }

        /// <summary>A box after <paramref name="yaw"/> quarter turns about the cell's vertical centre line.</summary>
        public static DeviceFormBox Turn(DeviceFormBox b, int yaw)
        {
            for (int i = 0; i < (yaw & 3); i++)
            {
                b = new DeviceFormBox(G - b.Z1, b.Y0, b.X0, G - b.Z0, b.Y1, b.X1, b.Slot);
            }

            return b;
        }

        /// <summary>The form-frame side a form-frame direction is: up = top, down = bottom, −Z = front, else side.</summary>
        public static FaceSide SideOf(int formDir) => formDir switch
        {
            0 => FaceSide.Top,
            1 => FaceSide.Bottom,
            5 => FaceSide.Front,
            _ => FaceSide.Side,
        };

        /// <summary>Every visible face of <paramref name="form"/> turned by <paramref name="yaw"/> quarter turns.</summary>
        public static List<DeviceFormQuad> Build(DeviceForm form, int yaw)
        {
            var turned = new DeviceFormBox[form.Boxes.Count];
            for (int i = 0; i < turned.Length; i++)
            {
                turned[i] = Turn(form.Boxes[i], yaw);
            }

            var quads = new List<DeviceFormQuad>(turned.Length * 6);
            for (int i = 0; i < turned.Length; i++)
            {
                for (int formDir = 0; formDir < 6; formDir++)
                {
                    int dir = TurnDir(formDir, yaw);
                    var q = FaceOf(turned[i], dir, SideOf(formDir));
                    if (!CoveredByAnother(turned, i, q))
                    {
                        quads.Add(q);
                    }
                }
            }

            return quads;
        }

        private static DeviceFormQuad FaceOf(DeviceFormBox b, int dir, FaceSide side)
        {
            switch (dir)
            {
                case 0: return new DeviceFormQuad(dir, side, b.Slot, b.Y1 == G, b.X0, b.Y1, b.Z0, b.X1, b.Y1, b.Z1);
                case 1: return new DeviceFormQuad(dir, side, b.Slot, b.Y0 == 0, b.X0, b.Y0, b.Z0, b.X1, b.Y0, b.Z1);
                case 2: return new DeviceFormQuad(dir, side, b.Slot, b.X1 == G, b.X1, b.Y0, b.Z0, b.X1, b.Y1, b.Z1);
                case 3: return new DeviceFormQuad(dir, side, b.Slot, b.X0 == 0, b.X0, b.Y0, b.Z0, b.X0, b.Y1, b.Z1);
                case 4: return new DeviceFormQuad(dir, side, b.Slot, b.Z1 == G, b.X0, b.Y0, b.Z1, b.X1, b.Y1, b.Z1);
                default: return new DeviceFormQuad(dir, side, b.Slot, b.Z0 == 0, b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z0);
            }
        }

        /// <summary>True when another box sits flush against the face's plane on its outer side and covers all of it.</summary>
        private static bool CoveredByAnother(DeviceFormBox[] boxes, int self, DeviceFormQuad q)
        {
            for (int j = 0; j < boxes.Length; j++)
            {
                if (j == self)
                {
                    continue;
                }

                var c = boxes[j];
                bool covers = q.Dir switch
                {
                    0 => c.Y0 == q.Y0 && c.X0 <= q.X0 && c.X1 >= q.X1 && c.Z0 <= q.Z0 && c.Z1 >= q.Z1,
                    1 => c.Y1 == q.Y0 && c.X0 <= q.X0 && c.X1 >= q.X1 && c.Z0 <= q.Z0 && c.Z1 >= q.Z1,
                    2 => c.X0 == q.X0 && c.Y0 <= q.Y0 && c.Y1 >= q.Y1 && c.Z0 <= q.Z0 && c.Z1 >= q.Z1,
                    3 => c.X1 == q.X0 && c.Y0 <= q.Y0 && c.Y1 >= q.Y1 && c.Z0 <= q.Z0 && c.Z1 >= q.Z1,
                    4 => c.Z0 == q.Z0 && c.X0 <= q.X0 && c.X1 >= q.X1 && c.Y0 <= q.Y0 && c.Y1 >= q.Y1,
                    _ => c.Z1 == q.Z0 && c.X0 <= q.X0 && c.X1 >= q.X1 && c.Y0 <= q.Y0 && c.Y1 >= q.Y1,
                };
                if (covers)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
