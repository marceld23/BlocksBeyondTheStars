// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The material editor's properties card (#3694): three columns with the label above each control, as the mock draws
    /// them — the material's key, name, description, palette section and dye switch, then mining, look and world spawn.
    /// Every value is a <c>‹ value ›</c> cycle (one pad stop: Left / Right step it), so the old 28 px − / + buttons are gone.
    /// </summary>
    public sealed partial class MaterialEditor
    {
        internal const float PropsX = 732f, PropsW = 1172f, ColW = 360f, ColGap = 22f;

        /// <summary>A row: label, gap, control, space under it; the description field is taller.</summary>
        internal const float RowStep = DevEditorForm.AboveH + DevEditorForm.AboveGap + DevEditorForm.RowH + 16f, DescH = 72f;

        /// <summary>Character limits sized so the typed text fits its field (#514); a source path may scroll.</summary>
        private const int KeyLimit = 28, NameLimit = 32, DescLimit = 160, PathLimit = 120;

        private Text _spawnEstimate;

        private static float Col(int i) => Pad + i * (ColW + ColGap);

        private static float ControlY(float rowY) => rowY + DevEditorForm.AboveH + DevEditorForm.AboveGap;

        private void BuildPropsCard()
        {
            var card = _shell.AddCard(PropsX, CardY, PropsW, CardH);
            UiKit.AddKicker(card, Pad, 20f, PropsW - 2f * Pad, L("ui.material.material"));
            float y = 56f;
            _keyField = FieldCell(card, 0, y, "ui.content.key", () => _b.key, v => _b.key = v, KeyLimit, "key");
            FieldCell(card, 1, y, "ui.content.name", () => _b.name, v => _b.name = v, NameLimit, "name");
            OptionCell(card, 2, y, "ui.material.category", BlockCategories, CategoryLabels(), () => _b.category, v => _b.category = v);
            y += RowStep;
            DevEditorForm.LabelAbove(card, Col(0), y, 2f * ColW + ColGap, L("ui.content.desc"));
            _form.Field(card, Col(0), ControlY(y), 2f * ColW + ColGap, DescH, () => _b.desc, v => Change(() => _b.desc = v, "desc"), DescLimit,
                multiline: true);
            CycleCell(card, 2, y, L("ui.material.tintable"), () => OffOn(_b.tintable), _ => Change(() => _b.tintable = !_b.tintable));
            y += RowStep + DescH - DevEditorForm.RowH;

            Kicker(card, ref y, "ui.material.mechanics");
            NumberCell(card, 0, y, "ui.material.hardness", () => _b.hardness, v => _b.hardness = v, 0.2f, 12f, 0.2f, "0.0");
            OptionCell(card, 1, y, "ui.material.tool", Tools, Display("blocktool", Tools), () => _b.requiredTool, v => _b.requiredTool = v);
            NumberCell(card, 2, y, "ui.material.min_tier", () => _b.minToolTier, v => _b.minToolTier = (int)v, 0, 3, 1, "0");
            y += RowStep;

            Kicker(card, ref y, "ui.material.look");
            NumberCell(card, 0, y, "ui.material.gloss", () => _b.gloss, v => _b.gloss = v, 0, 1, 0.05f, "0.00");
            NumberCell(card, 1, y, "ui.material.metal", () => _b.metal, v => _b.metal = v, 0, 1, 0.05f, "0.00");
            NumberCell(card, 2, y, "ui.material.emission", () => _b.emission, v => _b.emission = v, 0, 1, 0.05f, "0.00");
            y += RowStep;
            BuildBaseColourRow(card, y);
            y += RowStep;

            Kicker(card, ref y, "ui.material.world");
            BuildWorldRows(card, y);
            if (EditorShell.DesktopFeatures)
            {
                UiKit.AddLinkButton(card, Pad, CardH - 64f, ColW, 30f, L("ui.photos.open_folder"), OpenExportFolder);
            }
        }

        /// <summary>The base colour (the data-driven tint, the eraser and the whole-tile fills) as three channel cycles.</summary>
        private void BuildBaseColourRow(Transform card, float y)
        {
            DevEditorForm.LabelAbove(card, Col(0), y, ColW, L("ui.material.base_color"));
            ChannelCycle(card, 0, y, "R", 16);
            ChannelCycle(card, 1, y, "G", 8);
            ChannelCycle(card, 2, y, "B", 0);
        }

        private void ChannelCycle(Transform card, int col, float y, string channel, int shift)
        {
            int Get() => (_b.colorRgb >> shift) & 255;
            _form.Cycle(card, Col(col), ControlY(y), ColW, () => channel + "  " + Get(), dir => Change(() =>
            {
                int value = Mathf.Clamp(Get() + dir * 15, 0, 255);
                _b.colorRgb = (_b.colorRgb & ~(255 << shift)) | (value << shift);
            }));
        }

        private void BuildWorldRows(Transform card, float y)
        {
            OptionCell(card, 0, y, "ui.material.world_type", WorldTypes, Display("worldtype", WorldTypes), () => _b.worldType, v => _b.worldType = v);
            NumberCell(card, 1, y, "ui.material.frequency", () => _b.frequency, v => _b.frequency = v, 0, 0.5f, 0.01f, "0.00");
            // What the generator actually does with that number — the raw value saturates, so show the share
            // of deep rock this vein ends up claiming (#509).
            _spawnEstimate = UiKit.AddText(card, Col(2), ControlY(y), ColW, DevEditorForm.RowH, string.Empty, DevEditorForm.SmallSize,
                DevEditorForm.Muted, TextAnchor.MiddleLeft);
            _spawnEstimate.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.FitLabelLines(_spawnEstimate, DevEditorForm.SmallSize, DevEditorForm.LabelMin, UiKit.EditorMinFontSize);
            _form.OnRefresh(RefreshSpawnEstimate);
            RefreshSpawnEstimate();
            y += RowStep;
            NumberCell(card, 0, y, "ui.material.min_depth", () => _b.minDepth, v => _b.minDepth = (int)v, 0, 256, 2, "0");
            NumberCell(card, 1, y, "ui.material.max_depth", () => _b.maxDepth, v => _b.maxDepth = (int)v, 8, 2048, 64, "0");
            FieldCell(card, 2, y, "ui.material.source_png", () => _b.sourceImage, v => _b.sourceImage = v, PathLimit, "source");
        }

        /// <summary>Restates the frequency as the share of deep rock the vein will actually claim:
        /// <c>WorldGenerator.SelectOre</c> scales and caps the raw rarity (0.30/0.08 for a shallow band,
        /// 0.15/0.05 deeper), so the number on the cycle is not the number in the world.</summary>
        private void RefreshSpawnEstimate()
        {
            if (_spawnEstimate == null || _b == null)
            {
                return;
            }

            bool shallow = _b.minDepth <= 8;
            float scale = shallow ? 0.30f : 0.15f, cap = shallow ? 0.08f : 0.05f;
            float frac = Mathf.Clamp(_b.frequency * scale, 0f, cap);
            _spawnEstimate.text = string.Format(L("ui.material.spawn_estimate"), (frac * 100f).ToString("0.0"));
        }

        // ---- cells -------------------------------------------------------------------------------------------------------

        private UnityEngine.UI.InputField FieldCell(Transform card, int col, float y, string labelKey, Func<string> get, Action<string> set,
            int maxLength, string mergeKey)
        {
            DevEditorForm.LabelAbove(card, Col(col), y, ColW, L(labelKey));
            return _form.Field(card, Col(col), ControlY(y), ColW, DevEditorForm.RowH, get, v => Change(() => set(v), mergeKey), maxLength);
        }

        private void CycleCell(Transform card, int col, float y, string label, Func<string> show, Action<int> step)
        {
            DevEditorForm.LabelAbove(card, Col(col), y, ColW, label);
            _form.Cycle(card, Col(col), ControlY(y), ColW, show, step);
        }

        /// <summary>A number as a cycle: Left / Right (or the arrows) move it by <paramref name="step"/> within its range.</summary>
        private void NumberCell(Transform card, int col, float y, string labelKey, Func<float> get, Action<float> set, float min, float max,
            float step, string format)
            => CycleCell(card, col, y, L(labelKey), () => get().ToString(format),
                dir => Change(() => set((float)Math.Round(Mathf.Clamp(get() + dir * step, min, max), 4))));

        /// <summary>A choice among raw option values; the bundle stores the raw value, the cycle shows its label.</summary>
        private void OptionCell(Transform card, int col, float y, string labelKey, string[] values, string[] labels, Func<string> get,
            Action<string> put)
        {
            int Index() => Math.Max(0, Array.IndexOf(values, get()));
            CycleCell(card, col, y, L(labelKey), () => labels[Index()],
                dir => Change(() => put(values[(Index() + dir + values.Length) % values.Length])));
        }

        private void Kicker(Transform card, ref float y, string key)
        {
            y += 8f;
            UiKit.AddKicker(card, Pad, y, PropsW - 2f * Pad, L(key));
            y += 32f;
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        /// <summary>Localized label for a raw option value (<c>ui.opt.&lt;set&gt;.&lt;value&gt;</c>), falling
        /// back to the identifier itself so a newly added option still shows something (#516).</summary>
        private string Opt(string set, string value)
        {
            string key = "ui.opt." + set + "." + value;
            return Shell != null && Shell.Localizer != null && Shell.Localizer.Has(key) ? Shell.L(key) : value;
        }

        /// <summary>The display labels for a whole option set; the exported value stays the raw identifier.</summary>
        private string[] Display(string set, string[] values)
        {
            var labels = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                labels[i] = Opt(set, values[i]);
            }

            return labels;
        }

        /// <summary>Block-category labels reusing the palette section titles (<c>ui.cat.*</c>), so the editor
        /// names a category exactly like the build palettes it will show up in.</summary>
        private string[] CategoryLabels()
        {
            var labels = new string[BlockCategories.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                string key = "ui.cat." + BlockCategories[i];
                labels[i] = Shell != null && Shell.Localizer != null && Shell.Localizer.Has(key) ? Shell.L(key) : BlockCategories[i];
            }

            return labels;
        }

        /// <summary>Opens this material's export folder (or the exports folder before the first export) — the status bar
        /// names no paths any more. Desktop only.</summary>
        private void OpenExportFolder()
        {
            string key = ContentKeyRules.Clean(_b.key);
            string dir = ContentKeyRules.Check(key) == ContentKeyProblem.None && Directory.Exists(ExportDir(key))
                ? ExportDir(key)
                : Path.Combine(ExportRoot ?? AppPaths.Root, "material_exports");
            if (!DevEditorForm.OpenFolder(dir))
            {
                _shell.SetStatus(L("ui.ed.open_folder_failed"), EditorStatusSeverity.Error);
            }
        }
    }
}
