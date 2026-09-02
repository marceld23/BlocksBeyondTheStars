// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Launch into space?" — the question E at the cockpit of a LANDED ship asks (#1455). The only way off a
    /// planet used to be a button at the top of the Map tab, and a first-time player standing at the cockpit
    /// never found it ("wie komme ich vom Planeten weg?"). Confirm with the button, E or Enter; "Not yet"
    /// opens the map as the cockpit always did. Inert in space and inside the floating ship interior, where
    /// the cockpit is the helm and the server handles E itself.
    /// </summary>
    public sealed class LaunchPrompt : MonoBehaviour
    {
        public GameBootstrap Game;

        public static LaunchPrompt Instance { get; private set; }

        /// <summary>True while the question is on screen — PlayerController leaves E to it then.</summary>
        public static bool IsOpen => Instance != null && Instance._shown;

        /// <summary>The E press that opened the prompt must not also answer it — ignore keys this long.</summary>
        private const float KeyGraceSeconds = 0.25f;

        private Canvas _canvas;
        private GameObject _overlay;
        private bool _shown;
        private float _openedAt;
        private Action _onDecline;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Shows the question if the player is at the cockpit of a landed ship on a surface. Returns
        /// false (and shows nothing) in space or aboard the floating interior, so the caller keeps its old
        /// behaviour there.</summary>
        public bool TryOffer(Action onDecline)
        {
            if (Game == null || Game.SpaceViewActive || Game.LoadingPlanetType == "ship_interior" || _shown)
            {
                return false;
            }

            EnsureUi();
            _onDecline = onDecline;
            _overlay.SetActive(true);
            _shown = true;
            _openedAt = Time.unscaledTime;
            Game.SetCursorOwner(this, true); // the buttons need a free cursor (#413 arbiter)
            return true;
        }

        private void Update()
        {
            if (!_shown || Time.unscaledTime - _openedAt < KeyGraceSeconds)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || InputMap.Down(InputAction.Interact))
            {
                Confirm();
            }
        }

        private void Confirm()
        {
            Close();
            Game?.Network?.SendEnterSpace(); // the server answers with the flight view (or "board your ship first")
        }

        private void Decline()
        {
            Close();
            _onDecline?.Invoke();
        }

        private void Close()
        {
            _overlay?.SetActive(false);
            _shown = false;
            Game?.SetCursorOwner(this, false);
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("LaunchPrompt");
            _canvas.sortingOrder = 84; // above the HUD (60), below the death prompt (85)
            UiNav.Enable(_canvas.gameObject); // gamepad: A/B pick a button

            const float w = 640f, h = 250f;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - w) / 2f, (1080f - h) / 2f, w, h);
            _overlay = overlay;
            UiKit.AddText(panel, 0, 34, w, 60, Tr("ui.launch.title"), 34, new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(panel, 60, 140, 240, 64, Tr("ui.launch.yes"), Confirm, "btn_join");
            UiKit.AddButton(panel, 340, 140, 240, 64, Tr("ui.launch.no"), Decline, "btn_exit");
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
                "ui.launch.title" => "Launch into space?",
                "ui.launch.yes" => "Launch (E)",
                "ui.launch.no" => "Not yet — map",
                _ => key,
            };
        }
    }
}
