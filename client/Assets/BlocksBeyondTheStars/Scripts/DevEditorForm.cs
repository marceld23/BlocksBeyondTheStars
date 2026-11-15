// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The form parts the two developer editors share on the <see cref="EditorShell"/> (#3694, epic #3696): the item &amp;
    /// recipe editor (<see cref="ContentEditor"/>) and the material editor (<see cref="MaterialEditor"/>). Text fields,
    /// <c>‹ value ›</c> cycles and − / + steppers that read their value through a getter, so after an undo, a restored draft
    /// or New the whole form follows the edited definition with one <see cref="Refresh"/>. Every control is at least 44 px
    /// (the template's tap size); a cycle and a stepper are ONE pad stop that steps with Left / Right (<see cref="StepOnMove"/>).
    /// Tab / Shift+Tab walk the text fields (<see cref="TickTab"/>). The form names no locale keys itself: the editors pass
    /// their texts in, so the editor-text guard keeps finding every key in the editors' own files.
    /// </summary>
    public sealed class DevEditorForm
    {
        /// <summary>A control's height (the template's tap size) and the pitch of a label-left row.</summary>
        public const float RowH = 44f, Pitch = 52f;

        /// <summary>A − / + stepper: two 44 px buttons around a 52 px value.</summary>
        public const float StepperW = 148f, StepperValueW = 52f;

        /// <summary>A label above its control (the mock's <c>.lbl</c>): its height and the gap to the control.</summary>
        public const float AboveH = 22f, AboveGap = 4f;

        /// <summary>Row labels: 18 px down to 14 on one line, then two lines at 13 at the least.</summary>
        public const int LabelSize = 18, LabelMin = 14;

        /// <summary>Labels above a control and the small notes: 16 px down to 13.</summary>
        public const int SmallSize = 16;

        /// <summary>The text size inside the fields.</summary>
        public const int FieldFont = 18;

        /// <summary>The mock's muted label colour.</summary>
        public static readonly Color Muted = new Color(0.56f, 0.69f, 0.78f);

        private readonly List<Action> _refresh = new List<Action>();
        private readonly List<InputField> _fields = new List<InputField>();
        private readonly Action _fieldDone;

        /// <param name="fieldDone">Called when a text field loses the keyboard — the editor ends its typing undo step.</param>
        public DevEditorForm(Action fieldDone) => _fieldDone = fieldDone;

        /// <summary>The text fields in Tab order.</summary>
        public IReadOnlyList<InputField> Fields => _fields;

        /// <summary>Brings every control back in line with the edited definition.</summary>
        public void Refresh()
        {
            foreach (var refresh in _refresh)
            {
                refresh();
            }
        }

        /// <summary>Adds a display that follows the definition (a swatch, a note).</summary>
        public void OnRefresh(Action refresh)
        {
            if (refresh != null)
            {
                _refresh.Add(refresh);
            }
        }

        // ---- labels ----------------------------------------------------------------------------------------------------

        /// <summary>A label left of its control, as tall as the control.</summary>
        public static Text LabelLeft(Transform parent, float x, float y, float w, string text)
        {
            var label = UiKit.AddText(parent, x, y, w, RowH, text, LabelSize, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(label, LabelSize, LabelMin, UiKit.EditorMinFontSize);
            return label;
        }

        /// <summary>A label above its control (16 px, muted); the control goes <see cref="AboveH"/> + <see cref="AboveGap"/>
        /// lower.</summary>
        public static Text LabelAbove(Transform parent, float x, float y, float w, string text)
        {
            var label = UiKit.AddText(parent, x, y, w, AboveH, text, SmallSize, Muted, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, w, UiKit.EditorMinFontSize, SmallSize);
            return label;
        }

        // ---- controls --------------------------------------------------------------------------------------------------

        /// <summary>A text field showing <paramref name="get"/>; every keystroke goes to <paramref name="set"/>.</summary>
        public InputField Field(Transform parent, float x, float y, float w, float h, Func<string> get, Action<string> set,
            int maxLength, string placeholder = "", bool multiline = false)
        {
            var field = UiKit.AddInput(parent, x, y, w, h, get(), null, placeholder, maxLength, FieldFont, multiline);
            field.onValidateInput = (text, index, c) => c == '\t' ? '\0' : c; // Tab walks the fields, it types nothing
            field.onValueChanged.AddListener(v => set(v));
            field.onEndEdit.AddListener(_ => _fieldDone?.Invoke());
            _fields.Add(field);
            _refresh.Add(() =>
            {
                string value = get() ?? string.Empty;
                if (field.text != value)
                {
                    field.SetTextWithoutNotify(value);
                }
            });
            return field;
        }

        /// <summary>A <c>‹ value ›</c> cycle: <paramref name="step"/> gets -1 / +1 (arrows, a click on the value, Left /
        /// Right on a pad), <paramref name="show"/> names the value.</summary>
        public Text Cycle(Transform parent, float x, float y, float w, Func<string> show, Action<int> step)
        {
            Text value = null;
            value = UiKit.AddCycle(parent, x, y, w, RowH, show(), dir =>
            {
                step(dir);
                return show();
            });
            UiKit.FitLabelLines(value, 20, LabelMin, UiKit.EditorMinFontSize);
            _refresh.Add(() => value.text = show());
            return value;
        }

        /// <summary>A − value + stepper; the value is its one pad stop (Left / Right step it), − and + are mouse targets.
        /// Returns the value's focus stop.</summary>
        public Button Stepper(Transform parent, float x, float y, Func<string> show, Action<int> step)
        {
            Text value = null;
            void Step(int dir)
            {
                step(dir);
                value.text = show();
            }

            var minus = UiKit.AddFitButton(parent, x, y, RowH, RowH, "−", () => Step(-1));
            UiKit.StyleTier(minus, ButtonTier.Quiet);
            UiKit.NoPadStop(minus);
            var stop = UiKit.AddStepperValue(parent, x + RowH + 4f, y, StepperValueW, RowH, Step);
            value = UiKit.AddText(parent, x + RowH + 4f, y, StepperValueW, RowH, show(), LabelSize, UiKit.Cyan, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            value.raycastTarget = false;
            var plus = UiKit.AddFitButton(parent, x + StepperW - RowH, y, RowH, RowH, "+", () => Step(1));
            UiKit.StyleTier(plus, ButtonTier.Quiet);
            UiKit.NoPadStop(plus);
            _refresh.Add(() => value.text = show());
            return stop;
        }

        /// <summary>A plain button in the template's tiers (an "Add" under a list, the whole-canvas fills).</summary>
        public static Button ActionButton(Transform parent, float x, float y, float w, string label, System.Action onClick,
            ButtonTier tier = ButtonTier.Secondary, string icon = null)
        {
            var button = UiKit.AddTierButton(parent, x, y, w, RowH, label, onClick, tier, icon);
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                UiKit.FitButtonLabel(text, 20);
            }

            return button;
        }

        /// <summary>A 44 px quiet button with only an icon (a list row's remove).</summary>
        public static Button IconButton(Transform parent, float x, float y, string icon, System.Action onClick)
        {
            var button = UiKit.AddButton(parent, x, y, RowH, RowH, string.Empty, onClick);
            UiKit.StyleTier(button, ButtonTier.Quiet);
            UiKit.AddIcon(button.transform, (RowH - 28f) / 2f, (RowH - 28f) / 2f, 28f, icon);
            return button;
        }

        // ---- keys --------------------------------------------------------------------------------------------------------

        /// <summary>Tab / Shift+Tab: the next / previous text field (the first one when no field has the keyboard).</summary>
        public void TickTab()
        {
            if (!Input.GetKeyDown(KeyCode.Tab) || _fields.Count == 0 || EventSystem.current == null)
            {
                return;
            }

            var current = EventSystem.current.currentSelectedGameObject;
            int index = _fields.FindIndex(f => f != null && f.gameObject == current);
            bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            for (int tries = 0; tries < _fields.Count; tries++)
            {
                index = NextIndex(index, _fields.Count, back);
                var field = _fields[index];
                if (field != null && field.isActiveAndEnabled && field.interactable)
                {
                    field.Select();
                    field.ActivateInputField();
                    return;
                }
            }
        }

        /// <summary>The field after (or before) <paramref name="index"/>, wrapping around; -1 = none selected.</summary>
        public static int NextIndex(int index, int count, bool back)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (index < 0)
            {
                return back ? count - 1 : 0;
            }

            return back ? (index - 1 + count) % count : (index + 1) % count;
        }

        /// <summary>Destroys <paramref name="o"/> — at once outside play mode (edit-mode tests), where Destroy is refused.</summary>
        public static void Discard(UnityEngine.Object o)
        {
            if (o == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(o);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }

        /// <summary>Opens <paramref name="dir"/> in the system's file browser (made first when missing) — the developer's way
        /// to the export now that the status bar names no paths. False when that failed (logged).</summary>
        public static bool OpenFolder(string dir)
        {
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevEditor] Could not open the folder: {e.Message}");
                return false;
            }
        }

        // ---- the header badge --------------------------------------------------------------------------------------------

        /// <summary>The name of the "DEVELOPER" badge in the header.</summary>
        public const string BadgeName = "EditorDevBadge";

        /// <summary>
        /// The mock's amber "DEVELOPER" badge right of the shell's title: the output of these editors only reaches the game
        /// through a developer's merge and rebuild. It sits in the header bar before Back, so Back stays the header's last
        /// control (the pad's first focus never lands on it). No-op when the shell has no title.
        /// </summary>
        public static Text AddBadge(EditorShell shell, string text)
        {
            var header = shell != null ? shell.Canvas.transform.Find(EditorShell.HeaderName) : null;
            var title = header != null ? header.Find(EditorShell.TitleName)?.GetComponent<Text>() : null;
            if (title == null)
            {
                return null;
            }

            const float h = 30f, padX = 12f;
            string caption = (text ?? string.Empty).ToUpperInvariant();
            float captionW = UiKit.MeasureWidth(caption, SmallSize, FontStyle.Bold);
            float titleW = Mathf.Min(UiKit.MeasureWidth(title.text, title.fontSize, FontStyle.Bold), title.rectTransform.sizeDelta.x);
            float x = title.rectTransform.anchoredPosition.x + titleW + 16f;
            float y = -title.rectTransform.anchoredPosition.y + (title.rectTransform.sizeDelta.y - h) / 2f;
            var pill = UiKit.AddImage(header, x, y, captionW + 2f * padX, h, UiKit.PillSprite, new Color(UiKit.Warn.r, UiKit.Warn.g,
                UiKit.Warn.b, 0.16f), Image.Type.Sliced);
            pill.name = BadgeName;
            pill.raycastTarget = false;
            var label = UiKit.AddText(pill.transform, padX, 0f, captionW + 2f, h, caption, SmallSize, UiKit.Warn, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            label.raycastTarget = false;
            var back = header.Find(EditorShell.BackName);
            if (back != null)
            {
                pill.transform.SetSiblingIndex(back.GetSiblingIndex());
            }

            return label;
        }
    }
}
