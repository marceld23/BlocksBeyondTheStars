// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A radial menu of any number of wedges around the screen centre — the general form of the hotbar's four-wedge
    /// ring (<see cref="HotbarActionUi"/>): the same look and the same feel (dim at rest, bright under the pointer,
    /// cyan on a press, alpha hit-testing so a wedge's empty corners never take a click), for three wedges or twelve.
    /// One ring sprite per wedge count is drawn once and kept. The wedges count clockwise from the top; the pad walks
    /// them as a ring (<see cref="WireRing"/>).
    /// </summary>
    public static class UiWheel
    {
        /// <summary>The ring's outer diameter in canvas units.</summary>
        public const float Diameter = 560f;

        /// <summary>The radius the labels sit on.</summary>
        public const float LabelRadius = 198f;

        private const float InnerShare = 0.44f; // the hole in the middle, as a share of the outer radius
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();

        /// <summary>The wedge sprite for a ring of <paramref name="count"/> wedges: the top wedge, centred on +Y.</summary>
        public static Sprite WedgeSprite(int count)
        {
            count = Mathf.Clamp(count, 2, 16);
            if (Sprites.TryGetValue(count, out var cached) && cached != null)
            {
                return cached;
            }

            const int n = 256;
            const float edge = 1.5f; // anti-alias band, px
            float c = (n - 1) / 2f;
            float rOut = c - 1f, rIn = rOut * InnerShare;
            float half = (180f / count) - 1.2f; // leaves a small gap to each neighbour
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var buf = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float radial = Mathf.Clamp01((rOut - r) / edge) * Mathf.Clamp01((r - rIn) / edge);
                    float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 90f));
                    // The angular edge is measured along the arc, so the gap stays even from the hole to the rim.
                    float angular = Mathf.Clamp01((half - off) * (Mathf.Deg2Rad * Mathf.Max(1f, r)) / edge);
                    buf[(y * n) + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Min(radial, angular)));
                }
            }

            tex.SetPixels32(buf);
            tex.Apply(false, false); // stays readable: the alpha hit test samples it
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            Sprites[count] = sprite;
            return sprite;
        }

        /// <summary>Adds wedge <paramref name="index"/> of <paramref name="count"/> (0 = top, then clockwise) around the
        /// centre of a 1920×1080 canvas. <paramref name="onHover"/> hears the pointer enter (true) and leave (false).</summary>
        public static Button AddWedge(Transform parent, int index, int count, string label, Action onClick, Action<bool> onHover = null)
        {
            float angle = -index * (360f / count); // positive z is counter-clockwise; the ring counts clockwise
            var go = new GameObject("Wedge" + index, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Diameter, Diameter);
            rt.anchoredPosition = new Vector2(960f, -540f); // the canvas centre, in top-left anchor space
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);

            var img = go.AddComponent<Image>();
            img.sprite = WedgeSprite(count);
            img.color = new Color(0.07f, 0.13f, 0.22f, 0.92f);
            img.alphaHitTestMinimumThreshold = 0.5f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = new Color(0.70f, 0.74f, 0.80f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = UiKit.Cyan;
            colors.selectedColor = new Color(0.75f, 1f, 1.5f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            go.AddComponent<UiHover>();
            btn.onClick.AddListener(UiSound.Click);
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            if (onHover != null)
            {
                go.AddComponent<UiWheelHover>().Changed = onHover;
            }

            float labelW = Mathf.Clamp(2f * LabelRadius * Mathf.Sin(Mathf.PI / count) - 12f, 90f, 260f);
            var txt = UiKit.AddText(go.transform, 0f, 0f, labelW, 30f, label, 20, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabel(txt, 10, 20);
            UiKit.AddOutline(txt);
            txt.raycastTarget = false;
            var trt = txt.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, LabelRadius);   // before the rotation "up" is the wedge's middle
            trt.localRotation = Quaternion.Euler(0f, 0f, -angle);  // … turned back so the label reads upright
            return btn;
        }

        /// <summary>Gives the wedges stick navigation as a ring: left and right step round it, up and down follow the
        /// side of the ring the wedge is on. (The wedges share one centre, so the automatic navigation cannot tell
        /// them apart — see <see cref="HotbarActionUi.WireRing"/>.)</summary>
        public static void WireRing(IReadOnlyList<Button> wedges)
        {
            int n = wedges.Count;
            for (int i = 0; i < n; i++)
            {
                var next = wedges[(i + 1) % n];
                var prev = wedges[(i + n - 1) % n];
                bool rightSide = i > 0 && i * 2 < n; // clockwise from the top: the first half is the right side
                bool leftSide = i * 2 > n;
                wedges[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnRight = next,
                    selectOnLeft = prev,
                    selectOnDown = leftSide ? prev : next,
                    selectOnUp = rightSide ? prev : leftSide ? next : null,
                };
            }
        }
    }

    /// <summary>Tells a wheel which wedge the pointer is over.</summary>
    public sealed class UiWheelHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> Changed;

        public void OnPointerEnter(PointerEventData eventData) => Changed?.Invoke(true);

        public void OnPointerExit(PointerEventData eventData) => Changed?.Invoke(false);
    }
}
