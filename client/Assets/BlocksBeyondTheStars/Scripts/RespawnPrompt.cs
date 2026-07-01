// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Du bist gestorben" / "Das Schiff wurde zerstört" confirmation screen. The server already
    /// respawned the player (instantly + authoritatively); this is purely a client gate: after the death
    /// flash / ship explosion has played, a dark overlay with a title and a single "Weiter" button appears,
    /// and the player / ship only re-appears once it is clicked.
    ///
    /// While <see cref="GameBootstrap.AwaitingRespawnConfirm"/> is set, <see cref="PlayerController"/> holds
    /// the on-foot world reveal and <see cref="SpaceView"/> holds the landing teardown, so nothing happens
    /// until the player confirms. Driven by the server's respawn / space-closed events; no server change.
    /// </summary>
    public sealed class RespawnPrompt : MonoBehaviour
    {
        public GameBootstrap Game;

        // Let the death flash / explosion be seen before the modal slides in. On-foot death is a quick red wash;
        // a ship blowing up in space gets longer so the full fireball + debris play out before the modal covers it.
        private const float ShowDelay = 1.1f;
        private const float ShipShowDelay = 2.1f;
        private const float FadeIn = 0.4f;
        private const float BackdropAlpha = 0.82f;

        private Canvas _canvas;
        private Image _backdrop;
        private Text _title;
        private GameObject _panel; // backdrop + title + button, toggled as a group

        private bool _subscribed;
        private bool _armed;   // a death/destruction arrived; counting down to show
        private bool _shown;   // the modal is currently up
        private float _delay;
        private float _showDelay = ShowDelay; // how long to let the death FX play before the modal (per death kind)
        private float _fade;
        private string _titleKey = "ui.death.title";

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived += OnRespawn;
                Game.Network.SpaceClosed += OnSpaceClosed;
                _subscribed = true;
            }

            if (_armed && !_shown)
            {
                _delay += Time.deltaTime;
                if (_delay >= _showDelay)
                {
                    Show();
                }
            }

            if (_shown)
            {
                // Fade the backdrop in for polish.
                if (_fade < FadeIn)
                {
                    _fade += Time.deltaTime;
                    float k = Mathf.Clamp01(_fade / FadeIn);
                    if (_backdrop != null)
                    {
                        var c = _backdrop.color;
                        c.a = BackdropAlpha * k;
                        _backdrop.color = c;
                    }
                }

                // Keep the cursor free for the button every frame — other systems (flight, settle) won't
                // re-lock it while we're up, but this is cheap insurance against a one-frame fight.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnRespawn(RespawnNotice m)
        {
            if (m.Died)
            {
                Arm("ui.death.title", ShowDelay);
            }
        }

        private void OnSpaceClosed(SpaceClosed m)
        {
            if (m.ShipDisabled)
            {
                Arm("ui.death.ship_title", ShipShowDelay);
            }
        }

        private void Arm(string titleKey, float showDelay)
        {
            if (_armed || _shown)
            {
                return; // already gating this death — ignore a duplicate event
            }

            _titleKey = titleKey;
            _showDelay = showDelay;
            _armed = true;
            _delay = 0f;
            // Set the gate immediately (synchronously with the event) so PlayerController / SpaceView hold
            // from this very frame, even though the modal itself only appears after the animation delay.
            if (Game != null)
            {
                Game.AwaitingRespawnConfirm = true;
            }
        }

        private void Show()
        {
            EnsureUi();
            if (_title != null)
            {
                _title.text = Tr(_titleKey);
            }

            _panel?.SetActive(true);
            _shown = true;
            _fade = 0f;
            if (_backdrop != null)
            {
                var c = _backdrop.color;
                c.a = 0f;
                _backdrop.color = c;
            }
        }

        private void OnContinue()
        {
            _panel?.SetActive(false);
            _shown = false;
            _armed = false;
            if (Game != null)
            {
                Game.AwaitingRespawnConfirm = false; // release: world reveals / ship recovers from here
            }

            // Hand the cursor back to gameplay (on-foot look / flight re-lock it as before).
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("RespawnPrompt");
            _canvas.sortingOrder = 85; // above the HUD (60) and the DeathFx flash (80)
            UiNav.Enable(_canvas.gameObject); // the pad can confirm the respawn (inert on KB/mouse)

            _panel = new GameObject("RespawnPromptPanel", typeof(RectTransform));
            var prt = _panel.GetComponent<RectTransform>();
            prt.SetParent(_canvas.transform, false);
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            _backdrop = UiKit.AddImage(_panel.transform, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.02f, 0.04f, 0f));
            _backdrop.raycastTarget = true; // swallow clicks behind the modal so only "Weiter" reacts

            _title = UiKit.AddText(_panel.transform, 0, 410, 1920, 140, string.Empty, 66,
                new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);

            UiKit.AddButton(_panel.transform, (1920f - 300f) / 2f, 610, 300, 80, Tr("ui.death.continue"), OnContinue);

            _panel.SetActive(false);
        }

        private string Tr(string key)
        {
            string s = Game?.Localizer?.Get(key);
            if (!string.IsNullOrEmpty(s) && s != key)
            {
                return s;
            }

            // Fallback if the localizer isn't ready (kept English; bilingual values live in data/locales).
            switch (key)
            {
                case "ui.death.title": return "You have died";
                case "ui.death.ship_title": return "Your ship was destroyed";
                case "ui.death.continue": return "Continue";
                default: return key;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived -= OnRespawn;
                Game.Network.SpaceClosed -= OnSpaceClosed;
            }
        }
    }
}
