// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ending as a shown thing (#1124): when the server says the story is RESOLVED, this plays a
    /// three-leg cinematic over the running game — the resolution card, a credits roll (the same
    /// <c>ui.credits.body</c> the main menu shows), and the epilogue card that hands over to the
    /// post-story goal. Queued while a menu is open, the loading veil is up or another cutscene plays (the
    /// Story tab's replay button would otherwise race its own menu), and re-viewable any time — the server
    /// replays it on request. Timed with <see cref="CinematicTimeline"/> and framed by <see cref="CinematicFrame"/>
    /// like the intro.
    /// <para>
    /// Since #2461 it is a full cutscene: it opens the shared <see cref="GameBootstrap.Cutscenes"/> state, so the
    /// controls freeze and the HUD, the touch controls, the chat, the nameplates and VEGA's panel step aside; it
    /// skips on every device (Esc, the pad's cancel button, or the on-screen skip button — never a bare "any
    /// key": the player may be mid-walk when it starts); its epilogue title is a locale key; and the music director
    /// plays the resolution track whenever it STARTS (<see cref="Plays"/>), replays included.
    /// </para>
    /// </summary>
    public sealed class ResolutionCinematic : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Singleton so the finale banner (IMGUI draws above every canvas) can yield to us.</summary>
        public static ResolutionCinematic Instance { get; private set; }

        /// <summary>True while the cinematic is on screen.</summary>
        public bool Playing => _playing;

        /// <summary>True while a received ending waits for its moment (#2461) — VEGA's panel holds its lines meanwhile,
        /// so nothing types into the seconds before the ending takes the screen.</summary>
        public bool Pending => _pending != null;

        /// <summary>How many times the ending has STARTED in this world — the music director arms the resolution track
        /// on every new start (#2461, B17), so a replay gets its music and a won save no longer plays it at every login.</summary>
        public int Plays { get; private set; }

        // Legs: resolution card → credits roll → epilogue handover. ~43 s unskipped.
        private static readonly float[] LegDurations = { 7f, 26f, 10f };
        private const int FrameSortingOrder = 68; // between the intro frame (66) and the warp overlay (70)
        private const float CreditsWidth = 1000f;
        private const int TitleFontSize = 44, BodyFontSize = 26;

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
        private WorldLoadingOverlay _veil;
        private System.Func<string, string> _localize; // L as a cached delegate (the skip label asks every frame)

        private void Awake()
        {
            Instance = this;
            _timeline = new CinematicTimeline(LegDurations);
            _localize = L;
        }

        private void Start() => _veil = FindAnyObjectByType<WorldLoadingOverlay>();

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.StoryResolvedReceived += OnResolved;
                _subscribed = true;
            }

            if (!_playing && _pending != null && CanStart())
            {
                Begin(_pending);
                _pending = null;
            }

            if (!_playing)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            bool skip = CinematicFrame.SkipPressed() || (_frame != null && _frame.ConsumeSkipClick());
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

        private void OnResolved(StoryResolved m)
        {
            if (m == null)
            {
                return;
            }

            _pending = m;

            // The live win also speaks the epilogue as a VEGA story line (that is how it enters the story log). The
            // ending shows the same text on its own card a moment later, so the panel must not type it a second
            // time (#2461, B16) — the log keeps it either way (GameBootstrap records kind-2 lines on arrival).
            if (!string.IsNullOrEmpty(m.EpilogueTextKey))
            {
                VegaPanel.Instance?.WithdrawLine(m.EpilogueTextKey);
            }
        }

        /// <summary>The ending waits for a free screen: no menu (the Story tab's replay button), no loading veil (a
        /// join catch-up would otherwise play behind the curtain) and no other cutscene (#2461).</summary>
        private bool CanStart()
        {
            // A director card (#2468) keeps the shared state closed (its input stays free) but owns the frame — wait for it too.
            if (Game == null || Game.MenuOpen || GameBootstrap.CutsceneActive || CutsceneDirector.HoldsScreen)
            {
                return false;
            }

            bool veilUp = !Game.SpaceViewActive && _veil != null && _veil.VeilActive;
            return !veilUp;
        }

        private void Begin(StoryResolved msg)
        {
            _msg = msg;
            _elapsed = 0f;
            _bodyHeight = 0f;
            _playing = true;
            Plays++;
            GameBootstrap.Cutscenes.Begin(CutsceneGate.Resolution); // controls freeze, every overlay steps aside (#2461)

            _frame = CinematicFrame.Create("ResolutionFrame", FrameSortingOrder);
            _frame.EnableSkipButton(); // the way out for touch (and a free mouse) — #2461
            _frame.SetFade(0f);
            _frame.SetLetterbox(1f);

            // Title card text (leg 0) and the roll/epilogue body share two child texts on the frame's canvas. Their
            // fonts follow the player's UI scale like the frame's captions (#2461); both wrap inside the column.
            float scale = UiKit.UserScale;
            _title = UiKit.AddText(_frame.Root, 0, 0, CreditsWidth, 200f, string.Empty, Mathf.RoundToInt(TitleFontSize * scale),
                new Color(0.9f, 0.96f, 1f, 0f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            CenterAnchored(_title.rectTransform, new Vector2(0f, 120f));

            _body = UiKit.AddText(_frame.Root, 0, 0, CreditsWidth, 400f, string.Empty, Mathf.RoundToInt(BodyFontSize * scale),
                new Color(0.85f, 0.92f, 1f, 0f), TextAnchor.UpperCenter);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap; // the epilogue is a paragraph, not one endless line
            CenterAnchored(_body.rectTransform, Vector2.zero);
        }

        private static void CenterAnchored(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>The epilogue card's title: the localized key (#2461, B22) when the server sent one this client
        /// knows, else the pack's raw title (an older server, or a pack without a key).</summary>
        private string EpilogueTitle()
        {
            string key = _msg.EpilogueTitleKey;
            var loc = Game?.Localizer;
            return !string.IsNullOrEmpty(key) && loc != null && loc.Has(key) ? loc.Get(key) : _msg.EpilogueTitle;
        }

        private void Animate()
        {
            var (leg, progress) = _timeline.At(_elapsed);

            // A dark stage throughout: ease the black in over the first second, out over the last.
            _frame.SetFade(0.88f * CinematicTimeline.FadeWindow(_elapsed, 0f, _timeline.Total, 1.2f));
            _frame.SetHint(CinematicFrame.SkipLabel(_localize), _elapsed > 1.5f ? 0.75f : 0f);

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
                    SetText(_title, EpilogueTitle(), a2);
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
            GameBootstrap.Cutscenes.End(CutsceneGate.Resolution);
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

            if (_subscribed && Game?.Network != null)
            {
                Game.Network.StoryResolvedReceived -= OnResolved;
            }

            if (_playing)
            {
                GameBootstrap.Cutscenes.End(CutsceneGate.Resolution);
            }

            // The frame is a root-level object: leaving the world mid-ending must not leave it over the main menu.
            if (_frame != null)
            {
                Destroy(_frame.gameObject);
                _frame = null;
            }
        }
    }
}
