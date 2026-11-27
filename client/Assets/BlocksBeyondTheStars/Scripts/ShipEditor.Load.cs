// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Loading a ship into the editor (#1394, #3520): the LOAD picker (the shipped ships as starting points, the user ships
    /// of <c>usercontent/ship_types/</c>, the player's own exports) and the loaders behind its rows. On the editor shell the
    /// picker asks only over UNSAVED work, with the shell's question (<see cref="EditorShell.ConfirmDiscard"/>): the draft
    /// does not survive a load (the loaded ship is clean, so the shell removes the old draft) and a load starts a new undo
    /// history — without the question that work would be gone for good. A load of a clean build asks nothing.
    /// </summary>
    public sealed partial class ShipEditor
    {
        private EditorLoadPicker _loadPicker;

        /// <summary>The LOAD dialog (#1394): the shipped ships (every <see cref="ShipDefinition"/> with a
        /// layout, straight from the loaded content) as starting points, the user ships of <c>usercontent/ship_types/</c> (#3520),
        /// then the user's own exports under <c>ship_exports/</c>. The starter ship is a code-built box without a layout and is not listed.</summary>
        private void OpenLoadPicker()
        {
            _loadPicker?.Close();
            _loadPicker = EditorLoadPicker.Show(Shell, _shell, new[] { BuiltInShipsSection(), UserShipsSection(), ExportsSection() },
                () => _loadPicker = null);
        }

        private EditorLoadPicker.Section BuiltInShipsSection()
        {
            var builtIn = new EditorLoadPicker.Section { Title = L("ui.ed.sec_builtin_ships") };
            if (Shell?.Content == null)
            {
                return builtIn;
            }

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

            return builtIn;
        }

        private EditorLoadPicker.Section ExportsSection()
        {
            var mine = new EditorLoadPicker.Section { Title = L("ui.ed.sec_exports") };
            string root = Path.Combine(AppPaths.Root, "ship_exports");
            if (!Directory.Exists(root))
            {
                return mine;
            }

            foreach (var dir in Directory.GetDirectories(root))
            {
                if (!File.Exists(Path.Combine(dir, "layout.json")))
                {
                    continue;
                }

                string k = Path.GetFileName(dir);
                mine.Items.Add(new EditorLoadPicker.Item { Label = k, Load = () => LoadDesign(k) });
            }

            return mine;
        }

        // Unattended editor screenshots (-captureEditors, ScreenshotDirector.Editors.cs, #3687): a shipped ship on the table and
        // the two pickers, without clicks. Nothing is saved. Keep these three working when the editor is restyled.
        internal void CaptureLoadBuiltIn(ShipDefinition def) => LoadBuiltIn(def);

        internal EditorLoadPicker CaptureOpenLoadPicker()
        {
            OpenLoadPicker();
            return _loadPicker;
        }

        internal EditorFormPicker CaptureOpenFormPicker()
        {
            OpenFormPicker();
            return _formPicker;
        }

        /// <summary>Finds the palette entry for a loaded cell — by id AND kind first (a block and a marker may
        /// share an id), then by id alone; <c>Id == null</c> when the palette has no such entry.</summary>
        private EditorPaletteKit.Entry FindPalette(string id, string kind) => EditorPaletteKit.Find(_palette, id, kind);

        /// <summary>Replaces the build with <paramref name="cells"/> (layout coordinates; the room origin is
        /// added). Cells whose id the palette lacks, or that fall outside the room, are counted and named
        /// so the status line can say what was lost instead of dropping them silently (#1398).</summary>
        private int ApplyCells(IEnumerable<ExportCellJson> cells, List<string> skippedIds)
        {
            _view.Clear();
            _overlay?.Clear();
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

        /// <summary>The interior frame of a loaded layout (kept when the layout names none).</summary>
        private void ApplyInterior(ExportLayoutJson layout)
        {
            if (layout != null && layout.width > 0 && layout.length > 0 && layout.height > 0)
            {
                _intW = layout.width;
                _intL = layout.length;
                _intH = layout.height;
            }
        }

        /// <summary>The texts, stats, modules and cost of a saved ship (an export's <c>ship.json</c> or a draft). An empty
        /// cost list keeps the current cost when <paramref name="keepCostWhenEmpty"/> (an export without one).</summary>
        private void ApplyShipMeta(ExportShipJson ship, string fallbackKey, bool keepCostWhenEmpty)
        {
            _key = string.IsNullOrEmpty(ship.key) ? fallbackKey : ship.key;
            _keyEdited = true; // a loaded ship keeps its key: saving it again replaces it (the intended edit path)
            _shipName = string.IsNullOrEmpty(ship.name) ? _shipName : ship.name;
            _desc = string.IsNullOrEmpty(ship.description) ? _desc : ship.description;
            _requiredBlueprint = ship.requiredBlueprint ?? string.Empty;
            if (ship.baseHull > 0) _hull = ship.baseHull;
            _shield = ship.baseShield;
            if (ship.flightSpeed > 0) _flightSpeed = ship.flightSpeed;
            if (ship.handling > 0) _handling = ship.handling;
            if (ship.cargoSlots > 0) _cargo = ship.cargoSlots;
            _startModules = ship.startModules != null ? new List<string>(ship.startModules) : new List<string>();
            if (ship.craftCost == null || (ship.craftCost.Count == 0 && keepCostWhenEmpty))
            {
                return;
            }

            _craftCost.Clear();
            foreach (var c in ship.craftCost)
            {
                _craftCost.Add(new CostRow { Item = c.item ?? string.Empty, Count = c.count });
            }
        }

        /// <summary>Common tail of every load: frame, a fresh undo history (the loaded ship is the saved state), the status
        /// (with the skipped-cells note), the ship card.</summary>
        private void FinishLoad(string label, int skipped, List<string> skippedIds)
        {
            RebuildFrame();
            FrameCamera();
            MarkFresh();
            string loaded = string.Format(L("ui.ed.loaded"), label, _design.Count);
            if (skipped > 0)
            {
                Status(loaded + " " + string.Format(L("ui.ed.skipped"), skipped, string.Join(", ", skippedIds)), EditorStatusSeverity.Warning);
            }
            else
            {
                Status(loaded, EditorStatusSeverity.Info);
            }

            RebuildForm();
        }

        /// <summary>Loads a shipped ship type as a starting point (#1394): its layout cells plus the whole
        /// definition (localized name/description, stats, craft cost, blueprint, interior size, start
        /// modules). Saving under the same key replaces the ship at merge time — the intended edit path.</summary>
        private void LoadBuiltIn(ShipDefinition def)
        {
            EditorPlaytest.Loaded(EditorPlaytest.Ship);
            var layout = Shell?.Content?.GetShipLayout(def.Layout);
            if (layout == null)
            {
                Status(L("ui.ed.not_found"), EditorStatusSeverity.Error);
                return;
            }

            var cells = new List<ExportCellJson>(layout.Cells.Count);
            foreach (var c in layout.Cells)
            {
                cells.Add(new ExportCellJson { x = c.X, y = c.Y, z = c.Z, kind = c.Kind, id = c.Id, tint = c.Tint, glow = c.Glow, shape = c.Shape });
            }

            var skippedIds = new List<string>();
            int skipped = ApplyCells(cells, skippedIds);

            _key = _basedOn = def.Key; // a submission names the shipped ship it started from (#3262)
            _keyEdited = true;
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
            EditorPlaytest.Loaded(EditorPlaytest.Ship);
            _basedOn = string.Empty; // the player's own design, unless its ship.json names the shipped ship it started from (#4064)
            string dir = Path.Combine(AppPaths.Root, "ship_exports", key);
            string layoutPath = Path.Combine(dir, "layout.json");
            if (!File.Exists(layoutPath))
            {
                Status(L("ui.ed.not_found"), EditorStatusSeverity.Error);
                return;
            }

            try
            {
                var layout = JsonUtility.FromJson<ExportLayoutJson>(File.ReadAllText(layoutPath));
                var skippedIds = new List<string>();
                int skipped = ApplyCells(layout?.cells ?? new List<ExportCellJson>(), skippedIds);
                ApplyInterior(layout);
                string shipPath = Path.Combine(dir, "ship.json");
                if (File.Exists(shipPath) && JsonUtility.FromJson<ExportShipJson>(File.ReadAllText(shipPath)) is { } ship)
                {
                    ApplyShipMeta(ship, key, keepCostWhenEmpty: true);
                    _basedOn = SavedBase(ship.basedOn);
                }
                else
                {
                    _key = key;
                    _keyEdited = true;
                }

                FinishLoad(key, skipped, skippedIds);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShipEditor] Load failed: {e}");
                Status(L("ui.ed.load_failed_hint"), EditorStatusSeverity.Error); // a friendly line; the exception goes to the log (#3263)
            }
        }
    }
}
