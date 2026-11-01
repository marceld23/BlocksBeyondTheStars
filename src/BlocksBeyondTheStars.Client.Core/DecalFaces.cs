// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a world cell is to a voxel-face decal (#3058).</summary>
    public enum DecalCell : byte
    {
        /// <summary>Lets the neighbouring cube's face show: air, an unloaded chunk, glass, water, plants, torches, a shaped
        /// block — the same set the chunk mesher draws a neighbour's face against.</summary>
        Open = 0,

        /// <summary>Seals its neighbours' faces but takes no mark itself (lava, oil, a cactus body).</summary>
        Closed = 1,

        /// <summary>A plain opaque cube a mark can lie on.</summary>
        Surface = 2,
    }

    /// <summary>One exposed cube face a decal lies on.</summary>
    public readonly struct DecalFace
    {
        public DecalFace(int x, int y, int z, int dir, int openEdges)
        {
            X = x;
            Y = y;
            Z = z;
            Dir = dir;
            OpenEdges = openEdges;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        /// <summary>The face's outward direction: 0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z.</summary>
        public int Dir { get; }

        /// <summary>The face's convex edges (the in-plane neighbour cell is open, so the mesher bevels that edge): bit 0 =
        /// the low end of the first tangent axis, bit 1 its high end, bits 2/3 the same for the second tangent axis
        /// (tangent axes of axis <c>a = Dir / 2</c> are <c>(a + 1) % 3</c> and <c>(a + 2) % 3</c>).</summary>
        public int OpenEdges { get; }
    }

    /// <summary>Where a decal gathers its faces: a centre, the mark's main direction and how far it reaches.</summary>
    public readonly struct DecalBox
    {
        public DecalBox(float centerX, float centerY, float centerZ, float normalX, float normalY, float normalZ,
            float radius, float depth, float minDot, float facingLift)
        {
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            NormalX = normalX;
            NormalY = normalY;
            NormalZ = normalZ;
            Radius = radius;
            Depth = depth;
            MinDot = minDot;
            FacingLift = facingLift;
        }

        public float CenterX { get; }

        public float CenterY { get; }

        public float CenterZ { get; }

        /// <summary>The mark's main direction (unit length): the struck face's normal, or up for a crater.</summary>
        public float NormalX { get; }

        public float NormalY { get; }

        public float NormalZ { get; }

        /// <summary>The mark's radius across its plane (metres): a face is taken when its nearest point lies this close.</summary>
        public float Radius { get; }

        /// <summary>How far off the mark's plane (along the main direction) a face may lie.</summary>
        public float Depth { get; }

        /// <summary>Faces whose normal · main direction is below this are skipped (cos 80° ≈ 0.17 for a small mark on one
        /// wall; below −1 takes every direction).</summary>
        public float MinDot { get; }

        /// <summary>Above zero: a face must also turn toward the point this far above the centre along the main direction
        /// — a blast scorches what faces it (the carved bowl, the rim), never a cave ceiling under it. Zero = off.</summary>
        public float FacingLift { get; }
    }

    /// <summary>
    /// Face collection of the voxel-face decals (#3058, "Details (High)"). A scorch mark or crater is not projected
    /// through the depth buffer: it is a small mesh laid on the visible faces of the solid cells inside the decal box —
    /// one quad per exposed cube face, a few millimetres proud of it. So a mark can never land on a creature or player,
    /// never slides with the camera, and needs neither a depth texture nor a stencil. This class finds those faces
    /// (Unity-free, so the rules are tested headless); the client builds the quads and their UVs.
    /// </summary>
    public static class DecalFaces
    {
        /// <summary>The largest reach of a box in cells from its centre cell — (2·4 + 1)³ = 729 cells at most.</summary>
        public const int MaxReach = 4;

        private static readonly int[] DirX = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] DirY = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] DirZ = { 0, 0, 0, 0, 1, -1 };

        [ThreadStatic] private static DecalCell[]? _scratch;

        /// <summary>How many cells the box reaches out from its centre cell for this radius and depth (capped at
        /// <see cref="MaxReach"/>).</summary>
        public static int Reach(float radius, float depth)
            => Math.Max(0, Math.Min(MaxReach, (int)Math.Ceiling(Math.Max(radius, depth))));

        /// <summary>The unit outward normal of face direction <paramref name="dir"/> (0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z).</summary>
        public static (int X, int Y, int Z) Normal(int dir) => (DirX[dir], DirY[dir], DirZ[dir]);

        /// <summary>
        /// Fills <paramref name="into"/> with the exposed faces of <see cref="DecalCell.Surface"/> cells in the box around
        /// <paramref name="box"/>'s centre: the neighbour in the face's direction is <see cref="DecalCell.Open"/>, the face
        /// passes the box's direction and facing rules, and its nearest point lies within the radius across the mark's
        /// plane and within the depth off it. <paramref name="cell"/> is asked once per cell of the box plus a one-cell
        /// margin. Returns the face count.
        /// </summary>
        public static int Collect(Func<int, int, int, DecalCell> cell, in DecalBox box, List<DecalFace> into)
        {
            into.Clear();
            int reach = Reach(box.Radius, box.Depth);
            int ox = Floor(box.CenterX) - reach - 1, oy = Floor(box.CenterY) - reach - 1, oz = Floor(box.CenterZ) - reach - 1;
            int n = 2 * reach + 3;
            var cells = Fill(cell, ox, oy, oz, n);
            for (int ix = 1; ix < n - 1; ix++)
            {
                for (int iy = 1; iy < n - 1; iy++)
                {
                    for (int iz = 1; iz < n - 1; iz++)
                    {
                        if (cells[(ix * n + iy) * n + iz] == DecalCell.Surface)
                        {
                            AddFaces(cells, n, ix, iy, iz, ox, oy, oz, box, into);
                        }
                    }
                }
            }

            return into.Count;
        }

        /// <summary>
        /// The four corners of <paramref name="face"/>'s quad as x,y,z triples in <paramref name="into"/> (12 floats),
        /// lifted <paramref name="lift"/> off the face and pulled in by <paramref name="inset"/> along every convex edge
        /// (where the mesher's bevel cuts the cube's edge away, a full-size quad would hang in the air). Ordered so the
        /// triangles (0, 1, 2) and (0, 2, 3) face outward: (c1 − c0) × (c2 − c0) points along the face normal, which is
        /// Unity's clockwise front face seen from outside.
        /// </summary>
        public static void Corners(in DecalFace face, float lift, float inset, float[] into)
        {
            int a = face.Dir / 2, a1 = (a + 1) % 3, a2 = (a + 2) % 3;
            bool positive = (face.Dir & 1) == 0;
            float plane = Coord(face, a) + (positive ? 1f : 0f) + (positive ? lift : -lift);
            float lo1 = Coord(face, a1) + ((face.OpenEdges & 1) != 0 ? inset : 0f);
            float hi1 = Coord(face, a1) + 1f - ((face.OpenEdges & 2) != 0 ? inset : 0f);
            float lo2 = Coord(face, a2) + ((face.OpenEdges & 4) != 0 ? inset : 0f);
            float hi2 = Coord(face, a2) + 1f - ((face.OpenEdges & 8) != 0 ? inset : 0f);

            // A positive face walks its square (lo,lo) (hi,lo) (hi,hi) (lo,hi) in (a1, a2) — the turn e_a1 × e_a2 = e_a is
            // its outward normal; a negative face walks the same square the other way round.
            Put(into, 0, a, a1, a2, plane, lo1, lo2);
            Put(into, 1, a, a1, a2, plane, positive ? hi1 : lo1, positive ? lo2 : hi2);
            Put(into, 2, a, a1, a2, plane, hi1, hi2);
            Put(into, 3, a, a1, a2, plane, positive ? lo1 : hi1, positive ? hi2 : lo2);
        }

        private static DecalCell[] Fill(Func<int, int, int, DecalCell> cell, int ox, int oy, int oz, int n)
        {
            int size = n * n * n;
            var cells = _scratch;
            if (cells == null || cells.Length < size)
            {
                cells = new DecalCell[(2 * MaxReach + 3) * (2 * MaxReach + 3) * (2 * MaxReach + 3)];
                _scratch = cells;
            }

            for (int ix = 0; ix < n; ix++)
            {
                for (int iy = 0; iy < n; iy++)
                {
                    for (int iz = 0; iz < n; iz++)
                    {
                        cells[(ix * n + iy) * n + iz] = cell(ox + ix, oy + iy, oz + iz);
                    }
                }
            }

            return cells;
        }

        private static void AddFaces(DecalCell[] cells, int n, int ix, int iy, int iz, int ox, int oy, int oz,
            in DecalBox box, List<DecalFace> into)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                if (cells[((ix + DirX[dir]) * n + iy + DirY[dir]) * n + iz + DirZ[dir]] != DecalCell.Open)
                {
                    continue; // hidden face: the mesher draws nothing here
                }

                float dot = DirX[dir] * box.NormalX + DirY[dir] * box.NormalY + DirZ[dir] * box.NormalZ;
                if (dot < box.MinDot)
                {
                    continue;
                }

                int x = ox + ix, y = oy + iy, z = oz + iz;
                if (!Reaches(x, y, z, dir, box))
                {
                    continue;
                }

                into.Add(new DecalFace(x, y, z, dir, OpenEdges(cells, n, ix, iy, iz, dir)));
            }
        }

        /// <summary>The radius, depth and facing rules for one face of cell (x, y, z).</summary>
        private static bool Reaches(int x, int y, int z, int dir, in DecalBox box)
        {
            // The face's nearest point to the centre: clamp the centre into the face's square (its own axis is fixed).
            float fx = x + (DirX[dir] > 0 ? 1f : 0f), fy = y + (DirY[dir] > 0 ? 1f : 0f), fz = z + (DirZ[dir] > 0 ? 1f : 0f);
            float qx = DirX[dir] != 0 ? fx : Clamp(box.CenterX, x, x + 1f);
            float qy = DirY[dir] != 0 ? fy : Clamp(box.CenterY, y, y + 1f);
            float qz = DirZ[dir] != 0 ? fz : Clamp(box.CenterZ, z, z + 1f);
            float rx = qx - box.CenterX, ry = qy - box.CenterY, rz = qz - box.CenterZ;
            float along = rx * box.NormalX + ry * box.NormalY + rz * box.NormalZ;
            float planar2 = rx * rx + ry * ry + rz * rz - along * along;
            if (Math.Abs(along) > box.Depth || planar2 > box.Radius * box.Radius)
            {
                return false;
            }

            if (box.FacingLift <= 0f)
            {
                return true;
            }

            // The face's centre must lie on the near side of its plane as seen from the lifted point.
            float cx = x + 0.5f + DirX[dir] * 0.5f, cy = y + 0.5f + DirY[dir] * 0.5f, cz = z + 0.5f + DirZ[dir] * 0.5f;
            float px = box.CenterX + box.NormalX * box.FacingLift - cx;
            float py = box.CenterY + box.NormalY * box.FacingLift - cy;
            float pz = box.CenterZ + box.NormalZ * box.FacingLift - cz;
            return DirX[dir] * px + DirY[dir] * py + DirZ[dir] * pz > 0f;
        }

        private static int OpenEdges(DecalCell[] cells, int n, int ix, int iy, int iz, int dir)
        {
            int a = dir / 2, mask = 0;
            for (int k = 0; k < 4; k++)
            {
                int axis = (a + 1 + k / 2) % 3, step = (k & 1) == 0 ? -1 : 1;
                int jx = ix + (axis == 0 ? step : 0), jy = iy + (axis == 1 ? step : 0), jz = iz + (axis == 2 ? step : 0);
                if (cells[(jx * n + jy) * n + jz] == DecalCell.Open)
                {
                    mask |= 1 << k;
                }
            }

            return mask;
        }

        private static int Coord(in DecalFace face, int axis) => axis == 0 ? face.X : axis == 1 ? face.Y : face.Z;

        private static void Put(float[] into, int corner, int a, int a1, int a2, float plane, float u, float v)
        {
            into[corner * 3 + a] = plane;
            into[corner * 3 + a1] = u;
            into[corner * 3 + a2] = v;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static int Floor(float v) => (int)Math.Floor(v);
    }
}
