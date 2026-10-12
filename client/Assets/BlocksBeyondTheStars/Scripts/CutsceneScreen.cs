// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Who speaks in a speaker box: the pack's narrator (or a caption's own speaker id) resolved for display.</summary>
    public sealed class CutsceneSpeaker
    {
        public string Name = string.Empty;
        public Sprite Icon;
        public Color Color = UiKit.Cyan;
        public string LineStartCue = string.Empty;
    }

    /// <summary>
    /// The cutscene director's screen (#2468): one <see cref="CinematicFrame"/> (letterbox, subtitle, skip hint and button,
    /// fade and flash covers) plus the caption styles of synthesis §6.3 hung on its canvas — the top banner (T1), the
    /// title card, the speaker box with the narrator's icon, name and colour from the pack, the framed reading panel, the
    /// credits roll, the "Continue" button for page-gated captions and the galaxy chart of a <c>chart</c> shot. Pure
    /// presentation; the director decides what shows. Text is legacy uGUI on the frame's 1920×1080 canvas (Rajdhani with
    /// the Noto fallbacks), the fonts follow the player's UI scale like the frame's own caption (#2461). #2497: plus a world
    /// dim on a tiny canvas of its own under the HUD (<see cref="WorldDimSortingOrder"/>) — a card dims only the world.
    /// </summary>
    public sealed class CutsceneScreen
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float BarH = 130f;

        private const float BannerW = 900f, BannerH = 92f, BannerY = 112f;
        private const float SpeakerW = 1180f, SpeakerH = 206f;
        private const float ReaderW = 1100f, ReaderH = 620f;
        // #2474: the chart fits between the top letterbox bar and the speaker box (a chart shot usually has VEGA speaking).
        private const float ChartTop = BarH + 4f;
        private const float ChartSize = RefH - BarH - 26f - SpeakerH - 8f - ChartTop;
        private const float ContinueW = 320f, ContinueH = 46f;

        /// <summary>#2497: the world dim's sorting order — above the world labels (<c>ScreenLabelLayer</c>, 8), below the HUD (10),
        /// VEGA's panel (11), the flight HUD (12), the chat (25) and the touch layer (100).</summary>
        public const int WorldDimSortingOrder = 9;

        private readonly CinematicFrame _frame;
        private readonly Transform _root;
        private readonly string _name;
        private Canvas _worldDimCanvas; // #2497: built on the first dim (a banner or a staged scene never needs one)
        private Image _worldDim;
        private bool _destroyed;

        private readonly Image _banner;
        private readonly Image _bannerIcon;
        private readonly Text _bannerKicker;
        private readonly Text _bannerLine;

        private readonly Text _titleKicker;
        private readonly Text _title;

        private readonly Image _speaker;
        private readonly Image _speakerIcon;
        private readonly Text _speakerName;
        private readonly Text _speakerText;

        private readonly Image _reader;
        private readonly Text _readerHeading;
        private readonly Text _readerBody;

        private readonly Text _credits;

        private readonly Image _continue;
        private readonly Text _continueLabel;
        private bool _continueClicked;

        private GalaxyChartWidget _chart;
        private float _scale = -1f;

        private CutsceneScreen(CinematicFrame frame, string name)
        {
            _frame = frame;
            _root = frame.Root;
            _name = name ?? "Cutscene";

            _banner = UiKit.AddPanel(_root, (RefW - BannerW) * 0.5f, BannerY, BannerW, BannerH, new Color(0.04f, 0.09f, 0.15f, 0.9f));
            _bannerIcon = UiKit.AddImage(_banner.transform, 18f, 14f, 64f, 64f, null, Color.white);
            _bannerKicker = Wrapped(UiKit.AddText(_banner.transform, 96f, 8f, BannerW - 116f, 30f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold));
            _bannerLine = Wrapped(UiKit.AddText(_banner.transform, 96f, 36f, BannerW - 116f, 50f, string.Empty, 24, UiKit.TextCol, TextAnchor.UpperLeft));

            _titleKicker = Wrapped(UiKit.AddText(_root, 160f, 360f, RefW - 320f, 50f, string.Empty, 30, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold));
            _title = Wrapped(UiKit.AddText(_root, 160f, 412f, RefW - 320f, 140f, string.Empty, 62, new Color(0.94f, 0.97f, 1f), TextAnchor.UpperCenter, FontStyle.Bold));
            UiKit.AddOutline(_titleKicker);
            UiKit.AddOutline(_title);

            float speakerY = RefH - BarH - 26f - SpeakerH;
            _speaker = UiKit.AddPanel(_root, (RefW - SpeakerW) * 0.5f, speakerY, SpeakerW, SpeakerH, new Color(0.04f, 0.09f, 0.15f, 0.88f));
            _speakerIcon = UiKit.AddImage(_speaker.transform, 24f, 24f, 96f, 96f, null, Color.white);
            _speakerName = Wrapped(UiKit.AddText(_speaker.transform, 140f, 12f, SpeakerW - 170f, 34f, string.Empty, 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold));
            _speakerText = Wrapped(UiKit.AddText(_speaker.transform, 140f, 50f, SpeakerW - 170f, SpeakerH - 62f, string.Empty, 27, UiKit.TextCol, TextAnchor.UpperLeft));

            _reader = UiKit.AddPanel(_root, (RefW - ReaderW) * 0.5f, (RefH - ReaderH) * 0.5f, ReaderW, ReaderH, new Color(0.03f, 0.07f, 0.12f, 0.94f));
            _readerHeading = Wrapped(UiKit.AddText(_reader.transform, 40f, 26f, ReaderW - 80f, 44f, string.Empty, 28, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold));
            _readerBody = Wrapped(UiKit.AddText(_reader.transform, 40f, 84f, ReaderW - 80f, ReaderH - 110f, string.Empty, 26, UiKit.TextCol, TextAnchor.UpperLeft));

            _credits = Wrapped(UiKit.AddText(_root, (RefW - 1000f) * 0.5f, 0f, 1000f, 400f, string.Empty, 26, new Color(0.85f, 0.92f, 1f), TextAnchor.UpperCenter));

            // A tap or click on the speaker box or the reading frame turns the page too (d4 §2.3).
            MakeContinueTarget(_speaker);
            MakeContinueTarget(_reader);

            // The continue control for page-gated captions: the VEGA continue key or pad button, and a click/tap here — the
            // way on for touch players, whose touch layer (with its NEXT button) steps aside under a camera-taking scene.
            // Bottom centre: clear of the touch layer's stick (left), its action buttons (right) and the hotbar arrows
            // above the hotbar; the frame's skip button sits bottom right.
            var go = new GameObject("CutsceneContinue", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            UiKit.Place(go, (RefW - ContinueW) * 0.5f, RefH - 26f - ContinueH, ContinueW, ContinueH);
            _continue = go.AddComponent<Image>();
            _continue.sprite = UiKit.ButtonSprite;
            _continue.type = Image.Type.Sliced;
            _continue.color = new Color(0.08f, 0.26f, 0.38f, 0.92f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = _continue;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // the pad continues with its own button
            button.onClick.AddListener(() =>
            {
                _continueClicked = true;
                UiSound.Click();
            });
            _continueLabel = UiKit.AddText(go.transform, 0f, 0f, ContinueW, ContinueH, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabel(_continueLabel, 11, 20);

            HideAll();
            ShowContinue(string.Empty, false);
        }

        /// <summary>Builds the screen on its own canvas at <paramref name="sortingOrder"/> (67: above the prologue (65) and the
        /// shell intro (66) frames, below the ending (68), the warp (70) and the loading veil (75)).</summary>
        public static CutsceneScreen Create(string name, int sortingOrder, bool skipButton)
        {
            var frame = CinematicFrame.Create(name, sortingOrder);
            if (skipButton)
            {
                frame.EnableSkipButton();
            }

            return new CutsceneScreen(frame, name);
        }

        public CinematicFrame Frame => _frame;

        /// <summary>Whether the screen is drawn (the frame's canvas and the world dim are root-level objects of their own).</summary>
        public bool Visible
        {
            get => _root != null && _root.gameObject.activeSelf;
            set
            {
                if (_root != null && _root.gameObject.activeSelf != value)
                {
                    _root.gameObject.SetActive(value);
                }

                if (_worldDimCanvas != null && _worldDimCanvas.gameObject.activeSelf != value)
                {
                    _worldDimCanvas.gameObject.SetActive(value);
                }
            }
        }

        public void SetLetterbox(float amount01) => _frame.SetLetterbox(amount01);

        /// <summary>The frame's own black cover — over everything (a black-out, a reading frame, the credits).</summary>
        public void SetFade(float alpha) => _frame.SetFade(alpha);

        /// <summary>#2497: the world dim under the HUD (a card or an overlay).</summary>
        public void SetWorldDim(float alpha)
        {
            float a = Mathf.Clamp01(alpha);
            if (_worldDim == null)
            {
                if (a <= 0.001f || _destroyed)
                {
                    return;
                }

                BuildWorldDim();
            }

            if (!Mathf.Approximately(_worldDim.color.a, a))
            {
                _worldDim.color = new Color(0f, 0f, 0f, a);
            }

            bool on = a > 0.001f; // no full-screen quad drawn for nothing
            if (_worldDim.enabled != on)
            {
                _worldDim.enabled = on;
            }
        }

        /// <summary>#2497: a card dims only the world — a full-screen shade on a tiny canvas of its own under the HUD
        /// (<see cref="WorldDimSortingOrder"/>), so the HUD, the crosshair and VEGA's objective chip stay bright while the player
        /// keeps playing under the card. A HUD canvas (diegetic), so with the visor HUD it is drawn into the visor's layer under
        /// the HUD, not over it; the frame's own cover stays for black-outs, reading frames and the credits.</summary>
        private void BuildWorldDim()
        {
            _worldDimCanvas = UiKit.CreateDiegeticCanvas(_name + "WorldDim", UiKit.HudRefW, UiKit.HudRefH);
            _worldDimCanvas.sortingOrder = WorldDimSortingOrder;
            var dimGo = new GameObject("WorldDim", typeof(RectTransform));
            dimGo.transform.SetParent(_worldDimCanvas.transform, false);
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
            _worldDim = dimGo.AddComponent<Image>();
            _worldDim.sprite = UiKit.SolidSprite;
            _worldDim.color = new Color(0f, 0f, 0f, 0f);
            _worldDim.raycastTarget = false;
            if (_root != null && !_root.gameObject.activeSelf)
            {
                _worldDimCanvas.gameObject.SetActive(false); // the screen hides (a card under the menu): so does its dim
            }
        }

        public void SetFlash(float alpha) => _frame.SetFlash(alpha);

        public void SetHint(string text, float alpha) => _frame.SetHint(text, alpha);

        public bool ConsumeSkipClick() => _frame.ConsumeSkipClick();

        /// <summary>#2471: a touch tap the touch layer's look pad caught first (it lies over this frame while a card plays with
        /// the touch controls up): the skip button skips, the continue button, the speaker box and the reading frame turn the
        /// page. Returns whether the tap hit one of them.</summary>
        public bool TapAt(Vector2 screenPos)
        {
            if (!Visible)
            {
                return false;
            }

            if (_frame.TapSkip(screenPos))
            {
                return true;
            }

            if (Hit(_continue, screenPos) || Hit(_speaker, screenPos) || Hit(_reader, screenPos))
            {
                _continueClicked = true;
                UiSound.Click();
                return true;
            }

            return false;
        }

        private static bool Hit(Image target, Vector2 screenPos)
            => target != null && target.raycastTarget && target.gameObject.activeInHierarchy
               && RectTransformUtility.RectangleContainsScreenPoint(target.rectTransform, screenPos, null); // overlay canvas: no camera

        /// <summary>True once per click/tap on the continue button since the last call.</summary>
        public bool ConsumeContinueClick()
        {
            bool clicked = _continueClicked;
            _continueClicked = false;
            return clicked;
        }

        /// <summary>Shows or hides the continue button (never an invisible tap target).</summary>
        public void ShowContinue(string label, bool visible)
        {
            if (_continue.gameObject.activeSelf != visible)
            {
                _continue.gameObject.SetActive(visible);
            }

            if (visible)
            {
                _continueLabel.text = label ?? string.Empty;
            }
        }

        /// <summary>Draws the captions on screen now. <paramref name="banner"/>: the scene is a T1 banner — every caption
        /// shows in the top band. <paramref name="shotTime"/> drives the credits roll.</summary>
        public void Render(IReadOnlyList<CaptionView> views, CutsceneSpeaker speaker, bool banner, float shotTime)
        {
            ApplyScale();
            HideAll();
            bool subtitleShown = false;
            foreach (var v in views)
            {
                var c = v.Caption;
                float a = Mathf.Clamp01(v.Alpha);
                if (banner)
                {
                    ShowBanner(c.Kicker, v.Shown, speaker, a);
                    continue;
                }

                switch (c.Style)
                {
                    case CutsceneCaptionStyles.TitleCard:
                        SetText(_titleKicker, c.Kicker, a);
                        SetText(_title, v.Shown, a);
                        break;
                    case CutsceneCaptionStyles.Speaker:
                        ShowSpeaker(v.Shown, speaker, a);
                        break;
                    case CutsceneCaptionStyles.ReadingFrame:
                        _reader.gameObject.SetActive(true);
                        SetAlpha(_reader, a * 0.94f);
                        SetText(_readerHeading, c.Kicker, a);
                        SetText(_readerBody, v.Shown, a);
                        break;
                    case CutsceneCaptionStyles.Credits:
                        ShowCredits(v.Shown, a, c.End > c.At ? Mathf.Clamp01((shotTime - c.At) / (c.End - c.At)) : 0f);
                        break;
                    default:
                        if (!subtitleShown)
                        {
                            _frame.SetCaption(v.Shown, a);
                            subtitleShown = true;
                        }

                        break;
                }
            }
        }

        /// <summary>Shows the galaxy chart (a <c>chart</c> shot) — built once per shot from the caller's stars. #2474: it sits
        /// between the letterbox bars and above the speaker box (which draws over it), under every caption;
        /// <paramref name="selectedId"/> rings a star, <paramref name="routeFrom"/>/<paramref name="routeTo"/> draw the violet
        /// route of the Guardian reveal, and <paramref name="memory"/> tints the whole chart sepia — VEGA's memory of the stars
        /// (the scene's memory grade only reaches the 3D picture, not this overlay).</summary>
        public void ShowChart(IReadOnlyList<GalaxyChartWidget.Star> stars, IReadOnlyList<(string A, string B)> lanes,
            string selectedId = null, string routeFrom = null, string routeTo = null, bool memory = false)
        {
            HideChart();
            _chart = GalaxyChartWidget.Create(_root, (RefW - ChartSize) * 0.5f, ChartTop, ChartSize);
            _chart.transform.SetSiblingIndex(_banner.transform.GetSiblingIndex()); // under the captions, the speaker box on top
            _chart.Show(stars, lanes, selectedId, null, routeFrom, routeTo);
            if (memory)
            {
                foreach (var g in _chart.GetComponentsInChildren<Graphic>(true))
                {
                    g.color = Sepia(g.color);
                }
            }
        }

        /// <summary>A colour as VEGA remembers it: its brightness in warm sepia, alpha kept.</summary>
        internal static Color Sepia(Color c)
        {
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return new Color(Mathf.Clamp01(lum * 1.07f + 0.05f), Mathf.Clamp01(lum * 0.88f + 0.03f), Mathf.Clamp01(lum * 0.62f), c.a);
        }

        public void HideChart()
        {
            if (_chart != null)
            {
                Object.Destroy(_chart.gameObject);
                _chart = null;
            }
        }

        /// <summary>Tears the screen down (the frame's canvas and the world dim go with it).</summary>
        public void Destroy()
        {
            _destroyed = true;
            HideChart();
            if (_frame != null)
            {
                Object.Destroy(_frame.gameObject);
            }

            if (_worldDimCanvas != null)
            {
                Object.Destroy(_worldDimCanvas.gameObject);
            }
        }

        private void ShowBanner(string kicker, string line, CutsceneSpeaker speaker, float a)
        {
            _banner.gameObject.SetActive(true);
            SetAlpha(_banner, a * 0.9f);
            bool icon = speaker?.Icon != null;
            _bannerIcon.gameObject.SetActive(icon);
            if (icon)
            {
                _bannerIcon.sprite = speaker.Icon;
                _bannerIcon.color = new Color(speaker.Color.r, speaker.Color.g, speaker.Color.b, a);
            }

            float x = icon ? 96f : 24f;
            _bannerKicker.rectTransform.anchoredPosition = new Vector2(x, -8f);
            _bannerLine.rectTransform.anchoredPosition = new Vector2(x, string.IsNullOrEmpty(kicker) ? -22f : -36f);
            SetText(_bannerKicker, kicker, a);
            SetText(_bannerLine, line, a);
        }

        private void ShowSpeaker(string text, CutsceneSpeaker speaker, float a)
        {
            _speaker.gameObject.SetActive(true);
            SetAlpha(_speaker, a * 0.88f);
            bool icon = speaker?.Icon != null;
            _speakerIcon.gameObject.SetActive(icon);
            float x = icon ? 140f : 28f;
            if (icon)
            {
                _speakerIcon.sprite = speaker.Icon;
                _speakerIcon.color = new Color(speaker.Color.r, speaker.Color.g, speaker.Color.b, a);
            }

            _speakerName.rectTransform.anchoredPosition = new Vector2(x, -12f);
            _speakerText.rectTransform.anchoredPosition = new Vector2(x, -50f);
            var nameColor = speaker != null ? speaker.Color : UiKit.Cyan;
            _speakerName.color = new Color(nameColor.r, nameColor.g, nameColor.b, a);
            _speakerName.text = speaker?.Name ?? string.Empty;
            SetText(_speakerText, text, a);
        }

        private void ShowCredits(string text, float a, float progress)
        {
            if (!string.Equals(_credits.text, text, System.StringComparison.Ordinal))
            {
                _credits.text = text ?? string.Empty;
                float h = Mathf.Max(400f, _credits.preferredHeight);
                _credits.rectTransform.sizeDelta = new Vector2(1000f, h);
            }

            float height = _credits.rectTransform.sizeDelta.y;
            float travel = height + RefH; // from below the frame to fully above it
            _credits.rectTransform.anchoredPosition = new Vector2((RefW - 1000f) * 0.5f, -(RefH - travel * progress));
            SetAlpha(_credits, a);
        }

        private void HideAll()
        {
            _banner.gameObject.SetActive(false);
            SetText(_titleKicker, string.Empty, 0f);
            SetText(_title, string.Empty, 0f);
            _speaker.gameObject.SetActive(false);
            _reader.gameObject.SetActive(false);
            SetAlpha(_credits, 0f);
            _frame.SetCaption(string.Empty, 0f);
        }

        /// <summary>The fonts follow the player's UI scale (#2461 for the frame's own caption); the boxes keep their
        /// absolute place, the texts wrap inside them and may grow past their bottom edge rather than be cut.</summary>
        private void ApplyScale()
        {
            float s = UiKit.UserScale;
            if (Mathf.Approximately(s, _scale))
            {
                return;
            }

            _scale = s;
            Scale(_bannerKicker, 18, s);
            Scale(_bannerLine, 24, s);
            Scale(_titleKicker, 30, s);
            Scale(_title, 62, s);
            Scale(_speakerName, 24, s);
            Scale(_speakerText, 27, s);
            Scale(_readerHeading, 28, s);
            Scale(_readerBody, 26, s);
            Scale(_credits, 26, s);
        }

        private static void Scale(Text t, int size, float s) => t.fontSize = Mathf.RoundToInt(size * s);

        /// <summary>Makes a caption panel a continue target: a click or tap on it counts like the continue button. It only
        /// catches pointers while it is shown (a hidden panel is inactive).</summary>
        private void MakeContinueTarget(Image panel)
        {
            panel.raycastTarget = true;
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => _continueClicked = true);
        }

        private static Text Wrapped(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void SetText(Text t, string text, float alpha)
        {
            t.text = text ?? string.Empty;
            SetAlpha(t, alpha);
        }

        private static void SetAlpha(Graphic g, float alpha)
        {
            var c = g.color;
            c.a = Mathf.Clamp01(alpha);
            g.color = c;
        }
    }
}
