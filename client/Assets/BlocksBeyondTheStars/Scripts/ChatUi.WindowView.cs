// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The big chat window's view (#3264): a half-transparent holo panel with a title row ("Chat" and ×), a hint row, the
    /// history (normal text colour, 19 px, at most ~75 characters a line at the default width), a scrollbar, a
    /// "New messages ↓" marker while the reader is scrolled up, and the input row. Its place comes from
    /// <see cref="ResolveBigWindow"/> (<see cref="ChatWindowLayout"/>); only the slice of the history that fits is laid
    /// out, each line measured once per text width. It opens like the Tab menu (<c>CraftingTechShipUI.PlayReveal</c>):
    /// the window fades and rises (<see cref="UiKit.TransitionIn"/>), its holo frame wipes on left → right, the header
    /// fades first and the content once the wipe is past half-way — instant under reduced motion — and closes with the
    /// lane's short fade.
    /// </summary>
    public sealed partial class ChatUi
    {
        private const float BigPad = 14f, TitleH = 30f, HintH = 20f, HeaderGap = 6f;
        private const float BarW = 10f, BarGap = 8f, CloseSize = 30f;
        private const float BigInputH = 44f, InputBoxH = 34f;
        private const float MarkerW = 220f, MarkerH = 28f;
        private const int BigFontSize = 19;     // the plan's 18–20 px: bigger than the lane's 18, smaller than VEGA's chip
        private const float BigFillAlpha = 0.72f; // half-transparent: the world (and an attack coming) stays visible around it

        private readonly List<float> _heights = new(); // per history line: its laid-out height at _heightsWidth (NaN = not measured)
        private readonly System.Text.StringBuilder _slice = new();
        private System.Func<int, float> _heightOf;
        private float _heightsWidth = -1f;

        private RectTransform _big;      // placed by ResolveBigWindow; its CanvasGroup carries the close fade
        private CanvasGroup _bigGroup;
        private RectTransform _bigBody;  // fills _big — the TransitionIn target (its home stays at 0,0 wherever _big moves)
        private Image _bigFrame;
        private RectTransform _bigHeader, _bigContent, _bigViewport, _bigNewMarker;
        private CanvasGroup _bigHeaderGroup, _bigContentGroup;
        private TextMeshProUGUI _bigTitle, _bigHint, _bigLog, _bigNewLabel;
        private Button _bigClose;
        private Scrollbar _bigBar;
        private RectTransform _inputRow, _inputBox;
        private InputField _input;
        private Text _inputText, _inputPlaceholder;
        private ChatRect _bigRect;
        private float _bigHistW = 1f, _bigHistH = 1f;

        /// <summary>The big window's rectangle in HUD units for this frame's neighbours and the live canvas size — the
        /// on-screen keyboard's real panel top filled in. Pure for the EditMode test.</summary>
        public static ChatRect ResolveBigWindow(bool vegaSpeech, bool osk, bool touchKeyboard, float canvasW, float canvasH)
            => ChatWindowLayout.Resolve(vegaSpeech, osk, touchKeyboard, canvasW, canvasH, OnScreenKeyboardUi.PanelTop);

        private ChatRect ResolveBigRect()
        {
            var vega = Game?.Views.Vega;
            bool speech = vega != null && vega.SpeechVisible;
            bool osk = _mode == ChatWindowMode.Pad && OnScreenKeyboardUi.IsOpen;
            bool touch = _mode == ChatWindowMode.Prompt || (_mode == ChatWindowMode.Keyboard && TouchControlsUi.ShouldShow());
            var size = ((RectTransform)_canvas.transform).rect.size;
            return ResolveBigWindow(speech, osk, touch, size.x, size.y);
        }

        /// <summary>Re-places the window when VEGA starts or stops talking, the keyboard comes or goes, or the canvas
        /// changed size (resolution, UI scale) — a handful of float compares per frame.</summary>
        private void RelayoutIfMoved()
        {
            var r = ResolveBigRect();
            if (Mathf.Abs(r.X - _bigRect.X) > 0.5f || Mathf.Abs(r.Y - _bigRect.Y) > 0.5f
                || Mathf.Abs(r.W - _bigRect.W) > 0.5f || Mathf.Abs(r.H - _bigRect.H) > 0.5f)
            {
                ApplyBigLayout(r);
                RefreshBig();
            }
        }

        private void ShowBigWindow(bool reveal)
        {
            if (_big == null)
            {
                return;
            }

            _big.gameObject.SetActive(true);
            UiTween.Kill(_bigGroup);
            _bigGroup.alpha = 1f;

            // Re-read on every open: the language can change mid-session.
            _bigTitle.text = L("ui.chat.window_title");
            _bigHint.text = L("ui.chat.window_hint");
            _bigNewLabel.text = L("ui.chat.new_messages");
            _inputPlaceholder.text = L("ui.chat.send_hint");
            ApplyBigLayout(ResolveBigRect());
            RefreshBig();
            if (reveal)
            {
                PlayBigReveal();
            }
            else
            {
                SnapBigReveal();
            }
        }

        private void HideBigWindow()
        {
            if (_big == null || !_big.gameObject.activeSelf)
            {
                return;
            }

            UiTween.Alpha(_bigGroup, 0f, ShowSeconds, UiTween.Ease.OutCubic, 0f, () =>
            {
                if (_big != null && !WindowOpen)
                {
                    _big.gameObject.SetActive(false);
                }
            });
        }

        /// <summary>The Tab menu's opening (<c>CraftingTechShipUI.PlayReveal</c>): the window fades and rises, the header
        /// fades in first, the holo frame wipes on left → right and the content fades up once the wipe is past
        /// half-way. Instant under reduced motion.</summary>
        private void PlayBigReveal()
        {
            UiKit.TransitionIn(_bigBody.gameObject);
            var shape = _bigFrame != null ? _bigFrame.GetComponent<UiHolo.Shape>() : null;
            if (UiKit.ReducedMotion)
            {
                SnapBigReveal();
                return;
            }

            const float wipe = 0.30f, lead = 0.04f, contentFade = 0.22f;
            _bigHeaderGroup.alpha = 0f;
            UiTween.Alpha(_bigHeaderGroup, 1f, 0.18f, UiTween.Ease.OutQuad);
            if (shape != null)
            {
                UiTween.Kill(shape);
                shape.Reveal = 0f;
                UiTween.To(0f, 1f, wipe, r => { if (shape != null) { shape.Reveal = r; } }, UiTween.Ease.OutCubic, lead, null, shape);
            }

            _bigContentGroup.alpha = 0f;
            UiTween.Alpha(_bigContentGroup, 1f, contentFade, UiTween.Ease.OutQuad, lead + (wipe * 0.45f));
        }

        /// <summary>Everything fully drawn at once: reduced motion, and the browser prompt (which would freeze a reveal
        /// half-way).</summary>
        private void SnapBigReveal()
        {
            var shape = _bigFrame != null ? _bigFrame.GetComponent<UiHolo.Shape>() : null;
            if (shape != null)
            {
                UiTween.Kill(shape);
                shape.Reveal = 1f;
            }

            UiTween.Kill(_bigHeaderGroup);
            UiTween.Kill(_bigContentGroup);
            _bigHeaderGroup.alpha = 1f;
            _bigContentGroup.alpha = 1f;
            var bodyGroup = _bigBody.GetComponent<CanvasGroup>();
            if (bodyGroup != null)
            {
                bodyGroup.alpha = 1f;
            }
        }

        /// <summary>Lays the window out at <paramref name="r"/>: title row, hint row (keyboard only — the pad's keyboard
        /// and the browser prompt bring their own), the history with its scrollbar and marker, and the input row
        /// (keyboard only).</summary>
        private void ApplyBigLayout(ChatRect r)
        {
            _bigRect = r;
            float w = r.W, h = r.H;
            bool keyboard = _mode == ChatWindowMode.Keyboard;
            UiKit.Place(_big.gameObject, r.X, r.Y, w, h);
            foreach (var layer in new[] { _bigBody, _bigFrame.rectTransform, _bigHeader, _bigContent })
            {
                UiKit.Place(layer.gameObject, 0f, 0f, w, h);
            }

            UiKit.Place(_bigTitle.gameObject, BigPad, BigPad, Mathf.Max(0f, w - (2f * BigPad) - CloseSize - 8f), TitleH);
            UiKit.Place(_bigClose.gameObject, w - BigPad - CloseSize, BigPad, CloseSize, CloseSize);
            UiKit.Place(_bigHint.gameObject, BigPad, BigPad + TitleH, Mathf.Max(0f, w - (2f * BigPad)), HintH);
            _bigHint.gameObject.SetActive(keyboard);

            float top = BigPad + TitleH + (keyboard ? HintH : 0f) + HeaderGap;
            float inputH = keyboard ? BigInputH : 0f;
            _bigHistH = Mathf.Max(1f, h - top - inputH - BigPad);
            _bigHistW = Mathf.Max(1f, w - (2f * BigPad) - BarW - BarGap);
            UiKit.Place(_bigViewport.gameObject, BigPad, top, _bigHistW, _bigHistH);
            UiKit.Place(_bigLog.gameObject, 0f, 0f, _bigHistW, _bigHistH);
            UiKit.Place(_bigBar.gameObject, w - BigPad - BarW, top, BarW, _bigHistH);
            UiKit.Place(_bigNewMarker.gameObject, (w - MarkerW) / 2f, top + _bigHistH - MarkerH - 6f, MarkerW, MarkerH);

            _inputRow.gameObject.SetActive(keyboard);
            float boxW = Mathf.Max(1f, w - (2f * BigPad));
            UiKit.Place(_inputRow.gameObject, 0f, h - BigInputH - (BigPad / 2f), w, BigInputH);
            UiKit.Place(_inputBox.gameObject, BigPad, (BigInputH - InputBoxH) / 2f, boxW, InputBoxH);
            UiKit.Place(_inputText.gameObject, 10f, 0f, boxW - 20f, InputBoxH);
            UiKit.Place(_inputPlaceholder.gameObject, 10f, 0f, boxW - 20f, InputBoxH);
        }

        /// <summary>
        /// Renders the slice of the history in view: the newest lines that fit above the input row, or — scrolled up —
        /// the lines <see cref="_bigScroll"/> above them. Mirrors the position on the scrollbar (hidden while everything
        /// fits) and shows the "New messages ↓" marker while lines arrived below a reader. Called on a change only (a line,
        /// a scroll, a re-layout) — never per frame.
        /// </summary>
        private void RefreshBig()
        {
            if (_bigLog == null || !WindowOpen)
            {
                return;
            }

            EnsureHeights(_bigHistW);
            int count = _lines.Count;
            _bigMaxOffset = ChatScrollback.MaxOffset(count, _bigHistH, _heightOf);
            _bigScroll = Mathf.Clamp(_bigScroll, 0, _bigMaxOffset);
            int end = count - _bigScroll;
            int from = ChatScrollback.SliceStart(end, _bigHistH, _heightOf);
            _bigLog.text = ComposeSlice(from, end);

            bool scrollable = _bigMaxOffset > 0;
            if (_bigBar.gameObject.activeSelf != scrollable)
            {
                _bigBar.gameObject.SetActive(scrollable);
            }

            _bigBar.size = ChatScrollback.BarSize(end - from, count);
            _bigBar.SetValueWithoutNotify(ChatScrollback.BarValue(_bigScroll, _bigMaxOffset));

            if (_bigScroll == 0)
            {
                _newBelow = 0;
            }

            bool marker = _bigScroll > 0 && _newBelow > 0;
            if (_bigNewMarker.gameObject.activeSelf != marker)
            {
                _bigNewMarker.gameObject.SetActive(marker);
            }
        }

        private string ComposeSlice(int from, int end)
        {
            _slice.Clear();
            for (int i = from; i < end; i++)
            {
                if (i > from)
                {
                    _slice.Append('\n');
                }

                _slice.Append(_lines[i].Text);
            }

            return _slice.ToString();
        }

        /// <summary>Forgets every measured height when the text width changed (VEGA moved the window, the UI scale).</summary>
        private void EnsureHeights(float width)
        {
            if (Mathf.Abs(width - _heightsWidth) < 0.5f)
            {
                return;
            }

            _heightsWidth = width;
            for (int i = 0; i < _heights.Count; i++)
            {
                _heights[i] = float.NaN;
            }
        }

        /// <summary>One history line's height at the window's text width — measured the first time the slice reaches it
        /// (a wrapped line counts all its rows), then cached.</summary>
        private float HeightOf(int i)
        {
            float h = _heights[i];
            if (float.IsNaN(h))
            {
                h = Mathf.Max(1f, _bigLog.GetPreferredValues(_lines[i].Text, _heightsWidth, 10000f).y);
                _heights[i] = h;
            }

            return h;
        }

        private void OnBarChanged(float value)
        {
            int offset = ChatScrollback.OffsetForBar(value, _bigMaxOffset);
            if (offset != _bigScroll)
            {
                _bigScroll = offset;
                RefreshBig();
            }
        }

        private void JumpToNewest()
        {
            _bigScroll = 0;
            _newBelow = 0;
            RefreshBig();
        }

        private void CloseFromButton()
        {
            if (WindowOpen)
            {
                CloseWindow();
            }
        }

        private void KillWindowTweens()
        {
            UiTween.Kill(_bigGroup);
            UiTween.Kill(_bigHeaderGroup);
            UiTween.Kill(_bigContentGroup);
            var shape = _bigFrame != null ? _bigFrame.GetComponent<UiHolo.Shape>() : null;
            if (shape != null)
            {
                UiTween.Kill(shape);
            }
        }

        /// <summary>
        /// Builds the window once, hidden: body (the reveal's target) → holo frame, header (title, ×, hint) and content
        /// (history viewport with its mask, scrollbar, marker, input row). Sorted above VEGA's canvas, so the layout — not
        /// the draw order — keeps it off her speech panel. The frame takes clicks, so a tap on the window never reaches a
        /// touch button below it.
        /// </summary>
        private void BuildBigWindow(Transform root)
        {
            _heightOf = HeightOf;
            float w = ChatWindowLayout.DefaultW, h = ChatWindowLayout.DefaultH;
            var bigGo = new GameObject("ChatBigWindow", typeof(RectTransform));
            bigGo.transform.SetParent(root, false);
            _big = UiKit.Place(bigGo, ChatWindowLayout.DefaultW / 2f, ChatWindowLayout.DefaultTop, w, h);
            _bigGroup = bigGo.AddComponent<CanvasGroup>();
            _bigBody = AddLayer(_big, "Body", w, h);

            _bigFrame = UiHolo.AddPanel(_bigBody, 0f, 0f, w, h, new Color(0.05f, 0.10f, 0.16f, BigFillAlpha), 14f, 1.5f, 1.2f);
            _bigFrame.gameObject.name = "ChatBigFrame";
            _bigFrame.raycastTarget = true;

            _bigHeader = AddLayer(_bigBody, "Header", w, h);
            _bigHeaderGroup = _bigHeader.gameObject.AddComponent<CanvasGroup>();
            _bigTitle = UiText.Add(_bigHeader, BigPad, BigPad, w, TitleH, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, UiText.Look.Glow);
            _bigHint = UiText.Add(_bigHeader, BigPad, BigPad + TitleH, w, HintH, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);
            UiText.Fit(_bigHint, 10f); // a long language shrinks instead of running out of the window
            _bigClose = BuildCloseButton(_bigHeader);

            _bigContent = AddLayer(_bigBody, "Content", w, h);
            _bigContentGroup = _bigContent.gameObject.AddComponent<CanvasGroup>();
            _bigViewport = AddLayer(_bigContent, "History", w, h);
            _bigViewport.gameObject.AddComponent<RectMask2D>(); // a line measured a pixel short never spills over the title
            _bigLog = UiText.Add(_bigViewport, 0f, 0f, w, h, string.Empty, BigFontSize, UiKit.TextCol, TextAnchor.LowerLeft, FontStyle.Normal, UiText.Look.Outline);
            UiText.Wrap(_bigLog); // newest line at the bottom, right above the input row
            _bigBar = BuildHistoryBar(_bigContent);
            _bigNewMarker = BuildNewMarker(_bigContent);
            BuildInputRow(_bigContent, w);
            bigGo.SetActive(false);
        }

        private static RectTransform AddLayer(Transform parent, string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiKit.Place(go, 0f, 0f, w, h);
        }

        private Button BuildCloseButton(Transform parent)
        {
            var button = UiKit.AddButton(parent, 0f, 0f, CloseSize, CloseSize, "×", CloseFromButton);
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                UiKit.Place(label.gameObject, 0f, 0f, CloseSize, CloseSize);
                label.alignment = TextAnchor.MiddleCenter;
            }

            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        /// <summary>A standalone bottom-to-top scrollbar (the history is no ScrollRect — only its slice is laid out): its
        /// handle shows where the reader is, and a drag scrolls (<see cref="OnBarChanged"/>).</summary>
        private Scrollbar BuildHistoryBar(Transform parent)
        {
            var go = new GameObject("ChatHistoryBar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, 0f, 0f, BarW, 100f);
            var track = go.AddComponent<Image>();
            track.color = new Color(0.27f, 0.55f, 0.72f, 0.16f);
            var bar = go.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.SetParent(go.transform, false);
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.sizeDelta = Vector2.zero;
            handleRt.anchoredPosition = Vector2.zero;
            var handle = handleGo.AddComponent<Image>();
            handle.color = new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.6f);

            bar.handleRect = handleRt;
            bar.targetGraphic = handle;
            bar.navigation = new Navigation { mode = Navigation.Mode.None }; // a drag target, never a pad stop (#1411)
            bar.onValueChanged.AddListener(OnBarChanged);
            go.SetActive(false);
            return bar;
        }

        /// <summary>The "New messages ↓" pill at the bottom of the history: shown while lines arrived below a reader who
        /// scrolled up; a click jumps to the newest line.</summary>
        private RectTransform BuildNewMarker(Transform parent)
        {
            var pill = UiHolo.AddPanel(parent, 0f, 0f, MarkerW, MarkerH, new Color(0.05f, 0.18f, 0.26f, 0.94f), 10f, 1.2f, 1.4f);
            pill.gameObject.name = "ChatNewMessages";
            pill.raycastTarget = true;
            var button = pill.gameObject.AddComponent<Button>();
            button.targetGraphic = pill;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(UiSound.Click);
            button.onClick.AddListener(JumpToNewest);
            _bigNewLabel = UiText.Add(pill.transform, 0f, 0f, MarkerW, MarkerH, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Outline);
            UiText.Fit(_bigNewLabel, 10f);
            pill.gameObject.SetActive(false);
            return pill.rectTransform;
        }

        /// <summary>The input row (keyboard mode): a darker holo well with the hand-built field. It takes the focus guard
        /// itself — it is not a UiKit.AddInput field, and the chat canvas is the one that hides in the flight view (#1805).</summary>
        private void BuildInputRow(Transform parent, float w)
        {
            var rowGo = new GameObject("ChatInputRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            _inputRow = UiKit.Place(rowGo, 0f, 0f, w, BigInputH);
            var inputGo = new GameObject("ChatInput", typeof(RectTransform));
            inputGo.transform.SetParent(_inputRow, false);
            _inputBox = UiKit.Place(inputGo, BigPad, (BigInputH - InputBoxH) / 2f, w - (2f * BigPad), InputBoxH);
            var img = inputGo.AddComponent<Image>();
            var well = UiHolo.Apply(img, UiHolo.Style.Panel, 6f, 1f, 0.5f);
            if (well != null)
            {
                img.color = new Color(0.02f, 0.05f, 0.10f, 1f);
                well.FillOpacity = 0.9f;
            }
            else
            {
                img.color = new Color(0.04f, 0.09f, 0.18f, 0.95f); // bitmap fallback: the old flat field
            }

            _input = inputGo.AddComponent<InputField>();
            _inputText = UiKit.AddText(inputGo.transform, 10f, 0f, w - (2f * BigPad) - 20f, InputBoxH, string.Empty, BigFontSize, UiKit.TextCol, TextAnchor.MiddleLeft);
            _inputText.supportRichText = false;
            _inputPlaceholder = UiKit.AddText(inputGo.transform, 10f, 0f, w - (2f * BigPad) - 20f, InputBoxH, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            _input.textComponent = _inputText;
            _input.placeholder = _inputPlaceholder;

            // Room for a pasted build share code — the server refuses over-long codes with a clear hint
            // instead of truncating, and Submit() trims ordinary prose to the 200-char chat cap (#1154).
            _input.characterLimit = 4096;
            _input.lineType = InputField.LineType.SingleLine;
            _input.onEndEdit.AddListener(OnEndEdit);
            inputGo.AddComponent<InputFocusGuard>().Init(_input);
        }
    }
}
