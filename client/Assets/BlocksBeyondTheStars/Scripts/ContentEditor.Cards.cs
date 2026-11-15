// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The item and the recipe card of the item &amp; recipe editor (#3694): opaque cards in the shell's work area, as the
    /// mock draws them — the item on the left (key, name, description, category, then the tool and effect values in two
    /// columns), the recipe in the middle (station, output, vendor theme, the ingredients, the blueprint gating). Every
    /// control changes the definition through <see cref="Change"/>, so each one is an undo step.
    /// </summary>
    public sealed partial class ContentEditor
    {
        /// <summary>The cards: the work area less the shell's margin, three columns (item · recipe · check).</summary>
        internal const float CardY = EditorShellLayout.HeaderH + EditorShellLayout.Margin;

        internal const float CardH = EditorShellLayout.CanvasH - EditorShellLayout.HeaderH - EditorShellLayout.StatusH
                                     - 2f * EditorShellLayout.Margin;

        internal const float ItemX = 16f, ItemW = 700f, RecipeX = 732f, RecipeW = 640f, CheckX = 1388f, CheckW = 516f;

        /// <summary>The cards' inner margin.</summary>
        internal const float Pad = 24f;

        /// <summary>Label-left rows of the item card and the recipe card: label width, control x and width.</summary>
        internal const float ItemLabelW = 210f, ItemControlX = 244f, ItemControlW = 432f;

        internal const float RecipeLabelW = 200f, RecipeControlX = 232f, RecipeControlW = 384f;

        /// <summary>The item card's two value columns: a cell, its label, the stepper right of it.</summary>
        internal const float CellW = 326f, CellLabelW = 170f;

        /// <summary>An ingredient row: the item key field, the count stepper, the remove button.</summary>
        internal const float RowFieldW = 300f, ListH = 200f;

        /// <summary>Character limits sized so the typed text fits its field (#514).</summary>
        private const int KeyLimit = 28, NameLimit = 32, DescLimit = 160;

        private readonly List<RowUi> _inputRows = new List<RowUi>();
        private readonly List<RowUi> _unlockRows = new List<RowUi>();
        private RectTransform _inputList, _unlockList;

        private sealed class RowUi
        {
            public GameObject Go;
            public InputField Item;
        }

        /// <summary>Where a label-left row puts its label and its control.</summary>
        private readonly struct RowGeometry
        {
            public RowGeometry(float labelW, float controlX, float controlW)
            {
                LabelW = labelW;
                ControlX = controlX;
                ControlW = controlW;
            }

            public float LabelW { get; }

            public float ControlX { get; }

            public float ControlW { get; }
        }

        private static readonly RowGeometry ItemRow = new RowGeometry(ItemLabelW, ItemControlX, ItemControlW);
        private static readonly RowGeometry RecipeRow = new RowGeometry(RecipeLabelW, RecipeControlX, RecipeControlW);

        // ---- the item card -----------------------------------------------------------------------------------------------

        private void BuildItemCard()
        {
            var card = _shell.AddCard(ItemX, CardY, ItemW, CardH);
            UiKit.AddKicker(card, Pad, 20f, ItemW - 2f * Pad, L("ui.content.item"));
            float y = 56f;
            _keyField = FieldRow(card, ItemRow, ref y, "ui.content.key", () => _b.key, v => _b.key = v, KeyLimit, "key");
            FieldRow(card, ItemRow, ref y, "ui.content.name", () => _b.name, v => _b.name = v, NameLimit, "name");
            DevEditorForm.LabelLeft(card, Pad, y, ItemLabelW, L("ui.content.desc"));
            _form.Field(card, ItemControlX, y, ItemControlW, 76f, () => _b.desc, v => Change(() => _b.desc = v, "desc"), DescLimit,
                multiline: true);
            y += 84f;
            OptionRow(card, ItemRow, ref y, "ui.content.category", "category", Categories, () => _b.category, v => _b.category = v);
            StepperRow(card, ItemRow, ref y, "ui.content.max_stack", () => _b.maxStack, v => _b.maxStack = (int)v, 1, 4096, 1, "0");
            FieldRow(card, ItemRow, ref y, "ui.content.places_block", () => _b.placesBlock, v => _b.placesBlock = v, KeyLimit, "places");

            Kicker(card, ItemW, ref y, "ui.content.tool_stats");
            OptionRow(card, ItemRow, ref y, "ui.content.tool_kind", "tool", ToolKinds, () => _b.toolKind, v => _b.toolKind = v);
            var tool = new List<(string, Func<float>, Action<float>, float, float, float, string)>
            {
                ("ui.content.tier", () => _b.tier, v => _b.tier = (int)v, 1, 5, 1, "0"),
                ("ui.content.mining_power", () => _b.miningPower, v => _b.miningPower = v, 0, 8, 0.5f, "0.0"),
                ("ui.content.mining_radius", () => _b.miningRadius, v => _b.miningRadius = (int)v, 0, 1, 1, "0"),
                ("ui.content.damage", () => _b.damage, v => _b.damage = v, 0, 200, 5, "0"),
                ("ui.content.range", () => _b.range, v => _b.range = v, 0, 80, 1, "0"),
                ("ui.content.cooldown", () => _b.cooldownSeconds, v => _b.cooldownSeconds = v, 0, 5, 0.1f, "0.0"),
                ("ui.content.energy", () => _b.energy, v => _b.energy = v, 0, 20, 1, "0"),
            };
            Grid(card, ref y, tool);

            Kicker(card, ItemW, ref y, "ui.content.effects");
            var effects = new List<(string, Func<float>, Action<float>, float, float, float, string)>
            {
                ("ui.content.consume_health", () => _b.consumeHealth, v => _b.consumeHealth = v, -50, 100, 5, "0"),
                ("ui.content.consume_hunger", () => _b.consumeHunger, v => _b.consumeHunger = v, 0, 100, 5, "0"),
                ("ui.content.armor", () => _b.armor, v => _b.armor = v, 0, 0.75f, 0.05f, "0.00"),
                ("ui.content.oxygen", () => _b.oxygen, v => _b.oxygen = v, 0, 100, 5, "0"),
                ("ui.content.scan", () => _b.scan, v => _b.scan = v, 1, 4, 0.25f, "0.00"),
            };
            Grid(card, ref y, effects);
        }

        /// <summary>The values in two columns: label, then the stepper.</summary>
        private void Grid(Transform card, ref float y, List<(string Label, Func<float> Get, Action<float> Set, float Min, float Max,
            float Step, string Format)> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                float x = Pad + (i % 2) * CellW, rowY = y + (i / 2) * DevEditorForm.Pitch;
                DevEditorForm.LabelLeft(card, x, rowY, CellLabelW, L(c.Label));
                _form.Stepper(card, x + CellLabelW + 4f, rowY, () => c.Get().ToString(c.Format), NumberStep(c.Get, c.Set, c.Min, c.Max, c.Step));
            }

            y += ((cells.Count + 1) / 2) * DevEditorForm.Pitch;
        }

        // ---- the recipe card ---------------------------------------------------------------------------------------------

        private void BuildRecipeCard()
        {
            var card = _shell.AddCard(RecipeX, CardY, RecipeW, CardH);
            UiKit.AddKicker(card, Pad, 20f, RecipeW - 2f * Pad, L("ui.content.recipe"));
            float y = 56f;
            OptionRow(card, RecipeRow, ref y, "ui.content.station", "station", Stations, () => _b.station, v => _b.station = v);
            StepperRow(card, RecipeRow, ref y, "ui.content.output_count", () => _b.outputCount, v => _b.outputCount = (int)v, 1, 64, 1, "0");
            // Market barters are posted by a vendor THEME (miners/traders/researchers/settlers); empty = every
            // vendor offers it. Ignored for the other stations.
            FieldRow(card, RecipeRow, ref y, "ui.content.market_theme", () => _b.marketTheme, v => _b.marketTheme = v, KeyLimit, "theme");

            y += 8f;
            ListHeader(card, y, "ui.content.inputs", () => _b.inputs);
            _inputList = UiKit.ScrollList(card, Pad, y + 52f, RecipeW - 2f * Pad, ListH, 8f);
            y += 52f + ListH + 16f;

            Kicker(card, RecipeW, ref y, "ui.content.blueprint");
            DevEditorForm.LabelLeft(card, Pad, y, RecipeLabelW, L("ui.content.needs_blueprint"));
            _form.Cycle(card, RecipeControlX, y, RecipeControlW, () => OffOn(_b.hasBlueprint),
                _ => Change(() => _b.hasBlueprint = !_b.hasBlueprint));
            y += DevEditorForm.Pitch;
            StepperRow(card, RecipeRow, ref y, "ui.content.knowledge", () => _b.knowledgeCost, v => _b.knowledgeCost = (int)v, 0, 50, 1, "0");
            y += 8f;
            ListHeader(card, y, "ui.content.unlock_cost", () => _b.unlockCost);
            _unlockList = UiKit.ScrollList(card, Pad, y + 52f, RecipeW - 2f * Pad, ListH, 8f);
            RefreshRows();
        }

        /// <summary>A list's title with its Add button on the right.</summary>
        private void ListHeader(Transform card, float y, string labelKey, Func<List<AmountJson>> rows)
        {
            const float addW = 200f;
            DevEditorForm.LabelLeft(card, Pad, y, RecipeW - 2f * Pad - addW - 12f, L(labelKey));
            DevEditorForm.ActionButton(card, RecipeW - Pad - addW, y, addW, L("ui.content.add"), () =>
            {
                Change(() => rows().Add(new AmountJson { item = "iron_plate", count = 1 }));
                RefreshRows();
            }, ButtonTier.Secondary, "btn_new");
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        // ---- the ingredient / unlock rows --------------------------------------------------------------------------------

        private void RefreshRows()
        {
            if (_inputList == null || _unlockList == null)
            {
                return;
            }

            Fill(_b.inputs, _inputRows, _inputList, () => _b.inputs, "in");
            Fill(_b.unlockCost, _unlockRows, _unlockList, () => _b.unlockCost, "unlock");
        }

        private void Fill(List<AmountJson> model, List<RowUi> pool, RectTransform list, Func<List<AmountJson>> rows, string tag)
        {
            for (int i = 0; i < Mathf.Max(model.Count, pool.Count); i++)
            {
                if (i >= model.Count)
                {
                    pool[i].Go.SetActive(false);
                    continue;
                }

                var ui = i < pool.Count ? pool[i] : MakeRow(pool, list, rows, tag);
                ui.Item.SetTextWithoutNotify(model[i].item ?? string.Empty);
                ui.Go.SetActive(true);
            }

            _form.Refresh(); // the rows' counts
        }

        /// <summary>One pooled row, bound to its position in the list (the list itself is replaced by undo).</summary>
        private RowUi MakeRow(List<RowUi> pool, RectTransform list, Func<List<AmountJson>> rows, string tag)
        {
            int index = pool.Count;
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(list, false);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = DevEditorForm.RowH;
            var ui = new RowUi { Go = go };
            AmountJson Row() => index < rows().Count ? rows()[index] : null;
            ui.Item = UiKit.AddInput(go.transform, 0f, 0f, RowFieldW, DevEditorForm.RowH, string.Empty, null, L("ui.content.item_key"),
                KeyLimit, DevEditorForm.FieldFont);
            ui.Item.onValidateInput = (text, at, c) => c == '\t' ? '\0' : c;
            ui.Item.onValueChanged.AddListener(v =>
            {
                if (Row() != null)
                {
                    Change(() => Row().item = v, tag + index);
                }
            });
            ui.Item.onEndEdit.AddListener(_ => _history.EndMerge());
            _form.Stepper(go.transform, RowFieldW + 8f, 0f, () => Row() != null ? Row().count.ToString() : string.Empty,
                dir => Change(() =>
                {
                    if (Row() != null)
                    {
                        Row().count = Mathf.Clamp(Row().count + dir, 1, 999);
                    }
                }));
            DevEditorForm.IconButton(go.transform, RowFieldW + DevEditorForm.StepperW + 16f, 0f, "btn_delete", () =>
            {
                if (Row() != null)
                {
                    Change(() => rows().RemoveAt(index));
                    RefreshRows();
                }
            });
            pool.Add(ui);
            return ui;
        }

        // ---- row helpers -------------------------------------------------------------------------------------------------

        private InputField FieldRow(Transform card, RowGeometry g, ref float y, string labelKey, Func<string> get, Action<string> set,
            int maxLength, string mergeKey)
        {
            DevEditorForm.LabelLeft(card, Pad, y, g.LabelW, L(labelKey));
            var field = _form.Field(card, g.ControlX, y, g.ControlW, DevEditorForm.RowH, get, v => Change(() => set(v), mergeKey), maxLength);
            y += DevEditorForm.Pitch;
            return field;
        }

        private void StepperRow(Transform card, RowGeometry g, ref float y, string labelKey, Func<float> get, Action<float> set, float min,
            float max, float step, string format)
        {
            DevEditorForm.LabelLeft(card, Pad, y, g.LabelW, L(labelKey));
            _form.Stepper(card, g.ControlX, y, () => get().ToString(format), NumberStep(get, set, min, max, step));
            y += DevEditorForm.Pitch;
        }

        /// <summary>A <c>‹ value ›</c> row over a set of raw option values; the definition stores the raw value, the cycle
        /// shows its localized label.</summary>
        private void OptionRow(Transform card, RowGeometry g, ref float y, string labelKey, string set, string[] values, Func<string> get,
            Action<string> put)
        {
            var labels = Display(set, values);
            DevEditorForm.LabelLeft(card, Pad, y, g.LabelW, L(labelKey));
            _form.Cycle(card, g.ControlX, y, g.ControlW, () => labels[IndexOf(values, get())],
                dir => Change(() => put(values[(IndexOf(values, get()) + dir + values.Length) % values.Length])));
            y += DevEditorForm.Pitch;
        }

        private void Kicker(Transform card, float cardW, ref float y, string key)
        {
            y += 8f;
            UiKit.AddKicker(card, Pad, y, cardW - 2f * Pad, L(key));
            y += 32f;
        }

        /// <summary>One click of a stepper: the value moves by <paramref name="step"/> within its range (rounded, so 0.1 steps
        /// do not drift to 0.30000004).</summary>
        private Action<int> NumberStep(Func<float> get, Action<float> set, float min, float max, float step)
            => dir => Change(() => set((float)Math.Round(Mathf.Clamp(get() + dir * step, min, max), 4)));

        private static int IndexOf(string[] values, string value) => Math.Max(0, Array.IndexOf(values, value));

        /// <summary>Localized label for a raw option value (<c>ui.opt.&lt;set&gt;.&lt;value&gt;</c>), falling
        /// back to the identifier itself so a newly added enum member still shows something (#516).</summary>
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
    }
}
