// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One polygon of a device form with soft edges, turned into the cell (unit coordinates, <see cref="Facet"/>): a flat face,
    /// a chamfer strip, a corner triangle or a strip's end cap. <see cref="Dir"/> is the cube-face direction (the mesher's
    /// order, turned frame) whose tile, mapping, emission and light the polygon takes — for a flat face the way it looks, for a
    /// strip or corner the REFERENCE face picked by <see cref="DeviceFormSoft.ReferenceOf"/>. <see cref="Side"/> is that
    /// face's side in the form's frame (the key of its slot's tiles), <see cref="Slot"/> the box's slot, <see cref="Box"/> the
    /// index of the box in <see cref="DeviceForm.Boxes"/>. <see cref="Boundary"/> = a flat face on the cell's own face in its
    /// direction, left out where the neighbour seals that cube face. <see cref="Dirs"/> = a bit per direction (1 &lt;&lt; dir)
    /// the polygon looks between — one bit for a flat face, two for a strip, three for a corner (the shade blends them).
    /// </summary>
    public readonly struct DeviceFormFacet
    {
        public DeviceFormFacet(ShapeFacet facet, int dir, FaceSide side, int slot, int box, bool boundary, int dirs)
        {
            Facet = facet;
            Dir = dir;
            Side = side;
            Slot = slot;
            Box = box;
            Boundary = boundary;
            Dirs = dirs;
        }

        public ShapeFacet Facet { get; }

        public int Dir { get; }

        public FaceSide Side { get; }

        public int Slot { get; }

        public int Box { get; }

        public bool Boundary { get; }

        public int Dirs { get; }

        public bool Flat => (Dirs & (Dirs - 1)) == 0;

        /// <summary>Triangles the polygon costs.</summary>
        public int Triangles => Facet.IsQuad ? 2 : 1;
    }

    /// <summary>
    /// Device forms: soft edges on a device form: <see cref="ShapeChamfer"/> on the union of its boxes,
    /// turned into the cell. Each box goes in as a slot of its own, so no face is merged across two boxes and a fit picture
    /// stays one picture per box face, as today. The chamfer's border rule is the culling's friend: edges on the cell's border
    /// stay hard, so a face the neighbour seals (<see cref="DeviceFormFacet.Boundary"/>) has no strip against it, and every
    /// polygon stays inside the cell.
    /// <para>
    /// <b>Strip rule.</b> A strip or corner takes the tile and mapping of ONE of the faces it lies between, fixed in the
    /// form's own frame: a vertical face (a side or the front) before the top or bottom; among vertical faces the front (−Z)
    /// first, else the lowest direction index. Its light is that face's, its shade the mean of the faces it lies between.
    /// </para>
    /// </summary>
    public static class DeviceFormSoft
    {
        /// <summary>A form keeps hard edges when the chamfer would more than double its triangles (in any turn).</summary>
        public const float MaxTriangleRatio = 2f;

        /// <summary>The soft faces for each of the four turns, or null when <paramref name="width"/> is 0 or the form would
        /// pass the cap — then the form draws today's faces (<see cref="DeviceFormFaces.Build"/>).</summary>
        public static List<DeviceFormFacet>[]? BuildAllTurns(DeviceForm form, float width, float maxRatio = MaxTriangleRatio)
        {
            if (width <= 0f || form.Boxes.Count == 0)
            {
                return null;
            }

            var turns = new List<DeviceFormFacet>[4];
            for (int yaw = 0; yaw < 4; yaw++)
            {
                turns[yaw] = Build(form, yaw, width);
                if (Triangles(turns[yaw]) > maxRatio * 2 * DeviceFormFaces.Build(form, yaw).Count)
                {
                    return null;
                }
            }

            return turns;
        }

        /// <summary>The soft faces of the form turned by <paramref name="yaw"/> quarter turns (no cap).</summary>
        public static List<DeviceFormFacet> Build(DeviceForm form, int yaw, float width)
        {
            var boxes = new List<SlotBox>(form.Boxes.Count);
            for (int i = 0; i < form.Boxes.Count; i++)
            {
                var b = DeviceFormFaces.Turn(form.Boxes[i], yaw);
                boxes.Add(new SlotBox(new CustomShape.Box(b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1, DeviceForms.Grid), i + 1));
            }

            var result = new List<DeviceFormFacet>();
            foreach (var facet in ShapeChamfer.Build(boxes, width))
            {
                int box = facet.Slot - 1;
                int dirs = DirsOf(facet);
                int dir = ReferenceOf(dirs, yaw);
                int formDir = FormDir(dir, yaw);
                bool boundary = (dirs & (dirs - 1)) == 0 && OnBorder(facet, dir);
                result.Add(new DeviceFormFacet(facet, dir, DeviceFormFaces.SideOf(formDir), form.Boxes[box].Slot, box, boundary, dirs));
            }

            return result;
        }

        public static int Triangles(List<DeviceFormFacet> facets)
        {
            int n = 0;
            foreach (var f in facets)
            {
                n += f.Triangles;
            }

            return n;
        }

        /// <summary>The direction (turned frame) whose face a polygon looking between <paramref name="dirs"/> takes after —
        /// see the strip rule in the class summary.</summary>
        public static int ReferenceOf(int dirs, int yaw)
        {
            int best = -1, bestRank = int.MaxValue;
            for (int dir = 0; dir < 6; dir++)
            {
                if ((dirs & (1 << dir)) == 0)
                {
                    continue;
                }

                int formDir = FormDir(dir, yaw);
                int rank = formDir == 5 ? 0 : formDir >= 2 ? formDir : 10 + formDir; // front, other sides, then top/bottom
                if (rank < bestRank)
                {
                    best = dir;
                    bestRank = rank;
                }
            }

            return best;
        }

        /// <summary>The form-frame direction that <paramref name="yaw"/> quarter turns take onto <paramref name="dir"/>.</summary>
        public static int FormDir(int dir, int yaw)
        {
            for (int formDir = 0; formDir < 6; formDir++)
            {
                if (DeviceFormFaces.TurnDir(formDir, yaw) == dir)
                {
                    return formDir;
                }
            }

            return dir;
        }

        /// <summary>A bit per axis direction the polygon's normal has a share of.</summary>
        private static int DirsOf(ShapeFacet f)
        {
            var n = f.Normal;
            int dirs = 0;
            const float eps = 1e-6f;
            dirs |= n.Y > eps ? 1 << 0 : n.Y < -eps ? 1 << 1 : 0;
            dirs |= n.X > eps ? 1 << 2 : n.X < -eps ? 1 << 3 : 0;
            dirs |= n.Z > eps ? 1 << 4 : n.Z < -eps ? 1 << 5 : 0;
            return dirs;
        }

        /// <summary>True when the flat polygon lies in the cell's own face toward <paramref name="dir"/>.</summary>
        private static bool OnBorder(ShapeFacet f, int dir)
        {
            float plane = (dir & 1) == 0 ? 1f : 0f;
            float Coord(System.Numerics.Vector3 p) => dir < 2 ? p.Y : dir < 4 ? p.X : p.Z;
            return Math.Abs(Coord(f.A) - plane) < 1e-5f;
        }
    }
}
