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
    /// layout.json) a developer folds into the game's template pools with tools/merge_structure.py;
    /// <b>Submit</b> (#3262, <c>StructureEditor.Submit.cs</c>) sends the same bundle — with its kit — to the
    /// developers for review. Self-contained on the client; the palette + size tiers switch by <see cref="EditorMode"/>.
    /// <para>
    /// <b>LOAD</b> (#1395) offers the shipped templates (from the loaded content) and the templates already
    /// saved to the user-content folder as starting points, next to the user's exports. A shipped template
    /// is loaded as a COPY (key suffixed <c>_2</c>): user templates are added to the pool, not merged over
    /// it, so saving under the original key would only put a clone next to the original.
    /// </para>
    /// </summary>
    public sealed partial class StructureEditor : MonoBehaviour
    {
        public enum Mode { Station, Settlement }

        public AppShell Shell;
        public Mode EditorMode = Mode.Station;

        private int MaxW => RoomSize; private int MaxH => RoomSize; private int MaxL => RoomSize; // #3491: 128 for a station, 256 in the town editor
        private const float RaycastDist = 2400f;

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        /// <summary>One authored cell: the palette id + kind, plus the in-game per-voxel modifiers
        /// (dye/glow colour 0xRRGGBB, packed shape+orientation). Markers carry no modifiers.</summary>
        private struct CellData { public string Id; public string Kind; public int Tint, Glow, Shape; public string Port; public int Mode; public string Config, Label; public bool Keep; }

        /// <summary>#2260: what the device tool last set per device block — the next one of that block placed comes out alike.</summary>
        private readonly Dictionary<string, (int Mode, string Config, string Label)> _deviceBrush = new();
        private EditorDevicePanel _devicePanel;

        private readonly Dictionary<Vector3i, CellData> _design = new();   // cell -> authored cell (export source)
        private EditorVoxelChunkView _view;                                // chunked combined-mesh renderer

        private BlockTextureAtlas _atlas;                                  // editor-local atlas for palette icons + cell colours
        private EditorPaletteKit.Entry[] _palette;
        private int _selected;
        private string[] _tiers;
        private int _tier;

        // Brush: the dye/glow colour + shape + orientation applied to newly placed BLOCK cells (markers
        // ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / plain cube.
        private int _brushTint, _brushGlow, _brushOrient;
        private int _brushShape = EditorPlacementRules.AutoForm; // -1 = the block's own form (#1975); 0 = a cube on purpose
        private string _search = string.Empty;

        /// <summary>The form brush's name: "Automatic" (the block's own form, #1975) or the built-in form's name.</summary>
        private string FormName(int shape)
            => shape < 0 ? L("ui.ed.form_auto_short") : L(BuiltInForms.LocKeyOf(shape) ?? "ui.shape.cube"); // "Auto": a 112 px value (#3263)

        /// <summary>A tier's name as a compact value — the metropolis without its "(city district)" (#3263).</summary>
        private string TierLabel(string slug) => L(slug == StructureRoles.MetropolisTier ? "ui.tier_short.metropolis" : "ui.tier." + slug);

        /// <summary>The function value of a settlement module (#1826) — the role's short name, without "(module)" (#3263).</summary>
        private string RoleLabel(string role) => L("ui.role_short." + (string.IsNullOrEmpty(role) ? "whole" : role));

        private string _key = "my_structure";
        private string _name = string.Empty; // the localized default is set in Start (#3263)
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
            _name = L(EditorMode == Mode.Station ? "ui.ed.default.station" : "ui.ed.default.settlement");

            _cam = EditorSceneKit.CreateEditorCamera(transform, 1600f);
            EditorSceneKit.Frame(_cam.transform, ref _pitch, _yaw, _design.Keys, MaxW, MaxL); // opening view (#1390)

            _view = new EditorVoxelChunkView(transform);
            _view.SetAtlas(_atlas); // real block tiles on placed cells (#1400), each face its own (#2744)
            _ghost = new EditorPlacementGhost(transform);
            _overlay = new EditorPropOverlay(transform); // placed doors + marker silhouettes (#1975)
            _ghostMeshes = new EditorGhostMeshes();
            BuildRoom();
            BuildUi();
        }

        /// <summary>A marker palette entry: localized via <c>ui.marker.*</c>, grouped under "markers".</summary>
        private EditorPaletteKit.Entry M(string id, Color c) => new EditorPaletteKit.Entry
        {
            Id = id, Label = L("ui.marker." + id), Kind = "marker", Group = "markers", Color = c,
        };

        /// <summary>The full palette: the interaction markers this structure kind needs, the tools, the material tokens, then
        /// every placeable block from the loaded content — localized, category-grouped and iconed with its real atlas tile (see
        /// <see cref="EditorPaletteKit"/>). Built once in Start; the panel shows what the design's target reads (#3523,
        /// StructureEditor.Stamping.cs).</summary>
        private EditorPaletteKit.Entry[] BuildPalette()
        {
            var list = new List<EditorPaletteKit.Entry>();
            list.AddRange(WithLandmarkTools(EditorMode == Mode.Station ? StationMarkers() : SettlementMarkers())); // #3492
            list.AddRange(TokenEntries()); // #1890; #3523: a whole station template resolves them too
            list.AddRange(EditorPaletteKit.BlockEntries(Shell, _atlas, hullAirlocks: EditorMode == Mode.Station)); // #1982
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
            M("chest", new Color(0.75f, 0.55f, 0.25f)),      // #3523: a loot chest (its settings, generation 38)
            P("door"),                                       // #1877: docking ports — painted onto wall blocks
            P("wide"),
            P("ladder"),
            D(),                                             // #2260: a placed Crystal Net device's settings
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

        /// <summary>#2260: the device tool — a click on a placed Crystal Net device opens its settings.</summary>
        private EditorPaletteKit.Entry D() => new EditorPaletteKit.Entry
        {
            Id = "device", Label = L("ui.marker.device"), Kind = "device", Group = "markers", Color = new Color(0.62f, 0.45f, 1f),
        };

        /// <summary>The Crystal Net kind of an authored cell (None for markers and ordinary blocks).</summary>
        private CrystalDeviceKind DeviceKindOf(CellData d)
            => d.Kind == "block" && Shell?.Content?.GetBlock(d.Id) is { } def ? CrystalNetRules.KindOf(def) : CrystalDeviceKind.None;

        /// <summary>#2260: whether a block can carry pre-built settings — a conduit and a passive port (a lamp, a fire …) carry
        /// none; everything else the net registers does.</summary>
        private static bool CarriesDeviceSettings(CrystalDeviceKind kind)
            => kind != CrystalDeviceKind.None && kind != CrystalDeviceKind.Conduit && !CrystalNetRules.IsPassivePort(kind);

        /// <summary>Opens the device settings for the device cell under the cursor (#2260).</summary>
        private void EditDevice()
        {
            if (!TryGetHitCell(out var cell) || !_design.TryGetValue(cell, out var d))
            {
                return;
            }

            var kind = DeviceKindOf(d);
            if (!CarriesDeviceSettings(kind))
            {
                SetStatus(L("ui.ed.device.none"));
                return;
            }

            _devicePanel?.Close();
            _devicePanel = EditorDevicePanel.Show(Shell, _canvas.transform, kind, L("block." + d.Id + ".name"), d.Mode, d.Config, d.Label,
                (mode, config, label) =>
                {
                    if (!_design.TryGetValue(cell, out var now) || now.Id != d.Id)
                    {
                        return;
                    }

                    now.Mode = mode;
                    now.Config = config;
                    now.Label = label;
                    _deviceBrush[d.Id] = (mode, config, label);
                    PlaceCellData(cell, FindPalette(now.Id, now.Kind), now);
                    SetStatus(L("ui.ed.device.saved"));
                },
                () => _devicePanel = null);
        }

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
            M("entrance", new Color(0.95f, 0.85f, 0.45f)), // #3585: the road side of a module without a door (a field, a pen)
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
            P("door"),                                          // #1877: docking ports (ground kits may use them later)
            P("wide"),
            P("ladder"),
            D(),                                                // #2260: a placed Crystal Net device's settings
        };

        private void BuildRoom()
        {
            // Floor (raycast target) with the shared procedural grid + fill light (#1391).
            _floor = EditorSceneKit.BuildFloor(transform, MaxW, MaxL);
            EditorSceneKit.BuildSun(transform);
        }

        private void Update()
        {
            if (_cam == null || CreationSubmitDialog.IsAnyOpen)
            {
                return; // the submit dialog (#3262) owns the keys and the mouse while it is open
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
        private EditorPropOverlay _overlay;
        private EditorGhostMeshes _ghostMeshes;
        private EditorFormPicker _formPicker;
        private RawImage _formIcon;
        private string _hintShown;

        /// <summary>Shows where a click would land and WHAT it would leave there (#1975): the block's form (a bed as
        /// head + foot), the door the server would hang (axis + width read from the design), or a marker's
        /// silhouette — green when the placement is valid, red when the cell is occupied / out of bounds, or a door
        /// has no wall beside it or no free cells above (the status line says which).</summary>
        private void UpdateGhost(bool hidden)
        {
            if (hidden || _palette == null || _selected < 0 || _selected >= _palette.Length || !TryGetTargetCell(out var cell, out int hitFace))
            {
                _ghost?.Update(false, default, false);
                Hint(null);
                return;
            }

            var pal = _palette[_selected];
            if (pal.Kind is "device" or "port" or "keep" or "settings" or StructureStampRules.KindZone)
            {
                // A tool works on the cell under the cursor, not the cell beside it (#1877, #2260, #3492, #3493); the zone tool on
                // the corner it would set (StructureEditor.Zones.cs).
                bool on = ToolGhostCell(pal.Kind, out var hit);
                _ghost?.Update(on, hit, on);
                Hint(null);
                return;
            }

            bool free = InBounds(cell) && !_design.ContainsKey(cell);
            if (pal.Kind == "marker" && DoorBlocks.IsDoorMarker(pal.Id))
            {
                var fit = DoorProbe.Measure(IsSolidCell, cell.X, cell.Y, cell.Z);
                string why = null;
                bool valid = free && EditorPlacementRules.DoorValid(IsSolidCell, Occupied, InBounds, cell.X, cell.Y, cell.Z, out fit, out why);
                _ghost?.UpdateMesh(true, new Vector3(fit.CentreX(cell.X), cell.Y, fit.CentreZ(cell.Z)), valid,
                    _ghostMeshes.ForDoor(DoorBlocks.KindForMarker(pal.Id), fit.Width, fit.AxisX));
                Hint(free ? why : null);
                return;
            }

            if (pal.Kind == "marker")
            {
                // A silhouette where the game will put a person / a board / a chest …; a null mesh keeps the cube.
                _ghost?.UpdateMesh(true, new Vector3(cell.X, cell.Y, cell.Z), free, _ghostMeshes.ForSilhouette(pal.Id, MarkerSilhouettes.KindMarker));
                Hint(null);
                return;
            }

            // A block: the form it will take (and, for a bed, its foot half); a plain cube keeps the cube ghost.
            if (free)
            {
                if (!EditorPlacementRules.TryPlaceBlock(pal.Id, _brushShape, _brushOrient, cell.X, cell.Y, cell.Z, hitFace,
                        Occupied, InBounds, IsSolidCell, out var writes, out string refusal, Shell?.Content?.GetBlock(pal.Id)?.Facing))
                {
                    _ghost?.Update(true, cell, false);
                    Hint(refusal);
                    return;
                }

                if (writes.Count > 1 || writes[0].Shape != 0)
                {
                    _ghost?.UpdateMesh(true, new Vector3(cell.X, cell.Y, cell.Z), true, _ghostMeshes.ForWrites(writes, cell));
                    Hint(null);
                    return;
                }
            }

            _ghost?.Update(true, cell, free);
            Hint(null);
        }

        private void TryPlace()
        {
            var pal = _palette[_selected];
            if (ToolClick(pal))
            {
                return; // the port brush (#1877), the device tool (#2260), the keep brush, the settings tool (#3492/#3493) — or the ruin preview
            }

            if (!TryGetTargetCell(out var cell, out int hitFace) || !InBounds(cell) || _design.ContainsKey(cell))
            {
                return;
            }

            // A door marker with no wall beside it, or no room for the door above it, is never what was meant (#1975).
            if (pal.Kind == "marker" && DoorBlocks.IsDoorMarker(pal.Id)
                && !EditorPlacementRules.DoorValid(IsSolidCell, Occupied, InBounds, cell.X, cell.Y, cell.Z, out _, out string why))
            {
                SetStatus(L(why));
                return;
            }

            if (pal.Kind == "block")
            {
                // The server's own placement rules (#1975): the block's default form unless the brush chose one, a
                // bed as head + foot (refused where the foot would not fit), the ladder against its wall.
                if (!EditorPlacementRules.TryPlaceBlock(pal.Id, _brushShape, _brushOrient, cell.X, cell.Y, cell.Z, hitFace,
                        Occupied, InBounds, IsSolidCell, out var writes, out string refusal, Shell?.Content?.GetBlock(pal.Id)?.Facing))
                {
                    SetStatus(L(refusal));
                    return;
                }

                ClearLeaks();
                var device = _deviceBrush.TryGetValue(pal.Id, out var preset) ? preset : (0, string.Empty, string.Empty); // #2260
                foreach (var w in writes)
                {
                    PlaceCellData(new Vector3i(w.X, w.Y, w.Z), pal,
                        new CellData { Id = pal.Id, Kind = pal.Kind, Tint = _brushTint, Glow = _brushGlow, Shape = w.Shape, Mode = device.Item1, Config = device.Item2, Label = device.Item3 });
                }

                return;
            }

            ClearLeaks();
            PlaceCell(cell, pal);
        }

        private void TryRemove()
        {
            if (_previewOn || !TryGetHitCell(out var cell) || !_design.ContainsKey(cell))
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

            RemoveCell(cell);
            foreach (var (px, py, pz) in EditorPlacementRules.PartnerCells(d.Shape, cell.X, cell.Y, cell.Z))
            {
                // A bed goes as a pair (#1846): the other half of the same block leaves with this one.
                var partner = new Vector3i(px, py, pz);
                if (_design.TryGetValue(partner, out var pd) && pd.Id == d.Id)
                {
                    RemoveCell(partner);
                }
            }
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
        private bool TryGetTargetCell(out Vector3i cell) => TryGetTargetCell(out cell, out _);

        /// <summary>… and the face of the target cell the click came through (the hit normal; -1 for none): what a
        /// ladder hugs (#1975).</summary>
        private bool TryGetTargetCell(out Vector3i cell, out int hitFace)
            => EditorSceneKit.PickTargetCell(_cam, Input.mousePosition, RaycastDist, _floor, out cell, out hitFace);

        /// <summary>Resolves the actual occupied cell under the cursor (for removal); false on a miss or a
        /// floor-only hit.</summary>
        private bool TryGetHitCell(out Vector3i cell)
            => EditorSceneKit.PickHitCell(_cam, Input.mousePosition, RaycastDist, _floor, out cell);

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
            ushort blockId = 0;
            if (_atlas != null && pal.Kind != "marker" && Shell?.Content?.GetBlock(pal.Id) is { } def)
            {
                textured = true;
                tile = _atlas.TileUv(def.NumericId.Value);
                blockId = def.NumericId.Value; // #2744: each face its own tile, a front facing the open side
            }

            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : (textured ? Color.white : pal.Color));
            bool port = !string.IsNullOrEmpty(data.Port);
            baseCol = MarkedColor(baseCol, data); // #1877: a docking port reads cyan; #3492: a kept cell green

            bool door = pal.Kind == "marker" && DoorBlocks.IsDoorMarker(pal.Id);
            var silhouette = pal.Kind == "marker" && !door ? MarkerSilhouettes.For(pal.Id, MarkerSilhouettes.KindMarker) : null;
            _design[cell] = data;
            _view.Set(cell, new EditorVoxelChunkView.Cell
            {
                Color = baseCol,
                Glow = data.Glow != 0 || port,
                Shape = data.Shape,
                Marker = pal.Kind == "marker",
                Overlay = door || silhouette != null, // the overlay draws it (#1975): the real door / the silhouette
                Textured = textured,
                Uv = tile,
                BlockId = blockId,
            });

            if (door)
            {
                _overlay?.SetDoor(cell, DoorBlocks.KindForMarker(pal.Id), IsSolidCell);
            }
            else if (silhouette != null)
            {
                _overlay?.SetSilhouette(cell, pal.Id, MarkerSilhouettes.KindMarker, pal.Color);
            }
            else
            {
                _overlay?.Remove(cell);
            }

            _overlay?.OnDesignChanged(cell, IsSolidCell); // a block beside a door re-fits that door
        }

        private bool InBounds(Vector3i c) => EditorSceneKit.InRoom(c, MaxW, MaxH, MaxL);

        private bool InBounds(int x, int y, int z) => InBounds(new Vector3i(x, y, z));

        private bool Occupied(int x, int y, int z) => _design.ContainsKey(new Vector3i(x, y, z));

        /// <summary>A cell that blocks a door or carries a ladder: an authored BLOCK (markers are points, not voxels).</summary>
        private bool IsSolidCell(int x, int y, int z) => _design.TryGetValue(new Vector3i(x, y, z), out var d) && d.Kind == "block";

        private void RemoveCell(Vector3i cell)
        {
            _design.Remove(cell);
            _view.Remove(cell);
            _overlay?.Remove(cell);
            _overlay?.OnDesignChanged(cell, IsSolidCell);
        }

        /// <summary>A placement hint in the status line while the ghost is red for a RULE (a door without a wall, a bed
        /// without room) — set once per change, cleared when the ghost turns valid again.</summary>
        private void Hint(string key)
        {
            if (key == _hintShown)
            {
                return;
            }

            _hintShown = key;
            SetStatus(key != null ? L(key) : string.Empty);
        }

        /// <summary>The tile the form icons are drawn on: the selected block's, else stone.</summary>
        private ushort BrushTile()
            => EditorSceneKit.BrushTile(Shell, _palette != null && _selected >= 0 && _selected < _palette.Length ? _palette[_selected] : default);

        private RawImage FormIcon(Transform parent, float x, float y, float size)
        {
            var go = new GameObject("FormIcon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            EditorFormPicker.PaintIcon(raw, _atlas, BrushTile(), _brushShape);
            return raw;
        }

        private void OpenFormPicker()
        {
            _formPicker?.Close();
            _formPicker = EditorFormPicker.Show(Shell, _canvas.transform, _atlas, BrushTile(), _brushShape, shape =>
            {
                _brushShape = shape;
                if (_shapeLabel != null)
                {
                    _shapeLabel.text = FormName(shape);
                }

                EditorFormPicker.PaintIcon(_formIcon, _atlas, BrushTile(), shape);
            }, () => _formPicker = null);
        }

        // ----------------------------- export (the JSON shapes live in StructureEditor.Submit.cs) -----------------------------

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

            if (ExportElsewhere(key))
            {
                return; // #3523: a generator entry, a landmark (#3492) — or a preview, which is never the design
            }

            if (!ValidateForExport() || RefuseUnreadTokens())
            {
                return; // #1877: a broken port or a leaking module never leaves the editor; #3523: nor a token a module cannot resolve
            }

            var (meta, layout) = BuildExport(key);
            try
            {
                // 1) Export bundle (the "ship into the game" path via tools/merge_structure.py).
                string dir = Path.Combine(AppPaths.Root, meta.kind + "_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "structure.json"), JsonUtility.ToJson(meta, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));

                // 2) Data-shaped template written straight to the user-content folder the local server
                //    reads — so this structure can appear in your NEXT new world without any merge/rebuild.
                var tpl = new TemplateJson
                {
                    key = key, name = _name, tier = meta.tier, kind = meta.kind, pack = meta.pack, weight = meta.weight,
                    planetTypes = meta.planetTypes, role = meta.role, kit = meta.kit, function = meta.function, style = meta.style, underground = meta.underground,
                    protection = meta.protection, // generation 39
                    width = layout.width, height = layout.height, length = layout.length, cells = layout.cells,
                };
                string userDir = Path.Combine(AppPaths.Root, "usercontent", meta.kind + "_templates");
                Directory.CreateDirectory(userDir);
                File.WriteAllText(Path.Combine(userDir, key + ".json"), JsonUtility.ToJson(tpl, true));

                SetStatus(string.Format(L("ui.ed.saved_structure"), key, _design.Count, meta.pack) + SaveWarnings()); // #3523
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Export failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint")); // a friendly line; the exception goes to the log (#3263)
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
                    if (t.IsSource)
                    {
                        continue; // a generator entry has no cells (#3523: AddEntryItems lists it)
                    }

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
                        Load = () => ApplyTemplate(t.key, t.name, t.tier, t.pack, t.weight, t.planetTypes, t.cells, copy: false, role: t.role, kit: t.kit, function: t.function, style: t.style, underground: t.underground,
                            protection: t.protection),
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

            AddLandmarkItems(builtIn, user); // #3492
            AddEntryItems(builtIn, user); // #3523
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
        // #2260 export validation: device settings travel only on blocks that carry them, the mode inside the device's
        // range (a switch: OFF / ON), no separators the template's marker data would split on.
        private int DeviceMode(CellData d)
        {
            var kind = DeviceKindOf(d);
            if (!CarriesDeviceSettings(kind))
            {
                return 0;
            }

            int modes = kind == CrystalDeviceKind.Switch ? 2 : CrystalNetRules.ModeCount(kind);
            return modes > 0 ? Mathf.Clamp(d.Mode, 0, modes - 1) : 0;
        }

        private string DeviceConfig(CellData d)
            => CarriesDeviceSettings(DeviceKindOf(d)) ? (d.Config ?? string.Empty).Replace("|", string.Empty) : string.Empty;

        private string DeviceLabel(CellData d)
            => CarriesDeviceSettings(DeviceKindOf(d)) ? (d.Label ?? string.Empty).Replace("|", string.Empty) : string.Empty;

        /// <summary>#2260: a generator's <c>crystal</c> markers carry a pre-built device's settings — they go onto the block
        /// cell they mark, never into the marker palette.</summary>
        private static bool TakeDeviceMarker(string type, string data, Vector3i at, Dictionary<Vector3i, (int Mode, string Config, string Label)> devices)
        {
            if (type != TemplateDevices.Marker)
            {
                return false;
            }

            devices[at] = TemplateDevices.Decode(data ?? string.Empty);
            return true;
        }

        private EditorPaletteKit.Entry FindPalette(string id, string kind) => EditorPaletteKit.Find(_palette, id, kind);

        /// <summary>Replaces the build with <paramref name="cells"/>. Cells whose id the palette lacks, or that
        /// fall outside the room, are counted and named for the status line instead of vanishing (#1398).</summary>
        private int ApplyCells(IEnumerable<CellJson> cells, List<string> skippedIds)
        {
            _view.Clear();
            _overlay?.Clear();
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

                // A door authored as a BLOCK (older templates, #1982) heals into the door marker: the block palette no
                // longer carries doors, so the id resolves to the marker and the cell follows it.
                string kind = string.IsNullOrEmpty(c.kind) ? pal.Kind : c.kind;
                if (pal.Kind == "marker" && DoorBlocks.IsDoorMarker(pal.Id))
                {
                    kind = "marker";
                }

                var data = new CellData
                {
                    Id = c.id,
                    Kind = kind,
                    Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty, Keep = c.keep,
                    Mode = c.mode, Config = c.config ?? string.Empty, Label = c.label ?? string.Empty, // #2260
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
                cells.Add(new CellJson { x = c.X, y = c.Y, z = c.Z, kind = c.Kind, id = c.Id, tint = c.Tint, glow = c.Glow, shape = c.Shape, port = c.Port ?? string.Empty,
                    mode = c.Mode, config = c.Config ?? string.Empty, label = c.Label ?? string.Empty, keep = c.Keep });
            }

            ApplyTemplate(t.Key, t.Name, t.Tier, t.PackOrDefault, t.Weight, t.PlanetTypes, cells, copy: true, role: t.Role, kit: t.Kit, function: t.Function, style: t.Style, underground: t.Underground,
                protection: t.Protection);
        }

        /// <summary>Common load path for built-in and user templates: cells + form fields, then the status
        /// (skipped cells, copy hint) and a UI rebuild.</summary>
        private void ApplyTemplate(string key, string name, string tier, string pack, int weight, List<string> planetTypes, IEnumerable<CellJson> cells, bool copy, string role = "",
            string kit = "", string function = "", string style = "", bool underground = false, string protection = "")
        {
            var skippedIds = new List<string>();
            int skipped = ApplyCells(cells, skippedIds);
            _underground = underground; // #3333
            _protection = LandmarkRules.ProtectionOf(protection); // generation 39: the template's own (a kit module's is ignored)
            ResetLandmarkExtras(); // ruin zones, seat … (LoadLandmark sets them again)
            _landmarkMode = false; // #3492: LoadLandmark switches it back on
            _entryMode = _entrySourceInvalid = false; _entryExtras = null; // #3523: LoadEntry sets them again
            DropPreview(); // a load replaced the design a preview kept aside

            _key = copy ? key + "_2" : key;
            _basedOn = copy ? key : string.Empty; // a submission names the shipped template it started from (#3262)
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
                    style: meta?.style ?? string.Empty,
                    underground: meta != null && meta.underground,
                    protection: meta?.protection ?? string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Load failed: {e}");
                SetStatus(L("ui.ed.load_failed_hint")); // a friendly line; the exception goes to the log (#3263)
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
                t.Cells.Add(new TemplateCell { X = kv.Key.X, Y = kv.Key.Y, Z = kv.Key.Z, Kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, Id = d.Id, Tint = d.Tint, Glow = d.Glow, Shape = d.Shape, Port = d.Port ?? string.Empty,
                    Mode = DeviceMode(d), Config = DeviceConfig(d), Label = DeviceLabel(d) });
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
                            t.Cells.Add(new TemplateCell { X = c.x, Y = c.y, Z = c.z, Kind = c.kind, Id = c.id, Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty,
                                Mode = c.mode, Config = c.config ?? string.Empty, Label = c.label ?? string.Empty });
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
            var devices = new Dictionary<Vector3i, (int Mode, string Config, string Label)>(); // #2260
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
                        if (!TakeDeviceMarker(m.Type, m.Data, m.LocalPos, devices)) // #2260: a device's settings go onto its block
                        {
                            cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                        }
                    }
                }

                if (settlementMarkers != null)
                {
                    foreach (var m in settlementMarkers)
                    {
                        if (!TakeDeviceMarker(m.Type, m.Data, m.LocalPos, devices)) // #2260: a device's settings go onto its block
                        {
                            cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type });
                        }
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
                            var dv = devices.TryGetValue(new Vector3i(x, y, z), out var found) ? found : (0, string.Empty, string.Empty);
                            cells.Add(new CellJson { x = x, y = y, z = z, kind = "block", id = def.Key, tint = tint, glow = glow, shape = shapeAt?.Invoke(x, y, z) ?? 0,
                                mode = dv.Item1, config = dv.Item2, label = dv.Item3 });
                        }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Assembling the kit failed: {e}");
                SetStatus(L("ui.ed.assemble_failed_hint")); // a friendly line; the exception goes to the log (#3263)
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
            _formPicker?.Close();
            _formPicker = null;
            _sourcePicker?.Close(); // #3523
            _sourcePicker = null;
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            BuildUi();
            SetStatus(_status);
        }

        private static string Slug(string s) => EditorSceneKit.Slug(s);
    }
}
