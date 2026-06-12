using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Standalone structure editor (menu tool, sibling of <see cref="ShipEditor"/>) for building a
    /// <b>space station</b> or a <b>settlement</b> (village/town) by hand: an empty build room you fly
    /// through (hold RMB to look, WASD/QE to move) and place blocks + interaction markers into. A side
    /// panel sets the name + size tier; <b>Save</b> writes a template bundle (structure.json +
    /// layout.json) a developer folds into the game's template pools with tools/merge_structure.py.
    /// Self-contained on the client; the palette + size tiers switch by <see cref="EditorMode"/>.
    /// </summary>
    public sealed class StructureEditor : MonoBehaviour
    {
        public enum Mode { Station, Settlement }

        public AppShell Shell;
        public Mode EditorMode = Mode.Station;

        private const int MaxW = 32, MaxH = 16, MaxL = 32;

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        private readonly Dictionary<Vector3i, string> _design = new();
        private readonly Dictionary<Vector3i, GameObject> _cells = new();
        private readonly Dictionary<string, Material> _mats = new();

        private struct Pal { public string Id, Label, Kind; public Color Color; }
        private Pal[] _palette;
        private int _selected;
        private string[] _tiers;
        private int _tier;

        private string _key = "my_structure";
        private string _name = "My Structure";
        private string _status = string.Empty;
        private bool _mouseOverUi;

        private void Start()
        {
            _palette = EditorMode == Mode.Station ? StationPalette() : SettlementPalette();
            _tiers = EditorMode == Mode.Station
                ? new[] { "small", "medium", "large", "huge" }
                : new[] { "hamlet", "village", "town", "city" };
            _key = EditorMode == Mode.Station ? "my_station" : "my_settlement";
            _name = EditorMode == Mode.Station ? "My Station" : "My Settlement";

            var camGo = new GameObject("EditorCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            _cam.farClipPlane = 500f;
            camGo.AddComponent<AudioListener>();
            _cam.transform.position = new Vector3(MaxW / 2f, 8f, -12f);
            _pitch = 18f;
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            BuildRoom();
            BuildUi();
        }

        private static Pal P(string id, string label, string kind, Color c) => new Pal { Id = id, Label = label, Kind = kind, Color = c };

        private Pal[] StationPalette() => new[]
        {
            P("iron_wall", "Hull", "block", new Color(0.55f, 0.57f, 0.62f)),
            P("glass", "Viewport", "block", new Color(0.45f, 0.8f, 0.95f)),
            P("force_field", "Energy field", "block", new Color(0.35f, 0.8f, 1f)),
            P("light", "Light", "block", new Color(1f, 0.95f, 0.55f)),
            P("hangar", "Hangar marker", "marker", new Color(0.35f, 0.4f, 0.46f)),
            P("vendor", "Vendor marker", "marker", new Color(0.9f, 0.75f, 0.2f)),
            P("mission_board", "Mission board", "marker", new Color(0.4f, 0.7f, 0.95f)),
            P("heal_tank", "Heal tank", "marker", new Color(0.4f, 0.9f, 0.6f)),
            P("quarters", "Quarters", "marker", new Color(0.6f, 0.45f, 0.8f)),
            P("console", "Console", "marker", new Color(0.3f, 0.6f, 0.95f)),
        };

        private Pal[] SettlementPalette() => new[]
        {
            P("stone", "Stone wall", "block", new Color(0.50f, 0.50f, 0.52f)),
            P("iron_wall", "Metal wall", "block", new Color(0.55f, 0.57f, 0.62f)),
            P("glass", "Window", "block", new Color(0.45f, 0.8f, 0.95f)),
            P("ladder", "Ladder", "block", new Color(0.6f, 0.45f, 0.3f)),
            P("stairs", "Stairs", "block", new Color(0.65f, 0.55f, 0.4f)),
            P("light", "Lamp", "block", new Color(1f, 0.95f, 0.55f)),
            P("vendor", "Vendor marker", "marker", new Color(0.9f, 0.75f, 0.2f)),
            P("mission_board", "Mission board", "marker", new Color(0.4f, 0.7f, 0.95f)),
            P("npc", "Inhabitant", "marker", new Color(0.85f, 0.6f, 0.5f)),
            P("door_slide", "Slide door", "marker", new Color(0.40f, 0.85f, 0.95f)),
            P("door_hinge", "Hinge door", "marker", new Color(0.60f, 0.40f, 0.20f)),
        };

        private void BuildRoom()
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "BuildFloor";
            _floor.transform.position = new Vector3(MaxW / 2f, -0.5f, MaxL / 2f);
            _floor.transform.localScale = new Vector3(MaxW, 1f, MaxL);
            _floor.transform.SetParent(transform, false);
            _floor.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.10f, 0.13f, 0.18f), null);

            var lineMat = Unlit(new Color(0.2f, 0.35f, 0.45f, 1f));
            for (int x = 0; x <= MaxW; x += 2)
            {
                GridLine(new Vector3(x, 0.02f, MaxL / 2f), new Vector3(0.03f, 0.02f, MaxL), lineMat);
            }

            for (int z = 0; z <= MaxL; z += 2)
            {
                GridLine(new Vector3(MaxW / 2f, 0.02f, z), new Vector3(MaxW, 0.02f, 0.03f), lineMat);
            }

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

            float speed = (Input.GetKey(KeyCode.LeftShift) ? 22f : 11f) * Time.deltaTime;
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += _cam.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= _cam.transform.forward;
            if (Input.GetKey(KeyCode.D)) move += _cam.transform.right;
            if (Input.GetKey(KeyCode.A)) move -= _cam.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
            _cam.transform.position += move * speed;

            _mouseOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (_blocksLabel != null)
            {
                _blocksLabel.text = $"Placed: {_design.Count}";
            }

            if (!flying && !_mouseOverUi)
            {
                if (Input.GetMouseButtonDown(0)) TryPlace();
                else if (Input.GetMouseButtonDown(2)) TryRemove();
            }
        }

        private void TryPlace()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 300f))
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
            if (Physics.Raycast(ray, out var hit, 300f) && hit.collider.gameObject != _floor)
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
            go.transform.localScale = Vector3.one * (pal.Kind == "marker" ? 0.6f : 0.98f);
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

        // ----------------------------- export -----------------------------

        [Serializable] private sealed class CellJson { public int x, y, z; public string kind, id; }
        [Serializable] private sealed class LayoutJson { public int width, height, length; public List<CellJson> cells = new(); }
        [Serializable] private sealed class MetaJson { public string key, name, kind, tier, layout; }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus("Give it a key first.");
                return;
            }

            int maxX = 0, maxY = 0, maxZ = 0;
            var layout = new LayoutJson();
            foreach (var kv in _design)
            {
                var pal = Array.Find(_palette, p => p.Id == kv.Value);
                layout.cells.Add(new CellJson { x = kv.Key.X, y = kv.Key.Y, z = kv.Key.Z, kind = pal.Kind ?? "block", id = kv.Value });
                maxX = Mathf.Max(maxX, kv.Key.X);
                maxY = Mathf.Max(maxY, kv.Key.Y);
                maxZ = Mathf.Max(maxZ, kv.Key.Z);
            }

            layout.width = maxX + 1;
            layout.height = maxY + 1;
            layout.length = maxZ + 1;

            string modeName = EditorMode == Mode.Station ? "station" : "settlement";
            var meta = new MetaJson { key = key, name = _name, kind = modeName, tier = _tiers[_tier], layout = $"{key}.json" };

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, modeName + "_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "structure.json"), JsonUtility.ToJson(meta, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));
                SetStatus($"Saved '{key}' ({_design.Count} cells) to:\n{dir}\nRun tools/merge_structure.py to add it to the pool.");
            }
            catch (Exception e)
            {
                SetStatus("Export failed: " + e.Message);
            }
        }

        private GameObject _loadPicker;

        private string ExportsRoot => Path.Combine(Application.persistentDataPath,
            (EditorMode == Mode.Station ? "station" : "settlement") + "_exports");

        /// <summary>Lists saved designs of the current mode and lets you load one back in to keep editing.</summary>
        private void OpenLoadPicker()
        {
            if (_loadPicker != null)
            {
                Destroy(_loadPicker);
            }

            var keys = new List<string>();
            if (Directory.Exists(ExportsRoot))
            {
                foreach (var d in Directory.GetDirectories(ExportsRoot))
                {
                    if (File.Exists(Path.Combine(d, "layout.json")))
                    {
                        keys.Add(Path.GetFileName(d));
                    }
                }
            }

            var panel = UiKit.AddPanel(_canvas.transform, 700f, 260f, 520f, 520f, UiKit.Panel);
            _loadPicker = panel.gameObject;
            UiKit.AddText(panel.transform, 20f, 14f, 480f, 28f, L("ui.struct.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (keys.Count == 0)
            {
                UiKit.AddText(panel.transform, 20f, 60f, 480f, 28f, L("ui.save.none"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            }
            else
            {
                for (int i = 0; i < Mathf.Min(keys.Count, 11); i++)
                {
                    string k = keys[i];
                    UiKit.AddButton(panel.transform, 20f, 54f + i * 38f, 480f, 34f, "▸  " + k, () => LoadDesign(k));
                }
            }

            UiKit.AddButton(panel.transform, 20f, 472f, 480f, 38f, L("ui.menu.back"), () => { Destroy(_loadPicker); _loadPicker = null; });
        }

        /// <summary>Clears the current build and rebuilds it (+ the form) from a saved design.</summary>
        private void LoadDesign(string key)
        {
            string dir = Path.Combine(ExportsRoot, key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                SetStatus("Design not found.");
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<LayoutJson>(File.ReadAllText(layoutPath));

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
                            continue;
                        }

                        PlaceCell(cell, pal);
                    }
                }

                string metaPath = Path.Combine(dir, "structure.json");
                if (File.Exists(metaPath) && JsonUtility.FromJson<MetaJson>(File.ReadAllText(metaPath)) is { } meta)
                {
                    _key = string.IsNullOrEmpty(meta.key) ? key : meta.key;
                    _name = string.IsNullOrEmpty(meta.name) ? _name : meta.name;
                    int ti = System.Array.IndexOf(_tiers, meta.tier);
                    if (ti >= 0) _tier = ti;
                }
                else
                {
                    _key = key;
                }

                _status = $"Loaded '{key}' ({_design.Count} cells).";
                RebuildUi();
            }
            catch (Exception e)
            {
                SetStatus("Load failed: " + e.Message);
            }
        }

        /// <summary>Rebuilds the editor UI so the key/name/tier fields reflect a freshly loaded design (the
        /// placed cells live under the editor transform, not the canvas, so they survive the rebuild).</summary>
        private void RebuildUi()
        {
            _loadPicker = null; // destroyed along with the old canvas
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            BuildUi();
            SetStatus(_status);
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
            var m = new Material(shader) { color = color };
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = color };
        }

        // ----------------------------- UI (uGUI) -----------------------------

        private const float PanelH = 1048f;
        private Canvas _canvas;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Text _tierLabel;
        private readonly List<Image> _palButtons = new();

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void SetStatus(string s) { _status = s; if (_statusLabel != null) _statusLabel.text = s; }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Structure Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // Left: palette.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, L("ui.struct.palette"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var palList = UiKit.ScrollList(pal.transform, 10f, 48f, 280f, PanelH - 60f);
            for (int i = 0; i < _palette.Length; i++)
            {
                AddPaletteRow(palList, i);
            }

            Select(_selected);

            // Right: metadata.
            var meta = RightPanel(root, 380f, PanelH);
            string title = EditorMode == Mode.Station ? L("ui.struct.title_station") : L("ui.struct.title_settlement");
            UiKit.AddText(meta, 16f, 12f, 348f, 26f, title, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            float y = 56f;
            UiKit.AddText(meta, 16f, y, 348f, 22f, L("ui.struct.key"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;
            UiKit.AddInput(meta, 16f, y, 348f, 30f, _key, v => _key = v);
            y += 40f;
            UiKit.AddText(meta, 16f, y, 348f, 22f, L("ui.struct.name"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;
            UiKit.AddInput(meta, 16f, y, 348f, 30f, _name, v => _name = v);
            y += 44f;

            // Size tier stepper.
            UiKit.AddText(meta, 16f, y, 150f, 30f, L("ui.struct.tier"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _tierLabel = UiKit.AddText(meta, 176f, y, 120f, 30f, _tiers[_tier], 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 300f, y, 30f, 30f, "→", () => { _tier = (_tier + 1) % _tiers.Length; _tierLabel.text = _tiers[_tier]; });
            y += 44f;

            _blocksLabel = UiKit.AddText(meta, 16f, y, 348f, 24f, "Placed: 0", 15, UiKit.TextCol, TextAnchor.MiddleLeft);

            // Footer.
            _statusLabel = UiKit.AddText(meta, 16f, PanelH - 150f, 352f, 70f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(meta, 16f, PanelH - 70f, 150f, 40f, L("ui.struct.save"), Export);
            UiKit.AddButton(meta, 172f, PanelH - 70f, 86f, 40f, L("ui.struct.load"), OpenLoadPicker);
            UiKit.AddButton(meta, 264f, PanelH - 70f, 100f, 40f, L("ui.menu.back"), () => Shell?.CloseStructureEditor());

            // Controls hint.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(1200f, 24f);
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

        private void AddPaletteRow(Transform parent, int index)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 36f;
            var rt = row.GetComponent<RectTransform>();

            var img = row.AddComponent<Image>();
            img.sprite = UiKit.ButtonSprite;
            img.type = Image.Type.Sliced;

            var btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            int idx = index;
            btn.onClick.AddListener(() => Select(idx));

            var sw = new GameObject("Swatch", typeof(RectTransform));
            sw.transform.SetParent(rt, false);
            UiKit.Place(sw, 10f, 9f, 18f, 18f);
            var swImg = sw.AddComponent<Image>();
            swImg.sprite = UiKit.SolidSprite;
            swImg.color = _palette[index].Color;
            swImg.raycastTarget = false;

            string tag = _palette[index].Kind == "marker" ? "◆ " : string.Empty;
            UiKit.AddText(rt, 38f, 0f, 232f, 36f, tag + _palette[index].Label, 15, UiKit.TextCol);
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

        private string L(string key) => Shell?.L(key) ?? key;
    }
}
