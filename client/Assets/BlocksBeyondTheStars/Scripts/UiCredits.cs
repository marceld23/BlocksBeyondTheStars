// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The credits screen in the new uGUI design (replacing the IMGUI version): a themed panel
    /// with the title + body text and a Back button, on a DPI-independent canvas.</summary>
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
            var body = UiKit.AddText(root, px + 40, py + 110, pw - 80, ph - 220, shell.L("ui.credits.body"), 18, UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiKit.AddButton(root, px + 40, py + ph - 76, 220, 52, shell.L("ui.menu.back"), () => shell.CloseCredits());
            return canvas.gameObject;
        }
    }
}
