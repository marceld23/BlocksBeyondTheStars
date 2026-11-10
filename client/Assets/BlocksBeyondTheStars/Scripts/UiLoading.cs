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
    /// waits for the game data, from that download (#2186) — and steps the story line over the bar
    /// (<see cref="LoadingLore"/>, #3450). AppShell spawns it on the Loading phase and destroys it on leaving.
    /// </summary>
    public static class UiLoading
    {
        // Both panels share one top edge; since #3450 they are taller, so the story line has room above the bar.
        private const float PanelY = 724f, PanelH = 156f;

        // #3450: the story line in big letters — on the bar's left edge, as wide as the progress panel allows (its right
        // edge at 1180 minus the 20-unit inset), one line, shrunk only if a language needs it.
        private const float StoryX = 110f, StoryY = 738f, StoryW = 1050f, StoryH = 64f;
        private const int StorySize = 42, StoryMinSize = 28;

        /// <summary>#3450: how long a new story line takes to fade in.</summary>
        private const float StoryFadeSeconds = 0.25f;

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("LoadingUI");
            var root = canvas.transform;

            // Chrome echoing the menu: system check + title + version, on the menu's left line (#2953).
            const float sx = UiKit.ShellColumnX;
            UiKit.AddPanel(root, sx, 40f, UiKit.SystemCheckWidth, 200f, UiKit.PanelFill);
            UiKit.AddText(root, sx + 20f, 54f, UiKit.SystemCheckWidth - 40f, 22f, shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
            string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 28f;
                UiKit.AddIcon(root, sx + 6f, yy, 18f, sysIcons[i]);
                UiKit.AddText(root, sx + 32f, yy, UiKit.SystemCheckLabelW, 22f, shell.L(sysKeys[i]), 15, UiKit.TextCol);
                UiKit.AddText(root, sx + UiKit.SystemCheckOkX, yy, 50f, 22f, shell.L("ui.sys.ok"), 15, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            UiKit.AddLogo(root, UiKit.ShellTitleX, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 56);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Progress bar — on the same left line; its right edge stays at 1180, clear of the TIP panel. Over the bar the
            // story line (#3450; the browser's data-download line until the game data is in) — the updater writes it.
            UiKit.AddPanel(root, sx, PanelY, 1180f - sx, PanelH, UiKit.PanelFill);
            var title = UiKit.AddText(root, StoryX, StoryY, StoryW, StoryH, string.Empty, StorySize, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(root, 110f, 824f, 880f, 30f, UiKit.SolidSprite, new Color(0.04f, 0.10f, 0.18f, 0.9f));
            var fill = UiKit.AddImage(root, 110f, 824f, 880f, 30f, UiKit.SolidSprite, UiKit.Cyan);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            var percent = UiKit.AddText(root, 1010f, 812f, 150f, 50f, "0%", 30, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // TIP panel.
            UiKit.AddPanel(root, 1210f, PanelY, 630f, PanelH, UiKit.PanelFill);
            UiKit.AddText(root, 1240f, PanelY + 16f, 120f, 24f, shell.L("ui.loading.tip_label"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            // #2257: one of a few tips, a new one each time the screen comes up — the Crystal Net's among them.
            string[] tipKeys = { "ui.loading.tip", "ui.loading.tip.1", "ui.loading.tip.2", "ui.loading.tip.3", "ui.loading.tip.4" };
            var tip = UiKit.AddText(root, 1240f, PanelY + 42f, 570f, PanelH - 56f, shell.L(tipKeys[Random.Range(0, tipKeys.Length)]), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Status row: the server's boot stage while it builds the world (#3260), else the flavour line.
            var status = UiKit.AddText(root, 110f, 910f, 1700f, 24f, shell.L("ui.loading.status"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft);

            var updater = canvas.gameObject.AddComponent<LoadingUpdater>();
            updater.Shell = shell;
            updater.Status = status;
            updater.Title = title;
            updater.Fill = fill;
            updater.Percent = percent;
            return canvas.gameObject;
        }

        /// <summary>The data-download line on the loading screen: the localized words plus "done/total" once the
        /// manifest is known. The count is plain digits, so the key holds the words only (#2186).</summary>
        public static string DataLine(string words, int done, int total)
            => total > 0 ? words + "  " + Mathf.Clamp(done, 0, total) + "/" + total : words;

        /// <summary>Drives the progress bar + percentage from the shell's load progress each frame, and the story line
        /// over it (#3450). While the browser still downloads the game data, the title says so and the bar follows the
        /// download instead of sitting at 0 % (#2186) — the world cannot start before that data is in.</summary>
        private sealed class LoadingUpdater : MonoBehaviour
        {
            public AppShell Shell;
            public Text Status;
            public Text Title;
            public Image Fill;
            public Text Percent;

            private int _shownDone = -1, _shownTotal = -1; // the data line last written, so a still frame allocates nothing
            private string _shownStatusKey;                // the status row's key last written (#3260)
            private int _storyLine = LoadingLore.None;     // the story line on show (#3450) — it never steps back
            private float _storySeconds;                   // how long that line has been on show (unscaled seconds)
            private Color _titleColor = UiKit.TextCol;
            private float _titleAlpha = 1f;                // the title's fade-in, 0..1

            // LateUpdate, not Update: the shell decides the hand-off in its own Update, and the frame that says
            // "Building the world…" must already carry it when it renders (#3261) — whatever order the two run in.
            private void LateUpdate()
            {
                if (Shell == null)
                {
                    return;
                }

                // #3260: the stage the bundled server announced last, grouped into a message a child understands.
                string statusKey = Shell.LoadingStatusKey;
                if (Status != null && statusKey != _shownStatusKey)
                {
                    _shownStatusKey = statusKey;
                    Status.text = Shell.L(statusKey);
                }

                float p = !Shell.ContentReady && StreamingAssetsCache.UsesRemoteStreamingAssets ? ShowDataDownload() : ShowStory();
                if (Fill != null)
                {
                    Fill.fillAmount = p;
                }

                if (Percent != null)
                {
                    Percent.text = Mathf.RoundToInt(p * 100f) + "%";
                }
            }

            /// <summary>#2186: the browser still downloads the game data — the title counts the files, the bar follows them.
            /// Returns the bar's fill.</summary>
            private float ShowDataDownload()
            {
                int total = StreamingAssetsCache.RemoteFileTotal;
                int done = Mathf.Min(StreamingAssetsCache.RemoteFileCount, total);
                if (Title != null && (done != _shownDone || total != _shownTotal))
                {
                    _shownDone = done;
                    _shownTotal = total;
                    SetTitle(DataLine(Shell.L("ui.loading.data"), done, total), UiKit.TextCol, false);
                }

                return total > 0 ? (float)done / total : 0f;
            }

            /// <summary>#3450: Justus' story lines step through the bar, each up at least
            /// <see cref="LoadingLore.MinLineSeconds"/> (the time on the current line runs on unscaled time); "You awaken!"
            /// is the last thing shown before the world starts. Returns the bar's fill.</summary>
            private float ShowStory()
            {
                float p = Shell.LoadingProgress;
                bool handingOff = Shell.LoadingHandingOff;
                _storySeconds += Time.unscaledDeltaTime;
                int line = LoadingLore.LineFor(p, handingOff, _storyLine, _storySeconds);
                if (line != _storyLine)
                {
                    _storyLine = line;
                    _storySeconds = 0f;
                    if (Title != null)
                    {
                        // The hand-off frame is the one the world build freezes on — its line shows at full strength at once.
                        SetTitle(Shell.L(LoadingLore.KeyFor(line)), line == LoadingLore.Last ? UiKit.Cyan : UiKit.TextCol, !handingOff);
                    }
                }

                if (Title != null && _titleAlpha < 1f)
                {
                    _titleAlpha = handingOff ? 1f : Mathf.Min(1f, _titleAlpha + Time.unscaledDeltaTime / StoryFadeSeconds);
                    ApplyTitleColor();
                }

                return p;
            }

            /// <summary>Writes the title on one line in its box — shrunk only when a language needs it — and starts its
            /// fade-in (or shows it at once).</summary>
            private void SetTitle(string text, Color color, bool fadeIn)
            {
                Title.text = text;
                UiKit.FitLabelSingleLine(Title, StoryW, StoryMinSize, StorySize);
                Title.verticalOverflow = VerticalWrapMode.Overflow; // a taller fallback font must never truncate the line away
                _titleColor = color;
                _titleAlpha = fadeIn ? 0f : 1f;
                ApplyTitleColor();
            }

            private void ApplyTitleColor() => Title.color = new Color(_titleColor.r, _titleColor.g, _titleColor.b, _titleColor.a * _titleAlpha);
        }
    }
}
