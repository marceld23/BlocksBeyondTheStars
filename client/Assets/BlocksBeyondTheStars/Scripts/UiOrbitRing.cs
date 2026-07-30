// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A hairline ring (annulus) inscribed in its RectTransform, drawn as generated geometry rather than
    /// a stretched sprite — the orbit paths on the flight system chart (#623) and the "this planet has
    /// rings" glyph beside a ringed body.
    /// <para>Why a custom graphic instead of a ring texture: a sprite stretched to the orbit diameter
    /// scales its own line weight with the radius, so an outer orbit comes out fat and an inner one a
    /// hairline. Here <see cref="Thickness"/> is in canvas units and independent of the radius, so every
    /// orbit reads with the same line weight at any size — and one ring is a single draw element instead
    /// of the ~50 dot images a dashed-sprite ring would need.</para>
    /// <para>A non-square rect yields an ellipse (the ring glyph uses that for its tilted look). Uses the
    /// default UI material — no shader of its own, so nothing to always-include in the build.</para>
    /// </summary>
    public sealed class UiOrbitRing : MaskableGraphic
    {
        /// <summary>Line weight in canvas units, measured inwards from the rect edge.</summary>
        public float Thickness = 2f;

        /// <summary>Segments around the ring. 72 is smooth at chart size (5° per segment) and still only
        /// 144 vertices.</summary>
        public int Segments = 72;

        /// <summary>Creates a centre-pivot ring under <paramref name="parent"/>, sized
        /// <paramref name="size"/> (outer diameter, x × y) around <paramref name="pos"/>. Never a raycast
        /// target: the chart's own click handler must keep receiving clicks through it.</summary>
        public static UiOrbitRing Create(Transform parent, Vector2 pos, Vector2 size, Color color, float thickness = 2f)
        {
            var go = new GameObject("OrbitRing", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var ring = go.AddComponent<UiOrbitRing>();
            ring.Thickness = thickness;
            ring.color = color;
            ring.raycastTarget = false;
            return ring;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float outerX = rect.width * 0.5f, outerY = rect.height * 0.5f;
            if (outerX <= 0.5f || outerY <= 0.5f || Segments < 3)
            {
                return;
            }

            // Pull the inner edge in by the same number of units on both axes → constant line weight,
            // whatever the radius (and never past the centre, so a tiny ring degenerates to a filled dot).
            float innerX = Mathf.Max(0f, outerX - Thickness);
            float innerY = Mathf.Max(0f, outerY - Thickness);
            float cx = rect.x + outerX, cy = rect.y + outerY;

            var col = (Color32)color;
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                vh.AddVert(new Vector3(cx + cos * outerX, cy + sin * outerY), col, Vector2.zero);
                vh.AddVert(new Vector3(cx + cos * innerX, cy + sin * innerY), col, Vector2.zero);
            }

            for (int i = 0; i < Segments; i++)
            {
                int outerA = i * 2, innerA = outerA + 1;
                int outerB = (i + 1) % Segments * 2, innerB = outerB + 1;
                vh.AddTriangle(outerA, outerB, innerA);
                vh.AddTriangle(innerA, outerB, innerB);
            }
        }
    }
}
