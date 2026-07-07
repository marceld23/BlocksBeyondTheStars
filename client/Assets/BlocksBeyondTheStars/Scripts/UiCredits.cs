// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

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

            UiKit.AddImage(root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.55f));
            float px = 560f, pw = 800f, py = 230f, ph = 620f;
            UiKit.AddPanel(root, px, py, pw, ph, UiKit.Panel);

            UiKit.AddLogo(root, px + 40, py + 36, pw - 80, 44, shell.L("ui.credits.title"), 30);

            // Scrollable body between the title and the Back button. The button is laid out last (below),
            // and the body is clipped to this region, so the two can never overlap regardless of text length.
            float bodyY = py + 110f;
            float bodyH = ph - 200f;                 // leaves a gap above the Back button row
            BuildBodyScroll(root, px + 40f, bodyY, pw - 80f, bodyH, shell.L("ui.credits.body"));

            UiKit.AddButton(root, px + 40, py + ph - 76, 220, 52, shell.L("ui.menu.back"), () => shell.CloseCredits());
            return canvas.gameObject;
        }

        /// <summary>Builds a vertical ScrollRect clipped to the given rect containing the (wrapped) body text,
        /// with a permanent scrollbar along the right edge — mirroring the settings screen's scroll pattern.</summary>
        private static void BuildBodyScroll(Transform root, float x, float y, float w, float h, string body)
        {
            const float scrollbarW = 12f;
            const float gutter = 8f;                 // gap between text and scrollbar
            float textW = w - scrollbarW - gutter;

            var viewGo = new GameObject("CreditsScroll", typeof(RectTransform));
            viewGo.transform.SetParent(root, false);
            UiKit.Place(viewGo, x, y, w, h);

            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            viewGo.AddComponent<RectMask2D>();

            // Near-transparent graphic so the wheel/drag has something to hit over the text area.
            var hit = viewGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            var text = UiKit.AddText(content, 0f, 0f, textW, h, body, 18, UiKit.TextCol, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Size the content to the text so the viewport can scroll to the very bottom. preferredHeight
            // reflects wrapping at textW (set by AddText's RectTransform width above).
            float contentH = Mathf.Max(h, text.preferredHeight + 8f);
            content.sizeDelta = new Vector2(0f, contentH);

            scroll.viewport = viewGo.GetComponent<RectTransform>();
            scroll.content = content;

            UiKit.AddVerticalScrollbar(root, scroll, x + w - scrollbarW, y, scrollbarW, h);
        }
    }
}
