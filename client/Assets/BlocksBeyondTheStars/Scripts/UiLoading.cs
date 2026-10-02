// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The uGUI loading screen (M27 UI rework): the same sci-fi chrome as the menu plus a progress
    /// bar, a TIP panel and a status row, over the animated <see cref="MenuBackground"/>. A small
    /// updater drives the bar from the shell's (time-based) load progress — or, while a browser world
    /// waits for the game data, from that download (#2186). AppShell spawns it on the Loading phase and
    /// destroys it on leaving. (Flavour strings are English for now — localised next.)
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
            string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 28f;
                UiKit.AddIcon(root, 46f, yy, 18f, sysIcons[i]);
                UiKit.AddText(root, 72f, yy, 178f, 22f, shell.L(sysKeys[i]), 15, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 15, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            UiKit.AddLogo(root, 360f, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 56);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Progress bar.
            UiKit.AddPanel(root, 80f, 760f, 1100f, 120f, UiKit.PanelFill);
            var title = UiKit.AddText(root, 110f, 776f, 760f, 32f, shell.L("ui.loading.title"), 26, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
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
            updater.Title = title;
            updater.Fill = fill;
            updater.Percent = percent;
            return canvas.gameObject;
        }

        /// <summary>The data-download line on the loading screen: the localized words plus "done/total" once the
        /// manifest is known. The count is plain digits, so the key holds the words only (#2186).</summary>
        public static string DataLine(string words, int done, int total)
            => total > 0 ? words + "  " + Mathf.Clamp(done, 0, total) + "/" + total : words;

        /// <summary>Drives the progress bar + percentage from the shell's load progress each frame. While the
        /// browser still downloads the game data, the title says so and the bar follows the download instead of
        /// sitting at 0 % (#2186) — the world cannot start before that data is in.</summary>
        private sealed class LoadingUpdater : MonoBehaviour
        {
            public AppShell Shell;
            public Text Title;
            public Image Fill;
            public Text Percent;

            private int _shownDone = -1, _shownTotal = -1; // the data line last written, so a still frame allocates nothing

            private void Update()
            {
                if (Shell == null)
                {
                    return;
                }

                float p;
                if (!Shell.ContentReady && StreamingAssetsCache.UsesRemoteStreamingAssets)
                {
                    int total = StreamingAssetsCache.RemoteFileTotal;
                    int done = Mathf.Min(StreamingAssetsCache.RemoteFileCount, total);
                    p = total > 0 ? (float)done / total : 0f;
                    if (Title != null && (done != _shownDone || total != _shownTotal))
                    {
                        _shownDone = done;
                        _shownTotal = total;
                        Title.text = DataLine(Shell.L("ui.loading.data"), done, total);
                    }
                }
                else
                {
                    p = Shell.LoadingProgress;
                    if (_shownTotal >= 0 && Title != null)
                    {
                        _shownDone = _shownTotal = -1;
                        Title.text = Shell.L("ui.loading.title");
                    }
                }

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
