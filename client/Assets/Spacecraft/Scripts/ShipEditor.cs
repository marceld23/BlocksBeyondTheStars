using System;
using System.Collections.Generic;
using System.IO;
using Spacecraft.Shared.Geometry;
using UnityEngine;

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

        private string _status = string.Empty;
        private bool _mouseOverUi;
        private Vector2 _palScroll, _metaScroll;
        private GUIStyle _title, _head;

        private sealed class CostRow { public string Item = string.Empty; public int Count = 1; }

        private void Start()
        {
            _palette = new[]
            {
                P("iron_wall", "Hull", "block", new Color(0.55f, 0.57f, 0.62f)),
                P("glass", "Viewport", "block", new Color(0.45f, 0.8f, 0.95f)),
                P("light", "Light", "element", new Color(1f, 0.95f, 0.55f)),
                P("headlight", "Headlight", "element", new Color(0.95f, 0.97f, 1f)),
                P("light_red", "Port Light (red)", "element", new Color(1f, 0.3f, 0.3f)),
                P("light_green", "Starboard Light (green)", "element", new Color(0.3f, 1f, 0.4f)),
                P("engine", "Engine", "element", new Color(1f, 0.55f, 0.2f)),
                P("hatch", "Hatch", "element", new Color(0.7f, 0.5f, 0.3f)),
                P("cockpit", "Cockpit", "station", new Color(0.3f, 0.6f, 0.95f)),
                P("reactor", "Reactor", "station", new Color(0.9f, 0.35f, 0.3f)),
                P("life_support", "Life Support", "station", new Color(0.4f, 0.85f, 0.55f)),
                P("workshop", "Workshop", "station", new Color(0.75f, 0.65f, 0.4f)),
                P("medbay", "Medbay", "station", new Color(0.9f, 0.95f, 1f)),
                P("quarters", "Quarters", "station", new Color(0.6f, 0.45f, 0.8f)),
                P("cargo", "Cargo Hold", "station", new Color(0.7f, 0.6f, 0.45f)),
                P("hangar", "Hangar", "station", new Color(0.35f, 0.4f, 0.46f)),
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

            // Place (LMB) / remove (MMB) when not flying and not over a panel.
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

        // ----------------------------- UI -----------------------------

        private void OnGUI()
        {
            EnsureStyles();
            UiScale.Begin();
            float w = UiScale.Width, h = UiScale.Height;

            var palRect = new Rect(12f, 12f, 230f, h - 24f);
            var metaRect = new Rect(w - 372f, 12f, 360f, h - 24f);
            _mouseOverUi = palRect.Contains(Event.current.mousePosition) || metaRect.Contains(Event.current.mousePosition);

            DrawPalette(palRect);
            DrawMeta(metaRect);

            // Controls hint (bottom centre).
            GUI.Label(new Rect(w / 2f - 320f, h - 30f, 640f, 22f),
                "Hold RIGHT-MOUSE to look · WASD + Q/E (or Space/Ctrl) to fly · Shift = faster · LEFT-CLICK place · MIDDLE-CLICK remove · Esc = menu",
                _head);

            UiScale.End();
        }

        private void DrawPalette(Rect r)
        {
            Panel(r);
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width - 20f, 24f), "BLOCKS & PARTS", _title);

            var view = new Rect(r.x + 8f, r.y + 40f, r.width - 16f, r.height - 48f);
            var content = new Rect(0f, 0f, view.width - 16f, _palette.Length * 34f + 4f);
            _palScroll = GUI.BeginScrollView(view, _palScroll, content);
            for (int i = 0; i < _palette.Length; i++)
            {
                var rowR = new Rect(2f, i * 34f, content.width - 4f, 30f);
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = i == _selected ? new Color(0.2f, 0.7f, 0.9f) : new Color(0.18f, 0.26f, 0.36f);
                if (GUI.Button(rowR, $"   {_palette[i].Label}"))
                {
                    _selected = i;
                }

                GUI.backgroundColor = prev;
                var swatch = GUI.color;
                GUI.color = _palette[i].Color;
                GUI.DrawTexture(new Rect(rowR.x + 6f, rowR.y + 7f, 16f, 16f), Texture2D.whiteTexture);
                GUI.color = swatch;
            }

            GUI.EndScrollView();
        }

        private void DrawMeta(Rect r)
        {
            Panel(r);
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width - 20f, 24f), "SHIP TYPE", _title);

            var view = new Rect(r.x + 8f, r.y + 40f, r.width - 16f, r.height - 48f);
            var content = new Rect(0f, 0f, view.width - 16f, 560f + _craftCost.Count * 26f);
            _metaScroll = GUI.BeginScrollView(view, _metaScroll, content);
            float x = 6f, y = 4f, fw = content.width - 12f;

            _key = Field("Key (unique, slug)", x, ref y, fw, _key);
            _shipName = Field("Name", x, ref y, fw, _shipName);
            _desc = Field("Description", x, ref y, fw, _desc);
            _requiredBlueprint = Field("Required blueprint (blank = always)", x, ref y, fw, _requiredBlueprint);

            y += 6f;
            GUI.Label(new Rect(x, y, fw, 20f), "STATS", _head); y += 24f;
            _hull = Slider("Base hull", x, ref y, fw, _hull, 20f, 500f, "0");
            _shield = Slider("Base shield", x, ref y, fw, _shield, 0f, 300f, "0");
            _flightSpeed = Slider("Flight speed", x, ref y, fw, _flightSpeed, 0.4f, 2.5f, "0.0");
            _handling = Slider("Handling", x, ref y, fw, _handling, 0.4f, 2.5f, "0.0");
            _cargo = Mathf.RoundToInt(Slider("Cargo slots", x, ref y, fw, _cargo, 12f, 240f, "0"));

            y += 6f;
            GUI.Label(new Rect(x, y, fw, 20f), "CRAFT COST", _head); y += 24f;
            for (int i = 0; i < _craftCost.Count; i++)
            {
                _craftCost[i].Item = GUI.TextField(new Rect(x, y, fw - 96f, 22f), _craftCost[i].Item);
                int.TryParse(GUI.TextField(new Rect(x + fw - 90f, y, 50f, 22f), _craftCost[i].Count.ToString()), out var c);
                _craftCost[i].Count = Mathf.Max(0, c);
                if (GUI.Button(new Rect(x + fw - 34f, y, 30f, 22f), "×"))
                {
                    _craftCost.RemoveAt(i);
                    break;
                }

                y += 26f;
            }

            if (GUI.Button(new Rect(x, y, 120f, 24f), "+ add cost")) { _craftCost.Add(new CostRow { Item = "iron_plate", Count = 1 }); }
            y += 34f;

            GUI.Label(new Rect(x, y, fw, 20f), $"Blocks placed: {_design.Count}", _head); y += 26f;

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
            if (GUI.Button(new Rect(x, y, fw, 30f), "SAVE / EXPORT SHIP TYPE")) { Export(); }
            GUI.backgroundColor = prev;
            y += 36f;

            if (!string.IsNullOrEmpty(_status))
            {
                GUI.Label(new Rect(x, y, fw, 60f), _status); y += 62f;
            }

            if (GUI.Button(new Rect(x, y, 120f, 26f), "← Back")) { Shell?.CloseShipEditor(); }

            GUI.EndScrollView();
        }

        private string Field(string label, float x, ref float y, float w, string val)
        {
            GUI.Label(new Rect(x, y, w, 18f), label, _head); y += 20f;
            val = GUI.TextField(new Rect(x, y, w, 22f), val ?? string.Empty); y += 28f;
            return val;
        }

        private float Slider(string label, float x, ref float y, float w, float val, float min, float max, string fmt)
        {
            GUI.Label(new Rect(x, y, w, 18f), $"{label}: {val.ToString(fmt)}", _head); y += 20f;
            val = GUI.HorizontalSlider(new Rect(x, y + 4f, w, 18f), val, min, max); y += 28f;
            return val;
        }

        private void Panel(Rect r)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.12f, 0.24f, 0.92f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = UiKit.Cyan;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }

            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = UiKit.Cyan } };
            _head = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UiKit.TextCol } };
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
