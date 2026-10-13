// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a <c>{key:Action}</c> token names on a gamepad, and the pad's own controls lines built from it (#2507). The
    /// flight and spacewalk lines (<c>ui.space.controls_pad</c>, <c>ui.space.eva_controls_pad</c>) used to be fixed Xbox
    /// text — wrong for the PlayStation and Nintendo glyph sets and after a rebind. They are templates now, one
    /// <c>{key:Action}</c> token per control, resolved here from the player's bindings, glyph set and trigger switch.
    /// <c>HudUi.KeyTokenGlyph</c> reads its pad pseudo-actions through <see cref="Glyph"/> too, so a VEGA line and the HUD
    /// name a control the same way.
    /// </summary>
    public static class PadControlNames
    {
        /// <summary>Between two controls of a controls line: "Left stick fly · Right stick steer". Only this spaced dot
        /// separates — the dot inside a segment ("up·down") does not.</summary>
        public const string Separator = " · ";

        /// <summary>
        /// The pad's name for a control: the sticks (<c>Move</c>, <c>Look</c>, by their localized names
        /// <c>ui.key.pad_left_stick</c> / <c>_right_stick</c>), the fixed buttons (<c>Jump</c> the bottom and <c>Crouch</c>
        /// the right face button, <c>Mine</c>/<c>Scan</c> the right and <c>Place</c> the left shoulder button — with the
        /// trigger beside it while <paramref name="triggers"/> is on: "RB/RT"), the d-pad (<c>Hotbar</c>,
        /// <c>ui.key.pad_dpad</c> with the ◄ ► arrows), and every rebindable action by the pad button
        /// <paramref name="buttonOf"/> reports for it (0–19, the face buttons, bumpers, View/Menu, the stick clicks) in
        /// <paramref name="set"/>. Null when the pad has no control for the action — an action the player left unbound,
        /// or a name that is no action at all; the caller decides what to show instead.
        /// </summary>
        /// <param name="action">An <c>InputAction</c> name or one of <see cref="VegaText.PseudoActions"/>.</param>
        /// <param name="buttonOf">The pad button bound to an <c>InputAction</c> name — the player's rebind, else the stock
        /// button — or a negative number when none is.</param>
        /// <param name="set">The player's glyph set.</param>
        /// <param name="triggers">Whether the triggers mine and place too (the controller settings' switch).</param>
        /// <param name="localize">Resolves a locale key (the stick and d-pad names).</param>
        public static string? Glyph(string action, Func<string, int> buttonOf, PadGlyphSet set, bool triggers, Func<string, string> localize)
        {
            switch (action)
            {
                case "Move":
                    return localize("ui.key.pad_left_stick");
                case "Look":
                    return localize("ui.key.pad_right_stick");
                case "Jump":
                    return PadGlyphTable.Button(PadGlyphTable.BottomFace, set);
                case "Crouch":
                    return PadGlyphTable.Button(PadGlyphTable.RightFace, set);
                case "Mine":
                case "Scan":
                    return WithTrigger(PadGlyphTable.Button(PadGlyphTable.RightBumper, set), triggers, PadGlyphTable.Trigger(right: true, set));
                case "Place":
                    return WithTrigger(PadGlyphTable.Button(PadGlyphTable.LeftBumper, set), triggers, PadGlyphTable.Trigger(right: false, set));
                case "Hotbar":
                    return localize("ui.key.pad_dpad") + " " + VegaText.TouchHotbarLabel;
            }

            int button = buttonOf(action);
            return button < 0 ? null : PadGlyphTable.Button(button, set);
        }

        private static string? WithTrigger(string? button, bool triggers, string trigger)
            => triggers && button != null ? button + "/" + trigger : button;

        /// <summary>
        /// A pad controls line from its template: the <see cref="Separator"/>-joined segments with their <c>{key:Action}</c>
        /// tokens resolved by <paramref name="glyph"/>. A segment whose token has no pad control (<paramref name="glyph"/>
        /// returns null or empty — the player unbound the button) is dropped whole, never shown as a bare word, a "?" or a
        /// "—"; a segment without a token stays. A pad glyph that wears its own parentheses keeps one pair
        /// (<see cref="LineText.ExpandKeyTokens"/>).
        /// </summary>
        public static string ComposeLine(string? template, Func<string, string?> glyph)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var kept = new List<string>();
            foreach (string segment in template!.Split(new[] { Separator }, StringSplitOptions.None))
            {
                bool missing = false;
                string shown = LineText.ExpandKeyTokens(segment, action =>
                {
                    string? name = glyph(action);
                    if (string.IsNullOrEmpty(name))
                    {
                        missing = true;
                        return action;
                    }

                    return name!;
                }).Trim();

                if (!missing && shown.Length > 0)
                {
                    kept.Add(shown);
                }
            }

            return string.Join(Separator, kept);
        }
    }
}
