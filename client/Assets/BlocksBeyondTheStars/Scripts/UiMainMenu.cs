// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The uGUI main menu (M27 UI rework): the sci-fi mockup look built in code via <see cref="UiKit"/>
    /// — a SYSTEM CHECK panel, the BLOCKS BEYOND THE STARS title, framed cyan menu buttons wired to the shell, a
    /// tagline and the version. Shown over the animated <see cref="MenuBackground"/>. AppShell spawns
    /// it on the MainMenu phase and destroys it on leaving. Decorative panels (world/server info,
    /// community bar) + editable host/port land in a follow-up.
    /// </summary>
    public static class UiMainMenu
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MainMenuUI");
            var root = canvas.transform;

            // --- SYSTEM CHECK panel (decorative flavour) ---
            UiKit.AddPanel(root, 40f, 40f, 280f, 220f, UiKit.PanelFill);
            UiKit.AddText(root, 60f, 54f, 250f, 22f, shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
            string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 30f;
                UiKit.AddIcon(root, 46f, yy, 18f, sysIcons[i]);
                UiKit.AddText(root, 72f, yy, 178f, 22f, shell.L(sysKeys[i]), 16, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Title ---
            UiKit.AddLogo(root, 360f, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 64);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Connect-to-server dialog (built below; the JOIN button reveals it). Captured by the button.
            GameObject connect = null;

            // --- One-shot notice (e.g. why the last join was refused) ---
            if (!string.IsNullOrEmpty(shell.MenuNotice))
            {
                UiKit.AddText(root, 90f, 286f, 1200f, 28f, shell.MenuNotice, 17,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Menu buttons ---
            const float bx = 90f, bw = 440f, bh = 54f, gap = 62f;
            float by = 322f;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser build: a slimmed "enter your name and play" screen. There is no singleplayer,
            // host, editors or quit in the browser (no local filesystem, no bundled server, and quitting
            // a browser tab is meaningless). The server is preconfigured via Glitch/URL params, so the
            // primary action just joins it; "Connect to a server…" stays as a manual fallback. A name is
            // required so players never join the public realm anonymously. The whole block is guarded so
            // the native client (the #else below) is byte-for-byte unchanged.
            string[] webName = { shell.PlayerName };
            UiKit.AddText(root, bx, by, bw, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(root, bx, by + 28f, bw, 44f, webName[0], v => webName[0] = v);
            var webWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float wby = by + 112f;
            UiKit.AddButton(root, bx, wby, bw, bh, shell.L("ui.menu.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(webName[0]))
                {
                    webWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                shell.PlayerName = webName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                shell.StartJoin();
            }, "btn_join");
            UiKit.AddButton(root, bx, wby + gap, bw, bh, shell.L("ui.menu.connect_manual"), () =>
            {
                if (connect != null)
                {
                    connect.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, wby + gap * 2f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, wby + gap * 3f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
#else
            UiKit.AddButton(root, bx, by, bw, bh, shell.L("ui.menu.singleplayer"), shell.StartSingleplayer, "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap, bw, bh, shell.L("ui.menu.host"), shell.StartHost, "btn_join");
            UiKit.AddButton(root, bx, by + gap * 2f, bw, bh, shell.L("ui.menu.join"), () =>
            {
                if (connect != null)
                {
                    connect.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, by + gap * 3f, bw, bh, shell.L("ui.menu.editors"), () => shell.GoTo(ShellPhase.Editors), "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap * 4f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, by + gap * 5f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
            UiKit.AddButton(root, bx, by + gap * 6f, bw, bh, shell.L("ui.menu.quit"), shell.Quit, "btn_exit");
#endif

            // --- World / server info panel (bottom-right, decorative) ---
            UiKit.AddPanel(root, 1290f, 650f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 666f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 706f, "info_mode", shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 770f, "info_multiplayer", shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 834f, "info_procedural", shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            // The participate / "Join in" overlay (built below); the bottom-right button reveals it.
            GameObject participate = null;
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // "Mach mit" — replaces the old "Wishlist on Steam" line; opens the open-source participate panel.
            UiKit.AddButton(root, 1620f, 1018f, 260f, 48f, shell.L("ui.menu.contribute"),
                () => { if (participate != null) participate.SetActive(true); }, "btn_credits");

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            string[] name = { shell.PlayerName };
            string[] host = { shell.Host };
            string[] port = { shell.Port };
            string[] pass = { "" };
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            connect = dim.gameObject;
            dim.raycastTarget = true; // swallow clicks behind the dialog
            var dlg = UiKit.AddPanel(connect.transform, 660f, 280f, 600f, 520f, UiKit.Panel).transform;
            UiKit.AddText(dlg, 30f, 24f, 540f, 30f, shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(dlg, 30f, 80f, 540f, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, name[0], v => name[0] = v);
            UiKit.AddText(dlg, 30f, 160f, 540f, 22f, shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 186f, 540f, 38f, host[0], v => host[0] = v);
            UiKit.AddText(dlg, 30f, 240f, 540f, 22f, shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 266f, 260f, 38f, port[0], v => port[0] = v);
            UiKit.AddText(dlg, 30f, 320f, 540f, 22f, shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 346f, 540f, 38f, pass[0], v => pass[0] = v);
            UiKit.AddButton(dlg, 30f, 432f, 270f, 54f, shell.L("ui.menu.connect"), () =>
            {
                if (!string.IsNullOrWhiteSpace(name[0]))
                {
                    shell.PlayerName = name[0].Trim();
                    shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                    shell.Settings.Save();
                }

                shell.Host = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                shell.Port = string.IsNullOrWhiteSpace(port[0]) ? shell.Port : port[0].Trim();
                shell.Password = pass[0] ?? "";
                shell.StartJoin();
            }, "btn_join");
            UiKit.AddButton(dlg, 310f, 432f, 260f, 54f, shell.L("ui.menu.back"), () => connect.SetActive(false), "btn_exit");
            connect.SetActive(false);

            // --- Participate / "Join in" overlay (added last so it draws on top; hidden until "Mach mit") ---
            var pdim = UiKit.AddModalDim(root);
            participate = pdim.gameObject;
            var pdlg = UiKit.AddPanel(participate.transform, 560f, 250f, 800f, 580f, UiKit.Panel).transform;
            UiKit.AddText(pdlg, 40f, 26f, 720f, 36f, shell.L("ui.contribute.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            Text Para(float y, float h, string text, int size, Color col)
            {
                var t = UiKit.AddText(pdlg, 40f, y, 720f, h, text, size, col, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                return t;
            }

            Para(82f, 44f, shell.L("ui.contribute.intro"), 18, UiKit.TextCol);
            // Player feedback first (for everyone, in-game) — highlighted; then play, then the GitHub paths.
            Para(138f, 70f, "1.  " + shell.L("ui.contribute.feedback"), 17, UiKit.Ok);
            Para(212f, 50f, "2.  " + shell.L("ui.contribute.play"), 17, UiKit.TextCol);
            Para(266f, 70f, "3.  " + shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
            Para(340f, 50f, "4.  " + shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
            UiKit.AddText(pdlg, 40f, 424f, 720f, 26f, shell.L("ui.contribute.github"), 17, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(pdlg, 270f, 500f, 260f, 52f, shell.L("ui.menu.back"), () => participate.SetActive(false), "btn_exit");
            participate.SetActive(false);

            return canvas.gameObject;
        }

        private static void AddInfo(Transform root, float y, string icon, string title, string desc)
        {
            UiKit.AddIcon(root, 1314f, y + 4f, 32f, icon);
            UiKit.AddText(root, 1356f, y, 500f, 22f, title, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1356f, y + 24f, 500f, 22f, desc, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }
    }
}
