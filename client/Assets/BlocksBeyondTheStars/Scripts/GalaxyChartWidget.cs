// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hyperspace chart (#1603): a stars-only picture of the galaxy, one disc per star system at its
    /// real star-map position, drawn entirely with uGUI. The flight chart's second tab hosts it; it is
    /// built as a self-contained widget (like <see cref="SystemMapWidget"/>) so the travel screen can adopt
    /// the same picture later. The widget only draws and picks — what a star is called, which colour it
    /// has and whether a jump is allowed are decided by the caller, which knows the player's map state.
    /// <para>Legibility follows the flight chart (#592 lessons): white-on-dark, every star on a dark backing
    /// disc, names below the discs, no hairlines. The current system wears a cyan ring, a selected star a
    /// white one; jump lanes (#1125) are thin cyan lines between their two stars; unknown systems (#1113)
    /// are dimmed and labelled by the caller (a bare "?"). The finale system, once revealed, glows in the
    /// hyperspace violet the jump button already uses.</para>
    /// </summary>
    public sealed class GalaxyChartWidget : MonoBehaviour
    {
        /// <summary>One star as the caller wants it drawn.</summary>
        public struct Star
        {
            public string Id;
            public float MapX;
            public float MapY;
            public string Label;    // display name — already gated by the #1113 rule
            public Color Color;     // the star's colour (the caller resolves the fallback)
            public bool Current;    // the system the player is in
            public bool Known;      // entered before (or current) — an unknown star is dimmed
            public bool Finale;     // the Guardian finale system
            public string Players;  // comma-joined names of other players there, or empty
        }

        private const float StarDisc = 22f;
        private const float MarkerPad = StarDisc * 0.5f + 10f;
        private const float SnapRadius = 26f;
        private const float LaneThickness = 2f;

        private static readonly Color BackingCol = new Color(0.01f, 0.03f, 0.07f, 0.78f);
        private static readonly Color LaneCol = new Color(0.4f, 0.85f, 1f, 0.55f);
        private static readonly Color FinaleCol = new Color(0.62f, 0.42f, 0.9f);
        private static readonly Color UnknownLabelCol = new Color(0.30f, 0.55f, 0.72f, 0.7f);

        private RectTransform _rt;
        private float _half;
        private float _scale;
        private float _centreX, _centreY;
        private readonly List<string> _ids = new();
        private readonly List<float> _chartX = new();
        private readonly List<float> _chartY = new();
        private RectTransform _selection;

        /// <summary>Creates the widget as a square of <paramref name="size"/> at top-left <paramref name="x"/>/<paramref name="y"/>.</summary>
        public static GalaxyChartWidget Create(Transform parent, float x, float y, float size)
        {
            var go = new GameObject("GalaxyChart", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
            var bg = go.AddComponent<Image>();
            bg.sprite = UiKit.SolidSprite;
            bg.color = new Color(0.01f, 0.02f, 0.05f, 0.9f);
            bg.raycastTarget = false;

            var widget = go.AddComponent<GalaxyChartWidget>();
            widget._rt = go.GetComponent<RectTransform>();
            widget._half = size * 0.5f - 30f; // usable radius, like the flight chart
            return widget;
        }

        /// <summary>(Re)draws the galaxy: every star, the lanes between them, and the selection ring on
        /// <paramref name="selectedId"/> (null for none).</summary>
        public void Show(IReadOnlyList<Star> stars, IReadOnlyList<(string A, string B)> lanes, string selectedId)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            _ids.Clear();
            _chartX.Clear();
            _chartY.Clear();
            _selection = null;

            // A faint projection disc behind everything, as on the flight chart.
            float size = _rt.rect.width;
            Centered(Vector2.zero, new Vector2(size - 8f, size - 8f), UiKit.DiscSprite, new Color(0.10f, 0.16f, 0.24f, 0.35f));

            if (stars == null || stars.Count == 0)
            {
                return;
            }

            var xs = new float[stars.Count];
            var ys = new float[stars.Count];
            for (int i = 0; i < stars.Count; i++)
            {
                xs[i] = stars[i].MapX;
                ys[i] = stars[i].MapY;
            }

            // Fit every star (the galaxy grows OUTWARD of home, #1123), centred on the bounding box.
            GalaxyChartLayout.Centre(xs, ys, out _centreX, out _centreY);
            _scale = GalaxyChartLayout.FitScale(_half, xs, ys, _centreX, _centreY, MarkerPad);

            var chart = new Vector2[stars.Count];
            var byId = new Dictionary<string, int>();
            for (int i = 0; i < stars.Count; i++)
            {
                GalaxyChartLayout.ToChart(xs[i], ys[i], _scale, _centreX, _centreY, out float cx, out float cy);
                chart[i] = new Vector2(cx, cy);
                _ids.Add(stars[i].Id);
                _chartX.Add(cx);
                _chartY.Add(cy);
                if (!string.IsNullOrEmpty(stars[i].Id))
                {
                    byId[stars[i].Id] = i;
                }
            }

            // Lanes first so the stars sit on top of them.
            if (lanes != null)
            {
                foreach (var (a, b) in lanes)
                {
                    if (byId.TryGetValue(a, out int ia) && byId.TryGetValue(b, out int ib))
                    {
                        Line(chart[ia], chart[ib], LaneCol, LaneThickness);
                    }
                }
            }

            for (int i = 0; i < stars.Count; i++)
            {
                var s = stars[i];
                var p = chart[i];
                var col = s.Finale ? FinaleCol : s.Color;
                float disc = s.Current ? StarDisc + 4f : StarDisc;
                if (!s.Known && !s.Current)
                {
                    col = Color.Lerp(col, new Color(0.25f, 0.3f, 0.4f), 0.6f); // never entered — dimmed
                }

                // Soft corona, then the backing disc, then the star itself (the flight chart's star look).
                var corona = col;
                corona.a = s.Finale ? 0.28f : 0.16f;
                Centered(p, new Vector2(disc * 2.6f, disc * 2.6f), UiKit.DiscSprite, corona);
                Centered(p, new Vector2(disc + 8f, disc + 8f), UiKit.DiscSprite, BackingCol);
                Centered(p, new Vector2(disc, disc), UiKit.DiscSprite, col);

                if (s.Current)
                {
                    UiOrbitRing.Create(transform, p, new Vector2(disc + 20f, disc + 20f), UiKit.Cyan, 2f);
                    var ship = Centered(p + new Vector2(disc * 0.5f + 14f, disc * 0.5f + 10f), new Vector2(22f, 22f),
                        UiKit.Icon("map_ship") ?? UiKit.SolidSprite, new Color(0.5f, 0.9f, 1f));
                    ship.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
                }

                Label(p + new Vector2(0f, -disc * 0.5f - 13f), s.Label, s.Known || s.Current ? UiKit.TextCol : UnknownLabelCol, 15);
                if (!string.IsNullOrEmpty(s.Players))
                {
                    Label(p + new Vector2(0f, disc * 0.5f + 13f), s.Players, UiKit.Cyan, 13);
                }
            }

            // Selection ring, on top of everything; moved by Select().
            _selection = UiOrbitRing.Create(transform, Vector2.zero, new Vector2(StarDisc + 30f, StarDisc + 30f), Color.white, 2f)
                .GetComponent<RectTransform>();
            Select(selectedId);
        }

        /// <summary>Moves the selection ring onto a star (null hides it).</summary>
        public void Select(string id)
        {
            if (_selection == null)
            {
                return;
            }

            int i = string.IsNullOrEmpty(id) ? -1 : _ids.IndexOf(id);
            _selection.gameObject.SetActive(i >= 0);
            if (i >= 0)
            {
                _selection.anchoredPosition = new Vector2(_chartX[i], _chartY[i]);
            }
        }

        /// <summary>The star under a screen point (within the snap radius), or false. Clicks are measured
        /// from the rect's top-left pivot and re-based onto the centre the markers are anchored to — the
        /// same correction the flight chart needed before its markers became clickable.</summary>
        public bool TryPick(Vector2 screenPoint, out string id)
        {
            id = null;
            if (_rt == null || !RectTransformUtility.RectangleContainsScreenPoint(_rt, screenPoint, null)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screenPoint, null, out var local))
            {
                return false;
            }

            var lp = local - _rt.rect.center;
            int i = GalaxyChartLayout.Pick(_chartX.ToArray(), _chartY.ToArray(), lp.x, lp.y, SnapRadius);
            if (i < 0)
            {
                return false;
            }

            id = _ids[i];
            return true;
        }

        private Image Centered(Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A straight line between two chart points: a rotated solid sprite — there is no line
        /// primitive in the UI kit, and a rotated image is all a lane needs.</summary>
        private void Line(Vector2 a, Vector2 b, Color color, float thickness)
        {
            var d = b - a;
            var img = Centered((a + b) * 0.5f, new Vector2(d.magnitude, thickness), UiKit.SolidSprite, color);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        private void Label(Vector2 pos, string text, Color color, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 20f);
            rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
        }
    }
}
