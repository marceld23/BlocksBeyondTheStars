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

        /// <summary>One authored cell: the palette id + kind, plus the in-game per-voxel modifiers
        /// (dye/glow colour 0xRRGGBB, packed shape+orientation). Markers carry no modifiers.</summary>
        private struct CellData { public string Id; public string Kind; public int Tint, Glow, Shape; }

        private readonly Dictionary<Vector3i, CellData> _design = new();
        private readonly Dictionary<Vector3i, GameObject> _cells = new();
        private readonly Dictionary<string, Material> _mats = new();

        private struct Pal { public string Id, Label, Kind; public Color Color; }
        private Pal[] _palette;
        private int _selected;
        private string[] _tiers;
        private int _tier;

        // Brush: the dye/glow colour + shape + orientation applied to newly placed BLOCK cells (markers
        // ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / plain cube.
        private int _brushTint, _brushGlow, _brushShape, _brushOrient;
        private string _search = string.Empty;

        /// <summary>The 9 in-game block shapes (index = BlockShape enum). Orientation is 0..3 quarter-turns.</summary>
        private static readonly string[] ShapeNames = { "Cube", "Slab", "Pyramid", "Dome", "Sphere", "Ramp", "Stairs", "Cone", "Cylinder" };

        private string _key = "my_structure";
        private string _name = "My Structure";
        private string _pack = "default";   // template pack; a world enables a set of packs
        private int _weight = 1;            // relative selection weight within its tier
        private string _status = string.Empty;
        private bool _mouseOverUi;

        private void Start()
        {
            _palette = BuildPalette();
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

        /// <summary>The full palette: every placeable block from the loaded content (so all materials —
        /// including the dyeable, shapeable, light and glowing blocks — are available), preceded by the
        /// interaction markers this structure kind needs. Built once from <see cref="AppShell.Content"/>.</summary>
        private Pal[] BuildPalette()
        {
            var list = new List<Pal>();
            list.AddRange(EditorMode == Mode.Station ? StationMarkers() : SettlementMarkers());

            var content = Shell != null ? Shell.Content : null;
            if (content != null)
            {
                var keys = new List<string>(content.Blocks.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    if (key == "air")
                    {
                        continue;
                    }

                    var def = content.GetBlock(key);
                    string label = def != null ? L(def.NameKey) : key;
                    list.Add(P(key, label, "block", BlockSwatch(key)));
                }
            }

            return list.ToArray();
        }

        private static Pal[] StationMarkers() => new[]
        {
            P("hangar", "Hangar marker", "marker", new Color(0.35f, 0.4f, 0.46f)),
            P("vendor", "Vendor marker", "marker", new Color(0.9f, 0.75f, 0.2f)),
            P("mission_board", "Mission board", "marker", new Color(0.4f, 0.7f, 0.95f)),
            P("heal_tank", "Heal tank", "marker", new Color(0.4f, 0.9f, 0.6f)),
            P("quarters", "Quarters", "marker", new Color(0.6f, 0.45f, 0.8f)),
            P("console", "Console", "marker", new Color(0.3f, 0.6f, 0.95f)),
        };

        private static Pal[] SettlementMarkers() => new[]
        {
            P("vendor", "Vendor marker", "marker", new Color(0.9f, 0.75f, 0.2f)),
            P("mission_board", "Mission board", "marker", new Color(0.4f, 0.7f, 0.95f)),
            P("npc", "Inhabitant", "marker", new Color(0.85f, 0.6f, 0.5f)),
            P("door_slide", "Slide door", "marker", new Color(0.40f, 0.85f, 0.95f)),
            P("door_hinge", "Hinge door", "marker", new Color(0.60f, 0.40f, 0.20f)),
            P("loot", "Loot cache", "marker", new Color(0.8f, 0.7f, 0.3f)),
        };

        /// <summary>A stable, legible swatch colour for a block id (used only in the palette list — the
        /// real cell tints with the brush dye / glow at place time).</summary>
        private static Color BlockSwatch(string key)
        {
            int h = 0;
            foreach (char c in key) h = h * 31 + c;
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.32f, 0.78f);
        }

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

            // Rotate the shape brush (matches the in-game place-orientation control).
            if (!_mouseOverUi && Input.GetKeyDown(KeyCode.R))
            {
                _brushOrient = (_brushOrient + 1) & 3;
                if (_orientLabel != null) _orientLabel.text = (_brushOrient * 90) + "°";
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
            var data = new CellData { Id = pal.Id, Kind = pal.Kind };
            if (pal.Kind == "block")
            {
                // Only real blocks carry dye/glow/shape (markers are interaction points, not voxels).
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
                // Shaped cells render the real in-game geometry (unit cell 0..1), with a unit box collider
                // so place/remove raycasts still hit them.
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
                go.transform.localScale = Vector3.one * (pal.Kind == "marker" ? 0.6f : 0.98f);
            }

            go.GetComponent<Renderer>().sharedMaterial = MatFor(pal, data);
            _cells[cell] = go;
            _design[cell] = data;
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        private Material MatFor(Pal pal, CellData data)
        {
            // Dye wins for the base colour; a pure glow cell shows its glow colour; else the palette swatch.
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

        // ----------------------------- export -----------------------------

        [Serializable] private sealed class CellJson { public int x, y, z; public string kind, id; public int tint, glow, shape; }
        [Serializable] private sealed class LayoutJson { public int width, height, length; public List<CellJson> cells = new(); }
        [Serializable] private sealed class MetaJson { public string key, name, kind, tier, pack, layout; public int weight = 1; }

        // Data-shaped StructureTemplate (matches the server's StructureTemplate JSON) written straight to
        // the user-content folder so a structure built in-game appears in the next new world WITHOUT a merge.
        [Serializable] private sealed class TemplateJson
        {
            public string key, name, tier, kind, pack;
            public int weight = 1;
            public int width, height, length;
            public List<CellJson> cells = new();
        }

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
                var d = kv.Value;
                layout.cells.Add(new CellJson
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

            string modeName = EditorMode == Mode.Station ? "station" : "settlement";
            string pack = string.IsNullOrWhiteSpace(_pack) ? "default" : Slug(_pack);
            int weight = Mathf.Max(1, _weight);
            var meta = new MetaJson { key = key, name = _name, kind = modeName, tier = _tiers[_tier], pack = pack, weight = weight, layout = $"{key}.json" };

            try
            {
                // 1) Export bundle (the "ship into the game" path via tools/merge_structure.py).
                string dir = Path.Combine(Application.persistentDataPath, modeName + "_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "structure.json"), JsonUtility.ToJson(meta, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));

                // 2) Data-shaped template written straight to the user-content folder the local server
                //    reads — so this structure can appear in your NEXT new world without any merge/rebuild.
                var tpl = new TemplateJson
                {
                    key = key, name = _name, tier = _tiers[_tier], kind = modeName, pack = pack, weight = weight,
                    width = layout.width, height = layout.height, length = layout.length, cells = layout.cells,
                };
                string userDir = Path.Combine(Application.persistentDataPath, "usercontent", modeName + "_templates");
                Directory.CreateDirectory(userDir);
                File.WriteAllText(Path.Combine(userDir, key + ".json"), JsonUtility.ToJson(tpl, true));

                SetStatus($"Saved '{key}' ({_design.Count} cells).\nLive in your new worlds now (pack '{pack}').\nRun tools/merge_structure.py to ship it into the game.");
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

                        var data = new CellData
                        {
                            Id = c.id,
                            Kind = string.IsNullOrEmpty(c.kind) ? pal.Kind : c.kind,
                            Tint = c.tint, Glow = c.glow, Shape = c.shape,
                        };
                        PlaceCellData(cell, pal, data);
                    }
                }

                string metaPath = Path.Combine(dir, "structure.json");
                if (File.Exists(metaPath) && JsonUtility.FromJson<MetaJson>(File.ReadAllText(metaPath)) is { } meta)
                {
                    _key = string.IsNullOrEmpty(meta.key) ? key : meta.key;
                    _name = string.IsNullOrEmpty(meta.name) ? _name : meta.name;
                    _pack = string.IsNullOrEmpty(meta.pack) ? "default" : meta.pack;
                    _weight = Mathf.Max(1, meta.weight);
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
            var m = new Material(shader) { color = ShaderColor.Srgb(color) };
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(color) };
        }

        // ----------------------------- UI (uGUI) -----------------------------

        private const float PanelH = 1048f;
        private Canvas _canvas;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Text _tierLabel;
        private Text _weightLabel;
        private Text _shapeLabel;
        private Text _orientLabel;
        private Transform _palListParent;
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

            // Left: palette (markers + every placeable block) with a search filter.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, L("ui.struct.palette"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(pal.transform, 12f, 42f, 276f, 28f, _search, v => { _search = v ?? string.Empty; RebuildPaletteRows(); });
            _palListParent = UiKit.ScrollList(pal.transform, 10f, 78f, 280f, PanelH - 90f);
            RebuildPaletteRows();

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

            // Template pack (a world enables a set of packs) + selection weight within the tier.
            UiKit.AddText(meta, 16f, y, 348f, 22f, L("ui.struct.pack"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;
            UiKit.AddInput(meta, 16f, y, 348f, 30f, _pack, v => _pack = v);
            y += 44f;
            UiKit.AddText(meta, 16f, y, 150f, 30f, L("ui.struct.weight"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _weightLabel = UiKit.AddText(meta, 176f, y, 80f, 30f, _weight.ToString(), 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 260f, y, 30f, 30f, "−", () => { _weight = Mathf.Max(1, _weight - 1); _weightLabel.text = _weight.ToString(); });
            UiKit.AddButton(meta, 300f, y, 30f, 30f, "+", () => { _weight = Mathf.Min(99, _weight + 1); _weightLabel.text = _weight.ToString(); });
            y += 50f;

            // ── Block brush: dye + glow colour + shape + orientation applied to newly placed BLOCK cells ──
            UiKit.AddText(meta, 16f, y, 348f, 24f, L("ui.struct.brush"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            UiKit.AddText(meta, 16f, y, 70f, 30f, L("ui.struct.dye"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddInput(meta, 92f, y, 150f, 30f, HexOf(_brushTint), v => _brushTint = ParseHex(v));
            UiKit.AddButton(meta, 250f, y, 80f, 30f, L("ui.struct.brush_none"), () => { _brushTint = 0; RebuildUi(); });
            y += 38f;
            UiKit.AddText(meta, 16f, y, 70f, 30f, L("ui.struct.glow"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddInput(meta, 92f, y, 150f, 30f, HexOf(_brushGlow), v => _brushGlow = ParseHex(v));
            UiKit.AddButton(meta, 250f, y, 80f, 30f, L("ui.struct.brush_none"), () => { _brushGlow = 0; RebuildUi(); });
            y += 42f;
            UiKit.AddText(meta, 16f, y, 90f, 30f, L("ui.struct.shape"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _shapeLabel = UiKit.AddText(meta, 116f, y, 140f, 30f, ShapeNames[_brushShape], 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 262f, y, 30f, 30f, "−", () => { _brushShape = (_brushShape + ShapeNames.Length - 1) % ShapeNames.Length; _shapeLabel.text = ShapeNames[_brushShape]; });
            UiKit.AddButton(meta, 300f, y, 30f, 30f, "+", () => { _brushShape = (_brushShape + 1) % ShapeNames.Length; _shapeLabel.text = ShapeNames[_brushShape]; });
            y += 38f;
            UiKit.AddText(meta, 16f, y, 90f, 30f, L("ui.struct.orient"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _orientLabel = UiKit.AddText(meta, 116f, y, 140f, 30f, (_brushOrient * 90) + "°", 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 262f, y, 68f, 30f, "↻ R", () => { _brushOrient = (_brushOrient + 1) & 3; _orientLabel.text = (_brushOrient * 90) + "°"; });
            y += 46f;

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
            _rowToPaletteIndex.Add(index);
        }

        private readonly List<int> _rowToPaletteIndex = new();

        /// <summary>Rebuilds the palette list from the search filter (markers always shown; blocks matched by
        /// label or id). Rows carry their real <see cref="_palette"/> index so selection stays stable.</summary>
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

        /// <summary>Parses a 6-hex-digit colour string to 0xRRGGBB (0 = empty/invalid = "none").</summary>
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
