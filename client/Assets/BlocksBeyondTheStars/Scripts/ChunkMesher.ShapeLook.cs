// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- Colour slots and soft edges of player forms
    //
    // #3941: AddShapedBlock draws a player form from its per-slot (and maybe chamfered) faces and collides with its
    // value-blind boxes (BlockShapeGeometry.ColliderFaces). These two helpers are the parts of that which live outside the
    // method: the collider pass for such a form, and the colour slot's vertex values (ShapeColourSlotRule, Client.Core).
    public static partial class ChunkMesher
    {
        /// <summary>Appends <paramref name="faces"/> to the collider streams only (positions as AddShapedBlock places them).</summary>
        private static void AddShapeColliderFaces(List<Vector3> colliderVerts, List<int> colliderTris, List<BlockShapeGeometry.Face> faces,
            Vector3 cell, float sizeXZ, float sizeY)
        {
            Vector3 Place(Vector3 v) => cell + (sizeXZ == 1f && sizeY == 1f
                ? v
                : new Vector3(0.5f + (v.x - 0.5f) * sizeXZ, v.y * sizeY, 0.5f + (v.z - 0.5f) * sizeXZ));

            foreach (var face in faces)
            {
                int b = colliderVerts.Count;
                colliderVerts.Add(Place(face.A));
                colliderVerts.Add(Place(face.B));
                colliderVerts.Add(Place(face.C));
                colliderTris.Add(b); colliderTris.Add(b + 1); colliderTris.Add(b + 2);
                if (face.IsQuad)
                {
                    colliderVerts.Add(Place(face.D));
                    colliderTris.Add(b); colliderTris.Add(b + 2); colliderTris.Add(b + 3);
                }
            }
        }

        /// <summary>Turns a face's vertex values into those of its colour slot — vertex colour = gloss,
        /// metal, shade, emission; <paramref name="leaf"/>.yzw = the tint; <paramref name="tintMode"/> = the tint mode
        /// before any animation code is added. The rule itself: <see cref="ShapeColourSlotRule"/>. Also for the editors'
        /// previews, so they colour a slot as the world does.</summary>
        internal static void ApplyColourSlot(int slot, ref Color col, ref Vector4 leaf, ref float tintMode)
        {
            var look = ShapeColourSlotRule.Apply(slot, new ShapeFaceLook
            {
                Gloss = col.r,
                Metal = col.g,
                Shade = col.b,
                Emission = col.a,
                Mode = tintMode,
                TintR = leaf.y,
                TintG = leaf.z,
                TintB = leaf.w,
            });
            col = new Color(Mathf.Clamp01(look.Gloss), Mathf.Clamp01(look.Metal), Mathf.Clamp01(look.Shade), Mathf.Clamp01(look.Emission));
            leaf = new Vector4(leaf.x, look.TintR, look.TintG, look.TintB);
            tintMode = look.Mode;
        }
    }
}
