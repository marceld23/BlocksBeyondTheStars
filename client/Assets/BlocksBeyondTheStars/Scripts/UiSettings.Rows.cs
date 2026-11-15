// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The row controls of the settings screen (#3666): every row is a container over the full row width (the
    /// label left, the control right) that tells the description card what to show while the pointer is over it.</summary>
    public sealed partial class UiSettings
    {
        // Two-column row layout shared by every row so labels and controls line up throughout the screen: labels get
        // LabelW, every control column starts at CtrlX (both relative to the row).
        private const float RowH = 48f, RowStep = 58f;
        private const float LabelW = 400f;
        private const float CtrlX = 420f;
        private const int LabelSize = 20;

        /// <summary>Width of the control column.</summary>
        private float CtrlW => _rowW - CtrlX;

        /// <summary>
        /// Starts a row at <paramref name="y"/> and moves <paramref name="y"/> past it. Returns the row's transform: its
        /// controls are placed in row coordinates (0,0 = the row's top left). The row's near-transparent backing makes the
        /// whole width — the label too — count as "over this row" for the description card, and lights up faintly while
        /// the card explains it. <paramref name="info"/> is the explanation (null/empty = the label alone).
        /// </summary>
        private Transform Row(ref float y, string label, string info, float h = RowH)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            UiKit.Place(go, _x, y, _rowW, h);
            var backing = go.AddComponent<Image>();
            backing.sprite = UiKit.ButtonSprite;
            backing.type = Image.Type.Sliced;
            backing.color = UiSettingsRowInfo.Idle; // a hit area: enter events reach the row from the label and the controls
            var rowInfo = go.AddComponent<UiSettingsRowInfo>();
            rowInfo.Label = (label ?? string.Empty).Trim();
            rowInfo.Info = info ?? string.Empty;
            rowInfo.Backing = backing;
            rowInfo.Entered = OnRowEntered;
            if (!string.IsNullOrEmpty(label))
            {
                var text = UiKit.AddText(go.transform, 10f, 0f, LabelW - 10f, h, label, LabelSize, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(text, LabelW - 10f, 14, LabelSize); // a long translation shrinks instead of running into the control
            }

            y += h + (RowStep - RowH);
            return go.transform;
        }

        /// <summary>A sub-heading inside a category (cyan, upper case), with air above every group but the first.</summary>
        private void Kicker(ref float y, string text)
        {
            if (y > 20f)
            {
                y += 12f;
            }

            UiKit.AddKicker(_content, _x + 10f, y, _rowW - 20f, text);
            y += 34f;
        }

        /// <summary>A line of status text across the row width (no control): "no controller connected", "nobody is
        /// muted", "only on the High graphics level". As tall as its lines.</summary>
        private void Note(ref float y, string text)
        {
            UiKit.AddMeasuredText(_content, _x + 10f, y, _rowW - 20f, text, 16, UiKit.CyanDim, out float h);
            y += Mathf.Max(28f, h) + 10f;
        }

        /// <summary>A yes/no row: a switch plus its state word ("On"/"Off"), which is a mouse target of the switch too —
        /// a 60-px pill alone is small for a child's click. Only the switch is a stop for the pad.</summary>
        private void Toggle(ref float y, string label, bool on, System.Action onClick, string info = null)
        {
            var row = Row(ref y, label, info);
            UiKit.AddSwitch(row, CtrlX, (RowH - 32f) / 2f, on, _ => onClick());
            var state = UiKit.AddButton(row, CtrlX + 66f, 0f, Mathf.Min(220f, CtrlW - 66f), RowH, on ? L("ui.toggle.on") : L("ui.toggle.off"), onClick);
            UiKit.StyleTier(state, ButtonTier.Quiet);
            ((Image)state.targetGraphic).color = new Color(1f, 1f, 1f, 0f); // the word, not a second button
            state.navigation = new Navigation { mode = Navigation.Mode.None };
            var word = state.GetComponentInChildren<Text>();
            word.color = on ? UiKit.Cyan : UiKit.CyanDim;
        }

        /// <summary>A choice row: <c>‹ value ›</c> (<see cref="UiKit.AddCycle"/>). <paramref name="step"/> gets -1 or +1
        /// and applies the change (every step rebuilds the screen, so the shown value is the new one).</summary>
        private void Cycle(ref float y, string label, string value, System.Action<int> step, string info = null)
        {
            var row = Row(ref y, label, info);
            UiKit.AddCycle(row, CtrlX, 0f, CtrlW, RowH, value, dir =>
            {
                step(dir);
                return value;
            });
        }

        /// <summary>One per-effect switch of the atmosphere group (#2404): indented under the mode row; a change applies
        /// live and keeps the mode on Custom.</summary>
        private void AtmoToggle(ref float y, string key, bool on, System.Action<bool> set)
        {
            Toggle(ref y, "    " + L("ui.settings.atmo." + key), on, () =>
            {
                set(!on);
                S.Atmosphere = AtmosphereMode.Custom;
                S.Apply();
                ApplyLiveWorld();
                Rebuild();
            });
        }

        /// <summary>A number row: − and + around the value, a fill bar under the value shows where it sits in its range.
        /// For a pad the value is the row's one focus stop and Left / Right step it (<see cref="StepOnMove"/>); − and + stay
        /// mouse and touch targets.</summary>
        private void Stepper(ref float y, string label, float frac01, System.Action minus, System.Action plus, string value, string info = null)
        {
            var row = Row(ref y, label, info);
            const float step = 50f, gap = 8f;
            float mid = CtrlW - 2f * (step + gap);
            StepButton(row, CtrlX, "−", minus);
            UiKit.AddStepperValue(row, CtrlX + step + gap, 0f, mid, RowH - 6f, dir => (dir < 0 ? minus : plus)());
            UiKit.AddText(row, CtrlX + step + gap, 0f, mid, RowH - 6f, value, LabelSize, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            StepButton(row, _rowW - step, "+", plus);
            UiKit.AddImage(row, CtrlX + step + gap, RowH - 6f, mid, 4f, UiKit.SolidSprite, new Color(0.1f, 0.18f, 0.3f));
            UiKit.AddImage(row, CtrlX + step + gap, RowH - 6f, mid * Mathf.Clamp01(frac01), 4f, UiKit.SolidSprite, UiKit.Cyan);
        }

        /// <summary>A stepper's − or + button: the glyph centred and large, in the quiet look of the cycle arrows, and no
        /// focus stop of its own (the value between them is).</summary>
        private static void StepButton(Transform row, float x, string glyph, System.Action onClick)
        {
            var button = UiKit.AddButton(row, x, 0f, 50f, RowH, glyph, onClick);
            UiKit.StyleTier(button, ButtonTier.Quiet);
            UiKit.NoPadStop(button);
            var text = button.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = UiKit.Cyan;
            UiKit.Place(text.gameObject, 0f, -2f, 50f, RowH);
            text.resizeTextForBestFit = false;
            text.fontSize = 28;
        }

        private void VolRow(ref float y, string label, System.Func<float> get, System.Action<float> set, string info = null)
        {
            float v = get();
            // Apply() right away so the change is audible immediately — important when adjusting the volume from
            // the in-game pause menu, where waiting until you close settings to hear it feels broken (Severin playtest).
            Stepper(ref y, label, v,
                () => { set(Mathf.Clamp01(get() - 0.1f)); S.Apply(); Rebuild(); },
                () => { set(Mathf.Clamp01(get() + 0.1f)); S.Apply(); Rebuild(); },
                Mathf.RoundToInt(v * 100f) + "%", info);
        }

        /// <summary>A colour row of the character: <c>‹ swatch ›</c> steps through the palette both ways.</summary>
        private void ColorRow(ref float y, string label, Color color, System.Action<Color> set)
        {
            var row = Row(ref y, label, null);
            UiKit.AddCycle(row, CtrlX, 0f, CtrlW, RowH, string.Empty, dir =>
            {
                set(StepColor(color, dir));
                Rebuild();
                return string.Empty;
            });
            UiKit.AddImage(row, CtrlX + CtrlW / 2f - 70f, 10f, 140f, RowH - 20f, UiKit.SolidSprite, color); // clicks pass through to the cycle
        }

        /// <summary>A control-rebind row: the action label + a button showing its current keyboard key + a
        /// second button showing its gamepad button ("—" when the action has none). Clicking either arms
        /// capture for that device (<see cref="Update"/> binds the next key / pad button pressed).</summary>
        private void KeyRow(ref float y, InputAction action)
        {
            var row = Row(ref y, L(KeyLabel(action)), null);
            float keyW = CtrlW - 130f; // leave a 120-wide pad column + a 10 gap on the right
            string val = _rebinding == action ? L("ui.settings.press_key") : InputMap.Key(action).ToString();

            // Menu verbs show their key but cannot rebind it (#1198): Escape closes every screen and Tab
            // opens the menu, so a player who bound either away could be stuck inside a modal. Their PAD
            // button stays freely rebindable, which is the column a pad player actually cares about.
            bool locked = InputMap.KeyboardLocked(action);
            System.Action rebindKey = locked ? null : () => { _rebinding = action; _rebindingPad = null; Rebuild(); };
            var b = UiKit.AddButton(row, CtrlX, 0f, keyW, RowH, val, rebindKey);
            if (locked)
            {
                b.interactable = false;
            }
            else if (_rebinding == action)
            {
                b.GetComponent<Image>().color = UiKit.Cyan;
            }

            // Gamepad column: the action's current pad button (rebindable even with no pad plugged in yet).
            string padVal = _rebindingPad == action
                ? L("ui.settings.press_pad")
                : InputMap.PadGlyph(GamepadInputSource.ButtonFor(action)) ?? "—";
            var pb = UiKit.AddButton(row, CtrlX + keyW + 10f, 0f, 120f, RowH, padVal,
                () => { _rebindingPad = action; _rebinding = null; Rebuild(); });
            if (_rebindingPad == action)
            {
                pb.GetComponent<Image>().color = UiKit.Cyan;
            }
        }
    }
}
