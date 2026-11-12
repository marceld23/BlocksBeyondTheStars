// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The structure editor's LANDMARK mode (#3491–#3493): "Use as" gets a third value beside whole structure and kit
    /// module — a single giant building that stands on a planet on its own, usually as a ruin.
    /// <list type="bullet">
    /// <item><b>The room</b> is <see cref="LandmarkRules.MaxSize"/> blocks a side in the town editor (a bridge or a castle
    /// with its wall does not fit into 128).</item>
    /// <item><b>The form</b>: ruin or as built, the ruin profile and strength, the conditions (atmosphere, inhabited
    /// planet), "inhabited" (protected, its posts staffed) and the ground row — the row that lies on the ground; what is
    /// below it is carved into the rock.</item>
    /// <item><b>The ruin preview</b> runs the very pass the server runs (<see cref="LandmarkGenerator"/>) over the design
    /// with a seed of the author's choice and shows the result; a second click brings the design back. While it shows,
    /// nothing can be placed, removed or saved — a preview is never the design.</item>
    /// <item><b>Tools</b> in the palette: the <i>keep</i> brush (cells the ruin pass leaves standing), the <i>cavity</i>
    /// brush (the tunnels and caverns of an underground part), the <i>settings</i> tool (a click on a chest, vendor, sound
    /// or den marker opens <see cref="EditorMarkerPanel"/>), and the <i>sound</i> and <i>den</i> markers.</item>
    /// <item><b>Save</b> writes the compact template (<see cref="TemplateBody"/>) to the user-content folder — the local
    /// server offers it to the next new world — and an export bundle for <c>tools/merge_structure.py</c>.</item>
    /// </list>
    /// </summary>
    public sealed partial class StructureEditor
    {
        [Serializable] private sealed class PaletteJson { public string kind = "block", id = string.Empty; public int tint, glow, shape; public bool keep; }

        /// <summary>The landmark file (matches the server's StructureTemplate JSON, compact body included).</summary>
        [Serializable] private sealed class LandmarkJson
        {
            public string key, name, kind = "landmark", pack = "default", nameKey = string.Empty;
            public int weight = 1;
            public List<string> planetTypes = new();
            public bool landmark = true;
            public string ruin = string.Empty, ruinProfile = string.Empty;
            public float ruinStrength;
            public List<string> requires = new();
            public int groundRow;
            public bool @protected;
            public int width, height, length;
            public List<PaletteJson> palette = new();
            public List<string> rows = new();
            public List<CellJson> cells = new();
        }

        private static readonly Color KeepColor = new Color(0.45f, 1f, 0.5f);
        private static readonly Color CavityColor = new Color(0.55f, 0.75f, 1f);

        private bool _landmarkMode;
        private bool _lmRuin = true;
        private string _lmProfile = LandmarkRules.ProfileMasonry;
        private float _lmStrength; // 0 = the profile's own
        private bool _lmAtmosphere = true, _lmInhabited, _lmProtected;
        private int _groundRow;
        private string _lmNameKey = string.Empty;
        private int _previewSeed = 1;
        private bool _previewOn;
        private List<CellJson> _previewBackup;
        private EditorMarkerPanel _markerPanel;

        /// <summary>The build room's edge: a station keeps 128, the town editor — where landmarks are built — gets 256.</summary>
        private int RoomSize => EditorMode == Mode.Station ? 128 : LandmarkRules.MaxSize;

        private string LandmarkExportsRoot => Path.Combine(AppPaths.Root, "landmark_exports");

        private string UserLandmarksRoot => Path.Combine(AppPaths.Root, "usercontent", "landmark_templates");

        // ---------------------------------------------------------------- "use as"

        private string UseLabel()
            => L(_entryMode ? "ui.use.entry" : _landmarkMode ? "ui.use.landmark" : _moduleMode ? "ui.use.module" : "ui.use.whole");

        /// <summary>Whole structure → kit module → landmark (the town editor only) → generator entry (#3523) → whole structure.
        /// A preview gives the design back first; a generator entry shows its source's build.</summary>
        private void CycleUse()
        {
            EndPreview();
            if (_entryMode)
            {
                _entryMode = false;
            }
            else if (_landmarkMode)
            {
                _landmarkMode = false;
                _entryMode = true;
            }
            else if (_moduleMode)
            {
                _moduleMode = false;
                _landmarkMode = EditorMode == Mode.Settlement;
                _entryMode = EditorMode == Mode.Station;
            }
            else
            {
                _moduleMode = true;
                if (string.IsNullOrEmpty(_function))
                {
                    _function = Functions()[0];
                }
            }

            SyncRole();
            ShowEntryPreview(); // nothing outside an entry
            RebuildUi();
        }

        // ---------------------------------------------------------------- the palette's tools

        private EditorPaletteKit.Entry Tool(string id, string kind, Color c) => new EditorPaletteKit.Entry
        {
            Id = id, Label = L("ui.marker." + id), Kind = kind, Group = "markers", Color = c,
        };

        /// <summary>The settlement palette plus what a landmark needs: the sound and den markers and the three tools.</summary>
        private EditorPaletteKit.Entry[] WithLandmarkTools(EditorPaletteKit.Entry[] markers)
        {
            var list = new List<EditorPaletteKit.Entry>(markers)
            {
                M(LandmarkRules.SoundMarker, new Color(0.95f, 0.6f, 0.95f)),
                M(LandmarkRules.DenMarker, new Color(0.75f, 0.6f, 0.4f)),
                Tool("keep", "keep", KeepColor),
                Tool(LandmarkRules.CavityKind, LandmarkRules.CavityKind, CavityColor),
                Tool("settings", "settings", new Color(1f, 0.85f, 0.35f)),
            };
            return list.ToArray();
        }

        /// <summary>A click with a tool: the port brush (#1877), the device tool (#2260), the keep brush and the settings
        /// tool. True when the click was a tool's — or when the ruin preview shows, which takes no edits at all.</summary>
        private bool ToolClick(EditorPaletteKit.Entry pal)
        {
            if (_previewOn)
            {
                SetStatus(L(PreviewLockedKey())); // #3523: a turn, the materials, a ruin or an entry's build
                return true;
            }

            switch (pal.Kind)
            {
                case "port":
                    PaintPort(pal.Id);
                    return true;
                case "device":
                    EditDevice();
                    return true;
                case "keep":
                    ToggleKeep();
                    return true;
                case "settings":
                    EditMarker();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether a tool works on this cell (the ghost is green over it).</summary>
        private bool ToolFits(string tool, CellData d) => tool switch
        {
            "device" => CarriesDeviceSettings(DeviceKindOf(d)),
            "settings" => d.Kind == "marker" && EditorMarkerPanel.Edits(d.Id) && StructureStampRules.ReadsMarkerSettings(_target, d.Id), // #3523
            _ => d.Kind == "block", // the port and the keep brush paint a block
        };

        /// <summary>A cell's colour with its marks: a docking port reads cyan (#1877), a kept cell green.</summary>
        private static Color MarkedColor(Color baseCol, CellData data)
        {
            if (!string.IsNullOrEmpty(data.Port))
            {
                baseCol = Color.Lerp(baseCol, PortColor, 0.6f);
            }

            return data.Keep ? Color.Lerp(baseCol, KeepColor, 0.55f) : baseCol;
        }

        private void ToggleKeep()
        {
            if (!TryGetHitCell(out var cell) || !_design.TryGetValue(cell, out var d) || d.Kind != "block")
            {
                return;
            }

            d.Keep = !d.Keep;
            PlaceCellData(cell, FindPalette(d.Id, d.Kind), d);
            SetStatus(L(d.Keep ? "ui.ed.landmark.kept" : "ui.ed.landmark.unkept"));
        }

        private void EditMarker()
        {
            if (!TryGetHitCell(out var cell) || !_design.TryGetValue(cell, out var d) || !ToolFits("settings", d))
            {
                SetStatus(L("ui.ed.marker.none"));
                return;
            }

            _markerPanel?.Close();
            _markerPanel = EditorMarkerPanel.Show(Shell, _canvas.transform, d.Id, d.Config, settings =>
            {
                if (_design.TryGetValue(cell, out var now) && now.Id == d.Id)
                {
                    now.Config = settings;
                    _design[cell] = now;
                    SetStatus(L("ui.ed.device.saved"));
                }
            }, () => _markerPanel = null);
        }

        // ---------------------------------------------------------------- the form

        private float BuildLandmarkRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.ed.landmark.title"));
            y += 30f;
            y = LandmarkToggle(p, y, "ui.ed.landmark.ruin", _lmRuin, () => _lmRuin = !_lmRuin);
            if (_lmRuin)
            {
                RowLabel(p, y, L("ui.ed.landmark.profile"));
                ValueLabel(p, y, L("ui.ed.landmark.profile." + _lmProfile));
                UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
                {
                    int i = Array.IndexOf(LandmarkRules.Profiles, _lmProfile);
                    _lmProfile = LandmarkRules.Profiles[(i + 1) % LandmarkRules.Profiles.Length];
                    RebuildUi();
                });
                y += ValueRowStep;
                y = LandmarkNumber(p, y, "ui.ed.landmark.strength",
                    _lmStrength <= 0f ? L("ui.ed.landmark.strength_auto") : Mathf.RoundToInt(_lmStrength * 100f) + " %",
                    () => _lmStrength = Mathf.Max(0f, Mathf.Round((_lmStrength - 0.1f) * 10f) / 10f),
                    () => _lmStrength = Mathf.Min(1f, Mathf.Round((_lmStrength + 0.1f) * 10f) / 10f));
            }

            y = LandmarkNumber(p, y, "ui.ed.landmark.ground_row", _groundRow.ToString(),
                () => _groundRow = Mathf.Max(0, _groundRow - 1), () => _groundRow = Mathf.Min(RoomSize - 1, _groundRow + 1));
            y = LandmarkToggle(p, y, "ui.ed.landmark.atmosphere", _lmAtmosphere, () => _lmAtmosphere = !_lmAtmosphere);
            y = LandmarkToggle(p, y, "ui.ed.landmark.inhabited", _lmInhabited, () => _lmInhabited = !_lmInhabited);
            if (!_lmRuin)
            {
                y = LandmarkToggle(p, y, "ui.ed.landmark.protected", _lmProtected, () => _lmProtected = !_lmProtected);
            }

            FieldLabel(p, y, L("ui.ed.landmark.name_key"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _lmNameKey, v => _lmNameKey = v ?? string.Empty);
            return y + 66f; // the ruin preview is the shared preview section (StructureEditor.Previews.cs, #3523)
        }

        private float LandmarkToggle(Transform p, float y, string key, bool on, Action flip)
        {
            UiKit.AddFitButton(p, 16f, y, 348f, 34f, (on ? "☑ " : "☐ ") + L(key), () =>
            {
                flip();
                RebuildUi();
            });
            return y + 40f;
        }

        private float LandmarkNumber(Transform p, float y, string key, string value, Action less, Action more)
        {
            RowLabel(p, y, L(key));
            UiKit.AddText(p, ValueX, y, 90f, ValueRowH, value, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddFitButton(p, 260f, y + 3f, 40f, RowH, "−", () => { less(); RebuildUi(); });
            UiKit.AddFitButton(p, 310f, y + 3f, 40f, RowH, "+", () => { more(); RebuildUi(); });
            return y + ValueRowStep;
        }

        // ---------------------------------------------------------------- template ↔ design

        private List<CellJson> SnapshotCells()
        {
            var cells = new List<CellJson>(_design.Count);
            foreach (var kv in _design)
            {
                var d = kv.Value;
                cells.Add(new CellJson
                {
                    x = kv.Key.X, y = kv.Key.Y, z = kv.Key.Z,
                    kind = string.IsNullOrEmpty(d.Kind) ? "block" : d.Kind, id = d.Id,
                    tint = d.Tint, glow = d.Glow, shape = d.Shape, port = d.Port ?? string.Empty,
                    mode = d.Kind == "marker" ? 0 : DeviceMode(d),
                    config = d.Kind == "marker" ? d.Config ?? string.Empty : DeviceConfig(d),
                    label = d.Kind == "marker" ? string.Empty : DeviceLabel(d),
                    keep = d.Keep,
                });
            }

            return cells;
        }

        /// <summary>The design (or <paramref name="cells"/>) as a landmark template (cells listed; <see cref="TemplateBody.Compact"/>
        /// packs them for a save).</summary>
        private StructureTemplate BuildLandmarkTemplate(string key, List<CellJson> cells = null)
        {
            var t = new StructureTemplate
            {
                Key = key,
                Name = _name,
                Kind = "landmark",
                Landmark = true,
                Pack = string.IsNullOrWhiteSpace(_pack) ? "default" : Slug(_pack),
                Weight = Mathf.Max(1, _weight),
                PlanetTypes = PlanetTypeList(),
                Ruin = _lmRuin ? LandmarkRules.RuinAlways : LandmarkRules.RuinNever,
                RuinProfile = _lmProfile,
                RuinStrength = _lmStrength,
                GroundRow = _groundRow,
                Protected = !_lmRuin && _lmProtected,
                NameKey = (_lmNameKey ?? string.Empty).Trim(),
            };
            if (_lmAtmosphere)
            {
                t.Requires.Add(LandmarkRules.RequiresAtmosphere);
            }

            if (_lmInhabited)
            {
                t.Requires.Add(LandmarkRules.RequiresInhabited);
            }

            int maxX = 0, maxY = 0, maxZ = 0;
            foreach (var c in cells ?? SnapshotCells())
            {
                t.Cells.Add(new TemplateCell
                {
                    X = c.x, Y = c.y, Z = c.z, Kind = c.kind, Id = c.kind == LandmarkRules.CavityKind ? string.Empty : c.id,
                    Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port, Mode = c.mode, Config = c.config, Label = c.label, Keep = c.keep,
                });
                maxX = Mathf.Max(maxX, c.x);
                maxY = Mathf.Max(maxY, c.y);
                maxZ = Mathf.Max(maxZ, c.z);
            }

            t.Width = maxX + 1;
            t.Height = maxY + 1;
            t.Length = maxZ + 1;
            return t;
        }

        private static CellJson ToCellJson(TemplateCell c) => new CellJson
        {
            x = c.X, y = c.Y, z = c.Z, kind = c.Kind, id = c.Kind == LandmarkRules.CavityKind ? LandmarkRules.CavityKind : c.Id,
            tint = c.Tint, glow = c.Glow, shape = c.Shape, port = c.Port ?? string.Empty,
            mode = c.Mode, config = c.Config ?? string.Empty, label = c.Label ?? string.Empty, keep = c.Keep,
        };

        // ---------------------------------------------------------------- the ruin preview

        private string PreviewSurface()
        {
            string surface = Slug(_surface);
            return string.IsNullOrEmpty(surface) || Shell?.Content?.GetBlock(surface) == null ? "stone" : surface;
        }

        /// <summary>The cells of a built landmark: its blocks by key, its cavities, the markers that survived (the ruin preview,
        /// StructureEditor.Previews.cs).</summary>
        private List<CellJson> RuinCells(LandmarkLayout layout)
        {
            var s = layout.Structure;
            var cells = new List<CellJson>();
            var keys = new Dictionary<ushort, string>();
            for (int x = 0; x < s.Width; x++)
            {
                for (int y = 0; y < s.Height; y++)
                {
                    for (int z = 0; z < s.Length; z++)
                    {
                        ushort id = s.Get(x, y, z);
                        if (id == 0)
                        {
                            if (layout.IsCavity(x, y, z))
                            {
                                cells.Add(new CellJson { x = x, y = y, z = z, kind = LandmarkRules.CavityKind, id = LandmarkRules.CavityKind });
                            }

                            continue;
                        }

                        if (!keys.TryGetValue(id, out string key))
                        {
                            key = Shell.Content.BlockById(new BlockId(id))?.Key ?? string.Empty;
                            keys[id] = key;
                        }

                        var (tint, glow) = s.GetModifier(x, y, z);
                        cells.Add(new CellJson { x = x, y = y, z = z, kind = "block", id = key, tint = tint, glow = glow, shape = s.GetShape(x, y, z) });
                    }
                }
            }

            foreach (var m in s.Markers)
            {
                cells.Add(new CellJson { x = m.LocalPos.X, y = m.LocalPos.Y, z = m.LocalPos.Z, kind = "marker", id = m.Type, config = m.Data ?? string.Empty });
            }

            return cells;
        }

        // ---------------------------------------------------------------- save and load

        /// <summary>Saves the landmark: the compact template into the user-content folder (the local server offers it to
        /// the next new world) and the same file as an export bundle for <c>tools/merge_structure.py</c>.</summary>
        private void ExportLandmark(string key)
        {
            if (_previewOn)
            {
                SetStatus(L("ui.ed.landmark.preview_locked"));
                return;
            }

            var t = BuildLandmarkTemplate(key);
            var problems = LandmarkRules.Problems(t);
            if (_design.Count == 0 || problems.Count > 0)
            {
                SetStatus(_design.Count == 0 ? L("ui.submit.empty") : string.Format(L("ui.ed.landmark.problem"), problems[0]));
                return;
            }

            try
            {
                TemplateBody.Compact(t);
                string json = JsonUtility.ToJson(ToLandmarkJson(t), false);
                Directory.CreateDirectory(UserLandmarksRoot);
                File.WriteAllText(Path.Combine(UserLandmarksRoot, key + ".json"), json);
                string dir = Path.Combine(LandmarkExportsRoot, key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "landmark.json"), json);
                SetStatus(string.Format(L("ui.ed.landmark.saved"), key, _design.Count, json.Length / 1024) + SaveWarnings()); // #3523
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Landmark export failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"));
            }
        }

        private static LandmarkJson ToLandmarkJson(StructureTemplate t)
        {
            var json = new LandmarkJson
            {
                key = t.Key, name = t.Name, pack = t.PackOrDefault, weight = t.Weight, planetTypes = new List<string>(t.PlanetTypes),
                ruin = t.Ruin, ruinProfile = t.RuinProfile, ruinStrength = (float)t.RuinStrength, requires = new List<string>(t.Requires),
                groundRow = t.GroundRow, @protected = t.Protected, nameKey = t.NameKey,
                width = t.Width, height = t.Height, length = t.Length, rows = new List<string>(t.Rows),
            };
            foreach (var p in t.Palette)
            {
                json.palette.Add(new PaletteJson { kind = p.Kind, id = p.Id, tint = p.Tint, glow = p.Glow, shape = p.Shape, keep = p.Keep });
            }

            foreach (var c in t.Cells)
            {
                json.cells.Add(ToCellJson(c));
            }

            return json;
        }

        private static StructureTemplate FromLandmarkJson(LandmarkJson j)
        {
            var t = new StructureTemplate
            {
                Key = j.key ?? string.Empty, Name = j.name ?? string.Empty, Kind = "landmark", Landmark = true,
                Pack = string.IsNullOrEmpty(j.pack) ? "default" : j.pack, Weight = Mathf.Max(1, j.weight),
                PlanetTypes = j.planetTypes ?? new List<string>(), Ruin = j.ruin ?? string.Empty, RuinProfile = j.ruinProfile ?? string.Empty,
                RuinStrength = j.ruinStrength, Requires = j.requires ?? new List<string>(), GroundRow = j.groundRow, Protected = j.@protected,
                NameKey = j.nameKey ?? string.Empty, Width = j.width, Height = j.height, Length = j.length, Rows = j.rows ?? new List<string>(),
            };
            foreach (var p in j.palette ?? new List<PaletteJson>())
            {
                t.Palette.Add(new TemplatePaletteEntry { Kind = p.kind ?? "block", Id = p.id ?? string.Empty, Tint = p.tint, Glow = p.glow, Shape = p.shape, Keep = p.keep });
            }

            foreach (var c in j.cells ?? new List<CellJson>())
            {
                t.Cells.Add(new TemplateCell
                {
                    X = c.x, Y = c.y, Z = c.z, Kind = c.kind ?? "block", Id = c.id ?? string.Empty, Tint = c.tint, Glow = c.glow, Shape = c.shape,
                    Port = c.port ?? string.Empty, Mode = c.mode, Config = c.config ?? string.Empty, Label = c.label ?? string.Empty, Keep = c.keep,
                });
            }

            return t;
        }

        /// <summary>The LOAD dialog's landmarks (town editor only): the shipped ones as copies, then the user's own.</summary>
        private void AddLandmarkItems(EditorLoadPicker.Section builtIn, EditorLoadPicker.Section user)
        {
            if (EditorMode != Mode.Settlement)
            {
                return;
            }

            if (Shell?.Content != null)
            {
                foreach (var t in Shell.Content.Landmarks)
                {
                    if (t.IsSource)
                    {
                        continue; // #3523: a generator entry is listed by AddEntryItems
                    }

                    var template = t;
                    builtIn.Items.Add(new EditorLoadPicker.Item
                    {
                        Label = "♜ " + (string.IsNullOrEmpty(t.Name) ? t.Key : t.Name),
                        Detail = Detail(string.Empty, t.Width, t.Length, t.Height, TemplateBody.CellCount(t)),
                        Load = () => LoadLandmark(template, copy: true),
                    });
                }
            }

            AddUserLandmarkItems(user);
        }

        /// <summary>The user's own landmarks of <c>usercontent/landmark_templates/</c>, in file-name order.</summary>
        private void AddUserLandmarkItems(EditorLoadPicker.Section user)
        {
            if (!Directory.Exists(UserLandmarksRoot))
            {
                return;
            }

            var files = Directory.GetFiles(UserLandmarksRoot, "*.json");
            Array.Sort(files, string.CompareOrdinal);
            foreach (var file in files)
            {
                StructureTemplate t;
                try
                {
                    t = FromLandmarkJson(JsonUtility.FromJson<LandmarkJson>(File.ReadAllText(file)));
                }
                catch (Exception)
                {
                    continue; // one unreadable file must not hide the rest
                }

                if (t.Rows.Count == 0 && t.Cells.Count == 0)
                {
                    continue; // #3523: a generator entry (no cells) is listed by AddEntryItems
                }

                if (string.IsNullOrEmpty(t.Key))
                {
                    t.Key = Path.GetFileNameWithoutExtension(file);
                }

                var template = t;
                user.Items.Add(new EditorLoadPicker.Item
                {
                    Label = "♜ " + (string.IsNullOrEmpty(t.Name) ? t.Key : t.Name),
                    Detail = Detail(string.Empty, t.Width, t.Length, t.Height, TemplateBody.CellCount(t)),
                    Load = () => LoadLandmark(template, copy: false),
                });
            }
        }

        /// <summary>Loads a landmark into the room: its cells (the compact body expanded) and its form.</summary>
        private void LoadLandmark(StructureTemplate source, bool copy)
        {
            var cells = new List<CellJson>();
            TemplateBody.ForEachPlain(new StructureTemplate { Palette = source.Palette, Rows = source.Rows }, p => cells.Add(new CellJson
            {
                x = p.X, y = p.Y, z = p.Z, kind = p.Cavity ? LandmarkRules.CavityKind : "block", id = p.Cavity ? LandmarkRules.CavityKind : p.Id,
                tint = p.Tint, glow = p.Glow, shape = p.Shape, keep = p.Keep,
            }));
            foreach (var c in source.Cells)
            {
                cells.Add(ToCellJson(c));
            }

            _previewOn = false;
            _previewBackup = null;
            ApplyTemplate(source.Key, source.Name, string.Empty, source.PackOrDefault, source.Weight, source.PlanetTypes, cells, copy);
            _moduleMode = false;
            _landmarkMode = true;
            _lmRuin = LandmarkRules.IsRuin(source);
            _lmProfile = LandmarkRules.ProfileOf(source);
            _lmStrength = (float)source.RuinStrength;
            _lmAtmosphere = LandmarkRules.Requires(source, LandmarkRules.RequiresAtmosphere);
            _lmInhabited = LandmarkRules.Requires(source, LandmarkRules.RequiresInhabited);
            _lmProtected = source.Protected;
            _groundRow = source.GroundRow;
            _lmNameKey = source.NameKey ?? string.Empty;
            RebuildUi();
        }
    }
}
