// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>What one caption shows right now (#2468).</summary>
    public readonly struct CaptionView
    {
        public CaptionView(PlannedCaption caption, int page, string text, int visibleChars, float alpha, bool waitsForReader)
        {
            Caption = caption;
            Page = page;
            Text = text;
            VisibleChars = visibleChars;
            Alpha = alpha;
            WaitsForReader = waitsForReader;
        }

        public PlannedCaption Caption { get; }

        /// <summary>The page index shown.</summary>
        public int Page { get; }

        /// <summary>The page's whole text.</summary>
        public string Text { get; }

        /// <summary>How many characters of <see cref="Text"/> have typed out (all of them unless a speaker box types).</summary>
        public int VisibleChars { get; }

        /// <summary>0..1 fade.</summary>
        public float Alpha { get; }

        /// <summary>A page-gated page that has typed out: the continue control is live.</summary>
        public bool WaitsForReader { get; }

        /// <summary>The part of the page shown so far.</summary>
        public string Shown => VisibleChars >= Text.Length ? Text : Text.Substring(0, Math.Max(0, VisibleChars));
    }

    /// <summary>
    /// Runs a <see cref="CutscenePlan"/> against the clock (#2468): which shot is on, how far into it, which captions show
    /// which page, when a sound cue is due, and where the shot waits for the reader. Pure and Unity-free — the director
    /// feeds it the frame time (the game never scales time; the clip recorder's fixed capture rate keeps it deterministic)
    /// and the continue/skip presses, and draws what it reports.
    /// <list type="bullet">
    /// <item>A page-gated caption (subtitle, speaker box, reading frame with "Story text: wait for me") shows page by page
    /// on <see cref="Continue"/>; once its window has run out, the shot holds there (the camera keeps moving) until the
    /// reader has turned the last page. A first press on a typing speaker box completes the typing.</item>
    /// <item>Timed captions page through their planned page lengths and fade in and out.</item>
    /// <item>A skip is accepted after the scene's <c>minDisplay</c>; a requested replay is skippable at once, even a scene
    /// marked unskippable.</item>
    /// </list>
    /// </summary>
    public sealed class CutscenePlayback
    {
        /// <summary>Caption fade in/out (seconds).</summary>
        public const float CaptionFade = 0.35f;

        private readonly CutscenePlan _plan;
        private int[] _page = Array.Empty<int>();
        private bool[] _acked = Array.Empty<bool>();
        private bool[] _started = Array.Empty<bool>();
        private float[] _pageStart = Array.Empty<float>();
        private bool[] _typed = Array.Empty<bool>();
        private bool[] _sfxFired = Array.Empty<bool>();
        private readonly List<string> _dueSfx = new List<string>();
        private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        private bool _shotChanged = true;

        public CutscenePlayback(CutscenePlan plan)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            EnterShot(0);
        }

        public CutscenePlan Plan => _plan;

        /// <summary>Seconds since the scene started (real time, including every wait).</summary>
        public float Elapsed { get; private set; }

        /// <summary>The shot on screen.</summary>
        public int ShotIndex { get; private set; }

        /// <summary>Seconds into the shot (stands still while it waits for the reader).</summary>
        public float ShotTime { get; private set; }

        /// <summary>The scene ran through (or was ended).</summary>
        public bool Done { get; private set; }

        /// <summary>The shot holds because a page-gated caption waits for the reader.</summary>
        public bool Waiting { get; private set; }

        public PlannedShot Shot => _plan.Shots[Math.Min(ShotIndex, _plan.Shots.Count - 1)];

        /// <summary>Whether a skip is accepted now.</summary>
        public bool CanSkip => _plan.Replay || (_plan.Skippable && Elapsed >= _plan.MinDisplay);

        /// <summary>#2474: the caption keys whose last page has been fully on screen (typed out, turned, or its shot ran
        /// through).</summary>
        public IReadOnlyCollection<string> ShownKeys => _shown;

        /// <summary>#2474: what a skip gives back to the narrator panel — the plan's <see cref="CutscenePlan.ReturnOnSkip"/>
        /// lines whose caption has not been on screen yet (the player skipped before VEGA said it).</summary>
        public IReadOnlyList<string> KeysToReturnOnSkip()
        {
            var keys = new List<string>();
            foreach (string key in _plan.ReturnOnSkip)
            {
                if (!_shown.Contains(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        /// <summary>True once per shot start (the first shot included) until read — the director re-stages the camera.</summary>
        public bool TakeShotChanged()
        {
            bool changed = _shotChanged;
            _shotChanged = false;
            return changed;
        }

        /// <summary>The sound cues that came due since the last call.</summary>
        public IReadOnlyList<string> TakeDueSfx()
        {
            var due = _dueSfx.ToArray();
            _dueSfx.Clear();
            return due;
        }

        /// <summary>Advances the scene by <paramref name="dt"/> seconds.</summary>
        public void Tick(float dt)
        {
            if (Done || dt <= 0f)
            {
                return;
            }

            Elapsed += dt;
            var shot = Shot;
            float before = ShotTime;
            float t = before + dt;

            // An open page-gated caption holds the shot at its end. Repeat until stable: pulling the time back may drop a
            // caption that appeared later out of the window again.
            bool clamped = true;
            while (clamped)
            {
                clamped = false;
                for (int i = 0; i < shot.Captions.Count; i++)
                {
                    var c = shot.Captions[i];
                    if (c.PageGated && !_acked[i] && c.At <= t && t > c.End)
                    {
                        t = Math.Max(before, c.End);
                        clamped = true;
                    }
                }
            }

            bool waiting = false;
            for (int i = 0; i < shot.Captions.Count; i++)
            {
                var c = shot.Captions[i];
                if (c.At > t)
                {
                    continue;
                }

                if (!_started[i])
                {
                    _started[i] = true;
                    _pageStart[i] = Elapsed - (t - c.At); // the page appeared at its "at", not at the end of this frame
                }

                waiting |= c.PageGated && !_acked[i] && t >= c.End;
            }

            ShotTime = t;
            Waiting = waiting;
            for (int i = 0; i < shot.Sfx.Count; i++)
            {
                if (!_sfxFired[i] && shot.Sfx[i].At <= ShotTime)
                {
                    _sfxFired[i] = true;
                    _dueSfx.Add(shot.Sfx[i].Cue);
                }
            }

            RecordShown(shot);
            if (!Waiting && ShotTime >= shot.Duration && !AnyPageGatedOpen(shot))
            {
                foreach (var c in shot.Captions)
                {
                    MarkShown(c); // the shot ran through: every caption had its window
                }

                if (ShotIndex + 1 >= _plan.Shots.Count)
                {
                    Done = true;
                }
                else
                {
                    EnterShot(ShotIndex + 1);
                }
            }
        }

        /// <summary>Records the captions of the current shot whose last page is fully on screen now.</summary>
        private void RecordShown(PlannedShot shot)
        {
            for (int i = 0; i < shot.Captions.Count; i++)
            {
                var c = shot.Captions[i];
                if (!_started[i] || ShotTime < c.At || c.Pages.Count == 0)
                {
                    continue;
                }

                int last = c.Pages.Count - 1;
                bool typedOut = TypedChars(i, c, c.Pages[last]) >= c.Pages[last].Length;
                bool shown = c.PageGated
                    ? _acked[i] || (_page[i] >= last && typedOut)
                    : TimedPage(c, ShotTime - c.At) >= last && typedOut;
                if (shown)
                {
                    MarkShown(c);
                }
            }
        }

        private void MarkShown(PlannedCaption c)
        {
            if (!string.IsNullOrEmpty(c.Key))
            {
                _shown.Add(c.Key);
            }
        }

        /// <summary>The reader's continue (the VEGA continue key, a click or tap on the caption or the ▶ button): completes a
        /// typing speaker box, else turns the page of the first page-gated caption on screen; the last page closes it.
        /// Returns false when nothing waited for it.</summary>
        public bool Continue()
        {
            if (Done)
            {
                return false;
            }

            var shot = Shot;
            for (int i = 0; i < shot.Captions.Count; i++)
            {
                var c = shot.Captions[i];
                if (!c.PageGated || _acked[i] || !_started[i] || ShotTime < c.At)
                {
                    continue;
                }

                string page = c.Pages[Math.Min(_page[i], c.Pages.Count - 1)];
                if (TypedChars(i, c, page) < page.Length)
                {
                    _typed[i] = true;
                    return true;
                }

                _page[i]++;
                _pageStart[i] = Elapsed;
                _typed[i] = false;
                if (_page[i] >= c.Pages.Count)
                {
                    _acked[i] = true;
                    MarkShown(c); // the reader turned its last page
                }

                return true;
            }

            return false;
        }

        /// <summary>Ends the scene now (skip, interrupt, teardown).</summary>
        public void End() => Done = true;

        /// <summary>What every caption of the current shot shows now; hidden captions are left out.</summary>
        public IReadOnlyList<CaptionView> Visible()
        {
            var views = new List<CaptionView>();
            if (Done)
            {
                return views;
            }

            var shot = Shot;
            for (int i = 0; i < shot.Captions.Count; i++)
            {
                var c = shot.Captions[i];
                if (!_started[i] || ShotTime < c.At)
                {
                    continue;
                }

                if (c.PageGated)
                {
                    if (_acked[i])
                    {
                        continue;
                    }

                    int p = Math.Min(_page[i], c.Pages.Count - 1);
                    string page = c.Pages[p];
                    int typed = TypedChars(i, c, page);
                    float alpha = p > 0 ? 1f : Clamp01((Elapsed - _pageStart[i]) / CaptionFade); // fades in once, pages cut
                    views.Add(new CaptionView(c, p, page, typed, alpha, typed >= page.Length));
                }
                else
                {
                    if (ShotTime >= c.End)
                    {
                        continue;
                    }

                    int p = TimedPage(c, ShotTime - c.At);
                    float alpha = CinematicTimeline.FadeWindow(ShotTime, c.At, c.End, CaptionFade);
                    string page = c.Pages[p];
                    views.Add(new CaptionView(c, p, page, page.Length, alpha, waitsForReader: false));
                }
            }

            return views;
        }

        /// <summary>Whether a page-gated caption on screen has typed out its page — the continue control shows.</summary>
        public bool ReaderCanContinue
        {
            get
            {
                foreach (var v in Visible())
                {
                    if (v.WaitsForReader)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void EnterShot(int index)
        {
            ShotIndex = index;
            ShotTime = 0f;
            Waiting = false;
            var shot = Shot;
            int n = shot.Captions.Count;
            _page = new int[n];
            _acked = new bool[n];
            _started = new bool[n];
            _pageStart = new float[n];
            _typed = new bool[n];
            _sfxFired = new bool[shot.Sfx.Count];
            _shotChanged = true;
        }

        private bool AnyPageGatedOpen(PlannedShot shot)
        {
            for (int i = 0; i < shot.Captions.Count; i++)
            {
                if (shot.Captions[i].PageGated && !_acked[i])
                {
                    return true;
                }
            }

            return false;
        }

        private int TypedChars(int i, PlannedCaption c, string page)
        {
            if (!c.Typewriter || _typed[i])
            {
                return page.Length;
            }

            float age = Math.Max(0f, Elapsed - _pageStart[i]);
            return Math.Min(page.Length, (int)(age * CutscenePlan.TypewriterCharsPerSecond));
        }

        private static int TimedPage(PlannedCaption c, float sinceAt)
        {
            float acc = 0f;
            for (int p = 0; p < c.PageSeconds.Count; p++)
            {
                acc += c.PageSeconds[p];
                if (sinceAt < acc)
                {
                    return Math.Min(p, c.Pages.Count - 1);
                }
            }

            return c.Pages.Count - 1;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
