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

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Standalone ship-type editor (M27+ tooling; see docs/developer/SHIP_TYPE_EDITOR.md). An empty build
    /// room you fly through (hold RMB to look, WASD/QE to move) and place blocks into: hull, viewports,
    /// all ship stations, a hatch, lights and an engine. A side card sets the design's name, stats and
    /// blueprint/craft costs. Save writes a ship-type bundle (ship.json + layout.json) that a developer
    /// folds into the game with tools/merge_ship.py; Submit (#3262, <c>ShipEditor.Submit.cs</c>) sends the same
    /// bundle to the developers for review. Self-contained on the client (no server).
    /// <para>
    /// On the <see cref="EditorShell"/> (#3691, <c>ShipEditor.Ui.cs</c>): Back, the title with the ship's name, Undo / Redo,
    /// Load, New, Submit and the one primary Save sit in the shell's header; the palette card on the left has a
    /// <b>Place | Remove</b> switch (removing no longer needs the middle mouse button — Delete or X remove too), the ship card
    /// on the right holds name, description, values and the selected part; the 3D view between them stays free. Every
    /// placed or removed block is one undo step, unsaved work is kept as a draft (<c>ShipEditor.Document.cs</c>).
    /// </para>
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
    /// A uses the tool, X removes, Y turns the shape brush, and a centre reticle replaces the mouse pointer as
    /// the picking ray's origin. B leaves the viewport (through <see cref="EditorShell.HandleCancel"/> →
    /// <see cref="CloseTopLayer"/>); it never leaves the editor — only Back does (#3690).
    /// </para>
    /// </summary>
    public sealed partial class ShipEditor : MonoBehaviour, IEditorDocument
    {
        public AppShell Shell;

        /// <summary>Where the drafts go; null = the player's drafts folder. The EditMode tests point it at a temp folder.</summary>
        public string DraftRoot;

        private const int MaxW = ShipLayout.EditorRoomWidth, MaxH = ShipLayout.EditorRoomHeight, MaxL = ShipLayout.EditorRoomLength;
        private const float RaycastDist = 1200f;

        /// <summary>Room cell that holds the layout's interior origin (0,0,0); export subtracts it, load adds it.</summary>
        private static readonly Vector3i Origin = new(ShipLayout.EditorOriginX, 0, ShipLayout.EditorOriginZ);

        private Camera _cam;
        private GameObject _floor;
        private float _yaw, _pitch;

        /// <summary>One authored cell: palette id + kind plus the in-game per-voxel modifiers (dye/glow
        /// colour 0xRRGGBB, packed shape+orientation). Elements/stations carry no modifiers.</summary>
        private struct CellData
        {
            public string Id;
            public string Kind;
            public int Tint, Glow, Shape;

            public bool SameAs(CellData o) => Id == o.Id && Kind == o.Kind && Tint == o.Tint && Glow == o.Glow && Shape == o.Shape;
        }

        private readonly Dictionary<Vector3i, CellData> _design = new();   // cell -> authored cell (export source)
        private EditorVoxelChunkView _view;                                // chunked combined-mesh renderer

        private BlockTextureAtlas _atlas;                                  // editor-local atlas for palette icons + cell colours
        private EditorPaletteKit.Entry[] _palette;
        private int _selected;

        // Brush: dye/glow colour + shape + orientation applied to newly placed BLOCK cells (elements +
        // stations ignore them), mirroring the in-game dye + shape + place-orientation. 0 = none / cube.
        private int _brushTint, _brushGlow, _brushOrient;
        private int _brushShape = EditorPlacementRules.AutoForm; // -1 = the block's own form (#1975); 0 = a cube on purpose
        private string _search = string.Empty;

        /// <summary>The form brush's name: "Automatic" (the block's own form, #1975) or the built-in form's name.</summary>
        private string FormName(int shape)
            => shape < 0 ? L("ui.ed.form_auto_short") : L(BuiltInForms.LocKeyOf(shape) ?? "ui.shape.cube"); // "Auto": a short value (#3263)

        // --- editable metadata (defaults in ResetMeta) ---
        private string _key = string.Empty;
        private string _shipName = string.Empty;
        private string _desc = string.Empty;
        private string _requiredBlueprint = string.Empty;
        private float _hull, _shield, _flightSpeed, _handling;
        private int _cargo;
        private readonly List<CostRow> _craftCost = new();

        /// <summary>False while the key follows the name (a new design): the key now sits under "Advanced", and every new ship
        /// saved as "my_ship" would overwrite the last one. A typed or loaded key stays.</summary>
        private bool _keyEdited;

        // Interior (cabin) size the server stamps as 0..W-1 × 0..H × 0..L-1 (#1396) — explicit, never derived
        // from the placed cells, because exterior greebles sit outside it. Defaults match the starter box.
        private int _intW, _intL, _intH;
        private EditorInteriorFrame _frame;

        // The modules a loaded built-in ship starts with (reactor, life support, weapons …) — carried through
        // to ship.json so tools/merge_ship.py keeps them instead of deriving only from station tiles (#1399).
        private List<string> _startModules = new();

        private bool _mouseOverUi;

        private sealed class CostRow { public string Item = string.Empty; public int Count = 1; }

        private void Start()
        {
            // Editor-local block atlas: gives the palette (and the placed cells) the real material look.
            _atlas = Shell != null && Shell.Content != null ? BlockTextureAtlas.Acquire(Shell.Content) : null; // shared per process (#1523)
            _palette = BuildPalette();
            ResetMeta();

            _cam = EditorSceneKit.CreateEditorCamera(transform, 800f);
            _yaw = 0f;

            _view = new EditorVoxelChunkView(transform);
            _view.SetAtlas(_atlas); // real block tiles on placed cells (#1400), each face its own (#2744)
            _ghost = new EditorPlacementGhost(transform);
            _overlay = new EditorPropOverlay(transform); // placed doors, the hatch frame, station decor (#1975)
            _ghostMeshes = new EditorGhostMeshes();
            BuildRoom();
            _frame = new EditorInteriorFrame(transform);
            RebuildFrame();
            FrameCamera(); // opening view (#1390): the interior frame when nothing is placed yet
            BuildUi();
        }

        /// <summary>The metadata of a new design: the localized default name and description (#3263), the starter box's
        /// interior, stats and craft cost, a key that follows the name.</summary>
        private void ResetMeta()
        {
            _key = "my_ship";
            _keyEdited = false;
            _shipName = L("ui.ed.default.ship");
            _desc = L("ui.ed.default.ship_desc");
            _requiredBlueprint = string.Empty;
            _hull = 100f;
            _shield = 20f;
            _flightSpeed = 1f;
            _handling = 1f;
            _cargo = 48;
            _craftCost.Clear();
            _craftCost.Add(new CostRow { Item = "iron_plate", Count = 20 });
            _intW = 5;
            _intL = 7;
            _intH = 4;
            _startModules = new List<string>();
            _basedOn = string.Empty;
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

        /// <summary>Resolves the cell a placement would land in: the floor column, or the empty cell just
        /// outside the hit face (the chunk mesh is authored in world coords, so the hit point + normal locate
        /// the cell directly — no per-cell GameObject to read a transform from).</summary>
        private bool TryGetTargetCell(out Vector3i cell) => TryGetTargetCell(out cell, out _);

        /// <summary>… and the face of the target cell the click came through (the hit normal; -1 for none): what a
        /// ladder hugs (#1975). Picks through the pad reticle or the mouse (<see cref="PickPoint"/>).</summary>
        private bool TryGetTargetCell(out Vector3i cell, out int hitFace)
            => EditorSceneKit.PickTargetCell(_cam, PickPoint(), RaycastDist, _floor, out cell, out hitFace);

        /// <summary>Resolves the actual occupied cell under the cursor (for removal); false if the ray misses
        /// every block or hits only the floor.</summary>
        private bool TryGetHitCell(out Vector3i cell)
            => EditorSceneKit.PickHitCell(_cam, PickPoint(), RaycastDist, _floor, out cell);

        private EditorPlacementGhost _ghost;
        private EditorPropOverlay _overlay;
        private EditorGhostMeshes _ghostMeshes;
        private string _hintShown;

        /// <summary>Shows where a click would land and WHAT it would leave there (#1975): the block's form (a bed as
        /// head + foot), the door the server would hang (axis + width read from the design), the hatch frame, a
        /// station's block with its decor — green when the placement is valid, red when the cell is occupied / out
        /// of bounds, or a door has no room above it (the status line says which). With the Remove tool (#3691) a red
        /// cube marks the block a click takes away.</summary>
        private void UpdateGhost(bool hidden)
        {
            if (!hidden && Tool == EditorBuildTool.Remove)
            {
                bool hit = TryGetHitCell(out var target) && _design.ContainsKey(target);
                _ghost?.Update(hit, target, false);
                Hint(null);
                return;
            }

            if (hidden || _palette == null || _selected < 0 || _selected >= _palette.Length || !TryGetTargetCell(out var cell, out int hitFace))
            {
                _ghost?.Update(false, default, false);
                Hint(null);
                return;
            }

            var pal = _palette[_selected];
            bool free = InBounds(cell) && !_design.ContainsKey(cell);
            if (pal.Kind == "element" && DoorBlocks.IsDoorMarker(pal.Id))
            {
                var fit = DoorProbe.Measure(IsSolidCell, cell.X, cell.Y, cell.Z, DoorAxisFor(cell));
                string why = null;
                bool valid = free && DoorRoomAbove(cell, out why);
                _ghost?.UpdateMesh(true, new Vector3(fit.CentreX(cell.X), cell.Y, fit.CentreZ(cell.Z)), valid,
                    _ghostMeshes.ForDoor(DoorBlocks.KindForMarker(pal.Id), fit.Width, fit.AxisX));
                Hint(free ? why : null);
                return;
            }

            if (pal.Kind != "block")
            {
                // The hatch as a frame, a station as its block + decor; a null mesh keeps the cube.
                _ghost?.UpdateMesh(true, new Vector3(cell.X, cell.Y, cell.Z), free, _ghostMeshes.ForSilhouette(pal.Id, pal.Kind));
                Hint(null);
                return;
            }

            UpdateBlockGhost(pal, cell, hitFace, free);
        }

        /// <summary>A block's ghost: the form it will take (and, for a bed, its foot half); a plain cube keeps the cube ghost.</summary>
        private void UpdateBlockGhost(EditorPaletteKit.Entry pal, Vector3i cell, int hitFace, bool free)
        {
            if (free)
            {
                if (!EditorPlacementRules.TryPlaceBlock(pal.Id, _brushShape, _brushOrient, cell.X, cell.Y, cell.Z, hitFace,
                        Occupied, InBounds, IsSolidCell, out var writes, out string refusal))
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

        /// <summary>A click with the current tool (#3691): place the selected entry, or remove what the click hits.</summary>
        private void UseTool()
        {
            if (Tool == EditorBuildTool.Remove)
            {
                TryRemove();
            }
            else
            {
                TryPlace();
            }
        }

        private void TryPlace()
        {
            if (TryGetTargetCell(out var cell, out int hitFace))
            {
                PlaceAt(cell, hitFace);
            }
        }

        /// <summary>Places the selected palette entry at <paramref name="cell"/> (<paramref name="hitFace"/>: the face the click
        /// came through, -1 for none) as ONE undo step — a bed's two halves together. False when nothing was placed (an
        /// occupied cell, a refused door or bed — the status bar says why).</summary>
        private bool PlaceAt(Vector3i cell, int hitFace)
        {
            if (_palette == null || _selected < 0 || _selected >= _palette.Length || !InBounds(cell) || _design.ContainsKey(cell))
            {
                return false;
            }

            var pal = _palette[_selected];
            // A door with no room for its 2.8 blocks above is never what was meant (#1975). The jambs are not checked
            // here: the server completes a ship's hull around the layout, so a door on the layout's edge is fine.
            if (pal.Kind == "element" && DoorBlocks.IsDoorMarker(pal.Id) && !DoorRoomAbove(cell, out string why))
            {
                Status(L(why), EditorStatusSeverity.Warning);
                return false;
            }

            if (pal.Kind != "block")
            {
                var before = CellSnapshot();
                PlaceCell(cell, pal);
                Edited(before);
                return true;
            }

            // The server's own placement rules (#1975): the block's default form unless the brush chose one, a
            // bed as head + foot (refused where the foot would not fit), the ladder against its wall.
            if (!EditorPlacementRules.TryPlaceBlock(pal.Id, _brushShape, _brushOrient, cell.X, cell.Y, cell.Z, hitFace,
                    Occupied, InBounds, IsSolidCell, out var writes, out string refusal))
            {
                Status(L(refusal), EditorStatusSeverity.Warning);
                return false;
            }

            var snapshot = CellSnapshot();
            foreach (var w in writes)
            {
                PlaceCellData(new Vector3i(w.X, w.Y, w.Z), pal,
                    new CellData { Id = pal.Id, Kind = pal.Kind, Tint = _brushTint, Glow = _brushGlow, Shape = w.Shape });
            }

            Edited(snapshot);
            return true;
        }

        private void TryRemove()
        {
            if (TryGetHitCell(out var cell))
            {
                RemoveAt(cell);
            }
        }

        /// <summary>Removes the cell at <paramref name="cell"/> — a bed with its other half (#1846) — as ONE undo step.
        /// False when nothing is there.</summary>
        private bool RemoveAt(Vector3i cell)
        {
            if (!_design.TryGetValue(cell, out var d))
            {
                return false;
            }

            var before = CellSnapshot();
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

            Edited(before);
            return true;
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
            var tile = TileOf(pal, out bool textured, out ushort blockId);
            Color baseCol = data.Tint != 0
                ? EditorVoxelPreview.RgbToColor(data.Tint)
                : (data.Glow != 0 ? EditorVoxelPreview.RgbToColor(data.Glow) : (textured ? Color.white : pal.Color));

            bool door = pal.Kind == "element" && DoorBlocks.IsDoorMarker(pal.Id);
            var silhouette = !door && pal.Kind != "block" ? MarkerSilhouettes.For(pal.Id, pal.Kind) : null;
            _design[cell] = data;
            _view.Set(cell, new EditorVoxelChunkView.Cell
            {
                Color = baseCol,
                Glow = data.Glow != 0,
                Shape = data.Shape,
                Marker = false, // the ship editor has no markers (elements + stations are solid anchors)
                Overlay = door || (silhouette != null && !silhouette.Value.KeepsCube), // the overlay draws it (#1975)
                Textured = textured,
                Uv = tile,
                BlockId = blockId, // #2744: each face its own tile, a front facing the open side
            });

            if (door)
            {
                _overlay?.SetDoor(cell, DoorBlocks.KindForMarker(pal.Id), IsSolidCell, DoorAxisFor(cell));
            }
            else if (silhouette != null)
            {
                _overlay?.SetSilhouette(cell, pal.Id, pal.Kind, pal.Color);
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

        /// <summary>A cell that blocks a door or carries a ladder: a block, a station tile, or an element that is a block
        /// in the game (a light, the engine, glass) — not the hatch and not a door, which are openings.</summary>
        private bool IsSolidCell(int x, int y, int z)
            => _design.TryGetValue(new Vector3i(x, y, z), out var d)
               && (d.Kind != "element" || (d.Id != "hatch" && !DoorBlocks.IsDoorMarker(d.Id)));

        /// <summary>The wall axis the server pins for a door on the layout's rear wall (local Z = 0): the hatch is a
        /// wide gap in a wall along X, where a ±1 probe is ambiguous. Every other door lets the probe decide.</summary>
        private bool? DoorAxisFor(Vector3i cell) => cell.Z - Origin.Z == 0 ? true : (bool?)null;

        /// <summary>A door is 2.8 tall: the two cells above its floor cell must be free and inside the room.</summary>
        private bool DoorRoomAbove(Vector3i cell, out string refusalKey)
        {
            refusalKey = null;
            for (int up = 1; up <= 2; up++)
            {
                if (!InBounds(cell.X, cell.Y + up, cell.Z) || Occupied(cell.X, cell.Y + up, cell.Z))
                {
                    refusalKey = EditorPlacementRules.DoorNeedsWall;
                    return false;
                }
            }

            return true;
        }

        private void RemoveCell(Vector3i cell)
        {
            _design.Remove(cell);
            _view.Remove(cell);
            _overlay?.Remove(cell);
            _overlay?.OnDesignChanged(cell, IsSolidCell);
        }

        /// <summary>A placement hint in the status bar while the ghost is red for a RULE (a door without room above, a
        /// bed without room) — set once per change. When the ghost turns valid again only the hint itself goes: a
        /// message the hint did not write ("Saved.") stays until the next one (the shell's rule).</summary>
        private void Hint(string key)
        {
            if (key == _hintShown)
            {
                return;
            }

            string old = _hintShown;
            _hintShown = key;
            if (key != null)
            {
                Status(L(key), EditorStatusSeverity.Warning);
            }
            else if (old != null && _shell != null && _shell.StatusText.text == ShownAs(L(old)))
            {
                _shell.ClearStatus();
            }
        }

        /// <summary>The tile the form icons are drawn on: the selected block's, else stone.</summary>
        private ushort BrushTile()
            => EditorSceneKit.BrushTile(Shell, _palette != null && _selected >= 0 && _selected < _palette.Length ? _palette[_selected] : default);

        /// <summary>The atlas tile of a palette entry that is a real content block (stations never are;
        /// elements only when their id is a block key), and the block's id for its per-face tiles (#2744);
        /// <paramref name="textured"/> = false and id 0 otherwise.</summary>
        private Rect TileOf(EditorPaletteKit.Entry pal, out bool textured, out ushort blockId)
        {
            textured = false;
            blockId = 0;
            if (_atlas == null || pal.Kind == "station" || Shell?.Content?.GetBlock(pal.Id) is not { } def)
            {
                return default;
            }

            textured = true;
            blockId = def.NumericId.Value;
            return _atlas.TileUv(def.NumericId.Value);
        }

        private void OnDestroy()
        {
            _view?.Dispose();
            _ghost?.Dispose();
            _overlay?.Dispose();
            _ghostMeshes?.Dispose();
            _formPicker?.Close();
            _formPicker = null;
            _loadPicker?.Close();
            _loadPicker = null;
            _frame?.Dispose();
            _atlas?.Release(); // palette sprites reference this texture; the editor holds a reference (#423 lesson, shared since #1523)
            _atlas = null;
            if (_canvas != null)
            {
                // The shell on it writes a draft of unsaved work as it goes (EditorShell.FlushDraftOnDestroy).
                EditorSceneKit.Discard(_canvas.gameObject);
            }
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

        /// <summary>The key a save writes under: the typed key as a slug — or, when that is empty, the name as one.</summary>
        private string SaveKey()
        {
            string key = Slug(_key);
            return string.IsNullOrEmpty(key) ? Slug(_shipName) : key;
        }

        /// <summary>Save (the shell's primary button, Ctrl+S): writes the export bundle and the one-file user ship (#3520).
        /// True when it was written — the shell then removes the draft.</summary>
        private bool Export()
        {
            string key = SaveKey();
            if (string.IsNullOrEmpty(key))
            {
                Status(L("ui.ed.need_key"), EditorStatusSeverity.Warning);
                ShowAdvanced(true); // the key field lives there
                return false;
            }

            var (ship, layout) = BuildExport(key);
            try
            {
                string dir = Path.Combine(AppPaths.Root, "ship_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ship.json"), JsonUtility.ToJson(ship, true));
                File.WriteAllText(Path.Combine(dir, "layout.json"), JsonUtility.ToJson(layout, true));
                string userKey = WriteUserShip(key, ship, layout); // #3520: the one-file copy the local server reads (ShipEditor.UserContent.cs)
                // The third argument is only for a translation that still names the folder; the status bar shows no path.
                EditorPlaytest.Saved(EditorPlaytest.Ship);
                Status(string.Format(L("ui.ed.saved_ship"), key, _design.Count, key) + UserShipRenamedNote(key, userKey),
                    EditorStatusSeverity.Success);
                MarkSaved();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShipEditor] Export failed: {e}");
                Status(L("ui.ed.export_failed_hint"), EditorStatusSeverity.Error); // a friendly line; the exception goes to the log (#3263)
                return false;
            }
        }

        private static string Slug(string s) => EditorSceneKit.Slug(s);
    }
}
