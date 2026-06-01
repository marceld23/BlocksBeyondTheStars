using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// The uGUI main menu (M27 UI rework): the sci-fi mockup look built in code via <see cref="UiKit"/>
    /// — a SYSTEM CHECK panel, the SPACECRAFT title, framed cyan menu buttons wired to the shell, a
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
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 30f;
                UiKit.AddText(root, 60f, yy, 190f, 22f, shell.L(sysKeys[i]), 16, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Title ---
            UiKit.AddText(root, 360f, 70f, 800f, 96f, "SPACECRAFT", 72, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // --- Menu buttons ---
            const float bx = 90f, bw = 440f, bh = 56f, gap = 66f;
            float by = 330f;
            UiKit.AddButton(root, bx, by, bw, bh, shell.L("ui.menu.singleplayer"), shell.StartSingleplayer);
            UiKit.AddButton(root, bx, by + gap, bw, bh, shell.L("ui.menu.join"), shell.StartJoin);
            UiKit.AddButton(root, bx, by + gap * 2f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings);
            UiKit.AddButton(root, bx, by + gap * 3f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits));
            UiKit.AddButton(root, bx, by + gap * 4f, bw, bh, shell.L("ui.menu.quit"), shell.Quit);

            // --- World / server info panel (bottom-right, decorative) ---
            UiKit.AddPanel(root, 1290f, 650f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 666f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 706f, shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 770f, shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 834f, shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(root, 1420f, 1030f, 460f, 26f, shell.L("ui.menu.wishlist"), 16, UiKit.Cyan, TextAnchor.MiddleRight, FontStyle.Bold);

            return canvas.gameObject;
        }

        private static void AddInfo(Transform root, float y, string title, string desc)
        {
            UiKit.AddText(root, 1314f, y, 540f, 22f, title, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1314f, y + 24f, 540f, 22f, desc, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }
    }
}
