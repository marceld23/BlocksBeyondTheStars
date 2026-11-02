// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet globe mesh (#3127): a UV sphere of radius 0.5 — the size of Unity's primitive sphere — worn by the
    /// planets in the flight view, their atmosphere haze and cloud shell, and the bodies in the surface sky. Built
    /// Unity-free so the headless suite pins its layout; the client turns it into one shared mesh (<c>FxKit.GlobeMesh</c>).
    /// <para><b>Why not Unity's primitive.</b> The built-in sphere has 24 longitude segments and a triangle fan at each
    /// pole whose UVs take a different slice of the equirect planet map in every triangle: the map sheared into straight
    /// wedges where the camera looked at a pole, and the 24 segments showed as facets on the haze rim. Here every pole
    /// triangle has a pole vertex of its own, placed at the centre longitude of its segment, so the map runs into the
    /// pole without a shear; 48 × 24 segments keep the rim round.</para>
    /// <para><b>Mapping.</b> v runs from 0 at the south pole (y = −0.5) to 1 at the north pole, linear in latitude like
    /// the map's rows (<see cref="PlanetMapData"/>: row 0 is the south edge). u runs around the equator: u = 0 at +X,
    /// and the azimuth <c>atan2(z, x)</c> grows with u (<see cref="SeamAzimuthDeg"/>, <see cref="UvDirection"/> — the
    /// terms <see cref="PlanetSpin"/> takes). Seen from outside with north up, that is left to right in Unity's
    /// left-handed frame, so the map reads unmirrored, east to the right. The seam column exists twice (u = 0 and u = 1)
    /// so the texture wraps without a smear.</para>
    /// <para><b>Winding.</b> Unity draws the front of a triangle (a, b, c) on the side <c>Cross(b − a, c − a)</c> points
    /// to (its corners run clockwise on screen). Every triangle here faces outward, like the primitive's, so the
    /// back-face-culling shaders (SkyBodyPhase, PlanetLimb) draw the side they always drew.</para>
    /// </summary>
    public sealed class GlobeMeshData
    {
        /// <summary>Segments around the equator of the shared globe.</summary>
        public const int DefaultLonSegments = 48;

        /// <summary>Segments from pole to pole of the shared globe.</summary>
        public const int DefaultLatSegments = 24;

        /// <summary>The globe's radius — Unity's primitive sphere's, so every scale tuned for it still fits.</summary>
        public const float Radius = 0.5f;

        /// <summary>The azimuth (degrees, <c>atan2(z, x)</c>) of longitude u = 0.</summary>
        public const double SeamAzimuthDeg = 0.0;

        /// <summary>+1: the azimuth grows with u (<c>azimuth = seam + direction · u · 360</c>).</summary>
        public const double UvDirection = 1.0;

        private GlobeMeshData(int lonSegments, int latSegments)
        {
            LonSegments = lonSegments;
            LatSegments = latSegments;
            int vertices = VertexCountOf(lonSegments, latSegments);
            Positions = new float[vertices * 3];
            Normals = new float[vertices * 3];
            Uvs = new float[vertices * 2];
            Triangles = new int[TriangleCountOf(lonSegments, latSegments) * 3];
        }

        public int LonSegments { get; }

        public int LatSegments { get; }

        /// <summary>x, y, z per vertex.</summary>
        public float[] Positions { get; }

        /// <summary>x, y, z per vertex (unit length: the position × 2).</summary>
        public float[] Normals { get; }

        /// <summary>u, v per vertex.</summary>
        public float[] Uvs { get; }

        /// <summary>Three vertex indices per triangle, front faces outward (see the class remarks).</summary>
        public int[] Triangles { get; }

        public int VertexCount => Positions.Length / 3;

        /// <summary>One pole vertex per segment at each pole, <paramref name="latSegments"/> − 1 rings of
        /// <paramref name="lonSegments"/> + 1 vertices (the seam column twice).</summary>
        public static int VertexCountOf(int lonSegments, int latSegments) => 2 * lonSegments + (latSegments - 1) * (lonSegments + 1);

        /// <summary>One triangle per segment at each pole, two per quad between the rings.</summary>
        public static int TriangleCountOf(int lonSegments, int latSegments) => 2 * lonSegments * (latSegments - 1);

        /// <summary>The south-pole vertex of segment <paramref name="segment"/> (0 .. LonSegments − 1).</summary>
        public int SouthPole(int segment) => segment;

        /// <summary>The vertex of ring <paramref name="ring"/> (1 .. LatSegments − 1, from the south) at column
        /// <paramref name="column"/> (0 .. LonSegments; the last column repeats the first at u = 1).</summary>
        public int RingVertex(int ring, int column) => LonSegments + (ring - 1) * (LonSegments + 1) + column;

        /// <summary>The north-pole vertex of segment <paramref name="segment"/> (0 .. LonSegments − 1).</summary>
        public int NorthPole(int segment) => LonSegments + (LatSegments - 1) * (LonSegments + 1) + segment;

        /// <summary>Builds a globe of <paramref name="lonSegments"/> × <paramref name="latSegments"/> (at least 3 × 2).</summary>
        public static GlobeMeshData Build(int lonSegments = DefaultLonSegments, int latSegments = DefaultLatSegments)
        {
            var globe = new GlobeMeshData(Math.Max(3, lonSegments), Math.Max(2, latSegments));
            globe.FillVertices();
            globe.FillTriangles();
            return globe;
        }

        private void FillVertices()
        {
            for (int s = 0; s < LonSegments; s++)
            {
                double u = (s + 0.5) / LonSegments; // the segment's centre: no shear across the pole triangle
                Set(SouthPole(s), u, u, 0.0);
                Set(NorthPole(s), u, u, 1.0);
            }

            for (int ring = 1; ring < LatSegments; ring++)
            {
                double v = ring / (double)LatSegments;
                for (int column = 0; column <= LonSegments; column++)
                {
                    double u = column / (double)LonSegments;
                    double angleU = column == LonSegments ? 0.0 : u; // the seam column sits exactly on column 0
                    Set(RingVertex(ring, column), u, angleU, v);
                }
            }
        }

        private void Set(int index, double u, double angleU, double v)
        {
            double nx = 0.0, ny = v <= 0.0 ? -1.0 : 1.0, nz = 0.0;
            if (v > 0.0 && v < 1.0)
            {
                double az = (SeamAzimuthDeg / 360.0 + UvDirection * angleU) * 2.0 * Math.PI;
                double el = (v - 0.5) * Math.PI;
                nx = Math.Cos(el) * Math.Cos(az);
                ny = Math.Sin(el);
                nz = Math.Cos(el) * Math.Sin(az);
            }

            int p = index * 3;
            Normals[p] = (float)nx;
            Normals[p + 1] = (float)ny;
            Normals[p + 2] = (float)nz;
            Positions[p] = (float)(nx * Radius);
            Positions[p + 1] = (float)(ny * Radius);
            Positions[p + 2] = (float)(nz * Radius);
            Uvs[index * 2] = (float)u;
            Uvs[index * 2 + 1] = (float)v;
        }

        private void FillTriangles()
        {
            int t = 0;
            int top = LatSegments - 1;
            for (int s = 0; s < LonSegments; s++)
            {
                t = Triangle(t, SouthPole(s), RingVertex(1, s), RingVertex(1, s + 1));
                t = Triangle(t, RingVertex(top, s), NorthPole(s), RingVertex(top, s + 1));
            }

            for (int ring = 1; ring < top; ring++)
            {
                for (int s = 0; s < LonSegments; s++)
                {
                    int a = RingVertex(ring, s), b = RingVertex(ring, s + 1);
                    int c = RingVertex(ring + 1, s), d = RingVertex(ring + 1, s + 1);
                    t = Triangle(t, a, c, b);
                    t = Triangle(t, b, c, d);
                }
            }
        }

        private int Triangle(int t, int a, int b, int c)
        {
            Triangles[t] = a;
            Triangles[t + 1] = b;
            Triangles[t + 2] = c;
            return t + 3;
        }
    }
}
