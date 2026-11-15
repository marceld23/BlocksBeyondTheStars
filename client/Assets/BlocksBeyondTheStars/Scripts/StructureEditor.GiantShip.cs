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
    /// The structure editor's GIANT SHIP mode (#3616): "Use as" in the town editor gets a value after the landmark — a huge
    /// spaceship (up to <see cref="GiantShipRules.MaxSize"/> blocks a side, the town editor's room) that nobody can build. The
    /// game knows it only as a wreck: crashed on a planet and drifting in space, derived from this intact design with the
    /// instance's seed (<see cref="GiantShipGenerator"/>).
    /// <list type="bullet">
    /// <item><b>The form</b>: the style (metal, or an alien crystal ship whose rubble is crystal), the strength of the decay
    /// (0 = the ruin pass's own), the ground row (on a planet the rows below it are sunk into the rock — the keel), the name key;
    /// weight, pack and planet types are the rows every town template has. A wreck names no protection: players may always take
    /// it apart.</item>
    /// <item><b>The palette</b> shows the giant ship's markers only (<see cref="GiantShipRules.Markers"/>: the way in, the
    /// doorways, chests, sounds and the loot markers) plus the keep, cavity and settings tools
    /// (<see cref="StructureStampRules.ShowsInPalette"/>).</item>
    /// <item><b>Show as a wreck</b>: on a planet (<see cref="GiantShipGenerator.BuildPlanet"/>, ruin profile crash) or in space
    /// (<see cref="GiantShipGenerator.BuildSpace"/>, profile hull) with the preview seed — read-only like the landmark's ruin
    /// preview, never saved.</item>
    /// <item><b>Save</b> gives every slide door without an axis the one its walls tell (<see cref="GiantShipEditorRules"/>), checks
    /// the ship as the server will (<c>GameContent.GiantShipProblems</c>), then writes the compact template to
    /// <c>usercontent/giant_ship_templates/</c> (the local server offers it to the next new world) and an export bundle
    /// <c>giant_ship_exports/&lt;key&gt;/giant_ship.json</c> for <c>tools/merge_structure.py</c>.</item>
    /// </list>
    /// The fields the form does not show — a loaded file's ruin zones and minimum terrain generation — go from the loaded file
    /// to the saved one unchanged.
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The giant ship file (the server's <see cref="StructureTemplate"/> JSON with <c>giantShip: true</c>, compact
        /// body included) — what <c>data/giant_ships/*.json</c> and the user folder hold.</summary>
        [Serializable] private sealed class GiantShipJson
        {
            public string key, name, kind = GiantShipRules.TemplateKind, pack = "default", nameKey = string.Empty, style = string.Empty;
            public bool giantShip = true;
            public int weight = 1;
            public List<string> planetTypes = new();
            public int groundRow;
            public double ruinStrength;
            public List<ZoneJson> ruinZones = new();
            public int minTerrainGeneration; // no form row: kept from the loaded file
            public int width, height, length;
            public List<PaletteJson> palette = new();
            public List<string> rows = new();
            public List<CellJson> cells = new();
        }

        private bool _giantShipMode;
        private string _gsStyle = string.Empty;
        private float _gsStrength; // 0 = the ruin pass's own
        private string _gsNameKey = string.Empty;
        private readonly List<RuinZone> _gsZones = new(); // a loaded file's, written back unchanged
        private int _gsMinGeneration;
        private bool _wreckInSpace; // the wreck preview shows the space wreck (else the planet one)

        private string GiantShipExportsRoot => Path.Combine(AppPaths.Root, "giant_ship_exports");

        private string UserGiantShipsRoot => Path.Combine(AppPaths.Root, "usercontent", GiantShipRules.UserFolder);

        private bool GiantShipAlien => string.Equals(_gsStyle, GiantShipRules.StyleAlien, StringComparison.OrdinalIgnoreCase);

        /// <summary>Forgets the giant ship fields (a load sets them again in <see cref="LoadGiantShip"/>).</summary>
        private void ResetGiantShipExtras()
        {
            _giantShipMode = false;
            _gsStyle = string.Empty;
            _gsStrength = 0f;
            _gsNameKey = string.Empty;
            _gsZones.Clear();
            _gsMinGeneration = 0;
        }

        // ---------------------------------------------------------------- the form

        /// <summary>The title, what a giant ship is, the style, the strength of decay, the ground row and the name key.</summary>
        private float BuildGiantShipRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.ed.giantship.title"));
            y += 30f;
            UiKit.AddMeasuredText(p, 16f, y, 348f, L("ui.ed.giantship.hint"), 13, UiKit.CyanDim, out float h);
            y += h + 8f;
            RowLabel(p, y, L("ui.ed.giantship.style"));
            ValueLabel(p, y, L(GiantShipAlien ? "ui.ed.giantship.style.alien" : "ui.ed.giantship.style.normal"));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                _gsStyle = GiantShipAlien ? string.Empty : GiantShipRules.StyleAlien;
                RebuildUi();
            });
            y += ValueRowStep;
            y = LandmarkNumber(p, y, "ui.ed.landmark.strength",
                _gsStrength <= 0f ? L("ui.ed.landmark.strength_auto") : Mathf.RoundToInt(_gsStrength * 100f) + " %",
                () => _gsStrength = Mathf.Max(0f, Mathf.Round((_gsStrength - 0.1f) * 10f) / 10f),
                () => _gsStrength = Mathf.Min(1f, Mathf.Round((_gsStrength + 0.1f) * 10f) / 10f));
            y = LandmarkNumber(p, y, "ui.ed.landmark.ground_row", _groundRow.ToString(),
                () => _groundRow = Mathf.Max(0, _groundRow - 1), () => _groundRow = Mathf.Min(RoomSize - 1, _groundRow + 1));
            FieldLabel(p, y, L("ui.ed.landmark.name_key"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _gsNameKey, v => _gsNameKey = v ?? string.Empty);
            return y + 66f;
        }

        // ---------------------------------------------------------------- the wreck preview

        /// <summary>The preview section's giant ship rows (StructureEditor.Previews.cs): the seed and the two wrecks.</summary>
        private float BuildWreckPreviewRows(Transform p, float y)
        {
            y = BuildRuinSeedRow(p, y);
            FieldLabel(p, y, L("ui.ed.giantship.show_wreck"));
            y += 26f;
            UiKit.AddFitButton(p, 16f, y, 168f, 34f, L("ui.ed.giantship.on_planet"), () => ShowWreckPreview(space: false));
            UiKit.AddFitButton(p, 196f, y, 168f, 34f, L("ui.ed.giantship.in_space"), () => ShowWreckPreview(space: true));
            return y + 40f;
        }

        /// <summary>The wreck the server would derive with the preview seed: on a planet with air (not turned) or in space.</summary>
        private void ShowWreckPreview(bool space)
        {
            if (Shell?.Content == null)
            {
                return;
            }

            var design = DesignCells();
            if (design.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            var t = BuildGiantShipTemplate("preview", design, null);
            var cells = space
                ? SpaceWreckCells(GiantShipGenerator.BuildSpace(t, _previewSeed, Shell.Content))
                : RuinCells(GiantShipGenerator.BuildPlanet(t, _previewSeed, 0, Shell.Content, PreviewSurface(), atmosphere: true));
            ShowPreview(PreviewKind.Wreck, cells);
            _wreckInSpace = space;
            _status = string.Format(L("ui.ed.giantship.preview_shown"), L(space ? "ui.ed.giantship.in_space" : "ui.ed.giantship.on_planet"),
                _previewSeed, _design.Count, design.Count);
            RebuildUi();
        }

        /// <summary>The cells of a wreck in space: its blocks by key with their dye, glow and shape, then the markers it kept.</summary>
        private List<CellJson> SpaceWreckCells(GiantSpaceLayout s)
        {
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

        // ---------------------------------------------------------------- template ↔ design

        /// <summary>The design (or <paramref name="cells"/>) as a giant ship template — cells listed (<see cref="TemplateBody.Compact"/>
        /// packs them for a save). A slide door without an axis gets the one its walls tell; one whose walls tell none is added to
        /// <paramref name="unclearDoors"/> when given.</summary>
        private StructureTemplate BuildGiantShipTemplate(string key, List<CellJson> cells, List<Vector3i> unclearDoors)
        {
            var t = new StructureTemplate
            {
                Key = key,
                Name = _name,
                Kind = GiantShipRules.TemplateKind,
                GiantShip = true,
                Pack = string.IsNullOrWhiteSpace(_pack) ? "default" : Slug(_pack),
                Weight = Mathf.Max(1, _weight),
                PlanetTypes = PlanetTypeList(),
                Style = GiantShipAlien ? GiantShipRules.StyleAlien : string.Empty,
                GroundRow = _groundRow,
                RuinStrength = _gsStrength,
                NameKey = (_gsNameKey ?? string.Empty).Trim(),
                MinTerrainGeneration = _gsMinGeneration,
                RuinZones = CopyZones(_gsZones),
            };

            var solid = new HashSet<Vector3i>();
            foreach (var c in cells)
            {
                if (string.IsNullOrEmpty(c.kind) || c.kind == "block")
                {
                    solid.Add(new Vector3i(c.x, c.y, c.z)); // a wall of the doorway (markers and cavities are not)
                }
            }

            int maxX = 0, maxY = 0, maxZ = 0;
            foreach (var c in cells)
            {
                t.Cells.Add(GiantShipCell(c, solid, unclearDoors));
                maxX = Mathf.Max(maxX, c.x);
                maxY = Mathf.Max(maxY, c.y);
                maxZ = Mathf.Max(maxZ, c.z);
            }

            t.Width = maxX + 1;
            t.Height = maxY + 1;
            t.Length = maxZ + 1;
            MarkerCarves.WriteBack(t, _carves); // #3646: the cavity under a chest in a carved room
            return t;
        }

        /// <summary>One template cell of a giant ship: a cavity without a block id, a slide door with its axis.</summary>
        private static TemplateCell GiantShipCell(CellJson c, HashSet<Vector3i> solid, List<Vector3i> unclearDoors)
        {
            string kind = string.IsNullOrEmpty(c.kind) ? "block" : c.kind;
            string config = c.config ?? string.Empty;
            if (kind == "marker" && c.id == GiantShipRules.DoorMarker)
            {
                config = GiantShipEditorRules.WithDoorAxis(config, (x, y, z) => solid.Contains(new Vector3i(x, y, z)), c.x, c.y, c.z, out bool unclear);
                if (unclear)
                {
                    unclearDoors?.Add(new Vector3i(c.x, c.y, c.z));
                }
            }

            return new TemplateCell
            {
                X = c.x, Y = c.y, Z = c.z, Kind = kind, Id = CivilizationEditorRules.TemplateId(kind, c.id), // a cavity, an air pocket: ""
                Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty, Mode = c.mode, Config = config,
                Label = c.label ?? string.Empty, Keep = c.keep,
            };
        }

        private static List<RuinZone> CopyZones(IEnumerable<RuinZone> zones)
        {
            var list = new List<RuinZone>();
            foreach (var z in zones ?? Array.Empty<RuinZone>())
            {
                if (z != null)
                {
                    list.Add(new RuinZone { X0 = z.X0, Y0 = z.Y0, Z0 = z.Z0, X1 = z.X1, Y1 = z.Y1, Z1 = z.Z1, Profile = z.Profile ?? string.Empty, Strength = z.Strength });
                }
            }

            return list;
        }

        // ---------------------------------------------------------------- save

        /// <summary>Saves the giant ship: the doors' axes, the server's own check, then the compact template into the user-content
        /// folder and the same file as an export bundle for <c>tools/merge_structure.py</c>.</summary>
        private void ExportGiantShip(string key)
        {
            if (_design.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            if (RefuseUnreadTokens())
            {
                return; // a giant ship names concrete blocks: a material token would be air
            }

            var unclear = new List<Vector3i>();
            var t = BuildGiantShipTemplate(key, SnapshotCells(), unclear);
            if (unclear.Count > 0)
            {
                SetStatus(string.Format(L("ui.ed.giantship.door_unclear"), CellText(unclear[0])));
                return;
            }

            var problems = Shell?.Content != null ? Shell.Content.GiantShipProblems(t) : GiantShipRules.Problems(t, _ => true);
            if (problems.Count > 0)
            {
                SetStatus(string.Format(L("ui.ed.landmark.problem"), problems[0]));
                return;
            }

            try
            {
                TemplateBody.Compact(t);
                string json = JsonUtility.ToJson(ToGiantShipJson(t), false);
                Directory.CreateDirectory(UserGiantShipsRoot);
                File.WriteAllText(Path.Combine(UserGiantShipsRoot, key + ".json"), json);
                string dir = Path.Combine(GiantShipExportsRoot, key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "giant_ship.json"), json);
                SetStatus(string.Format(L("ui.ed.giantship.saved"), key, _design.Count, json.Length / 1024) + SaveWarnings());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StructureEditor] Giant ship export failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"));
            }
        }

        private static GiantShipJson ToGiantShipJson(StructureTemplate t)
        {
            var json = new GiantShipJson
            {
                key = t.Key, name = t.Name, pack = t.PackOrDefault, nameKey = t.NameKey ?? string.Empty, style = t.Style ?? string.Empty,
                weight = t.Weight, planetTypes = new List<string>(t.PlanetTypes), groundRow = t.GroundRow, ruinStrength = t.RuinStrength,
                ruinZones = ToZoneJsons(t.RuinZones), minTerrainGeneration = t.MinTerrainGeneration,
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

        private static StructureTemplate FromGiantShipJson(GiantShipJson j)
        {
            var t = new StructureTemplate
            {
                Key = j.key ?? string.Empty, Name = j.name ?? string.Empty, Kind = GiantShipRules.TemplateKind, GiantShip = true,
                Pack = string.IsNullOrEmpty(j.pack) ? "default" : j.pack, Weight = Mathf.Max(1, j.weight),
                PlanetTypes = j.planetTypes ?? new List<string>(), NameKey = j.nameKey ?? string.Empty, Style = j.style ?? string.Empty,
                GroundRow = j.groundRow, RuinStrength = j.ruinStrength, RuinZones = FromZoneJsons(j.ruinZones),
                MinTerrainGeneration = j.minTerrainGeneration, Width = j.width, Height = j.height, Length = j.length,
                Rows = j.rows ?? new List<string>(),
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

        // ---------------------------------------------------------------- load

        /// <summary>The LOAD dialog's giant ships (town editor only): the shipped ones as copies, then the user's own.</summary>
        private void AddGiantShipItems(EditorLoadPicker.Section builtIn, EditorLoadPicker.Section user)
        {
            if (EditorMode != Mode.Settlement)
            {
                return;
            }

            if (Shell?.Content != null)
            {
                foreach (var t in Shell.Content.GiantShips)
                {
                    var template = t;
                    builtIn.Items.Add(GiantShipItem(t, () => LoadGiantShip(template, copy: true)));
                }
            }

            if (!Directory.Exists(UserGiantShipsRoot))
            {
                return;
            }

            var files = Directory.GetFiles(UserGiantShipsRoot, "*.json");
            Array.Sort(files, string.CompareOrdinal);
            foreach (var file in files)
            {
                StructureTemplate t;
                try
                {
                    t = FromGiantShipJson(JsonUtility.FromJson<GiantShipJson>(File.ReadAllText(file)));
                }
                catch (Exception)
                {
                    continue; // one unreadable file must not hide the rest
                }

                if (t.Rows.Count == 0 && t.Cells.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(t.Key))
                {
                    t.Key = Path.GetFileNameWithoutExtension(file);
                }

                var template = t;
                user.Items.Add(GiantShipItem(t, () => LoadGiantShip(template, copy: false)));
            }
        }

        private EditorLoadPicker.Item GiantShipItem(StructureTemplate t, Action load) => new EditorLoadPicker.Item
        {
            Label = "◆ " + (string.IsNullOrEmpty(t.Name) ? t.Key : t.Name),
            Detail = Detail(string.Empty, t.Width, t.Length, t.Height, TemplateBody.CellCount(t)),
            Load = load,
        };

        /// <summary>Loads a giant ship into the room: its cells (the compact body expanded) and its form.</summary>
        private void LoadGiantShip(StructureTemplate source, bool copy)
        {
            var cells = TemplateDesignCells(source, out var carves); // #3646: air pockets, markers in a cavity (StructureEditor.Civilization.cs)
            ApplyTemplate(source.Key, source.Name, string.Empty, source.PackOrDefault, source.Weight, source.PlanetTypes, cells, copy);
            _carves = carves;
            _moduleMode = false;
            _giantShipMode = true;
            _gsStyle = GiantShipGenerator.IsAlien(source) ? GiantShipRules.StyleAlien : string.Empty;
            _gsStrength = (float)source.RuinStrength;
            _gsNameKey = source.NameKey ?? string.Empty;
            _gsZones.AddRange(CopyZones(source.RuinZones));
            _gsMinGeneration = source.MinTerrainGeneration;
            _groundRow = source.GroundRow;
            RebuildUi();
        }
    }
}
