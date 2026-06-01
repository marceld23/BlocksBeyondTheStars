using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// The uGUI loading screen (M27 UI rework): the same sci-fi chrome as the menu plus a progress
    /// bar, a TIP panel and a status row, over the animated <see cref="MenuBackground"/>. A small
    /// updater drives the bar from the shell's (time-based) load progress. AppShell spawns it on the
    /// Loading phase and destroys it on leaving. (Flavour strings are English for now — localised next.)
    /// </summary>
    public static class UiLoading
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("LoadingUI");
            var root = canvas.transform;

            // Chrome echoing the menu: system check + title + version.
            UiKit.AddPanel(root, 40f, 40f, 280f, 200f, UiKit.PanelFill);
            UiKit.AddText(root, 60f, 54f, 250f, 22f, shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 28f;
                UiKit.AddText(root, 60f, yy, 190f, 22f, shell.L(sysKeys[i]), 15, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 15, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            UiKit.AddLogo(root, 360f, 70f, 800f, 96f, "SPACECRAFT", 64);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Progress bar.
            UiKit.AddPanel(root, 80f, 760f, 1100f, 120f, UiKit.PanelFill);
            UiKit.AddText(root, 110f, 776f, 760f, 32f, shell.L("ui.loading.title"), 26, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(root, 110f, 824f, 880f, 30f, UiKit.SolidSprite, new Color(0.04f, 0.10f, 0.18f, 0.9f));
            var fill = UiKit.AddImage(root, 110f, 824f, 880f, 30f, UiKit.SolidSprite, UiKit.Cyan);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            var percent = UiKit.AddText(root, 1010f, 812f, 150f, 50f, "0%", 30, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // TIP panel.
            UiKit.AddPanel(root, 1210f, 760f, 630f, 120f, UiKit.PanelFill);
            UiKit.AddText(root, 1240f, 776f, 120f, 24f, shell.L("ui.loading.tip_label"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var tip = UiKit.AddText(root, 1240f, 802f, 570f, 64f, shell.L("ui.loading.tip"), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Status row.
            UiKit.AddText(root, 110f, 910f, 1700f, 24f, shell.L("ui.loading.status"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft);

            var updater = canvas.gameObject.AddComponent<LoadingUpdater>();
            updater.Shell = shell;
            updater.Fill = fill;
            updater.Percent = percent;
            return canvas.gameObject;
        }

        /// <summary>Drives the progress bar + percentage from the shell's load progress each frame.</summary>
        private sealed class LoadingUpdater : MonoBehaviour
        {
            public AppShell Shell;
            public Image Fill;
            public Text Percent;

            private void Update()
            {
                if (Shell == null)
                {
                    return;
                }

                float p = Shell.LoadingProgress;
                if (Fill != null)
                {
                    Fill.fillAmount = p;
                }

                if (Percent != null)
                {
                    Percent.text = Mathf.RoundToInt(p * 100f) + "%";
                }
            }
        }
    }
}
