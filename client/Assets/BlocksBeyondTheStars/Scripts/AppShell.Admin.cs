// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pause menu's admin rows (#3509): <b>Admin</b> opens the admin view (only for a player the server lets in — the
    /// gamepad's way to it, F8 being a keyboard key) and <b>Call an admin</b> (#3508, every other player) opens a small box
    /// for a few words; Send hands them to the server (<see cref="NetworkClient.SendCallAdmin"/>) and returns to the world,
    /// where the confirmation toast shows. While the box is up the pause panel behind it is hidden, so the pad only walks the
    /// box; Esc closes the box first.
    /// </summary>
    public sealed partial class AppShell
    {
        private const float CallBoxW = 640f;

        private GameObject _pauseOverlay;
        private GameObject _callAdminDialog;
        private InputField _callAdminInput;

        /// <summary>True while the "Call an admin" box is up.</summary>
        private bool CallAdminDialogOpen => _callAdminDialog != null;

        private static bool AdminRowOffered(GameBootstrap boot) => boot?.Network != null && boot.Network.HasAdminView;

        /// <summary>Every player in a world whose server files the call — except an admin, who has the Admin row instead (a
        /// singleplayer world's player is its world admin: calling oneself would only file a report nobody else reads).</summary>
        private static bool CallAdminRowOffered(GameBootstrap boot) => boot?.Network != null && boot.Network.OffersCallAdmin;

        /// <summary>How many admin rows the pause menu gets.</summary>
        private static int PauseAdminRowCount(GameBootstrap boot) => (AdminRowOffered(boot) ? 1 : 0) + (CallAdminRowOffered(boot) ? 1 : 0);

        /// <summary>Adds the admin rows at <paramref name="y"/>; returns the next row's y.</summary>
        private float AddPauseAdminRows(Transform panel, float y, GameBootstrap boot)
        {
            if (AdminRowOffered(boot))
            {
                UiKit.AddFitButton(panel, 90f, y, 300f, 56f, L("ui.pause.admin"), OpenAdminView);
                y += PauseRow;
            }

            if (CallAdminRowOffered(boot))
            {
                UiKit.AddFitButton(panel, 90f, y, 300f, 56f, L("ui.pause.call_admin"), OpenCallAdminDialog);
                y += PauseRow;
            }

            return y;
        }

        /// <summary>Back to the world, then the admin view (it enters admin mode first when needed).</summary>
        private void OpenAdminView()
        {
            var boot = Boot();
            CancelQuit();
            boot?.Context.Panel<AdminPanelUi>()?.RequestOpen();
        }

        private void OpenCallAdminDialog()
        {
            if (_quitDialog == null || CallAdminDialogOpen)
            {
                return;
            }

            string hint = L("ui.pause.call_admin_hint");
            float hintH = Mathf.Ceil(UiKit.MeasureHeight(hint, CallBoxW - 56f, 17, FontStyle.Normal, false));
            float h = 76f + hintH + 16f + 52f + 22f + 52f + 28f;
            var (overlay, panel) = UiKit.AddModalOverlay(_quitDialog.transform, (1920f - CallBoxW) / 2f, (1080f - h) / 2f, CallBoxW, h);
            _callAdminDialog = overlay;
            if (_pauseOverlay != null)
            {
                _pauseOverlay.SetActive(false); // the pad walks only the box
            }

            var title = UiKit.AddText(panel, 28f, 20f, CallBoxW - 56f, 44f, L("ui.pause.call_admin_title"), 24, UiKit.Cyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelLines(title, 24, 16, 14, 1);
            var hintText = UiKit.AddText(panel, 28f, 76f, CallBoxW - 56f, hintH, hint, 17, UiKit.TextCol, TextAnchor.UpperLeft);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float y = 76f + hintH + 16f;
            _callAdminInput = UiKit.AddInput(panel, 28f, y, CallBoxW - 56f, 52f, string.Empty, null, L("ui.pause.call_admin_placeholder"), 200);
            y += 52f + 22f;
            UiKit.AddFitButton(panel, 28f, y, 300f, 52f, L("ui.pause.call_admin_send"), SendCallAdmin);
            UiKit.AddFitButton(panel, CallBoxW - 28f - 240f, y, 240f, 52f, L("ui.pause.call_admin_cancel"), CloseCallAdminDialog);
            UiKit.OpenModal(overlay);
            if (InputMap.ActiveDevice == InputDeviceKind.KeyboardMouse)
            {
                _callAdminInput.ActivateInputField(); // type at once; the pad opens its keyboard on the field (#1211)
            }
        }

        private void SendCallAdmin()
        {
            string text = _callAdminInput != null ? (_callAdminInput.text ?? string.Empty).Trim() : string.Empty;
            if (text.Length == 0)
            {
                return; // nothing to send yet — the box stays
            }

            Boot()?.Network?.SendCallAdmin(text);
            CancelQuit(); // back to the world, where the server's confirmation shows (closes the box too)
        }

        private void CloseCallAdminDialog()
        {
            if (_callAdminDialog != null)
            {
                UiKit.ReleaseTextFieldFocus(_callAdminDialog.transform);
                _callAdminDialog.SetActive(false); // Destroy() only lands at the end of the frame
                Destroy(_callAdminDialog);
            }

            _callAdminDialog = null;
            _callAdminInput = null;
            if (_pauseOverlay != null)
            {
                _pauseOverlay.SetActive(true);
            }
        }
    }
}
