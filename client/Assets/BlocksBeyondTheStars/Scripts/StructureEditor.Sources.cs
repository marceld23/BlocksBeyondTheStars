// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Generate from any source (#3523): the editor's <b>Generate</b> runs any structure source of the registry
    /// (<see cref="StructureSources"/>, the very builds the server runs for a fresh instance) instead of the two fixed generators.
    /// A picker lists the sources by where they stand (surface, underground, space); the form shows the picked source's
    /// parameters (<see cref="IStructureSource.GetParams"/>: a number, a choice, a switch or a text), the seed with its reroll
    /// and — where the source has an intact form — <i>as in the world</i> (the generator's own decay for the current terrain
    /// generation) against <i>intact</i>. The result loads as editable cells: blocks by key with their dye, glow and form,
    /// markers onto the marker palette (a chest's or sound's settings kept), pre-built devices onto their blocks, cavity cells
    /// as cavity. The station editor bakes space sources, the town editor surface and underground ones; the classic Generate is
    /// the <c>settlement</c> / <c>station</c> source. The same source rows describe a generator entry (StructureEditor.Entry.cs).
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The source sections of the picker, in order (a ship source is never offered).</summary>
        private static readonly StructureSourceKind[] PickerKinds =
        {
            StructureSourceKind.Surface, StructureSourceKind.Underground, StructureSourceKind.Space,
        };

        private string _sourceKey = string.Empty;
        private readonly Dictionary<string, string> _sourceValues = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _asInWorld = true;
        private EditorLoadPicker _sourcePicker;

        /// <summary>The source the form shows: the picked one while it fits the editor (or the entry's pool), else the default.</summary>
        private IStructureSource CurrentSource()
        {
            var picked = StructureSources.Get(_sourceKey);
            return picked != null && SourceFits(picked) ? picked : StructureSources.Get(DefaultSourceKey()) ?? StructureSources.All[0];
        }

        private bool SourceFits(IStructureSource s)
            => _entryMode ? StructureStampRules.PoolTakes(CurrentTarget(), s) : StructureStampRules.EditorBakes(EditorMode == Mode.Station, s.Kind);

        /// <summary>The classic Generate (a settlement / a station) — and, for an entry, a source its pool takes.</summary>
        private string DefaultSourceKey()
        {
            if (EditorMode == Mode.Station)
            {
                return "station";
            }

            if (!_entryMode)
            {
                return "settlement";
            }

            return _entryPool == StructureStampRules.PoolUnderground ? "ancient_ruin"
                : _entryPool == StructureStampRules.PoolLandmark ? "monument"
                : "settlement";
        }

        /// <summary>A parameter's value: the one set in the form (normalized), else its default.</summary>
        private string ParamValue(StructureParam p)
            => _sourceValues.TryGetValue(p.Key, out string v) && p.TryNormalize(v, out string n) ? n : p.Default;

        /// <summary>Every declared parameter of <paramref name="source"/> with its current value.</summary>
        private Dictionary<string, string> ParamValues(IStructureSource source)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Shell?.Content != null)
            {
                foreach (var p in source.GetParams(Shell.Content))
                {
                    values[p.Key] = ParamValue(p);
                }
            }

            return values;
        }

        // ---------------------------------------------------------------- the form

        /// <summary>The procedural starting point: the source with its parameters, the seed, intact / as in the world, Generate —
        /// and the villages' surface block.</summary>
        private float BuildGenerateRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.struct.generate_title"));
            y += 28f;
            var source = CurrentSource();
            y = BuildSourceRows(p, y, source);
            y = BuildSeedRow(p, y);
            if (source.SupportsIntact)
            {
                UiKit.AddFitButton(p, 16f, y, 348f, 34f, (_asInWorld ? "☑ " : "☐ ") + L("ui.struct.as_in_world"), () =>
                {
                    _asInWorld = !_asInWorld;
                    RebuildUi();
                });
                y += 40f;
            }
            else
            {
                var only = UiKit.AddText(p, 16f, y, 348f, 22f, L("ui.struct.as_in_world_only"), 14, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(only, 348f, UiKit.EditorMinFontSize, 14);
                y += 28f;
            }

            UiKit.AddFitButton(p, 16f, y, 348f, 34f, L("ui.struct.generate"), GenerateFromSource);
            y += 40f;
            return BuildSurfaceRow(p, y) + 6f;
        }

        /// <summary>The town editor's surface block (the villages' material, the ruins' rubble, the tokens' preview).</summary>
        private float BuildSurfaceRow(Transform p, float y)
        {
            if (EditorMode != Mode.Settlement)
            {
                return y;
            }

            FieldLabel(p, y, L("ui.struct.surface"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _surface, v => _surface = v ?? string.Empty);
            return y + 66f;
        }

        /// <summary>The seed and its reroll (an entry's preview follows a reroll at once).</summary>
        private float BuildSeedRow(Transform p, float y)
        {
            var seedLabel = UiKit.AddText(p, 16f, y, 60f, RowH, L("ui.struct.seed"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(seedLabel, 60f, UiKit.EditorMinFontSize, 15);
            var seedInput = UiKit.AddInput(p, 80f, y, 110f, RowH, _seed.ToString(CultureInfo.InvariantCulture), v =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    _seed = s;
                }
            });
            seedInput.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddFitButton(p, 196f, y, 168f, RowH, L("ui.struct.reroll"), () =>
            {
                _seed = UnityEngine.Random.Range(1, 1000000);
                seedInput.SetTextWithoutNotify(_seed.ToString(CultureInfo.InvariantCulture));
                if (_entryMode)
                {
                    ShowEntryPreview();
                }
            });
            return y + 36f;
        }

        /// <summary>The source row (its name and the picker) and one row per parameter.</summary>
        private float BuildSourceRows(Transform p, float y, IStructureSource source)
        {
            RowLabel(p, y, L("ui.struct.source"));
            ValueLabel(p, y, L(source.LabelKey));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "…", OpenSourcePicker);
            y += ValueRowStep;
            if (Shell?.Content == null)
            {
                return y;
            }

            foreach (var param in source.GetParams(Shell.Content))
            {
                y = ParamRow(p, y, param);
            }

            return y;
        }

        /// <summary>One parameter: a switch, a text field, a number with − / +, or a choice with →.</summary>
        private float ParamRow(Transform p, float y, StructureParam param)
        {
            string label = L(param.LabelKey);
            string value = ParamValue(param);
            switch (param.Type)
            {
                case StructureParamType.Bool:
                    UiKit.AddFitButton(p, 16f, y, 348f, 34f, (value == "true" ? "☑ " : "☐ ") + label,
                        () => SetParam(param, value == "true" ? "false" : "true"));
                    return y + 40f;
                case StructureParamType.Text:
                    FieldLabel(p, y, label);
                    UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, value, v => _sourceValues[param.Key] = CleanText(v));
                    return y + 66f;
                case StructureParamType.Int:
                    {
                        FieldLabel(p, y, label);
                        int n = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : param.Min;
                        UiKit.AddText(p, 16f, y + 24f, 220f, RowH, value, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                        UiKit.AddFitButton(p, 260f, y + 24f, 40f, RowH, "−",
                            () => SetParam(param, Math.Max(param.Min, n - 1).ToString(CultureInfo.InvariantCulture)));
                        UiKit.AddFitButton(p, 310f, y + 24f, 40f, RowH, "+",
                            () => SetParam(param, Math.Min(param.Max, n + 1).ToString(CultureInfo.InvariantCulture)));
                        return y + 60f;
                    }

                default:
                    {
                        FieldLabel(p, y, label);
                        var shown = UiKit.AddText(p, 16f, y + 22f, 290f, ValueRowH, OptionLabel(param, value), 16, UiKit.Cyan,
                            TextAnchor.MiddleLeft, FontStyle.Bold);
                        UiKit.FitLabelLines(shown, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
                        UiKit.AddFitButton(p, ArrowX, y + 25f, ArrowW, RowH, "→", () => SetParam(param, NextOption(param, value)));
                        return y + 62f;
                    }
            }
        }

        /// <summary>A text parameter never carries the settings line's separators.</summary>
        private static string CleanText(string v) => (v ?? string.Empty).Replace(";", " ").Replace("|", " ").Replace("\n", " ");

        /// <summary>Sets a parameter. An entry's preview is NOT rebuilt (a number stepped through − / + would rebuild the source
        /// and re-frame the camera on every click): the status line points at <i>Update the preview</i>.</summary>
        private void SetParam(StructureParam param, string value)
        {
            _sourceValues[param.Key] = value;
            if (_entryMode && _previewOn)
            {
                SetStatus(L("ui.ed.entry.stale"));
            }

            RebuildUi();
        }

        private static string NextOption(StructureParam p, string current)
        {
            if (p.Options.Count == 0)
            {
                return current;
            }

            int i = 0;
            for (int k = 0; k < p.Options.Count; k++)
            {
                if (p.Options[k] == current)
                {
                    i = k;
                }
            }

            return p.Options[(i + 1) % p.Options.Count];
        }

        /// <summary>A choice's value as the form shows it: a tier or a ship by its name, the shared options (auto / yes / no,
        /// none …) translated, anything else (a kit, a template, a profession key) as it is.</summary>
        private string OptionLabel(StructureParam p, string v)
        {
            if (string.IsNullOrEmpty(v))
            {
                return L("ui.source.opt.none");
            }

            string key = p.Key == "tier" ? "ui.tier." + v : p.Key == "ship" ? "ship." + v + ".name" : "ui.source.opt." + v;
            return Shell?.Localizer != null && Shell.Localizer.Has(key) ? L(key) : v;
        }

        // ---------------------------------------------------------------- the picker

        /// <summary>Lists every source by where it stands; a source this editor (or this entry's pool) cannot take says where it
        /// belongs instead of loading.</summary>
        private void OpenSourcePicker()
        {
            _sourcePicker?.Close();
            var sections = new List<EditorLoadPicker.Section>();
            foreach (var kind in PickerKinds)
            {
                var section = new EditorLoadPicker.Section { Title = L("ui.struct.source_kind." + kind.ToString().ToLowerInvariant()) };
                foreach (var s in StructureSources.All)
                {
                    if (s.Kind != kind)
                    {
                        continue;
                    }

                    var source = s;
                    bool fits = SourceFits(s);
                    section.Items.Add(new EditorLoadPicker.Item
                    {
                        Label = L(s.LabelKey),
                        Detail = fits ? (s.SupportsIntact ? L("ui.struct.source_intact") : string.Empty) : L(ElsewhereKey(s.Kind)),
                        Load = () => PickSource(source, fits),
                    });
                }

                sections.Add(section);
            }

            _sourcePicker = EditorLoadPicker.Show(Shell, _canvas.transform, sections, 0, () => _sourcePicker = null, L("ui.struct.source_title"));
        }

        /// <summary>Where a source that does not fit belongs: the other editor, or (an entry) another pool.</summary>
        private string ElsewhereKey(StructureSourceKind kind)
            => _entryMode ? "ui.struct.source_not_pool"
                : kind == StructureSourceKind.Space ? "ui.struct.source_in_station" : "ui.struct.source_in_town";

        private void PickSource(IStructureSource source, bool fits)
        {
            if (!fits)
            {
                SetStatus(L(_entryMode ? "ui.ed.entry.wrong_pool"
                    : source.Kind == StructureSourceKind.Space ? "ui.struct.source_needs_station" : "ui.struct.source_needs_town"));
                return;
            }

            _sourceKey = source.Key;
            _entrySourceInvalid = false; // #3523: a loaded entry's unknown source is replaced by a chosen one
            _sourceValues.Clear();
            if (Shell?.Content != null)
            {
                foreach (var param in source.GetParams(Shell.Content))
                {
                    // The classic Generate followed the size stepper: a source with a size starts at the form's tier.
                    if (param.Key == "tier" && param.TryNormalize(_tiers[_tier], out string tier))
                    {
                        _sourceValues[param.Key] = tier;
                    }
                }
            }

            if (_entryMode)
            {
                ShowEntryPreview();
            }

            RebuildUi();
        }

        // ---------------------------------------------------------------- the build

        /// <summary>Runs the source for the form's parameters and seed on the client; null (logged) when it cannot build.</summary>
        private BuiltStructure BuildSource(IStructureSource source, bool asInWorld)
        {
            if (Shell?.Content == null)
            {
                return null;
            }

            var request = new StructureBuildRequest(Shell.Content, _seed)
            {
                Params = ParamValues(source),
                Surface = PreviewSurface(),
                Generation = WorldDescription.CurrentTerrainGeneration,
                AsInWorld = asInWorld,
            };
            try
            {
                return source.Build(request);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Source '{source.Key}' could not build: {e}");
                return null;
            }
        }

        /// <summary>Generate: the source's build as editable cells, a whole structure of this editor (#3523).</summary>
        private void GenerateFromSource()
        {
            var source = CurrentSource();
            var built = BuildSource(source, _asInWorld || !source.SupportsIntact);
            if (built == null)
            {
                SetStatus(L("ui.ed.load_failed_hint")); // a friendly line; the exception went to the log (#3263)
                return;
            }

            string tier = _tiers[_tier];
            foreach (var param in source.GetParams(Shell.Content))
            {
                string v = ParamValue(param);
                if (param.Key == "tier" && Array.IndexOf(_tiers, v) >= 0)
                {
                    tier = v;
                }
            }

            string name = L(source.LabelKey);
            ApplyTemplate($"{source.Key}_{_seed}", $"{name} #{_seed}", tier, "default", 1, null, BuiltCells(built), copy: false,
                underground: source.Kind == StructureSourceKind.Underground);
            _groundRow = built.GroundRow; // a landmark made from it keeps the row that sat on the ground
            SetStatus(string.Format(L("ui.ed.generated"), name, _design.Count, _seed) + "\n" + _status);
        }

        /// <summary>A built structure as cells: markers first (a block on a marker cell is the one skipped), devices onto their
        /// blocks, blocks by key with dye, glow and form, cavity cells as cavity.</summary>
        private List<CellJson> BuiltCells(BuiltStructure b)
        {
            var markers = new List<(string Type, Vector3i At, string Data)>(b.Markers.Count);
            foreach (var m in b.Markers)
            {
                markers.Add((m.Type, new Vector3i(m.X, m.Y, m.Z), m.Data));
            }

            return GridCells(b.Width, b.Height, b.Length, b.Get, b.GetModifier, b.GetShape, b.IsCavity, markers);
        }

        /// <summary>A settlement-shaped structure as cells (the template ruin preview).</summary>
        private List<CellJson> SettlementCells(SettlementStructure s)
        {
            var markers = new List<(string Type, Vector3i At, string Data)>(s.Markers.Count);
            foreach (var m in s.Markers)
            {
                markers.Add((m.Type, m.LocalPos, m.Data));
            }

            return GridCells(s.Width, s.Height, s.Length, s.Get, s.GetModifier, s.GetShape, null, markers);
        }

        /// <summary>Any voxel box as cells (see <see cref="BuiltCells"/>). A marker keeps its data as its settings only where the
        /// settings tool edits it (a chest, a vendor, a sound, a den).</summary>
        private List<CellJson> GridCells(int w, int h, int l, Func<int, int, int, ushort> get, Func<int, int, int, (int Tint, int Glow)> mod,
            Func<int, int, int, int> shape, Func<int, int, int, bool> cavity, List<(string Type, Vector3i At, string Data)> markers)
        {
            var cells = new List<CellJson>();
            var devices = new Dictionary<Vector3i, (int Mode, string Config, string Label)>(); // #2260
            foreach (var (type, at, data) in markers)
            {
                if (!TakeDeviceMarker(type, data, at, devices))
                {
                    cells.Add(new CellJson { x = at.X, y = at.Y, z = at.Z, kind = "marker", id = type,
                        config = EditorMarkerPanel.Edits(type) ? data ?? string.Empty : string.Empty });
                }
            }

            var keys = new Dictionary<ushort, string>();
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int z = 0; z < l; z++)
                    {
                        AddGridCell(cells, keys, devices, x, y, z, get(x, y, z), mod, shape, cavity);
                    }
                }
            }

            return cells;
        }

        private void AddGridCell(List<CellJson> cells, Dictionary<ushort, string> keys, Dictionary<Vector3i, (int Mode, string Config, string Label)> devices,
            int x, int y, int z, ushort v, Func<int, int, int, (int Tint, int Glow)> mod, Func<int, int, int, int> shape, Func<int, int, int, bool> cavity)
        {
            if (v == 0)
            {
                if (cavity != null && cavity(x, y, z))
                {
                    cells.Add(new CellJson { x = x, y = y, z = z, kind = LandmarkRules.CavityKind, id = LandmarkRules.CavityKind });
                }

                return;
            }

            if (!keys.TryGetValue(v, out string key))
            {
                key = Shell.Content.BlockById(new BlockId(v))?.Key ?? string.Empty;
                keys[v] = key;
            }

            if (key.Length == 0)
            {
                return;
            }

            var (tint, glow) = mod != null ? mod(x, y, z) : (0, 0);
            var dv = devices.TryGetValue(new Vector3i(x, y, z), out var found) ? found : (0, string.Empty, string.Empty);
            cells.Add(new CellJson
            {
                x = x, y = y, z = z, kind = "block", id = key, tint = tint, glow = glow, shape = shape?.Invoke(x, y, z) ?? 0,
                mode = dv.Item1, config = dv.Item2, label = dv.Item3,
            });
        }
    }
}
