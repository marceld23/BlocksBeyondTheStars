// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The big chat window of <see cref="ChatUi"/> (#3264) — how it opens, takes its text and closes. Enter (d-pad up, the
    /// touch CHAT button) opens it; Enter on a chat line sends it and closes the window, Enter on a slash command sends it
    /// and keeps the window open so the answer can be read where it was asked, Esc (or ×) closes it. While it is open
    /// <see cref="GameBootstrap.ChatTyping"/> is up (the controls freeze, the crosshair and the interaction prompt step
    /// aside) and the mouse cursor is free for the wheel, the scrollbar and the buttons. The window is non-modal: anything
    /// that takes over the screen — a menu or panel (the taming minigame too), a cutscene or transition cinematic, the
    /// death screen and its respawn prompt, the launch and vendor questions — closes it (<see cref="MustYield"/>).
    /// The view itself (build, layout, history, reveal) is <c>ChatUi.WindowView.cs</c>.
    /// </summary>
    public sealed partial class ChatUi
    {
        /// <summary>How the open window takes its text.</summary>
        private enum ChatWindowMode
        {
            Closed,

            /// <summary>The window's own input row: desktop keyboard, or a native tablet's system keyboard.</summary>
            Keyboard,

            /// <summary>The gamepad's on-screen keyboard, with the window docked above it.</summary>
            Pad,

            /// <summary>The blocking browser prompt of a touch browser (WebGL), with the window in the upper half.</summary>
            Prompt,
        }

        private ChatWindowMode _mode = ChatWindowMode.Closed;
        private int _openFrame = -1, _closedFrame = -1;
        private bool _refocus;      // a click elsewhere took the field's focus — the window keeps the keys (next frame)
        private int _bigScroll;     // lines hidden below the shown slice (ChatScrollback; 0 = the newest at the bottom)
        private int _bigMaxOffset;  // the furthest worth scrolling: the oldest line at the top of a full window
        private int _newBelow;      // lines that arrived while the reader was scrolled up — the "New messages ↓" marker
        private Coroutine _promptRoutine;
        private HudUi _hookedHud;
        private string _lastCause = string.Empty;
        private float _lastCauseAt = -100f;
        private const float CauseRepeatSeconds = 5f; // a cause repeating within this (lava, a swarm) is written once

        /// <summary>True while the big window is open, in any of its input modes.</summary>
        public bool WindowOpen => _mode != ChatWindowMode.Closed;

        /// <summary>Opens the big chat window (the same path as pressing Enter). Public so the touch layer's CHAT button
        /// can open it — tablets have no Enter key. Pressed again while the browser-prompt window is open (WebGL touch),
        /// it asks for the next line.</summary>
        public void OpenInput()
        {
            // Not while a /bump screenshot holds the canvas off for its end-of-frame capture: the focus guard would
            // release the field under the hidden canvas within the opening frame, and the box would be stuck without
            // focus. The capture is over by the next frame.
            if (_capturing || Game?.Network == null || MustYield())
            {
                return;
            }

            EnsureBuilt();
            if (WindowOpen)
            {
                if (_mode == ChatWindowMode.Prompt)
                {
                    StartPrompt();
                }

                return;
            }

            if (TouchTextEntry.NeedsPrompt)
            {
                OpenWindow(ChatWindowMode.Prompt);
                StartPrompt();
                return;
            }

            if (OnScreenKeyboardUi.WantedFor())
            {
                // The keyboard first: the window lays itself out above the keyboard's panel from its very first frame.
                OpenPadKeyboard(TakeReplyPrefill()); // #3507: "/r " ready after an admin's line
                OpenWindow(ChatWindowMode.Pad);
                return;
            }

            OpenWindow(ChatWindowMode.Keyboard);

            // Focus AFTER the window is awake and placed (#1806): uGUI's ActivateInputField silently no-ops on a field
            // under an inactive parent — the row then appeared but took no keys, and neither Enter nor Esc could get the
            // player out.
            _input.ActivateInputField();
            PrefillReply(); // #3507: "/r " ready after an admin's line (ChatUi.Direct.cs)
        }

        private void OpenWindow(ChatWindowMode mode)
        {
            _mode = mode;
            _openFrame = Time.frameCount;
            _refocus = false;
            _bigScroll = 0;
            _newBelow = 0;
            Game.ChatTyping = true;
            if (mode != ChatWindowMode.Pad)
            {
                Game.SetCursorOwner(this, true); // the wheel, the scrollbar and × want the mouse (the pad has its keyboard)
            }

            // Show the chat canvas BEFORE the field wakes and focuses: in the flight view the context toggle keeps it
            // disabled while the window is closed, and it only re-enables it on the NEXT frame. The field would focus under
            // a disabled canvas this frame — its Text caches "no canvas" and the caret rebuild at the end of the frame
            // throws in InputField.GenerateCaret (the 2026.9.9 crash report). The /bump capture owns the canvas meanwhile.
            if (_canvas != null && !_capturing)
            {
                _canvas.enabled = true;
            }

            // The browser prompt freezes the page on the frame behind it, so that window skips its reveal animation.
            ShowBigWindow(reveal: mode != ChatWindowMode.Prompt);
            RefreshLog(); // the lane steps aside while the window shows the whole history
        }

        /// <summary>Closes the window with its short fade; the lane comes back without the lines read in it.</summary>
        private void CloseWindow()
        {
            if (!WindowOpen)
            {
                return;
            }

            var mode = _mode;
            _mode = ChatWindowMode.Closed;
            _closedFrame = Time.frameCount;
            _refocus = false;
            Game.ChatTyping = false;
            Game.SetCursorOwner(this, false);
            if (mode == ChatWindowMode.Pad && OnScreenKeyboardUi.IsOpen)
            {
                OnScreenKeyboardUi.Dismiss(); // a cutscene or a menu took over while the pad was typing
            }

            if (_input != null)
            {
                if (_input.isFocused)
                {
                    _input.DeactivateInputField(); // its end-edit finds the window closed and does nothing
                }

                _input.text = string.Empty;
            }

            ReleaseSelection();
            _laneFromSeq = _appended;
            HideBigWindow();
            RefreshLog();
        }

        /// <summary>The component goes away with the window open: drop every claim on the game without animating.</summary>
        private void DropWindowOnDisable()
        {
            _promptRoutine = null; // coroutines stop with the component
            if (WindowOpen)
            {
                var mode = _mode;
                _mode = ChatWindowMode.Closed;
                if (Game != null)
                {
                    Game.ChatTyping = false;
                    Game.SetCursorOwner(this, false);
                }

                if (mode == ChatWindowMode.Pad && OnScreenKeyboardUi.IsOpen)
                {
                    OnScreenKeyboardUi.Dismiss();
                }
            }

            ReleaseSelection();
        }

        /// <summary>
        /// Everything the open window steps aside for — and the states it may not open in: a menu or panel (the taming
        /// minigame counts, it is a HUD-level menu owner), a cutscene or a scene that hides the HUD (the landing and launch
        /// transitions hold the screen through the same gate), a cinematic owning the camera, the first-spawn prologue, the
        /// death screen and its respawn prompt, the launch and the vendor question. They all close the chat, never the
        /// other way round.
        /// </summary>
        private bool MustYield()
        {
            var g = Game;
            var views = g.Views;
            return g.MenuOpen || GameBootstrap.CutsceneActive || GameBootstrap.HudHidden || g.CinematicCameraActive
                || g.VegaPrologueActive || g.AwaitingRespawnConfirm || g.Health <= 0f
                || (views.LaunchPrompt != null && views.LaunchPrompt.IsOpen)
                || (views.VendorChoice != null && views.VendorChoice.IsOpen);
        }

        /// <summary>The open window's per-frame work: yield to whatever took over, Esc while the field is not focused,
        /// give the field its focus back, scroll, and re-place the window when a neighbour came or went.</summary>
        private void UpdateWindowFrame()
        {
            if (!WindowOpen)
            {
                return;
            }

            if (MustYield())
            {
                CloseWindow();
                return;
            }

            // A focused field handles Esc itself (OnEndEdit); between a click elsewhere and the refocus it is this one.
            if (_mode != ChatWindowMode.Pad && Input.GetKeyDown(KeyCode.Escape) && (_input == null || !_input.isFocused))
            {
                Game.MarkMenuInputHandled(); // the same Esc must not also open the pause menu
                CloseWindow();
                return;
            }

            Refocus();
            ScrollStep();
            RelayoutIfMoved();
        }

        /// <summary>
        /// The input field let go of its focus. Enter sends: a chat line closes the window, a slash command keeps it open
        /// until Esc so its answer can be read (#3264). Esc closes. Anything else — a click on the scrollbar, a button,
        /// the world — only took the focus away: the window keeps the keys and refocuses the field next frame.
        /// </summary>
        private void OnEndEdit(string text)
        {
            if (_mode != ChatWindowMode.Keyboard || Time.frameCount == _openFrame)
            {
                return; // closed already, or the Enter that opened the window
            }

            if (_capturing)
            {
                _refocus = true; // the /bump screenshot switched the canvas off under the field (InputFocusGuard)
                return;
            }

            if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))
            {
                bool command = IsCommand(text);
                Submit(text);
                if (command && _mode == ChatWindowMode.Keyboard)
                {
                    _input.text = string.Empty;
                    _refocus = true;
                    return;
                }

                CloseWindow();
                return;
            }

            if (Input.GetKey(KeyCode.Escape))
            {
                Game.MarkMenuInputHandled();
                CloseWindow();
                return;
            }

            _refocus = true;
        }

        /// <summary>Gives the field its focus back after a click elsewhere — never under a canvas that is off (the /bump
        /// capture, the flight view): a field focused there crashes uGUI's caret rebuild (#1805).</summary>
        private void Refocus()
        {
            if (!_refocus || _mode != ChatWindowMode.Keyboard || _capturing || _input == null)
            {
                return;
            }

            if (_canvas == null || !_canvas.enabled || !InputFocusGuard.HasLiveCanvas(_input))
            {
                return;
            }

            _refocus = false;
            if (!_input.isFocused)
            {
                _input.ActivateInputField();
            }
        }

        /// <summary>Drops the EventSystem selection if it still points at the chat box. uGUI never
        /// deselects a deactivated InputField on its own, so after Esc/Enter the hidden box stayed the
        /// "selected" object until the next world click — and every "is a text field focused?" check
        /// (the VEGA continue key first of all) kept answering yes (#1634).</summary>
        private void ReleaseSelection()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _input != null && es.currentSelectedGameObject == _input.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
        }

        /// <summary>The wheel and PageUp/PageDown page through the history (#1922); scrolling up stops once the oldest
        /// line is at the top. The pad's keyboard owns the stick, so its window simply follows the newest lines.</summary>
        private void ScrollStep()
        {
            if (_mode == ChatWindowMode.Pad)
            {
                return;
            }

            int scrolled = ChatScrollback.Step(_bigScroll, _lines.Count, Input.mouseScrollDelta.y,
                Input.GetKeyDown(KeyCode.PageUp), Input.GetKeyDown(KeyCode.PageDown));
            scrolled = Mathf.Min(scrolled, _bigMaxOffset);
            if (scrolled != _bigScroll)
            {
                _bigScroll = scrolled;
                RefreshBig();
            }
        }

        /// <summary>A line arrived: a reader scrolled up keeps the same lines in view and the "New messages ↓" marker
        /// says something came in; at the bottom the window simply follows.</summary>
        private void OnHistoryLineAdded()
        {
            if (!WindowOpen)
            {
                return;
            }

            if (_bigScroll > 0)
            {
                _bigScroll = ChatScrollback.OnLineAdded(_bigScroll, _lines.Count);
                _newBelow++;
            }

            RefreshBig();
        }

        private void OpenPadKeyboard(string initial = "")
        {
            OnScreenKeyboardUi.Open(L("ui.chat.hint"), initial ?? string.Empty, _input != null ? _input.characterLimit : 0,
                OnPadDone, OnPadCancel, dim: false); // the history above the keyboard stays readable
        }

        /// <summary>The pad's keyboard was confirmed: a chat line closes the window, a command keeps it open and brings
        /// the keyboard back (its answer arrives above it; B closes both).</summary>
        private void OnPadDone(string text)
        {
            if (this == null || _mode != ChatWindowMode.Pad)
            {
                return;
            }

            bool command = IsCommand(text);
            Submit(text);
            if (command && _mode == ChatWindowMode.Pad)
            {
                OpenPadKeyboard();
                return;
            }

            CloseWindow();
        }

        private void OnPadCancel()
        {
            if (this != null && _mode == ChatWindowMode.Pad)
            {
                CloseWindow();
            }
        }

        private void StartPrompt()
        {
            if (_promptRoutine == null && isActiveAndEnabled)
            {
                _promptRoutine = StartCoroutine(PromptAfterAFrame());
            }
        }

        /// <summary>The browser prompt of a touch browser blocks the page with the last drawn frame behind it, so the
        /// window is drawn once first. A chat line closes the window; a command keeps it open for its answer (× or the
        /// CHAT button again — the next line); a cancelled prompt closes it.</summary>
        private IEnumerator PromptAfterAFrame()
        {
            yield return null;
            _promptRoutine = null;
            if (_mode != ChatWindowMode.Prompt)
            {
                yield break;
            }

            string text = TouchTextEntry.Prompt(L("ui.chat.hint"), string.Empty);
            bool command = IsCommand(text);
            Submit(text);
            if (!command && _mode == ChatWindowMode.Prompt)
            {
                CloseWindow();
            }
        }

        /// <summary>Follows the rig's HUD for its damage-cause event (the HUD is built by the same rig, in no fixed
        /// order relative to the chat).</summary>
        private void HookHud()
        {
            var hud = Game.Views.Hud;
            if (hud == _hookedHud)
            {
                return;
            }

            UnhookHud();
            _hookedHud = hud;
            if (hud != null)
            {
                hud.DamageCauseShown += OnDamageCause;
            }
        }

        private void UnhookHud()
        {
            if (!ReferenceEquals(_hookedHud, null))
            {
                _hookedHud.DamageCauseShown -= OnDamageCause; // a managed delegate list — fine on a destroyed HUD too
            }

            _hookedHud = null;
        }

        /// <summary>
        /// The HUD names what hurt the player in the middle of the screen — under the open window. While the window is
        /// open the cause therefore also lands in the history as a system line; a cause that keeps repeating (lava, a
        /// swarm) is written once until it has paused for <see cref="CauseRepeatSeconds"/>.
        /// </summary>
        private void OnDamageCause(string key)
        {
            if (!WindowOpen || string.IsNullOrEmpty(key))
            {
                return;
            }

            string text = L(key);
            float now = Time.unscaledTime;
            bool repeat = text == _lastCause && now - _lastCauseAt < CauseRepeatSeconds;
            _lastCause = text;
            _lastCauseAt = now;
            if (!repeat)
            {
                Append($"<color=#FF8A7A>{ChatMarkup.RichSafe(text)}</color>");
            }
        }
    }
}
