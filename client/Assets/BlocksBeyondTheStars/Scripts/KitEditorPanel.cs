// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Data-shaped mirror of <see cref="StructureKit"/> for Unity's JsonUtility (#1877) — the same camelCase
    /// field names the server's loader reads, so a kit saved here is live in the next new world.</summary>
    [Serializable]
    public sealed class KitJson
    {
        public string key = string.Empty, name = string.Empty, kind = "station", tier = "medium", pack = "default", start = string.Empty;
        public int weight = 1, modulesMin, modulesMax, maxExtent;
        public int solarWings, antennas, domes; // #1920: station exterior detail (0 = none)
        public List<string> planetTypes = new List<string>();
        public List<KitEntryJson> entries = new List<KitEntryJson>();
        public int colsMin, colsMax, rowsMin, rowsMax, plotStride, building, storeys;
        public bool modulesOnly;
        public bool underground; // #3333: settlement / city kits only — composed solely by the underground stamper
        public int grid, districtSize, street, height;
        public List<string> roleMap = new List<string>();

        // #3600: the planned layouts (grown grid, grown) and the grown city
        public string layout = string.Empty, suburbKit = string.Empty;
        public List<string> forms = new List<string>();
        public int mainStreet, spacing, extent, minTerrainGeneration;
        public bool flat, grown;

        public static KitJson From(StructureKit k)
        {
            var j = new KitJson
            {
                key = k.Key, name = k.Name, kind = k.KindOrDefault, tier = k.Tier, pack = k.PackOrDefault, start = k.Start,
                weight = k.Weight, modulesMin = k.ModulesMin, modulesMax = k.ModulesMax, maxExtent = k.MaxExtent,
                solarWings = k.SolarWings, antennas = k.Antennas, domes = k.Domes,
                planetTypes = new List<string>(k.PlanetTypes),
                colsMin = k.ColsMin, colsMax = k.ColsMax, rowsMin = k.RowsMin, rowsMax = k.RowsMax, plotStride = k.PlotStride,
                building = k.Building, storeys = k.Storeys, modulesOnly = k.ModulesOnly, underground = k.Underground,
                grid = k.Grid, districtSize = k.DistrictSize, street = k.Street, height = k.Height, roleMap = new List<string>(k.RoleMap),
                layout = k.Layout ?? string.Empty, forms = new List<string>(k.Forms), mainStreet = k.MainStreet, spacing = k.Spacing,
                extent = k.Extent, flat = k.Flat, grown = k.Grown, suburbKit = k.SuburbKit ?? string.Empty,
                minTerrainGeneration = k.MinTerrainGeneration,
            };
            foreach (var e in k.Entries)
            {
                j.entries.Add(new KitEntryJson { module = e.Module, min = e.Min, max = e.Max, required = e.Required, weight = e.Weight, rotate = e.Rotate });
            }

            return j;
        }

        public StructureKit ToKit()
        {
            var k = new StructureKit
            {
                Key = key, Name = name, Kind = kind, Tier = tier, Pack = pack, Start = start ?? string.Empty,
                Weight = weight, ModulesMin = modulesMin, ModulesMax = modulesMax, MaxExtent = maxExtent,
                SolarWings = solarWings, Antennas = antennas, Domes = domes,
                PlanetTypes = new List<string>(planetTypes ?? new List<string>()),
                ColsMin = colsMin, ColsMax = colsMax, RowsMin = rowsMin, RowsMax = rowsMax, PlotStride = plotStride,
                Building = building, Storeys = storeys, ModulesOnly = modulesOnly, Underground = underground,
                Grid = grid, DistrictSize = districtSize, Street = street, Height = height, RoleMap = new List<string>(roleMap ?? new List<string>()),
                Layout = layout ?? string.Empty, Forms = new List<string>(forms ?? new List<string>()), MainStreet = mainStreet, Spacing = spacing,
                Extent = extent, Flat = flat, Grown = grown, SuburbKit = suburbKit ?? string.Empty, MinTerrainGeneration = minTerrainGeneration,
            };
            foreach (var e in entries ?? new List<KitEntryJson>())
            {
                k.Entries.Add(new KitEntry { Module = e.module, Min = e.min, Max = e.max, Required = e.required, Weight = e.weight, Rotate = e.rotate });
            }

            return k;
        }
    }

    [Serializable]
    public sealed class KitEntryJson
    {
        public string module = string.Empty;
        public int min, max = 1, weight = 1;
        public bool required, rotate = true;
    }

    /// <summary>
    /// The kit panel of the structure editor (#1877): the kits of this editor's kind (shipped ones as editable copies,
    /// the user's own from <c>usercontent/structure_kits</c>), their fields, the entries table (module, min, max,
    /// required, weight, rotate) and the grid of a village or city. Save writes the user kit file the local server
    /// reads and the export bundle <c>tools/merge_structure.py</c> folds into <c>data/structure_kits.json</c>.
    /// <para>
    /// On the menu template (#3692): centred on the screen (it opened at the top left corner, as did its module picker), Close
    /// as the ✕ of an in-game screen, Save kit the one primary button, Delete kit red and set apart — and it asks first
    /// (<see cref="EditorShell.ConfirmDelete"/>: "Delete “…” for good?"; it deleted the file at once). Messages are green when
    /// something was saved and red when it failed. Esc / pad B close the module picker, then the panel
    /// (<see cref="CloseTopLayer"/>).
    /// </para>
    /// </summary>
    internal sealed partial class KitEditorPanel
    {
        private const float W = 1100f, H = 900f;

        /// <summary>The panel's top-left corner: centred in the 1920 × 1080 reference.</summary>
        internal const float X = (1920f - W) / 2f, Y = (1080f - H) / 2f;

        /// <summary>The template's control height (Close, New, Use, the footer).</summary>
        internal const float ControlH = 44f;
        private readonly AppShell _shell;
        private readonly Transform _canvas;
        private readonly string _editorKind;       // "station" | "settlement" (settlement mode also edits city kits)
        private readonly Action<string> _useKit;   // the editor adopts this kit key
        private readonly Action _onClosed;
        private readonly Func<string, List<StructureTemplate>> _modules; // #1890: the module pool of a kind for the picker
        private readonly List<KitJson> _kits = new List<KitJson>();
        private GameObject _picker;
        private string _pickerFilter = string.Empty;
        private int _current = -1;
        private GameObject _overlay;
        private string _status = string.Empty;
        private bool _statusFailed;
        private readonly Action<string, Action> _confirmDelete; // the editor shell's "Delete … for good?"
        private UiKit.ConfirmDialog _ownConfirm;               // the fallback question without a shell

        private KitEditorPanel(AppShell shell, Transform canvas, string editorKind, Action<string> useKit, Action onClosed,
            Func<string, List<StructureTemplate>> modules, Action<string, Action> confirmDelete)
        {
            _shell = shell;
            _canvas = canvas;
            _editorKind = editorKind;
            _useKit = useKit;
            _onClosed = onClosed;
            _modules = modules;
            _confirmDelete = confirmDelete;
        }

        public static string UserKitsRoot => Path.Combine(AppPaths.Root, "usercontent", "structure_kits");

        /// <summary>Every kit the editor knows: the shipped ones of the kinds this mode edits, then the user's files.</summary>
        public static List<KitJson> KnownKits(AppShell shell, string editorKind)
        {
            var list = new List<KitJson>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (shell?.Content != null)
            {
                foreach (var k in shell.Content.StructureKits)
                {
                    if (KindMatches(editorKind, k.KindOrDefault) && seen.Add(k.Key))
                    {
                        list.Add(KitJson.From(k));
                    }
                }
            }

            if (Directory.Exists(UserKitsRoot))
            {
                var files = Directory.GetFiles(UserKitsRoot, "*.json");
                Array.Sort(files, string.CompareOrdinal);
                foreach (var file in files)
                {
                    try
                    {
                        var j = JsonUtility.FromJson<KitJson>(File.ReadAllText(file));
                        if (j == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(j.key))
                        {
                            j.key = Path.GetFileNameWithoutExtension(file);
                        }

                        if (!KindMatches(editorKind, j.kind))
                        {
                            continue;
                        }

                        int i = list.FindIndex(k => string.Equals(k.key, j.key, StringComparison.OrdinalIgnoreCase));
                        if (i >= 0)
                        {
                            list[i] = j; // a user file overrides the shipped copy of the same key
                        }
                        else
                        {
                            list.Add(j);
                        }
                    }
                    catch (Exception)
                    {
                        // one unreadable file must not hide the rest (the server skips it the same way)
                    }
                }
            }

            return list;
        }

        private static bool KindMatches(string editorKind, string kitKind)
            => editorKind == StructureKit.KindStation ? kitKind == StructureKit.KindStation : kitKind != StructureKit.KindStation;

        /// <param name="confirmDelete">Asks before a kit file is deleted (the editor shell's <see cref="EditorShell.ConfirmDelete"/>:
        /// the kit's name and the deletion); null asks with <see cref="UiKit.ConfirmDanger"/> on the canvas.</param>
        public static KitEditorPanel Show(AppShell shell, Transform canvas, string editorKind, string currentKey, Action<string> useKit, Action onClosed,
            Func<string, List<StructureTemplate>> modules = null, Action<string, Action> confirmDelete = null)
        {
            var panel = new KitEditorPanel(shell, canvas, editorKind, useKit, onClosed, modules, confirmDelete);
            panel._kits.AddRange(KnownKits(shell, editorKind));
            panel._current = panel._kits.FindIndex(k => k.key == currentKey);
            if (panel._current < 0 && panel._kits.Count > 0)
            {
                panel._current = 0;
            }

            panel.Build();
            return panel;
        }

        public void Close()
        {
            ClosePicker();
            _ownConfirm?.Close();
            _ownConfirm = null;
            if (_overlay != null)
            {
                Kill(_overlay);
                _overlay = null;
            }

            _onClosed?.Invoke();
        }

        /// <summary>Esc / pad B: the panel's own question, then the module picker, then the panel. Always closes something.</summary>
        public bool CloseTopLayer()
        {
            if (_ownConfirm != null && _ownConfirm.IsOpen)
            {
                _ownConfirm.Escape();
                return true;
            }

            if (_picker != null)
            {
                ClosePicker();
                return true;
            }

            Close();
            return true;
        }

        /// <summary>The panel is open (for the editor's tests).</summary>
        internal GameObject Overlay => _overlay;

        private static void Kill(GameObject go)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private string L(string key) => _shell?.L(key) ?? key;

        /// <summary><paramref name="key"/>'s text, or <paramref name="fallback"/> when the table has none.</summary>
        private string LOr(string key, string fallback)
        {
            string text = L(key);
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
        }

        /// <summary>A kit kind's name instead of its id (#3263).</summary>
        private string KindText(string kind) => LOr("ui.kit.kind." + kind, kind);

        /// <summary>A tier's name instead of its id — the metropolis without "(city district)" (#3263).</summary>
        private string TierText(string tier)
            => tier == StructureRoles.MetropolisTier ? LOr("ui.tier_short.metropolis", tier) : LOr("ui.tier." + tier, tier);

        /// <summary>A module's function (station) or plot role (settlement) by its name instead of its id (#3263).</summary>
        private string FunctionText(string function)
            => string.IsNullOrEmpty(function) ? string.Empty : LOr("ui.function." + function, LOr("ui.role_short." + function, function));

        /// <summary>The module picker (#1890): every module of the kit's kind (station modules for a station kit, settlement
        /// modules for a village, district modules for a city), filterable by key, function, style or tier.</summary>
        private void OpenPicker(string kitKind, Action<string> pick)
        {
            ClosePicker();
            var all = _modules?.Invoke(kitKind == StructureKit.KindStation ? StructureKit.KindStation : StructureKit.KindSettlement) ?? new List<StructureTemplate>();
            var pool = all.FindAll(t => kitKind == StructureKit.KindStation
                || (kitKind == StructureKit.KindCity) == (t.Tier == StructureRoles.MetropolisTier));
            pool.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            const float PW = 720f, PH = 760f;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas, (1920f - PW) / 2f, (1080f - PH) / 2f, PW, PH); // centred (#3692)
            _picker = overlay;
            UiKit.AddText(panel, 20f, 12f, 500f, ControlH, L("ui.kit.pick_module"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var list = UiKit.ScrollList(panel, 20f, 112f, PW - 40f, PH - 132f);
            UiKit.AddText(panel, 20f, 64f, 80f, 40f, L("ui.kit.filter"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            // The field stays while typing; only the rows below are rebuilt (rebuilding the field would drop its focus).
            UiKit.AddInput(panel, 100f, 64f, PW - 120f, 40f, _pickerFilter, v =>
            {
                _pickerFilter = v ?? string.Empty;
                FillPicker(list, pool, pick, PW - 60f);
            });
            FillPicker(list, pool, pick, PW - 60f);
            // Last: the pad starts in the filter, never on Close.
            UiKit.AddTierButton(panel, PW - 170f, 12f, 150f, ControlH, L("ui.kit.close"), ClosePicker, ButtonTier.Quiet, "btn_close");
        }

        private void FillPicker(Transform list, List<StructureTemplate> pool, Action<string> pick, float width)
        {
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(list.GetChild(i).gameObject);
            }

            string f = _pickerFilter.Trim().ToLowerInvariant();
            foreach (var t in pool)
            {
                string style = t.IsAlienStyle ? L("ui.style.alien") : string.Empty;
                string label = $"{t.Key}  ·  {FunctionText(t.FunctionOrRole)}  ·  {TierText(t.Tier)}  ·  {t.Width}×{t.Height}×{t.Length}" + (style.Length > 0 ? "  ·  " + style : string.Empty);
                if (f.Length > 0 && label.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                string key = t.Key;
                UiKit.AddFitButton(list, 0f, 0f, width, 30f, label, () => { ClosePicker(); pick(key); });
            }
        }

        private void ClosePicker()
        {
            if (_picker != null)
            {
                Kill(_picker);
                _picker = null;
            }
        }

        private KitJson Kit => _current >= 0 && _current < _kits.Count ? _kits[_current] : null;

        private void Rebuild()
        {
            if (_overlay != null)
            {
                Kill(_overlay);
                _overlay = null;
            }

            Build();
        }

        private void Build()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas, X, Y, W, H);
            _overlay = overlay;
            UiKit.AddText(panel, 20f, 12f, 600f, ControlH, L("ui.kit.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            BuildKitList(panel);

            // Right: the fields + the entries.
            var k2 = Kit;
            if (k2 != null)
            {
                BuildFields(panel, k2);
            }
            else
            {
                UiKit.AddText(panel, 300f, 78f, 700f, 30f, L("ui.ed.none"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            }

            // Last in the panel: the pad's first focus is a kit, never Close.
            UiKit.AddTierButton(panel, W - 170f, 12f, 150f, ControlH, L("ui.kit.close"), Close, ButtonTier.Quiet, "btn_close");
        }

        /// <summary>The left column: the known kits, New and Use.</summary>
        private void BuildKitList(Transform panel)
        {
            UiKit.AddText(panel, 20f, 60f, 240f, 24f, L("ui.kit.list"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var list = UiKit.ScrollList(panel, 20f, 88f, 250f, H - 88f - 132f);
            for (int i = 0; i < _kits.Count; i++)
            {
                int idx = i;
                var k = _kits[i];
                string label = (i == _current ? "▶ " : string.Empty) + (string.IsNullOrEmpty(k.name) ? k.key : k.name) + "  ·  " + KindText(k.kind) + " / " + TierText(k.tier);
                UiKit.AddFitButton(list, 0f, 0f, 240f, 36f, label, () => { _current = idx; Rebuild(); }); // 36: two 13 px lines
            }

            // One above the other, the list column wide: "Использовать" did not fit 120 px (#3263).
            UiKit.AddTierButton(panel, 20f, H - 120f, 250f, ControlH, L("ui.kit.new"), NewKit, ButtonTier.Secondary, "btn_new");
            if (Kit != null)
            {
                UiKit.AddTierButton(panel, 20f, H - 64f, 250f, ControlH, L("ui.kit.use"), () => { _useKit?.Invoke(Kit.key); Close(); },
                    ButtonTier.Secondary, "btn_continue");
            }
        }

        /// <summary>The kit's fields, its layout rows, the entries table and the footer.</summary>
        private void BuildFields(Transform panel, KitJson k2)
        {
            float x0 = FieldsX, y = 64f;
            Field(panel, x0, y, L("ui.kit.key"), k2.key, v => k2.key = EditorSceneKit.Slug(v));
            Field(panel, x0 + ColumnStep, y, L("ui.kit.name"), k2.name, v => k2.name = v);
            y += 40f;
            var kinds = _editorKind == StructureKit.KindStation ? new[] { StructureKit.KindStation } : new[] { StructureKit.KindSettlement, StructureKit.KindCity };
            Stepper(panel, x0, y, L("ui.kit.kind"), k2.kind, kinds, KindText, v => { k2.kind = v; Rebuild(); });
            var tiers = k2.kind == StructureKit.KindStation ? new[] { "small", "medium", "large", "huge", "colossal" }
                : k2.kind == StructureKit.KindCity ? new[] { StructureRoles.MetropolisTier }
                : new[] { "hamlet", "village", "town", "city" };
            Stepper(panel, x0 + ColumnStep, y, L("ui.kit.tier"), k2.tier, tiers, TierText, v => k2.tier = v);
            y += 40f;
            Field(panel, x0, y, L("ui.kit.pack"), k2.pack, v => k2.pack = EditorSceneKit.Slug(v)); // "Pack", not the long struct label (#3263)
            IntField(panel, x0 + ColumnStep, y, L("ui.kit.weight"), k2.weight, v => k2.weight = Mathf.Max(1, v));
            y += 40f;
            if (k2.kind != StructureKit.KindStation)
            {
                Field(panel, x0, y, L("ui.kit.planet_types"), string.Join(", ", k2.planetTypes), v => k2.planetTypes = SplitList(v));
                // #3333: an underground kit is never drawn for the surface; only the underground stamper composes it.
                UiKit.AddFitButton(panel, x0 + ColumnStep, y, 192f, 30f, (k2.underground ? "☑ " : "☐ ") + L("ui.kit.underground"), () => { k2.underground = !k2.underground; Rebuild(); });
                y += 40f;
            }

            IntField(panel, x0, y, L("ui.kit.modules_min"), k2.modulesMin, v => k2.modulesMin = Mathf.Max(0, v));
            IntField(panel, x0 + ColumnStep, y, L("ui.kit.modules_max"), k2.modulesMax, v => k2.modulesMax = Mathf.Max(0, v));
            y += 40f;
            if (k2.kind == StructureKit.KindStation)
            {
                Field(panel, x0, y, L("ui.kit.start"), k2.start, v => k2.start = EditorSceneKit.Slug(v));
                IntField(panel, x0 + ColumnStep, y, L("ui.kit.max_extent"), k2.maxExtent, v => k2.maxExtent = Mathf.Max(0, v));
                y += 40f;
                // #1920: the station's exterior detail — how many the composer mounts (0 = none); the test assembly shows it.
                IntField(panel, x0, y, L("ui.kit.solar_wings"), k2.solarWings, v => k2.solarWings = Mathf.Max(0, v));
                IntField(panel, x0 + IntStep, y, L("ui.kit.antennas"), k2.antennas, v => k2.antennas = Mathf.Max(0, v));
                IntField(panel, x0 + (2f * IntStep), y, L("ui.kit.domes"), k2.domes, v => k2.domes = Mathf.Max(0, v));
                y += 40f;
            }
            else if (k2.kind == StructureKit.KindCity)
            {
                IntField(panel, x0, y, L("ui.kit.grid"), k2.grid, v => k2.grid = v);
                IntField(panel, x0 + IntStep, y, L("ui.kit.district"), k2.districtSize, v => k2.districtSize = v);
                IntField(panel, x0 + (2f * IntStep), y, L("ui.kit.street"), k2.street, v => k2.street = v);
                IntField(panel, x0 + (3f * IntStep), y, L("ui.kit.height"), k2.height, v => k2.height = v);
                y += 40f;
                y = CityMapRows(panel, x0, y, k2); // #3600: the district map as a clickable grid, the grown city's rows
            }
            else
            {
                y = SettlementLayoutRows(panel, x0, y, k2); // #3600: the layout stepper and the picked layout's rows
            }

            BuildEntries(panel, k2, x0, y);
            BuildFooter(panel, k2, x0);
        }

        /// <summary>The entries table: module (typed or picked), min, max, required, weight, rotate, remove.</summary>
        private void BuildEntries(Transform panel, KitJson k2, float x0, float y)
        {
            UiKit.AddText(panel, x0, y, 220f, 24f, L("ui.kit.col_module"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(panel, x0 + 230f, y, 50f, 24f, L("ui.kit.col_min"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 290f, y, 50f, 24f, L("ui.kit.col_max"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 350f, y, 90f, 24f, L("ui.kit.col_required"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 450f, y, 60f, 24f, L("ui.kit.col_weight"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 520f, y, 80f, 24f, L("ui.kit.col_rotate"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            y += 26f;
            var rows = UiKit.ScrollList(panel, x0, y, 760f, H - y - 132f);
            for (int i = 0; i < k2.entries.Count; i++)
            {
                var e = k2.entries[i];
                int idx = i;
                var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(rows, false);
                row.sizeDelta = new Vector2(740f, 34f);
                var le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 34f;
                le.minHeight = 34f;
                // #1890: the module is picked from the pool (the key stays typeable for a module that is not saved yet).
                UiKit.AddInput(row, 0f, 2f, 180f, 30f, e.module, v => e.module = EditorSceneKit.Slug(v), string.Empty, 0, 14);
                UiKit.AddFitButton(row, 184f, 2f, 36f, 30f, "…", () => OpenPicker(k2.kind, key => { e.module = key; Rebuild(); }));
                UiKit.AddInput(row, 230f, 2f, 50f, 30f, e.min.ToString(), v => { if (int.TryParse(v, out var n)) e.min = Mathf.Max(0, n); }, string.Empty, 0, 14);
                UiKit.AddInput(row, 290f, 2f, 50f, 30f, e.max.ToString(), v => { if (int.TryParse(v, out var n)) e.max = Mathf.Max(0, n); }, string.Empty, 0, 14);
                UiKit.AddFitButton(row, 350f, 2f, 90f, 30f, e.required ? "☑" : "☐", () => { e.required = !e.required; Rebuild(); });
                UiKit.AddInput(row, 450f, 2f, 60f, 30f, e.weight.ToString(), v => { if (int.TryParse(v, out var n)) e.weight = Mathf.Max(1, n); }, string.Empty, 0, 14);
                UiKit.AddFitButton(row, 520f, 2f, 80f, 30f, e.rotate ? "☑" : "☐", () => { e.rotate = !e.rotate; Rebuild(); });
                UiKit.AddFitButton(row, 610f, 2f, 110f, 30f, L("ui.kit.remove"), () => { k2.entries.RemoveAt(idx); Rebuild(); });
            }
        }

        /// <summary>The message line (green: saved, red: failed), then + Entry, the primary Save kit and — set apart, red — Delete kit.</summary>
        private void BuildFooter(Transform panel, KitJson k2, float x0)
        {
            var status = UiKit.AddText(panel, x0, H - 116f, W - 20f - x0, 44f, _status, 14, _statusFailed ? UiKit.DangerCol : UiKit.Ok, TextAnchor.MiddleLeft);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddTierButton(panel, x0, H - 64f, FooterW, ControlH, L("ui.kit.add_entry"), () => { k2.entries.Add(new KitEntryJson()); Rebuild(); },
                ButtonTier.Secondary, "btn_new");
            UiKit.AddTierButton(panel, x0 + FooterW + 16f, H - 64f, FooterW, ControlH, L("ui.kit.save"), Save, ButtonTier.Primary, "btn_save");
            UiKit.AddTierButton(panel, W - 20f - FooterW, H - 64f, FooterW, ControlH, L("ui.kit.delete"), Delete, ButtonTier.Danger, "btn_delete");
        }

        /// <summary>The footer buttons' width.</summary>
        internal const float FooterW = 220f;

        /// <summary>The field grid (#3263): a 140 px label column (it was 110–130, and "Plot stride", "Solar wings" and
        /// the long pack label ran into their inputs), two text columns, four number columns.</summary>
        internal const float FieldsX = 284f, ColumnStep = 400f, IntStep = 200f, FieldLabelW = 140f;

        private static void FieldLabel(Transform panel, float x, float y, string label)
        {
            var text = UiKit.AddText(panel, x, y, FieldLabelW, 30f, label, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(text, FieldLabelW, UiKit.EditorMinFontSize, 14);
        }

        private void Field(Transform panel, float x, float y, string label, string value, Action<string> onChange)
        {
            FieldLabel(panel, x, y, label);
            UiKit.AddInput(panel, x + FieldLabelW + 4f, y, 228f, 30f, value ?? string.Empty, onChange, string.Empty, 0, 14);
        }

        private void IntField(Transform panel, float x, float y, string label, int value, Action<int> onChange)
        {
            FieldLabel(panel, x, y, label);
            var input = UiKit.AddInput(panel, x + FieldLabelW + 4f, y, 50f, 30f, value.ToString(), v => { if (int.TryParse(v, out var n)) onChange(n); }, string.Empty, 0, 14);
            input.contentType = InputField.ContentType.IntegerNumber;
        }

        /// <summary>A picked value shown by its name (<paramref name="display"/>), stored as its id.</summary>
        private void Stepper(Transform panel, float x, float y, string label, string value, string[] options, Func<string, string> display,
            Action<string> onChange)
        {
            FieldLabel(panel, x, y, label);
            var text = UiKit.AddText(panel, x + FieldLabelW + 4f, y, 166f, 30f, display(value), 14, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabelLines(text, 14, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            UiKit.AddFitButton(panel, x + FieldLabelW + 176f, y, 44f, 30f, "→", () =>
            {
                int i = Array.IndexOf(options, value);
                value = options[(i + 1) % options.Length];
                text.text = display(value);
                onChange(value);
            });
        }

        private void NewKit()
        {
            var j = new KitJson
            {
                key = "my_kit_" + (_kits.Count + 1),
                name = string.Format(L("ui.ed.default.kit"), _kits.Count + 1),
                kind = _editorKind == StructureKit.KindStation ? StructureKit.KindStation : StructureKit.KindSettlement,
                tier = _editorKind == StructureKit.KindStation ? "small" : "village",
            };
            _kits.Add(j);
            _current = _kits.Count - 1;
            Rebuild();
        }

        private void Save()
        {
            var k = Kit;
            if (k == null || string.IsNullOrEmpty(k.key))
            {
                SetStatus(L("ui.ed.need_key"), failed: true);
                Rebuild();
                return;
            }

            if (k.kind == StructureKit.KindStation)
            {
                k.underground = false; // #3333: stations hang in space — the server would ignore the flag anyway
            }

            try
            {
                Directory.CreateDirectory(UserKitsRoot);
                string json = JsonUtility.ToJson(k, true);
                File.WriteAllText(Path.Combine(UserKitsRoot, k.key + ".json"), json);
                string dir = Path.Combine(AppPaths.Root, _editorKind + "_exports", k.key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "kit.json"), json);
                SetStatus(string.Format(L("ui.kit.saved"), k.key, k.entries.Count), failed: false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitEditorPanel] Saving kit '{k.key}' failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"), failed: true); // a friendly line; the exception goes to the log (#3263)
            }

            Rebuild();
        }

        private void SetStatus(string text, bool failed)
        {
            _status = text ?? string.Empty;
            _statusFailed = failed;
        }

        /// <summary>Delete kit: asks first ("Delete “…” for good?") — it deleted the file at once (#3692).</summary>
        private void Delete()
        {
            var k = Kit;
            if (k == null)
            {
                return;
            }

            string name = string.IsNullOrEmpty(k.name) ? k.key : k.name;
            if (_confirmDelete != null)
            {
                _confirmDelete(name, DeleteNow);
                return;
            }

            _ownConfirm = UiKit.ConfirmDanger(_canvas, L("ui.editor.confirm_delete").Replace("{name}", name), L("ui.editor.delete"), DeleteNow,
                L("ui.action.cancel"));
        }

        /// <summary>Deletes the current kit's user file (a shipped kit has none — it leaves the list until the panel reopens).</summary>
        private void DeleteNow()
        {
            _ownConfirm = null;
            var k = Kit;
            if (k == null)
            {
                return;
            }

            try
            {
                string file = Path.Combine(UserKitsRoot, k.key + ".json");
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitEditorPanel] Deleting kit '{k.key}' failed: {e}");
                SetStatus(L("ui.ed.delete_failed_hint"), failed: true);
            }

            _kits.RemoveAt(_current);
            _current = _kits.Count > 0 ? 0 : -1;
            Rebuild();
        }

        private static List<string> SplitList(string text, char extra = ',')
        {
            var list = new List<string>();
            foreach (var raw in (text ?? string.Empty).Split(new[] { extra, ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = raw.Trim();
                if (s.Length > 0)
                {
                    list.Add(s);
                }
            }

            return list;
        }
    }
}
