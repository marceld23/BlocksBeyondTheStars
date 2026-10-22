// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared 3D scene pieces of the standalone build editors (<see cref="ShipEditor"/>,
    /// <see cref="StructureEditor"/>): the build floor with its grid, the fill light, the mouse-wheel
    /// dolly + "frame the build" camera helpers. Keeping them here means both editors look and steer
    /// the same, and a grid/camera tweak lands in both at once (#1390 #1391). It also holds the small
    /// helpers both editors used to carry as identical copies: the editor camera, the cell picking
    /// rays, the room bounds test, the form-icon brush tile, the right-hand panel and bottom hint line,
    /// and the hex-colour / key-slug text helpers.
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

        /// <summary>The editor's own camera under <paramref name="parent"/>: solid deep-space backdrop, the given
        /// far clip, and the scene's audio listener.</summary>
        public static Camera CreateEditorCamera(Transform parent, float farClip)
        {
            var camGo = new GameObject("EditorCamera");
            camGo.transform.SetParent(parent, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.farClipPlane = farClip;
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        /// <summary>Resolves the cell a placement would land in: the floor column, or the empty cell just
        /// outside the hit face (the chunk mesh is authored in world coords, so the hit point + normal locate
        /// the cell directly — no per-cell GameObject to read a transform from). <paramref name="hitFace"/> is
        /// the face of the target cell the click came through (the hit normal; -1 for none): what a ladder
        /// hugs (#1975).</summary>
        public static bool PickTargetCell(Camera cam, Vector3 screenPoint, float dist, GameObject floor, out Vector3i cell, out int hitFace)
        {
            cell = default;
            hitFace = -1;
            var ray = cam.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out var hit, dist))
            {
                return false;
            }

            if (hit.collider.gameObject == floor)
            {
                cell = new Vector3i(Mathf.FloorToInt(hit.point.x), 0, Mathf.FloorToInt(hit.point.z));
            }
            else
            {
                Vector3 outside = hit.point + hit.normal * 0.5f; // step out of the hit cell into its empty neighbour
                cell = new Vector3i(Mathf.FloorToInt(outside.x), Mathf.FloorToInt(outside.y), Mathf.FloorToInt(outside.z));
            }

            hitFace = ShapeCode.FaceFromDirection(Mathf.RoundToInt(hit.normal.x), Mathf.RoundToInt(hit.normal.y), Mathf.RoundToInt(hit.normal.z));
            return true;
        }

        /// <summary>Resolves the actual occupied cell under <paramref name="screenPoint"/> (for removal); false if
        /// the ray misses every block or hits only the floor.</summary>
        public static bool PickHitCell(Camera cam, Vector3 screenPoint, float dist, GameObject floor, out Vector3i cell)
        {
            cell = default;
            var ray = cam.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out var hit, dist) || hit.collider.gameObject == floor)
            {
                return false;
            }

            Vector3 inside = hit.point - hit.normal * 0.5f; // step into the hit cell
            cell = new Vector3i(Mathf.FloorToInt(inside.x), Mathf.FloorToInt(inside.y), Mathf.FloorToInt(inside.z));
            return true;
        }

        /// <summary>True when <paramref name="c"/> lies inside the editor room of the given size.</summary>
        public static bool InRoom(Vector3i c, int maxW, int maxH, int maxL)
            => c.X >= 0 && c.X < maxW && c.Y >= 0 && c.Y < maxH && c.Z >= 0 && c.Z < maxL;

        /// <summary>The tile the form icons are drawn on: the block of <paramref name="pal"/>, else stone.</summary>
        public static ushort BrushTile(AppShell shell, EditorPaletteKit.Entry pal)
        {
            var def = pal.Kind == "block" && shell?.Content != null ? shell.Content.GetBlock(pal.Id) : null;
            def ??= shell?.Content?.GetBlock("stone");
            return def?.NumericId.Value ?? (ushort)0;
        }

        /// <summary>A panel anchored to the top-right corner of <paramref name="root"/> (16 px inset), so it hugs
        /// the screen edge at any resolution.</summary>
        public static RectTransform RightPanel(Transform root, float w, float h)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-16f, -16f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;
            return rt;
        }

        /// <summary>The bottom-centre controls hint line: a <paramref name="width"/>-wide, non-raycast label 14 px
        /// above the bottom edge of <paramref name="root"/>, showing <paramref name="text"/>.</summary>
        public static Text AddBottomHint(Transform root, float width, string text)
        {
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(width, 24f);
            hrt.anchoredPosition = new Vector2(0f, 14f);
            var hint = hintGo.AddComponent<Text>();
            hint.font = UiKit.Font;
            hint.fontSize = 16;
            hint.color = UiKit.TextCol;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.raycastTarget = false;
            hint.text = text;
            return hint;
        }

        /// <summary>Parses a 6-hex-digit colour string to 0xRRGGBB (0 = empty/invalid = "none").</summary>
        public static int ParseHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return 0;
            }

            s = s.Trim().TrimStart('#');
            return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? (v & 0xFFFFFF) : 0;
        }

        /// <summary>The 6-hex-digit form of 0xRRGGBB for an input field; empty for 0 ("none").</summary>
        public static string HexOf(int rgb) => rgb == 0 ? string.Empty : rgb.ToString("x6");

        /// <summary>A file/key slug: lower-case letters and digits, with spaces, dashes and underscores turned
        /// into underscores and everything else dropped; empty for a blank input.</summary>
        public static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// The placement ghost of the build editors: a softly pulsing translucent cube at the cell a click
    /// would fill — green when the placement is valid, red when the cell is occupied or out of bounds.
    /// The ship editor had one, the station/settlement editor did not (#1391). Since #1975 it also shows
    /// a MESH instead of the cube (<see cref="UpdateMesh"/>): the door the server would hang, a block's
    /// form, a bed as head + foot, a marker's silhouette.
    /// </summary>
    internal sealed class EditorPlacementGhost
    {
        private readonly GameObject _go;
        private readonly Renderer _renderer;
        private readonly Material _valid, _invalid;
        private GameObject _meshGo;      // the mesh mode (#1975): a door, a form, a bed pair, a silhouette
        private MeshFilter _meshFilter;
        private Renderer _meshRenderer;

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

            if (_meshGo != null && _meshGo.activeSelf)
            {
                _meshGo.SetActive(false); // cube mode
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

        /// <summary>Shows a MESH ghost (#1975) — the door the server would hang, a block's form, a bed pair, a
        /// marker's silhouette — with its origin at <paramref name="origin"/> (world space); the cube hides. Meshes
        /// belong to the caller (<see cref="EditorGhostMeshes"/> caches them); a null mesh falls back to the cube
        /// at the origin's cell.</summary>
        public void UpdateMesh(bool show, Vector3 origin, bool valid, Mesh mesh)
        {
            if (mesh == null)
            {
                Update(show, new Vector3i(Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y), Mathf.FloorToInt(origin.z)), valid);
                return;
            }

            if (_go == null)
            {
                return;
            }

            if (_meshGo == null)
            {
                _meshGo = new GameObject("PlacementGhostMesh", typeof(MeshFilter), typeof(MeshRenderer));
                _meshGo.transform.SetParent(_go.transform.parent, false); // no collider: must never block the picking ray
                _meshFilter = _meshGo.GetComponent<MeshFilter>();
                _meshRenderer = _meshGo.GetComponent<Renderer>();
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
            }

            if (_go.activeSelf)
            {
                _go.SetActive(false); // mesh mode
            }

            if (_meshGo.activeSelf != show)
            {
                _meshGo.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            if (_meshFilter.sharedMesh != mesh)
            {
                _meshFilter.sharedMesh = mesh;
            }

            _meshGo.transform.position = origin;
            _meshRenderer.sharedMaterial = valid ? _valid : _invalid;
        }

        public void Dispose()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
            }

            if (_meshGo != null)
            {
                Object.Destroy(_meshGo);
            }

            Object.Destroy(_valid);
            Object.Destroy(_invalid);
        }
    }

    /// <summary>
    /// The ship editor's interior frame (#1396): a translucent cyan box marking the volume the server treats
    /// as the cabin — <c>0..W-1 × 0..H × 0..L-1</c> of the layout, shifted by the room origin. Everything
    /// outside it is exterior (wings, engines, antennae) and may sit anywhere in the room; the floor
    /// guarantee, the roof and the hatch logic only look inside. Rebuilt whenever the dims change.
    /// </summary>
    internal sealed class EditorInteriorFrame
    {
        private readonly Transform _parent;
        private readonly Material _fill, _edge;
        private GameObject _root;

        public EditorInteriorFrame(Transform parent)
        {
            _parent = parent;
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            _fill = new Material(shader) { renderQueue = 2990 };
            _fill.SetColor("_Color", ShaderColor.Srgb(new Color(0.40f, 0.82f, 1f, 0.08f)));
            _edge = new Material(shader) { renderQueue = 2991 };
            _edge.SetColor("_Color", ShaderColor.Srgb(new Color(0.40f, 0.82f, 1f, 0.55f)));
        }

        /// <summary>Rebuilds the frame for an interior of <paramref name="w"/>×<paramref name="h"/>×<paramref name="l"/>
        /// cells whose (0,0,0) sits at <paramref name="origin"/>. The cabin spans y = 0..h (roof row included).</summary>
        public void Rebuild(Vector3i origin, int w, int h, int l)
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }

            _root = new GameObject("InteriorFrame");
            _root.transform.SetParent(_parent, false);
            var size = new Vector3(w, h + 1, l);
            var centre = new Vector3(origin.X + w / 2f, origin.Y + (h + 1) / 2f, origin.Z + l / 2f);
            Box(centre, size, _fill);

            // Twelve thin edge bars so the frame reads even where the fill is too faint against the floor.
            const float t = 0.06f;
            var min = centre - size / 2f;
            var max = centre + size / 2f;
            foreach (float y in new[] { min.y, max.y })
            {
                foreach (float z in new[] { min.z, max.z })
                {
                    Box(new Vector3(centre.x, y, z), new Vector3(size.x, t, t), _edge);
                }

                foreach (float x in new[] { min.x, max.x })
                {
                    Box(new Vector3(x, y, centre.z), new Vector3(t, t, size.z), _edge);
                }
            }

            foreach (float x in new[] { min.x, max.x })
            {
                foreach (float z in new[] { min.z, max.z })
                {
                    Box(new Vector3(x, centre.y, z), new Vector3(t, size.y, t), _edge);
                }
            }
        }

        private void Box(Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Frame";
            Object.Destroy(go.GetComponent<Collider>()); // never blocks the picking ray
            go.transform.SetParent(_root.transform, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }

            Object.Destroy(_fill);
            Object.Destroy(_edge);
        }
    }
}
