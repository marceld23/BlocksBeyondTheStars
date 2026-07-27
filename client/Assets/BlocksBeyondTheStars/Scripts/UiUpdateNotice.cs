// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The once-per-session "update available" dialog over the main menu (#543). The quiet startup
    /// check (<see cref="ClientUpdater.CheckForNoticeOnStartup"/>) found a newer official release;
    /// this offers the actual install — the same Velopack download-and-restart flow as the settings
    /// screen's manual check — or "later", which dismisses the notice for the rest of the session.
    /// AppShell spawns it on the MainMenu phase (own canvas, so the menu itself never needs a rebuild)
    /// and tears it down when the phase changes or the notice is dismissed.
    /// </summary>
    public static class UiUpdateNotice
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("UpdateNoticeUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // gamepad can answer the dialog too

            var dim = UiKit.AddModalDim(root);
            var dlg = UiKit.AddDialogPanel(dim.transform, 610f, 330f, 700f, 420f);
            UiKit.AddText(dlg, 30f, 24f, 640f, 32f, shell.L("ui.update.title"), 24,
                UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var body = UiKit.AddText(dlg, 40f, 84f, 620f, 130f,
                string.Format(shell.L("ui.update.body"), ClientUpdater.NoticeVersion, AppShell.Version),
                18, UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            var status = UiKit.AddText(dlg, 76f, 226f, 584f, 60f, "", 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddSpinner(dlg, 42f, 224f, 26f, status); // spins while the status text is in progress ("…")

            Button installBtn = null;
            Button laterBtn = null;

            // Mirrors the settings screen's status labels so the two update UIs speak the same language.
            void Refresh()
            {
                if (status == null)
                {
                    return; // dialog torn down while the download ran
                }

                status.text = ClientUpdater.State switch
                {
                    UpdateState.Checking => shell.L("ui.settings.update_checking"),
                    UpdateState.Downloading => shell.L("ui.settings.update_downloading") + " " + ClientUpdater.Detail,
                    UpdateState.Restarting => shell.L("ui.settings.update_restarting"),
                    UpdateState.UpToDate => shell.L("ui.settings.update_uptodate"),
                    UpdateState.NotInstalled => shell.L("ui.settings.update_notinstalled"),
                    UpdateState.Failed => shell.L("ui.settings.update_failed") + " " + ClientUpdater.Detail,
                    _ => string.Empty,
                };

                // No second click while the download runs — and no "later" either: the restart is coming.
                installBtn.interactable = !ClientUpdater.Busy;
                laterBtn.interactable = !ClientUpdater.Busy;
            }

            installBtn = UiKit.AddButton(dlg, 40f, 330f, 320f, 54f, shell.L("ui.update.install"), () =>
            {
                if (!ClientUpdater.Busy)
                {
                    ClientUpdater.CheckForUpdates(shell.Settings.UpdateFeedUrl, Refresh);
                }
            }, "btn_join");

            laterBtn = UiKit.AddButton(dlg, 380f, 330f, 280f, 54f, shell.L("ui.update.later"), () =>
            {
                ClientUpdater.NoticeDismissed = true; // AppShell.Update tears the dialog down
            }, "btn_exit");

            return canvas.gameObject;
        }
    }
}
