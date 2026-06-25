// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The HUD companion panel for the ship AI "VEGA": shows her lines with a typewriter effect (queued,
    /// radio blip per line) and a persistent objective chip while the onboarding chain is active, with a
    /// skip button. Lines arrive as locale KEYS (<see cref="ShipAiLine"/>) and are localized here, so the
    /// companion is fully bilingual and offline-safe. Advisor hints (Kind 1) respect the settings mute.
    /// </summary>
    public sealed class VegaPanel : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        private const float CharsPerSecond = 42f;
        private const float AutoAdvanceSeconds = 25f; // fallback so an unattended line never blocks forever
        private const KeyCode ContinueKey = KeyCode.N;

        private Canvas _canvas;
        private GameObject _speech;
        private Text _speechText;
        private Text _continueHint;
        private GameObject _chip;
        private Text _chipText;

        private readonly Queue<string> _queue = new Queue<string>();
        private string _current = string.Empty;
        private float _shown;     // characters revealed so far
        private float _holdLeft;  // auto-advance fallback once fully revealed

        private string _objectiveKey = string.Empty;
        private int _objProgress, _objTarget;

        // How many break reminders have fired this session — drives the escalating wording and the next due time.
        private int _remindersSent;

        private void Start()
        {
            _canvas = UiKit.CreateDiegeticCanvas("VegaPanel");

            // Speech panel: left side above the vitals, out of the crosshair's way. VEGA gets a small
            // generated avatar chip beside her name (uGUI icon pass).
            _speech = UiKit.AddPanel(_canvas.transform, 24, 600, 600, 150, new Color(0.05f, 0.10f, 0.16f, 0.82f)).gameObject;
            var avatar = UiKit.Icon("icon_vega");
            float nameX = 14f;
            if (avatar != null)
            {
                UiKit.AddImage(_speech.transform, 12, 5, 30, 30, avatar, UiKit.Cyan);
                nameX = 50f;
            }

            UiKit.AddText(_speech.transform, nameX, 6, 300, 28, L("ui.vega.name"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speechText = UiKit.AddText(_speech.transform, 14, 38, 572, 88, string.Empty, 19, UiKit.TextCol, TextAnchor.UpperLeft);
            _speechText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _speechText.verticalOverflow = VerticalWrapMode.Overflow;
            // Lines advance on a KEYPRESS (they queued straight through each other before — unreadable).
            _continueHint = UiKit.AddText(_speech.transform, 14, 124, 572, 24, L("ui.vega.next"), 15, UiKit.CyanDim, TextAnchor.MiddleRight);
            _continueHint.gameObject.SetActive(false);
            _speech.SetActive(false);

            // Objective chip: small persistent strip below the speech spot. (Skipping/restarting the
            // tutorial lives in the Settings tab — the mouse is captured for camera control out here,
            // so a button on the chip was unreachable.)
            _chip = UiKit.AddPanel(_canvas.transform, 24, 760, 600, 44, new Color(0.05f, 0.10f, 0.16f, 0.66f)).gameObject;
            _chipText = UiKit.AddText(_chip.transform, 14, 0, 574, 44, string.Empty, 19, UiKit.Cyan, TextAnchor.MiddleLeft);
            _chip.SetActive(false);

            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived += OnLine;
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnLine(ShipAiLine m)
        {
            _objectiveKey = m.ObjectiveKey ?? string.Empty;
            _objProgress = m.ObjectiveProgress;
            _objTarget = m.ObjectiveTarget;

            bool muted = m.Kind == 1 && Settings is { VegaHints: false };
            if (!string.IsNullOrEmpty(m.Text) && !muted)
            {
                _queue.Enqueue(m.Text); // LLM-authored line (already in the player's language)
            }
            else if (!string.IsNullOrEmpty(m.LineKey) && !muted)
            {
                string text = L(m.LineKey);
                if (!string.IsNullOrEmpty(m.LineArg) && text.Contains("{0}"))
                {
                    text = string.Format(text, m.LineArg);
                }

                _queue.Enqueue(text);
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_chip == null)
            {
                return;
            }

            bool hasObjective = !string.IsNullOrEmpty(_objectiveKey);
            _chip.SetActive(hasObjective);
            if (hasObjective)
            {
                string counter = _objTarget > 1 ? $"  ({Mathf.Min(_objProgress, _objTarget)}/{_objTarget})" : string.Empty;
                _chipText.text = $"{L("ui.vega.objective")}: {L(_objectiveKey)}{counter}";
            }
        }

        /// <summary>Real-world "take a break" nudge: once the player has been in this session for the chosen
        /// number of minutes (and again every interval after), VEGA enqueues a break reminder. This is driven
        /// purely client-side from the session wall-clock — deliberately NOT a server <see cref="ShipAiLine"/>,
        /// so the meta hint never lands in the Story Log. Gated on the comfort setting; the wording escalates.</summary>
        private void CheckPlaytimeReminder()
        {
            if (Game == null || Settings is not { PlaytimeReminder: true })
            {
                return;
            }

            float interval = Mathf.Max(1, Settings.ReminderMinutes) * 60f;
            if (Game.SessionSeconds < (_remindersSent + 1) * interval)
            {
                return;
            }

            _remindersSent++;
            string key = _remindersSent switch
            {
                1 => "ui.reminder.break",
                2 => "ui.reminder.break.again",
                _ => "ui.reminder.break.long",
            };
            _queue.Enqueue(L(key)); // shows via the normal typewriter path; bypasses the VegaHints mute by design
        }

        /// <summary>True when the continue key should be ignored: the in-game menu is open, or a text
        /// field (chat, beacon label) currently has keyboard focus.</summary>
        private bool InputCaptured()
            => (Game != null && Game.MenuOpen)
               || UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject != null;

        private void Update()
        {
            if (_speechText == null)
            {
                return;
            }

            CheckPlaytimeReminder();

            if (_current.Length == 0 && _queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _shown = 0f;
                _holdLeft = AutoAdvanceSeconds;
                _speech.SetActive(true);
                _continueHint.gameObject.SetActive(false);
                ClientAudio.Instance?.Cue("ai_blip"); // VEGA's radio chirp
            }

            if (_current.Length == 0)
            {
                if (_speech.activeSelf)
                {
                    _speech.SetActive(false);
                }

                return;
            }

            bool pressed = Input.GetKeyDown(ContinueKey) && !InputCaptured();

            if (_shown < _current.Length)
            {
                // Still typing: the continue key fast-completes the reveal instead of skipping the line.
                _shown = pressed ? _current.Length : Mathf.Min(_current.Length, _shown + Time.deltaTime * CharsPerSecond);
                _speechText.text = _current.Substring(0, (int)_shown);
                if (_shown >= _current.Length)
                {
                    _continueHint.gameObject.SetActive(true);
                }

                return;
            }

            // Fully revealed: wait for the continue key (the lines used to run into each other —
            // unreadable); a generous timeout still auto-advances an unattended panel.
            _holdLeft -= Time.deltaTime;
            if (pressed || _holdLeft <= 0f)
            {
                _current = string.Empty;
                _speechText.text = string.Empty;
                _continueHint.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived -= OnLine;
            }

            // The panel canvas is a root-level object (CreateDiegeticCanvas) — destroy it with the rig,
            // or the objective chip would keep floating over the main menu after leaving the world.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
