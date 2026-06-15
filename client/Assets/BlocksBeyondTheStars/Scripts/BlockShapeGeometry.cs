using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Pure geometry for the non-cube building shapes (sphere, dome, pyramid, ramp, …). Produces a list of
    /// outward-facing polygons in the unit cell [0,1]^3 (cell-local), rotated by the placement yaw. The mesher
    /// (<see cref="ChunkMesher"/>) turns these into vertices/triangles + the usual per-vertex attribute streams,
    /// so a shaped block textures, tints and lights exactly like a cube. Winding matches the cube faces
    /// (CCW seen from outside: normal = cross(B-A, D-A)), so the back-face-culling atlas shader shows them.
    /// </summary>
    internal static class BlockShapeGeometry
    {
        /// <summary>One outward-facing polygon — a quad (4 corners) or a triangle (3). Corners are CCW from
        /// outside; the mesher emits tris (0,1,2)[,(0,2,3)].</summary>
        internal readonly struct Face
        {
            public readonly Vector3 A, B, C, D;
            public readonly bool IsQuad;

            public Face(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a; B = b; C = c; D = default; IsQuad = false;
            }

            public Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                A = a; B = b; C = c; D = d; IsQuad = true;
            }
        }

        /// <summary>Builds the polygons for a shape index (see <see cref="BlockShape"/>), rotated by a yaw
        /// orientation (0..3 quarter-turns about the cell's vertical centre). Returns null for cube/unknown.</summary>
        public static List<Face> Build(int shapeIndex, int orientation)
        {
            var faces = new List<Face>();
            switch ((BlockShape)shapeIndex)
            {
                case BlockShape.Slab: Box(faces, 0f, 0f, 0f, 1f, 0.5f, 1f); break;
                case BlockShape.Pyramid: Pyramid(faces); break;
                case BlockShape.Dome: Dome(faces); break;
                case BlockShape.Sphere: Sphere(faces); break;
                case BlockShape.Ramp: Ramp(faces); break;
                case BlockShape.Stairs: Stairs(faces); break;
                case BlockShape.Cone: Cone(faces); break;
                case BlockShape.Cylinder: Cylinder(faces); break;
                default: return null; // Cube / unknown → no custom geometry
            }

            if ((orientation & 3) != 0)
            {
                for (int i = 0; i < faces.Count; i++)
                {
                    var f = faces[i];
                    faces[i] = f.IsQuad
                        ? new Face(Yaw(f.A, orientation), Yaw(f.B, orientation), Yaw(f.C, orientation), Yaw(f.D, orientation))
                        : new Face(Yaw(f.A, orientation), Yaw(f.B, orientation), Yaw(f.C, orientation));
                }
            }

            return faces;
        }

        // --- Primitive builders (unit cell, y up, centre at 0.5,*,0.5) ---

        /// <summary>An axis-aligned box [x0,x1]×[y0,y1]×[z0,z1] with all six faces wound outward.</summary>
        private static void Box(List<Face> f, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            f.Add(new Face(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0))); // +Y top
            f.Add(new Face(new(x0, y0, z1), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1))); // -Y bottom
            f.Add(new Face(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1))); // +X
            f.Add(new Face(new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0))); // -X
            f.Add(new Face(new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), new(x0, y0, z1))); // +Z
            f.Add(new Face(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0))); // -Z
        }

        private static void Pyramid(List<Face> f)
        {
            var apex = new Vector3(0.5f, 1f, 0.5f);
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1))); // -Y base
            // Four triangular sides over the base edges (each verified outward-facing).
            f.Add(new Face(new(1, 0, 0), apex, new(1, 0, 1)));
            f.Add(new Face(new(1, 0, 1), apex, new(0, 0, 1)));
            f.Add(new Face(new(0, 0, 1), apex, new(0, 0, 0)));
            f.Add(new Face(new(0, 0, 0), apex, new(1, 0, 0)));
        }

        private static void Ramp(List<Face> f)
        {
            // Wedge rising toward +Z: full floor, a vertical back wall at z=1, the sloped top, two side triangles.
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1))); // -Y floor
            f.Add(new Face(new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1))); // +Z back wall
            f.Add(new Face(new(0, 0, 0), new(0, 1, 1), new(1, 1, 1), new(1, 0, 0))); // slope (up + toward -Z)
            f.Add(new Face(new(0, 0, 0), new(0, 0, 1), new(0, 1, 1)));               // -X side triangle
            f.Add(new Face(new(1, 0, 0), new(1, 1, 1), new(1, 0, 1)));               // +X side triangle
        }

        private static void Stairs(List<Face> f)
        {
            // Two steps rising toward +Z, built as explicit faces (no overlapping interior faces / z-fighting).
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)));         // -Y floor
            f.Add(new Face(new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1)));         // +Z back wall (full height)
            f.Add(new Face(new(0, 0, 0), new(0, 0.5f, 0), new(1, 0.5f, 0), new(1, 0, 0)));   // -Z front riser (step 1)
            f.Add(new Face(new(0, 0.5f, 0), new(0, 0.5f, 0.5f), new(1, 0.5f, 0.5f), new(1, 0.5f, 0))); // +Y tread (step 1)
            f.Add(new Face(new(0, 0.5f, 0.5f), new(0, 1, 0.5f), new(1, 1, 0.5f), new(1, 0.5f, 0.5f))); // -Z riser (step 2)
            f.Add(new Face(new(0, 1, 0.5f), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0.5f)));   // +Y tread (step 2)
            // -X side (two rectangles forming the L silhouette)
            f.Add(new Face(new(0, 0, 0.5f), new(0, 0.5f, 0.5f), new(0, 0.5f, 0), new(0, 0, 0)));
            f.Add(new Face(new(0, 0, 1), new(0, 1, 1), new(0, 1, 0.5f), new(0, 0, 0.5f)));
            // +X side
            f.Add(new Face(new(1, 0, 0), new(1, 0.5f, 0), new(1, 0.5f, 0.5f), new(1, 0, 0.5f)));
            f.Add(new Face(new(1, 0, 0.5f), new(1, 1, 0.5f), new(1, 1, 1), new(1, 0, 1)));
        }

        private const float Cx = 0.5f, Cz = 0.5f, R = 0.5f;

        private static void Cylinder(List<Face> f)
        {
            var profile = new[] { (R, 0f), (R, 1f) };
            Lathe(f, profile, 12, capBottom: true, capTop: true);
        }

        private static void Cone(List<Face> f)
        {
            var profile = new[] { (R, 0f), (0f, 1f) };
            Lathe(f, profile, 12, capBottom: true, capTop: false);
        }

        private static void Dome(List<Face> f)
        {
            // Half-sphere stretched to fill the cell height: radius R in XZ, apex at y=1.
            const int rings = 4;
            var profile = new (float R, float Y)[rings + 1];
            for (int k = 0; k <= rings; k++)
            {
                float phi = (k / (float)rings) * (Mathf.PI * 0.5f); // 0 (equator) .. 90° (pole)
                profile[k] = (R * Mathf.Cos(phi), Mathf.Sin(phi));
            }

            Lathe(f, profile, 12, capBottom: true, capTop: false);
        }

        private static void Sphere(List<Face> f)
        {
            const int rings = 6;
            var profile = new (float R, float Y)[rings + 1];
            for (int k = 0; k <= rings; k++)
            {
                float phi = -Mathf.PI * 0.5f + (k / (float)rings) * Mathf.PI; // -90° .. +90°
                profile[k] = (R * Mathf.Cos(phi), 0.5f + R * Mathf.Sin(phi));
            }

            Lathe(f, profile, 10, capBottom: false, capTop: false);
        }

        /// <summary>Surface of revolution about the cell's vertical centre, from a bottom→top (radius, y) profile.
        /// A pole ring (radius 0) collapses its band to triangles. Optional flat caps close the ends.</summary>
        private static void Lathe(List<Face> f, (float R, float Y)[] profile, int seg, bool capBottom, bool capTop)
        {
            Vector3 P(float r, float y, int s)
            {
                float a = (s / (float)seg) * Mathf.PI * 2f;
                return new Vector3(Cx + r * Mathf.Cos(a), y, Cz + r * Mathf.Sin(a));
            }

            for (int j = 0; j < profile.Length - 1; j++)
            {
                var (rL, yL) = profile[j];
                var (rU, yU) = profile[j + 1];
                for (int s = 0; s < seg; s++)
                {
                    int s1 = s + 1;
                    if (rL <= 1e-4f) // bottom pole → triangle up to the ring above
                    {
                        f.Add(new Face(new Vector3(Cx, yL, Cz), P(rU, yU, s), P(rU, yU, s1)));
                    }
                    else if (rU <= 1e-4f) // top pole → triangle from the ring below
                    {
                        f.Add(new Face(P(rL, yL, s), new Vector3(Cx, yU, Cz), P(rL, yL, s1)));
                    }
                    else
                    {
                        f.Add(new Face(P(rL, yL, s), P(rU, yU, s), P(rU, yU, s1), P(rL, yL, s1)));
                    }
                }
            }

            if (capBottom)
            {
                var (r0, y0) = profile[0];
                var c = new Vector3(Cx, y0, Cz);
                for (int s = 0; s < seg; s++)
                {
                    f.Add(new Face(c, P(r0, y0, s), P(r0, y0, s + 1))); // -Y
                }
            }

            if (capTop)
            {
                var (rT, yT) = profile[profile.Length - 1];
                var c = new Vector3(Cx, yT, Cz);
                for (int s = 0; s < seg; s++)
                {
                    f.Add(new Face(c, P(rT, yT, s + 1), P(rT, yT, s))); // +Y
                }
            }
        }

        /// <summary>Rotates a cell-local point by <paramref name="quarterTurns"/> × 90° about the vertical
        /// centre axis (0.5, *, 0.5).</summary>
        private static Vector3 Yaw(Vector3 p, int quarterTurns)
        {
            float x = p.x, z = p.z;
            for (int i = 0; i < (quarterTurns & 3); i++)
            {
                float dx = x - 0.5f, dz = z - 0.5f;
                x = 0.5f - dz;
                z = 0.5f + dx;
            }

            return new Vector3(x, p.y, z);
        }
    }
}
