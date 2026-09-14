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
        public List<string> planetTypes = new List<string>();
        public List<KitEntryJson> entries = new List<KitEntryJson>();
        public int colsMin, colsMax, rowsMin, rowsMax, plotStride, building, storeys;
        public bool modulesOnly;
        public int grid, districtSize, street, height;
        public List<string> roleMap = new List<string>();

        public static KitJson From(StructureKit k)
        {
            var j = new KitJson
            {
                key = k.Key, name = k.Name, kind = k.KindOrDefault, tier = k.Tier, pack = k.PackOrDefault, start = k.Start,
                weight = k.Weight, modulesMin = k.ModulesMin, modulesMax = k.ModulesMax, maxExtent = k.MaxExtent,
                planetTypes = new List<string>(k.PlanetTypes),
                colsMin = k.ColsMin, colsMax = k.ColsMax, rowsMin = k.RowsMin, rowsMax = k.RowsMax, plotStride = k.PlotStride,
                building = k.Building, storeys = k.Storeys, modulesOnly = k.ModulesOnly,
                grid = k.Grid, districtSize = k.DistrictSize, street = k.Street, height = k.Height, roleMap = new List<string>(k.RoleMap),
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
                PlanetTypes = new List<string>(planetTypes ?? new List<string>()),
                ColsMin = colsMin, ColsMax = colsMax, RowsMin = rowsMin, RowsMax = rowsMax, PlotStride = plotStride,
                Building = building, Storeys = storeys, ModulesOnly = modulesOnly,
                Grid = grid, DistrictSize = districtSize, Street = street, Height = height, RoleMap = new List<string>(roleMap ?? new List<string>()),
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
    /// </summary>
    internal sealed class KitEditorPanel
    {
        private const float W = 1100f, H = 900f;
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

        private KitEditorPanel(AppShell shell, Transform canvas, string editorKind, Action<string> useKit, Action onClosed,
            Func<string, List<StructureTemplate>> modules)
        {
            _shell = shell;
            _canvas = canvas;
            _editorKind = editorKind;
            _useKit = useKit;
            _onClosed = onClosed;
            _modules = modules;
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

        public static KitEditorPanel Show(AppShell shell, Transform canvas, string editorKind, string currentKey, Action<string> useKit, Action onClosed,
            Func<string, List<StructureTemplate>> modules = null)
        {
            var panel = new KitEditorPanel(shell, canvas, editorKind, useKit, onClosed, modules);
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
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
                _overlay = null;
            }

            _onClosed?.Invoke();
        }

        private string L(string key) => _shell?.L(key) ?? key;

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
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas, 0f, 0f, PW, PH);
            _picker = overlay;
            UiKit.AddText(panel, 20f, 12f, 500f, 30f, L("ui.kit.pick_module"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, PW - 120f, 12f, 100f, 30f, L("ui.kit.close"), ClosePicker);
            var list = UiKit.ScrollList(panel, 20f, 92f, PW - 40f, PH - 112f);
            UiKit.AddText(panel, 20f, 52f, 80f, 30f, L("ui.kit.filter"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            // The field stays while typing; only the rows below are rebuilt (rebuilding the field would drop its focus).
            UiKit.AddInput(panel, 100f, 52f, PW - 120f, 30f, _pickerFilter, v =>
            {
                _pickerFilter = v ?? string.Empty;
                FillPicker(list, pool, pick, PW - 60f);
            });
            FillPicker(list, pool, pick, PW - 60f);
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
                string label = $"{t.Key}  ·  {t.FunctionOrRole}  ·  {t.Tier}  ·  {t.Width}×{t.Height}×{t.Length}" + (style.Length > 0 ? "  ·  " + style : string.Empty);
                if (f.Length > 0 && label.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                string key = t.Key;
                UiKit.AddButton(list, 0f, 0f, width, 30f, label, () => { ClosePicker(); pick(key); });
            }
        }

        private void ClosePicker()
        {
            if (_picker != null)
            {
                UnityEngine.Object.Destroy(_picker);
                _picker = null;
            }
        }

        private KitJson Kit => _current >= 0 && _current < _kits.Count ? _kits[_current] : null;

        private void Rebuild()
        {
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
                _overlay = null;
            }

            Build();
        }

        private void Build()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas, 0f, 0f, W, H);
            _overlay = overlay;
            UiKit.AddText(panel, 20f, 12f, 600f, 30f, L("ui.kit.title"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, W - 120f, 12f, 100f, 30f, L("ui.kit.close"), Close);

            // Left: the kit list.
            UiKit.AddText(panel, 20f, 50f, 240f, 24f, L("ui.kit.list"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var list = UiKit.ScrollList(panel, 20f, 78f, 250f, H - 170f);
            for (int i = 0; i < _kits.Count; i++)
            {
                int idx = i;
                var k = _kits[i];
                string label = (i == _current ? "▶ " : string.Empty) + (string.IsNullOrEmpty(k.name) ? k.key : k.name) + "  ·  " + k.kind + "/" + k.tier;
                UiKit.AddButton(list, 0f, 0f, 240f, 30f, label, () => { _current = idx; Rebuild(); });
            }

            UiKit.AddButton(panel, 20f, H - 84f, 120f, 34f, L("ui.kit.new"), NewKit);
            if (Kit != null)
            {
                UiKit.AddButton(panel, 150f, H - 84f, 120f, 34f, L("ui.kit.use"), () => { _useKit?.Invoke(Kit.key); Close(); });
            }

            // Right: the fields + the entries.
            var k2 = Kit;
            if (k2 == null)
            {
                UiKit.AddText(panel, 300f, 78f, 700f, 30f, L("ui.ed.none"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                return;
            }

            float x0 = 300f, y = 50f;
            Field(panel, x0, y, L("ui.kit.key"), k2.key, v => k2.key = Slug(v));
            Field(panel, x0 + 380f, y, L("ui.kit.name"), k2.name, v => k2.name = v);
            y += 40f;
            var kinds = _editorKind == StructureKit.KindStation ? new[] { StructureKit.KindStation } : new[] { StructureKit.KindSettlement, StructureKit.KindCity };
            Stepper(panel, x0, y, L("ui.kit.kind"), k2.kind, kinds, v => { k2.kind = v; Rebuild(); });
            var tiers = k2.kind == StructureKit.KindStation ? new[] { "small", "medium", "large", "huge", "colossal" }
                : k2.kind == StructureKit.KindCity ? new[] { StructureRoles.MetropolisTier }
                : new[] { "hamlet", "village", "town", "city" };
            Stepper(panel, x0 + 380f, y, L("ui.kit.tier"), k2.tier, tiers, v => k2.tier = v);
            y += 40f;
            Field(panel, x0, y, L("ui.struct.pack"), k2.pack, v => k2.pack = Slug(v));
            IntField(panel, x0 + 380f, y, L("ui.kit.weight"), k2.weight, v => k2.weight = Mathf.Max(1, v));
            y += 40f;
            if (k2.kind != StructureKit.KindStation)
            {
                Field(panel, x0, y, L("ui.kit.planet_types"), string.Join(", ", k2.planetTypes), v => k2.planetTypes = SplitList(v));
                y += 40f;
            }

            IntField(panel, x0, y, L("ui.kit.modules_min"), k2.modulesMin, v => k2.modulesMin = Mathf.Max(0, v));
            IntField(panel, x0 + 380f, y, L("ui.kit.modules_max"), k2.modulesMax, v => k2.modulesMax = Mathf.Max(0, v));
            y += 40f;
            if (k2.kind == StructureKit.KindStation)
            {
                Field(panel, x0, y, L("ui.kit.start"), k2.start, v => k2.start = Slug(v));
                IntField(panel, x0 + 380f, y, L("ui.kit.max_extent"), k2.maxExtent, v => k2.maxExtent = Mathf.Max(0, v));
                y += 40f;
            }
            else if (k2.kind == StructureKit.KindCity)
            {
                IntField(panel, x0, y, L("ui.kit.grid"), k2.grid, v => k2.grid = v);
                IntField(panel, x0 + 190f, y, L("ui.kit.district"), k2.districtSize, v => k2.districtSize = v);
                IntField(panel, x0 + 380f, y, L("ui.kit.street"), k2.street, v => k2.street = v);
                IntField(panel, x0 + 570f, y, L("ui.kit.height"), k2.height, v => k2.height = v);
                y += 40f;
                Field(panel, x0, y, L("ui.kit.role_map"), string.Join(" ", k2.roleMap), v => k2.roleMap = SplitList(v, ' '));
                y += 40f;
            }
            else
            {
                IntField(panel, x0, y, L("ui.kit.cols_min"), k2.colsMin, v => k2.colsMin = v);
                IntField(panel, x0 + 190f, y, L("ui.kit.cols_max"), k2.colsMax, v => k2.colsMax = v);
                IntField(panel, x0 + 380f, y, L("ui.kit.rows_min"), k2.rowsMin, v => k2.rowsMin = v);
                IntField(panel, x0 + 570f, y, L("ui.kit.rows_max"), k2.rowsMax, v => k2.rowsMax = v);
                y += 40f;
                IntField(panel, x0, y, L("ui.kit.plot_stride"), k2.plotStride, v => k2.plotStride = v);
                IntField(panel, x0 + 190f, y, L("ui.kit.building"), k2.building, v => k2.building = v);
                IntField(panel, x0 + 380f, y, L("ui.kit.storeys"), k2.storeys, v => k2.storeys = v);
                UiKit.AddButton(panel, x0 + 570f, y, 180f, 30f, (k2.modulesOnly ? "☑ " : "☐ ") + L("ui.kit.modules_only"), () => { k2.modulesOnly = !k2.modulesOnly; Rebuild(); });
                y += 40f;
            }

            // The entries table.
            UiKit.AddText(panel, x0, y, 220f, 24f, L("ui.kit.col_module"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(panel, x0 + 230f, y, 50f, 24f, L("ui.kit.col_min"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 290f, y, 50f, 24f, L("ui.kit.col_max"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 350f, y, 90f, 24f, L("ui.kit.col_required"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 450f, y, 60f, 24f, L("ui.kit.col_weight"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, x0 + 520f, y, 80f, 24f, L("ui.kit.col_rotate"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);
            y += 26f;
            var rows = UiKit.ScrollList(panel, x0, y, 760f, H - y - 100f);
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
                UiKit.AddInput(row, 0f, 2f, 180f, 30f, e.module, v => e.module = Slug(v), string.Empty, 0, 14);
                UiKit.AddButton(row, 184f, 2f, 36f, 30f, "…", () => OpenPicker(k2.kind, key => { e.module = key; Rebuild(); }));
                UiKit.AddInput(row, 230f, 2f, 50f, 30f, e.min.ToString(), v => { if (int.TryParse(v, out var n)) e.min = Mathf.Max(0, n); }, string.Empty, 0, 14);
                UiKit.AddInput(row, 290f, 2f, 50f, 30f, e.max.ToString(), v => { if (int.TryParse(v, out var n)) e.max = Mathf.Max(0, n); }, string.Empty, 0, 14);
                UiKit.AddButton(row, 350f, 2f, 90f, 30f, e.required ? "☑" : "☐", () => { e.required = !e.required; Rebuild(); });
                UiKit.AddInput(row, 450f, 2f, 60f, 30f, e.weight.ToString(), v => { if (int.TryParse(v, out var n)) e.weight = Mathf.Max(1, n); }, string.Empty, 0, 14);
                UiKit.AddButton(row, 520f, 2f, 80f, 30f, e.rotate ? "☑" : "☐", () => { e.rotate = !e.rotate; Rebuild(); });
                UiKit.AddButton(row, 610f, 2f, 110f, 30f, L("ui.kit.remove"), () => { k2.entries.RemoveAt(idx); Rebuild(); });
            }

            UiKit.AddButton(panel, x0, H - 84f, 160f, 34f, L("ui.kit.add_entry"), () => { k2.entries.Add(new KitEntryJson()); Rebuild(); });
            UiKit.AddButton(panel, x0 + 180f, H - 84f, 160f, 34f, L("ui.kit.save"), Save);
            UiKit.AddButton(panel, x0 + 360f, H - 84f, 160f, 34f, L("ui.kit.delete"), Delete);
            UiKit.AddText(panel, x0 + 540f, H - 84f, 520f, 34f, _status, 13, UiKit.Ok, TextAnchor.MiddleLeft);
        }

        private void Field(Transform panel, float x, float y, string label, string value, Action<string> onChange)
        {
            UiKit.AddText(panel, x, y, 130f, 30f, label, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddInput(panel, x + 135f, y, 230f, 30f, value ?? string.Empty, onChange, string.Empty, 0, 14);
        }

        private void IntField(Transform panel, float x, float y, string label, int value, Action<int> onChange)
        {
            UiKit.AddText(panel, x, y, 110f, 30f, label, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var input = UiKit.AddInput(panel, x + 115f, y, 70f, 30f, value.ToString(), v => { if (int.TryParse(v, out var n)) onChange(n); }, string.Empty, 0, 14);
            input.contentType = InputField.ContentType.IntegerNumber;
        }

        private void Stepper(Transform panel, float x, float y, string label, string value, string[] options, Action<string> onChange)
        {
            UiKit.AddText(panel, x, y, 110f, 30f, label, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var text = UiKit.AddText(panel, x + 115f, y, 190f, 30f, value, 14, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(panel, x + 310f, y, 40f, 30f, "→", () =>
            {
                int i = Array.IndexOf(options, value);
                value = options[(i + 1) % options.Length];
                text.text = value;
                onChange(value);
            });
        }

        private void NewKit()
        {
            var j = new KitJson
            {
                key = "my_kit_" + (_kits.Count + 1),
                name = "My Kit " + (_kits.Count + 1),
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
                _status = L("ui.ed.need_key");
                Rebuild();
                return;
            }

            try
            {
                Directory.CreateDirectory(UserKitsRoot);
                string json = JsonUtility.ToJson(k, true);
                File.WriteAllText(Path.Combine(UserKitsRoot, k.key + ".json"), json);
                string dir = Path.Combine(AppPaths.Root, _editorKind + "_exports", k.key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "kit.json"), json);
                _status = string.Format(L("ui.kit.saved"), k.key, k.entries.Count);
            }
            catch (Exception e)
            {
                _status = string.Format(L("ui.ed.export_failed"), e.Message);
            }

            Rebuild();
        }

        private void Delete()
        {
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
                _status = string.Format(L("ui.ed.export_failed"), e.Message);
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
