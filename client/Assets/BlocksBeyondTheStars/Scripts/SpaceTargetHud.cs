// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The outline a <see cref="SpaceTargetFrame"/> draws (#2277): every target disposition has its own shape, so
    /// colour is never the only cue.</summary>
    public enum TargetFrameShape
    {
        /// <summary>Four square corner brackets — a neutral target, and the ship scanner's lock.</summary>
        Corners,

        /// <summary>The corner brackets turned onto their points — an enemy (with a "!" over it).</summary>
        Diamond,

        /// <summary>A closed, hollow diamond — a raider that still demands cargo.</summary>
        DiamondOutline,

        /// <summary>A ring — a friendly target (station, life pod, another pilot).</summary>
        Ring,
    }

    /// <summary>
    /// One bracket frame on the flight HUD around a target on screen (#2283: the ship scanner's lock and the target lock
    /// share this one class — the scanner only adds its charge ring and the empty track). Plain uGUI images and texts at
    /// absolute positions under the flight view's targeting layer (no LayoutGroups); the label texts are only assigned
    /// when they change, so a frame that follows its target every frame does not rebuild text meshes.
    /// </summary>
    public sealed class SpaceTargetFrame
    {
        private const float CornerLength = 18f;

        private readonly RectTransform _root;
        private readonly RectTransform _shape;
        private readonly Image[] _bars = new Image[8];
        private readonly Image _ring;
        private readonly Text _mark;
        private readonly Text _label;
        private readonly Text _subLabel;
        private readonly Image _chargeTrack;
        private readonly Image _charge;
        private string _labelText = string.Empty;
        private string _subText = string.Empty;
        private int _labelLines = 1;
        private float _extent = 60f;

        public SpaceTargetFrame(Transform parent, string name, bool chargeRing)
        {
            var rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            _root = rootGo.GetComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = Vector2.zero;

            var shapeGo = new GameObject("Shape", typeof(RectTransform));
            shapeGo.transform.SetParent(_root, false);
            _shape = shapeGo.GetComponent<RectTransform>();
            _shape.anchorMin = _shape.anchorMax = _shape.pivot = new Vector2(0.5f, 0.5f);
            _shape.sizeDelta = Vector2.zero;
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i] = NewImage(_shape, "Bar" + i, UiKit.SolidSprite);
            }

            _ring = NewImage(_root, "Ring", SpaceTargetHud.RingSprite);
            _ring.enabled = false;

            if (chargeRing)
            {
                // #2247: the empty track shows from the lock on — "something here wants filling" — and the fill runs
                // round it while the trigger is held.
                _chargeTrack = NewImage(_root, "ChargeTrack", SpaceTargetHud.RingSprite);
                _charge = NewImage(_root, "Charge", SpaceTargetHud.RingSprite);
                _charge.type = Image.Type.Filled;
                _charge.fillMethod = Image.FillMethod.Radial360;
                _charge.fillOrigin = (int)Image.Origin360.Top;
                _charge.fillClockwise = true;
                _charge.enabled = false;
            }

            _mark = NewText(_root, "Mark", 24, TextAnchor.MiddleCenter, new Vector2(40f, 32f));
            _mark.fontStyle = FontStyle.Bold;
            _mark.text = "!";
            _mark.enabled = false;
            _label = NewText(_root, "Label", 17, TextAnchor.UpperCenter, new Vector2(520f, 48f));
            _subLabel = NewText(_root, "SubLabel", 16, TextAnchor.UpperCenter, new Vector2(520f, 24f));
            rootGo.SetActive(false);
        }

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public void Hide()
        {
            // #2429: during a scene or application teardown the overlay canvas is destroyed before SpaceView.OnDestroy
            // resets the lock — a destroyed RectTransform's gameObject throws, so the Unity fake-null check comes first.
            if (_root != null && _root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>Puts the frame at <paramref name="anchored"/> (the targeting layer's local point) with a half size of
        /// <paramref name="half"/> units. <paramref name="brackets"/> false leaves only the label and the charge ring (the
        /// scanner's system ping, which has no target).</summary>
        public void Place(Vector2 anchored, float half, TargetFrameShape shape, Color color, float thickness, bool brackets = true, bool mark = false)
        {
            if (_root == null)
            {
                return; // #2429: the overlay is already torn down
            }

            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            _root.anchoredPosition = anchored;
            bool diamond = shape == TargetFrameShape.Diamond || shape == TargetFrameShape.DiamondOutline;
            // A diamond's points reach h·√2 along the axes; shrink it so it covers about what the square would.
            float h = diamond ? half * 0.82f : half;
            _shape.localRotation = diamond ? Quaternion.Euler(0f, 0f, 45f) : Quaternion.identity;
            _extent = diamond ? h * 1.4142f : half;

            bool bars = brackets && shape != TargetFrameShape.Ring;
            // Corner brackets are short; a closed outline runs every bar along its whole edge (each pair meets mid-edge).
            float len = shape == TargetFrameShape.DiamondOutline ? h + thickness : Mathf.Min(CornerLength, h);
            for (int c = 0; c < 4; c++)
            {
                float sx = c % 2 == 0 ? -1f : 1f;
                float sy = c < 2 ? 1f : -1f;
                var horiz = _bars[c * 2];
                var vert = _bars[c * 2 + 1];
                horiz.rectTransform.sizeDelta = new Vector2(len, thickness);
                horiz.rectTransform.anchoredPosition = new Vector2(sx * (h - len * 0.5f), sy * h);
                vert.rectTransform.sizeDelta = new Vector2(thickness, len);
                vert.rectTransform.anchoredPosition = new Vector2(sx * h, sy * (h - len * 0.5f));
                horiz.color = color;
                vert.color = color;
                horiz.enabled = bars;
                vert.enabled = bars;
            }

            bool ring = brackets && shape == TargetFrameShape.Ring;
            _ring.enabled = ring;
            if (ring)
            {
                _ring.color = color;
                _ring.rectTransform.sizeDelta = new Vector2(half * 2.2f, half * 2.2f);
                _ring.rectTransform.anchoredPosition = Vector2.zero;
            }

            _mark.enabled = mark && brackets;
            if (_mark.enabled)
            {
                _mark.color = color;
                _mark.rectTransform.anchoredPosition = new Vector2(0f, _extent + 16f);
            }

            _label.rectTransform.anchoredPosition = new Vector2(0f, -_extent - 30f);
            _subLabel.rectTransform.anchoredPosition = new Vector2(0f, -_extent - 30f - _labelLines * 19f);
        }

        /// <summary>The label under the frame (assigned only when it changed).</summary>
        public void SetLabel(string text, Color color)
        {
            text ??= string.Empty;
            if (!string.Equals(text, _labelText, System.StringComparison.Ordinal))
            {
                _labelText = text;
                _label.text = text;
                _labelLines = 1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        _labelLines++;
                    }
                }

                _subLabel.rectTransform.anchoredPosition = new Vector2(0f, -_extent - 30f - _labelLines * 19f);
            }

            if (_label.color != color)
            {
                _label.color = color;
            }
        }

        /// <summary>A second line in its own colour under the label ("In range" / "Too far"); empty hides it.</summary>
        public void SetSubLabel(string text, Color color)
        {
            text ??= string.Empty;
            if (!string.Equals(text, _subText, System.StringComparison.Ordinal))
            {
                _subText = text;
                _subLabel.text = text;
            }

            _subLabel.enabled = text.Length > 0;
            if (_subLabel.color != color)
            {
                _subLabel.color = color;
            }
        }

        /// <summary>The scanner's hold ring (a frame built with <c>chargeRing</c>): the track always, the fill while
        /// <paramref name="filling"/>.</summary>
        public void SetCharge(bool filling, float progress, float size, Color color)
        {
            if (_charge == null)
            {
                return;
            }

            _chargeTrack.enabled = true;
            _chargeTrack.color = new Color(color.r, color.g, color.b, 0.28f);
            _chargeTrack.rectTransform.sizeDelta = new Vector2(size, size);
            _chargeTrack.rectTransform.anchoredPosition = Vector2.zero;
            _charge.enabled = filling;
            _charge.color = new Color(color.r, color.g, color.b, 0.9f);
            _charge.fillAmount = progress;
            _charge.rectTransform.sizeDelta = new Vector2(size, size);
            _charge.rectTransform.anchoredPosition = Vector2.zero;
        }

        private static Image NewImage(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        internal static Text NewText(Transform parent, string name, int size, TextAnchor anchor, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = size;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false; // names come from players — never let one inject markup
            t.raycastTarget = false;
            return t;
        }
    }

    /// <summary>
    /// A direction marker on the inner ellipse of the flight HUD (#2277/#2283): the edge arrow toward a locked target off
    /// screen (filled and doubled for an enemy, filled for a raider demanding cargo, hollow for the rest), the small threat
    /// ticks toward other attackers, and the amber waypoint arrow. The arrow turns; its distance label stays upright and
    /// sits on the inner side, so it never leaves the screen.
    /// </summary>
    public sealed class SpaceEdgeMarker
    {
        private readonly RectTransform _root;
        private readonly RectTransform _arrow;
        private readonly Image _head;
        private readonly Image _second;
        private readonly Text _label;
        private readonly float _size;
        private string _labelText = string.Empty;

        public SpaceEdgeMarker(Transform parent, string name, float size, bool withLabel)
        {
            _size = size;
            var rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            _root = rootGo.GetComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = Vector2.zero;

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_root, false);
            _arrow = arrowGo.GetComponent<RectTransform>();
            _arrow.anchorMin = _arrow.anchorMax = _arrow.pivot = new Vector2(0.5f, 0.5f);
            _arrow.sizeDelta = Vector2.zero;

            _head = NewTriangle(_arrow, "Head", size, Vector2.zero);
            _second = NewTriangle(_arrow, "Second", size, new Vector2(0f, -size * 0.62f)); // the "»" of an enemy arrow
            _second.enabled = false;
            if (withLabel)
            {
                _label = SpaceTargetFrame.NewText(_root, "Label", 15, TextAnchor.MiddleCenter, new Vector2(240f, 22f));
                _label.fontStyle = FontStyle.Bold;
            }

            rootGo.SetActive(false);
        }

        public void Hide()
        {
            // #2429: during a scene or application teardown the overlay canvas is destroyed before SpaceView.OnDestroy
            // resets the lock — a destroyed RectTransform's gameObject throws, so the Unity fake-null check comes first.
            if (_root != null && _root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>Shows the arrow at <paramref name="anchored"/> pointing along <paramref name="angleDeg"/>
        /// (counter-clockwise from screen-right). <paramref name="scale"/> pulses it.</summary>
        public void Place(Vector2 anchored, float angleDeg, Color color, bool hollow, bool doubled, float scale = 1f)
        {
            if (_root == null)
            {
                return; // #2429: the overlay is already torn down
            }

            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            _root.anchoredPosition = anchored;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, BlocksBeyondTheStars.Client.Core.SpaceTargeting.UpSpriteRotation(angleDeg));
            _arrow.localScale = new Vector3(scale, scale, 1f);
            var sprite = hollow ? SpaceTargetHud.HollowTriangleSprite : UiKit.TriangleSprite;
            _head.enabled = true;
            _head.sprite = sprite;
            _head.color = color;
            _second.enabled = doubled;
            if (doubled)
            {
                _second.sprite = sprite;
                _second.color = new Color(color.r, color.g, color.b, color.a * 0.75f);
            }

            if (_label != null)
            {
                // Toward the screen centre (against the arrow's direction), far enough that the upright one-line text
                // clears the arrow: half its width when the arrow points sideways, half its height when it points up/down.
                float rad = angleDeg * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                float back = _size * (doubled ? 1.15f : 0.5f); // how far the arrow reaches behind its point
                float d = back + 8f + Mathf.Abs(c) * 42f + Mathf.Abs(s) * 11f;
                _label.rectTransform.anchoredPosition = new Vector2(-c, -s) * d;
                _label.color = color;
            }
        }

        /// <summary>Shows only the label at an on-screen point (the waypoint while it is in view), no arrow.</summary>
        public void PlacePoint(Vector2 anchored, Color color)
        {
            if (_root == null)
            {
                return; // #2429: the overlay is already torn down
            }

            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            _root.anchoredPosition = anchored;
            _head.enabled = false;
            _second.enabled = false;
            if (_label != null)
            {
                _label.rectTransform.anchoredPosition = Vector2.zero;
                _label.color = color;
            }
        }

        /// <summary>The distance text (assigned only when it changed).</summary>
        public void SetLabel(string text)
        {
            if (_label == null)
            {
                return;
            }

            text ??= string.Empty;
            if (!string.Equals(text, _labelText, System.StringComparison.Ordinal))
            {
                _labelText = text;
                _label.text = text;
            }
        }

        private static Image NewTriangle(Transform parent, string name, float size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.TriangleSprite;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchoredPosition = offset;
            return img;
        }
    }

    /// <summary>The procedural sprites of the flight targeting HUD (generated once, white, tinted by the caller).</summary>
    public static class SpaceTargetHud
    {
        private static Sprite _ring;
        private static Sprite _hollowTriangle;

        /// <summary>A soft white ring — the friendly frame and the scanner's hold ring.</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (_ring != null)
                {
                    return _ring;
                }

                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f;
                        float dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.08f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                tex.SetPixels32(px);
                tex.Apply(false, true);
                _ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _ring;
            }
        }

        /// <summary>An outlined triangle pointing UP (same footprint as <see cref="UiKit.TriangleSprite"/>) — the hollow
        /// arrow of a neutral or friendly target.</summary>
        public static Sprite HollowTriangleSprite
        {
            get
            {
                if (_hollowTriangle != null)
                {
                    return _hollowTriangle;
                }

                const int n = 64;
                const float stroke = 7f; // outline width in texels
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                // The slanted edges are steeper than 45°: their perpendicular distance is the horizontal one times this.
                float slant = 1f / Mathf.Sqrt(1f + 4f);
                for (int y = 0; y < n; y++)
                {
                    float t = y / (float)(n - 1);
                    float half = (1f - t) * (n * 0.5f);
                    for (int x = 0; x < n; x++)
                    {
                        float side = (half - Mathf.Abs(x - (n - 1) * 0.5f)) * 2f * slant; // to a slanted edge
                        float inside = Mathf.Min(side, y + 0.5f);                      // and to the base
                        float a = Mathf.Clamp01(inside) * Mathf.Clamp01(stroke - inside + 1f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                tex.SetPixels32(px);
                tex.Apply(false, true);
                _hollowTriangle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _hollowTriangle;
            }
        }
    }
}
