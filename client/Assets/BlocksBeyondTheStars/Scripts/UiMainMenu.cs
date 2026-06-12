using UnityEngine;

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

            // --- Menu buttons ---
            const float bx = 90f, bw = 440f, bh = 54f, gap = 62f;
            float by = 322f;
            UiKit.AddButton(root, bx, by, bw, bh, shell.L("ui.menu.singleplayer"), shell.StartSingleplayer, "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap, bw, bh, shell.L("ui.menu.join"), () => { if (connect != null) connect.SetActive(true); }, "btn_join");
            UiKit.AddButton(root, bx, by + gap * 2f, bw, bh, shell.L("ui.menu.editors"), () => shell.GoTo(ShellPhase.Editors), "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap * 3f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, by + gap * 4f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
            UiKit.AddButton(root, bx, by + gap * 5f, bw, bh, shell.L("ui.menu.quit"), shell.Quit, "btn_exit");

            // --- World / server info panel (bottom-right, decorative) ---
            UiKit.AddPanel(root, 1290f, 650f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 666f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 706f, "info_mode", shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 770f, "info_multiplayer", shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 834f, "info_procedural", shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(root, 1420f, 1030f, 460f, 26f, shell.L("ui.menu.wishlist"), 16, UiKit.Cyan, TextAnchor.MiddleRight, FontStyle.Bold);

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            string[] host = { shell.Host };
            string[] port = { shell.Port };
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            connect = dim.gameObject;
            dim.raycastTarget = true; // swallow clicks behind the dialog
            var dlg = UiKit.AddPanel(connect.transform, 660f, 360f, 600f, 360f, UiKit.Panel).transform;
            UiKit.AddText(dlg, 30f, 24f, 540f, 30f, shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(dlg, 30f, 80f, 540f, 22f, shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, host[0], v => host[0] = v);
            UiKit.AddText(dlg, 30f, 160f, 540f, 22f, shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 186f, 260f, 38f, port[0], v => port[0] = v);
            UiKit.AddButton(dlg, 30f, 272f, 270f, 54f, shell.L("ui.menu.connect"), () =>
            {
                shell.Host = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                shell.Port = string.IsNullOrWhiteSpace(port[0]) ? shell.Port : port[0].Trim();
                shell.StartJoin();
            }, "btn_join");
            UiKit.AddButton(dlg, 310f, 272f, 260f, 54f, shell.L("ui.menu.back"), () => connect.SetActive(false), "btn_exit");
            connect.SetActive(false);

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
