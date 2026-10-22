// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The credits screen in the new uGUI design (replacing the IMGUI version): a themed panel
    /// with the title + a scrollable body text and a Back button, on a DPI-independent canvas. The body
    /// lives in a masked ScrollRect (with a visible scrollbar) so the long, localized credits text can be
    /// read in full without overflowing the panel or being covered by the Back button.</summary>
    public static class UiCredits
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("CreditsUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // gamepad can leave the screen (inert on KB/mouse)

            float px = 560f, pw = 800f, py = 230f, ph = 620f;
            UiKit.AddModalOverlay(root, px, py, pw, ph); // shared scrim + opaque panel (#588)

            UiKit.AddLogo(root, px + 40, py + 36, pw - 80, 44, shell.L("ui.credits.title"), 30);

            // Scrollable body between the title and the Back button. The button is laid out last (below),
            // and the body is clipped to this region, so the two can never overlap regardless of text length.
            float bodyY = py + 110f;
            float bodyH = ph - 200f;                 // leaves a gap above the Back button row
            BuildBodyScroll(root, px + 40f, bodyY, pw - 80f, bodyH, shell.L("ui.credits.body"));

            UiKit.AddButton(root, px + 40, py + ph - 76, 220, 52, shell.L("ui.menu.back"), () => shell.CloseCredits());

            // Replay the once-per-install intro cinematic (#759) — the phase change tears this screen down.
            UiKit.AddButton(root, px + pw - 260, py + ph - 76, 220, 52, shell.L("ui.credits.watch_intro"),
                () => shell.PlayIntroFromCredits());
            return canvas.gameObject;
        }

        /// <summary>Builds a vertical ScrollRect clipped to the given rect containing the (wrapped) body text,
        /// with a permanent scrollbar along the right edge — mirroring the settings screen's scroll pattern.</summary>
        private static void BuildBodyScroll(Transform root, float x, float y, float w, float h, string body)
        {
            const float scrollbarW = 12f;
            const float gutter = 8f;                 // gap between text and scrollbar
            float textW = w - scrollbarW - gutter;

            // The viewport's near-transparent hit graphic gives the wheel/drag something to hit over the text area.
            var content = UiKit.AddScrollViewport(root, "CreditsScroll", x, y, w, h, out var scroll);

            var text = UiKit.AddText(content, 0f, 0f, textW, h, body, 18, UiKit.TextCol, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Size the content to the text so the viewport can scroll to the very bottom. preferredHeight
            // reflects wrapping at textW (set by AddText's RectTransform width above).
            float contentH = Mathf.Max(h, text.preferredHeight + 8f);
            content.sizeDelta = new Vector2(0f, contentH);

            // The Text's own rect must span the whole content too: RectMask2D culls a renderer as soon as its
            // RECT (not its overflowing glyphs) leaves the mask, so a viewport-tall rect at the top of a taller
            // content made the entire text vanish once scrolled past the first page (#1384).
            text.rectTransform.sizeDelta = new Vector2(textW, contentH);

            UiKit.AddVerticalScrollbar(root, scroll, x + w - scrollbarW, y, scrollbarW, h);
        }
    }
}
