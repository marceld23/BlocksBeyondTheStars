// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shader-drawn HUD chrome: rounded panels, rings and bars rendered by <c>BlocksBeyondTheStars/UiHolo</c>
    /// on a plain quad, crisp at any DPI and animatable (border sweep, boot reveal, glow pulse). One shared
    /// material — every per-element parameter travels in the quad's extra UV channels via <see cref="Shape"/>,
    /// so all holo elements on a canvas still batch. When the shader is missing (stripped build, unsupported
    /// target) every helper falls back to the bitmap <see cref="UiKit"/> sprites, so the HUD never disappears.
    /// </summary>
    public static class UiHolo
    {
        public enum Style { Panel = 0, Ring = 1, Bar = 2 }

        private static Material _material;
        private static bool _probed;

        /// <summary>The shared holo material, or null when the shader is unavailable.</summary>
        public static Material Material
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;
                    var shader = Shader.Find("BlocksBeyondTheStars/UiHolo");
                    if (shader != null && shader.isSupported)
                    {
                        _material = new Material(shader) { name = "UiHolo (shared)", hideFlags = HideFlags.HideAndDontSave };
                        _material.SetColor("_EdgeColor", new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 1f));
                        _material.SetFloat("_Scan", 0.05f);
                        _material.SetFloat("_SweepSpeed", 0.10f);
                        _material.SetFloat("_GlowWidth", 10f);
                    }
                    else
                    {
                        Debug.LogWarning("[UiHolo] BlocksBeyondTheStars/UiHolo shader not found — HUD chrome falls back to bitmap sprites.");
                    }
                }

                return _material;
            }
        }

        public static bool Available => Material != null;

        /// <summary>
        /// Writes the holo parameters into the quad's UV1/UV2 and pads the quad outward so the outer glow has
        /// room to render. The Image must be a Simple, 4-vertex quad (the <see cref="UiKit.SolidSprite"/>).
        /// Setting any property re-dirties the vertices — cheap (four vertices), and only on change.
        /// </summary>
        [DisallowMultipleComponent]
        public sealed class Shape : BaseMeshEffect
        {
            [SerializeField] private float _radius = 10f;
            [SerializeField] private float _border = 1.5f;
            [SerializeField] private float _glow = 1f;
            [SerializeField] private float _pad = 14f;
            [SerializeField] private float _reveal = 1f;
            [SerializeField] private float _fillOpacity = 1f;
            [SerializeField] private Style _style = Style.Panel;

            public float Radius { get => _radius; set => Set(ref _radius, value); }
            public float Border { get => _border; set => Set(ref _border, value); }
            /// <summary>Outer glow strength (0 = none, 1 = normal, 2+ = highlighted).</summary>
            public float Glow { get => _glow; set => Set(ref _glow, value); }
            /// <summary>Quad padding in canvas units — room for the glow outside the logical rect.</summary>
            public float Pad { get => _pad; set => Set(ref _pad, value); }
            /// <summary>Boot-up wipe, 0 = hidden … 1 = fully drawn.</summary>
            public float Reveal { get => _reveal; set => Set(ref _reveal, Mathf.Clamp01(value)); }
            /// <summary>Opacity of the fill alone (the vertex alpha — Image.color.a × CanvasGroup — scales the
            /// whole element: fill, border and glow), so a faint fill can still carry a bright edge.</summary>
            public float FillOpacity { get => _fillOpacity; set => Set(ref _fillOpacity, Mathf.Clamp01(value)); }
            public Style Kind { get => _style; set { if (_style != value) { _style = value; Dirty(); } } }

            private void Set(ref float field, float value)
            {
                if (!Mathf.Approximately(field, value))
                {
                    field = value;
                    Dirty();
                }
            }

            private void Dirty()
            {
                if (graphic != null)
                {
                    graphic.SetVerticesDirty();
                }
            }

            private static readonly List<UIVertex> _verts = new List<UIVertex>(4);

            public override void ModifyMesh(VertexHelper vh)
            {
                if (!IsActive() || vh.currentVertCount != 4)
                {
                    return;
                }

                _verts.Clear();
                var v = default(UIVertex);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    vh.PopulateUIVertex(ref v, i);
                    _verts.Add(v);
                    minX = Mathf.Min(minX, v.position.x); maxX = Mathf.Max(maxX, v.position.x);
                    minY = Mathf.Min(minY, v.position.y); maxY = Mathf.Max(maxY, v.position.y);
                }

                float w = maxX - minX, h = maxY - minY;
                float pad = _glow > 0f ? _pad : 0f;
                var p1 = new Vector4(w, h, _radius, _border);
                var p2 = new Vector4((float)_style, _glow, pad, _reveal);
                var p3 = new Vector4(_fillOpacity, 0f, 0f, 0f);
                for (int i = 0; i < 4; i++)
                {
                    v = _verts[i];
                    var pos = v.position;
                    // Expand the quad outward by `pad`; uv0 already spans 0..1 across the (now padded) quad.
                    pos.x = pos.x <= minX + 0.001f ? minX - pad : maxX + pad;
                    pos.y = pos.y <= minY + 0.001f ? minY - pad : maxY + pad;
                    v.position = pos;
                    v.uv0 = new Vector4(pos.x <= minX ? 0f : 1f, pos.y <= minY ? 0f : 1f, 0f, 0f);
                    v.uv1 = p1;
                    v.uv2 = p2;
                    v.uv3 = p3;
                    vh.SetUIVertex(v, i);
                }
            }
        }

        /// <summary>Turns an existing Image into a holo shape (solid sprite + shared material + parameters).
        /// No-op (returns null) when the shader is unavailable, leaving the Image as it was.</summary>
        public static Shape Apply(Image img, Style style, float radius, float border, float glow = 1f)
        {
            var mat = Material;
            if (img == null || mat == null)
            {
                return null;
            }

            img.sprite = UiKit.SolidSprite;
            img.type = Image.Type.Simple;
            img.material = mat;
            var shape = img.GetComponent<Shape>() ?? img.gameObject.AddComponent<Shape>();
            shape.Kind = style;
            shape.Radius = radius;
            shape.Border = border;
            shape.Glow = glow;
            return shape;
        }

        /// <summary>A holographic panel at a top-left anchored rect (falls back to <see cref="UiKit.AddPanel"/>).</summary>
        public static Image AddPanel(Transform parent, float x, float y, float w, float h, Color fill, float radius = 10f, float border = 1.5f, float glow = 1f)
        {
            if (!Available)
            {
                return UiKit.AddPanel(parent, x, y, w, h, fill);
            }

            // The fill's translucency rides in the shape (FillOpacity); Image.color stays opaque so the vertex
            // alpha is free for CanvasGroup fades and caller tints of the WHOLE element.
            var img = UiKit.AddImage(parent, x, y, w, h, UiKit.SolidSprite, new Color(fill.r, fill.g, fill.b, 1f));
            img.gameObject.name = "HoloPanel";
            var shape = Apply(img, Style.Panel, radius, border, glow);
            if (shape != null)
            {
                shape.FillOpacity = fill.a;
            }

            return img;
        }

        /// <summary>A holographic ring (radar/compass face) filling a top-left anchored square.</summary>
        public static Image AddRing(Transform parent, float x, float y, float size, Color fill, float border = 2f, float glow = 1.2f)
        {
            if (!Available)
            {
                // Fallback: the bitmap radar disc (a Graphic per object — never stack an Image and a RawImage).
                var go = new GameObject("RadarDisc", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                UiKit.Place(go, x, y, size, size);
                var raw = go.AddComponent<RawImage>();
                raw.texture = UiKit.RadarCircle;
                raw.raycastTarget = false;
                return null;
            }

            var img = UiKit.AddImage(parent, x, y, size, size, UiKit.SolidSprite, new Color(fill.r, fill.g, fill.b, 1f));
            img.gameObject.name = "HoloRing";
            Apply(img, Style.Ring, size * 0.5f, border, glow).FillOpacity = fill.a;
            return img;
        }

        /// <summary>A rounded bar track + fill pair. The fill's width is driven via <see cref="SetBar"/>.</summary>
        public static (Image Track, Image Fill) AddBar(Transform parent, float x, float y, float w, float h, Color track, Color fill)
        {
            var t = UiKit.AddImage(parent, x, y, w, h, UiKit.SolidSprite, Available ? new Color(track.r, track.g, track.b, 1f) : track);
            t.gameObject.name = "HoloBarTrack";
            var f = UiKit.AddImage(parent, x, y, w, h, UiKit.SolidSprite, fill);
            f.gameObject.name = "HoloBarFill";
            if (Available)
            {
                Apply(t, Style.Panel, h * 0.5f, 1f, 0.35f).FillOpacity = track.a;
                Apply(f, Style.Panel, h * 0.5f, 0.5f, 0.9f);
            }
            else
            {
                f.type = Image.Type.Filled;
                f.fillMethod = Image.FillMethod.Horizontal;
                f.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            return (t, f);
        }

        /// <summary>Sets a bar fill to <paramref name="frac"/> of <paramref name="fullWidth"/> (holo: resize the
        /// rounded fill, never thinner than its own height so the pill keeps its shape; fallback: fillAmount).</summary>
        public static void SetBar(Image fill, float frac, float fullWidth)
        {
            if (fill == null)
            {
                return;
            }

            frac = Mathf.Clamp01(frac);
            if (fill.type == Image.Type.Filled)
            {
                fill.fillAmount = frac;
                return;
            }

            var rt = fill.rectTransform;
            float h = rt.sizeDelta.y;
            float target = frac <= 0.001f ? 0f : Mathf.Max(h, fullWidth * frac);
            if (!Mathf.Approximately(rt.sizeDelta.x, target))
            {
                rt.sizeDelta = new Vector2(target, h);
            }

            bool visible = target > 0f;
            if (fill.enabled != visible)
            {
                fill.enabled = visible;
            }
        }

        private static readonly List<Shape> _shapeScratch = new List<Shape>(32);

        /// <summary>Boot-up sequence: every holo shape under <paramref name="root"/> wipes in left→right with a
        /// short stagger. Instant under reduced motion. Returns the total duration.</summary>
        public static float PlayReveal(Transform root, float perShape = 0.32f, float stagger = 0.045f, float delay = 0f)
        {
            if (root == null)
            {
                return 0f;
            }

            _shapeScratch.Clear();
            root.GetComponentsInChildren(true, _shapeScratch);
            if (UiKit.ReducedMotion || _shapeScratch.Count == 0)
            {
                foreach (var s in _shapeScratch)
                {
                    s.Reveal = 1f;
                }

                return 0f;
            }

            for (int i = 0; i < _shapeScratch.Count; i++)
            {
                var s = _shapeScratch[i];
                s.Reveal = 0f;
                UiTween.To(0f, 1f, perShape, r => { if (s != null) { s.Reveal = r; } }, UiTween.Ease.OutCubic, delay + i * stagger, null, s);
            }

            return delay + perShape + stagger * _shapeScratch.Count;
        }

        /// <summary>A brief glow flash on one shape (selection / change feedback): glow jumps to
        /// <paramref name="peak"/> and eases back to <paramref name="rest"/>.</summary>
        public static void Flash(Shape shape, float peak = 2.6f, float rest = 1f, float duration = 0.35f)
        {
            if (shape == null)
            {
                return;
            }

            UiTween.Kill(shape);
            UiTween.To(peak, rest, duration, g => { if (shape != null) { shape.Glow = g; } }, UiTween.Ease.OutQuad, 0f, null, shape);
        }
    }
}
