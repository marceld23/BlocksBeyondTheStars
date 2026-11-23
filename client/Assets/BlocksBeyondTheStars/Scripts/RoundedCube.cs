// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A creature's box part with rounded edges. The same unit cube and the same texture coordinates as
    /// <see cref="TiledCube"/> (so hides, baked coats, pinned parts and placements land exactly as on the sharp
    /// cube), but every face is a small grid whose rim bends round the edge: a part reads as a soft brick, and two
    /// parts that touch show a groove between them — the look of the concept pictures' animals.
    /// <para>
    /// The part is a unit cube scaled by its size, so the mesh is built for one size's proportions (a radius that is
    /// even in the world needs different amounts on each axis of the unit cube) — every animal of a species has the
    /// same proportions and shares it. Kept free of everything but vectors and lists, so it compiles outside Unity.
    /// </para>
    /// </summary>
    public static class RoundedCube
    {
        /// <summary>The radius of a part's edges in metres: a share of its thinnest side.</summary>
        public static float Radius(Vector3 size, float share)
            => Mathf.Max(0f, Mathf.Min(share, 0.45f)) * Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z)));

        /// <summary>The point and normal on the rounded box for a point <paramref name="p"/> on the sharp box's
        /// surface (both in metres about the box's centre; <paramref name="half"/> = half its size).</summary>
        public static Vector3 Round(Vector3 p, Vector3 half, float radius, out Vector3 normal)
        {
            var inner = new Vector3(
                Mathf.Clamp(p.x, -(half.x - radius), half.x - radius),
                Mathf.Clamp(p.y, -(half.y - radius), half.y - radius),
                Mathf.Clamp(p.z, -(half.z - radius), half.z - radius));
            var out3 = p - inner;
            float m = out3.magnitude;
            if (m < 1e-6f)
            {
                normal = new Vector3(0f, 1f, 0f);
                return p;
            }

            normal = out3 * (1f / m);
            return inner + (normal * radius);
        }

        /// <summary>Where a face's grid lines run along an edge of <paramref name="length"/> metres (as shares 0..1):
        /// the two ends, the lines where the flat middle begins, and one line inside each bend (at half its angle).</summary>
        private static float[] Lines(float length, float radius)
        {
            float bend = radius / length, mid = radius * 0.5858f / length;
            return new[] { 0f, mid, bend, 1f - bend, 1f - mid, 1f };
        }

        /// <summary>Builds the rounded cube of a part of <paramref name="size"/> metres into the lists, in the unit
        /// cube's space. <paramref name="flat"/> is the sharp tiled cube it replaces (its corners give the faces and
        /// their texture coordinates).</summary>
        public static void Build(Vector3 size, float radius, TiledCube.Data flat, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            var half = size * 0.5f;
            for (int face = 0; face < 6; face++)
            {
                int v = face * 4;
                var p00 = Corner(flat, v);
                var edgeU = Corner(flat, v + 1) - p00;
                var edgeV = Corner(flat, v + 2) - p00;
                var n = new Vector3(flat.Normals[v * 3], flat.Normals[(v * 3) + 1], flat.Normals[(v * 3) + 2]);
                var uv00 = new Vector2(flat.Uvs[v * 2], flat.Uvs[(v * 2) + 1]);
                var uvU = new Vector2(flat.Uvs[(v + 1) * 2] - uv00.x, flat.Uvs[((v + 1) * 2) + 1] - uv00.y);
                var uvV = new Vector2(flat.Uvs[(v + 2) * 2] - uv00.x, flat.Uvs[((v + 2) * 2) + 1] - uv00.y);
                float[] us = Lines(Scale(edgeU, size).magnitude, radius);
                float[] vs = Lines(Scale(edgeV, size).magnitude, radius);
                int first = verts.Count;
                for (int j = 0; j < vs.Length; j++)
                {
                    for (int i = 0; i < us.Length; i++)
                    {
                        var unit = p00 + (edgeU * us[i]) + (edgeV * vs[j]);
                        var at = Round(Scale(unit, size), half, radius, out Vector3 normal);
                        verts.Add(new Vector3(at.x / size.x, at.y / size.y, at.z / size.z));
                        normals.Add(Scale(normal, size).normalized); // the scaled cube turns it back into `normal`
                        uvs.Add(new Vector2(uv00.x + (uvU.x * us[i]) + (uvV.x * vs[j]), uv00.y + (uvU.y * us[i]) + (uvV.y * vs[j])));
                    }
                }

                bool uvIsN = Vector3.Dot(Vector3.Cross(edgeU, edgeV), n) > 0f;
                for (int j = 0; j + 1 < vs.Length; j++)
                {
                    for (int i = 0; i + 1 < us.Length; i++)
                    {
                        int a = first + (j * us.Length) + i, b = a + 1, c = a + us.Length, d = c + 1;
                        if (uvIsN)
                        {
                            tris.Add(a); tris.Add(b); tris.Add(d);
                            tris.Add(a); tris.Add(d); tris.Add(c);
                        }
                        else
                        {
                            tris.Add(a); tris.Add(c); tris.Add(d);
                            tris.Add(a); tris.Add(d); tris.Add(b);
                        }
                    }
                }
            }
        }

        private static Vector3 Corner(TiledCube.Data d, int v)
            => new Vector3(d.Positions[v * 3], d.Positions[(v * 3) + 1], d.Positions[(v * 3) + 2]);

        private static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    }
}
