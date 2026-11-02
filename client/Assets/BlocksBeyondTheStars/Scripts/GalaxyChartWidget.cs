// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hyperspace chart (#1603): a stars-only picture of the galaxy, one disc per star system at its
    /// real star-map position, drawn entirely with uGUI. The flight chart's second tab hosts it, and the travel
    /// screen's Galaxy view (#3130); the cutscene that recalls VEGA's star chart shows it too. The widget only draws
    /// and picks — what a star is called, which colour it has and whether a jump is allowed are decided by the
    /// caller, which knows the player's map state.
    /// <para>Legibility follows the flight chart (#592 lessons): white-on-dark, every star on a dark backing
    /// disc, names below the discs, no hairlines. The current system wears a cyan ring, a selected star a
    /// white one; jump lanes (#1125) are thin cyan lines between their two stars; unknown systems (#1113)
    /// are dimmed and labelled by the caller (a bare "?"). The finale system, once revealed, glows in the
    /// hyperspace violet the jump button already uses.</para>
    /// <para><b>Zoom (#3129).</b> A random galaxy has no minimum star spacing and a grown one pushes new stars far out, so
    /// at 32+ systems the fitted chart crowds. <see cref="EnableInteraction"/> turns on the mouse wheel (zoom about the
    /// cursor), dragging (pan) and a − / + / centre / all button row for pad and touch. Zoom and pan only move the
    /// markers apart — discs and labels keep their size — and labels that would cover a more important one are hidden
    /// until the stars spread (<see cref="GalaxyChartLayout.LabelsShown"/>). The math lives in Shared
    /// (<see cref="GalaxyChartLayout"/>) so the headless suite pins it.</para>
    /// </summary>
    public sealed partial class GalaxyChartWidget : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
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
        private const float RouteThickness = 4f;
        private const float ZigAmplitude = 5f;      // a wormhole's zigzag, chart units (constant at any zoom)
        private const float WheelStep = 1.25f;      // zoom factor per mouse-wheel notch
        private const float ButtonStep = 1.6f;      // zoom factor per − / + press
        private const float CentreZoom = 2.5f;      // "centre" zooms at least this far in, so it visibly does something
        private const float MinSeparation = StarDisc * 1.5f; // the closest pair stands this far apart at the zoom limit

        private static readonly Color BackingCol = new Color(0.01f, 0.03f, 0.07f, 0.78f);
        private static readonly Color LaneCol = new Color(0.4f, 0.85f, 1f, 0.55f);
        private static readonly Color WormholeCol = new Color(0.78f, 0.5f, 1f, 0.85f); // #2242: a known wormhole pair
        private static readonly Color FinaleCol = new Color(0.62f, 0.42f, 0.9f);
        private static readonly Color UnknownLabelCol = new Color(0.30f, 0.55f, 0.72f, 0.7f);

        /// <summary>A star was clicked (not dragged) — its id.</summary>
        public event System.Action<string> StarClicked;

        /// <summary>The zoom or the pan changed (the caller may keep <see cref="View"/> across a rebuild).</summary>
        public event System.Action ViewChanged;

        private RectTransform _rt;
        private RectTransform _view; // the clipped layer every marker, line and label lives in
        private float _half;
        private float _scale;       // the fit scale: map units → chart units at zoom 1
        private float _minX, _maxX, _minY, _maxY;
        private float _maxZoom = 1f;
        private bool _interactive;
        private GalaxyChartView _viewState;
        private bool _hasView;
        private int _selectedIndex = -1;
        private readonly List<string> _ids = new();
        private readonly List<float> _mapX = new();
        private readonly List<float> _mapY = new();
        private readonly List<float> _chartX = new();
        private readonly List<float> _chartY = new();
        private readonly List<StarMarks> _marks = new();
        private readonly List<LineMark> _lines = new();
        private RectTransform _selection;

        /// <summary>The look at the chart: zoom and the map point in the middle. Setting it clamps and redraws; a caller
        /// that rebuilds its page sets it back after <see cref="Show"/> to keep the player's zoom.</summary>
        public GalaxyChartView View
        {
            get => _viewState;
            set
            {
                _viewState = value;
                _hasView = true;
                if (_ids.Count > 0)
                {
                    ApplyView(notify: false);
                }
            }
        }

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

            // Everything that moves with the zoom sits in a clipped layer, so a zoomed-in chart never spills over the page.
            var viewGo = new GameObject("View", typeof(RectTransform), typeof(RectMask2D));
            viewGo.transform.SetParent(go.transform, false);
            var view = viewGo.GetComponent<RectTransform>();
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(4f, 4f);
            view.offsetMax = new Vector2(-4f, -4f);
            widget._view = view;
            return widget;
        }

        /// <summary>Turns on zoom and pan (#3129): the chart takes the wheel, drags and clicks (a click on a star raises
        /// <see cref="StarClicked"/>), and gets its − / + / centre / all buttons. The cutscene's chart stays a picture.</summary>
        public void EnableInteraction(System.Func<string, string> localize)
        {
            if (_interactive)
            {
                return;
            }

            _interactive = true;
            GetComponent<Image>().raycastTarget = true; // the background catches wheel, drag and click
            BuildZoomButtons(localize ?? (k => k));
        }

        /// <summary>(Re)draws the galaxy: every star, the lanes between them, the wormhole pairs the player knows
        /// (#2242), and the selection ring on <paramref name="selectedId"/> (null for none). #2474: <paramref name="routeFrom"/>
        /// and <paramref name="routeTo"/> (both set) draw a bold violet line between two stars — VEGA's chapter 7 scene draws
        /// it from the player's system to the newly revealed Guardian system. A zoom the player set survives a redraw.</summary>
        public void Show(IReadOnlyList<Star> stars, IReadOnlyList<(string A, string B)> lanes, string selectedId,
            IReadOnlyList<(string A, string B)> wormholes = null, string routeFrom = null, string routeTo = null)
        {
            for (int i = _view.childCount - 1; i >= 0; i--)
            {
                Destroy(_view.GetChild(i).gameObject);
            }

            _ids.Clear();
            _mapX.Clear();
            _mapY.Clear();
            _chartX.Clear();
            _chartY.Clear();
            _marks.Clear();
            _lines.Clear();
            _selection = null;
            _selectedIndex = -1;

            // A faint projection disc behind everything, as on the flight chart.
            float size = _rt.rect.width;
            Centered(Vector2.zero, new Vector2(size - 8f, size - 8f), UiKit.DiscSprite, new Color(0.10f, 0.16f, 0.24f, 0.35f));

            if (stars == null || stars.Count == 0)
            {
                return;
            }

            Fit(stars);
            var byId = new Dictionary<string, int>();
            for (int i = 0; i < stars.Count; i++)
            {
                if (!string.IsNullOrEmpty(stars[i].Id))
                {
                    byId[stars[i].Id] = i;
                }
            }

            AddLanes(lanes, byId);
            AddWormholes(wormholes, byId);
            if (!string.IsNullOrEmpty(routeFrom) && !string.IsNullOrEmpty(routeTo)
                && byId.TryGetValue(routeFrom, out int ra) && byId.TryGetValue(routeTo, out int rb))
            {
                _lines.Add(new LineMark(ra, rb, 0f, 0f, 1f, 0f, NewLine(FinaleCol, RouteThickness))); // #2474: over the lanes, under the stars
            }

            for (int i = 0; i < stars.Count; i++)
            {
                _marks.Add(BuildStar(stars[i]));
            }

            // Selection ring, on top of everything; moved by Select().
            _selection = UiOrbitRing.Create(_view, Vector2.zero, new Vector2(StarDisc + 30f, StarDisc + 30f), Color.white, 2f)
                .GetComponent<RectTransform>();
            if (!_hasView)
            {
                _viewState = new GalaxyChartView(1f, (_minX + _maxX) * 0.5f, (_minY + _maxY) * 0.5f);
                _hasView = true;
            }

            ApplyView(notify: false);
            Select(selectedId);
        }

        /// <summary>The fit of the whole galaxy (the galaxy grows OUTWARD of home, #1123, so it is centred on the bounding
        /// box) and how far in the chart may zoom.</summary>
        private void Fit(IReadOnlyList<Star> stars)
        {
            var xs = new float[stars.Count];
            var ys = new float[stars.Count];
            for (int i = 0; i < stars.Count; i++)
            {
                xs[i] = stars[i].MapX;
                ys[i] = stars[i].MapY;
                _ids.Add(stars[i].Id);
                _mapX.Add(xs[i]);
                _mapY.Add(ys[i]);
                _chartX.Add(0f);
                _chartY.Add(0f);
            }

            GalaxyChartLayout.Centre(xs, ys, out float centreX, out float centreY);
            GalaxyChartLayout.Bounds(xs, ys, out _minX, out _maxX, out _minY, out _maxY);
            _scale = GalaxyChartLayout.FitScale(_half, xs, ys, centreX, centreY, MarkerPad);
            _maxZoom = GalaxyChartLayout.MaxZoom(xs, ys, _scale, MinSeparation);
        }

        private void AddLanes(IReadOnlyList<(string A, string B)> lanes, Dictionary<string, int> byId)
        {
            if (lanes == null)
            {
                return;
            }

            foreach (var (a, b) in lanes)
            {
                if (byId.TryGetValue(a, out int ia) && byId.TryGetValue(b, out int ib))
                {
                    _lines.Add(new LineMark(ia, ib, 0f, 0f, 1f, 0f, NewLine(LaneCol, LaneThickness)));
                }
            }
        }

        /// <summary>#2242: the wormhole pairs this player knows — a violet zigzag, like the crack it is, not a straight lane.</summary>
        private void AddWormholes(IReadOnlyList<(string A, string B)> wormholes, Dictionary<string, int> byId)
        {
            if (wormholes == null)
            {
                return;
            }

            const int zigs = 8;
            foreach (var (a, b) in wormholes)
            {
                if (!byId.TryGetValue(a, out int wa) || !byId.TryGetValue(b, out int wb))
                {
                    continue;
                }

                float prevS = 0f;
                for (int z = 1; z <= zigs; z++)
                {
                    float s = z < zigs ? ZigAmplitude * (z % 2 == 0 ? 1f : -1f) : 0f;
                    _lines.Add(new LineMark(wa, wb, (z - 1) / (float)zigs, prevS, z / (float)zigs, s, NewLine(WormholeCol, LaneThickness)));
                    prevS = s;
                }
            }
        }

        /// <summary>One star's marks: soft corona, backing disc, the star itself (the flight chart's star look), the cyan
        /// ring and ship of the current system, its label below and the other players' names above.</summary>
        private StarMarks BuildStar(Star s)
        {
            var col = s.Finale ? FinaleCol : s.Color;
            float disc = s.Current ? StarDisc + 4f : StarDisc;
            if (!s.Known && !s.Current)
            {
                col = Color.Lerp(col, new Color(0.25f, 0.3f, 0.4f), 0.6f); // never entered — dimmed
            }

            var marks = new StarMarks();
            var corona = col;
            corona.a = s.Finale ? 0.28f : 0.16f;
            marks.Add(Centered(Vector2.zero, new Vector2(disc * 2.6f, disc * 2.6f), UiKit.DiscSprite, corona).rectTransform, Vector2.zero);
            marks.Add(Centered(Vector2.zero, new Vector2(disc + 8f, disc + 8f), UiKit.DiscSprite, BackingCol).rectTransform, Vector2.zero);
            marks.Add(Centered(Vector2.zero, new Vector2(disc, disc), UiKit.DiscSprite, col).rectTransform, Vector2.zero);

            if (s.Current)
            {
                marks.Add(UiOrbitRing.Create(_view, Vector2.zero, new Vector2(disc + 20f, disc + 20f), UiKit.Cyan, 2f).GetComponent<RectTransform>(), Vector2.zero);
                var shipOffset = new Vector2(disc * 0.5f + 14f, disc * 0.5f + 10f);
                var ship = Centered(Vector2.zero, new Vector2(22f, 22f), UiKit.Icon("map_ship") ?? UiKit.SolidSprite, new Color(0.5f, 0.9f, 1f));
                ship.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
                marks.Add(ship.rectTransform, shipOffset);
            }

            marks.Priority = s.Current ? 0 : s.Known ? 3 : 4;
            marks.Label = Label(s.Label, s.Known || s.Current ? UiKit.TextCol : UnknownLabelCol, 15);
            marks.LabelOffset = new Vector2(0f, -disc * 0.5f - 13f);
            if (!string.IsNullOrEmpty(s.Players))
            {
                marks.Players = Label(s.Players, UiKit.Cyan, 13);
                marks.PlayersOffset = new Vector2(0f, disc * 0.5f + 13f);
                marks.Priority = System.Math.Min(marks.Priority, 2); // a friend out there is worth a label
            }

            return marks;
        }

        /// <summary>Moves the selection ring onto a star (null hides it).</summary>
        public void Select(string id)
        {
            _selectedIndex = string.IsNullOrEmpty(id) ? -1 : _ids.IndexOf(id);
            PlaceSelection();
            if (_ids.Count > 0)
            {
                ApplyLabels(); // the selection's label wins its place
            }
        }

        private void PlaceSelection()
        {
            if (_selection == null)
            {
                return;
            }

            _selection.gameObject.SetActive(_selectedIndex >= 0);
            if (_selectedIndex >= 0)
            {
                _selection.anchoredPosition = new Vector2(_chartX[_selectedIndex], _chartY[_selectedIndex]);
            }
        }

        /// <summary>The star under a screen point (within the snap radius), or false. Clicks are measured
        /// from the rect's top-left pivot and re-based onto the centre the markers are anchored to — the
        /// same correction the flight chart needed before its markers became clickable.</summary>
        public bool TryPick(Vector2 screenPoint, out string id) => TryPick(screenPoint, null, out id);

        private bool TryPick(Vector2 screenPoint, Camera eventCamera, out string id)
        {
            id = null;
            if (!TryLocal(screenPoint, eventCamera, out var lp))
            {
                return false;
            }

            int i = GalaxyChartLayout.Pick(_chartX.ToArray(), _chartY.ToArray(), lp.x, lp.y, SnapRadius);
            if (i < 0)
            {
                return false;
            }

            id = _ids[i];
            return true;
        }

        /// <summary>A screen point in the markers' frame (chart units from the chart centre); false outside the chart.</summary>
        private bool TryLocal(Vector2 screenPoint, Camera eventCamera, out Vector2 local)
        {
            local = Vector2.zero;
            if (_rt == null || !RectTransformUtility.RectangleContainsScreenPoint(_rt, screenPoint, eventCamera)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screenPoint, eventCamera, out var lp))
            {
                return false;
            }

            local = lp - _rt.rect.center;
            return true;
        }

        private Image Centered(Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(_view, false);
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

        /// <summary>A straight line segment: a rotated solid sprite — there is no line primitive in the UI kit. Its ends
        /// are set by <see cref="ApplyView"/>.</summary>
        private RectTransform NewLine(Color color, float thickness)
        {
            var img = Centered(Vector2.zero, new Vector2(1f, thickness), UiKit.SolidSprite, color);
            return img.rectTransform;
        }

        private Text Label(string text, Color color, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(_view, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 20f);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }
    }
}
