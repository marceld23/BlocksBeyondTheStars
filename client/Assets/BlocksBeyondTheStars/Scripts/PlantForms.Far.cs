// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public static partial class PlantForms
    {
        /// <summary>
        /// Plant far pictures: the geometry of individual <paramref name="variant"/> of a species — the same
        /// buffers <see cref="Build"/> turns into its mesh, appended to the lists (which the caller clears). The far
        /// picture is baked from them (<see cref="PlantFarBake"/>, PlantBodies.FarPictures.cs), so the crossed picture
        /// beyond the bodies' range has the body's outline. <paramref name="radius"/> is what <see cref="Built.Radius"/>
        /// would be.
        /// </summary>
        public static void Geometry(int family, string key, bool tall, bool accent, long worldSeed, string location, int variant,
            List<Vector3> verts, List<Vector3> normals, List<Color> tones, List<Vector4> parts, List<int> tris, out float radius)
        {
            int from = verts.Count;
            Grow(family, key, tall, accent, worldSeed, location, variant, out _).CopyTo(verts, normals, tones, parts, tris);
            radius = 0.05f;
            for (int i = from; i < verts.Count; i++)
            {
                radius = Mathf.Max(radius, new Vector2(verts[i].x, verts[i].z).magnitude);
            }
        }
    }
}
