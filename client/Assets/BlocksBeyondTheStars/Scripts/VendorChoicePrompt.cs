// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Trade or talk?" — the question E at a vendor NPC asks. E at a vendor always opened the market, so a vendor's
    /// dialogues (and everything a profession says) were unreachable: talking only fired when no station was in reach,
    /// and a vendor is a station ("market") by definition. E / Enter picks Trade — the old behaviour stays one key
    /// press away — the Talk button opens the NPC's dialogue. Market blocks you look at still open the market at once.
    /// </summary>
    public sealed class VendorChoicePrompt : MonoBehaviour
    {
        public GameBootstrap Game;

        public static VendorChoicePrompt Instance { get; private set; }

        /// <summary>True while the question is on screen — PlayerController leaves E to it then.</summary>
        public static bool IsOpen => Instance != null && Instance._shown;

        /// <summary>The E press that opened the prompt must not also answer it.</summary>
        private const float KeyGraceSeconds = 0.25f;

        private Canvas _canvas;
        private GameObject _overlay;
        private UnityEngine.UI.Text _title;
        private bool _shown;
        private float _openedAt;
        private Action _onTrade;
        private Action _onTalk;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Shows the question for one vendor. Returns false (and shows nothing) when it cannot, so the caller
        /// falls back to opening the market directly.</summary>
        public bool TryOffer(string npcLabel, Action onTrade, Action onTalk)
        {
            if (Game == null || _shown || onTrade == null || onTalk == null)
            {
                return false;
            }

            EnsureUi();
            _onTrade = onTrade;
            _onTalk = onTalk;
            _title.text = string.IsNullOrEmpty(npcLabel) ? Tr("ui.vendor.choice_title") : npcLabel;
            _overlay.SetActive(true);
            _shown = true;
            _openedAt = Time.unscaledTime;
            Game.SetCursorOwner(this, true);
            return true;
        }

        private void Update()
        {
            if (!_shown || Time.unscaledTime - _openedAt < KeyGraceSeconds)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || InputMap.Down(InputAction.Interact))
            {
                Trade();
            }
        }

        private void Trade()
        {
            var act = _onTrade;
            Close();
            act?.Invoke();
        }

        private void Talk()
        {
            var act = _onTalk;
            Close();
            act?.Invoke();
        }

        private void Close()
        {
            _overlay?.SetActive(false);
            _shown = false;
            _onTrade = null;
            _onTalk = null;
            Game?.SetCursorOwner(this, false);
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("VendorChoicePrompt");
            _canvas.sortingOrder = 84; // above the HUD (60), below the death prompt (85) — like the launch question
            UiNav.Enable(_canvas.gameObject); // gamepad: A/B pick a button

            const float w = 640f, h = 250f;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - w) / 2f, (1080f - h) / 2f, w, h);
            _overlay = overlay;
            _title = UiKit.AddText(panel, 20, 34, w - 40f, 60, string.Empty, 30, new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _title.supportRichText = false; // NPC names are generated text
            UiKit.AddButton(panel, 60, 140, 240, 64, Tr("ui.vendor.trade"), Trade, "btn_join");
            UiKit.AddButton(panel, 340, 140, 240, 64, Tr("ui.vendor.talk"), Talk, "btn_feedback");
            _overlay.SetActive(false);
        }

        private string Tr(string key)
        {
            string s = Game?.Localizer?.Get(key);
            if (!string.IsNullOrEmpty(s) && s != key)
            {
                return s;
            }

            return key switch
            {
                "ui.vendor.choice_title" => "Trade or talk?",
                "ui.vendor.trade" => "Trade (E)",
                "ui.vendor.talk" => "Talk",
                _ => key,
            };
        }
    }
}
