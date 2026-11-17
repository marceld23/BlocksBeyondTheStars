// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a click on a menu button LOOKS like — until now it only made a sound, and the stock tint (cyan while the
    /// mouse button is down) is over before the action fires on release. Two parts:
    /// <list type="bullet">
    /// <item><b>the press</b> (<see cref="UiPressFx"/>): the button gives a little while the pointer holds it down —
    /// seen before anything happens;</item>
    /// <item><b>the click</b> (<see cref="Flash"/>): the button's outline lights up and fades. It is drawn on a canvas
    /// of its own, because many clicks rebuild or close their window at once (every settings row does): a flash on
    /// the button itself would die with it.</item>
    /// </list>
    /// Every button built by <see cref="UiKit.AddButton"/> gets both, so they look the same everywhere; the pad's and
    /// a finger's click flash too (the flash hangs on the click, not on the mouse). Off under "Reduced effects" and
    /// outside play mode (EditMode tests click buttons).
    /// </summary>
    public static class UiClickFx
    {
        /// <summary>How far a held button shrinks (about its centre).</summary>
        public const float PressScale = 0.97f;

        /// <summary>How long the outline's flash takes to fade, seconds.</summary>
        public const float FlashSeconds = 0.22f;

        private const int MaxFlashes = 6;
        private static readonly Color FlashColour = new Color(0.72f, 0.95f, 1f, 1f);
        private static readonly Vector3[] Corners = new Vector3[4];
        private static readonly List<Image> Pool = new List<Image>(MaxFlashes);
        private static RectTransform _layer;
        private static int _next;

        /// <summary>Gives <paramref name="button"/> the press and the click look.</summary>
        public static void Attach(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.AddComponent<UiPressFx>();
            button.onClick.AddListener(() => Flash(button)); // added before the button's own action: it may destroy the button
        }

        /// <summary>Lights the outline of <paramref name="button"/> up where it stands on the screen.</summary>
        public static void Flash(Button button)
        {
            if (button == null || !Application.isPlaying || UiKit.ReducedMotion || !(button.transform is RectTransform rect))
            {
                return;
            }

            var canvas = button.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root == null || root.renderMode == RenderMode.WorldSpace || rect.rect.width < 1f || rect.rect.height < 1f)
            {
                return;
            }

            // Where the button is on the screen, in pixels (corner 0 = bottom left, 2 = top right).
            var camera = root.renderMode == RenderMode.ScreenSpaceCamera ? root.worldCamera : null;
            rect.GetWorldCorners(Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, Corners[2]);
            float scale = (max.x - min.x) / rect.rect.width; // the canvas' own scale: keeps the outline as thick as the button's
            if (scale <= 0.0001f)
            {
                return;
            }

            var image = NextImage();
            var rt = image.rectTransform;
            rt.sizeDelta = rect.rect.size;
            rt.localScale = new Vector3(scale, scale, 1f);
            rt.anchoredPosition = (min + max) * 0.5f;
            image.gameObject.SetActive(true);
            UiTween.Kill(image);
            UiTween.To(1f, 0f, FlashSeconds, a => SetAlpha(image, a), UiTween.Ease.OutQuad, 0f, () => Hide(image), image);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image != null)
            {
                var colour = FlashColour;
                colour.a = alpha;
                image.color = colour;
            }
        }

        private static void Hide(Image image)
        {
            if (image != null)
            {
                image.gameObject.SetActive(false);
            }
        }

        /// <summary>A flash image from the small pool: a free one, else the oldest (clicks never queue up).</summary>
        private static Image NextImage()
        {
            EnsureLayer();
            Pool.RemoveAll(i => i == null);
            foreach (var free in Pool)
            {
                if (!free.gameObject.activeSelf)
                {
                    return free;
                }
            }

            if (Pool.Count >= MaxFlashes)
            {
                _next = (_next + 1) % Pool.Count;
                return Pool[_next];
            }

            var go = new GameObject("ClickFlash", typeof(RectTransform));
            go.transform.SetParent(_layer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var image = go.AddComponent<Image>();
            image.sprite = UiKit.ClickFrameSprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            go.SetActive(false);
            Pool.Add(image);
            return image;
        }

        /// <summary>The flashes' own canvas: plain screen pixels, above every window, below the tooltip; it takes no
        /// click and lives as long as the game does.</summary>
        private static void EnsureLayer()
        {
            if (_layer != null)
            {
                return;
            }

            var go = new GameObject("UiClickFx", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 890;
            Object.DontDestroyOnLoad(go); // play mode only: Flash returns before this in edit mode
            _layer = (RectTransform)go.transform;
        }
    }

    /// <summary>The press: while the pointer holds the button down it sits a little smaller (about its centre, whatever
    /// its pivot), and springs back on release or when the pointer leaves. Not under "Reduced effects".</summary>
    public sealed class UiPressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private bool _pressed;
        private Vector3 _scale;
        private Vector2 _shift;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pressed || UiKit.ReducedMotion || eventData == null || eventData.button != PointerEventData.InputButton.Left
                || !(transform is RectTransform rect))
            {
                return;
            }

            var control = GetComponent<Selectable>();
            if (control != null && !control.IsInteractable())
            {
                return;
            }

            _scale = rect.localScale;
            // The layout's buttons hang on their top-left corner: shrinking would pull them toward it, so the same
            // move shifts the button by what its centre would otherwise travel.
            var toCentre = new Vector2(0.5f, 0.5f) - rect.pivot;
            _shift = Vector2.Scale(Vector2.Scale(rect.rect.size, toCentre), new Vector2(_scale.x, _scale.y)) * (1f - UiClickFx.PressScale);
            rect.anchoredPosition += _shift;
            rect.localScale = new Vector3(_scale.x * UiClickFx.PressScale, _scale.y * UiClickFx.PressScale, _scale.z);
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        private void OnDisable() => Release();

        private void Release()
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            if (transform is RectTransform rect)
            {
                rect.anchoredPosition -= _shift;
                rect.localScale = _scale;
            }
        }
    }
}
