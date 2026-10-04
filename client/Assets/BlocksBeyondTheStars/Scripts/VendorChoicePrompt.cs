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
    /// <para>#2248: the same two-button question serves the ship's workshop once a bio lab module is fitted —
    /// "Workshop (E)" keeps crafting one key press away, the second button opens the bio lab
    /// (<see cref="TryOfferPair"/>).</para>
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
        private UnityEngine.UI.Text _firstLabel;
        private UnityEngine.UI.Text _secondLabel;
        private bool _shown;
        private float _openedAt;
        private Action _onFirst;
        private Action _onSecond;

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
            => TryOfferPair(string.IsNullOrEmpty(npcLabel) ? Tr("ui.vendor.choice_title") : npcLabel,
                Tr("ui.vendor.trade_choice"), onTrade, Tr("ui.vendor.talk"), onTalk);

        /// <summary>Shows a two-way question: E / Enter picks the first answer, the second is a button. Returns false
        /// (and shows nothing) when it cannot, so the caller falls back to the first answer directly.</summary>
        public bool TryOfferPair(string title, string firstLabel, Action onFirst, string secondLabel, Action onSecond)
        {
            if (Game == null || _shown || onFirst == null || onSecond == null)
            {
                return false;
            }

            EnsureUi();
            _onFirst = onFirst;
            _onSecond = onSecond;
            _title.text = title;
            if (_firstLabel != null)
            {
                _firstLabel.text = firstLabel;
            }

            if (_secondLabel != null)
            {
                _secondLabel.text = secondLabel;
            }

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
                First();
            }
        }

        private void First()
        {
            var act = _onFirst;
            Close();
            act?.Invoke();
        }

        private void Second()
        {
            var act = _onSecond;
            Close();
            act?.Invoke();
        }

        private void Close()
        {
            _overlay?.SetActive(false);
            _shown = false;
            _onFirst = null;
            _onSecond = null;
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
            _firstLabel = UiKit.AddButton(panel, 60, 140, 240, 64, string.Empty, First, "btn_join").GetComponentInChildren<UnityEngine.UI.Text>();
            _secondLabel = UiKit.AddButton(panel, 340, 140, 240, 64, string.Empty, Second, "btn_feedback").GetComponentInChildren<UnityEngine.UI.Text>();
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
                "ui.vendor.trade_choice" => "Trade (E)",
                "ui.vendor.talk" => "Talk",
                _ => key,
            };
        }
    }
}
