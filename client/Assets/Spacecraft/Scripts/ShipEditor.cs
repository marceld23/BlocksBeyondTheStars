using System;
using System.Collections.Generic;
using System.IO;
using Spacecraft.Shared.Geometry;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Standalone ship-type editor (M27+ tooling; see docs/SHIP_TYPE_EDITOR_PLAN.md). An empty build
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

        private readonly Dictionary<Vector3i, string> _design = new();   // cell -> palette id
        private readonly Dictionary<Vector3i, GameObject> _cells = new();
        private readonly Dictionary<string, Material> _mats = new();

        private struct Pal { public string Id, Label, Kind; public Color Color; }
        private Pal[] _palette;
        private int _selected;

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
            _palette = new[]
            {
                P("iron_wall", "Hull", "block", new Color(0.55f, 0.57f, 0.62f)),
                P("glass", "Window", "block", new Color(0.45f, 0.8f, 0.95f)),
                P("light", "Light", "element", new Color(1f, 0.95f, 0.55f)),
                P("headlight", "Headlight", "element", new Color(0.95f, 0.97f, 1f)),
                P("light_red", "Port Light (red)", "element", new Color(1f, 0.3f, 0.3f)),
                P("light_green", "Starboard Light (green)", "element", new Color(0.3f, 1f, 0.4f)),
                P("engine", "Engine", "element", new Color(1f, 0.55f, 0.2f)),
                P("hatch", "Hatch", "element", new Color(0.7f, 0.5f, 0.3f)),
                P("door_slide", "Door", "element", new Color(0.4f, 0.85f, 0.95f)),
                P("cockpit", "Cockpit", "station", new Color(0.3f, 0.6f, 0.95f)),
                P("reactor", "Reactor", "station", new Color(0.9f, 0.35f, 0.3f)),
                P("life_support", "Life Support", "station", new Color(0.4f, 0.85f, 0.55f)),
                P("workshop", "Workshop", "station", new Color(0.75f, 0.65f, 0.4f)),
                P("medbay", "Medbay", "station", new Color(0.9f, 0.95f, 1f)),
                P("quarters", "Quarters", "station", new Color(0.6f, 0.45f, 0.8f)),
                P("cargo", "Cargo Hold", "station", new Color(0.7f, 0.6f, 0.45f)),
                P("hangar", "Hangar", "station", new Color(0.35f, 0.4f, 0.46f)),
                P("ship_laser_basic", "Laser Cannon", "station", new Color(0.45f, 1f, 1f)),
                P("ship_cannon_1", "Ship Cannon", "station", new Color(0.95f, 0.55f, 0.4f)),
            };

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
        }

        private void TryPlace()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 200f))
            {
                return;
            }

            Vector3i cell;
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Cell {pal.Id}";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            go.transform.localScale = Vector3.one * 0.98f;
            go.GetComponent<Renderer>().sharedMaterial = MatFor(pal);
            _cells[cell] = go;
            _design[cell] = pal.Id;
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        private Material MatFor(Pal pal)
        {
            if (!_mats.TryGetValue(pal.Id, out var m))
            {
                m = Lit(pal.Color, null);
                _mats[pal.Id] = m;
            }

            return m;
        }

        // ----------------------------- UI (modern uGUI) -----------------------------

        private const float PanelH = 1048f;

        private Canvas _canvas;
        private RectTransform _form;
        private Text _statusLabel;
        private Text _blocksLabel;
        private readonly List<Image> _palButtons = new();
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

            // Left: block/part palette.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, "BLOCKS & PARTS", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var palList = UiKit.ScrollList(pal.transform, 10f, 48f, 280f, PanelH - 60f);
            for (int i = 0; i < _palette.Length; i++)
            {
                AddPaletteRow(palList, i);
            }

            Select(_selected);

            // Right: ship metadata + stats + cost (anchored to the top-right so it hugs the edge).
            var meta = RightPanel(root, 380f, PanelH);
            UiKit.AddText(meta, 16f, 12f, 348f, 26f, "SHIP TYPE", 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            _form = UiKit.ScrollList(meta, 8f, 48f, 364f, PanelH - 48f - 116f, 3f);
            BuildForm();

            // Pinned footer: status + save + back.
            _statusLabel = UiKit.AddText(meta, 12f, PanelH - 112f, 356f, 44f, string.Empty, 14, UiKit.Ok);
            _statusLabel.alignment = TextAnchor.UpperLeft;
            UiKit.AddButton(meta, 12f, PanelH - 62f, 220f, 38f, "SAVE / EXPORT", Export);
            UiKit.AddButton(meta, 240f, PanelH - 62f, 128f, 38f, "← Back", () => Shell?.CloseShipEditor());

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

            UiKit.AddText(row, 38f, 0f, 232f, 36f, _palette[index].Label, 16, UiKit.TextCol);
            _palButtons.Add(img);
        }

        private void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < _palButtons.Count; i++)
            {
                _palButtons[i].color = i == index ? new Color(0.45f, 0.82f, 1f, 1f) : new Color(0.62f, 0.68f, 0.76f, 1f);
            }
        }

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

        [Serializable] private sealed class ExportCellJson { public int x, y, z; public string kind, id; }
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
                var pal = Array.Find(_palette, p => p.Id == kv.Value);
                layout.cells.Add(new ExportCellJson { x = kv.Key.X, y = kv.Key.Y, z = kv.Key.Z, kind = pal.Kind ?? "block", id = kv.Value });
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
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = color };
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = color };
        }
    }
}
