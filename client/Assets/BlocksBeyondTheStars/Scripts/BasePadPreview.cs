// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3070: the landing square a base landing pad would get, shown while the pad is held and aimed at a spot — a frame of
    /// <see cref="BasePadRules.Size"/> × <see cref="BasePadRules.Size"/> blocks at the pad's top with corner posts as high
    /// as the kept-clear volume (<see cref="BasePadRules.Clearance"/>), green when that volume holds nothing solid or fluid
    /// in the blocks this client knows, red otherwise — and every column that is in the way marked red from its lowest to
    /// its highest obstacle. Advisory only: the server checks the place. The world is read again only when the aimed cell
    /// changes (a few thousand block reads), so holding still costs nothing.
    /// </summary>
    internal sealed class BasePadPreview
    {
        private static readonly Color ClearColor = new Color(0.35f, 0.95f, 0.55f, 0.6f);
        private static readonly Color BlockedColor = new Color(1f, 0.32f, 0.25f, 0.6f);
        private static readonly Color ObstacleColor = new Color(1f, 0.25f, 0.2f, 0.3f);

        private const float Strip = 0.18f; // the frame's width
        private const float Post = 0.1f;   // the corner posts' thickness
        private const float Lift = 0.04f;  // above the pad's top, so the frame never flickers in the ground
        private const float Grow = 0.03f;  // an obstacle mark stands just outside its blocks

        private GameObject _go;
        private MeshRenderer _frame;
        private Mesh _frameMesh;
        private Mesh _obstacleMesh;
        private Material _clearMat;
        private Material _blockedMat;
        private Material _obstacleMat;
        private Vector3Int _cell;
        private bool _shown;

        /// <summary>Shows the square around the pad cell <paramref name="cell"/>; <paramref name="blocks"/> answers whether
        /// a world cell is in the way. The volume is re-read only when the cell changed since the last frame shown.</summary>
        public void Show(Vector3Int cell, System.Func<int, int, int, bool> blocks)
        {
            if (_go == null)
            {
                Create();
            }

            if (!_shown || cell != _cell)
            {
                _cell = cell;
                _frame.sharedMaterial = BuildObstacles(cell, blocks) ? _blockedMat : _clearMat;
            }

            _go.transform.position = new Vector3(cell.x, cell.y, cell.z);
            _shown = true;
            if (!_go.activeSelf)
            {
                _go.SetActive(true);
            }
        }

        public void Hide()
        {
            _shown = false;
            if (_go != null && _go.activeSelf)
            {
                _go.SetActive(false);
            }
        }

        /// <summary>Tears the preview down with its owner (scene unload / controller destroyed).</summary>
        public void Destroy()
        {
            foreach (var o in new Object[] { _frameMesh, _obstacleMesh, _clearMat, _blockedMat, _obstacleMat, _go })
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _go = null;
            _frame = null;
            _frameMesh = null;
            _obstacleMesh = null;
            _clearMat = _blockedMat = _obstacleMat = null;
        }

        private void Create()
        {
            _go = new GameObject("BasePadPreview");
            _clearMat = MakeMaterial(ClearColor);
            _blockedMat = MakeMaterial(BlockedColor);
            _obstacleMat = MakeMaterial(ObstacleColor);

            _frameMesh = new Mesh { name = "BasePadFrame" };
            _frame = AddPart("Frame", _frameMesh, _clearMat);
            BuildFrame(_frameMesh);

            _obstacleMesh = new Mesh { name = "BasePadObstacles" };
            AddPart("Obstacles", _obstacleMesh, _obstacleMat);
        }

        private MeshRenderer AddPart(string name, Mesh mesh, Material mat)
        {
            var part = new GameObject(name);
            part.transform.SetParent(_go.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = mat;
            return renderer;
        }

        /// <summary>The placement ghost's translucent-runtime-mesh pattern: the Cloud shader is Always-Included, so
        /// Shader.Find survives build stripping; drawn over the world and the fluids.</summary>
        private static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(color));
            mat.renderQueue = 3001;
            return mat;
        }

        /// <summary>The square's frame at the pad's top (the pad cell is the origin, its square spans HalfSize cells to
        /// every side) and the four corner posts up to the top of the kept-clear volume.</summary>
        private static void BuildFrame(Mesh mesh)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float lo = -BasePadRules.HalfSize, hi = BasePadRules.HalfSize + 1f, y = 1f + Lift;
            Flat(verts, tris, lo, lo, hi, lo + Strip, y);
            Flat(verts, tris, lo, hi - Strip, hi, hi, y);
            Flat(verts, tris, lo, lo + Strip, lo + Strip, hi - Strip, y);
            Flat(verts, tris, hi - Strip, lo + Strip, hi, hi - Strip, y);
            float top = 1f + BasePadRules.Clearance;
            foreach (float px in new[] { lo, hi - Post })
            {
                foreach (float pz in new[] { lo, hi - Post })
                {
                    Box(verts, tris, new Vector3(px, 1f, pz), new Vector3(px + Post, top, pz + Post));
                }
            }

            Apply(mesh, verts, tris);
        }

        /// <summary>Reads the kept-clear volume above the pad cell and marks each column with something in it. True when
        /// any column is blocked.</summary>
        private bool BuildObstacles(Vector3Int cell, System.Func<int, int, int, bool> blocks)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int dx = -BasePadRules.HalfSize; dx <= BasePadRules.HalfSize; dx++)
            {
                for (int dz = -BasePadRules.HalfSize; dz <= BasePadRules.HalfSize; dz++)
                {
                    int low = 0, high = 0;
                    for (int dy = 1; dy <= BasePadRules.Clearance; dy++)
                    {
                        if (blocks(cell.x + dx, cell.y + dy, cell.z + dz))
                        {
                            low = low == 0 ? dy : low;
                            high = dy;
                        }
                    }

                    if (low > 0)
                    {
                        Box(verts, tris, new Vector3(dx - Grow, low - Grow, dz - Grow), new Vector3(dx + 1f + Grow, high + 1f + Grow, dz + 1f + Grow));
                    }
                }
            }

            Apply(_obstacleMesh, verts, tris);
            return verts.Count > 0;
        }

        private static void Apply(Mesh mesh, List<Vector3> verts, List<int> tris)
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>A flat quad at height <paramref name="y"/> over x0..x1 × z0..z1 (the shader draws both sides).</summary>
        private static void Flat(List<Vector3> verts, List<int> tris, float x0, float z0, float x1, float z1, float y)
            => Quad(verts, tris, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0));

        /// <summary>The six faces of the box <paramref name="a"/>..<paramref name="b"/>.</summary>
        private static void Box(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b)
        {
            Quad(verts, tris, new(a.x, b.y, a.z), new(a.x, b.y, b.z), new(b.x, b.y, b.z), new(b.x, b.y, a.z)); // +Y
            Quad(verts, tris, new(a.x, a.y, b.z), new(a.x, a.y, a.z), new(b.x, a.y, a.z), new(b.x, a.y, b.z)); // -Y
            Quad(verts, tris, new(b.x, a.y, a.z), new(b.x, b.y, a.z), new(b.x, b.y, b.z), new(b.x, a.y, b.z)); // +X
            Quad(verts, tris, new(a.x, a.y, b.z), new(a.x, b.y, b.z), new(a.x, b.y, a.z), new(a.x, a.y, a.z)); // -X
            Quad(verts, tris, new(b.x, a.y, b.z), new(b.x, b.y, b.z), new(a.x, b.y, b.z), new(a.x, a.y, b.z)); // +Z
            Quad(verts, tris, new(a.x, a.y, a.z), new(a.x, b.y, a.z), new(b.x, b.y, a.z), new(b.x, a.y, a.z)); // -Z
        }

        private static void Quad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }
    }
}
