// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The input-glyph helpers of <see cref="HudUi"/>: how a prompt, a controls line or a <c>{key:Action}</c> token names
    /// a control on the keyboard, the gamepad and a tablet (#2463, #2502, #2507).
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>The Interact glyph for a prompt: the pad button, the key, or on a tablet the label of the on-screen
        /// USE button (#2049) — <see cref="InputMap.Glyph"/> alone falls back to the key name there.</summary>
        private static string InteractGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
            => InputMap.ActiveDevice == InputDeviceKind.Touch ? loc.Get("ui.touch.use") : GlyphText(loc, InputAction.Interact);


        /// <summary><see cref="InputMap.Glyph"/> with mouse buttons rendered as their localized short names
        /// (the LMB/RMB style the hint line already uses) instead of the raw KeyCode name — "Mouse2" reads
        /// like a debug string on the HUD (#935). Pad glyphs pass through untouched. The Suit tab names the active gear's
        /// keys through it too (#2288).</summary>
        internal static string GlyphText(BlocksBeyondTheStars.Shared.Localization.Localizer loc, InputAction action)
        {
            string glyph = InputMap.Glyph(action);
            KeyCode key = InputMap.Key(action);
            string mouseKey = InputMap.MouseLocaleKey(key);
            if (mouseKey != null && glyph == key.ToString())
            {
                return loc.Get(mouseKey);
            }

            // #2502: Unity names the Enter key "Return" — the chat key read "Return chat" once the controls line named the
            // bound key instead of a hard-coded "Enter".
            return key == KeyCode.Return && glyph == "Return" ? "Enter" : glyph;
        }

        /// <summary>Like <see cref="GlyphText"/>, but on a gamepad an action without a pad button (the lamp, the cloak) names
        /// the pad's Actions list it is reached through — not the keyboard letter, which on a pad is some other button
        /// (pad B is crouch / back). Used by the gear strip and the Suit tab (#2290, #2288).</summary>
        internal static string ActionGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, InputAction action)
        {
            if (InputMap.ActiveDevice == InputDeviceKind.Gamepad && InputMap.PadGlyph(GamepadInputSource.ButtonFor(action)) == null)
            {
                return InputMap.PadGlyph(GamepadInputSource.ButtonFor(InputAction.ContextActions)) ?? loc.Get("ui.touch.actions");
            }

            return GlyphText(loc, action);
        }

        /// <summary>What a <c>{key:Action}</c> token in a locale string reads as (#2463) — one rule for VEGA's lines
        /// (<see cref="VegaPanel"/>) and the HUD prompts: on a tablet the touch layout's own button for the action (USE,
        /// MAP, ≡ …, <see cref="VegaText.TouchControlLabel"/>) or else the on-screen action menu; on a pad and the
        /// keyboard <see cref="ActionGlyph"/> (the pad button or the Actions list, the bound key, mouse buttons by their
        /// short names). A name that is no <see cref="InputAction"/> reads as itself, so a stale locale never shows
        /// braces.</summary>
        internal static string KeyTokenGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string action)
            => KeyTokenGlyph(loc, action, prompt: false);

        /// <summary><see cref="KeyTokenGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer, string)"/> for a HUD prompt
        /// (<paramref name="prompt"/>, #2502): on a tablet an action without its own button reads as the ACT button that
        /// opens the list (<see cref="VegaText.TouchPromptFallbackKey"/>) — "ACT: undock" — instead of VEGA's "the on-screen
        /// action menu", which does not fit a short "X: verb" prompt. Keyboard and pad read the same either way.</summary>
        internal static string KeyTokenGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string action, bool prompt)
        {
            if (VegaText.IsPseudoAction(action))
            {
                return PseudoActionGlyph(loc, action, prompt); // #2469: move, look, jump, crouch, mine, place, scan, the quick bar
            }

            if (!System.Enum.TryParse<InputAction>(action, out var a))
            {
                return action;
            }

            if (InputMap.ActiveDevice == InputDeviceKind.Touch)
            {
                string touch = VegaText.TouchControlLabel(action, key => loc != null ? loc.Get(key) : key);
                return touch ?? (loc != null ? loc.Get(VegaText.TouchFallbackKey(prompt)) : action);
            }

            return loc != null ? ActionGlyph(loc, a) : InputMap.Glyph(a);
        }

        /// <summary>The glyph a HUD prompt names an action by (#2502) — <see cref="KeyTokenGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer, string, bool)"/>
        /// in its prompt form, so a tablet reads its USE button or ACT, never the key letter <see cref="InputMap.Glyph"/>
        /// falls back to there, and a pad names the Actions list for a verb without a pad button.</summary>
        internal static string PromptGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, InputAction action)
            => KeyTokenGlyph(loc, action.ToString(), prompt: true);

        /// <summary>"(E)" for a prompt — a pad glyph that wears its own parentheses stays "(X)", never "((X))" (#2502).</summary>
        private static string InParentheses(string glyph) => BlocksBeyondTheStars.Shared.Localization.LineText.InParentheses(glyph);

        /// <summary>What a pseudo-action token (<see cref="VegaText.PseudoActions"/>, #2469) reads as: the continuous controls
        /// are no rebindable <see cref="InputAction"/>, so each device names them its own way — the keyboard by its fixed keys
        /// (WASD, the mouse, Space, Ctrl/C, LMB/RMB, 1–9), the pad by its sticks and fixed buttons (RB mines, LB places, the
        /// bottom face button jumps, B crouches, the d-pad cycles the quick bar — in the player's glyph set, and the
        /// triggers beside the shoulder buttons while they are switched on, #2507: <see cref="PadControlGlyph"/>), the tablet
        /// by the layout's buttons, stick and look pad (<see cref="VegaText.TouchControlLabel"/>).</summary>
        private static string PseudoActionGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string action, bool prompt)
        {
            string L(string key) => loc != null ? loc.Get(key) : key;
            switch (InputMap.ActiveDevice)
            {
                case InputDeviceKind.Touch:
                    return VegaText.TouchControlLabel(action, L) ?? (loc != null ? loc.Get(VegaText.TouchFallbackKey(prompt)) : action);
                case InputDeviceKind.Gamepad:
                    return PadControlGlyph(loc, action) ?? action;
                default:
                    return action switch
                    {
                        "Move" => L("ui.key.move_keys"),
                        "Look" => L("ui.key.look_mouse"),
                        "Jump" => L("ui.key.jump_keys"),
                        "Crouch" => L("ui.key.crouch_keys"),
                        "Mine" or "Scan" => L("ui.key.mouse_left"),
                        "Place" => L("ui.key.mouse_right"),
                        "Hotbar" => L("ui.key.hotbar_keys"),
                        _ => action,
                    };
            }
        }

        /// <summary>A HUD prompt, button or toast with its <c>{key:Action}</c> tokens resolved by <see cref="KeyTokenGlyph"/>
        /// (#2463) — "menu ({key:UiMenu})" reads "menu (Tab)", "menu (Menu)" on a pad and "menu (≡)" on a tablet. Prompt
        /// form (#2502): a verb without its own touch button reads "ACT" (the button that opens the list), not VEGA's
        /// longer phrase; VEGA's panel and the cutscene captions keep that through <see cref="KeyTokenGlyph"/>.</summary>
        internal static string ExpandKeyTokens(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string text)
            => VegaText.ExpandKeyTokens(text, action => KeyTokenGlyph(loc, action, prompt: true));

        /// <summary>What can change a token's glyph without a device switch (#2502): another settings object, a key or pad
        /// rebind (<see cref="ClientSettings.BindingsVersion"/>), the pad's glyph set or the triggers switch (#2507: mine and
        /// place read "RB/RT" while it is on) — the cache key of the expanded controls lines, next to the device and the
        /// language.</summary>
        internal static (ClientSettings Settings, int Bindings, PadGlyphSet Glyphs, bool Triggers) GlyphStamp
        {
            get
            {
                var settings = GamepadInputSource.Settings;
                return (settings, settings != null ? settings.BindingsVersion : -1, settings != null ? settings.PadGlyphs : PadGlyphSet.Xbox,
                    settings != null && settings.PadTriggersMinePlace);
            }
        }

        /// <summary>What a <c>{key:Action}</c> token names on the pad (#2507): the sticks, the fixed face and shoulder buttons
        /// (with the triggers while they are switched on), the d-pad, and every rebindable action by its bound pad button —
        /// all in the player's glyph set (<see cref="PadControlNames.Glyph"/>, the one table in <see cref="PadGlyphTable"/>).
        /// Null when the pad has no control for the action: <see cref="KeyTokenGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer, string, bool)"/>
        /// then names the Actions list (<see cref="ActionGlyph"/>), the pad's controls lines drop the segment
        /// (<see cref="PadLine"/>).</summary>
        internal static string PadControlGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string action)
        {
            var settings = GamepadInputSource.Settings;
            return PadControlNames.Glyph(action, PadButtonOf, InputMap.PadGlyphs, settings != null && settings.PadTriggersMinePlace,
                key => loc != null ? loc.Get(key) : key);
        }

        /// <summary>The pad button bound to an action, by name (0–19), or −1 for no button / no such action.</summary>
        private static int PadButtonOf(string action)
            => System.Enum.TryParse<InputAction>(action, out var bound) ? InputMap.PadButtonIndex(GamepadInputSource.ButtonFor(bound)) : -1;

        /// <summary>A pad controls line (#2507) — the flight and spacewalk lines <c>ui.space.controls_pad</c> /
        /// <c>ui.space.eva_controls_pad</c>: one <c>{key:Action}</c> token per control, named by <see cref="PadControlGlyph"/>;
        /// a control the player left without a pad button drops out of the line (<see cref="PadControlNames.ComposeLine"/>).
        /// The callers cache the result per device, language and <see cref="GlyphStamp"/>.</summary>
        internal static string PadLine(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key)
            => PadControlNames.ComposeLine(loc.Get(key), action => PadControlGlyph(loc, action));

        // #2502: the keyboard controls line names the bound keys now ({key:Action} tokens) — expanded once per language,
        // binding and feedback key, not on every 10 Hz refresh.
        private static string _keyboardHint;
        private static (string Template, (ClientSettings, int, PadGlyphSet, bool) Stamp, string Feedback) _keyboardHintKey;

        /// <summary>The on-foot controls line for the keyboard (#2502): <c>ui.hud.hint</c> with the player's bound keys.</summary>
        internal static string KeyboardHint(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var key = (loc.Get("ui.hud.hint"), GlyphStamp, FeedbackUi.HotkeyName);
            if (_keyboardHint != null && key.Equals(_keyboardHintKey))
            {
                return _keyboardHint;
            }

            _keyboardHint = ExpandKeyTokens(loc, key.Item1).Replace("{feedback_key}", key.Item3);
            _keyboardHintKey = key;
            return _keyboardHint;
        }

        /// <summary>The jump control's name for a hint (#2290): the pad's bottom face button, the tablet's JUMP button or the
        /// keyboard's space bar. Jump is no rebindable action (the legacy "Jump" axis), so <see cref="InputMap.Glyph"/>
        /// cannot name it.</summary>
        internal static string JumpGlyph(BlocksBeyondTheStars.Shared.Localization.Localizer loc) => InputMap.ActiveDevice switch
        {
            InputDeviceKind.Gamepad => InputMap.PadGlyph(KeyCode.JoystickButton0),
            InputDeviceKind.Touch => loc.Get("ui.touch.jump"),
            _ => loc.Get("ui.hud.jump_key"),
        };

        // #2461 (N9): the pad's controls line used to be one fixed string ("(A) jump · (Y) view …") — wrong for a
        // PlayStation or Nintendo glyph set, for a rebound pad button, and silent about the triggers once the player
        // switched them on. It is composed from the live glyphs now, and cached: rebuilt only when the language, a
        // binding or one of the pad settings changes.
        private static string _padHint;
        private static (string Template, int Bindings, PadGlyphSet Glyphs, bool Triggers) _padHintKey;

        /// <summary>The on-foot controls line for a gamepad, in the player's glyph set and bindings (#2461, N9).</summary>
        internal static string PadHint(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var settings = GamepadInputSource.Settings;
            string template = loc.Get("ui.hud.hint_pad_glyphs");
            bool triggers = settings != null && settings.PadTriggersMinePlace;
            var key = (template, settings != null ? settings.BindingsVersion : -1,
                settings != null ? settings.PadGlyphs : PadGlyphSet.Xbox, triggers);
            if (_padHint != null && key.Equals(_padHintKey))
            {
                return _padHint;
            }

            // Mine / place / jump / crouch are fixed pad buttons (the shoulder and the bottom face buttons); the triggers
            // join mine/place only while the player has them switched on — the one rule of PadControlGlyph (#2507).
            _padHint = template
                .Replace("{mine}", PadControlGlyph(loc, "Mine"))
                .Replace("{place}", PadControlGlyph(loc, "Place"))
                .Replace("{jump}", PadControlGlyph(loc, "Jump"))
                .Replace("{crouch}", PadControlGlyph(loc, "Crouch"))
                .Replace("{use}", PadGlyphOf(InputAction.Interact))
                .Replace("{view}", PadGlyphOf(InputAction.ToggleThirdPerson))
                .Replace("{actions}", PadGlyphOf(InputAction.ContextActions))
                .Replace("{vega}", PadGlyphOf(InputAction.VegaContinue))
                .Replace("{menu}", PadGlyphOf(InputAction.UiMenu));
            _padHintKey = key;
            return _padHint;
        }

        /// <summary>The pad button bound to an action, by name — "—" when the player unbound it.</summary>
        private static string PadGlyphOf(InputAction action) => InputMap.PadGlyph(GamepadInputSource.ButtonFor(action)) ?? "—";
    }
}
