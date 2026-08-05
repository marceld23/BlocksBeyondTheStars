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
    /// prologue). Pure presentation: it holds no timing and captures no input of its own.
    /// </summary>
    public sealed class CinematicFrame : MonoBehaviour
    {
        private const float RefW = 1920f, RefH = 1080f;
        private const float BarH = 130f; // full letterbox bar height (≈12 % of the reference height)

        private Canvas _canvas;
        private RectTransform _barTop, _barBottom;
        private Text _caption;
        private Text _hint;
        private Image _fade;  // black cover (fade from/to black)
        private Image _flash; // white cover (reveal flash), above the fade

        /// <summary>Creates the frame on its own canvas at the given sorting order (65 sits above the
        /// in-game HUD/VEGA panel but below the world-loading veil at 75; the shell intro uses 66).</summary>
        public static CinematicFrame Create(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var frame = go.AddComponent<CinematicFrame>();
            frame.Build(name, sortingOrder);
            return frame;
        }

        private void Build(string name, int sortingOrder)
        {
            _canvas = UiKit.CreateCanvas(name + "Canvas", (int)RefW, (int)RefH);
            _canvas.sortingOrder = sortingOrder;
            var root = _canvas.transform;

            _barTop = Bar(root, top: true);
            _barBottom = Bar(root, top: false);

            // Caption: subtitle-style, centered above the lower bar's resting edge.
            var capGo = new GameObject("Caption", typeof(RectTransform));
            capGo.transform.SetParent(root, false);
            var capRt = capGo.GetComponent<RectTransform>();
            capRt.anchorMin = capRt.anchorMax = capRt.pivot = new Vector2(0.5f, 0f);
            capRt.anchoredPosition = new Vector2(0f, BarH + 64f);
            capRt.sizeDelta = new Vector2(1400f, 120f);
            _caption = capGo.AddComponent<Text>();
            _caption.font = UiKit.Font;
            _caption.fontSize = 34;
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
            hintRt.anchoredPosition = new Vector2(-36f, 26f);
            hintRt.sizeDelta = new Vector2(500f, 30f);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = UiKit.Font;
            _hint.fontSize = 18;
            _hint.color = new Color(0.7f, 0.8f, 0.9f, 0f);
            _hint.alignment = TextAnchor.MiddleRight;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;

            _fade = Cover(root, "Fade", Color.black);
            _flash = Cover(root, "Flash", Color.white);

            SetLetterbox(0f);
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

            _hint.text = text ?? string.Empty;
            var c = _hint.color;
            c.a = Mathf.Clamp01(alpha);
            _hint.color = c;
        }

        /// <summary>Black cover alpha (1 = fully black).</summary>
        public void SetFade(float alpha) => SetCover(_fade, alpha);

        /// <summary>White cover alpha (reveal flash), drawn above the black fade.</summary>
        public void SetFlash(float alpha) => SetCover(_flash, alpha);

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
