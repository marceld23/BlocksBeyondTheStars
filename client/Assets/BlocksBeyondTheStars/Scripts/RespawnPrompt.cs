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
        private GameObject _continueRoot; // the classic single "Weiter" gate
        private GameObject _choiceRoot;   // the deferred-respawn choice (home spawn vs ship, issue #462)

        private bool _subscribed;
        private bool _armed;   // a death/destruction arrived; counting down to show
        private bool _shown;   // the modal is currently up
        private bool _choiceMode;         // the server deferred the respawn and awaits our pick
        private string _choiceLabel = string.Empty; // home-spawn display label ("" → generic word)
        private float _delay;
        private float _showDelay = ShowDelay; // how long to let the death FX play before the modal (per death kind)
        private float _fade;
        private string _titleKey = "ui.death.title";

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived += OnRespawn;
                Game.Network.RespawnOptionsReceived += OnRespawnOptions;
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

            }
        }

        private void OnRespawn(RespawnNotice m)
        {
            if (m.Died)
            {
                Arm("ui.death.title", ShowDelay);
            }
        }

        /// <summary>The server deferred the respawn (a home spawn is set, issue #462): the modal offers
        /// "wake up at &lt;home&gt;" vs "wake up at your ship" instead of the plain continue gate.</summary>
        private void OnRespawnOptions(RespawnOptions m)
        {
            _choiceMode = true;
            _choiceLabel = m.CustomLabel ?? string.Empty;
            Arm("ui.death.title", ShowDelay);
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

            RefreshButtons();
            _panel?.SetActive(true);
            _shown = true;
            // Cursor-only owner (#413): the button needs a free cursor, but gameplay holds through
            // AwaitingRespawnConfirm, not MenuOpen — the arbiter keeps the two axes separate.
            Game?.SetCursorOwner(this, true);
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
            _choiceMode = false;
            if (Game != null)
            {
                Game.AwaitingRespawnConfirm = false; // release: world reveals / ship recovers from here
                Game.SetCursorOwner(this, false);    // arbiter re-locks only once NO other owner is open (#413)
            }
        }

        /// <summary>A respawn pick was made: tell the server, then swap the modal to the classic continue
        /// gate — the RespawnNotice with the resulting wake-up spot lands right after (instant on local play).</summary>
        private void OnChoice(bool useCustomSpawn)
        {
            Game?.Network?.SendRespawnChoice(useCustomSpawn);
            _choiceMode = false;
            RefreshButtons();
        }

        /// <summary>Shows either the choice pair (deferred respawn) or the single continue button. The choice
        /// buttons are rebuilt each time so the home label is always the current one.</summary>
        private void RefreshButtons()
        {
            if (_continueRoot != null)
            {
                _continueRoot.SetActive(!_choiceMode);
            }

            if (_choiceRoot == null)
            {
                return;
            }

            _choiceRoot.SetActive(_choiceMode);
            if (!_choiceMode)
            {
                return;
            }

            foreach (Transform child in _choiceRoot.transform)
            {
                Destroy(child.gameObject);
            }

            string home = string.IsNullOrEmpty(_choiceLabel) ? Tr("ui.spawn.generic") : _choiceLabel;
            string homeText = Tr("ui.death.respawn_home").Replace("{name}", home);
            UiKit.AddButton(_choiceRoot.transform, (1920f - 560f) / 2f, 590, 560, 80, homeText, () => OnChoice(true));
            UiKit.AddButton(_choiceRoot.transform, (1920f - 560f) / 2f, 690, 560, 80, Tr("ui.death.respawn_ship"), () => OnChoice(false));
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

            _continueRoot = FullStretchChild(_panel.transform, "RespawnContinue");
            UiKit.AddButton(_continueRoot.transform, (1920f - 300f) / 2f, 610, 300, 80, Tr("ui.death.continue"), OnContinue);
            _choiceRoot = FullStretchChild(_panel.transform, "RespawnChoice");

            _panel.SetActive(false);
        }

        private static GameObject FullStretchChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
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
                case "ui.death.respawn_ship": return "Wake up at your ship";
                case "ui.death.respawn_home": return "Wake up at {name}";
                case "ui.spawn.generic": return "your base";
                default: return key;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived -= OnRespawn;
                Game.Network.RespawnOptionsReceived -= OnRespawnOptions;
                Game.Network.SpaceClosed -= OnSpaceClosed;
            }
        }
    }
}
