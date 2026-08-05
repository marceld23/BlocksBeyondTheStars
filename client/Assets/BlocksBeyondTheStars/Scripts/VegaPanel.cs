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

        // Left-column layout in HUD reference units (1536×864). The column is full: vitals end at y 260,
        // the toast sits at 268, the scan panel starts at 650 and the hotbar backplate owns y 742…834 /
        // x 400…1136. These two constants are what a layout tweak should move (#482).
        private const float SpeechY = 396f, SpeechH = 190f;
        private const float ChipY = 594f;

        // The speech body's text rect — the page splitter (#736) measures wrapped lines against exactly
        // this box, so a page can never be taller than what VerticalWrapMode.Truncate would show.
        private const float SpeechTextW = 612f, SpeechTextH = 116f;

        private Canvas _canvas;
        private GameObject _speech;
        private Text _speechText;
        private Text _continueHint;
        private GameObject _chip;
        private Text _chipText;

        private readonly Queue<(string Text, bool Prologue)> _queue = new Queue<(string, bool)>();
        private string _current = string.Empty;  // the page being typed/read (not the whole line)
        private float _shown;     // characters revealed so far
        private float _holdLeft;  // auto-advance fallback once fully revealed

        // Long lines are split into panel-sized pages, advanced with the same continue key (#736). German
        // runs 12–20 % longer than English, so the bandit briefing and several hints exceed the ~4 visible
        // lines — they used to be silently truncated.
        private readonly List<string> _pages = new List<string>();
        private int _page;
        private static readonly TextGenerator Measurer = new TextGenerator();

        // First-spawn narrative prologue (#738, reworked in #754): Kind-4 lines run through the SAME speech
        // panel as every other VEGA line (same measure, same paging, same user UI scale) — they used to get
        // a bespoke full-screen dialog whose near-black plate read as "text across the whole screen". The
        // only prologue extras now are a dim behind the panel and Esc-to-skip.
        private Image _dim;
        private bool _currentIsPrologue;

        private string _objectiveKey = string.Empty;
        private int _objProgress, _objTarget;

        // How many break reminders have fired this session — drives the escalating wording and the next due time.
        private int _remindersSent;

        private void Start()
        {
            // The HUD reference (1536×864), NOT the 1920×1080 default — VEGA was missed by the 2026-06-07
            // "bigger HUD" pass (#482), so her lines rendered 25 % smaller than every other HUD element at
            // every resolution. Subtitle-class text has to read while the eye is on the crosshair.
            _canvas = UiKit.CreateDiegeticCanvas("VegaPanel", UiKit.HudRefW, UiKit.HudRefH);
            _canvas.sortingOrder = 11; // just above HudUi (10) — a story line must never be occluded

            // Prologue dim (#754): a soft full-screen shade BEHIND the speech panel while a Kind-4 story
            // page is up, so the moment reads cinematic without a separate dialog stealing the layout.
            _dim = UiKit.AddImage(_canvas.transform, 0, 0, UiKit.HudRefW, UiKit.HudRefH, UiKit.SolidSprite,
                new Color(0f, 0f, 0f, 0.55f));
            _dim.gameObject.SetActive(false);

            // Speech panel: left side above the vitals, out of the crosshair's way. VEGA gets a small
            // generated avatar chip beside her name (uGUI icon pass). Coordinates are in HUD reference
            // units; the left column is tight (vitals → speech → chip → scan panel → hotbar), see #482.
            _speech = UiKit.AddPanel(_canvas.transform, 24, SpeechY, 640, SpeechH, new Color(0.05f, 0.10f, 0.16f, 0.82f)).gameObject;
            var avatar = UiKit.Icon("icon_vega");
            float nameX = 14f;
            if (avatar != null)
            {
                UiKit.AddImage(_speech.transform, 12, 6, 34, 34, avatar, UiKit.Cyan);
                nameX = 54f;
            }

            UiKit.AddText(_speech.transform, nameX, 6, 320, 30, L("ui.vega.name"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speechText = UiKit.AddText(_speech.transform, 14, 44, SpeechTextW, SpeechTextH, string.Empty, 22, UiKit.TextCol, TextAnchor.UpperLeft);
            _speechText.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Truncate, NOT Overflow: an LLM-authored line has no length bound on the wire, and an
            // over-long one used to run over the continue hint and out of the panel background (#482).
            _speechText.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.AddOutline(_speechText); // readable over bright terrain / snow / sky
            // Lines advance on a KEYPRESS (they queued straight through each other before — unreadable).
            _continueHint = UiKit.AddText(_speech.transform, 14, 160, 612, 24, L("ui.vega.next"), 16, UiKit.CyanDim, TextAnchor.MiddleRight);
            _continueHint.gameObject.SetActive(false);
            _speech.SetActive(false);

            // Objective chip: small persistent strip below the speech spot. (Skipping/restarting the
            // tutorial lives in the Settings tab — the mouse is captured for camera control out here,
            // so a button on the chip was unreachable.)
            _chip = UiKit.AddPanel(_canvas.transform, 24, ChipY, 640, 48, new Color(0.05f, 0.10f, 0.16f, 0.66f)).gameObject;
            _chipText = UiKit.AddText(_chip.transform, 14, 0, 614, 48, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft);
            // Wrap + truncate as a safety net — the UiKit default (Overflow) would let an over-long
            // objective label spill outside the chip background (#736 side finding).
            _chipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chipText.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.AddOutline(_chipText);
            _chip.SetActive(false);

            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived += OnLine;
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>Capture hook (<see cref="ScreenshotDirector"/>): drop any queued VEGA speech and hide the
        /// panel, so an unattended screenshot run never catches the onboarding/greeting dialog in the frame.
        /// (Previously a fresh world needed a throwaway run just to get past the intro lines.)</summary>
        public void DismissSpeechForCapture()
        {
            _queue.Clear();
            _pages.Clear();
            _current = string.Empty;
            if (_speech != null)
            {
                _speech.SetActive(false);
            }

            SetPrologueChrome(false); // never hold an unattended capture run hostage on a story page
        }

        private void OnLine(ShipAiLine m)
        {
            _objectiveKey = m.ObjectiveKey ?? string.Empty;
            _objProgress = m.ObjectiveProgress;
            _objTarget = m.ObjectiveTarget;

            bool muted = m.Kind == 1 && Settings is { VegaHints: false };
            if (!string.IsNullOrEmpty(m.Text) && !muted)
            {
                _queue.Enqueue((m.Text, false)); // LLM-authored line (already in the player's language)
            }
            else if (!string.IsNullOrEmpty(m.LineKey) && !muted)
            {
                string text = L(m.LineKey);
                if (!string.IsNullOrEmpty(m.LineArg) && text.Contains("{0}"))
                {
                    text = string.Format(text, m.LineArg);
                }

                // Prologue pages (Kind 4, #754) ride the normal queue with a flag — same panel, same
                // paging, plus the dim + Esc-to-skip while one is showing.
                _queue.Enqueue((text, m.Kind == 4));
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
            _queue.Enqueue((L(key), false)); // shows via the normal typewriter path; bypasses the VegaHints mute by design
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
                var (text, prologue) = _queue.Dequeue();
                _currentIsPrologue = prologue;
                SetPrologueChrome(prologue);
                _pages.Clear();
                _pages.AddRange(SplitPages(text));
                _speech.SetActive(true);
                ClientAudio.Instance?.Cue("ai_blip"); // VEGA's radio chirp
                ShowPage(0);
            }

            if (_current.Length == 0)
            {
                if (_speech.activeSelf)
                {
                    _speech.SetActive(false);
                }

                SetPrologueChrome(false);
                _currentIsPrologue = false;
                return;
            }

            // Esc skips the whole prologue (#754): drop the current story page and every queued one, so
            // a returning player isn't forced through the narration again. Normal lines keep Esc for the menu.
            if (_currentIsPrologue && Input.GetKeyDown(KeyCode.Escape) && !InputCaptured())
            {
                Game?.MarkMenuInputHandled(); // this Esc is spent here, whatever runs later this frame
                while (_queue.Count > 0 && _queue.Peek().Prologue)
                {
                    _queue.Dequeue();
                }

                _current = string.Empty;
                _pages.Clear();
                _speechText.text = string.Empty;
                _continueHint.gameObject.SetActive(false);
                return;
            }

            bool pressed = Input.GetKeyDown(ContinueKey) && !InputCaptured();

            if (_shown < _current.Length)
            {
                // Still typing: the continue key fast-completes the reveal instead of skipping the page.
                _shown = pressed ? _current.Length : Mathf.Min(_current.Length, _shown + Time.deltaTime * CharsPerSecond);
                _speechText.text = _current.Substring(0, (int)_shown);
                if (_shown >= _current.Length)
                {
                    ShowContinueHint();
                }

                return;
            }

            // Fully revealed: wait for the continue key (the lines used to run into each other —
            // unreadable); a generous timeout still auto-advances an unattended panel.
            _holdLeft -= Time.deltaTime;
            if (pressed || _holdLeft <= 0f)
            {
                if (_page < _pages.Count - 1)
                {
                    ShowPage(_page + 1); // the line continues on the next page (#736)
                }
                else
                {
                    _current = string.Empty;
                    _pages.Clear();
                    _speechText.text = string.Empty;
                    _continueHint.gameObject.SetActive(false);
                }
            }
        }

        private void ShowPage(int index)
        {
            _page = index;
            _current = _pages.Count > 0 ? _pages[index] : string.Empty;
            _shown = 0f;
            _holdLeft = AutoAdvanceSeconds;
            _speechText.text = string.Empty;
            _continueHint.gameObject.SetActive(false);
        }

        private void ShowContinueHint()
        {
            // Multi-page lines get a page indicator so "Continue" visibly means "next page", not "dismiss".
            string hint = _pages.Count > 1
                ? L("ui.vega.next") + "  " + string.Format(L("ui.vega.page"), _page + 1, _pages.Count)
                : L("ui.vega.next");
            if (_currentIsPrologue)
            {
                hint += "      " + L("ui.vega.prologue.skip"); // Esc skips the narration (#754)
            }

            _continueHint.text = hint;
            _continueHint.gameObject.SetActive(true);
        }

        /// <summary>Shows/hides the story-page chrome (#754): the dim behind the speech panel, and the
        /// <see cref="GameBootstrap.VegaPrologueActive"/> flag AppShell checks so Esc skips the story
        /// instead of opening the leave-game menu.</summary>
        private void SetPrologueChrome(bool on)
        {
            if (_dim != null && _dim.gameObject.activeSelf != on)
            {
                _dim.gameObject.SetActive(on);
            }

            if (Game != null)
            {
                Game.VegaPrologueActive = on;
            }
        }

        /// <summary>Splits a line into pages that fit the speech box, cutting only on wrap-line boundaries.
        /// Layout is measured with an explicit scaleFactor of 1, so line heights come back in HUD reference
        /// units regardless of canvas scaling (the What's-new dialog's proven measurement pattern).</summary>
        private List<string> SplitPages(string text)
        {
            var settings = _speechText.GetGenerationSettings(new Vector2(SpeechTextW, 0f));
            settings.scaleFactor = 1f;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            Measurer.Populate(text, settings);
            var lines = Measurer.lines;
            var starts = new List<int>(lines.Count);
            var heights = new List<float>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                starts.Add(lines[i].startCharIdx);
                heights.Add(lines[i].height);
            }

            var pages = new List<string>();
            foreach (var (start, length) in VegaText.PageRanges(starts, heights, text.Length, SpeechTextH))
            {
                string page = text.Substring(start, length).Trim();
                if (page.Length > 0)
                {
                    pages.Add(page);
                }
            }

            if (pages.Count == 0)
            {
                pages.Add(text); // measurement produced nothing usable — behave like the unpaged panel
            }

            return pages;
        }

        private void OnDestroy()
        {
            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived -= OnLine;
            }

            SetPrologueChrome(false); // clears GameBootstrap.VegaPrologueActive with the rig teardown

            // The panel canvas is a root-level object (CreateDiegeticCanvas) — destroy it with the rig,
            // or the objective chip would keep floating over the main menu after leaving the world.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
