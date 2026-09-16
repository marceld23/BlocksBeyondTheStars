// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
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
    /// <para>
    /// <b>LOAD</b> (#1395) offers the shipped templates (from the loaded content) and the templates already
    /// saved to the user-content folder as starting points, next to the user's exports. A shipped template
    /// is loaded as a COPY (key suffixed <c>_2</c>): user templates are added to the pool, not merged over
    /// it, so saving under the original key would only put a clone next to the original.
    /// </para>
    /// </summary>
    public sealed class StructureEditor : MonoBehaviour
    {
        public enum Mode { Station, Settlement }

        public AppShell Shell;
        public Mode EditorMode = Mode.Station;

        private const int MaxW = 128, MaxH = 128, MaxL = 128;
        private const float RaycastDist = 2400f;

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        /// <summary>One authored cell: the palette id + kind, plus the in-game per-voxel modifiers
        /// (dye/glow colour 0xRRGGBB, packed shape+orientation). Markers carry no modifiers.</summary>
        private struct CellData { public string Id; public string Kind; public int Tint, Glow, Shape; public string Port; }

        private readonly Dictionary<Vector3i, CellData> _design = new();   // cell -> authored cell (export source)
        private EditorVoxelChunkView _view;                                // chunked combined-mesh renderer

        private BlockTextureAtlas _atlas;                                  // editor-local atlas for palette icons + cell colours
        private EditorPaletteKit.Entry[] _palette;
        private int _selected;
        private string[] _tiers;
        private int _tier;

        // Brush: the dye/glow colour + shape + orientation applied to newly placed BLOCK cells (markers
        // ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / plain cube.
        private int _brushTint, _brushGlow, _brushShape, _brushOrient;
        private string _search = string.Empty;

        /// <summary>The 9 in-game block shapes (index = BlockShape enum; localized via <c>ui.shape.*</c>).
        /// Orientation is 0..3 quarter-turns.</summary>
        private static readonly string[] ShapeSlugs = { "cube", "slab", "pyramid", "dome", "sphere", "ramp", "stairs", "cone", "cylinder" };

        private string ShapeName(int i) => L("ui.shape." + ShapeSlugs[i]);

        private string TierLabel(string slug) => L("ui.tier." + slug);

        /// <summary>The "Use as" label of a role (#1826): whole structure, or one of the module roles.</summary>
        private string RoleLabel(string role) => L("ui.role." + (string.IsNullOrEmpty(role) ? "whole" : role));

        private string _key = "my_structure";
        private string _name = "My Structure";
        private string _pack = "default";   // template pack; a world enables a set of packs
        private int _weight = 1;            // relative selection weight within its tier
        private string _role = string.Empty; // #1826: "" = a whole structure, else a building-module role (StructureRoles)
        private bool _moduleMode;             // #1877: "Use as" = module (of a kit) instead of a complete structure
        private string _kit = string.Empty;   // #1877: the kit (module name) this module belongs to
        private string _function = string.Empty; // #1877: the module's function within its kit
        private string _style = string.Empty;    // #1890: "" = human, "alien" = the alien variant of a settlement module
        private int _portDoor;                // #1877: door option (StructurePorts.DoorOptions index) the port brush paints
        private readonly HashSet<Vector3i> _leaks = new(); // #1877: cells the seal check painted red
        private KitEditorPanel _kitPanel;
        private string _planetTypes = string.Empty; // settlements only: comma-separated planet-type keys (#1115); blank = every world
        private int _seed = 1;              // procedural starting point (#1401): seed fed to the world-gen generator
        private string _surface = "grass";  // settlements only: the biome surface block the generator builds villages from
        private string _status = string.Empty;
        private bool _mouseOverUi;

        private void Start()
        {
            // Editor-local block atlas: gives the palette (and the placed cells) the real material look.
            _atlas = Shell != null && Shell.Content != null ? BlockTextureAtlas.Acquire(Shell.Content) : null; // shared per process (#1523)
            _palette = BuildPalette();
            _tiers = EditorMode == Mode.Station
                ? new[] { "small", "medium", "large", "huge", "colossal" } // colossal = the rare mega-station (#1402)
                : new[] { "hamlet", "village", "town", "city", StructureRoles.MetropolisTier }; // metropolis = a city-composer district module (#1826)
            _key = EditorMode == Mode.Station ? "my_station" : "my_settlement";
            _name = EditorMode == Mode.Station ? "My Station" : "My Settlement";

            var camGo = new GameObject("EditorCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            _cam.farClipPlane = 1600f;
            camGo.AddComponent<AudioListener>();
            EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL); // opening view (#1390)

            _view = new EditorVoxelChunkView(transform);
            _view.SetAtlas(_atlas?.Texture); // real block tiles on placed cells (#1400)
            _ghost = new EditorPlacementGhost(transform);
            BuildRoom();
            BuildUi();
        }

        /// <summary>A marker palette entry: localized via <c>ui.marker.*</c>, grouped under "markers".</summary>
        private EditorPaletteKit.Entry M(string id, Color c) => new EditorPaletteKit.Entry
        {
            Id = id, Label = L("ui.marker." + id), Kind = "marker", Group = "markers", Color = c,
        };

        /// <summary>The full palette: the interaction markers this structure kind needs, then every
        /// placeable block from the loaded content — localized, category-grouped and iconed with its
        /// real atlas tile (see <see cref="EditorPaletteKit"/>). Built once in Start.</summary>
        private EditorPaletteKit.Entry[] BuildPalette()
        {
            var list = new List<EditorPaletteKit.Entry>();
            list.AddRange(EditorMode == Mode.Station ? StationMarkers() : SettlementMarkers());
            list.AddRange(EditorPaletteKit.BlockEntries(Shell, _atlas));
            return list.ToArray();
        }

        private EditorPaletteKit.Entry[] StationMarkers() => new[]
        {
            M("hangar", new Color(0.35f, 0.4f, 0.46f)),
            M("vendor", new Color(0.9f, 0.75f, 0.2f)),
            M("mission_board", new Color(0.4f, 0.7f, 0.95f)),
            M("heal_tank", new Color(0.4f, 0.9f, 0.6f)),
            M("quarters", new Color(0.6f, 0.45f, 0.8f)),
            M("console", new Color(0.3f, 0.6f, 0.95f)),
            M("npc", new Color(0.85f, 0.6f, 0.5f)),          // used by the shipped spire/bazaar templates (#1398)
            M("greenhouse", new Color(0.45f, 0.85f, 0.35f)), // hydroponics bay marker (#628)
            M("spawn", new Color(0.95f, 0.95f, 0.6f)),       // arrival point the procedural hub emits (#1401)
            M("door_slide", new Color(0.40f, 0.85f, 0.95f)), // procedural stations hang doors on these (#1401)
            M("door_hinge", new Color(0.60f, 0.40f, 0.20f)),
            M("door_energy", new Color(0.35f, 0.80f, 1f)), // the airtight air-curtain door (#793)
            M("cabin", new Color(0.55f, 0.75f, 0.95f)),      // one resident sleeps here (#1874)
            M("lounge", new Color(0.85f, 0.65f, 0.35f)),     // a canteen / bar seat the crew gathers at (#1874)
            M("room", new Color(0.75f, 0.55f, 0.85f)),       // furnish this room procedurally (#1828)
            M("doctor", ProfessionColors[0]),                // 2026-09: the profession posts (NpcProfessions)
            M("grocer", ProfessionColors[1]),
            M("arms_dealer", ProfessionColors[2]),
            M("sage", ProfessionColors[3]),
            M("tamer", ProfessionColors[4]),
            M("blockfarmer", ProfessionColors[5]),
            M("streamer", ProfessionColors[6]),
            M("reporter", ProfessionColors[7]),
            P("door"),                                       // #1877: docking ports — painted onto wall blocks
            P("wide"),
            P("ladder"),
        };

        /// <summary>A material-token entry (#1890): placed like a block, exported as its token (<c>@wall</c> …), drawn as a
        /// plain swatch — the composer puts the planet's own material there.</summary>
        private EditorPaletteKit.Entry T(string token, Color c) => new EditorPaletteKit.Entry
        {
            Id = token, Label = L("ui.token." + token.Substring(1)), Kind = "block", Group = "tokens", Color = c,
        };

        /// <summary>A port brush entry (#1877): paints <c>tag[:door]</c> onto an existing wall block.</summary>
        private EditorPaletteKit.Entry P(string tag) => new EditorPaletteKit.Entry
        {
            Id = tag, Label = L("ui.marker.port_" + tag), Kind = "port", Group = "markers", Color = PortColor,
        };

        private static readonly Color PortColor = new Color(0.2f, 0.9f, 1f);

        /// <summary>Marker colours of the eight profession posts, in <c>NpcProfessions.All</c> order.</summary>
        private static readonly Color[] ProfessionColors =
        {
            new Color(0.95f, 0.35f, 0.40f), new Color(0.95f, 0.60f, 0.30f), new Color(0.45f, 0.50f, 0.40f), new Color(0.55f, 0.35f, 0.85f),
            new Color(0.55f, 0.75f, 0.30f), new Color(0.70f, 0.55f, 0.35f), new Color(0.95f, 0.40f, 0.85f), new Color(0.35f, 0.55f, 0.95f),
        };
        private static readonly Color LeakColor = new Color(1f, 0.2f, 0.2f);

        private EditorPaletteKit.Entry[] SettlementMarkers() => new[]
        {
            M("vendor", new Color(0.9f, 0.75f, 0.2f)),
            M("mission_board", new Color(0.4f, 0.7f, 0.95f)),
            M("npc", new Color(0.85f, 0.6f, 0.5f)),
            M("door_slide", new Color(0.40f, 0.85f, 0.95f)),
            M("door_hinge", new Color(0.60f, 0.40f, 0.20f)),
            M("door_energy", new Color(0.35f, 0.80f, 1f)), // the airtight air-curtain door (#793)
            M("room", new Color(0.75f, 0.55f, 0.85f)),     // furnish this floor procedurally (#1828)
            M("loot", new Color(0.8f, 0.7f, 0.3f)),
            M("greenhouse", new Color(0.45f, 0.85f, 0.35f)),    // the generator's garden-house marker (#626, #1401)
            M("chest", new Color(0.75f, 0.55f, 0.25f)),         // loot chest (stilt_hamlet, #1398)
            M("data_terminal", new Color(0.35f, 0.9f, 0.9f)),   // lore terminal (walled_market, #1398)
            M("tavern", new Color(0.85f, 0.55f, 0.25f)),        // #1890: the innkeeper's post — the room is a tavern
            M("workshop", new Color(0.55f, 0.55f, 0.6f)),       // #1890: the craftsman's post — the room is a workshop
            M("lounge", new Color(0.85f, 0.65f, 0.35f)),        // an evening seat
            M("guard_post", new Color(0.55f, 0.2f, 0.6f)),      // a G.D.S. guardian
            M("doctor", ProfessionColors[0]),                   // 2026-09: the profession posts — the room is furnished to fit
            M("grocer", ProfessionColors[1]),
            M("arms_dealer", ProfessionColors[2]),
            M("sage", ProfessionColors[3]),
            M("tamer", ProfessionColors[4]),
            M("blockfarmer", ProfessionColors[5]),
            M("streamer", ProfessionColors[6]),
            M("reporter", ProfessionColors[7]),
            T(MaterialTokens.Wall, new Color(0.62f, 0.55f, 0.42f)), // #1890: material tokens — resolved per planet
            T(MaterialTokens.Accent, new Color(0.55f, 0.85f, 0.95f)),
            T(MaterialTokens.Roof, new Color(0.55f, 0.4f, 0.3f)),
            T(MaterialTokens.Floor, new Color(0.5f, 0.5f, 0.48f)),
            T(MaterialTokens.Path, new Color(0.7f, 0.65f, 0.5f)),
            P("door"),                                          // #1877: docking ports (ground kits may use them later)
            P("wide"),
            P("ladder"),
        };

        private void BuildRoom()
        {
            // Floor (raycast target) with the shared procedural grid + fill light (#1391).
            _floor = EditorSceneKit.BuildFloor(transform, MaxW, MaxL);
            EditorSceneKit.BuildSun(transform);
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

            // Keystrokes belong to a focused text field (the palette search, the key/name inputs) —
            // typing "sand" must not fly the camera away or turn the brush (#1388).
            bool typing = UiKit.TextFieldFocused();
            bool fast = Input.GetKey(KeyCode.LeftShift);
            if (!typing)
            {
                float speed = (fast ? 55f : 22f) * Time.deltaTime;
                var move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += _cam.transform.forward;
                if (Input.GetKey(KeyCode.S)) move -= _cam.transform.forward;
                if (Input.GetKey(KeyCode.D)) move += _cam.transform.right;
                if (Input.GetKey(KeyCode.A)) move -= _cam.transform.right;
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
                _cam.transform.position += move * speed;

                if (Input.GetKeyDown(KeyCode.F))
                {
                    EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL);
                }
            }

            _mouseOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!_mouseOverUi)
            {
                EditorSceneKit.WheelDolly(_cam.transform, fast); // wheel over a panel scrolls the panel
            }

            if (_blocksLabel != null && _lastPlaced != _design.Count)
            {
                _lastPlaced = _design.Count;
                _blocksLabel.text = string.Format(L("ui.ed.placed"), _design.Count);
            }

            UpdateGhost(flying || _mouseOverUi);
            if (!flying && !_mouseOverUi)
            {
                if (Input.GetMouseButtonDown(0)) TryPlace();
                else if (Input.GetMouseButtonDown(2)) TryRemove();
            }

            // Rotate the shape brush (matches the in-game place-orientation control).
            if (!typing && !_mouseOverUi && Input.GetKeyDown(KeyCode.R))
            {
                _brushOrient = (_brushOrient + 1) & 3;
                if (_orientLabel != null) _orientLabel.text = (_brushOrient * 90) + "°";
            }

            _view.Flush(); // upload any chunk meshes touched by this frame's edits
        }

        private EditorPlacementGhost _ghost;

        /// <summary>Shows where a click would land: green = free cell, red = occupied / out of bounds.</summary>
        private void UpdateGhost(bool hidden)
        {
            Vector3i cell = default;
            bool show = !hidden && TryGetTargetCell(out cell);
            _ghost?.Update(show, cell, show && InBounds(cell) && !_design.ContainsKey(cell));
        }

        private void TryPlace()
        {
            var pal = _palette[_selected];
            if (pal.Kind == "port")
            {
                PaintPort(pal.Id); // #1877: the port brush marks an existing wall block
                return;
            }

            if (TryGetTargetCell(out var cell) && InBounds(cell) && !_design.ContainsKey(cell))
            {
                ClearLeaks();
                PlaceCell(cell, pal);
            }
        }

        private void TryRemove()
        {
            if (!TryGetHitCell(out var cell) || !_design.ContainsKey(cell))
            {
                return;
            }

            ClearLeaks();
            var d = _design[cell];
            if (_palette[_selected].Kind == "port" && !string.IsNullOrEmpty(d.Port))
            {
                d.Port = string.Empty; // the port brush's middle click clears the port, the block stays
                PlaceCellData(cell, FindPalette(d.Id, d.Kind), d);
                return;
            }

            _design.Remove(cell);
            _view.Remove(cell);
        }

        /// <summary>Paints <c>tag[:door]</c> onto the wall block under the cursor (#1877). Markers never carry a port.</summary>
        private void PaintPort(string tag)
        {
            if (!TryGetHitCell(out var cell) || !_design.TryGetValue(cell, out var d) || d.Kind != "block")
            {
                return;
            }

            string door = StructurePorts.DoorOptions[_portDoor];
            d.Port = door == StructurePorts.DoorSlide ? tag : tag + ":" + door;
            ClearLeaks();
            PlaceCellData(cell, FindPalette(d.Id, d.Kind), d);
        }

        /// <summary>Paints the seal check's leak cells red until the next edit.</summary>
        private void PaintLeaks(IEnumerable<Vector3i> leaks)
        {
            ClearLeaks();
            foreach (var c in leaks)
            {
                if (!InBounds(c))
                {
                    continue;
                }

                _leaks.Add(c);
                _design.TryGetValue(c, out var d);
                _view.Set(c, new EditorVoxelChunkView.Cell { Color = LeakColor, Glow = true, Shape = d.Shape, Marker = false, Textured = false });
            }

            _view.Flush();
        }

        private void ClearLeaks()
        {
            if (_leaks.Count == 0)
            {
                return;
            }

            var cells = new List<Vector3i>(_leaks);
            _leaks.Clear();
            foreach (var c in cells)
            {
                if (_design.TryGetValue(c, out var d))
                {
                    PlaceCellData(c, FindPalette(d.Id, d.Kind), d);
                }
                else
                {
                    _view.Remove(c);
                }
            }
        }

        /// <summary>Resolves the empty cell just outside the hit face (or the floor column) for placement. The
        /// chunk mesh is authored in world coords, so the hit point + normal locate the cell directly.</summary>
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
                Vector3 outside = hit.point + hit.normal * 0.5f;
                cell = new Vector3i(Mathf.FloorToInt(outside.x), Mathf.FloorToInt(outside.y), Mathf.FloorToInt(outside.z));
            }

            return true;
        }

        /// <summary>Resolves the actual occupied cell under the cursor (for removal); false on a miss or a
        /// floor-only hit.</summary>
        private bool TryGetHitCell(out Vector3i cell)
        {
            cell = default;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, RaycastDist) || hit.collider.gameObject == _floor)
            {
                return false;
            }

            Vector3 inside = hit.point - hit.normal * 0.5f;
            cell = new Vector3i(Mathf.FloorToInt(inside.x), Mathf.FloorToInt(inside.y), Mathf.FloorToInt(inside.z));
            return true;
        }

        private void PlaceCell(Vector3i cell, EditorPaletteKit.Entry pal)
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

        private void PlaceCellData(Vector3i cell, EditorPaletteKit.Entry pal, CellData data)
        {
            // Real blocks show their atlas tile (#1400); dye/glow tint the texture like in-game. Markers keep
            // the palette swatch. The chunked view bakes directional shading + face culling; markers render
            // as small inset cubes.
            bool textured = false;
            Rect tile = default;
            if (_atlas != null && pal.Kind != "marker" && Shell?.Content?.GetBlock(pal.Id) is { } def)
            {
                textured = true;
                tile = _atlas.TileUv(def.NumericId.Value);
            }

            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : (textured ? Color.white : pal.Color));
            bool port = !string.IsNullOrEmpty(data.Port);
            if (port)
            {
                baseCol = Color.Lerp(baseCol, PortColor, 0.6f); // #1877: a docking port reads as a cyan wall block
            }

            _design[cell] = data;
            _view.Set(cell, new EditorVoxelChunkView.Cell
            {
                Color = baseCol,
                Glow = data.Glow != 0 || port,
                Shape = data.Shape,
                Marker = pal.Kind == "marker",
                Textured = textured,
                Uv = tile,
            });
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        // ----------------------------- export -----------------------------

        [Serializable] private sealed class CellJson { public int x, y, z; public string kind, id; public int tint, glow, shape; public string port = string.Empty; }
        [Serializable] private sealed class LayoutJson { public int width, height, length; public List<CellJson> cells = new(); }
        [Serializable] private sealed class MetaJson
        {
            public string key, name, kind, tier, pack, layout;
            public int weight = 1;
            public List<string> planetTypes = new(); // carried through so a re-merge keeps the restriction (#1399)
            public string role = string.Empty;       // #1826: "" = whole structure, else a building-module role
            public string kit = string.Empty;        // #1877: the kit this module belongs to
            public string function = string.Empty;   // #1877: the module's function within its kit
            public string style = string.Empty;      // #1890: "" = human, "alien" = the alien variant
        }

        // Data-shaped StructureTemplate (matches the server's StructureTemplate JSON) written straight to
        // the user-content folder so a structure built in-game appears in the next new world WITHOUT a merge.
        [Serializable] private sealed class TemplateJson
        {
            public string key, name, tier, kind, pack;
            public int weight = 1;
            public List<string> planetTypes = new();
            public string role = string.Empty;
            public string kit = string.Empty;
            public string function = string.Empty;
            public string style = string.Empty;
            public int width, height, length;
            public List<CellJson> cells = new();
        }

        /// <summary>The planet-types field as a list: comma/space separated keys, trimmed, empty = every world.</summary>
        private List<string> PlanetTypeList()
        {
            var list = new List<string>();
            foreach (var raw in (_planetTypes ?? string.Empty).Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = raw.Trim().ToLowerInvariant();
                if (s.Length > 0 && !list.Contains(s))
                {
                    list.Add(s);
                }
            }

            return list;
        }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.ed.need_key"));
                return;
            }

            if (!ValidateForExport())
            {
                return; // #1877: a broken port or a leaking module never leaves the editor
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
                    tint = d.Tint, glow = d.Glow, shape = d.Shape, port = d.Port ?? string.Empty,
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
            var planetTypes = EditorMode == Mode.Settlement ? PlanetTypeList() : new List<string>();
            // #1826: a module's role rides along; a city-district module always carries the metropolis tier.
            // #1877: a kit module carries its kit and function too (the settlement role mirrors a known function so
            // the legacy per-plot composer still recognises it).
            SyncRole();
            string role = EditorMode == Mode.Settlement ? _role ?? string.Empty : string.Empty;
            string kitKey = _moduleMode ? Slug(_kit) : string.Empty;
            string function = _moduleMode ? _function ?? string.Empty : string.Empty;
            string style = _moduleMode && EditorMode == Mode.Settlement ? _style ?? string.Empty : string.Empty;
            string tier = StructureRoles.IsCityRole(role) ? StructureRoles.MetropolisTier : _tiers[_tier];
            var meta = new MetaJson
            {
                key = key, name = _name, kind = modeName, tier = tier, pack = pack, weight = weight, layout = $"{key}.json",
                planetTypes = planetTypes, role = role, kit = kitKey, function = function, style = style,
            };

            try
            {
                // 1) Export bundle (the "ship into the game" path via tools/merge_structure.py).
                string dir = Path.Combine(AppPaths.Root, modeName + "_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "structure.json"), JsonUtility.ToJson(meta, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));

                // 2) Data-shaped template written straight to the user-content folder the local server
                //    reads — so this structure can appear in your NEXT new world without any merge/rebuild.
                var tpl = new TemplateJson
                {
                    key = key, name = _name, tier = tier, kind = modeName, pack = pack, weight = weight,
                    planetTypes = planetTypes, role = role, kit = kitKey, function = function, style = style,
                    width = layout.width, height = layout.height, length = layout.length, cells = layout.cells,
                };
                string userDir = Path.Combine(AppPaths.Root, "usercontent", modeName + "_templates");
                Directory.CreateDirectory(userDir);
                File.WriteAllText(Path.Combine(userDir, key + ".json"), JsonUtility.ToJson(tpl, true));

                SetStatus(string.Format(L("ui.ed.saved_structure"), key, _design.Count, pack));
            }
            catch (Exception e)
            {
                SetStatus(string.Format(L("ui.ed.export_failed"), e.Message));
            }
        }

        private EditorLoadPicker _loadPicker;

        private string ModeName => EditorMode == Mode.Station ? "station" : "settlement";

        private string ExportsRoot => Path.Combine(AppPaths.Root, ModeName + "_exports");

        private string UserTemplatesRoot => Path.Combine(AppPaths.Root, "usercontent", ModeName + "_templates");

        /// <summary>The LOAD dialog (#1395): the shipped templates of this mode (from the loaded content),
        /// the templates already in the user-content folder, then the user's export bundles.</summary>
        private void OpenLoadPicker()
        {
            _loadPicker?.Close();

            var builtIn = new EditorLoadPicker.Section { Title = L("ui.ed.sec_builtin_templates") };
            if (Shell?.Content != null)
            {
                var pool = EditorMode == Mode.Station ? Shell.Content.StationTemplates : Shell.Content.SettlementTemplates;
                var sorted = new List<StructureTemplate>(pool);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var t in sorted)
                {
                    var tpl = t;
                    builtIn.Items.Add(new EditorLoadPicker.Item
                    {
                        Label = string.IsNullOrEmpty(t.Name) ? t.Key : t.Name,
                        Detail = Detail(t.Tier, t.Width, t.Length, t.Height, t.Cells.Count),
                        Load = () => LoadBuiltIn(tpl),
                    });
                }
            }

            var user = new EditorLoadPicker.Section { Title = L("ui.ed.sec_user_templates") };
            if (Directory.Exists(UserTemplatesRoot))
            {
                var files = Directory.GetFiles(UserTemplatesRoot, "*.json");
                System.Array.Sort(files, string.CompareOrdinal);
                foreach (var file in files)
                {
                    TemplateJson tpl;
                    try
                    {
                        tpl = JsonUtility.FromJson<TemplateJson>(File.ReadAllText(file));
                    }
                    catch (Exception)
                    {
                        continue; // one unreadable file must not hide the rest (the server skips it the same way)
                    }

                    if (tpl == null || tpl.cells == null || tpl.cells.Count == 0)
                    {
                        continue;
                    }

                    string fileKey = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(tpl.key))
                    {
                        tpl.key = fileKey;
                    }

                    var t = tpl;
                    user.Items.Add(new EditorLoadPicker.Item
                    {
                        Label = string.IsNullOrEmpty(t.name) ? t.key : t.name,
                        Detail = Detail(t.tier, t.width, t.length, t.height, t.cells.Count),
                        Load = () => ApplyTemplate(t.key, t.name, t.tier, t.pack, t.weight, t.planetTypes, t.cells, copy: false, role: t.role, kit: t.kit, function: t.function, style: t.style),
                    });
                }
            }

            var mine = new EditorLoadPicker.Section { Title = L("ui.ed.sec_exports") };
            if (Directory.Exists(ExportsRoot))
            {
                var dirs = Directory.GetDirectories(ExportsRoot);
                System.Array.Sort(dirs, string.CompareOrdinal);
                foreach (var d in dirs)
                {
                    if (!File.Exists(Path.Combine(d, "layout.json")))
                    {
                        continue;
                    }

                    string k = Path.GetFileName(d);
                    mine.Items.Add(new EditorLoadPicker.Item { Label = k, Load = () => LoadDesign(k) });
                }
            }

            _loadPicker = EditorLoadPicker.Show(Shell, _canvas.transform, new[] { builtIn, user, mine }, _design.Count, () => _loadPicker = null);
        }

        private string Detail(string tier, int w, int l, int h, int cells)
        {
            string tierText = string.IsNullOrEmpty(tier) || System.Array.IndexOf(_tiers, tier) < 0 ? tier ?? string.Empty : TierLabel(tier);
            return $"{tierText} · {w}×{l}×{h} · " + string.Format(L("ui.ed.cells"), cells);
        }

        /// <summary>Finds the palette entry for a loaded cell — by id AND kind first (door_slide is a block in
        /// the shipped templates but a marker in the settlement palette), then by id alone; <c>Id == null</c>
        /// when the palette has no such entry.</summary>
        private EditorPaletteKit.Entry FindPalette(string id, string kind)
        {
            int i = System.Array.FindIndex(_palette, p => p.Id == id && p.Kind == kind);
            if (i < 0)
            {
                i = System.Array.FindIndex(_palette, p => p.Id == id);
            }

            return i < 0 ? default : _palette[i];
        }

        /// <summary>Replaces the build with <paramref name="cells"/>. Cells whose id the palette lacks, or that
        /// fall outside the room, are counted and named for the status line instead of vanishing (#1398).</summary>
        private int ApplyCells(IEnumerable<CellJson> cells, List<string> skippedIds)
        {
            _view.Clear();
            _design.Clear();
            int skipped = 0;
            foreach (var c in cells)
            {
                var cell = new Vector3i(c.x, c.y, c.z);
                var pal = FindPalette(c.id, c.kind);
                if (pal.Id == null || !InBounds(cell) || _design.ContainsKey(cell))
                {
                    skipped++;
                    if (!skippedIds.Contains(c.id ?? "?"))
                    {
                        skippedIds.Add(c.id ?? "?");
                    }

                    continue;
                }

                var data = new CellData
                {
                    Id = c.id,
                    Kind = string.IsNullOrEmpty(c.kind) ? pal.Kind : c.kind,
                    Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty,
                };
                PlaceCellData(cell, pal, data);
            }

            _view.Flush(); // build all loaded chunks in one batch
            return skipped;
        }

        /// <summary>Loads a shipped template as a COPY: the key gets a <c>_2</c> suffix and the status says so,
        /// because the user-content pool is added to the built-in one — a save under the original key would
        /// only put a clone next to the original (and pinned worlds keep the original anyway).</summary>
        private void LoadBuiltIn(StructureTemplate t)
        {
            var cells = new List<CellJson>(t.Cells.Count);
            foreach (var c in t.Cells)
            {
                cells.Add(new CellJson { x = c.X, y = c.Y, z = c.Z, kind = c.Kind, id = c.Id, tint = c.Tint, glow = c.Glow, shape = c.Shape, port = c.Port ?? string.Empty });
            }

            ApplyTemplate(t.Key, t.Name, t.Tier, t.PackOrDefault, t.Weight, t.PlanetTypes, cells, copy: true, role: t.Role, kit: t.Kit, function: t.Function, style: t.Style);
        }

        /// <summary>Common load path for built-in and user templates: cells + form fields, then the status
        /// (skipped cells, copy hint) and a UI rebuild.</summary>
        private void ApplyTemplate(string key, string name, string tier, string pack, int weight, List<string> planetTypes, IEnumerable<CellJson> cells, bool copy, string role = "",
            string kit = "", string function = "", string style = "")
        {
            var skippedIds = new List<string>();
            int skipped = ApplyCells(cells, skippedIds);

            _key = copy ? key + "_2" : key;
            _name = string.IsNullOrEmpty(name) ? key : name;
            _pack = string.IsNullOrEmpty(pack) ? "default" : pack;
            _weight = Mathf.Max(1, weight);
            _planetTypes = planetTypes != null ? string.Join(", ", planetTypes) : string.Empty;
            _role = StructureRoles.IsKnown(role) ? role ?? string.Empty : string.Empty; // #1826
            // #1877: a kit module (kit + function) or a legacy module (role) opens the module view.
            _kit = kit ?? string.Empty;
            _style = style ?? string.Empty;
            _function = !string.IsNullOrEmpty(function) ? function : _role;
            _moduleMode = !string.IsNullOrEmpty(_kit) || !string.IsNullOrEmpty(_role) || !string.IsNullOrEmpty(function);
            if (_moduleMode && string.IsNullOrEmpty(_function))
            {
                _function = Functions()[0];
            }

            SyncRole();
            int ti = System.Array.IndexOf(_tiers, tier);
            if (ti >= 0) _tier = ti;

            FinishLoad(_name, skipped, skippedIds, copy ? key : null);
        }

        private void FinishLoad(string label, int skipped, List<string> skippedIds, string copiedFrom)
        {
            EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL);
            _status = string.Format(L("ui.ed.loaded"), label, _design.Count);
            if (skipped > 0)
            {
                _status += "\n" + string.Format(L("ui.ed.skipped"), skipped, string.Join(", ", skippedIds));
            }

            if (copiedFrom != null)
            {
                _status += "\n" + string.Format(L("ui.ed.copy_hint"), copiedFrom);
            }

            RebuildUi();
        }

        /// <summary>The procedural envelope of a tier (#1402): read from the generators' own layout tables so the
        /// hint can never drift from what world-gen builds.</summary>
        private string SizeHint(string tier)
        {
            if (EditorMode == Mode.Station)
            {
                var (modules, floors, rw, rh, rl) = StationGenerator.Layout(tier);
                return string.Format(L("ui.struct.size_station"), modules, floors, rw, rh, rl);
            }

            // A module's envelope (#1826): a city district, or the plot building of the tier.
            if (StructureRoles.IsCityRole(_role) || tier == StructureRoles.MetropolisTier)
            {
                return string.Format(L("ui.struct.size_module"), CityGenerator.ModuleSize, CityGenerator.Height - 1, CityGenerator.ModuleSize);
            }

            if (!string.IsNullOrEmpty(_role))
            {
                // #1890: a kit module fits the shipped modular kits (8 × 8); a legacy plot module the old 6 × 6 plot.
                var (mw, mh, ml) = _moduleMode && !string.IsNullOrEmpty(_kit)
                    ? SettlementGenerator.ModularPlotEnvelope(tier)
                    : SettlementGenerator.PlotModuleEnvelope(tier);
                return string.Format(L("ui.struct.size_module"), mw, mh, ml);
            }

            var (cols, rows, baseFloors) = SettlementGenerator.Layout(tier);
            bool town = tier == "town" || tier == "city";
            int p = SettlementGenerator.Plot;
            return string.Format(L("ui.struct.size_settlement"),
                cols * p + 1, (cols + 1) * p + 1, rows * p + 1, (rows + 1) * p + 1, baseFloors, town ? baseFloors + 1 : baseFloors);
        }

        /// <summary>Runs the world-gen generator for the current tier + seed on the client (the client ships the
        /// WorldGeneration assembly) and loads the result as editable cells (#1401): blocks via numeric id → key,
        /// markers onto the marker palette. A procedural city becomes a five-minute template job.</summary>
        private void GenerateProcedural()
        {
            if (Shell?.Content == null)
            {
                return;
            }

            string tier = _tiers[_tier];
            if (tier == StructureRoles.MetropolisTier)
            {
                tier = "city"; // the district envelope has no settlement generator of its own; a city is the closest start
            }

            var cells = new List<CellJson>();
            try
            {
                int w, h, l;
                Func<int, int, int, ushort> get;
                if (EditorMode == Mode.Station)
                {
                    var s = StationGenerator.Generate(tier, _seed, Shell.Content);
                    w = s.Width; h = s.Height; l = s.Length; get = s.Get;
                    foreach (var m in s.Markers)
                    {
                        cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                    }
                }
                else
                {
                    string surface = Slug(_surface);
                    if (string.IsNullOrEmpty(surface) || Shell.Content.GetBlock(surface) == null)
                    {
                        surface = "stone"; // the generator's own fallback material
                    }

                    var s = SettlementGenerator.Generate(tier, ruined: false, _seed, surface, Shell.Content);
                    w = s.Width; h = s.Height; l = s.Length; get = s.Get;
                    foreach (var m in s.Markers)
                    {
                        cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                    }
                }

                // Markers first: a block on a marker cell is the one that gets skipped (and reported), never the
                // vendor / mission board a settlement needs to function.
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int z = 0; z < l; z++)
                        {
                            ushort v = get(x, y, z);
                            if (v == 0 || Shell.Content.BlockById(new BlockId(v)) is not { } def)
                            {
                                continue;
                            }

                            cells.Add(new CellJson { x = x, y = y, z = z, kind = "block", id = def.Key });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SetStatus(string.Format(L("ui.ed.load_failed"), e.Message));
                return;
            }

            ApplyTemplate($"{ModeName}_{tier}_{_seed}", $"{TierLabel(tier)} #{_seed}", tier, "default", 1, null, cells, copy: false);
            SetStatus(string.Format(L("ui.ed.generated"), TierLabel(tier), _design.Count, _seed) + "\n" + _status);
        }

        /// <summary>Clears the current build and rebuilds it (+ the form) from a saved export bundle.</summary>
        private void LoadDesign(string key)
        {
            string dir = Path.Combine(ExportsRoot, key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                SetStatus(L("ui.ed.not_found"));
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<LayoutJson>(File.ReadAllText(layoutPath));
                string metaPath = Path.Combine(dir, "structure.json");
                MetaJson meta = File.Exists(metaPath) ? JsonUtility.FromJson<MetaJson>(File.ReadAllText(metaPath)) : null;
                ApplyTemplate(
                    string.IsNullOrEmpty(meta?.key) ? key : meta.key,
                    meta?.name ?? _name,
                    meta?.tier,
                    meta?.pack,
                    meta?.weight ?? 1,
                    meta?.planetTypes,
                    layout?.cells ?? new List<CellJson>(),
                    copy: false,
                    role: meta?.role ?? string.Empty,
                    kit: meta?.kit ?? string.Empty,
                    function: meta?.function ?? string.Empty,
                    style: meta?.style ?? string.Empty);
            }
            catch (Exception e)
            {
                SetStatus(string.Format(L("ui.ed.load_failed"), e.Message));
            }
        }

        // ----------------------------- kits, ports, the seal (#1877) -----------------------------

        /// <summary>The functions a module of this editor's kind may take: station functions, or the settlement plot
        /// and city district roles.</summary>
        private string[] Functions()
        {
            if (EditorMode == Mode.Station)
            {
                return StructureRoles.StationFunctions;
            }

            var list = new List<string>();
            foreach (var r in StructureRoles.All)
            {
                if (r.Length > 0)
                {
                    list.Add(r);
                }
            }

            return list.ToArray();
        }

        private string FunctionLabel(string function)
            => EditorMode == Mode.Station ? L("ui.function." + function) : RoleLabel(function);

        /// <summary>Keeps the legacy settlement role in step with the module view: a known plot / district function is
        /// the role (a city role pins the metropolis tier), anything else leaves it empty.</summary>
        private void SyncRole()
        {
            _role = EditorMode == Mode.Settlement && _moduleMode && StructureRoles.IsKnown(_function) ? _function ?? string.Empty : string.Empty;
            int metropolis = System.Array.IndexOf(_tiers, StructureRoles.MetropolisTier);
            if (metropolis < 0)
            {
                return;
            }

            if (StructureRoles.IsCityRole(_role))
            {
                _tier = metropolis;
            }
            else if (_tier == metropolis)
            {
                _tier = System.Math.Max(0, System.Array.IndexOf(_tiers, "village"));
            }
        }

        /// <summary>The current build as the data contract the composers read (ports included).</summary>
        private StructureTemplate BuildTemplate()
        {
            int maxX = 0, maxY = 0, maxZ = 0;
            var t = new StructureTemplate { Key = Slug(_key), Name = _name, Tier = _tiers[_tier], Kind = ModeName, Kit = _moduleMode ? Slug(_kit) : string.Empty, Function = _moduleMode ? _function : string.Empty, Role = _role, Style = _moduleMode ? _style : string.Empty };
            foreach (var kv in _design)
            {
                var d = kv.Value;
                t.Cells.Add(new TemplateCell { X = kv.Key.X, Y = kv.Key.Y, Z = kv.Key.Z, Kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, Id = d.Id, Tint = d.Tint, Glow = d.Glow, Shape = d.Shape, Port = d.Port ?? string.Empty });
                maxX = Mathf.Max(maxX, kv.Key.X);
                maxY = Mathf.Max(maxY, kv.Key.Y);
                maxZ = Mathf.Max(maxZ, kv.Key.Z);
            }

            t.Width = maxX + 1;
            t.Height = maxY + 1;
            t.Length = maxZ + 1;
            return t;
        }

        /// <summary>Save gate (#1877): port errors, — for station modules — leaks, and (#1901) a block standing in a door's
        /// lane block the export.</summary>
        private bool ValidateForExport()
        {
            var t = BuildTemplate();
            var errors = StructurePorts.Validate(t);
            if (errors.Count > 0)
            {
                SetStatus(string.Format(L("ui.ed.port_errors"), errors.Count, errors[0]));
                return false;
            }

            if (_moduleMode && EditorMode == Mode.Station)
            {
                var leaks = StructureSeal.FindLeaks(t);
                if (leaks.Count > 0)
                {
                    PaintLeaks(leaks);
                    SetStatus(string.Format(L("ui.ed.seal_leaks"), leaks.Count));
                    return false;
                }
            }

            var blockedLanes = RoomFurnisher.BlockedDoorLanes(t);
            if (blockedLanes.Count > 0)
            {
                PaintLeaks(blockedLanes);
                SetStatus(string.Format(L("ui.ed.door_lanes_blocked"), blockedLanes.Count));
                return false;
            }

            return true;
        }

        /// <summary>The "Check seal" button: paints the leaks — or else the blocks in a door lane (#1901) — red, or reports the
        /// module airtight.</summary>
        private void CheckSeal()
        {
            var t = BuildTemplate();
            var errors = StructurePorts.Validate(t);
            var leaks = StructureSeal.FindLeaks(t);
            var blockedLanes = leaks.Count > 0 ? new List<Vector3i>() : RoomFurnisher.BlockedDoorLanes(t);
            PaintLeaks(leaks.Count > 0 ? leaks : blockedLanes);
            SetStatus(errors.Count > 0
                ? string.Format(L("ui.ed.port_errors"), errors.Count, errors[0])
                : leaks.Count > 0 ? string.Format(L("ui.ed.seal_leaks"), leaks.Count)
                : blockedLanes.Count > 0 ? string.Format(L("ui.ed.door_lanes_blocked"), blockedLanes.Count) : L("ui.ed.seal_ok"));
        }

        private void OpenKitPanel()
        {
            _kitPanel?.Close();
            _kitPanel = KitEditorPanel.Show(Shell, _canvas.transform, ModeName, Slug(_kit), key => { _kit = key; RebuildUi(); }, () => _kitPanel = null,
                kind => ModulePool(kind).FindAll(t => t.IsModule));
        }

        /// <summary>The kit named in the kit field: a shipped one, or the user's own file.</summary>
        private StructureKit FindKit(string key)
        {
            foreach (var j in KitEditorPanel.KnownKits(Shell, ModeName))
            {
                if (j.key == key)
                {
                    return j.ToKit();
                }
            }

            return null;
        }

        /// <summary>Every module of the editor's kind the composers may use: the shipped pools plus the user's template
        /// files (the client's content never reads the user-content folder).</summary>
        private List<StructureTemplate> ModulePool(string kind)
        {
            var list = new List<StructureTemplate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Shell?.Content != null)
            {
                foreach (var t in kind == StructureKit.KindStation ? Shell.Content.StationTemplates : Shell.Content.SettlementTemplates)
                {
                    if (seen.Add(t.Key))
                    {
                        list.Add(t);
                    }
                }
            }

            string dir = Path.Combine(AppPaths.Root, "usercontent", (kind == StructureKit.KindStation ? "station" : "settlement") + "_templates");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var j = JsonUtility.FromJson<TemplateJson>(File.ReadAllText(file));
                        if (j == null || j.cells == null || j.cells.Count == 0)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(j.key))
                        {
                            j.key = Path.GetFileNameWithoutExtension(file);
                        }

                        var t = new StructureTemplate { Key = j.key, Name = j.name, Tier = j.tier, Kind = j.kind, Pack = j.pack, Weight = j.weight, Role = j.role ?? string.Empty, Kit = j.kit ?? string.Empty, Function = j.function ?? string.Empty, Style = j.style ?? string.Empty, Width = j.width, Height = j.height, Length = j.length, PlanetTypes = j.planetTypes ?? new List<string>() };
                        foreach (var c in j.cells)
                        {
                            t.Cells.Add(new TemplateCell { X = c.x, Y = c.y, Z = c.z, Kind = c.kind, Id = c.id, Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty });
                        }

                        int i = list.FindIndex(x => string.Equals(x.Key, t.Key, StringComparison.OrdinalIgnoreCase));
                        if (i >= 0) list[i] = t; else list.Add(t);
                    }
                    catch (Exception)
                    {
                        // one unreadable file must not hide the rest
                    }
                }
            }

            return list;
        }

        /// <summary>The "Assemble" button (#1877): composes the kit named in the kit field with the real composer and
        /// loads the result into the room, read-only in spirit — a quick way to see how the pieces dock.</summary>
        private void Assemble()
        {
            if (Shell?.Content == null)
            {
                return;
            }

            var kit = FindKit(Slug(_kit));
            if (kit == null)
            {
                SetStatus(L("ui.ed.need_kit"));
                return;
            }

            var cells = new List<CellJson>();
            string tier = kit.Tier;
            try
            {
                int w, h, l;
                Func<int, int, int, ushort> get;
                Func<int, int, int, (int Tint, int Glow)> modifier = null; // #1920: a station's blue solar wings keep their tint
                Func<int, int, int, int> shapeAt = null;
                IReadOnlyList<StationMarker> stationMarkers = null;
                IReadOnlyList<SettlementMarker> settlementMarkers = null;
                if (kit.KindOrDefault == StructureKit.KindStation)
                {
                    var pool = ModulePool(StructureKit.KindStation);
                    var s = StationKitComposer.Compose(kit, key => pool.Find(t => t.Key == key), _seed, Shell.Content, out _, out string failure);
                    if (s == null)
                    {
                        SetStatus(string.Format(L("ui.ed.assemble_failed"), failure));
                        return;
                    }

                    w = s.Width; h = s.Height; l = s.Length; get = s.Get; stationMarkers = s.Markers;
                    modifier = s.GetModifier;
                    shapeAt = s.GetShape;
                }
                else if (kit.KindOrDefault == StructureKit.KindCity)
                {
                    var pool = ModulePool(StructureKit.KindSettlement);
                    var s = CityGenerator.Generate(_seed, Shell.Content, Array.Empty<CityGenerator.OpenZone>(), null, 0, new List<string>(), null, CityLayoutSpec.FromKit(kit), kit, pool);
                    w = s.Width; h = s.Height; l = s.Length; get = s.Get; settlementMarkers = s.Markers;
                }
                else
                {
                    var pool = ModulePool(StructureKit.KindSettlement);
                    string surface = Slug(_surface);
                    if (string.IsNullOrEmpty(surface) || Shell.Content.GetBlock(surface) == null)
                    {
                        surface = "stone";
                    }

                    var layout = SettlementLayoutSpec.FromKit(kit, tier, new System.Random(_seed));
                    var s = SettlementGenerator.Generate(tier, false, _seed, surface, Shell.Content, null, 0, new List<string>(), null, layout, kit, pool);
                    w = s.Width; h = s.Height; l = s.Length; get = s.Get; settlementMarkers = s.Markers;
                }

                if (stationMarkers != null)
                {
                    foreach (var m in stationMarkers)
                    {
                        cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                    }
                }

                if (settlementMarkers != null)
                {
                    foreach (var m in settlementMarkers)
                    {
                        cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                    }
                }

                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                        for (int z = 0; z < l; z++)
                        {
                            ushort v = get(x, y, z);
                            if (v == 0 || Shell.Content.BlockById(new BlockId(v)) is not { } def)
                            {
                                continue;
                            }

                            var (tint, glow) = modifier != null ? modifier(x, y, z) : (0, 0);
                            cells.Add(new CellJson { x = x, y = y, z = z, kind = "block", id = def.Key, tint = tint, glow = glow, shape = shapeAt?.Invoke(x, y, z) ?? 0 });
                        }
            }
            catch (Exception e)
            {
                SetStatus(string.Format(L("ui.ed.assemble_failed"), e.Message));
                return;
            }

            // The composed result is a whole structure (a preview to walk through or save as a complete one); the kit
            // key stays in the field so switching back to "module" re-assembles with the next seed straight away.
            string kitKey = kit.Key;
            ApplyTemplate($"{kitKey}_{_seed}", $"{kit.Name} #{_seed}", tier, "default", 1, null, cells, copy: false);
            _kit = kitKey;
            SetStatus(string.Format(L("ui.ed.assembled"), kitKey, _design.Count, _seed) + "\n" + _status);
        }

        /// <summary>Rebuilds the editor UI so the key/name/tier fields reflect a freshly loaded design (the
        /// placed cells live under the editor transform, not the canvas, so they survive the rebuild).</summary>
        private void RebuildUi()
        {
            _loadPicker?.Close(); // (also destroyed along with the old canvas)
            _loadPicker = null;
            _kitPanel?.Close();
            _kitPanel = null;
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

        // ----------------------------- UI (uGUI) -----------------------------

        private const float PanelH = 1048f;
        private Canvas _canvas;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Text _tierLabel;
        private Text _roleLabel;
        private Text _sizeHintLabel;
        private Text _weightLabel;
        private Text _shapeLabel;
        private Text _orientLabel;
        private Transform _palListParent;
        private PaletteListUi _palList;
        private int _lastPlaced = -1;

        private void OnDestroy()
        {
            _kitPanel?.Close();
            _kitPanel = null;
            _view?.Dispose();
            _ghost?.Dispose();
            _atlas?.Release(); // palette sprites reference this texture; the editor holds a reference (#423 lesson, shared since #1523)
            _atlas = null;
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

            // Left: palette (markers + every placeable block), grouped by category, with a search filter.
            var pal = UiKit.AddPanel(root, 16f, 16f, 300f, PanelH, UiKit.PanelFill);
            UiKit.AddText(pal.transform, 16f, 12f, 268f, 26f, L("ui.struct.palette"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(pal.transform, 12f, 42f, 276f, 28f, _search, v => { _search = v ?? string.Empty; _palList.Rebuild(_search); }, L("ui.pal.search"));
            _palListParent = UiKit.ScrollList(pal.transform, 10f, 78f, 280f, PanelH - 90f);
            _palList = new PaletteListUi(Shell, _palListParent, _palette, _selected);
            _palList.OnSelected = i => _selected = i;
            _palList.Rebuild(_search);

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
            _tierLabel = UiKit.AddText(meta, 176f, y, 120f, 30f, TierLabel(_tiers[_tier]), 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 300f, y, 30f, 30f, "→", () =>
            {
                _tier = (_tier + 1) % _tiers.Length;
                _tierLabel.text = TierLabel(_tiers[_tier]);
                if (_sizeHintLabel != null) _sizeHintLabel.text = SizeHint(_tiers[_tier]);
            });
            y += 32f;

            // What the procedural generator builds for this tier, so a template matches its scale (#1402).
            _sizeHintLabel = UiKit.AddText(meta, 16f, y, 348f, 22f, SizeHint(_tiers[_tier]), 12, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;

            // Use as (#1826 / #1877): a complete structure, or a MODULE of a kit with a function — the composers dock
            // station modules port to port and stamp settlement / city modules into plots and districts.
            // #1890: the kit panel is reachable from here in both modes — editing a kit needs no module on the table.
            UiKit.AddText(meta, 16f, y, 120f, 30f, L("ui.struct.role"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _roleLabel = UiKit.AddText(meta, 136f, y, 118f, 30f, L(_moduleMode ? "ui.use.module" : "ui.use.whole"), 13, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 290f, y, 74f, 30f, L("ui.struct.kits"), OpenKitPanel);
            UiKit.AddButton(meta, 256f, y, 30f, 30f, "→", () =>
            {
                _moduleMode = !_moduleMode;
                if (_moduleMode && string.IsNullOrEmpty(_function))
                {
                    _function = Functions()[0];
                }

                SyncRole();
                RebuildUi();
            });
            y += 32f;
            if (_moduleMode)
            {
                UiKit.AddText(meta, 16f, y, 56f, 30f, L("ui.struct.kit"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddInput(meta, 74f, y, 290f, 30f, _kit, v => _kit = v ?? string.Empty);
                y += 36f;
                UiKit.AddText(meta, 16f, y, 150f, 30f, L("ui.struct.function"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                var functionLabel = UiKit.AddText(meta, 176f, y, 120f, 30f, FunctionLabel(_function), 13, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddButton(meta, 300f, y, 30f, 30f, "→", () =>
                {
                    var list = Functions();
                    int i = System.Array.IndexOf(list, _function ?? string.Empty);
                    _function = list[(i + 1) % list.Length];
                    SyncRole();
                    functionLabel.text = FunctionLabel(_function);
                    _tierLabel.text = TierLabel(_tiers[_tier]);
                    if (_sizeHintLabel != null) _sizeHintLabel.text = SizeHint(_tiers[_tier]);
                });
                y += 32f;
                if (EditorMode == Mode.Station)
                {
                    UiKit.AddText(meta, 16f, y, 150f, 30f, L("ui.struct.port_door"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var doorLabel = UiKit.AddText(meta, 176f, y, 120f, 30f, L("ui.door_opt." + StructurePorts.DoorOptions[_portDoor]), 13, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiKit.AddButton(meta, 300f, y, 30f, 30f, "→", () =>
                    {
                        _portDoor = (_portDoor + 1) % StructurePorts.DoorOptions.Length;
                        doorLabel.text = L("ui.door_opt." + StructurePorts.DoorOptions[_portDoor]);
                    });
                }
                else
                {
                    // #1890: a settlement module is built for humans or for aliens; a kit uses the settlement's own.
                    UiKit.AddText(meta, 16f, y, 150f, 30f, L("ui.struct.style"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var styleLabel = UiKit.AddText(meta, 176f, y, 120f, 30f, L(_style == StructureTemplate.StyleAlien ? "ui.style.alien" : "ui.style.human"), 13, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiKit.AddButton(meta, 300f, y, 30f, 30f, "→", () =>
                    {
                        _style = _style == StructureTemplate.StyleAlien ? string.Empty : StructureTemplate.StyleAlien;
                        styleLabel.text = L(_style == StructureTemplate.StyleAlien ? "ui.style.alien" : "ui.style.human");
                    });
                }

                y += 32f;
                UiKit.AddButton(meta, 16f, y, 168f, 30f, L("ui.struct.check_seal"), CheckSeal);
                UiKit.AddButton(meta, 196f, y, 168f, 30f, L("ui.struct.assemble"), Assemble);
                y += 36f;
            }

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

            // Settlements may restrict themselves to planet types (#1115); stations float in space.
            if (EditorMode == Mode.Settlement)
            {
                UiKit.AddText(meta, 16f, y, 348f, 22f, L("ui.struct.planet_types"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 26f;
                UiKit.AddInput(meta, 16f, y, 348f, 30f, _planetTypes, v => _planetTypes = v ?? string.Empty);
                y += 44f;
            }

            // ── Procedural starting point (#1401): run the world-gen generator for this tier + seed and
            // load the result into the room to refine into a template. ──
            UiKit.AddText(meta, 16f, y, 348f, 24f, L("ui.struct.generate_title"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            UiKit.AddText(meta, 16f, y, 56f, 30f, L("ui.struct.seed"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var seedInput = UiKit.AddInput(meta, 74f, y, 86f, 30f, _seed.ToString(), v => { if (int.TryParse(v, out var s)) _seed = s; });
            seedInput.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddButton(meta, 166f, y, 76f, 30f, L("ui.struct.reroll"), () =>
            {
                _seed = UnityEngine.Random.Range(1, 1000000);
                seedInput.SetTextWithoutNotify(_seed.ToString());
            });
            UiKit.AddButton(meta, 248f, y, 116f, 30f, L("ui.struct.generate"), GenerateProcedural);
            y += 38f;
            if (EditorMode == Mode.Settlement)
            {
                UiKit.AddText(meta, 16f, y, 130f, 30f, L("ui.struct.surface"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddInput(meta, 150f, y, 214f, 30f, _surface, v => _surface = v ?? string.Empty);
                y += 38f;
            }

            y += 8f;

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
            _shapeLabel = UiKit.AddText(meta, 116f, y, 140f, 30f, ShapeName(_brushShape), 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 262f, y, 30f, 30f, "−", () => { _brushShape = (_brushShape + ShapeSlugs.Length - 1) % ShapeSlugs.Length; _shapeLabel.text = ShapeName(_brushShape); });
            UiKit.AddButton(meta, 300f, y, 30f, 30f, "+", () => { _brushShape = (_brushShape + 1) % ShapeSlugs.Length; _shapeLabel.text = ShapeName(_brushShape); });
            y += 38f;
            UiKit.AddText(meta, 16f, y, 90f, 30f, L("ui.struct.orient"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _orientLabel = UiKit.AddText(meta, 116f, y, 140f, 30f, (_brushOrient * 90) + "°", 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(meta, 262f, y, 68f, 30f, "↻ R", () => { _brushOrient = (_brushOrient + 1) & 3; _orientLabel.text = (_brushOrient * 90) + "°"; });
            y += 46f;

            _lastPlaced = _design.Count;
            _blocksLabel = UiKit.AddText(meta, 16f, y, 348f, 24f, string.Format(L("ui.ed.placed"), _design.Count), 15, UiKit.TextCol, TextAnchor.MiddleLeft);

            // Footer: save gets a full-width row of its own so the label never has to shrink; load +
            // back share the row below.
            _statusLabel = UiKit.AddText(meta, 16f, PanelH - 208f, 348f, 84f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(meta, 16f, PanelH - 116f, 348f, 42f, L("ui.struct.save"), Export);
            UiKit.AddButton(meta, 16f, PanelH - 66f, 168f, 40f, L("ui.struct.load"), OpenLoadPicker);
            UiKit.AddButton(meta, 196f, PanelH - 66f, 168f, 40f, L("ui.menu.back"), () => Shell?.CloseStructureEditor());

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
