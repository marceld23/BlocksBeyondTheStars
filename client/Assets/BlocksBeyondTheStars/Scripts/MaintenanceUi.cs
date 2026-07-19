// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Maintenance announcements from the server (issue #249). Two shapes:
    /// - Restart countdown: a persistent full-width top banner with a live mm:ss timer. It has NO dismiss
    ///   path at all — a planned restart must stay in the player's face until it happens or is cancelled.
    /// - Info message: a modal (RespawnPrompt pattern: opaque click-swallowing dim, single button) whose OK
    ///   button only unlocks after a short delay so it cannot be clicked away by reflex; after confirming,
    ///   the text lingers in the top banner for a while.
    /// Lives on its own canvas ABOVE the touch controls (sortingOrder 100) — on tablets/WebGL-touch a lower
    /// banner would sit under the on-screen sticks. Deliberately independent of the HUD canvas, which hides
    /// while a menu is open; a maintenance warning must survive menus and loading overlays.
    /// </summary>
    public sealed class MaintenanceUi : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float OkUnlockDelay = 3f;      // anti reflex-click: OK stays disabled this long
        private const float InfoBannerLinger = 60f;  // acknowledged info stays in the banner this long
        private const float CancelBannerLinger = 10f;
        private const float UrgentSeconds = 60f;     // banner turns red below this

        private static readonly Color BannerCalmBg = new Color(0.24f, 0.15f, 0.02f, 0.94f);
        private static readonly Color BannerCalmText = new Color(1f, 0.78f, 0.25f);
        private static readonly Color BannerUrgentBg = new Color(0.34f, 0.05f, 0.05f, 0.96f);
        private static readonly Color BannerUrgentText = new Color(1f, 0.55f, 0.45f);

        private Canvas _canvas;
        private GameObject _banner;
        private Image _bannerBg;
        private Text _bannerText;
        private GameObject _modal;
        private Text _modalText;
        private Button _modalOk;
        private float _okDelay;

        private bool _subscribed;
        private bool _countdownActive;
        private bool _restartingNow;
        private float _deadline;              // Time.unscaledTime of the restart
        private string _freeText = string.Empty;
        private float _lingerRemaining;       // transient banner (acked info / cancel note)
        private string _lingerText = string.Empty;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.MaintenanceNoticeReceived += OnNotice;
                _subscribed = true;
            }

            if (_modal != null && _modal.activeSelf)
            {
                if (_okDelay > 0f)
                {
                    _okDelay -= Time.unscaledDeltaTime;
                    if (_okDelay <= 0f && _modalOk != null)
                    {
                        _modalOk.interactable = true;
                    }
                }

            }

            if (_lingerRemaining > 0f)
            {
                _lingerRemaining -= Time.unscaledDeltaTime;
            }

            RefreshBanner();
        }

        private void OnNotice(MaintenanceNotice m)
        {
            EnsureUi();
            switch (m.Kind)
            {
                case MaintenanceNotice.KindInfo:
                    ShowInfoModal(m.Text);
                    break;

                case MaintenanceNotice.KindRestartCountdown:
                    _countdownActive = true;
                    _restartingNow = m.SecondsRemaining <= 0;
                    _deadline = Time.unscaledTime + Mathf.Max(0, m.SecondsRemaining);
                    _freeText = m.Text ?? string.Empty;
                    if (Game != null)
                    {
                        Game.MaintenanceRestartPending = true;
                    }

                    break;

                case MaintenanceNotice.KindCancelled:
                    _countdownActive = false;
                    _restartingNow = false;
                    _lingerText = Tr("ui.maint.cancelled");
                    _lingerRemaining = CancelBannerLinger;
                    if (Game != null)
                    {
                        Game.MaintenanceRestartPending = false;
                    }

                    break;
            }
        }

        private void ShowInfoModal(string text)
        {
            if (_modalText != null)
            {
                _modalText.text = text;
            }

            _okDelay = OkUnlockDelay;
            if (_modalOk != null)
            {
                _modalOk.interactable = false;
            }

            _modal?.SetActive(true);
            // Cursor-only owner (#413): the OK button needs a free cursor, but gameplay is not paused
            // (a maintenance notice can arrive mid-flight; the countdown plays out regardless).
            Game?.SetCursorOwner(this, true);
        }

        private void OnInfoAck()
        {
            _modal?.SetActive(false);
            if (_modalText != null)
            {
                _lingerText = _modalText.text;
                _lingerRemaining = InfoBannerLinger;
            }

            Game?.SetCursorOwner(this, false); // arbiter re-locks only once NO other owner is open (#413)
        }

        private void RefreshBanner()
        {
            if (_banner == null)
            {
                return;
            }

            bool lingering = !_countdownActive && _lingerRemaining > 0f;
            bool show = _countdownActive || lingering;
            if (_banner.activeSelf != show)
            {
                _banner.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            if (_countdownActive)
            {
                float remaining = Mathf.Max(0f, _deadline - Time.unscaledTime);
                string line;
                if (_restartingNow || remaining <= 0.5f)
                {
                    line = Tr("ui.maint.restarting_now");
                }
                else
                {
                    int total = Mathf.CeilToInt(remaining);
                    string clock = $"{total / 60}:{total % 60:00}";
                    line = Tr("ui.maint.restart_in").Replace("{0}", clock);
                }

                if (!string.IsNullOrEmpty(_freeText))
                {
                    line += "  —  " + _freeText;
                }

                bool urgent = _restartingNow || remaining <= UrgentSeconds;
                _bannerBg.color = urgent ? BannerUrgentBg : BannerCalmBg;
                _bannerText.color = urgent ? BannerUrgentText : BannerCalmText;
                _bannerText.text = line;
            }
            else
            {
                _bannerBg.color = BannerCalmBg;
                _bannerText.color = BannerCalmText;
                _bannerText.text = _lingerText;
            }
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("MaintenanceUi");
            _canvas.sortingOrder = 110; // above menus (50-60), loading (75), respawn (85) AND touch controls (100)
            UiNav.Enable(_canvas.gameObject);

            // Top banner: full reference width, no dismiss controls by design.
            _banner = new GameObject("MaintenanceBanner", typeof(RectTransform));
            var brt = _banner.GetComponent<RectTransform>();
            brt.SetParent(_canvas.transform, false);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            _bannerBg = UiKit.AddImage(_banner.transform, 0, 0, 1920, 64, UiKit.SolidSprite, BannerCalmBg);
            _bannerBg.raycastTarget = false; // the banner informs; it must not eat gameplay clicks
            _bannerText = UiKit.AddText(_banner.transform, 0, 0, 1920, 64, string.Empty, 30,
                BannerCalmText, TextAnchor.MiddleCenter, FontStyle.Bold);
            _bannerText.raycastTarget = false;
            _banner.SetActive(false);

            // Info modal: RespawnPrompt pattern — opaque dim swallowing clicks, one delayed-unlock button.
            _modal = new GameObject("MaintenanceModal", typeof(RectTransform));
            var mrt = _modal.GetComponent<RectTransform>();
            mrt.SetParent(_canvas.transform, false);
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;
            UiKit.AddModalDim(_modal.transform, 0.85f);
            UiKit.AddText(_modal.transform, 0, 330, 1920, 90, Tr("ui.maint.info_title"), 52,
                new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _modalText = UiKit.AddText(_modal.transform, 360, 440, 1200, 220, string.Empty, 34,
                new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter);
            _modalOk = UiKit.AddButton(_modal.transform, (1920f - 300f) / 2f, 700, 300, 80, Tr("ui.maint.ok"), OnInfoAck);
            _modal.SetActive(false);
        }

        private string Tr(string key)
        {
            string s = Game?.Localizer?.Get(key);
            if (!string.IsNullOrEmpty(s) && s != key)
            {
                return s;
            }

            // Fallback if the localizer isn't ready (bilingual values live in data/locales).
            switch (key)
            {
                case "ui.maint.restart_in": return "Server restart in {0}";
                case "ui.maint.restarting_now": return "The server is restarting now…";
                case "ui.maint.cancelled": return "The scheduled server restart was cancelled.";
                case "ui.maint.info_title": return "Server announcement";
                case "ui.maint.ok": return "Got it";
                default: return key;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.MaintenanceNoticeReceived -= OnNotice;
            }
        }
    }
}
