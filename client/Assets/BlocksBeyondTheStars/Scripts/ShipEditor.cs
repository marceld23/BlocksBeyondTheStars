// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
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
    /// <para>
    /// Coordinates (#1396, #1397): the layout's interior origin (0,0,0) sits at <see cref="Origin"/> in the
    /// room, so exterior cells at negative x/z (wings, engines, nav lights — every shipped layout has them)
    /// have space. The interior size is an explicit field, not the bounding box: the server treats
    /// <c>0..W-1 × 0..H × 0..L-1</c> as the cabin (floor guarantee, roof, hatch) and everything else as
    /// exterior, so a roof antenna must not grow the cabin. LOAD offers the shipped ships as starting
    /// points (#1394) next to the user's own exports.
    /// </para>
    /// <para>
    /// On a gamepad the editor has two focus modes and <b>Start</b> swaps between them (#1198), because
    /// one stick cannot walk a list and fly a camera at the same time. In PANEL mode
    /// (<see cref="UiNavFocus"/> active) the sticks walk the palette and the form. In VIEWPORT mode the
    /// left stick moves, the right stick looks, LB/RB drop and rise, the d-pad steps through the palette,
    /// A places, X removes, Y turns the shape brush, and a centre reticle replaces the mouse pointer as
    /// the picking ray's origin. B leaves the viewport; from the panels it leaves the editor.
    /// </para>
    /// </summary>
    public sealed class ShipEditor : MonoBehaviour
    {
        public AppShell Shell;

        private const int MaxW = ShipLayout.EditorRoomWidth, MaxH = ShipLayout.EditorRoomHeight, MaxL = ShipLayout.EditorRoomLength;
        private const float RaycastDist = 1200f;

        /// <summary>Room cell that holds the layout's interior origin (0,0,0); export subtracts it, load adds it.</summary>
        private static readonly Vector3i Origin = new(ShipLayout.EditorOriginX, 0, ShipLayout.EditorOriginZ);

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        // Gamepad focus (#1198): false = the side panels own the sticks, true = the build viewport does.
        // Panels first, so a pad player lands on the palette and picks a block before flying anywhere.
        private bool _padViewport;
        private GameObject _reticle;
        private Text _hintLabel;

        // Stick look is already a per-frame delta (GamepadInputSource scales by deltaTime); this puts it
        // in the same range as the mouse path's 2.6 multiplier so both feel alike.
        private const float PadLookScale = 2.6f;

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

        // Interior (cabin) size the server stamps as 0..W-1 × 0..H × 0..L-1 (#1396) — explicit, never derived
        // from the placed cells, because exterior greebles sit outside it. Defaults match the starter box.
        private int _intW = 5, _intL = 7, _intH = 4;
        private EditorInteriorFrame _frame;

        // The modules a loaded built-in ship starts with (reactor, life support, weapons …) — carried through
        // to ship.json so tools/merge_ship.py keeps them instead of deriving only from station tiles (#1399).
        private List<string> _startModules = new();

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
            _yaw = 0f;

            _view = new EditorVoxelChunkView(transform);
            _view.SetAtlas(_atlas?.Texture); // real block tiles on placed cells (#1400)
            _ghost = new EditorPlacementGhost(transform);
            BuildRoom();
            _frame = new EditorInteriorFrame(transform);
            RebuildFrame();
            FrameCamera(); // opening view (#1390): the interior frame when nothing is placed yet
            BuildUi();
        }

        private void RebuildFrame() => _frame?.Rebuild(Origin, Mathf.Max(1, _intW), Mathf.Max(1, _intH), Mathf.Max(1, _intL));

        /// <summary>Frames the build — or, with nothing placed, the interior frame, so a fresh editor opens
        /// on the cabin volume rather than an arbitrary patch of floor.</summary>
        private void FrameCamera()
        {
            IReadOnlyCollection<Vector3i> cells = _design.Keys;
            if (cells.Count == 0)
            {
                cells = new[]
                {
                    Origin,
                    new Vector3i(Origin.X + _intW - 1, _intH, Origin.Z + _intL - 1),
                };
            }

            EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, cells, MaxW, MaxL);
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
                P("console", "station", new Color(0.35f, 0.7f, 0.9f)), // the ship console (#1073); in 5 of 7 shipped layouts (#1398)
                P("hangar", "station", new Color(0.35f, 0.4f, 0.46f)),
                P("ship_laser_basic", "station", new Color(0.45f, 1f, 1f)),
                P("ship_cannon_1", "station", new Color(0.95f, 0.55f, 0.4f)),
            };

            list.AddRange(EditorPaletteKit.BlockEntries(Shell, _atlas));
            return list.ToArray();
        }

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

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            UpdatePadFocus(pad);
            bool padViewport = pad && _padViewport;

            // The mouse looks while RMB is held; the pad looks whenever the viewport has focus. Holding a
            // button to look would cost a face button the editor needs for placing.
            bool flying = Input.GetMouseButton(1);
            Cursor.lockState = flying ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !flying;

            if (flying)
            {
                _yaw += Input.GetAxis("Mouse X") * 2.6f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 2.6f, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else if (padViewport)
            {
                _yaw += InputMap.PadLookX() * PadLookScale;
                _pitch = Mathf.Clamp(_pitch - InputMap.PadLookY() * PadLookScale, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // Keystrokes belong to a focused text field (the palette search, the form inputs) — typing
            // "sand" must not fly the camera away or turn the brush (#1388).
            bool typing = UiKit.TextFieldFocused();
            bool fast = Input.GetKey(KeyCode.LeftShift) || (padViewport && InputMap.PadHeld(PadButton.L3));
            float speed = (fast ? 30f : 14f) * Time.deltaTime;
            var move = Vector3.zero;
            if (!typing)
            {
                if (Input.GetKey(KeyCode.W)) move += _cam.transform.forward;
                if (Input.GetKey(KeyCode.S)) move -= _cam.transform.forward;
                if (Input.GetKey(KeyCode.D)) move += _cam.transform.right;
                if (Input.GetKey(KeyCode.A)) move -= _cam.transform.right;
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;
                if (Input.GetKeyDown(KeyCode.F))
                {
                    FrameCamera();
                }
            }

            if (padViewport)
            {
                move += _cam.transform.forward * InputMap.PadStickY();
                move += _cam.transform.right * InputMap.PadStickX();
                if (InputMap.PadHeld(PadButton.Rb)) move += Vector3.up;
                if (InputMap.PadHeld(PadButton.Lb)) move += Vector3.down;
            }

            _cam.transform.position += move * speed;

            // Place (LMB) / remove (MMB) when not flying and not over a uGUI panel. The pad has no pointer,
            // so in viewport mode the panels can never be 'under' the aim and the reticle always picks.
            _mouseOverUi = !padViewport && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!_mouseOverUi)
            {
                EditorSceneKit.WheelDolly(_cam.transform, fast); // wheel over a panel scrolls the panel (#1390)
            }

            if (_blocksLabel != null && _lastPlaced != _design.Count)
            {
                _lastPlaced = _design.Count;
                _blocksLabel.text = string.Format(L("ui.ed.placed"), _design.Count);
            }

            UpdateGhost(flying || _mouseOverUi);
            if (padViewport)
            {
                if (InputMap.PadDown(PadButton.A))
                {
                    TryPlace();
                }
                else if (InputMap.PadDown(PadButton.X))
                {
                    TryRemove();
                }

                StepPalette(InputMap.PadDpadStep());
            }
            else if (!flying && !_mouseOverUi)
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

            // Rotate the shape brush (matches the in-game place-orientation control); Y on the pad.
            if ((!typing && !_mouseOverUi && Input.GetKeyDown(KeyCode.R)) || (padViewport && InputMap.PadDown(PadButton.Y)))
            {
                _brushOrient = (_brushOrient + 1) & 3;
            }

            RefreshPadChrome(pad, padViewport);

            _view.Flush(); // upload any chunk meshes touched by this frame's edits
        }

        /// <summary>Start swaps the pad between the side panels and the build viewport; B leaves the
        /// viewport. Exactly one of the two owns the sticks at a time, so walking a list and flying the
        /// camera never fight over the same axis (#1198). From the panels, B falls through to AppShell,
        /// which closes the editor.</summary>
        private void UpdatePadFocus(bool pad)
        {
            if (_canvas == null)
            {
                return; // BuildUi has not run yet
            }

            if (!pad)
            {
                UiNav.SetSuspended(_canvas.gameObject, false); // mouse in hand — the panels are always live
                return;
            }

            if (InputMap.Down(InputAction.UiMenu))
            {
                _padViewport = !_padViewport;
            }
            else if (_padViewport && InputMap.Down(InputAction.UiCancel))
            {
                _padViewport = false;
            }

            UiNav.SetSuspended(_canvas.gameObject, _padViewport);
        }

        /// <summary>Moves the palette selection by one repeat-gated d-pad step (the pad's stand-in for
        /// scrolling the list, which needs a pointer).</summary>
        private void StepPalette(float step)
        {
            if (Mathf.Abs(step) < 0.5f || _palList == null || _palette == null || _palette.Length == 0)
            {
                return;
            }

            int next = _palList.Selected + (step > 0f ? -1 : 1); // >0 = left = previous, matching the hotbar
            _palList.Select(Mathf.Clamp(next, 0, _palette.Length - 1));
        }

        /// <summary>Shows the centre reticle and the pad control hint only while the pad flies the
        /// viewport — with a mouse the pointer IS the aim, and a crosshair would just be clutter.</summary>
        private void RefreshPadChrome(bool pad, bool padViewport)
        {
            if (_reticle != null && _reticle.activeSelf != padViewport)
            {
                _reticle.SetActive(padViewport);
            }

            if (_hintLabel != null)
            {
                string key = !pad ? "ui.struct.hint" : padViewport ? "ui.struct.hint_pad" : "ui.struct.hint_pad_panel";
                string text = L(key);
                if (_hintLabel.text != text)
                {
                    _hintLabel.text = text;
                }
            }
        }

        /// <summary>Where the picking ray starts on screen: the mouse pointer, or the centre reticle while
        /// the pad owns the viewport — a pad has no pointer, and a screen-centre crosshair is how every
        /// pad-driven builder aims (#1198).</summary>
        private Vector3 PickPoint() => _padViewport && InputMap.ActiveDevice == InputDeviceKind.Gamepad
            ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
            : Input.mousePosition;

        /// <summary>Resolves the cell a placement would land in: the floor column, or the empty cell just
        /// outside the hit face (the chunk mesh is authored in world coords, so the hit point + normal locate
        /// the cell directly — no per-cell GameObject to read a transform from).</summary>
        private bool TryGetTargetCell(out Vector3i cell)
        {
            cell = default;
            var ray = _cam.ScreenPointToRay(PickPoint());
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
            var ray = _cam.ScreenPointToRay(PickPoint());
            if (!Physics.Raycast(ray, out var hit, RaycastDist) || hit.collider.gameObject == _floor)
            {
                return false;
            }

            Vector3 inside = hit.point - hit.normal * 0.5f; // step into the hit cell
            cell = new Vector3i(Mathf.FloorToInt(inside.x), Mathf.FloorToInt(inside.y), Mathf.FloorToInt(inside.z));
            return true;
        }

        private EditorPlacementGhost _ghost;

        /// <summary>The placement ghost (shared <see cref="EditorPlacementGhost"/>): green when the
        /// placement is valid, red when out of bounds or occupied.</summary>
        private void UpdateGhost(bool hidden)
        {
            Vector3i cell = default;
            bool show = !hidden && TryGetTargetCell(out cell);
            _ghost?.Update(show, cell, show && InBounds(cell) && !_design.ContainsKey(cell));
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
            // Real blocks (and the elements that are blocks, e.g. glass / nav lights) show their atlas tile
            // (#1400); dye/glow tint the texture like in-game. Stations and non-block elements keep the
            // palette swatch. The chunked view bakes the directional shading + face culling.
            var tile = TileOf(pal, out bool textured);
            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : (textured ? Color.white : pal.Color));

            _design[cell] = data;
            _view.Set(cell, new EditorVoxelChunkView.Cell
            {
                Color = baseCol,
                Glow = data.Glow != 0,
                Shape = data.Shape,
                Marker = false, // the ship editor has no markers (elements + stations are solid anchors)
                Textured = textured,
                Uv = tile,
            });
        }

        private bool InBounds(Vector3i c) => c.X >= 0 && c.X < MaxW && c.Y >= 0 && c.Y < MaxH && c.Z >= 0 && c.Z < MaxL;

        /// <summary>The atlas tile of a palette entry that is a real content block (stations never are;
        /// elements only when their id is a block key); <paramref name="textured"/> = false otherwise.</summary>
        private Rect TileOf(EditorPaletteKit.Entry pal, out bool textured)
        {
            textured = false;
            if (_atlas == null || pal.Kind == "station" || Shell?.Content?.GetBlock(pal.Id) is not { } def)
            {
                return default;
            }

            textured = true;
            return _atlas.TileUv(def.NumericId.Value);
        }

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
            _ghost?.Dispose();
            _frame?.Dispose();
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
            UiNav.Enable(_canvas.gameObject); // pad: walk the palette + the form (#1198)
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
            _statusLabel = UiKit.AddText(meta, 12f, PanelH - 160f, 356f, 46f, string.Empty, 13, UiKit.Ok);
            _statusLabel.alignment = TextAnchor.UpperLeft;
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap; // "skipped 3 cells: …" needs a second line
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
            _hintLabel = hint; // RefreshPadChrome swaps this for the pad wording

            BuildReticle(root);
        }

        /// <summary>The pad's aiming crosshair: two thin bars at the exact screen centre, which is where
        /// <see cref="PickPoint"/> casts from. Hidden until the pad takes the viewport.</summary>
        private void BuildReticle(Transform root)
        {
            _reticle = new GameObject("Reticle", typeof(RectTransform));
            _reticle.transform.SetParent(root, false);
            var rt = (RectTransform)_reticle.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(22f, 22f);
            rt.anchoredPosition = Vector2.zero;
            Bar(rt, new Vector2(22f, 2f));
            Bar(rt, new Vector2(2f, 22f));
            _reticle.SetActive(false);

            static void Bar(RectTransform parent, Vector2 size)
            {
                var go = new GameObject("Bar", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var brt = (RectTransform)go.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = size;
                brt.anchoredPosition = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.sprite = UiKit.SolidSprite;
                img.color = new Color(0.45f, 0.92f, 1f, 0.85f);
                img.raycastTarget = false;
            }
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

            // Interior (cabin) frame: the volume the server walks, floors and roofs (#1396). The cyan box in
            // the room follows these steppers; exterior cells may sit anywhere outside it.
            FormHeader(L("ui.ship.interior"));
            Stepper(L("ui.ship.int_w"), () => _intW, v => { _intW = Mathf.RoundToInt(v); RebuildFrame(); }, 3f, MaxW - Origin.X, 1f, "0");
            Stepper(L("ui.ship.int_l"), () => _intL, v => { _intL = Mathf.RoundToInt(v); RebuildFrame(); }, 3f, MaxL - Origin.Z, 1f, "0");
            Stepper(L("ui.ship.int_h"), () => _intH, v => { _intH = Mathf.RoundToInt(v); RebuildFrame(); }, 2f, MaxH - 1, 1f, "0");
            var hint = FormLabel(L("ui.ship.interior_hint"));
            hint.fontSize = 12;
            hint.color = UiKit.CyanDim;

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
            public List<string> startModules = new();
        }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                _status = L("ui.ed.need_key");
                return;
            }

            // Cells go out in layout coordinates (room minus the interior origin, #1397); the interior size is
            // the explicit cabin frame, not the bounding box of everything placed (#1396).
            var layout = new ExportLayoutJson { width = _intW, height = _intH, length = _intL };
            foreach (var kv in _design)
            {
                var d = kv.Value;
                layout.cells.Add(new ExportCellJson
                {
                    x = kv.Key.X - Origin.X, y = kv.Key.Y - Origin.Y, z = kv.Key.Z - Origin.Z,
                    kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, id = d.Id,
                    tint = d.Tint, glow = d.Glow, shape = d.Shape,
                });
            }

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
                startModules = new List<string>(_startModules),
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
                string dir = Path.Combine(AppPaths.Root, "ship_exports", key);
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

        private EditorLoadPicker _loadPicker;

        /// <summary>The LOAD dialog (#1394): the shipped ships (every <see cref="ShipDefinition"/> with a
        /// layout, straight from the loaded content) as starting points, then the user's own exports under
        /// <c>ship_exports/</c>. The starter ship is a code-built box without a layout and is not listed.</summary>
        private void OpenLoadPicker()
        {
            _loadPicker?.Close();

            var builtIn = new EditorLoadPicker.Section { Title = L("ui.ed.sec_builtin_ships") };
            if (Shell?.Content != null)
            {
                var ships = new List<ShipDefinition>(Shell.Content.Ships.Values);
                ships.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var def in ships)
                {
                    var layout = Shell.Content.GetShipLayout(def.Layout);
                    if (layout == null)
                    {
                        continue;
                    }

                    var d = def;
                    builtIn.Items.Add(new EditorLoadPicker.Item
                    {
                        Label = L(def.NameKey),
                        Detail = $"{layout.Width}×{layout.Length}×{layout.Height} · " + string.Format(L("ui.ed.cells"), layout.Cells.Count),
                        Load = () => LoadBuiltIn(d),
                    });
                }
            }

            var mine = new EditorLoadPicker.Section { Title = L("ui.ed.sec_exports") };
            string root = Path.Combine(AppPaths.Root, "ship_exports");
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (!File.Exists(Path.Combine(dir, "layout.json")))
                    {
                        continue;
                    }

                    string k = Path.GetFileName(dir);
                    mine.Items.Add(new EditorLoadPicker.Item { Label = k, Load = () => LoadDesign(k) });
                }
            }

            _loadPicker = EditorLoadPicker.Show(Shell, _canvas.transform, new[] { builtIn, mine }, _design.Count, () => _loadPicker = null);
        }

        /// <summary>Finds the palette entry for a loaded cell — by id AND kind first (a block and a marker may
        /// share an id), then by id alone; <c>Id == null</c> when the palette has no such entry.</summary>
        private EditorPaletteKit.Entry FindPalette(string id, string kind)
        {
            int i = System.Array.FindIndex(_palette, p => p.Id == id && p.Kind == kind);
            if (i < 0)
            {
                i = System.Array.FindIndex(_palette, p => p.Id == id);
            }

            return i < 0 ? default : _palette[i];
        }

        /// <summary>Replaces the build with <paramref name="cells"/> (layout coordinates; the room origin is
        /// added). Cells whose id the palette lacks, or that fall outside the room, are counted and named
        /// so the status line can say what was lost instead of dropping them silently (#1398).</summary>
        private int ApplyCells(IEnumerable<ExportCellJson> cells, List<string> skippedIds)
        {
            _view.Clear();
            _design.Clear();
            int skipped = 0;
            foreach (var c in cells)
            {
                var cell = new Vector3i(c.x + Origin.X, c.y + Origin.Y, c.z + Origin.Z);
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
                    Tint = c.tint, Glow = c.glow, Shape = c.shape,
                };
                PlaceCellData(cell, pal, data);
            }

            _view.Flush(); // build all loaded chunks in one batch
            return skipped;
        }

        /// <summary>Common tail of every load: frame, status (with the skipped-cells note), form.</summary>
        private void FinishLoad(string label, int skipped, List<string> skippedIds)
        {
            RebuildFrame();
            FrameCamera();
            _status = string.Format(L("ui.ed.loaded"), label, _design.Count);
            if (skipped > 0)
            {
                _status += "\n" + string.Format(L("ui.ed.skipped"), skipped, string.Join(", ", skippedIds));
            }

            RebuildForm();
        }

        /// <summary>Loads a shipped ship type as a starting point (#1394): its layout cells plus the whole
        /// definition (localized name/description, stats, craft cost, blueprint, interior size, start
        /// modules). Saving under the same key replaces the ship at merge time — the intended edit path.</summary>
        private void LoadBuiltIn(ShipDefinition def)
        {
            var layout = Shell?.Content?.GetShipLayout(def.Layout);
            if (layout == null)
            {
                _status = L("ui.ed.not_found");
                return;
            }

            var cells = new List<ExportCellJson>(layout.Cells.Count);
            foreach (var c in layout.Cells)
            {
                cells.Add(new ExportCellJson { x = c.X, y = c.Y, z = c.Z, kind = c.Kind, id = c.Id, tint = c.Tint, glow = c.Glow, shape = c.Shape });
            }

            var skippedIds = new List<string>();
            int skipped = ApplyCells(cells, skippedIds);

            _key = def.Key;
            _shipName = L(def.NameKey);
            _desc = L(def.DescriptionKey);
            _requiredBlueprint = def.RequiredBlueprint ?? string.Empty;
            _hull = def.BaseHull;
            _shield = def.BaseShield;
            _flightSpeed = def.FlightSpeed;
            _handling = def.Handling;
            _intW = Mathf.Max(1, layout.Width);
            _intL = Mathf.Max(1, layout.Length);
            _intH = Mathf.Max(1, layout.Height);
            _startModules = new List<string>(def.StartModules);
            _craftCost.Clear();
            foreach (var c in def.CraftCost)
            {
                _craftCost.Add(new CostRow { Item = c.Item, Count = c.Count });
            }

            FinishLoad(_shipName, skipped, skippedIds);
        }

        /// <summary>Clears the current build and rebuilds it from a saved design's <c>layout.json</c>.</summary>
        private void LoadDesign(string key)
        {
            string dir = Path.Combine(AppPaths.Root, "ship_exports", key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                _status = L("ui.ed.not_found");
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<ExportLayoutJson>(File.ReadAllText(layoutPath));
                var skippedIds = new List<string>();
                int skipped = ApplyCells(layout?.cells ?? new List<ExportCellJson>(), skippedIds);
                if (layout != null && layout.width > 0 && layout.length > 0 && layout.height > 0)
                {
                    _intW = layout.width;
                    _intL = layout.length;
                    _intH = layout.height;
                }

                string shipPath = Path.Combine(dir, "ship.json");
                if (File.Exists(shipPath) && JsonUtility.FromJson<ExportShipJson>(File.ReadAllText(shipPath)) is { } ship)
                {
                    _key = string.IsNullOrEmpty(ship.key) ? key : ship.key;
                    _shipName = string.IsNullOrEmpty(ship.name) ? _shipName : ship.name;
                    _desc = string.IsNullOrEmpty(ship.description) ? _desc : ship.description;
                    _requiredBlueprint = ship.requiredBlueprint ?? string.Empty;
                    if (ship.baseHull > 0) _hull = ship.baseHull;
                    _shield = ship.baseShield;
                    if (ship.flightSpeed > 0) _flightSpeed = ship.flightSpeed;
                    if (ship.handling > 0) _handling = ship.handling;
                    if (ship.cargoSlots > 0) _cargo = ship.cargoSlots;
                    _startModules = ship.startModules != null ? new List<string>(ship.startModules) : new List<string>();
                    if (ship.craftCost != null && ship.craftCost.Count > 0)
                    {
                        _craftCost.Clear();
                        foreach (var c in ship.craftCost)
                        {
                            _craftCost.Add(new CostRow { Item = c.item ?? string.Empty, Count = c.count });
                        }
                    }
                }
                else
                {
                    _key = key;
                }

                FinishLoad(key, skipped, skippedIds);
            }
            catch (Exception e)
            {
                _status = string.Format(L("ui.ed.load_failed"), e.Message);
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
    }
}
