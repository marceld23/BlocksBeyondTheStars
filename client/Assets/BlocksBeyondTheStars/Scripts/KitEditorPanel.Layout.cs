// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The kit panel's layout rows (#3600). A settlement kit picks its layout with one stepper — the classic plot grid, a
    /// grown grid or grown — and the panel shows only that layout's rows, so it grows by one row at most: the plot grid keeps
    /// its two rows, a planned layout shows main street, setback, extent and "flat", a grown one also its village forms. A city
    /// kit's district map is a grid of letters to click through, with the grown city's switch and its suburb kit.
    /// </summary>
    internal sealed partial class KitEditorPanel
    {
        /// <summary>The district letters a click walks through, the outline's "no district" last.</summary>
        private const string MapLetters = "PRMHGTO.";

        private string LayoutText(string layout) => L("ui.kit.layout." + (string.IsNullOrEmpty(layout) ? "plots" : layout));

        private float SettlementLayoutRows(Transform panel, float x0, float y, KitJson k)
        {
            Stepper(panel, x0, y, L("ui.kit.layout"), k.layout ?? string.Empty, StructureKit.Layouts, LayoutText, v => { k.layout = v; Rebuild(); });
            y += 40f;
            if (string.IsNullOrEmpty(k.layout))
            {
                IntField(panel, x0, y, L("ui.kit.cols_min"), k.colsMin, v => k.colsMin = v);
                IntField(panel, x0 + IntStep, y, L("ui.kit.cols_max"), k.colsMax, v => k.colsMax = v);
                IntField(panel, x0 + (2f * IntStep), y, L("ui.kit.rows_min"), k.rowsMin, v => k.rowsMin = v);
                IntField(panel, x0 + (3f * IntStep), y, L("ui.kit.rows_max"), k.rowsMax, v => k.rowsMax = v);
                y += 40f;
                IntField(panel, x0, y, L("ui.kit.plot_stride"), k.plotStride, v => k.plotStride = v);
                IntField(panel, x0 + IntStep, y, L("ui.kit.building"), k.building, v => k.building = v);
                IntField(panel, x0 + (2f * IntStep), y, L("ui.kit.storeys"), k.storeys, v => k.storeys = v);
                UiKit.AddFitButton(panel, x0 + (3f * IntStep), y, 192f, 30f, (k.modulesOnly ? "☑ " : "☐ ") + L("ui.kit.modules_only"), () => { k.modulesOnly = !k.modulesOnly; Rebuild(); });
                return y + 40f;
            }

            IntField(panel, x0, y, L("ui.kit.main_street"), k.mainStreet, v => k.mainStreet = Mathf.Clamp(v, 0, 4));
            IntField(panel, x0 + IntStep, y, L("ui.kit.spacing"), k.spacing, v => k.spacing = Mathf.Clamp(v, 0, 4));
            IntField(panel, x0 + (2f * IntStep), y, L("ui.kit.extent"), k.extent, v => k.extent = Mathf.Clamp(v, 0, 128));
            UiKit.AddFitButton(panel, x0 + (3f * IntStep), y, 192f, 30f, (k.flat ? "☑ " : "☐ ") + L("ui.kit.flat"), () => { k.flat = !k.flat; Rebuild(); });
            y += 40f;
            if (k.layout != StructureKit.LayoutGrown)
            {
                return y;
            }

            FieldLabel(panel, x0, y, L("ui.kit.forms"));
            for (int i = 0; i < StructureKit.AllForms.Length; i++)
            {
                string form = StructureKit.AllForms[i];
                bool on = k.forms == null || k.forms.Count == 0 || k.forms.Contains(form);
                UiKit.AddFitButton(panel, x0 + FieldLabelW + 4f + ((i % 3) * FormToggleStep), y + ((i / 3) * 36f), FormToggleW, 30f,
                    (on ? "☑ " : "☐ ") + L("ui.kit.form." + form), () => { ToggleForm(k, form); Rebuild(); });
            }

            return y + 76f;
        }

        /// <summary>The width of one village-form toggle and the step to the next (two rows of three beside the label column — a
        /// Russian "crossroads" needs the full toggle width).</summary>
        internal const float FormToggleW = 192f, FormToggleStep = 200f;

        /// <summary>Switches a form on or off — an empty list means every form, so the first click writes the full list first.</summary>
        private static void ToggleForm(KitJson k, string form)
        {
            k.forms ??= new List<string>();
            if (k.forms.Count == 0)
            {
                k.forms.AddRange(StructureKit.AllForms);
            }

            if (!k.forms.Remove(form))
            {
                k.forms.Add(form);
            }

            if (k.forms.Count == StructureKit.AllForms.Length)
            {
                k.forms.Clear(); // all of them again: the kit lists none
            }
        }

        private float CityMapRows(Transform panel, float x0, float y, KitJson k)
        {
            UiKit.AddFitButton(panel, x0, y, 192f, 30f, (k.grown ? "☑ " : "☐ ") + L("ui.kit.grown_city"), () => { k.grown = !k.grown; Rebuild(); });
            Field(panel, x0 + ColumnStep, y, L("ui.kit.suburb_kit"), k.suburbKit, v => k.suburbKit = EditorSceneKit.Slug(v));
            y += 40f;
            FieldLabel(panel, x0, y, L("ui.kit.role_map"));
            int grid = k.grid > 0 ? Mathf.Clamp(k.grid | 1, 3, 9) : CityGenerator.Modules;
            for (int gz = 0; gz < grid; gz++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    int cx = gx, cz = gz;
                    UiKit.AddFitButton(panel, x0 + FieldLabelW + 4f + (gx * MapCellStep), y + (gz * MapCellStep), MapCellW, MapCellW,
                        MapLetter(k, grid, gx, gz).ToString(), () => { CycleMapCell(k, grid, cx, cz); Rebuild(); });
                }
            }

            float legendX = x0 + FieldLabelW + 12f + (grid * MapCellStep);
            var legend = UiKit.AddText(panel, legendX, y, W - 20f - legendX, grid * MapCellStep, L("ui.kit.map_legend"), 13, UiKit.CyanDim,
                TextAnchor.UpperLeft);
            legend.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + (grid * MapCellStep) + 8f;
        }

        /// <summary>A district map cell's size and the step to the next.</summary>
        internal const float MapCellW = 26f, MapCellStep = 28f;

        /// <summary>The letter of a district: the kit's map, else the composer's own rule.</summary>
        private static char MapLetter(KitJson k, int grid, int gx, int gz)
        {
            if (k.roleMap != null && gz < k.roleMap.Count && gx < k.roleMap[gz].Length)
            {
                return char.ToUpperInvariant(k.roleMap[gz][gx]);
            }

            return CityGenerator.RoleAtFor(gx, gz, grid, null) switch
            {
                CityGenerator.Role.Plaza => 'P',
                CityGenerator.Role.Open => 'O',
                CityGenerator.Role.Market => 'M',
                CityGenerator.Role.Hall => 'H',
                CityGenerator.Role.Garden => 'G',
                CityGenerator.Role.Tower => 'T',
                CityGenerator.Role.None => '.',
                _ => 'R',
            };
        }

        /// <summary>Steps one district to the next letter, writing the whole map out first (the composer's rule as letters).</summary>
        private static void CycleMapCell(KitJson k, int grid, int gx, int gz)
        {
            var rows = new List<string>();
            for (int z = 0; z < grid; z++)
            {
                var row = new StringBuilder();
                for (int x = 0; x < grid; x++)
                {
                    char c = MapLetter(k, grid, x, z);
                    row.Append(x == gx && z == gz ? MapLetters[(MapLetters.IndexOf(c) + 1) % MapLetters.Length] : c);
                }

                rows.Add(row.ToString());
            }

            k.roleMap = rows;
        }
    }
}
