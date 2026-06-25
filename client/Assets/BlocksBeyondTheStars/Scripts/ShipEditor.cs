// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Standalone ship-type editor (M27+ tooling; see docs/developer/SHIP_TYPE_EDITOR.md). An empty build
    /// room you fly through (hold RMB to look, WASD/QE to move) and place blocks into: hull, viewports,
    /// all ship stations, a hatch, lights and an engine. A side panel sets the design's name, stats and
    /// blueprint/craft costs. Save writes a ship-type bundle (ship.json + layout.json) that a developer
    /// folds into the game with tools/merge_ship.py. Self-contained on the client (no server).
    /// </summary>
    public sealed class ShipEditor : MonoBehaviour
    {
        public AppShell Shell;

        private const int MaxW = 24, MaxH = 16, MaxL = 24;

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        /// <summary>One authored cell: palette id + kind plus the in-game per-voxel modifiers (dye/glow
        /// colour 0xRRGGBB, packed shape+orientation). Elements/stations carry no modifiers.</summary>
        private struct CellData { public string Id; public string Kind; public int Tint, Glow, Shape; }

        private readonly Dictionary<Vector3i, CellData> _design = new();   // cell -> authored cell
        private readonly Dictionary<Vector3i, GameObject> _cells = new();
        private readonly Dictionary<string, Material> _mats = new();

        private struct Pal { public string Id, Label, Kind; public Color Color; }
        private Pal[] _palette;
        private int _selected;

        // Brush: dye/glow colour + shape + orientation applied to newly placed BLOCK cells (elements +
        // stations ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / cube.
        private int _brushTint, _brushGlow, _brushShape, _brushOrient;
        private string _search = string.Empty;
        private static readonly string[] ShapeNames = { "Cube", "Slab", "Pyramid", "Dome", "Sphere", "Ramp", "Stairs", "Cone", "Cylinder" };

        // --- editable metadata ---
        private string _key = "my_ship";
        private string _shipName = "My Ship";
        private string _desc = "A custom ship.";
        private string _requiredBlueprint = string.Empty;
        private float _hull = 100f, _shield = 20f, _flightSpeed = 1f, _handling = 1f;
        private int _cargo = 48;
        private readonly List<CostRow> _craftCost = new() { new CostRow { Item = "iron_plate", Count = 20 } };

        private string _statusText = string.Empty;
        private bool _mouseOverUi;

        /// <summary>Setting the status also pushes it to the on-screen label.</summary>
        private string _status
        {
            get => _statusText;
            set { _statusText = value; if (_statusLabel != null) _statusLabel.text = value; }
        }

        private sealed class CostRow { public string Item = string.Empty; public int Count = 1; }

        private void Start()
        {
            _palette = BuildPalette();

            var camGo = new GameObject("EditorCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            _cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            _cam.transform.position = new Vector3(MaxW / 2f, 6f, -10f);
            _yaw = 0f;
            _pitch = 15f;
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            BuildRoom();
            BuildUi();
        }

        private static Pal P(string id, string label, string kind, Color c) => new Pal { Id = id, Label = label, Kind = kind, Color = c };

        private string L(string key) => Shell != null ? Shell.L(key) : key;

        /// <summary>The ship palette: the special ship elements + stations + weapons, followed by every
        /// placeable block from the loaded content (so all materials — dyeable, shapeable, light/glowing —
        /// are available to author with). Built once from <see cref="AppShell.Content"/>.</summary>
        private Pal[] BuildPalette()
        {
            var list = new List<Pal>
            {
                P("light", "Light", "element", new Color(1f, 0.95f, 0.55f)),
                P("headlight", "Headlight", "element", new Color(0.95f, 0.97f, 1f)),
                P("light_red", "Port Light (red)", "element", new Color(1f, 0.3f, 0.3f)),
                P("light_green", "Starboard Light (green)", "element", new Color(0.3f, 1f, 0.4f)),
                P("engine", "Engine", "element", new Color(1f, 0.55f, 0.2f)),
                P("hatch", "Hatch", "element", new Color(0.7f, 0.5f, 0.3f)),
                P("door_slide", "Sliding Door", "element", new Color(0.4f, 0.85f, 0.95f)),
                P("door_hinge", "Hinged Door", "element", new Color(0.55f, 0.8f, 0.7f)),
                P("cockpit", "Cockpit", "station", new Color(0.3f, 0.6f, 0.95f)),
                P("reactor", "Reactor", "station", new Color(0.9f, 0.35f, 0.3f)),
                P("life_support", "Life Support", "station", new Color(0.4f, 0.85f, 0.55f)),
                P("workshop", "Workshop", "station", new Color(0.75f, 0.65f, 0.4f)),
                P("medbay", "Medbay (Heal-Tank)", "station", new Color(0.9f, 0.95f, 1f)),
                P("quarters", "Quarters", "station", new Color(0.6f, 0.45f, 0.8f)),
                P("cargo", "Cargo Hold", "station", new Color(0.7f, 0.6f, 0.45f)),
                P("hangar", "Hangar", "station", new Color(0.35f, 0.4f, 0.46f)),
                P("ship_laser_basic", "Laser Cannon", "station", new Color(0.45f, 1f, 1f)),
                P("ship_cannon_1", "Ship Cannon", "station", new Color(0.95f, 0.55f, 0.4f)),
            };

            var content = Shell != null ? Shell.Content : null;
            if (content != null)
            {
                var keys = new List<string>(content.Blocks.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    if (key == "air")
                    {
                        continue;
                    }

                    var def = content.GetBlock(key);
                    list.Add(P(key, def != null ? L(def.NameKey) : key, "block", BlockSwatch(key)));
                }
            }

            return list.ToArray();
        }

        private static Color BlockSwatch(string key)
        {
            int h = 0;
            foreach (char c in key) h = h * 31 + c;
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.32f, 0.78f);
        }

        private void BuildRoom()
        {
            // Build floor (raycast target + visual grid base).
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "BuildFloor";
            _floor.transform.position = new Vector3(MaxW / 2f, -0.5f, MaxL / 2f);
            _floor.transform.localScale = new Vector3(MaxW, 1f, MaxL);
            _floor.transform.SetParent(transform, false);
            _floor.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.10f, 0.13f, 0.18f), null);

            // Faint grid lines on the floor.
            var lineMat = Unlit(new Color(0.2f, 0.35f, 0.45f, 1f));
            for (int x = 0; x <= MaxW; x++)
            {
                GridLine(new Vector3(x, 0.02f, MaxL / 2f), new Vector3(0.03f, 0.02f, MaxL), lineMat);
            }

            for (int z = 0; z <= MaxL; z++)
            {
                GridLine(new Vector3(MaxW / 2f, 0.02f, z), new Vector3(MaxW, 0.02f, 0.03f), lineMat);
            }

            // A directional fill light so the lit cubes read in 3D.
            var lightGo = new GameObject("EditorSun");
            lightGo.transform.SetParent(transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            sun.intensity = 1f;
        }

        private void GridLine(Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Grid";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private void Update()
        {
            if (_cam == null)
            {
                return;
            }

            bool flying = Input.GetMouseButton(1);
            Cursor.lockState = flying ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !flying;

            if (flying)
            {
                _yaw += Input.GetAxis("Mouse X") * 2.6f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 2.6f, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            float speed = (Input.GetKey(KeyCode.LeftShift) ? 18f : 9f) * Time.deltaTime;
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += _cam.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= _cam.transform.forward;
            if (Input.GetKey(KeyCode.D)) move += _cam.transform.right;
            if (Input.GetKey(KeyCode.A)) move -= _cam.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
            _cam.transform.position += move * speed;

            // Place (LMB) / remove (MMB) when not flying and not over a uGUI panel.
            _mouseOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (_blocksLabel != null)
            {
                _blocksLabel.text = $"Blocks placed: {_design.Count}";
            }

            UpdateGhost(flying || _mouseOverUi);
            if (!flying && !_mouseOverUi)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    TryPlace();
                }
                else if (Input.GetMouseButtonDown(2))
                {
                    TryRemove();
                }
            }

            // Rotate the shape brush (matches the in-game place-orientation control).
            if (!_mouseOverUi && Input.GetKeyDown(KeyCode.R))
            {
                _brushOrient = (_brushOrient + 1) & 3;
            }
        }

        /// <summary>Resolves the cell a placement would land in (floor hit or hit-block neighbour).</summary>
        private bool TryGetTargetCell(out Vector3i cell)
        {
            cell = default;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 200f))
            {
                return false;
            }

            if (hit.collider.gameObject == _floor)
            {
                cell = new Vector3i(Mathf.FloorToInt(hit.point.x), 0, Mathf.FloorToInt(hit.point.z));
            }
            else
            {
                var t = hit.collider.transform.position;
                var hc = new Vector3i(Mathf.FloorToInt(t.x), Mathf.FloorToInt(t.y), Mathf.FloorToInt(t.z));
                cell = new Vector3i(hc.X + Mathf.RoundToInt(hit.normal.x), hc.Y + Mathf.RoundToInt(hit.normal.y), hc.Z + Mathf.RoundToInt(hit.normal.z));
            }

            return true;
        }

        private GameObject _ghost;
        private Material _ghostValid, _ghostInvalid;

        /// <summary>The placement ghost: a softly pulsing translucent cube at the target cell —
        /// green when the placement is valid, red when out of bounds or occupied.</summary>
        private void UpdateGhost(bool hidden)
        {
            if (_ghost == null)
            {
                _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ghost.name = "PlacementGhost";
                Destroy(_ghost.GetComponent<Collider>()); // must never block the picking ray
                _ghost.transform.SetParent(transform, false);
                var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                _ghostValid = new Material(shader) { renderQueue = 3000 };
                _ghostValid.SetColor("_Color", ShaderColor.Srgb(new Color(0.30f, 1f, 0.60f, 0.30f)));
                _ghostInvalid = new Material(shader) { renderQueue = 3000 };
                _ghostInvalid.SetColor("_Color", ShaderColor.Srgb(new Color(1f, 0.25f, 0.20f, 0.30f)));
            }

            Vector3i cell = default;
            bool show = !hidden && TryGetTargetCell(out cell);
            if (_ghost.activeSelf != show)
            {
                _ghost.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            bool valid = InBounds(cell) && !_design.ContainsKey(cell);
            _ghost.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            _ghost.transform.localScale = Vector3.one * (1.0f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f));
            _ghost.GetComponent<Renderer>().sharedMaterial = valid ? _ghostValid : _ghostInvalid;
        }

        private void TryPlace()
        {
            if (!TryGetTargetCell(out var cell))
            {
                return;
            }

            if (InBounds(cell) && !_design.ContainsKey(cell))
            {
                PlaceCell(cell, _palette[_selected]);
            }
        }

        private void TryRemove()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 200f) && hit.collider.gameObject != _floor)
            {
                var t = hit.collider.transform.position;
                var cell = new Vector3i(Mathf.FloorToInt(t.x), Mathf.FloorToInt(t.y), Mathf.FloorToInt(t.z));
                if (_cells.TryGetValue(cell, out var go))
                {
                    Destroy(go);
                    _cells.Remove(cell);
                    _design.Remove(cell);
                }
            }
        }

        private void PlaceCell(Vector3i cell, Pal pal)
        {
            var data = new CellData { Id = pal.Id, Kind = pal.Kind };
            if (pal.Kind == "block")
            {
                // Only real blocks carry dye/glow/shape (elements + stations are special-rendered anchors).
                data.Tint = _brushTint;
                data.Glow = _brushGlow;
                data.Shape = _brushShape != 0 ? ShapeCode.Pack(_brushShape, _brushOrient) : 0;
            }

            PlaceCellData(cell, pal, data);
        }

        private void PlaceCellData(Vector3i cell, Pal pal, CellData data)
        {
            GameObject go;
            Mesh shapeMesh = data.Shape != 0
                ? EditorVoxelPreview.ShapeMesh(ShapeCode.ShapeOf(data.Shape), ShapeCode.OrientationOf(data.Shape))
                : null;
            if (shapeMesh != null)
            {
                go = new GameObject($"Cell {pal.Id}", typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = shapeMesh;
                var bc = go.AddComponent<BoxCollider>();
                bc.center = Vector3.one * 0.5f;
                bc.size = Vector3.one;
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(cell.X, cell.Y, cell.Z);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Cell {pal.Id}";
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
                go.transform.localScale = Vector3.one * 0.98f;
            }

            go.GetComponent<Renderer>().sharedMaterial = MatFor(pal, data);
            _cells[cell] = go;
            _design[cell] = data;
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        private Material MatFor(Pal pal, CellData data)
        {
            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : pal.Color);
            string key = $"{pal.Id}|{data.Tint}|{data.Glow}";
            if (!_mats.TryGetValue(key, out var m))
            {
                m = Lit(baseCol, null);
                if (data.Glow != 0)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", ShaderColor.Srgb(EditorVoxelPreview.RgbToColor(data.Glow)));
                }

                _mats[key] = m;
            }

            return m;
        }

        // ----------------------------- UI (modern uGUI) -----------------------------

        private const float PanelH = 1048f;

        private Canvas _canvas;
        private RectTransform _form;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Text _shapeLabel;
        private Text _orientLabel;
        private Transform _palListParent;
        private readonly List<Image> _palButtons = new();
        private readonly List<int> _rowToPaletteIndex = new();
        private readonly List<CostUi> _costPool = new();

        private sealed class CostUi
        {
            public GameObject Go;
            public InputField Item;
            public InputField Count;
            public CostRow Bound;
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Ship Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // Left: block/part palette (elements + stations + every placeable block) with a search filter.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, "BLOCKS & PARTS", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(pal.transform, 12f, 42f, 276f, 28f, _search, v => { _search = v ?? string.Empty; RebuildPaletteRows(); });
            _palListParent = UiKit.ScrollList(pal.transform, 10f, 78f, 280f, PanelH - 90f);
            RebuildPaletteRows();

            // Right: ship metadata + stats + cost (anchored to the top-right so it hugs the edge).
            var meta = RightPanel(root, 380f, PanelH);
            UiKit.AddText(meta, 16f, 12f, 348f, 26f, "SHIP TYPE", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            _form = UiKit.ScrollList(meta, 8f, 48f, 364f, PanelH - 48f - 116f, 3f);
            BuildForm();

            // Pinned footer: status + save + back.
            _statusLabel = UiKit.AddText(meta, 12f, PanelH - 112f, 356f, 44f, string.Empty, 14, UiKit.Ok);
            _statusLabel.alignment = TextAnchor.UpperLeft;
            UiKit.AddButton(meta, 12f, PanelH - 62f, 150f, 38f, "SAVE / EXPORT", Export);
            UiKit.AddButton(meta, 168f, PanelH - 62f, 96f, 38f, "LOAD", OpenLoadPicker);
            UiKit.AddButton(meta, 270f, PanelH - 62f, 98f, 38f, "← Back", () => Shell?.CloseShipEditor());

            // Bottom-centre controls hint.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(1100f, 24f);
            hrt.anchoredPosition = new Vector2(0f, 14f);
            var hint = hintGo.AddComponent<Text>();
            hint.font = UiKit.Font;
            hint.fontSize = 16;
            hint.color = UiKit.TextCol;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.raycastTarget = false;
            hint.text = "Hold RIGHT-MOUSE to look · WASD + Q/E (or Space/Ctrl) to fly · Shift = faster · LEFT-CLICK place · MIDDLE-CLICK remove · Esc = menu";
        }

        private void BuildForm()
        {
            FormLabel("Key (unique, slug)");
            InputRow(_key, v => _key = v);
            FormLabel("Name");
            InputRow(_shipName, v => _shipName = v);
            FormLabel("Description");
            InputRow(_desc, v => _desc = v);
            FormLabel("Required blueprint (blank = always)");
            InputRow(_requiredBlueprint, v => _requiredBlueprint = v);

            FormHeader("STATS");
            Stepper("Base hull", () => _hull, v => _hull = v, 20f, 500f, 10f, "0");
            Stepper("Base shield", () => _shield, v => _shield = v, 0f, 300f, 10f, "0");
            Stepper("Flight speed", () => _flightSpeed, v => _flightSpeed = v, 0.4f, 2.5f, 0.05f, "0.00");
            Stepper("Handling", () => _handling, v => _handling = v, 0.4f, 2.5f, 0.05f, "0.00");
            Stepper("Cargo slots", () => _cargo, v => _cargo = Mathf.RoundToInt(v), 12f, 240f, 4f, "0");

            // Block brush: dye + glow colour + shape + orientation applied to newly placed BLOCK cells
            // (every block is dyeable + shapeable in-game; shaped blocks orient at placement).
            FormHeader("DYE / SHAPE BRUSH");
            FormLabel("Dye colour (hex RRGGBB, blank = none)");
            InputRow(HexOf(_brushTint), v => _brushTint = ParseHex(v));
            FormLabel("Glow colour (hex RRGGBB, blank = none)");
            InputRow(HexOf(_brushGlow), v => _brushGlow = ParseHex(v));
            Stepper("Shape (0=Cube…8=Cylinder)", () => _brushShape, v => _brushShape = Mathf.Clamp(Mathf.RoundToInt(v), 0, 8), 0f, 8f, 1f, "0");
            Stepper("Orientation (×90°, key R)", () => _brushOrient, v => _brushOrient = Mathf.RoundToInt(v) & 3, 0f, 3f, 1f, "0");

            _blocksLabel = FormLabel("Blocks placed: 0");

            // CRAFT COST is the last section so its dynamic rows can simply append to the form.
            var head = Row(_form, 28f);
            UiKit.AddText(head, 4f, 0f, 240f, 28f, "CRAFT COST", 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(head, 252f, 0f, 104f, 26f, "+ add", () =>
            {
                _craftCost.Add(new CostRow { Item = "iron_plate", Count = 1 });
                RefreshCostRows();
            });

            RefreshCostRows();
        }

        private void AddPaletteRow(Transform parent, int index)
        {
            var row = Row(parent, 36f);
            var img = row.gameObject.AddComponent<Image>();
            img.sprite = UiKit.ButtonSprite;
            img.type = Image.Type.Sliced;

            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            int idx = index;
            btn.onClick.AddListener(() => Select(idx));

            var sw = new GameObject("Swatch", typeof(RectTransform));
            sw.transform.SetParent(row, false);
            UiKit.Place(sw, 10f, 9f, 18f, 18f);
            var swImg = sw.AddComponent<Image>();
            swImg.sprite = UiKit.SolidSprite;
            swImg.color = _palette[index].Color;
            swImg.raycastTarget = false;

            string tag = _palette[index].Kind == "marker" ? "◆ " : string.Empty;
            UiKit.AddText(row, 38f, 0f, 232f, 36f, tag + _palette[index].Label, 16, UiKit.TextCol);
            _palButtons.Add(img);
            _rowToPaletteIndex.Add(index);
        }

        /// <summary>Rebuilds the palette list from the search filter; rows carry their real
        /// <see cref="_palette"/> index so selection stays stable across filters.</summary>
        private void RebuildPaletteRows()
        {
            if (_palListParent == null)
            {
                return;
            }

            for (int i = _palListParent.childCount - 1; i >= 0; i--)
            {
                Destroy(_palListParent.GetChild(i).gameObject);
            }

            _palButtons.Clear();
            _rowToPaletteIndex.Clear();

            string q = _search.Trim().ToLowerInvariant();
            for (int i = 0; i < _palette.Length; i++)
            {
                var p = _palette[i];
                bool match = q.Length == 0
                    || (p.Label != null && p.Label.ToLowerInvariant().Contains(q))
                    || (p.Id != null && p.Id.ToLowerInvariant().Contains(q));
                if (match)
                {
                    AddPaletteRow(_palListParent, i);
                }
            }

            Select(_selected);
        }

        private void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < _palButtons.Count; i++)
            {
                _palButtons[i].color = _rowToPaletteIndex[i] == index ? new Color(0.45f, 0.82f, 1f, 1f) : new Color(0.62f, 0.68f, 0.76f, 1f);
            }
        }

        private static int ParseHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return 0;
            }

            s = s.Trim().TrimStart('#');
            return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? (v & 0xFFFFFF) : 0;
        }

        private static string HexOf(int rgb) => rgb == 0 ? string.Empty : rgb.ToString("x6");

        private void RefreshCostRows()
        {
            int i = 0;
            foreach (var c in _craftCost)
            {
                var ui = i < _costPool.Count ? _costPool[i] : MakeCostRow();
                ui.Bound = null; // suppress notify while we set the displayed text
                ui.Item.SetTextWithoutNotify(c.Item);
                ui.Count.SetTextWithoutNotify(c.Count.ToString());
                ui.Bound = c;
                ui.Go.SetActive(true);
                i++;
            }

            for (; i < _costPool.Count; i++)
            {
                _costPool[i].Go.SetActive(false);
            }
        }

        private CostUi MakeCostRow()
        {
            var row = Row(_form, 30f);
            var ui = new CostUi { Go = row.gameObject };
            ui.Item = UiKit.AddInput(row, 4f, 2f, 200f, 26f, string.Empty, null, "item id");
            ui.Count = UiKit.AddInput(row, 210f, 2f, 72f, 26f, string.Empty, null);
            ui.Count.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddButton(row, 288f, 2f, 30f, 26f, "×", () =>
            {
                if (ui.Bound != null)
                {
                    _craftCost.Remove(ui.Bound);
                    RefreshCostRows();
                }
            });

            ui.Item.onValueChanged.AddListener(v => { if (ui.Bound != null) ui.Bound.Item = v; });
            ui.Count.onValueChanged.AddListener(v => { if (ui.Bound != null && int.TryParse(v, out var c)) ui.Bound.Count = Mathf.Max(0, c); });
            _costPool.Add(ui);
            return ui;
        }

        // --- small uGUI form builders ---

        private static RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return go.GetComponent<RectTransform>();
        }

        private Text FormLabel(string text)
        {
            var row = Row(_form, 22f);
            return UiKit.AddText(row, 4f, 0f, 352f, 22f, text, 15, UiKit.TextCol);
        }

        private void FormHeader(string text)
        {
            Row(_form, 8f); // spacer
            var row = Row(_form, 24f);
            UiKit.AddText(row, 4f, 0f, 352f, 24f, text, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        private void InputRow(string value, Action<string> onChange)
        {
            var row = Row(_form, 32f);
            UiKit.AddInput(row, 4f, 2f, 352f, 28f, value, onChange);
        }

        private void Stepper(string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt)
        {
            var row = Row(_form, 30f);
            UiKit.AddText(row, 4f, 0f, 156f, 30f, label, 15, UiKit.TextCol);
            var valTxt = UiKit.AddText(row, 196f, 0f, 72f, 30f, get().ToString(fmt), 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(row, 164f, 1f, 28f, 28f, "−", () => { set(Mathf.Clamp(get() - step, min, max)); valTxt.text = get().ToString(fmt); });
            UiKit.AddButton(row, 272f, 1f, 28f, 28f, "+", () => { set(Mathf.Clamp(get() + step, min, max)); valTxt.text = get().ToString(fmt); });
        }

        private static RectTransform RightPanel(Transform root, float w, float h)
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

        // ----------------------------- export -----------------------------

        [Serializable] private sealed class ExportCellJson { public int x, y, z; public string kind, id; public int tint, glow, shape; }
        [Serializable] private sealed class ExportLayoutJson { public int width, height, length; public List<ExportCellJson> cells = new(); }
        [Serializable] private sealed class ExportCostJson { public string item; public int count; }
        [Serializable] private sealed class ExportShipJson
        {
            public string key, name, description, requiredBlueprint, layout;
            public float baseHull, baseShield, flightSpeed, handling;
            public int cargoSlots;
            public List<ExportCostJson> craftCost = new();
        }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                _status = "Give the ship a key first.";
                return;
            }

            int maxX = 0, maxY = 0, maxZ = 0;
            var layout = new ExportLayoutJson();
            foreach (var kv in _design)
            {
                var d = kv.Value;
                layout.cells.Add(new ExportCellJson
                {
                    x = kv.Key.X, y = kv.Key.Y, z = kv.Key.Z,
                    kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, id = d.Id,
                    tint = d.Tint, glow = d.Glow, shape = d.Shape,
                });
                maxX = Mathf.Max(maxX, kv.Key.X);
                maxY = Mathf.Max(maxY, kv.Key.Y);
                maxZ = Mathf.Max(maxZ, kv.Key.Z);
            }

            layout.width = maxX + 1;
            layout.height = maxY + 1;
            layout.length = maxZ + 1;

            var ship = new ExportShipJson
            {
                key = key,
                name = _shipName,
                description = _desc,
                requiredBlueprint = string.IsNullOrWhiteSpace(_requiredBlueprint) ? null : _requiredBlueprint.Trim(),
                layout = $"{key}.json",
                baseHull = Mathf.Round(_hull),
                baseShield = Mathf.Round(_shield),
                flightSpeed = (float)Math.Round(_flightSpeed, 2),
                handling = (float)Math.Round(_handling, 2),
                cargoSlots = _cargo,
            };

            foreach (var c in _craftCost)
            {
                if (!string.IsNullOrWhiteSpace(c.Item) && c.Count > 0)
                {
                    ship.craftCost.Add(new ExportCostJson { item = c.Item.Trim(), count = c.Count });
                }
            }

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "ship_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ship.json"), JsonUtility.ToJson(ship, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));
                _status = $"Saved '{key}' ({_design.Count} blocks) to:\n{dir}\nRun tools/merge_ship.py to add it to the game.";
            }
            catch (Exception e)
            {
                _status = "Export failed: " + e.Message;
            }
        }

        private GameObject _loadPicker;

        /// <summary>Lists the saved ship designs (under <c>ship_exports/</c>) and lets you load one back in to
        /// keep editing it.</summary>
        private void OpenLoadPicker()
        {
            if (_loadPicker != null)
            {
                Destroy(_loadPicker);
            }

            var keys = new List<string>();
            string root = Path.Combine(Application.persistentDataPath, "ship_exports");
            if (Directory.Exists(root))
            {
                foreach (var d in Directory.GetDirectories(root))
                {
                    if (File.Exists(Path.Combine(d, "layout.json")))
                    {
                        keys.Add(Path.GetFileName(d));
                    }
                }
            }

            var panel = UiKit.AddPanel(_canvas.transform, 700f, 280f, 520f, 520f, UiKit.Panel);
            _loadPicker = panel.gameObject;
            UiKit.AddText(panel.transform, 20f, 14f, 480f, 28f, "LOAD DESIGN", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (keys.Count == 0)
            {
                UiKit.AddText(panel.transform, 20f, 60f, 480f, 28f, "No saved ship designs yet.", 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            }
            else
            {
                for (int i = 0; i < Mathf.Min(keys.Count, 11); i++)
                {
                    string k = keys[i];
                    UiKit.AddButton(panel.transform, 20f, 54f + i * 38f, 480f, 34f, "▸  " + k, () =>
                    {
                        LoadDesign(k);
                        Destroy(_loadPicker);
                        _loadPicker = null;
                    });
                }
            }

            UiKit.AddButton(panel.transform, 20f, 472f, 480f, 38f, "Close", () => { Destroy(_loadPicker); _loadPicker = null; });
        }

        /// <summary>Clears the current build and rebuilds it from a saved design's <c>layout.json</c>.</summary>
        private void LoadDesign(string key)
        {
            string dir = Path.Combine(Application.persistentDataPath, "ship_exports", key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                _status = "Design not found.";
                if (_statusLabel != null) _statusLabel.text = _status;
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<ExportLayoutJson>(File.ReadAllText(layoutPath));

                foreach (var go in _cells.Values)
                {
                    Destroy(go);
                }

                _cells.Clear();
                _design.Clear();

                if (layout?.cells != null)
                {
                    foreach (var c in layout.cells)
                    {
                        var cell = new Vector3i(c.x, c.y, c.z);
                        var pal = System.Array.Find(_palette, p => p.Id == c.id);
                        if (pal.Id == null || !InBounds(cell) || _design.ContainsKey(cell))
                        {
                            continue; // unknown palette id or out of bounds
                        }

                        var data = new CellData
                        {
                            Id = c.id,
                            Kind = string.IsNullOrEmpty(c.kind) ? pal.Kind : c.kind,
                            Tint = c.tint, Glow = c.glow, Shape = c.shape,
                        };
                        PlaceCellData(cell, pal, data);
                    }
                }

                string shipPath = Path.Combine(dir, "ship.json");
                if (File.Exists(shipPath) && JsonUtility.FromJson<ExportShipJson>(File.ReadAllText(shipPath)) is { } ship)
                {
                    _key = string.IsNullOrEmpty(ship.key) ? key : ship.key;
                    _shipName = string.IsNullOrEmpty(ship.name) ? _shipName : ship.name;
                }
                else
                {
                    _key = key;
                }

                _status = $"Loaded '{key}' ({_design.Count} blocks).";
                RebuildForm();
            }
            catch (Exception e)
            {
                _status = "Load failed: " + e.Message;
                if (_statusLabel != null) _statusLabel.text = _status;
            }
        }

        /// <summary>Rebuilds the right-hand form (key/name + stats) so it reflects a freshly loaded design.</summary>
        private void RebuildForm()
        {
            if (_form == null)
            {
                return;
            }

            for (int i = _form.childCount - 1; i >= 0; i--)
            {
                Destroy(_form.GetChild(i).gameObject);
            }

            BuildForm();
            if (_statusLabel != null) _statusLabel.text = _status;
        }

        private static string Slug(string s)
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

        private static Material Lit(Color color, Texture2D tex)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(color) };
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(color) };
        }
    }
}
