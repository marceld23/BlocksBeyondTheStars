// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the main menu's "Join in" button shows about the playtest tasks behind it (<see cref="JoinInMark"/>):
    /// nothing while none waits, a quiet amber frame while some are under way, and — while some are new or ask for a
    /// re-test — the amber frame with a light running around it, an amber label and their number in a pill on the corner.
    /// An install that never opened the Test Pilot page gets the running light without a number until its first visit:
    /// it fetches no catalogue at start-up, so it would never learn that a task waits.
    /// <para>The 12-pixel dot it replaces (<see cref="UiKit.AddBadge"/>) was easy to miss and was placed once, when the
    /// menu was built: the catalogue's fetch ends a moment later, so a new task showed only after the menu had been
    /// rebuilt for some other reason. This asks the tracker twice a second and redraws when the answer changed.</para>
    /// <para>Amber is <see cref="UiKit.Warn"/> — the colour of the "New" chip on the Test Pilot page, and one that
    /// follows the player's colour-vision palette; the number says the same without a colour. Under
    /// <see cref="UiKit.ReducedMotion"/> the light stands still (the frame fully lit). The icon stays as it is: the
    /// line icons are cyan art, and a tint multiplies — amber over cyan comes out green.</para>
    /// </summary>
    public sealed class UiJoinInMark : MonoBehaviour
    {
        private const float CheckSeconds = 0.5f;
        private const float PillH = 26f, PillRim = 2f, PillOut = 9f, PillUp = 12f;

        private static readonly Color PillInk = new Color(0.16f, 0.10f, 0.01f);
        private static readonly Color PillRimCol = new Color(0.01f, 0.03f, 0.07f);

        private readonly List<GameObject> _parts = new List<GameObject>();
        private Button _button;
        private float _buttonW;
        private JoinInMarkKind _kind = JoinInMarkKind.None;
        private int _count;
        private float _nextCheck;

        /// <summary>Puts the mark on a freshly built quiet <paramref name="button"/> of width <paramref name="buttonW"/>.</summary>
        public static UiJoinInMark Attach(Button button, float buttonW)
        {
            if (button == null)
            {
                return null;
            }

            var mark = button.gameObject.AddComponent<UiJoinInMark>();
            mark._button = button;
            mark._buttonW = buttonW;
            mark.Refresh();
            return mark;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckSeconds;
                Refresh();
            }
        }

        private void Refresh()
        {
            var hub = PlaytestHub.Instance; // desktop builds only — without it there is never a task
            int fresh = hub != null ? hub.Tracker.NewCount : 0;
            int open = hub != null ? hub.Tracker.OpenCount : 0;
            // An install that never opened the Test Pilot page knows no task (it fetches none at start-up): there the
            // button invites by itself until the first visit.
            bool visited = hub == null || hub.Progress.Visited;
            Show(JoinInMark.Kind(fresh, open, visited, hub != null && hub.Configured), fresh);
        }

        /// <summary>Draws the mark for <paramref name="kind"/> with <paramref name="count"/> new tasks; a no-op when
        /// that is what the button shows already.</summary>
        public void Show(JoinInMarkKind kind, int count)
        {
            if (_button == null || (kind == _kind && count == _count))
            {
                return;
            }

            _kind = kind;
            _count = count;
            Clear();
            UiKit.StyleTier(_button, ButtonTier.Quiet); // back to the neighbours' look (the label's colour above all)
            if (kind == JoinInMarkKind.None || !(_button.transform is RectTransform frame))
            {
                return;
            }

            Color amber = UiKit.Warn;
            var rest = new Color(amber.r * 0.62f, amber.g * 0.62f, amber.b * 0.62f, 1f);
            bool lit = kind == JoinInMarkKind.New || kind == JoinInMarkKind.Invite;
            var light = UiRunningLight.Attach(frame, lit ? Color.Lerp(rest, amber, 0.25f) : rest, amber,
                Color.Lerp(amber, Color.white, 0.75f), running: lit);
            _parts.Add(light.gameObject);
            if (!lit)
            {
                return;
            }

            foreach (var label in _button.GetComponentsInChildren<Text>(true))
            {
                label.color = amber;
            }

            if (kind == JoinInMarkKind.New)
            {
                AddPill(JoinInMark.CountLabel(count), amber); // an invitation knows no number
            }
        }

        // The number, in a pill that sits on the button's top right corner and reaches a little past it. The dark rim
        // keeps it apart from the frame it crosses.
        private void AddPill(string text, Color amber)
        {
            float w = Mathf.Max(PillH, Mathf.Ceil(UiKit.MeasureWidth(text, 18)) + 14f);
            float x = _buttonW - w + PillOut;
            var rim = UiKit.AddImage(_button.transform, x - PillRim, -PillUp - PillRim, w + (2f * PillRim), PillH + (2f * PillRim),
                UiKit.PillSprite, PillRimCol, Image.Type.Sliced);
            var pill = UiKit.AddImage(_button.transform, x, -PillUp, w, PillH, UiKit.PillSprite, amber, Image.Type.Sliced);
            UiKit.AddText(pill.transform, 0f, 0f, w, PillH, text, 18, PillInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            _parts.Add(rim.gameObject);
            _parts.Add(pill.gameObject);
        }

        private void Clear()
        {
            foreach (var part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                part.SetActive(false); // gone at once for StyleTier's sweep over the children, destroyed at the frame's end
                if (Application.isPlaying)
                {
                    Destroy(part);
                }
                else
                {
                    DestroyImmediate(part);
                }
            }

            _parts.Clear();
        }
    }
}
