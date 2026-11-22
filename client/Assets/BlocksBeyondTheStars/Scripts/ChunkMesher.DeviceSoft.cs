// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Soft edges on device forms. <see cref="Share"/> is the chamfer as a share of a micro cell (6 cm, never wider than the
    /// cube bevel). <see cref="Cap"/> is the triangle ratio above which a form keeps hard edges: 4.5, because at the rule
    /// of thumb of 2 (<see cref="DeviceFormSoft.MaxTriangleRatio"/>) eleven of the 28 forms stayed hard — all six tanks,
    /// the workbench, the rail stop — beside soft ones, which reads as a mistake; at 4.5 every form of the table is soft
    /// and the largest costs 228 triangles (the workbench), on Medium and High only. The tier rule is the player forms'
    /// (<see cref="BlockShapeGeometry.SoftTierAllows"/>: Medium and High on a desktop, never in the browser).
    /// </summary>
    internal static class LookDeviceSoft
    {
        public const float Share = ShapeChamfer.DefaultShare;

        /// <summary>High enough that every form of the table is soft.</summary>
        public const float Cap = 4.5f;

        public static float Width => ShapeChamfer.WidthFor(Share, DeviceForms.Grid, ChunkMesher.BevelAmount);

        /// <summary>True when the soft polygons are drawn now (the tier allows).</summary>
        public static bool Now => BlockShapeGeometry.SoftTierAllows;
    }

    // ---------------------------------------------------------------- soft edges on device forms
    //
    // The chamfered polygons of a device form (DeviceFormSoft, Client.Core) through the same streams as its hard faces
    // (AddDeviceForm): a polygon takes the tile, mapping, quarter turns, emission, animation and light of its reference face
    // (`Dir`); a strip or corner continues that face's mapping across itself — sliced by position, or for fit the box face's
    // extent laid onto the region, clamped to it — and takes the mean shade of the faces it lies between. A polygon on the
    // cell's border (`Boundary`) is left out where the neighbour seals that cube face, like a hard face; edges on the border
    // are never cut, so no strip stands against such a face.
    public static partial class ChunkMesher
    {
        [System.ThreadStatic] private static Vector3[] _deviceSoftPoints;
        [System.ThreadStatic] private static Vector2[] _deviceSoftUv;

        private static void AddSoftDeviceForm(ChunkMeshData data, BlockTextureAtlas atlas, DeviceFormInfo info, Vector3 cell, int yaw,
            DeviceCell c, float matR, float matG, float emission, float tintMode, float animCode, Vector4 leaf)
        {
            var faces = info.SoftFaces[yaw & 3];
            var pts = _deviceSoftPoints ??= new Vector3[4];
            var fr = _deviceSoftUv ??= new Vector2[4];
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                int dir = f.Dir;
                if (f.Boundary && !c.Open[dir])
                {
                    continue;
                }

                var t = info.TileOf(f.Slot, f.Side);
                bool own = t.Slot == 0;
                Rect tile = own ? c.Tile[dir] : atlas.TileUv(t.Slot);
                float em = own ? emission : info.EmissionOf(f.Slot, emission);
                int rot = c.Rot[dir];
                int n = f.Facet.IsQuad ? 4 : 3;
                pts[0] = SoftPoint(f.Facet.A);
                pts[1] = SoftPoint(f.Facet.B);
                pts[2] = SoftPoint(f.Facet.C);
                pts[3] = f.Facet.IsQuad ? SoftPoint(f.Facet.D) : pts[2];
                SoftDeviceFractions(info, yaw, f, rot, t, pts, n, fr);
                float mode = tintMode + (own && c.Anim[dir] ? animCode : 0f);
                EmitSoftDeviceFace(data, cell, f, pts, n, tile, rot, fr, c, new Color(matR, matG, MeanFaceShade(f.Dirs), em), mode, leaf);
            }
        }

        private static Vector3 SoftPoint(System.Numerics.Vector3 p) => new Vector3(p.X, p.Y, p.Z);

        /// <summary>The mean cube-face shade of the directions a polygon looks between (one for a flat face).</summary>
        private static float MeanFaceShade(int dirs)
        {
            float sum = 0f;
            int count = 0;
            for (int d = 0; d < 6; d++)
            {
                if ((dirs & (1 << d)) != 0)
                {
                    sum += FaceShade(d);
                    count++;
                }
            }

            return count > 0 ? sum / count : 1f;
        }

        /// <summary>The tile fractions of a polygon's corners: the reference face's cube mapping of the position, turned like
        /// that cube face; for fit the extent of the box's face in that direction laid onto the region, clamped to it — so a
        /// strip continues the picture of the face it belongs to and a picture stays one per box face.</summary>
        private static void SoftDeviceFractions(DeviceFormInfo info, int yaw, in DeviceFormFacet f, int rot, DeviceTileRef t,
            Vector3[] pts, int n, Vector2[] fr)
        {
            int dir = f.Dir;
            for (int i = 0; i < n; i++)
            {
                fr[i] = TurnFraction(CubeFaceFraction(dir, pts[i]), rot);
            }

            if (!t.Fit)
            {
                return;
            }

            var b = DeviceFormFaces.Turn(info.Form.Boxes[f.Box], yaw);
            const float e = 1f / DeviceForms.Grid;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) != 0 ? b.X1 * e : b.X0 * e, (k & 2) != 0 ? b.Y1 * e : b.Y0 * e, (k & 4) != 0 ? b.Z1 * e : b.Z0 * e);
                var q = TurnFraction(CubeFaceFraction(dir, corner), rot);
                uMin = Mathf.Min(uMin, q.x);
                uMax = Mathf.Max(uMax, q.x);
                vMin = Mathf.Min(vMin, q.y);
                vMax = Mathf.Max(vMax, q.y);
            }

            for (int i = 0; i < n; i++)
            {
                float su = uMax > uMin ? Mathf.Clamp01((fr[i].x - uMin) / (uMax - uMin)) : 0f;
                float sv = vMax > vMin ? Mathf.Clamp01((fr[i].y - vMin) / (vMax - vMin)) : 0f;
                fr[i] = new Vector2(Mathf.Lerp(t.U0, t.U1, su), Mathf.Lerp(t.V0, t.V1, sv));
            }
        }

        /// <summary>The direction of increasing u (x) and v (y) of <see cref="CubeFaceFraction"/> on cube face
        /// <paramref name="dir"/>, unturned.</summary>
        private static void CubeFaceAxes(int dir, out Vector3 u, out Vector3 v)
        {
            switch (dir)
            {
                case 0: u = Vector3.right; v = Vector3.forward; break;
                case 1: u = Vector3.right; v = Vector3.back; break;
                case 2: u = Vector3.forward; v = Vector3.up; break;
                case 3: u = Vector3.back; v = Vector3.up; break;
                case 4: u = Vector3.left; v = Vector3.up; break;
                default: u = Vector3.right; v = Vector3.up; break;
            }
        }

        /// <summary>One soft polygon (3 or 4 corners) with every per-vertex stream. The tangent is the reference face's u
        /// direction after the quarter turns, laid into the polygon's plane.</summary>
        private static void EmitSoftDeviceFace(ChunkMeshData data, Vector3 cell, in DeviceFormFacet f, Vector3[] pts, int n, Rect tile,
            int rot, Vector2[] fr, DeviceCell c, Color col, float mode, Vector4 leaf)
        {
            int dir = f.Dir;
            int baseIndex = data.Verts.Count;
            Vector3 nrm = Vector3.Cross(pts[1] - pts[0], pts[n - 1] - pts[0]).normalized;
            CubeFaceAxes(dir, out var u, out var v);
            Vector3 tan = (rot & 3) switch { 1 => v, 2 => -u, 3 => -v, _ => u };
            Vector3 bit = (rot & 3) switch { 1 => -u, 2 => -v, 3 => u, _ => v };
            tan = (tan - (nrm * Vector3.Dot(nrm, tan))).normalized;
            float hand = Vector3.Dot(Vector3.Cross(nrm, tan), bit) < 0f ? -1f : 1f;
            var tv = new Vector4(tan.x, tan.y, tan.z, hand);
            Vector4 ao = c.Ao[dir];
            for (int i = 0; i < n; i++)
            {
                float shade = f.Boundary ? CornerAo(ao, CubeFaceFraction(dir, pts[i])) : DeviceInnerShade;
                data.Verts.Add(cell + pts[i]);
                data.Colors.Add(new Color(col.r, col.g, col.b * shade, col.a));
                data.Uvs.Add(new Vector2(tile.xMin + (fr[i].x * tile.width), tile.yMin + (fr[i].y * tile.height)));
                data.Tangents.Add(tv);
                data.SkyUv.Add(new Vector2(c.Sky[dir], mode));
                data.LeafUv.Add(leaf);
                data.BlockLight.Add(c.Light[dir]);
                data.BlockLightDir.Add(c.LightDir[dir]);
            }

            var tris = data.OpaqueTris;
            tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            if (n == 4)
            {
                tris.Add(baseIndex); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
            }
        }
    }
}
