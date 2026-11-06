// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The compact chat lane of <see cref="ChatUi"/>: incoming lines at the left edge while the big window is closed.
    /// The lines live in a holo window (#1799) — the same chrome as VEGA's speech panel, fitted to the lines it shows — so
    /// the text reads on snow, sand and sky where the bare overlay of #643 washed out. The window comes and goes with its
    /// content: lines age out after <see cref="FadeSeconds"/> unless the player picked another
    /// <see cref="ChatVisibility"/>, the toggle key (default J) mutes it for the session, and no lines means no window.
    /// While the big window is open (#3264) the lane is hidden; on close it comes back without the lines read there.
    /// </summary>
    public sealed partial class ChatUi
    {
        private RectTransform _window;
        private CanvasGroup _windowGroup;
        private TextMeshProUGUI _log;
        private bool _windowShown;
        private float _nextRefresh = float.MaxValue;
        private int _laneFromSeq; // #3264: the first line (by _appended count) the lane may show — later than the window's read lines

        private const int VisibleLines = 12;       // the full lane fits 12 rows at 18 px; a /tp list is usually 8-15 lines
        private const float FadeSeconds = 12f;     // how long a line stays up in the Auto mode
        private const float FadeOutSeconds = 0.6f; // the lane's dim-out once its last line has aged out
        private const float ShowSeconds = 0.15f;   // the lane's (and the big window's) fade-in, and the fade-out on an explicit close
        private const int LogFontSize = 18;        // between the old bare overlay (16) and VEGA's chip (20)

        // The left lane in HUD reference space (1536×864) — see BuildLane, ResolveLane and ResolveWindow.
        private const float LaneX = 10f, LaneW = 380f;
        private const float LaneTop = 280f, LaneBottom = 590f; // between the toast (268) and VEGA's chip (594)
        private const float InputRowY = 596f, InputRowH = 44f, LaneGap = 6f;
        private const float WindowPad = 10f; // holo window inset around the text block

        // VEGA visibility as of the last refresh — the lane is re-resolved when either flips (UpdateLaneFrame).
        private bool _vegaSpeechSeen, _vegaChipSeen;

        /// <summary>Where the scrollback and the input row sit this frame, in HUD reference units.</summary>
        public readonly struct ChatLane
        {
            public ChatLane(float logY, float logH, float inputY)
            {
                LogY = logY;
                LogH = logH;
                InputY = inputY;
            }

            public float LogY { get; }

            public float LogH { get; }

            public float InputY { get; }
        }

        /// <summary>
        /// Lane arbitration with VEGA: the chat (#643) and VEGA's speech panel + objective chip (#482) were
        /// both placed in the "free" left column, and the chat — sorted above VEGA — drew straight across a
        /// story line while its input row sat exactly on the objective chip. VEGA is the story-critical,
        /// transient one, so the chat yields: while a speech line is up the scrollback ends above the
        /// panel, and while typing with either VEGA element up the input row stacks directly under the
        /// (shortened) scrollback instead of on top of the chip. With VEGA quiet the lane is the old one.
        /// Since #3264 the lane never types (the input row lives in the big window), so the lane passes
        /// <c>typing: false</c>; the typing geometry stays pinned by its EditMode test. Pure so an EditMode test can
        /// pin the geometry.
        /// </summary>
        public static ChatLane ResolveLane(bool typing, bool speechVisible, bool chipVisible)
        {
            float bottom = speechVisible ? VegaPanel.SpeechY - LaneGap : LaneBottom;
            float inputY = InputRowY;
            if (typing && (speechVisible || chipVisible))
            {
                inputY = bottom - InputRowH;
                bottom = inputY - LaneGap;
            }

            return new ChatLane(LaneTop, Mathf.Max(0f, bottom - LaneTop), inputY);
        }

        /// <summary>The holo window's rect this frame plus where its two rows sit inside it, in HUD reference
        /// units. <see cref="H"/> is 0 when there is nothing to frame (no lines, not typing).</summary>
        public readonly struct ChatWindow
        {
            public ChatWindow(float y, float h, float textH, float inputY, float capacity)
            {
                Y = y;
                H = h;
                TextH = textH;
                InputY = inputY;
                Capacity = capacity;
            }

            /// <summary>Window top, canvas space (x = <c>LaneX</c>, width = <c>LaneW</c>).</summary>
            public float Y { get; }

            /// <summary>Window height; 0 = hidden.</summary>
            public float H { get; }

            /// <summary>Height of the text block inside the window (0 = no block). It sits at <c>WindowPad</c>.</summary>
            public float TextH { get; }

            /// <summary>Window-local top of the input row (only meaningful while typing).</summary>
            public float InputY { get; }

            /// <summary>The tallest text block that fits — the caller drops rows until its block measures at most this.</summary>
            public float Capacity { get; }
        }

        /// <summary>
        /// The window hugs its content (#1799): it ends where the lane ends (or under the input row while
        /// typing), grows upward by exactly the measured text block plus padding, and never rises above the
        /// lane top — the toast sits right there. While typing the input row is the window's bottom row, so
        /// the box the player types into is framed together with the lines it answers; with no lines that
        /// leaves a compact input frame. No text and no typing → no window at all. Pure for the EditMode test.
        /// </summary>
        public static ChatWindow ResolveWindow(bool typing, bool speechVisible, bool chipVisible, float contentH)
        {
            var lane = ResolveLane(typing, speechVisible, chipVisible);
            float bottom = typing ? lane.InputY + InputRowH : lane.LogY + lane.LogH;
            float inputH = typing ? InputRowH : 0f;
            float capacity = Mathf.Max(0f, bottom - LaneTop - inputH - 2f * WindowPad);
            float textH = Mathf.Clamp(contentH, 0f, capacity);
            float textBlock = textH > 0f ? textH + 2f * WindowPad : 0f;
            float h = textBlock + inputH;
            return new ChatWindow(bottom - h, h, textH, textBlock, capacity);
        }

        /// <summary>The lane's per-frame work: re-lay out when VEGA's speech panel or objective chip appears or goes (two
        /// bool reads, no allocation), and re-render when the next line is due to age out (RefreshLog parks
        /// <see cref="_nextRefresh"/> at float.MaxValue whenever nothing is pending).</summary>
        private void UpdateLaneFrame()
        {
            var vega = Game.Views.Vega;
            bool speechUp = vega != null && vega.SpeechVisible;
            bool chipUp = vega != null && vega.ChipVisible;
            if (speechUp != _vegaSpeechSeen || chipUp != _vegaChipSeen || Time.unscaledTime >= _nextRefresh)
            {
                RefreshLog();
            }
        }

        /// <summary>
        /// Re-renders the lane for the current moment: which lines are still young enough to show, and how bright.
        /// Called when something changed or a line is due to expire — never blindly every frame;
        /// <see cref="_nextRefresh"/> carries the next moment worth redrawing at. Hidden while the big window is open.
        /// </summary>
        private void RefreshLog()
        {
            if (_log == null || _window == null)
            {
                return;
            }

            var vega = Game?.Views.Vega;
            _vegaSpeechSeen = vega != null && vega.SpeechVisible;
            _vegaChipSeen = vega != null && vega.ChipVisible;
            _nextRefresh = float.MaxValue;
            if (WindowOpen)
            {
                SetWindowShown(false, ShowSeconds); // the big window shows the whole history meanwhile (#3264)
                return;
            }

            int end = _lines.Count;
            int from = LaneFirstLine();
            float textH = 0f;
            string block = from < end ? FitLaneBlock(ref from, end, out textH) : string.Empty;

            // Idle chat yields to VEGA's speech panel (#1798).
            var win = ResolveWindow(false, _vegaSpeechSeen, _vegaChipSeen, textH);
            bool visible = win.H > 0f;
            if (visible)
            {
                // Content and geometry change only while there is something to show: a window on its way
                // out keeps its last lines so they dim rather than vanish a beat before the frame does.
                _log.text = block;
                UiKit.Place(_window.gameObject, LaneX, win.Y, LaneW, win.H);
                UiKit.Place(_log.gameObject, WindowPad, WindowPad, LaneW - 2f * WindowPad, Mathf.Max(win.TextH, 1f));
            }

            // Lines that aged out dim over their final moment; an explicit close (the J key, the Off mode) or an empty
            // history goes as briskly as the window came.
            bool agedOut = !visible && Mode != ChatVisibility.Off && _lines.Count > 0;
            SetWindowShown(visible, agedOut ? FadeOutSeconds : ShowSeconds);
        }

        /// <summary>
        /// The oldest line the lane may show: none the player already read in the big window (#3264), at most
        /// <see cref="VisibleLines"/> back, and — in the Auto mode — none that has aged out. The oldest row still up is the
        /// next to go, so that is the next moment worth redrawing at; once the last one is gone the whole window dims out.
        /// </summary>
        private int LaneFirstLine()
        {
            var mode = Mode;
            int count = _lines.Count;
            if (mode == ChatVisibility.Off)
            {
                return count;
            }

            int unread = Mathf.Clamp(_laneFromSeq - (_appended - count), 0, count);
            int from = Mathf.Max(unread, count - VisibleLines);
            if (mode == ChatVisibility.Always)
            {
                return from;
            }

            float now = Time.unscaledTime;
            while (from < count && now - _lines[from].Time >= FadeSeconds)
            {
                from++;
            }

            if (from < count)
            {
                _nextRefresh = Mathf.Min(_nextRefresh, _lines[from].Time + FadeSeconds);
            }

            return from;
        }

        /// <summary>
        /// Fits the block to the lane window's capacity: drops the oldest lines until what is left measures in (TMP lays
        /// the block out against the window's inner width without rendering it). At most a dozen measurements per
        /// refresh, and refreshes are rare (a new line, a fade tick, a VEGA flip). A window that is hidden right now is
        /// woken for the measurement — TMP needs a live rect.
        /// </summary>
        private string FitLaneBlock(ref int from, int end, out float textH)
        {
            if (!_window.gameObject.activeSelf)
            {
                _window.gameObject.SetActive(true);
            }

            float capacity = ResolveWindow(false, _vegaSpeechSeen, _vegaChipSeen, float.MaxValue).Capacity;
            string block = Compose(from, end, trimmed: false);
            textH = Measure(block);
            while (from < end - 1 && textH > capacity)
            {
                from++;
                block = Compose(from, end, trimmed: true);
                textH = Measure(block);
            }

            return block;
        }

        /// <summary>Height of <paramref name="block"/> laid out at the lane window's inner width, canvas units.</summary>
        private float Measure(string block)
            => _log.GetPreferredValues(block, LaneW - 2f * WindowPad, 10000f).y;

        /// <summary>Fades the lane's holo window in (<see cref="ShowSeconds"/>) or out over <paramref name="fadeOut"/>
        /// and deactivates it once gone. A line arriving mid-fade simply reverses the tween from where it is.</summary>
        private void SetWindowShown(bool shown, float fadeOut)
        {
            if (_window == null || _windowGroup == null)
            {
                return;
            }

            if (shown)
            {
                if (!_window.gameObject.activeSelf)
                {
                    _window.gameObject.SetActive(true);
                }

                if (!_windowShown)
                {
                    _windowShown = true;
                    UiTween.Alpha(_windowGroup, 1f, ShowSeconds);
                }

                return;
            }

            if (!_windowShown)
            {
                if (_window.gameObject.activeSelf)
                {
                    _window.gameObject.SetActive(false); // woken for a measurement that found nothing to show
                }

                return;
            }

            _windowShown = false;
            UiTween.Alpha(_windowGroup, 0f, fadeOut, UiTween.Ease.OutCubic, 0f, () =>
            {
                if (_window != null && !_windowShown)
                {
                    _window.gameObject.SetActive(false);
                }
            });
        }

        /// <summary>The history from index <paramref name="from"/> up to (excluding) <paramref name="end"/>, one per row,
        /// with a dim hint row on top when lines were dropped to fit: "press Enter to read everything" — the big window
        /// holds the whole history (#1922, #3264).</summary>
        private string Compose(int from, int end, bool trimmed)
        {
            var sb = new System.Text.StringBuilder();
            if (trimmed)
            {
                sb.AppendLine(HintRow("ui.chat.scroll_open"));
            }

            for (int i = from; i < end; i++)
            {
                sb.AppendLine(_lines[i].Text);
            }

            return sb.ToString();
        }

        private string HintRow(string key) => $"<color=#80E5D2><size=85%>{ChatMarkup.RichSafe(L(key))}</size></color>";

        private void KillLaneTweens()
        {
            if (_windowGroup != null)
            {
                UiTween.Kill(_windowGroup);
            }
        }

        /// <summary>
        /// The lane lives in the left column between the vitals panel (ends y 260) and the scan panel (starts y 650). The
        /// old bottom-left placement covered ~85 % of the scan panel plus the left hotbar cells and the controls hint
        /// line. WIDTH IS CAPPED for the same reason the scan panel caps its own: the hotbar backplate owns x 400…1136, so
        /// this lane must not reach x 400. In the flight view the lane is clear too — its instruments sit at the very
        /// bottom (y 818+). The lane is SHARED with VEGA (speech panel y 396…586, objective chip y 594…642): RefreshLog
        /// re-places the window via ResolveLane/ResolveWindow whenever VEGA shows or hides one of hers.
        ///
        /// The window (#1799) is VEGA's speech-panel chrome — same fill, radius and glow — so the two read as one family in
        /// the column; the bare overlay of #643 had no backplate and washed out on bright ground. It starts hidden and
        /// transparent; RefreshLog sizes it to its lines and fades it in and out (a CanvasGroup — UiHolo keeps the vertex
        /// alpha free for exactly that). Since #3264 it only shows lines — nothing in it takes a click.
        /// </summary>
        private void BuildLane(Transform root)
        {
            var panel = UiHolo.AddPanel(root, LaneX, LaneBottom, LaneW, 1f, new Color(0.05f, 0.10f, 0.16f, 0.82f), 12f, 1.5f, 1.2f);
            panel.gameObject.name = "ChatLane";
            panel.raycastTarget = false;
            _window = panel.rectTransform;
            _windowGroup = _window.gameObject.AddComponent<CanvasGroup>();
            _windowGroup.alpha = 0f;
            _windowGroup.interactable = false;
            _windowGroup.blocksRaycasts = false;

            _log = UiText.Add(_window, WindowPad, WindowPad, LaneW - 2f * WindowPad, 1f, string.Empty, LogFontSize, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Normal, UiText.Look.Outline);
            UiText.Wrap(_log); // wrap inside the window; the block is fitted to its measured height, never clipped
            _window.gameObject.SetActive(false); // no lines yet — no window (RefreshLog wakes it)
        }
    }
}
