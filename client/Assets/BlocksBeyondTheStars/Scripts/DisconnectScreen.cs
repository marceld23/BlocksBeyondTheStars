// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Full-screen "connection lost" gate (issue #249). Before this existed, a server going away mid-game
    /// left the client silently frozen — the transport's disconnect only flipped a flag nobody surfaced.
    /// Two variants: a planned maintenance restart (<see cref="GameBootstrap.MaintenanceRestartPending"/> —
    /// "the server is restarting, reconnect shortly") and an unexpected connection loss. One button, back to
    /// the menu; no ESC, no click-outside, no auto-dismiss.
    ///
    /// An INTENTIONAL leave (quit dialog → ReturnToMenu) destroys the whole rig, and with it this component,
    /// before its Update could ever show the panel — the disconnect handler only sets a flag, so a teardown
    /// disconnect can never flash the screen.
    /// </summary>
    public sealed class DisconnectScreen : MonoBehaviour
    {
        public GameBootstrap Game;
        public AppShell Shell;

        private Canvas _canvas;
        private GameObject _panel;
        private Text _title;
        private Text _detail;

        private bool _subscribed;
        private bool _pending; // disconnect arrived; show on the next Update
        private bool _shown;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.Disconnected += OnDisconnected;
                _subscribed = true;
            }

            if (_pending && !_shown)
            {
                // A refused join already bounces to the menu with its own notice (AppShell watches
                // JoinRejectedReason) — don't stack the disconnect screen on top of that flow.
                if (Game != null && !string.IsNullOrEmpty(Game.JoinRejectedReason))
                {
                    _pending = false;
                    return;
                }

                Show();
            }

        }

        private void OnDisconnected()
        {
            _pending = true; // flag only — UI work happens in Update, never on a teardown callback
        }

        private void Show()
        {
            EnsureUi();
            bool maintenance = Game != null && Game.MaintenanceRestartPending;
            _title.text = Tr(maintenance ? "ui.disconnect.maint_title" : "ui.disconnect.title");
            _detail.text = Tr(maintenance ? "ui.disconnect.maint_detail" : "ui.disconnect.detail");
            _panel.SetActive(true);
            _shown = true;
            Game?.SetMenuOwner(this, true); // freeze player control + free the cursor under the panel (#413)
        }

        private void OnBackToMenu()
        {
            _panel?.SetActive(false);
            _shown = false;
            _pending = false;
            Shell?.ReturnToMenu(); // destroys the rig (and this component)
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("DisconnectScreen");
            _canvas.sortingOrder = 112; // above everything incl. the maintenance banner (110)
            UiNav.Enable(_canvas.gameObject);

            _panel = new GameObject("DisconnectPanel", typeof(RectTransform));
            var prt = _panel.GetComponent<RectTransform>();
            prt.SetParent(_canvas.transform, false);
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            UiKit.AddModalDim(_panel.transform, 0.92f);
            _title = UiKit.AddText(_panel.transform, 0, 370, 1920, 120, string.Empty, 58,
                new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _detail = UiKit.AddText(_panel.transform, 360, 500, 1200, 140, string.Empty, 32,
                new Color(0.8f, 0.84f, 0.92f), TextAnchor.MiddleCenter);
            UiKit.AddButton(_panel.transform, (1920f - 340f) / 2f, 680, 340, 80, Tr("ui.disconnect.to_menu"), OnBackToMenu);
            _panel.SetActive(false);
        }

        private string Tr(string key)
        {
            string s = Game?.Localizer?.Get(key);
            if (!string.IsNullOrEmpty(s) && s != key)
            {
                return s;
            }

            switch (key)
            {
                case "ui.disconnect.title": return "Connection lost";
                case "ui.disconnect.detail": return "The connection to the server was lost.";
                case "ui.disconnect.maint_title": return "Server restart";
                case "ui.disconnect.maint_detail": return "The server is restarting for maintenance. Please rejoin in a moment.";
                case "ui.disconnect.to_menu": return "Back to menu";
                default: return key;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.Disconnected -= OnDisconnected;
            }
        }
    }
}
