// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// CIVILIZATION templates in the town editor (#3646, docs/developer/CIVILIZATIONS.md): a civilization's site or prop is a
    /// landmark-style template that names a civilization and a pool, so the landmark form edits it.
    /// <list type="bullet">
    /// <item><b>Load</b> lists the shipped civilization templates (as copies) and the user's own saved ones. The fields the form
    /// has no rows for (<see cref="CivilizationFields"/>: the civilization, the pool, a prop's prop pool, the file's protection)
    /// and the sea-floor seat go from the loaded file to the saved one unchanged.</item>
    /// <item><b>Cells</b>: an air pocket loads as a cell of the <i>air pocket</i> brush (kind <see cref="CivilizationRules.AirKind"/>)
    /// and saves as one again — never as a block or a plain cavity (<see cref="CivilizationEditorRules"/>); a prop marker loads
    /// as the <i>prop</i> marker, its pool or template kept and set with the settings tool. A marker standing in a cavity or an
    /// air pocket (a chest in a diving bell or a carved tunnel) keeps its place, and the carve under it is written back on save
    /// (<see cref="MarkerCarves"/>; landmarks and giant ships too).</item>
    /// <item><b>Save</b> checks it as the content loader will (<see cref="CivilizationEditorRules.SaveProblems"/>) and writes
    /// <c>civilization_exports/&lt;key&gt;/civilization.json</c> for <c>tools/merge_structure.py</c> — never the landmark folder,
    /// whose pool would offer it as a landmark.</item>
    /// </list>
    /// </summary>
    public sealed partial class StructureEditor
    {
        private static readonly Color AirColor = new Color(0.8f, 0.97f, 1f);
        private static readonly Color PropColor = new Color(0.85f, 0.75f, 0.55f);

        /// <summary>The loaded civilization template's fields (empty: the design is no civilization template).</summary>
        private CivilizationFields _civ = new();

        /// <summary>The cavities and air pockets under the loaded template's markers — the design holds the marker there, a save
        /// writes the carve back (<see cref="MarkerCarves"/>).</summary>
        private Dictionary<(int X, int Y, int Z), TemplateBody.Plain> _carves = new();

        /// <summary>Whether the design is a civilization template in the landmark form.</summary>
        private bool CivilizationMode => _landmarkMode && _civ.IsSet;

        private string CivilizationExportsRoot => Path.Combine(AppPaths.Root, CivilizationEditorRules.ExportsFolder);

        /// <summary>Forgets what the last loaded template carried (a landmark or giant ship load sets it again).</summary>
        private void ResetCivilizationExtras()
        {
            _civ = new CivilizationFields();
            _carves = new Dictionary<(int X, int Y, int Z), TemplateBody.Plain>();
        }

        /// <summary>A landmark's, civilization template's or giant ship's cells for the design: the compact body expanded, then the
        /// listed cells — an air pocket as an air pocket, never a plain cavity (<see cref="CivilizationEditorRules"/>) — and a
        /// marker standing in a cavity or an air pocket in its own place, the carve under it returned in <paramref name="carves"/>
        /// for the save (the design holds one cell per place; it used to skip the marker).</summary>
        private static List<CellJson> TemplateDesignCells(StructureTemplate source, out Dictionary<(int X, int Y, int Z), TemplateBody.Plain> carves)
        {
            var under = MarkerCarves.Under(source);
            var cells = new List<CellJson>();
            TemplateBody.ForEachPlain(new StructureTemplate { Palette = source.Palette, Rows = source.Rows }, p =>
            {
                if (!under.ContainsKey((p.X, p.Y, p.Z)))
                {
                    cells.Add(new CellJson
                    {
                        x = p.X, y = p.Y, z = p.Z, kind = CivilizationEditorRules.DesignKind(p), id = CivilizationEditorRules.DesignId(p),
                        tint = p.Tint, glow = p.Glow, shape = p.Shape, keep = p.Keep,
                    });
                }
            });
            foreach (var c in source.Cells)
            {
                bool carve = c.Kind == LandmarkRules.CavityKind || c.Kind == CivilizationRules.AirKind;
                if (!carve || !under.ContainsKey((c.X, c.Y, c.Z)))
                {
                    cells.Add(ToCellJson(c));
                }
            }

            carves = under;
            return cells;
        }

        /// <summary>The air-pocket brush and the prop marker (the palette shows them for a civilization template only).</summary>
        private EditorPaletteKit.Entry[] CivilizationTools() => new[]
        {
            Tool(CivilizationEditorRules.AirPocketId, StructureStampRules.KindAir, AirColor),
            M(CivilizationRules.PropMarker, PropColor),
        };

        /// <summary>The LOAD dialog's civilization templates (town editor only): the shipped ones as copies — generator entries
        /// left out —, then the user's own export bundles.</summary>
        private void AddCivilizationItems(EditorLoadPicker.Section builtIn, EditorLoadPicker.Section user)
        {
            if (EditorMode != Mode.Settlement)
            {
                return;
            }

            if (Shell?.Content != null)
            {
                foreach (var t in Shell.Content.CivilizationTemplates)
                {
                    if (!t.IsSource)
                    {
                        var template = t;
                        builtIn.Items.Add(CivilizationItem(t, () => LoadLandmark(template, copy: true)));
                    }
                }
            }

            if (!Directory.Exists(CivilizationExportsRoot))
            {
                return;
            }

            var dirs = Directory.GetDirectories(CivilizationExportsRoot);
            Array.Sort(dirs, string.CompareOrdinal);
            foreach (var dir in dirs)
            {
                if (ReadCivilizationBundle(Path.Combine(dir, CivilizationEditorRules.BundleFile)) is { } t)
                {
                    user.Items.Add(CivilizationItem(t, () => LoadLandmark(t, copy: false)));
                }
            }
        }

        /// <summary>A saved bundle as a template, or null when it is missing, unreadable, empty or names no civilization.</summary>
        private static StructureTemplate ReadCivilizationBundle(string file)
        {
            StructureTemplate t;
            try
            {
                if (!File.Exists(file))
                {
                    return null;
                }

                t = FromLandmarkJson(JsonUtility.FromJson<LandmarkJson>(File.ReadAllText(file)));
            }
            catch (Exception)
            {
                return null; // one unreadable bundle must not hide the rest
            }

            if (string.IsNullOrEmpty(t.Civilization) || (t.Rows.Count == 0 && t.Cells.Count == 0))
            {
                return null;
            }

            if (string.IsNullOrEmpty(t.Key))
            {
                t.Key = Path.GetFileName(Path.GetDirectoryName(file));
            }

            return t;
        }

        /// <summary>A LOAD row: the name, then the civilization's name with the size.</summary>
        private EditorLoadPicker.Item CivilizationItem(StructureTemplate t, Action load)
        {
            var civ = Shell?.Content?.GetCivilization(t.Civilization);
            return new EditorLoadPicker.Item
            {
                Label = "♜ " + (string.IsNullOrEmpty(t.Name) ? t.Key : t.Name),
                Detail = Detail(civ != null ? L(civ.NameKey) : t.Civilization, t.Width, t.Length, t.Height, TemplateBody.CellCount(t)),
                Load = load,
            };
        }

        /// <summary>Writes a civilization template's file into its export bundle for <c>tools/merge_structure.py</c>.</summary>
        private void WriteCivilizationBundle(string key, string json)
        {
            string dir = Path.Combine(CivilizationExportsRoot, key);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, CivilizationEditorRules.BundleFile), json);
            Saved(string.Format(L("ui.ed.civ.saved"), key, _design.Count, json.Length / 1024)); // with the save warnings
        }
    }
}
