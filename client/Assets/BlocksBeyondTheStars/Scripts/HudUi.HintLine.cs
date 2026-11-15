// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where the on-foot controls line sits (#3668). The short line — at most four keys for the situation — sits bottom
    /// right (HudZones G), right-aligned, out of the hotbar's way; the full line keeps the old place, one row across the
    /// whole screen bottom (#3431). The mode is <see cref="ClientSettings.ControlsHint"/>.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>Width of the short controls line in the bottom-right corner.</summary>
        private const float ShortHintW = 560f;

        private int _hintPlaced = -1; // 0 full, 1 short — the rect is only touched when the mode (or the short line's edge) changes
        private float _hintRight;     // the short line's right edge as placed

        /// <summary>
        /// The short line's right edge: 10 in from the HUD frame's right side — and in a browser also clear of the page's
        /// fullscreen button, which sits over the canvas's real bottom-right corner (WebGLTemplates index.html) and covered the
        /// line's last key. On a canvas wider than the frame the button may be far enough away already.
        /// </summary>
        private float ShortHintRight()
        {
            float right = W - 10f;
            if (Application.platform == RuntimePlatform.WebGLPlayer && _canvas != null)
            {
                float canvasW = ((RectTransform)_canvas.transform).rect.width;
                right = Mathf.Min(right, canvasW - TouchHudLayout.BrowserButtonInset(_canvas.scaleFactor, Screen.dpi / 96f));
            }

            return right;
        }

        /// <summary>
        /// Adds the hints that only apply in the moment to the controls line: the full line appends all of them, the short
        /// one lets the first that applies take its last slot (the menu key, which the pause menu's Esc needs no line for)
        /// — so the short line stays at four keys and still names the control the player cannot find otherwise (#3668).
        /// </summary>
        private string WithContextHints(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string hint, bool shortHint)
        {
            if (hint.Length == 0)
            {
                return hint;
            }

            // Creative/Sandbox worlds only: the one control the hint can't afford to omit, because flight has
            // no other tell. Appended rather than baked into ui.hud.hint so Explorer worlds stay uncluttered.
            // #2502: "{key:Jump}×2" — the pad line used to append the keyboard's "Space×2".
            string fly = Game.CanFly ? ExpandKeyTokens(loc, loc.Get("ui.hud.hint_fly")) : null;

            // Holding a rotatable block (a crafted shape or furniture): surface the rotate control (#863) —
            // nothing else in the game ever said the key exists. Appended only while it applies, like the
            // fly hint, so the always-on line stays short.
            // On a pad only the first half: "Shift+{key} back" is the keyboard's alone.
            string rotate = Game.HoldingRotatableBlock
                ? loc.Get("ui.hud.hint_rotate").Replace("{key}", RotateGlyph(loc))
                : null;
            if (InputMap.ActiveDevice == InputDeviceKind.Gamepad)
            {
                rotate = FirstHintPart(rotate);
            }

            // The middle-click slot-action ring (#924) has no other on-screen tell (#935): advertise it
            // while the hotbar is up and the selected slot holds anything to act on, like the hints above.
            // RefreshHotbar ran just before this, so the root's active state is current for this frame.
            string slot = _hotbarRoot != null && _hotbarRoot.activeSelf && !string.IsNullOrEmpty(Game.ItemInSlot(Game.SelectedHotbarSlot))
                ? loc.Get("ui.hud.hint_slot_actions").Replace("{key}", GlyphText(loc, InputAction.HotbarAction))
                : null;

            if (shortHint)
            {
                return WithContextSlot(hint, fly ?? FirstHintPart(rotate) ?? slot);
            }

            foreach (string extra in new[] { fly, rotate, slot })
            {
                if (extra != null)
                {
                    hint += " · " + extra;
                }
            }

            return hint;
        }

        /// <summary>The short controls line with its last " · " part replaced by <paramref name="context"/> (unchanged when
        /// there is none) — every language writes the short lines as " · "-separated parts.</summary>
        internal static string WithContextSlot(string shortLine, string context)
        {
            if (string.IsNullOrEmpty(context) || string.IsNullOrEmpty(shortLine))
            {
                return shortLine;
            }

            int cut = shortLine.LastIndexOf(" · ", System.StringComparison.Ordinal);
            return (cut > 0 ? shortLine.Substring(0, cut) : shortLine) + " · " + context;
        }

        /// <summary>
        /// The control that turns a held shape, for the controls line. RotateShape has no stock pad button
        /// (GamepadInputSource.DefaultButtonFor): on a pad it lives on the d-pad's down arrow (GamepadInputSource.DpadActionDown,
        /// #1220) unless the player bound a button to it — so the pad reads "D-pad ▼", never the keyboard's R that
        /// <see cref="GlyphText"/> fell back to. The keyboard keeps its bound key (<see cref="ActionGlyph"/>).
        /// </summary>
        internal static string RotateGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            if (InputMap.ActiveDevice == InputDeviceKind.Gamepad && InputMap.PadGlyph(GamepadInputSource.ButtonFor(InputAction.RotateShape)) == null)
            {
                return loc.Get("ui.key.pad_dpad") + " ▼";
            }

            return ActionGlyph(loc, InputAction.RotateShape);
        }

        // "R rotate · Shift+R back" → "R rotate": one slot, one key.
        private static string FirstHintPart(string hint)
        {
            if (hint == null)
            {
                return null;
            }

            int cut = hint.IndexOf(" · ", System.StringComparison.Ordinal);
            return cut > 0 ? hint.Substring(0, cut) : hint;
        }

        private void PlaceHintLine(bool shortHint)
        {
            int placed = shortHint ? 1 : 0;
            float right = ShortHintRight();
            if (_hint == null || (placed == _hintPlaced && Mathf.Approximately(right, _hintRight)))
            {
                return;
            }

            _hintPlaced = placed;
            _hintRight = right;
            if (shortHint)
            {
                UiKit.Place(_hint.gameObject, right - ShortHintW, H - 30f, ShortHintW, 22f);
                _hint.alignment = TextAlignmentOptions.Right;
                _hint.fontSizeMax = 16f;
                _hint.fontSize = 16f;
            }
            else
            {
                // The full line spans the frame; in a browser its margins grow to clear the fullscreen button too.
                float margin = Mathf.Max(HintMargin, W - right);
                UiKit.Place(_hint.gameObject, margin, H - 26f, W - (2f * margin), 20f);
                _hint.alignment = TextAlignmentOptions.Center;
                _hint.fontSizeMax = 14f;
                _hint.fontSize = 14f;
            }
        }
    }
}
