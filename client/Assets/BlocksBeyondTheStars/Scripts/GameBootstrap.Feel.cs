// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's own "HUD off" (<see cref="InputAction.ToggleHud"/>, F10 by default): a clean view of the world for
    /// a look around or a screenshot. It rides on the same switch a cutscene uses (<see cref="HudHidden"/>), so
    /// everything that steps aside for a scene steps aside here too. A short note names the key while the HUD goes —
    /// nobody is left without a HUD and without a clue — and every new world starts with the HUD on.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The player switched the HUD off with their key.</summary>
        public static bool PlayerHudOff { get; private set; }

        private bool _feelStarted;

        private void TickFeel()
        {
            if (!_feelStarted)
            {
                _feelStarted = true;
                PlayerHudOff = false; // a new world: the HUD is back
            }

            if (!InputMap.Down(InputAction.ToggleHud) || InputMap.TextEntryActive || CutsceneActive)
            {
                return;
            }

            PlayerHudOff = !PlayerHudOff;
            if (PlayerHudOff && Localizer != null)
            {
                HudOffNote.Show(Localizer.Get("ui.hud.hidden_note").Replace("{key}", InputMap.Glyph(InputAction.ToggleHud)));
            }
            else
            {
                HudOffNote.Hide();
            }
        }
    }

    /// <summary>The one line that stays when the HUD goes: which key brings it back. Its own small canvas, so it shows
    /// although the HUD does not; it fades by itself after a few seconds.</summary>
    internal static class HudOffNote
    {
        private const float ShowSeconds = 3.5f;

        private static Canvas _canvas;
        private static CanvasGroup _group;
        private static Text _text;
        private static HudOffNoteTimer _timer;

        public static void Show(string text)
        {
            if (_canvas == null)
            {
                _canvas = UiKit.CreateCanvas("HudOffNote");
                _canvas.sortingOrder = 70;
                var raycaster = _canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.enabled = false;
                }

                _group = _canvas.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _text = UiKit.AddText(_canvas.transform, 360f, 40f, 1200f, 40f, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                _text.raycastTarget = false;
                UiKit.AddOutline(_text);
                _timer = _canvas.gameObject.AddComponent<HudOffNoteTimer>();
            }

            _text.text = text;
            _canvas.gameObject.SetActive(true);
            UiTween.Kill(_group);
            _group.alpha = 1f;
            if (UiKit.ReducedMotion)
            {
                // A tween jumps to its end under reduced motion — delay and all — and would hide the note in the very
                // frame it showed. So it stands for its time without a fade and then goes.
                _timer.HideAt = Time.unscaledTime + ShowSeconds;
                return;
            }

            _timer.HideAt = -1f;
            UiTween.Alpha(_group, 0f, 0.6f, UiTween.Ease.InQuad, ShowSeconds, Hide);
        }

        public static void Hide()
        {
            if (_canvas != null)
            {
                UiTween.Kill(_group);
                _canvas.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>The note's own clock for players with reduced effects: it sits on the note's canvas (so it only runs
    /// while the note shows) and hides it once <see cref="HideAt"/> has passed, in real time — the world may be held.</summary>
    internal sealed class HudOffNoteTimer : MonoBehaviour
    {
        /// <summary>The unscaled time at which the note goes; negative = the fade takes care of it.</summary>
        public float HideAt = -1f;

        private void Update()
        {
            if (HideAt >= 0f && Time.unscaledTime >= HideAt)
            {
                HideAt = -1f;
                HudOffNote.Hide();
            }
        }
    }
}
