// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The fallen player's banner (#3765, the death rule of a Hard Survival world). The server does not ask where to
    /// wake up: the player has fallen and <b>watches</b> — the client flies the observer's camera (the server says so
    /// with <c>PlayerStateUpdate.Spectating</c>), and this banner says what is going on: a team-mate can bring you back
    /// with a medkit where you fell; or, when nobody is left who could, a countdown and then the control that starts you
    /// again at your ship. No modal and no cursor: the player keeps flying. The server owns every rule
    /// (<see cref="FallenRules"/>); the countdown here only mirrors the number it sent.
    /// </summary>
    public sealed class FallenView : MonoBehaviour
    {
        public GameBootstrap Game;

        private bool _subscribed;
        private bool _fallen;
        private bool _sawSpectating; // the state update that starts the watching has arrived
        private float _restartIn;    // seconds as last told (-1: wait for a team-mate)
        private float _toldAt;
        private Canvas _canvas;
        private GameObject _panel;
        private Text _title;
        private Text _line;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnOptionsReceived += OnRespawnOptions;
                _subscribed = true;
            }

            if (!_fallen)
            {
                return;
            }

            // Standing again (revived, or back at the ship): the state update ends the watching — and nothing else does.
            // A respawn notice is no sign of it: a rescue teleport sends one too, and the banner that hid on it never came
            // back, which took the restart control with it.
            _sawSpectating |= Game.Spectating;
            if (_sawSpectating && !Game.Spectating)
            {
                Hide();
                return;
            }

            // The control token is expanded before the seconds go in (FallenBanner.Line): the restart line has a
            // {key:Interact} and no {0}, and string.Format on it threw — every frame, before the press below was read.
            float left = FallenBanner.SecondsLeft(_restartIn, Time.unscaledTime - _toldAt);
            _line.text = FallenBanner.Line(Tr(FallenBanner.LineKey(left)), left, t => HudUi.ExpandKeyTokens(Game.Localizer, t));
            if (FallenBanner.MayRestart(left) && !Game.MenuOpen && !Game.ChatTyping && InputMap.Down(InputAction.Interact))
            {
                Game.Network?.SendRespawnChoice(false); // the server checks it again and answers with the snap to the ship
            }
        }

        private void OnRespawnOptions(RespawnOptions m)
        {
            if (!m.Fallen)
            {
                return;
            }

            _restartIn = m.RestartInSeconds;
            _toldAt = Time.unscaledTime;
            if (!_fallen)
            {
                _fallen = true;
                _sawSpectating = false;
                Show();
            }
        }

        private void Show()
        {
            EnsureUi();
            _title.text = Tr("ui.fallen.title");
            _panel.SetActive(true);
        }

        private void Hide()
        {
            _fallen = false;
            _panel?.SetActive(false);
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("FallenView");
            _canvas.sortingOrder = 62; // above the HUD (60), under the death flash (80)
            _panel = UiKit.AddStretchChild(_canvas.transform, "FallenBanner");
            var plate = UiKit.AddImage(_panel.transform, 460f, 150f, 1000f, 112f, UiKit.SolidSprite, new Color(0.02f, 0.03f, 0.06f, 0.72f));
            plate.raycastTarget = false;
            _title = UiKit.AddText(_panel.transform, 460f, 156f, 1000f, 44f, string.Empty, 30,
                new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _line = UiKit.AddText(_panel.transform, 480f, 202f, 960f, 54f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleCenter);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.raycastTarget = false;
            _line.raycastTarget = false;
            _panel.SetActive(false);
        }

        private string Tr(string key) => UiKit.Tr(Game?.Localizer, key, Fallback);

        /// <summary>English fallbacks while the localizer is not ready (the real texts live in data/locales).</summary>
        private static readonly System.Func<string, string> Fallback = key => key switch
        {
            "ui.fallen.title" => "You have fallen",
            "ui.fallen.wait" => "A team-mate can bring you back with a medkit at the place where you fell. Until then you watch.",
            "ui.fallen.countdown" => "Nobody is left to help. You can start again at your ship in {0} s.",
            "ui.fallen.restart" => "{key:Interact}: start again at your ship",
            _ => key,
        };

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnOptionsReceived -= OnRespawnOptions;
            }

            // A scene-root canvas: it goes with the rig, or the banner stays over the main menu (#2936).
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
