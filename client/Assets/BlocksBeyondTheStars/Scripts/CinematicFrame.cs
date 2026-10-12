// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Reusable full-screen cinematic chrome (#759/#760): animated letterbox bars, a subtitle-style
    /// caption, a small skip hint, and black/white cover planes for fades and flashes — all on its own
    /// code-built uGUI canvas, driven by whoever owns the cinematic (the shell intro, the staged VEGA
    /// prologue, the ending). Pure presentation: it holds no timing. The one input it offers is opt-in: a
    /// skip button behind the hint (<see cref="EnableSkipButton"/>, #2461) for mouse and touch players.
    ///
    /// Accessibility (#2461): the white flash follows the Reduce-flashes setting (<see cref="FxKit.FlashScale"/>),
    /// the caption and the hint follow the player's UI scale, and the hint draws ABOVE the black cover, so it stays
    /// readable on the ending's darkened stage.
    /// </summary>
    public sealed class CinematicFrame : MonoBehaviour
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float BarH = 130f; // full letterbox bar height (≈12 % of the reference height)

        private const int CaptionFontSize = 34;
        private const int HintFontSize = 18;
        private const float HintRight = 36f, HintBottom = 26f, HintH = 30f;
        private const float PlatePadX = 22f, PlatePadY = 12f; // the skip button's margin around its label

        private Canvas _canvas;
        private RectTransform _barTop, _barBottom;
        private Text _caption;
        private Text _hint;
        private Image _fade;  // black cover (fade from/to black)
        private Image _flash; // white cover (reveal flash), above everything
        private Image _skipPlate; // the clickable plate behind the hint (null until EnableSkipButton)
        private bool _skipClicked;
        private float _appliedScale = -1f;

        /// <summary>Creates the frame on its own canvas at the given sorting order (65 sits above the
        /// in-game HUD/VEGA panel but below the world-loading veil at 75; the shell intro uses 66).</summary>
        public static CinematicFrame Create(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var frame = go.AddComponent<CinematicFrame>();
            frame.Build(name, sortingOrder);
            return frame;
        }

        /// <summary>The frame's canvas root, for cinematics that need their own child elements beyond the
        /// caption/hint (#1124: the resolution cinematic hangs its credits roll here). Children share the
        /// canvas' sorting order and die with the frame.</summary>
        public Transform Root => _canvas != null ? _canvas.transform : transform;

        /// <summary>The skip hint for the control in hand (#2461): "[Esc]" on the keyboard, the pad's cancel button
        /// (B by default, follows the glyph set and the binding), and the on-screen button on touch. Every cinematic
        /// shows this instead of the old fixed "Skip · [Esc]", which named a key no pad or tablet has.</summary>
        public static string SkipLabel(System.Func<string, string> localize)
        {
            if (localize == null)
            {
                return string.Empty;
            }

            return InputMap.ActiveDevice switch
            {
                InputDeviceKind.Touch => localize("ui.cutscene.skip_touch"),
                InputDeviceKind.Gamepad => SafeFormat(localize("ui.cutscene.skip_pad"), InputMap.Glyph(InputAction.UiCancel)),
                _ => SafeFormat(localize("ui.cutscene.skip_key"), "Esc"), // UiCancel's keyboard key is locked to Escape
            };
        }

        /// <summary>The skip control (#2461): Esc on the keyboard and the pad's cancel button — the same
        /// <see cref="InputAction.UiCancel"/> that backs out of every screen. Touch players get the button instead.</summary>
        public static bool SkipPressed() => InputMap.Down(InputAction.UiCancel);

        private static string SafeFormat(string template, string arg)
        {
            try
            {
                return string.Format(template ?? string.Empty, arg);
            }
            catch (System.FormatException)
            {
                return template ?? string.Empty; // a stray brace in a translation must never blank the hint
            }
        }

        private void Build(string name, int sortingOrder)
        {
            _canvas = UiKit.CreateCanvas(name + "Canvas", (int)RefW, (int)RefH);
            _canvas.sortingOrder = sortingOrder;
            var root = _canvas.transform;

            _barTop = Bar(root, top: true);
            _barBottom = Bar(root, top: false);

            // The black cover sits UNDER the text (#2461): the ending darkens its stage to 0.88 black, and a hint drawn
            // below that cover was all but invisible. Text over black is the cinematic convention anyway.
            _fade = Cover(root, "Fade", Color.black);

            // Caption: subtitle-style, centered above the lower bar's resting edge.
            var capGo = new GameObject("Caption", typeof(RectTransform));
            capGo.transform.SetParent(root, false);
            var capRt = capGo.GetComponent<RectTransform>();
            capRt.anchorMin = capRt.anchorMax = capRt.pivot = new Vector2(0.5f, 0f);
            capRt.anchoredPosition = new Vector2(0f, BarH + 64f);
            capRt.sizeDelta = new Vector2(1400f, 120f);
            _caption = capGo.AddComponent<Text>();
            _caption.font = UiKit.Font;
            _caption.fontSize = CaptionFontSize;
            _caption.color = new Color(0.9f, 0.96f, 1f, 0f);
            _caption.alignment = TextAnchor.MiddleCenter;
            _caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            _caption.verticalOverflow = VerticalWrapMode.Overflow;
            _caption.raycastTarget = false;
            UiKit.AddOutline(_caption);

            // Skip hint: small, bottom-right inside the lower bar's area.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root, false);
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(1f, 0f);
            hintRt.anchoredPosition = new Vector2(-HintRight, HintBottom);
            hintRt.sizeDelta = new Vector2(500f, HintH);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = UiKit.Font;
            _hint.fontSize = HintFontSize;
            _hint.color = new Color(0.7f, 0.8f, 0.9f, 0f);
            _hint.alignment = TextAnchor.MiddleRight;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;

            _flash = Cover(root, "Flash", Color.white);

            SetLetterbox(0f);
            ApplyUserScale();
        }

        /// <summary>Turns the hint into a button (#2461): a click or a tap on it asks to skip — the way out for touch
        /// players (no Esc, no pad) and for a free mouse cursor. The owner reads the request once per frame with
        /// <see cref="ConsumeSkipClick"/>. The plate only catches pointers while the hint is visible.</summary>
        public void EnableSkipButton()
        {
            if (_skipPlate != null || _hint == null)
            {
                return;
            }

            var plateGo = new GameObject("SkipButton", typeof(RectTransform));
            plateGo.transform.SetParent(_canvas.transform, false);
            plateGo.transform.SetSiblingIndex(_hint.transform.GetSiblingIndex()); // right behind its label
            var rt = plateGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            _skipPlate = plateGo.AddComponent<Image>();
            _skipPlate.sprite = UiKit.SolidSprite;
            _skipPlate.color = new Color(0.05f, 0.10f, 0.16f, 0f);
            _skipPlate.raycastTarget = false;
            var button = plateGo.AddComponent<Button>();
            button.targetGraphic = _skipPlate;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // no pad stop: the pad skips with its button
            button.onClick.AddListener(() =>
            {
                _skipClicked = true;
                UiSound.Click();
            });
            LayoutSkipPlate();
        }

        /// <summary>A touch tap the touch layer caught first (#2471): its full-screen look pad (canvas order 100) lies over every
        /// cinematic frame, so under a card — where the touch controls stay up — a tap meant for the skip button lands there and
        /// is handed on (<see cref="CutsceneDirector.TapAt"/>). Counts as a click when it hits the visible skip button.</summary>
        public bool TapSkip(Vector2 screenPos)
        {
            if (_skipPlate == null || !_skipPlate.raycastTarget || _canvas == null || !_canvas.gameObject.activeInHierarchy
                || !RectTransformUtility.RectangleContainsScreenPoint(_skipPlate.rectTransform, screenPos, null))
            {
                return false; // overlay canvas: no camera
            }

            _skipClicked = true;
            UiSound.Click();
            return true;
        }

        /// <summary>True once per click/tap on the skip button since the last call.</summary>
        public bool ConsumeSkipClick()
        {
            bool clicked = _skipClicked;
            _skipClicked = false;
            return clicked;
        }

        /// <summary>0 = bars parked off-screen, 1 = fully extended letterbox.</summary>
        public void SetLetterbox(float amount01)
        {
            float h = Mathf.Clamp01(amount01) * BarH;
            if (_barTop != null)
            {
                _barTop.sizeDelta = new Vector2(0f, h);
            }

            if (_barBottom != null)
            {
                _barBottom.sizeDelta = new Vector2(0f, h);
            }
        }

        public void SetCaption(string text, float alpha)
        {
            if (_caption == null)
            {
                return;
            }

            ApplyUserScale();
            _caption.text = text ?? string.Empty;
            var c = _caption.color;
            c.a = Mathf.Clamp01(alpha);
            _caption.color = c;
        }

        public void SetHint(string text, float alpha)
        {
            if (_hint == null)
            {
                return;
            }

            ApplyUserScale();
            string value = text ?? string.Empty;
            bool changed = !string.Equals(_hint.text, value, System.StringComparison.Ordinal);
            _hint.text = value;
            var c = _hint.color;
            c.a = Mathf.Clamp01(alpha);
            _hint.color = c;

            if (_skipPlate != null)
            {
                if (changed)
                {
                    LayoutSkipPlate();
                }

                var p = _skipPlate.color;
                p.a = Mathf.Clamp01(alpha) * 0.85f;
                _skipPlate.color = p;
                _skipPlate.raycastTarget = alpha > 0.05f && value.Length > 0; // never an invisible tap target
            }
        }

        /// <summary>Black cover alpha (1 = fully black).</summary>
        public void SetFade(float alpha) => SetCover(_fade, alpha);

        /// <summary>White cover alpha (reveal flash), drawn above everything. Scaled by the Reduce-flashes setting
        /// (<see cref="FxKit.FlashScale"/>, #2461) like the warp and the wormhole flashes — the intro's hand-off and the
        /// prologue's boot glitch used to ignore it.</summary>
        public void SetFlash(float alpha) => SetCover(_flash, alpha * FxKit.FlashScale);

        /// <summary>The caption and the hint follow the player's UI scale (#2461) — 34 pt subtitles on a 1080p
        /// reference read small on a phone or in a small browser window. Only the fonts (and the skip button around
        /// the hint) scale; the canvas does not, so the letterbox and whatever an owner hangs on <see cref="Root"/>
        /// keep their absolute layout.</summary>
        private void ApplyUserScale()
        {
            float scale = UiKit.UserScale;
            if (Mathf.Approximately(scale, _appliedScale))
            {
                return;
            }

            _appliedScale = scale;
            if (_caption != null)
            {
                _caption.fontSize = Mathf.RoundToInt(CaptionFontSize * scale);
                _caption.rectTransform.sizeDelta = new Vector2(1400f, 120f * scale);
            }

            if (_hint != null)
            {
                _hint.fontSize = Mathf.RoundToInt(HintFontSize * scale);
                _hint.rectTransform.sizeDelta = new Vector2(500f * scale, HintH * scale);
            }

            LayoutSkipPlate();
        }

        /// <summary>Fits the skip button around its label: right-aligned with the hint, padded for a thumb.</summary>
        private void LayoutSkipPlate()
        {
            if (_skipPlate == null || _hint == null)
            {
                return;
            }

            float scale = _appliedScale > 0f ? _appliedScale : 1f;
            float w = Mathf.Max(120f * scale, _hint.preferredWidth + 2f * PlatePadX);
            float h = HintH * scale + 2f * PlatePadY;
            var rt = _skipPlate.rectTransform;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-HintRight + PlatePadX, HintBottom - PlatePadY);
        }

        private static void SetCover(Image img, float alpha)
        {
            if (img == null)
            {
                return;
            }

            var c = img.color;
            c.a = Mathf.Clamp01(alpha);
            img.color = c;
        }

        private static RectTransform Bar(Transform root, bool top)
        {
            var go = new GameObject(top ? "BarTop" : "BarBottom", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = top ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            rt.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            rt.pivot = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = Color.black;
            img.raycastTarget = false;
            return rt;
        }

        private static Image Cover(Transform root, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = new Color(color.r, color.g, color.b, 0f);
            img.raycastTarget = false;
            return img;
        }

        private void OnDestroy()
        {
            // The canvas is a root-level sibling (CreateCanvas) — take it down with the frame.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
