// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A coloured frame along a button's rounded edge with a light that runs around it — the "something new waits
    /// here" mark of the main menu's "Join in" button. Drawn as generated geometry (the frame as a strip, the light as
    /// vertex colours, a soft halo outside the head) on the default UI material, like <see cref="UiOrbitRing"/>: no
    /// shader of its own, so nothing to always-include in the build. The maths is <see cref="RunningLight"/> (Client.Core).
    /// <para>The frame covers the button sprite's own 2-pixel edge, so it is drawn opaque. With <see cref="Running"/>
    /// off — and under <see cref="UiKit.ReducedMotion"/> — it stands still: nothing is rebuilt per frame.</para>
    /// </summary>
    public sealed class UiRunningLight : MaskableGraphic
    {
        /// <summary>The corner radius of the frame it follows (the button sprites' 14).</summary>
        public float Radius = 14f;

        /// <summary>The frame's line weight, measured inwards from the rect edge.</summary>
        public float Thickness = 2.5f;

        /// <summary>How far the head's halo reaches outside the frame.</summary>
        public float Halo = 7f;

        /// <summary>Seconds for one lap.</summary>
        public float Period = 2.6f;

        /// <summary>The tail's length as a fraction of the frame.</summary>
        public float Tail = 0.34f;

        /// <summary>The frame away from the light.</summary>
        public Color Rest = Color.gray;

        /// <summary>The frame in the light (and the whole frame when the light stands still).</summary>
        public Color Bright = Color.white;

        /// <summary>The head itself.</summary>
        public Color Spark = Color.white;

        /// <summary>The light runs. Off: a still frame in <see cref="Rest"/>.</summary>
        public bool Running;

        private RunningLightPoint[] _outline;
        private Vector2 _outlineSize;
        private const float StepSeconds = 1f / 30f;

        private float _head;
        private float _lastStep = -1f;
        private bool _wasLit;

        private bool Lit => Running && !UiKit.ReducedMotion;

        /// <summary>Adds the frame over <paramref name="target"/> (it stretches with it) as its last child.</summary>
        public static UiRunningLight Attach(RectTransform target, Color rest, Color bright, Color spark, bool running)
        {
            var go = new GameObject("RunningLight", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(target, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var light = go.AddComponent<UiRunningLight>();
            light.Rest = rest;
            light.Bright = bright;
            light.Spark = spark;
            light.Running = running;
            light.raycastTarget = false; // clicks belong to the button underneath
            return light;
        }

        private void Update()
        {
            bool lit = Lit;
            if (lit)
            {
                // Thirty steps a second are smooth for a light with a long tail, and the menu's canvas is rebuilt half
                // as often as at 60 fps (small hardware).
                if (Time.unscaledTime - _lastStep < StepSeconds)
                {
                    return;
                }

                _lastStep = Time.unscaledTime;
                _head = RunningLight.Head(Time.unscaledTimeAsDouble, Period);
                SetVerticesDirty();
            }
            else if (_wasLit)
            {
                SetVerticesDirty(); // "calmer effects" was switched on while the light ran: one rebuild to the still frame
            }

            _wasLit = lit;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            if (_outline == null || _outlineSize != rect.size)
            {
                _outline = RunningLight.Outline(rect.width, rect.height, Radius);
                _outlineSize = rect.size;
            }

            bool lit = Lit;
            // Standing still: the quiet frame, or — where the light would run but motion is reduced — the frame fully lit.
            Color still = Running ? Bright : Rest;
            Vector2 centre = rect.center;
            for (int i = 0; i < _outline.Length; i++)
            {
                var p = _outline[i];
                float glow = lit ? RunningLight.Glow(p.At, _head, Tail) : 0f;
                Color line = lit ? Color.Lerp(Color.Lerp(Rest, Bright, Mathf.Clamp01(glow * 1.5f)), Spark, Mathf.Clamp01((glow - 0.8f) * 4f)) : still;
                line.a *= color.a;
                Color haloIn = Bright, haloOut = Bright;
                haloIn.a = glow * 0.45f * color.a;
                haloOut.a = 0f;

                var edge = new Vector2(centre.x + p.X, centre.y + p.Y);
                var normal = new Vector2(p.NormalX, p.NormalY);
                vh.AddVert(edge, line, Vector2.zero);                        // 0: the frame's outer edge
                vh.AddVert(edge - (normal * Thickness), line, Vector2.zero); // 1: its inner edge
                vh.AddVert(edge, haloIn, Vector2.zero);                      // 2: the halo at the frame
                vh.AddVert(edge + (normal * Halo), haloOut, Vector2.zero);   // 3: the halo's faded rim
            }

            for (int i = 0; i + 1 < _outline.Length; i++)
            {
                int a = i * 4, b = a + 4;
                vh.AddTriangle(a, b, a + 1);
                vh.AddTriangle(a + 1, b, b + 1);
                if (lit)
                {
                    vh.AddTriangle(a + 3, b + 3, a + 2);
                    vh.AddTriangle(a + 2, b + 3, b + 2);
                }
            }
        }
    }
}
