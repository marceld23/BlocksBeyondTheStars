// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ending as a shown thing (#1124): when the server says the story is RESOLVED, this plays a
    /// three-leg cinematic over the running game — the resolution card, a credits roll (the same
    /// <c>ui.credits.body</c> the main menu shows), and the epilogue card that hands over to the
    /// post-story goal. Skippable with Esc (never a bare "any key": the player may be mid-walk when it
    /// starts), queued while a menu is open (the Story tab's replay button would otherwise race its own
    /// menu), and re-viewable any time — the server replays it on request. Timed with
    /// <see cref="CinematicTimeline"/> and framed by <see cref="CinematicFrame"/> like the intro.
    /// </summary>
    public sealed class ResolutionCinematic : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Singleton so the finale banner (IMGUI draws above every canvas) can yield to us.</summary>
        public static ResolutionCinematic Instance { get; private set; }

        /// <summary>True while the cinematic is on screen.</summary>
        public bool Playing => _playing;

        // Legs: resolution card → credits roll → epilogue handover. ~43 s unskipped.
        private static readonly float[] LegDurations = { 7f, 26f, 10f };
        private const int FrameSortingOrder = 68; // between the intro frame (66) and the warp overlay (70)
        private const float CreditsWidth = 1000f;

        private CinematicTimeline _timeline;
        private bool _subscribed;
        private StoryResolved _pending; // received but not started yet (a menu may be open)
        private StoryResolved _msg;
        private bool _playing;
        private float _elapsed;
        private CinematicFrame _frame;
        private Text _title;
        private Text _body;
        private float _bodyHeight;

        private void Awake()
        {
            Instance = this;
            _timeline = new CinematicTimeline(LegDurations);
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.StoryResolvedReceived += m => _pending = m;
                _subscribed = true;
            }

            // Start only once no menu is open — the Story tab's replay button stays visible under us
            // otherwise, and a joiner's catch-up must not fight the loading/menu flow.
            if (!_playing && _pending != null && Game != null && !Game.MenuOpen)
            {
                Begin(_pending);
                _pending = null;
            }

            if (!_playing)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            bool skip = Input.GetKeyDown(KeyCode.Escape);
            if (skip)
            {
                Game?.MarkMenuInputHandled(); // this Esc skips the ending — don't also pop the quit prompt (#1151)
            }

            if (_timeline.Done(_elapsed) || skip)
            {
                Finish();
                return;
            }

            Animate();
        }

        private void Begin(StoryResolved msg)
        {
            _msg = msg;
            _elapsed = 0f;
            _playing = true;
            _frame = CinematicFrame.Create("ResolutionFrame", FrameSortingOrder);
            _frame.SetFade(0f);
            _frame.SetLetterbox(1f);

            // Title card text (leg 0) and the roll/epilogue body share two child texts on the frame's canvas.
            _title = UiKit.AddText(_frame.Root, 0, 0, CreditsWidth, 200f, string.Empty, 44,
                new Color(0.9f, 0.96f, 1f, 0f), TextAnchor.MiddleCenter, FontStyle.Bold);
            CenterAnchored(_title.rectTransform, new Vector2(0f, 120f));

            _body = UiKit.AddText(_frame.Root, 0, 0, CreditsWidth, 400f, string.Empty, 26,
                new Color(0.85f, 0.92f, 1f, 0f), TextAnchor.UpperCenter);
            CenterAnchored(_body.rectTransform, Vector2.zero);
        }

        private static void CenterAnchored(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void Animate()
        {
            var (leg, progress) = _timeline.At(_elapsed);

            // A dark stage throughout: ease the black in over the first second, out over the last.
            _frame.SetFade(0.88f * CinematicTimeline.FadeWindow(_elapsed, 0f, _timeline.Total, 1.2f));
            _frame.SetHint(L("ui.intro.skip"), _elapsed > 1.5f ? 0.55f : 0f);

            switch (leg)
            {
                case 0: // resolution card: story name + the stand-down line
                    float a0 = CinematicTimeline.FadeWindow(_elapsed, 0.8f, LegDurations[0], 1.2f);
                    SetText(_title, L(_msg.StoryNameKey), a0);
                    SetText(_body, L("ui.finale.resolved"), a0);
                    _body.rectTransform.anchoredPosition = new Vector2(0f, -20f);
                    break;

                case 1: // credits roll: the menu's credits body, scrolled bottom → top
                    SetText(_title, L("ui.credits.title"), CinematicTimeline.FadeWindow(_elapsed,
                        LegDurations[0], LegDurations[0] + 3f, 1f) * 0.9f);
                    _title.rectTransform.anchoredPosition = new Vector2(0f, 380f);
                    if (_bodyHeight <= 0f)
                    {
                        SetText(_body, L("ui.credits.body"), 1f);
                        _bodyHeight = Mathf.Max(400f, _body.preferredHeight);
                        _body.rectTransform.sizeDelta = new Vector2(CreditsWidth, _bodyHeight);
                    }

                    SetAlpha(_body, 1f);
                    float travel = _bodyHeight + 1100f; // from below the frame to fully above it
                    _body.rectTransform.anchoredPosition = new Vector2(0f, -560f + travel * progress);
                    break;

                default: // epilogue card: the handover to what starts after the story
                    float a2 = CinematicTimeline.FadeWindow(_elapsed,
                        LegDurations[0] + LegDurations[1] + 0.5f, _timeline.Total - 0.3f, 1.2f);
                    _title.rectTransform.anchoredPosition = new Vector2(0f, 220f);
                    _body.rectTransform.sizeDelta = new Vector2(CreditsWidth, 500f);
                    _body.rectTransform.anchoredPosition = new Vector2(0f, -60f);
                    SetText(_title, _msg.EpilogueTitle, a2);
                    SetText(_body, string.IsNullOrEmpty(_msg.EpilogueTextKey) ? string.Empty : L(_msg.EpilogueTextKey), a2);
                    break;
            }
        }

        private static void SetText(Text t, string text, float alpha)
        {
            t.text = text ?? string.Empty;
            SetAlpha(t, alpha);
        }

        private static void SetAlpha(Text t, float alpha)
        {
            var c = t.color;
            c.a = Mathf.Clamp01(alpha);
            t.color = c;
        }

        private void Finish()
        {
            _playing = false;
            _bodyHeight = 0f;
            _msg = null;
            if (_frame != null)
            {
                Destroy(_frame.gameObject); // tears the canvas down with it
                _frame = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
