// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared 3D scene pieces of the standalone build editors (<see cref="ShipEditor"/>,
    /// <see cref="StructureEditor"/>): the build floor with its grid, the fill light, the mouse-wheel
    /// dolly + "frame the build" camera helpers. Keeping them here means both editors look and steer
    /// the same, and a grid/camera tweak lands in both at once (#1390 #1391).
    /// </summary>
    internal static class EditorSceneKit
    {
        /// <summary>Cells per grid-texture tile: a brighter MAJOR line every this many cells.</summary>
        private const int MajorEvery = 8;

        /// <summary>Pixels per cell inside the tile — enough for a crisp 2-px line that mip-filters to a
        /// soft but still visible line at distance (the old 0.03-unit cube lines fell below one pixel and
        /// aliased away, #1391).</summary>
        private const int PxPerCell = 32;

        private static Texture2D _gridTex;

        /// <summary>The build floor: a <paramref name="w"/>×<paramref name="l"/> slab whose top sits at
        /// y = 0 (the raycast target for floor placements) carrying the procedural grid — a minor line
        /// on every cell edge and a major line every <see cref="MajorEvery"/> cells, tiled so the lines
        /// land exactly on the integer cell boundaries.</summary>
        public static GameObject BuildFloor(Transform parent, int w, int l)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "BuildFloor";
            floor.transform.position = new Vector3(w / 2f, -0.5f, l / 2f);
            floor.transform.localScale = new Vector3(w, 1f, l);
            floor.transform.SetParent(parent, false);

            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Texture");
            var mat = new Material(shader)
            {
                color = ShaderColor.Srgb(Color.white),
                mainTexture = GridTexture(),
                mainTextureScale = new Vector2(w / (float)MajorEvery, l / (float)MajorEvery),
            };
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            return floor;
        }

        /// <summary>The directional fill light that makes the lit cells read in 3D.</summary>
        public static Light BuildSun(Transform parent)
        {
            var lightGo = new GameObject("EditorSun");
            lightGo.transform.SetParent(parent, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            sun.intensity = 1f;
            return sun;
        }

        /// <summary>One <see cref="MajorEvery"/>-cell tile of the floor grid, built once and shared:
        /// dark floor, a 2-px minor line on every cell edge, a 3-px brighter major line on the tile
        /// edge. Mipmapped + trilinear + anisotropic so the lines stay visible at a grazing angle.</summary>
        private static Texture2D GridTexture()
        {
            if (_gridTex != null)
            {
                return _gridTex;
            }

            const int size = MajorEvery * PxPerCell;
            var floorCol = new Color(0.10f, 0.13f, 0.18f);
            var minorCol = new Color(0.26f, 0.44f, 0.58f);
            var majorCol = new Color(0.45f, 0.78f, 1.00f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "EditorGrid",
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                wrapMode = TextureWrapMode.Repeat,
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool major = x < 3 || y < 3;
                    bool minor = x % PxPerCell < 2 || y % PxPerCell < 2;
                    px[y * size + x] = major ? majorCol : minor ? minorCol : floorCol;
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            _gridTex = tex;
            return tex;
        }

        /// <summary>Units the camera moves per wheel notch (Shift triples it).</summary>
        private const float DollyPerNotch = 3f;

        /// <summary>Mouse-wheel zoom: dollies the camera along its view direction (#1390). A fly-through
        /// editor has no orbit pivot to zoom about, so moving the eye is the honest "zoom".</summary>
        public static void WheelDolly(Transform cam, bool fast)
        {
            float notches = Input.mouseScrollDelta.y;
            if (Mathf.Abs(notches) < 0.01f)
            {
                return;
            }

            cam.position += cam.forward * (notches * DollyPerNotch * (fast ? 3f : 1f));
        }

        /// <summary>Points the camera at the build (or, with nothing placed, at a starter patch in the
        /// middle of the floor) from a comfortable three-quarter view: pitch 30° down, yaw kept, far
        /// enough back that the whole bounds fit. Used for the opening view and the F hotkey (#1390).</summary>
        public static void Frame(Transform cam, ref float pitch, float yaw, IReadOnlyCollection<Vector3i> cells, int w, int l)
        {
            var b = cells.Count == 0
                ? new Bounds(new Vector3(w / 2f, 0f, l / 2f), new Vector3(16f, 0f, 16f))
                : CellBounds(cells);

            pitch = 30f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            float dist = Mathf.Max(10f, b.extents.magnitude * 2.2f);
            cam.rotation = rot;
            cam.position = b.center - rot * Vector3.forward * dist;
        }

        private static Bounds CellBounds(IEnumerable<Vector3i> cells)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var c in cells)
            {
                var p = new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
                if (first)
                {
                    b = new Bounds(p, Vector3.one);
                    first = false;
                }
                else
                {
                    b.Encapsulate(new Bounds(p, Vector3.one));
                }
            }

            return b;
        }
    }

    /// <summary>
    /// The placement ghost of the build editors: a softly pulsing translucent cube at the cell a click
    /// would fill — green when the placement is valid, red when the cell is occupied or out of bounds.
    /// The ship editor had one, the station/settlement editor did not (#1391).
    /// </summary>
    internal sealed class EditorPlacementGhost
    {
        private readonly GameObject _go;
        private readonly Renderer _renderer;
        private readonly Material _valid, _invalid;

        public EditorPlacementGhost(Transform parent)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.name = "PlacementGhost";
            Object.Destroy(_go.GetComponent<Collider>()); // must never block the picking ray
            _go.transform.SetParent(parent, false);
            _renderer = _go.GetComponent<Renderer>();

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            _valid = new Material(shader) { renderQueue = 3000 };
            _valid.SetColor("_Color", ShaderColor.Srgb(new Color(0.30f, 1f, 0.60f, 0.30f)));
            _invalid = new Material(shader) { renderQueue = 3000 };
            _invalid.SetColor("_Color", ShaderColor.Srgb(new Color(1f, 0.25f, 0.20f, 0.30f)));
            _go.SetActive(false);
        }

        /// <summary>Shows the ghost at <paramref name="cell"/> (or hides it when <paramref name="show"/> is false).</summary>
        public void Update(bool show, Vector3i cell, bool valid)
        {
            if (_go == null)
            {
                return;
            }

            if (_go.activeSelf != show)
            {
                _go.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            _go.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            _go.transform.localScale = Vector3.one * (1.0f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f));
            _renderer.sharedMaterial = valid ? _valid : _invalid;
        }

        public void Dispose()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
            }

            Object.Destroy(_valid);
            Object.Destroy(_invalid);
        }
    }
}
