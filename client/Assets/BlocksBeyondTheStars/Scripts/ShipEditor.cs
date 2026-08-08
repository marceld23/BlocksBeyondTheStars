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

        private const int MaxW = 48, MaxH = 32, MaxL = 48;
        private const float RaycastDist = 1200f;

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        /// <summary>One authored cell: palette id + kind plus the in-game per-voxel modifiers (dye/glow
        /// colour 0xRRGGBB, packed shape+orientation). Elements/stations carry no modifiers.</summary>
        private struct CellData { public string Id; public string Kind; public int Tint, Glow, Shape; }

        private readonly Dictionary<Vector3i, CellData> _design = new();   // cell -> authored cell (export source)
        private EditorVoxelChunkView _view;                                // chunked combined-mesh renderer

        private BlockTextureAtlas _atlas;                                  // editor-local atlas for palette icons + cell colours
        private EditorPaletteKit.Entry[] _palette;
        private int _selected;

        // Brush: dye/glow colour + shape + orientation applied to newly placed BLOCK cells (elements +
        // stations ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / cube.
        private int _brushTint, _brushGlow, _brushShape, _brushOrient;
        private string _search = string.Empty;

        /// <summary>The 9 in-game block shapes (index = BlockShape enum; localized via <c>ui.shape.*</c>).</summary>
        private static readonly string[] ShapeSlugs = { "cube", "slab", "pyramid", "dome", "sphere", "ramp", "stairs", "cone", "cylinder" };

        private string ShapeName(int i) => L("ui.shape." + ShapeSlugs[Mathf.Clamp(i, 0, ShapeSlugs.Length - 1)]);

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
            // Editor-local block atlas: gives the palette (and the placed cells) the real material look.
            _atlas = Shell != null && Shell.Content != null ? new BlockTextureAtlas(Shell.Content) : null;
            _palette = BuildPalette();

            var camGo = new GameObject("EditorCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            _cam.farClipPlane = 800f;
            camGo.AddComponent<AudioListener>();
            _cam.transform.position = new Vector3(MaxW / 2f, 10f, -18f);
            _yaw = 0f;
            _pitch = 15f;
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            _view = new EditorVoxelChunkView(transform);
            BuildRoom();
            BuildUi();
        }

        private string L(string key) => Shell != null ? Shell.L(key) : key;

        /// <summary>A ship element/station palette entry: localized via <c>ui.part.*</c>, grouped under "parts".</summary>
        private EditorPaletteKit.Entry P(string id, string kind, Color c) => new EditorPaletteKit.Entry
        {
            Id = id, Label = L("ui.part." + id), Kind = kind, Group = "parts", Color = c,
        };

        /// <summary>The ship palette: the special ship elements + stations + weapons, followed by every
        /// placeable block from the loaded content — localized, category-grouped and iconed with its real
        /// atlas tile (see <see cref="EditorPaletteKit"/>). Built once from <see cref="AppShell.Content"/>.</summary>
        private EditorPaletteKit.Entry[] BuildPalette()
        {
            var list = new List<EditorPaletteKit.Entry>
            {
                P("light", "element", new Color(1f, 0.95f, 0.55f)),
                P("headlight", "element", new Color(0.95f, 0.97f, 1f)),
                P("light_red", "element", new Color(1f, 0.3f, 0.3f)),
                P("light_green", "element", new Color(0.3f, 1f, 0.4f)),
                P("engine", "element", new Color(1f, 0.55f, 0.2f)),
                P("hatch", "element", new Color(0.7f, 0.5f, 0.3f)),
                P("door_slide", "element", new Color(0.4f, 0.85f, 0.95f)),
                P("door_hinge", "element", new Color(0.55f, 0.8f, 0.7f)),
                P("door_energy", "element", new Color(0.35f, 0.80f, 1f)), // air-curtain door (#793); ship doors all register as energy anyway

                P("cockpit", "station", new Color(0.3f, 0.6f, 0.95f)),
                P("reactor", "station", new Color(0.9f, 0.35f, 0.3f)),
                P("life_support", "station", new Color(0.4f, 0.85f, 0.55f)),
                P("workshop", "station", new Color(0.75f, 0.65f, 0.4f)),
                P("medbay", "station", new Color(0.9f, 0.95f, 1f)),
                P("quarters", "station", new Color(0.6f, 0.45f, 0.8f)),
                P("cargo", "station", new Color(0.7f, 0.6f, 0.45f)),
                P("hangar", "station", new Color(0.35f, 0.4f, 0.46f)),
                P("ship_laser_basic", "station", new Color(0.45f, 1f, 1f)),
                P("ship_cannon_1", "station", new Color(0.95f, 0.55f, 0.4f)),
            };

            list.AddRange(EditorPaletteKit.BlockEntries(Shell, _atlas));
            return list.ToArray();
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

            float speed = (Input.GetKey(KeyCode.LeftShift) ? 30f : 14f) * Time.deltaTime;
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
            if (_blocksLabel != null && _lastPlaced != _design.Count)
            {
                _lastPlaced = _design.Count;
                _blocksLabel.text = string.Format(L("ui.ed.placed"), _design.Count);
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

            _view.Flush(); // upload any chunk meshes touched by this frame's edits
        }

        /// <summary>Resolves the cell a placement would land in: the floor column, or the empty cell just
        /// outside the hit face (the chunk mesh is authored in world coords, so the hit point + normal locate
        /// the cell directly — no per-cell GameObject to read a transform from).</summary>
        private bool TryGetTargetCell(out Vector3i cell)
        {
            cell = default;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, RaycastDist))
            {
                return false;
            }

            if (hit.collider.gameObject == _floor)
            {
                cell = new Vector3i(Mathf.FloorToInt(hit.point.x), 0, Mathf.FloorToInt(hit.point.z));
            }
            else
            {
                Vector3 outside = hit.point + hit.normal * 0.5f; // step out of the hit cell into its empty neighbour
                cell = new Vector3i(Mathf.FloorToInt(outside.x), Mathf.FloorToInt(outside.y), Mathf.FloorToInt(outside.z));
            }

            return true;
        }

        /// <summary>Resolves the actual occupied cell under the cursor (for removal); false if the ray misses
        /// every block or hits only the floor.</summary>
        private bool TryGetHitCell(out Vector3i cell)
        {
            cell = default;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, RaycastDist) || hit.collider.gameObject == _floor)
            {
                return false;
            }

            Vector3 inside = hit.point - hit.normal * 0.5f; // step into the hit cell
            cell = new Vector3i(Mathf.FloorToInt(inside.x), Mathf.FloorToInt(inside.y), Mathf.FloorToInt(inside.z));
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
            if (TryGetHitCell(out var cell) && _design.ContainsKey(cell))
            {
                _design.Remove(cell);
                _view.Remove(cell);
            }
        }

        private void PlaceCell(Vector3i cell, EditorPaletteKit.Entry pal)
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

        private void PlaceCellData(Vector3i cell, EditorPaletteKit.Entry pal, CellData data)
        {
            // Dye wins for the base colour; a pure glow cell shows its glow colour; else the palette swatch
            // (mirrors the in-game look). The chunked view bakes the directional shading + face culling.
            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : pal.Color);

            _design[cell] = data;
            _view.Set(cell, new EditorVoxelChunkView.Cell
            {
                Color = baseCol,
                Glow = data.Glow != 0,
                Shape = data.Shape,
                Marker = false, // the ship editor has no markers (elements + stations are solid anchors)
            });
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        // ----------------------------- UI (modern uGUI) -----------------------------

        private const float PanelH = 1048f;

        private Canvas _canvas;
        private RectTransform _form;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Transform _palListParent;
        private PaletteListUi _palList;
        private int _lastPlaced = -1;
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
            _view?.Dispose();
            _atlas?.Destroy(); // palette sprites reference this texture; the editor owns it (#423 lesson)
            _atlas = null;
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

            // Left: block/part palette (elements + stations + every placeable block), grouped by
            // category, with a search filter.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, L("ui.ship.palette"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(pal.transform, 12f, 42f, 276f, 28f, _search, v => { _search = v ?? string.Empty; _palList.Rebuild(_search); }, L("ui.pal.search"));
            _palListParent = UiKit.ScrollList(pal.transform, 10f, 78f, 280f, PanelH - 90f);
            _palList = new PaletteListUi(Shell, _palListParent, _palette, _selected);
            _palList.OnSelected = i => _selected = i;
            _palList.Rebuild(_search);

            // Right: ship metadata + stats + cost (anchored to the top-right so it hugs the edge).
            var meta = RightPanel(root, 380f, PanelH);
            UiKit.AddText(meta, 16f, 12f, 348f, 26f, L("ui.ship.title"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            _form = UiKit.ScrollList(meta, 8f, 48f, 364f, PanelH - 48f - 164f, 3f);
            BuildForm();

            // Pinned footer: status, then save on a full-width row of its own (so the label never has
            // to shrink), then load + back sharing the row below.
            _statusLabel = UiKit.AddText(meta, 12f, PanelH - 160f, 356f, 46f, string.Empty, 14, UiKit.Ok);
            _statusLabel.alignment = TextAnchor.UpperLeft;
            UiKit.AddButton(meta, 12f, PanelH - 110f, 356f, 42f, L("ui.struct.save"), Export);
            UiKit.AddButton(meta, 12f, PanelH - 62f, 172f, 38f, L("ui.struct.load"), OpenLoadPicker);
            UiKit.AddButton(meta, 196f, PanelH - 62f, 172f, 38f, L("ui.menu.back"), () => Shell?.CloseShipEditor());

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
            hint.text = L("ui.struct.hint");
        }

        private void BuildForm()
        {
            FormLabel(L("ui.struct.key"));
            InputRow(_key, v => _key = v);
            FormLabel(L("ui.struct.name"));
            InputRow(_shipName, v => _shipName = v);
            FormLabel(L("ui.ship.desc"));
            InputRow(_desc, v => _desc = v);
            FormLabel(L("ui.ship.blueprint"));
            InputRow(_requiredBlueprint, v => _requiredBlueprint = v);

            FormHeader(L("ui.ship.stats"));
            Stepper(L("ui.ship.hull"), () => _hull, v => _hull = v, 20f, 500f, 10f, "0");
            Stepper(L("ui.ship.shield"), () => _shield, v => _shield = v, 0f, 300f, 10f, "0");
            Stepper(L("ui.ship.speed"), () => _flightSpeed, v => _flightSpeed = v, 0.4f, 2.5f, 0.05f, "0.00");
            Stepper(L("ui.ship.handling"), () => _handling, v => _handling = v, 0.4f, 2.5f, 0.05f, "0.00");
            Stepper(L("ui.ship.cargo"), () => _cargo, v => _cargo = Mathf.RoundToInt(v), 12f, 240f, 4f, "0");

            // Block brush: dye + glow colour + shape + orientation applied to newly placed BLOCK cells
            // (every block is dyeable + shapeable in-game; shaped blocks orient at placement).
            FormHeader(L("ui.ship.brush"));
            FormLabel(L("ui.ship.dye_hex"));
            InputRow(HexOf(_brushTint), v => _brushTint = ParseHex(v));
            FormLabel(L("ui.ship.glow_hex"));
            InputRow(HexOf(_brushGlow), v => _brushGlow = ParseHex(v));
            Stepper(L("ui.struct.shape"), () => _brushShape, v => _brushShape = Mathf.Clamp(Mathf.RoundToInt(v), 0, 8), 0f, 8f, 1f, "0",
                v => ShapeName(Mathf.RoundToInt(v)));
            Stepper(L("ui.struct.orient"), () => _brushOrient, v => _brushOrient = Mathf.RoundToInt(v) & 3, 0f, 3f, 1f, "0",
                v => (Mathf.RoundToInt(v) * 90) + "°");

            _lastPlaced = _design.Count;
            _blocksLabel = FormLabel(string.Format(L("ui.ed.placed"), _design.Count));

            // CRAFT COST is the last section so its dynamic rows can simply append to the form.
            var head = Row(_form, 28f);
            UiKit.AddText(head, 4f, 0f, 240f, 28f, L("ui.ship.cost"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(head, 252f, 0f, 104f, 26f, L("ui.ship.cost_add"), () =>
            {
                _craftCost.Add(new CostRow { Item = "iron_plate", Count = 1 });
                RefreshCostRows();
            });

            RefreshCostRows();
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
            ui.Item = UiKit.AddInput(row, 4f, 2f, 200f, 26f, string.Empty, null, L("ui.ship.cost_item"));
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

        private void Stepper(string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt,
            Func<float, string> display = null)
        {
            string Show() => display != null ? display(get()) : get().ToString(fmt);
            var row = Row(_form, 30f);
            UiKit.AddText(row, 4f, 0f, 156f, 30f, label, 15, UiKit.TextCol);
            var valTxt = UiKit.AddText(row, 196f, 0f, 72f, 30f, Show(), 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(row, 164f, 1f, 28f, 28f, "−", () => { set(Mathf.Clamp(get() - step, min, max)); valTxt.text = Show(); });
            UiKit.AddButton(row, 272f, 1f, 28f, 28f, "+", () => { set(Mathf.Clamp(get() + step, min, max)); valTxt.text = Show(); });
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
                _status = L("ui.ed.need_key");
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
                _status = string.Format(L("ui.ed.saved_ship"), key, _design.Count, dir);
            }
            catch (Exception e)
            {
                _status = string.Format(L("ui.ed.export_failed"), e.Message);
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

            // Shared menu-modal chrome (#588) — the picker had no scrim, so the editor behind it stayed
            // fully lit and clickable through the gaps.
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, 700f, 280f, 520f, 520f);
            _loadPicker = overlay;
            UiKit.AddText(panel.transform, 20f, 14f, 480f, 28f, L("ui.struct.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (keys.Count == 0)
            {
                UiKit.AddText(panel.transform, 20f, 60f, 480f, 28f, L("ui.ed.none"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
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

            UiKit.AddButton(panel.transform, 20f, 472f, 480f, 38f, L("ui.menu.back"), () => { Destroy(_loadPicker); _loadPicker = null; });
        }

        /// <summary>Clears the current build and rebuilds it from a saved design's <c>layout.json</c>.</summary>
        private void LoadDesign(string key)
        {
            string dir = Path.Combine(Application.persistentDataPath, "ship_exports", key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                _status = L("ui.ed.not_found");
                if (_statusLabel != null) _statusLabel.text = _status;
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<ExportLayoutJson>(File.ReadAllText(layoutPath));

                _view.Clear();
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

                _view.Flush(); // build all loaded chunks in one batch

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

                _status = string.Format(L("ui.ed.loaded"), key, _design.Count);
                RebuildForm();
            }
            catch (Exception e)
            {
                _status = string.Format(L("ui.ed.load_failed"), e.Message);
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
