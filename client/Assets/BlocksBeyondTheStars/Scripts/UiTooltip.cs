// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one tooltip of the interface: a small panel with a name and a line or two, shown next to what the player
    /// points at. One shared view on its own top canvas, so any screen gets tooltips with one call:
    /// <see cref="Attach"/> on the element. It appears after a short rest of the mouse
    /// (<see cref="HoverSeconds"/>), on the element the pad has focused, and on a long press of a finger
    /// (<see cref="LongPressSeconds"/>); it never takes a click (no raycast target) and goes the moment the pointer
    /// leaves, a button goes down or the element disappears. The texts are asked for when it opens, so they are always
    /// current and cost nothing while nobody looks.
    /// </summary>
    public static class UiTooltip
    {
        /// <summary>How long the mouse rests on an element before its tooltip shows.</summary>
        public const float HoverSeconds = 0.35f;

        /// <summary>How long a finger holds an element before its tooltip shows.</summary>
        public const float LongPressSeconds = 0.5f;

        private const float MaxWidth = 380f, Pad = 12f, Gap = 18f;
        private const int TitleSize = 20, BodySize = 17;

        private static Canvas _canvas;
        private static RectTransform _panel;
        private static Text _title, _body;
        private static UiTooltipTrigger _owner;

        /// <summary>Gives <paramref name="target"/> a tooltip: <paramref name="title"/> (one line, may return null or
        /// empty for "nothing to say right now") and an optional <paramref name="body"/> below it.</summary>
        public static void Attach(GameObject target, Func<string> title, Func<string> body = null)
        {
            if (target == null || title == null)
            {
                return;
            }

            var trigger = target.GetComponent<UiTooltipTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<UiTooltipTrigger>();
            }

            trigger.Title = title;
            trigger.Body = body;
        }

        /// <summary>The first sentence of <paramref name="text"/> (up to <paramref name="max"/> characters) — a tooltip
        /// names the thing, the detail pane tells the rest.</summary>
        public static string FirstSentence(string text, int max = 140)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string t = text.Trim();
            int end = -1;
            for (int i = 0; i < t.Length - 1; i++)
            {
                if ((t[i] == '.' || t[i] == '!' || t[i] == '?' || t[i] == '。') && (t[i + 1] == ' ' || t[i + 1] == '\n'))
                {
                    end = i + 1;
                    break;
                }
            }

            if (end > 0 && end <= max)
            {
                return t.Substring(0, end);
            }

            return t.Length <= max ? t : t.Substring(0, max).TrimEnd() + "…";
        }

        internal static bool IsShownFor(UiTooltipTrigger trigger) => _owner == trigger && _panel != null && _panel.gameObject.activeSelf;

        internal static void Show(UiTooltipTrigger trigger, bool besideElement)
        {
            string title = trigger.Title?.Invoke();
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            string body = trigger.Body?.Invoke() ?? string.Empty;
            Ensure();
            _owner = trigger;
            float textW = MaxWidth - (2f * Pad);
            _title.text = title;
            float titleH = Mathf.Ceil(UiKit.MeasureHeight(title, textW, TitleSize, FontStyle.Bold, false));
            UiKit.Place(_title.gameObject, Pad, Pad, textW, titleH);
            float bodyH = 0f;
            _body.gameObject.SetActive(body.Length > 0);
            if (body.Length > 0)
            {
                _body.text = body;
                bodyH = Mathf.Ceil(UiKit.MeasureHeight(body, textW, BodySize, FontStyle.Normal, false));
                UiKit.Place(_body.gameObject, Pad, Pad + titleH + 4f, textW, bodyH);
            }

            float h = (2f * Pad) + titleH + (bodyH > 0f ? bodyH + 4f : 0f);
            PlaceAndShow(trigger, besideElement, h);
        }

        /// <summary>Puts the panel next to the pointer (or under the pad's focused element), keeps it on the screen and
        /// fades it in.</summary>
        private static void PlaceAndShow(UiTooltipTrigger trigger, bool besideElement, float h)
        {
            float sf = Mathf.Max(0.01f, _canvas.scaleFactor);
            float canvasW = Screen.width / sf, canvasH = Screen.height / sf;
            Vector2 at;
            if (besideElement && trigger.transform is RectTransform rt)
            {
                // The pad's focus: below the element, left edges aligned.
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                at = new Vector2(corners[0].x / sf, ((Screen.height - corners[0].y) / sf) + 6f);
            }
            else
            {
                Vector2 pointer = Input.mousePosition;
                at = new Vector2((pointer.x / sf) + Gap, ((Screen.height - pointer.y) / sf) + Gap);
            }

            // Keep it on the screen: flip above / to the left of the pointer when it would run off.
            if (at.x + MaxWidth > canvasW - 8f)
            {
                at.x = Mathf.Max(8f, canvasW - MaxWidth - 8f);
            }

            if (at.y + h > canvasH - 8f)
            {
                at.y = Mathf.Max(8f, at.y - h - (2f * Gap));
            }

            UiKit.Place(_panel.gameObject, at.x, at.y, MaxWidth, h);
            _panel.gameObject.SetActive(true);
            var group = _panel.GetComponent<CanvasGroup>();
            group.alpha = UiKit.ReducedMotion ? 1f : 0f;
            if (!UiKit.ReducedMotion)
            {
                UiTween.Alpha(group, 1f, 0.1f, UiTween.Ease.OutQuad);
            }
        }

        internal static void Hide(UiTooltipTrigger trigger)
        {
            if (_owner == trigger && _panel != null)
            {
                _panel.gameObject.SetActive(false);
                _owner = null;
            }
        }

        private static void Ensure()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("Tooltip");
            _canvas.sortingOrder = 900; // above every window
            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false; // a tooltip never takes a click
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);
            }

            var img = UiKit.AddPanel(_canvas.transform, 0f, 0f, MaxWidth, 60f, new Color(0.03f, 0.08f, 0.17f, 0.96f));
            _panel = img.rectTransform;
            _panel.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _title = UiKit.AddText(_panel, Pad, Pad, MaxWidth - (2f * Pad), 26f, string.Empty, TitleSize, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.raycastTarget = false;
            _body = UiKit.AddText(_panel, Pad, Pad + 30f, MaxWidth - (2f * Pad), 26f, string.Empty, BodySize, UiKit.TextCol, TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.raycastTarget = false;
            _panel.gameObject.SetActive(false);
        }
    }

    /// <summary>The element side of <see cref="UiTooltip"/>: watches the pointer, the pad's focus and a finger's long
    /// press on its element and asks the shared view to show or hide.</summary>
    public sealed class UiTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Func<string> Title;
        public Func<string> Body;

        private bool _hover, _focus, _finger;
        private float _since;
        private Canvas _canvas;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            _finger = eventData.pointerId >= 0; // touch ids start at 0; the mouse buttons are negative
            _since = Time.unscaledTime;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            UiTooltip.Hide(this);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerId >= 0)
            {
                _hover = true; // a finger: the press itself starts the long-press count
                _finger = true;
                _since = Time.unscaledTime;
                return;
            }

            _hover = false; // a mouse click acts — the tooltip gets out of the way until the pointer comes back
            UiTooltip.Hide(this);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId >= 0)
            {
                _hover = false;
                UiTooltip.Hide(this);
            }
        }

        public void OnSelect(BaseEventData eventData)
        {
            // Only the pad's focus speaks: a mouse click selects its button too, and that is no question.
            _focus = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _since = Time.unscaledTime;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _focus = false;
            UiTooltip.Hide(this);
        }

        private void Update()
        {
            if (!_hover && !_focus)
            {
                return;
            }

            // A window that closes by switching its canvas off (the Tab menu) leaves its slots active and the pad's
            // focus on one of them: no deselect and no disable ever arrives, so the tooltip would stay — or pop up —
            // beside a slot nobody sees.
            if (!CanvasShowing())
            {
                _hover = false;
                _focus = false;
                UiTooltip.Hide(this);
                return;
            }

            if (UiTooltip.IsShownFor(this))
            {
                return;
            }

            float wait = _focus ? UiTooltip.HoverSeconds * 2f : _finger ? UiTooltip.LongPressSeconds : UiTooltip.HoverSeconds;
            if (Time.unscaledTime - _since >= wait)
            {
                UiTooltip.Show(this, besideElement: _focus && !_hover);
            }
        }

        /// <summary>Whether the canvas this element is drawn on is showing — its own and the root above it. Looked up
        /// once and kept (only asked while the element is pointed at or focused); an element without a canvas counts
        /// as showing.</summary>
        private bool CanvasShowing()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
                if (_canvas == null)
                {
                    return true;
                }
            }

            return _canvas.enabled && _canvas.rootCanvas.enabled;
        }

        private void OnTransformParentChanged() => _canvas = null; // moved to another window: look the canvas up again

        private void OnDisable()
        {
            _hover = false;
            _focus = false;
            UiTooltip.Hide(this);
        }
    }
}
