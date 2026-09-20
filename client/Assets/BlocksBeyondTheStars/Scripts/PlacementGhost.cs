// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The translucent placement preview (#863): while a rotatable block is held, the exact form + pending
    /// orientation hovers in the cell a right-click would fill, so "which way is it turned?" is answered by
    /// looking instead of by decoding a HUD label. Uses the same <see cref="BlockShapeGeometry"/> the world
    /// mesher renders with — built-in and player-designed forms alike — so the ghost can never promise a
    /// silhouette the placed block then betrays. A custom form this client has no geometry for previews as
    /// a plain cube, which is exactly what the server would place.
    /// </summary>
    internal sealed class PlacementGhost
    {
        // The hologram blue every other energy visual uses (base shield, door fields), a bit fainter — the
        // ghost must read as "not there yet", never as a placed glass block.
        private static readonly Color GhostColor = new Color(0.55f, 0.85f, 1f, 0.35f);

        private GameObject _go;
        private MeshFilter _filter;
        private Mesh _mesh;
        private int _builtKey = int.MinValue;

        /// <summary>Shows the ghost for a shape + orientation at a world cell, rebuilding the mesh only when
        /// the (shape, yaw, up-face) combination actually changed — moving the aim just moves the object.</summary>
        public void Show(Vector3Int cell, int shapeIndex, int yaw, int upFace)
        {
            if (_go == null)
            {
                Create();
            }

            int key = (shapeIndex << 5) | ((upFace & 7) << 2) | (yaw & 3);
            if (key != _builtKey)
            {
                _builtKey = key;
                BuildMesh(shapeIndex, yaw, upFace);
            }

            _go.transform.position = new Vector3(cell.x, cell.y, cell.z);
            _go.SetActive(true);
        }

        public void Hide()
        {
            if (_go != null && _go.activeSelf)
            {
                _go.SetActive(false);
            }
        }

        /// <summary>Tears the ghost down with its owner (scene unload / controller destroyed).</summary>
        public void Destroy()
        {
            if (_mesh != null)
            {
                Object.Destroy(_mesh);
                _mesh = null;
            }

            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
            }
        }

        private void Create()
        {
            _go = new GameObject("PlacementGhost");
            _filter = _go.AddComponent<MeshFilter>();
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // The project's translucent-runtime-mesh pattern (BaseShieldView, DoorView): the Cloud shader is
            // in Always-Included Shaders, so Shader.Find survives build stripping.
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(GhostColor));
            mat.renderQueue = 3001; // over the world + fluids, so the preview never vanishes into a wall
            renderer.sharedMaterial = mat;

            _mesh = new Mesh { name = "PlacementGhost" };
            _filter.sharedMesh = _mesh;
        }

        private void BuildMesh(int shapeIndex, int yaw, int upFace)
        {
            // A player form over several blocks (#1961) turns but never tips — the server pins it upright, so the
            // ghost must not promise a tilt. Its further blocks are drawn below, like the bed's foot half.
            string formVoxels = null;
            if (ShapeCode.IsCustomShape(shapeIndex) && BlockShapeGeometry.TryGetCustomVoxels(shapeIndex, out string registered)
                && CustomShape.IsMulti(registered))
            {
                formVoxels = registered;
                upFace = ShapeCode.UpPlusY;
            }

            var faces = BlockShapeGeometry.Build(shapeIndex, yaw, upFace);
            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Add(BlockShapeGeometry.Face f)
            {
                int b = verts.Count;
                verts.Add(f.A); verts.Add(f.B); verts.Add(f.C);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                if (f.IsQuad)
                {
                    verts.Add(f.D);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            if (faces != null && faces.Count > 0)
            {
                foreach (var f in faces)
                {
                    Add(f);
                }

                if (formVoxels != null)
                {
                    // Every block the form will take — the server refuses the place when one of them is not
                    // free, so a ghost block inside a wall is the warning itself.
                    int cells = CustomShape.CellCount(formVoxels);
                    for (int cell = 1; cell < cells; cell++)
                    {
                        var part = BlockShapeGeometry.Build(shapeIndex, yaw, upFace, cell);
                        if (part == null)
                        {
                            continue;
                        }

                        var (ox, oy, oz) = CustomShape.CellOffset(formVoxels, cell, yaw);
                        var offset = new Vector3(ox, oy, oz);
                        foreach (var f in part)
                        {
                            Add(f.Map(p => p + offset));
                        }
                    }
                }

                // A bed is two cells (#1846): preview the foot half on the cell the head's yaw points to, so
                // "where does the other half go?" is answered by looking. The server refuses the place when
                // that cell is not free, so a ghost foot inside a wall is the warning itself.
                if (shapeIndex == (int)BlockShape.BedHead
                    && FurnitureShapes.TryBedPartnerOffset(ShapeCode.Pack(shapeIndex, yaw, upFace), out int dx, out int dz))
                {
                    var foot = BlockShapeGeometry.Build((int)BlockShape.BedFoot, yaw, upFace);
                    var shift = new Vector3(dx, 0f, dz);
                    if (foot != null)
                    {
                        foreach (var f in foot)
                        {
                            Add(f.Map(p => p + shift));
                        }
                    }
                }
            }
            else
            {
                // Cube geometry (or a custom form whose voxels never arrived): a plain unit box.
                UnitBox(verts, tris);
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>Six outward-wound quads of the unit cell [0,1]³ (as triangles).</summary>
        private static void UnitBox(List<Vector3> verts, List<int> tris)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Quad(new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0)); // +Y
            Quad(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)); // -Y
            Quad(new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1)); // +X
            Quad(new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0)); // -X
            Quad(new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1)); // +Z
            Quad(new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0)); // -Z
        }
    }
}
