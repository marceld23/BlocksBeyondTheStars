// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which layout the on-screen button NAMES follow (#1219). Purely cosmetic: a pad reports the
    /// same button numbers whatever is printed on it, so this only picks the wording, matched by physical
    /// position — see <see cref="PadGlyphTable"/>. Stored in the client settings (<c>ClientSettings.PadGlyphs</c>,
    /// JsonUtility writes it as its number); it lives here since #2507 so the headless suite can name the buttons.</summary>
    public enum PadGlyphSet
    {
        Xbox,
        PlayStation,
        Nintendo,
    }

    /// <summary>
    /// The names of the pad's controls in the player's glyph set — the one table every pad hint reads
    /// (<c>InputMap.PadGlyph</c> / <c>InputMap.PadTriggerGlyph</c> delegate here since #2507, so the composed controls
    /// lines can be tested headless). A button is named by its NUMBER (<c>KeyCode.JoystickButtonN</c> = N, the XInput
    /// layout of <c>GamepadInputSource</c>): the number never changes, only the name does, matched by PHYSICAL POSITION
    /// (button 0 is the bottom face button: Xbox A, PlayStation Cross, Nintendo B).
    ///
    /// The PlayStation shapes are spelled out rather than drawn: the bundled UI font (Rajdhani) is Latin-only, so
    /// ✕ ○ □ △ would come out as missing-glyph boxes — and glyph SPRITES would cost the WebGL build more than a
    /// control hint is worth.
    /// </summary>
    public static class PadGlyphTable
    {
        /// <summary>The bottom face button (Xbox A) — jump, and up on a spacewalk.</summary>
        public const int BottomFace = 0;

        /// <summary>The right face button (Xbox B) — crouch, and down on a spacewalk.</summary>
        public const int RightFace = 1;

        /// <summary>The left shoulder button (Xbox LB) — place.</summary>
        public const int LeftBumper = 4;

        /// <summary>The right shoulder button (Xbox RB) — mine, and fire at the helm.</summary>
        public const int RightBumper = 5;

        /// <summary>The highest button number a pad binding can hold (<c>KeyCode.JoystickButton19</c>).</summary>
        public const int LastButton = 19;

        /// <summary>The name of pad button <paramref name="index"/> (0–19) in <paramref name="set"/>, or null for a number
        /// outside the pad's range (an unbound action).</summary>
        public static string? Button(int index, PadGlyphSet set) => index switch
        {
            0 => set switch { PadGlyphSet.PlayStation => "(Cross)", PadGlyphSet.Nintendo => "(B)", _ => "(A)" },
            1 => set switch { PadGlyphSet.PlayStation => "(Circle)", PadGlyphSet.Nintendo => "(A)", _ => "(B)" },
            2 => set switch { PadGlyphSet.PlayStation => "(Square)", PadGlyphSet.Nintendo => "(Y)", _ => "(X)" },
            3 => set switch { PadGlyphSet.PlayStation => "(Triangle)", PadGlyphSet.Nintendo => "(X)", _ => "(Y)" },
            4 => set switch { PadGlyphSet.PlayStation => "L1", PadGlyphSet.Nintendo => "L", _ => "LB" },
            5 => set switch { PadGlyphSet.PlayStation => "R1", PadGlyphSet.Nintendo => "R", _ => "RB" },
            // "View" is what the button is called on every Xbox pad since the One (the two overlapping rectangles);
            // "Back" was the 360's name, which is Unity's — and no player looking at a modern pad found it, so VEGA
            // lines seemed undismissable on a controller.
            6 => set switch { PadGlyphSet.PlayStation => "Share", PadGlyphSet.Nintendo => "-", _ => "View" },
            // Same story for "Start": since the Xbox One it is the "Menu" button (three lines, right of the logo). A
            // player reading "Start" reached for the Xbox-logo button — which Windows' Game Bar owns — and concluded
            // the game had no way into its menus on a pad.
            7 => set switch { PadGlyphSet.PlayStation => "Options", PadGlyphSet.Nintendo => "+", _ => "Menu" },
            8 => set == PadGlyphSet.Xbox ? "LS" : "L3",
            9 => set == PadGlyphSet.Xbox ? "RS" : "R3",
            >= 10 and <= LastButton => "B" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };

        /// <summary>The name of a trigger in <paramref name="set"/> (#2461): RT/LT on Xbox, R2/L2 on PlayStation, ZR/ZL on
        /// Nintendo. The triggers are an axis, not a button, so <see cref="Button"/> cannot name them.</summary>
        public static string Trigger(bool right, PadGlyphSet set) => set switch
        {
            PadGlyphSet.PlayStation => right ? "R2" : "L2",
            PadGlyphSet.Nintendo => right ? "ZR" : "ZL",
            _ => right ? "RT" : "LT",
        };
    }
}
