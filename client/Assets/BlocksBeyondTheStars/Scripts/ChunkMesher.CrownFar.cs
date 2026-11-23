// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The far stage of needle and palm crowns (ChunkMesher.CrownForms.cs for the near forms). With staged crowns a
    /// needle or frond cell that has a tree axis is drawn near the camera as its crown form (a bough, the spire, a frond,
    /// the palm's heart); beyond the clusters' range the leaf block's shell would turn the conifer's cone into a stack of
    /// blocks where the range ends, and on Low and Potato — which draw no clusters — it would never be a cone at all. So
    /// such a cell meshes the far form of its crown form instead of its shell faces
    /// (<see cref="CrownFarForms"/>: a closed wedge, a frustum, two-sided leaf cards), turned like the near form, with the
    /// crown cutout code, so the block shader paints and fades it exactly as it painted and faded the shell (the near
    /// clusters dither in over it, its shadow hands over at the same distance).
    /// <para>
    /// Display only: the cell keeps every collider face its shell had (the same rule), and stays the leaf block for mining,
    /// light and the server (tree crowns have no collider today, #2184, so the faces it records are none). A cell inside
    /// the crown gets its far form too: the near clusters draw it, and the gaps between the outer boughs show it. A shell
    /// cell beside a formed one draws its face toward it (<see cref="CrownFarFormedAt"/>), because that neighbour no longer
    /// fills its cell. The same decision in every chunk: it rests on the blocks around the cell only.
    /// </para>
    /// </summary>
    public static partial class ChunkMesher
    {
        [System.ThreadStatic]
        private static List<BlockShapeGeometry.Face> _crownFarScratch;

        /// <summary>The far form's faces in the cell's 0..1 box, turned about the cell's middle by <paramref name="yaw"/>
        /// degrees (the near form's turn, CrownClusters): a per-thread scratch list, valid until the next call.</summary>
        private static List<BlockShapeGeometry.Face> CrownFarFaces(int form, int variant, float yaw)
        {
            var list = _crownFarScratch ??= new List<BlockShapeGeometry.Face>();
            list.Clear();
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var half = new Vector3(0.5f, 0.5f, 0.5f);
            foreach (var q in CrownFarForms.Of(form, variant))
            {
                list.Add(new BlockShapeGeometry.Face(
                    Turned(q.A, turn, half), Turned(q.B, turn, half), Turned(q.C, turn, half), Turned(q.D, turn, half),
                    new Vector2(q.UvA.X, q.UvA.Y), new Vector2(q.UvB.X, q.UvB.Y), new Vector2(q.UvC.X, q.UvC.Y), new Vector2(q.UvD.X, q.UvD.Y))
                    .Finish((ShapePart)q.Part));
            }

            return list;
        }

        private static Vector3 Turned(System.Numerics.Vector3 p, Quaternion turn, Vector3 half)
            => (turn * (new Vector3(p.X, p.Y, p.Z) - half)) + half;

        /// <summary>The turn of a crown form's instance (CrownClusters.Rebuild): an outward form by its yaw, a form on the
        /// tree's axis (the spire, the heart) by a quarter turn from the cell's hash.</summary>
        private static float CrownFarYaw(int form, float yaw, int hash)
            => form == CrownFarForms.NeedleTip || form == CrownFarForms.FrondRosette ? 90f * (((uint)hash >> 9) & 3u) : yaw;

        /// <summary>True when the crown cell at (nx, ny, nz) holding <paramref name="nb"/> draws a far form instead of its
        /// shell: a needle or frond cell with a tree axis (<see cref="TreeAxis"/>, the rule of <see cref="CrownShapeOf"/>).
        /// A shell cell beside it must draw the face between them. Only shell cells ask (a formed cell never reaches the
        /// face loop), so only leaf crowns touching a conifer or palm and needle or frond cells too far from a trunk pay
        /// for the axis search.</summary>
        private static bool CrownFarFormedAt(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, BlockId nb, int nx, int ny, int nz)
        {
            if (!GameLook.CrownFormsOn || nb.Value == 0 || (nb.Value != traits.NeedlesOf() && nb.Value != traits.FrondsOf()))
            {
                return false;
            }

            return TreeAxis(traits, worldBlock, nx, ny, nz, out _, out _);
        }
    }
}
