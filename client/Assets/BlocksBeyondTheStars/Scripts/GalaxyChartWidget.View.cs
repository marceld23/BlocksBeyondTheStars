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
    /// #3129: the hyperspace chart's zoom and pan — the cached marks are only moved (no rebuild per wheel notch, which
    /// would churn the browser's GC), the input handlers, the button row and the label de-overlap.
    /// </summary>
    public sealed partial class GalaxyChartWidget
    {
        /// <summary>The marks of one star, each with its offset from the star's chart position.</summary>
        private sealed class StarMarks
        {
            public readonly List<(RectTransform Rt, Vector2 Offset)> Parts = new();
            public Text Label;
            public Vector2 LabelOffset;
            public Text Players;
            public Vector2 PlayersOffset;
            public int Priority; // 0 = most important label

            public void Add(RectTransform rt, Vector2 offset) => Parts.Add((rt, offset));
        }

        /// <summary>A line segment between two stars: from <c>T0</c> to <c>T1</c> along the line A→B, pushed sideways by
        /// <c>S0</c>/<c>S1</c> chart units (a wormhole's zigzag; 0 for a lane).</summary>
        private readonly struct LineMark
        {
            public LineMark(int a, int b, float t0, float s0, float t1, float s1, RectTransform rt)
            {
                A = a;
                B = b;
                T0 = t0;
                S0 = s0;
                T1 = t1;
                S1 = s1;
                Rt = rt;
            }

            public int A { get; }

            public int B { get; }

            public float T0 { get; }

            public float S0 { get; }

            public float T1 { get; }

            public float S1 { get; }

            public RectTransform Rt { get; }
        }

        private Vector2 _dragLast;
        private bool _dragging;
        private bool _forwardDrag; // this drag belongs to the page's scroll view (the chart is fully zoomed out)

        /// <summary>Clamps the view and moves every mark to it.</summary>
        private void ApplyView(bool notify)
        {
            _viewState = GalaxyChartLayout.Clamp(_viewState, _maxZoom, _minX, _maxX, _minY, _maxY);
            for (int i = 0; i < _ids.Count; i++)
            {
                GalaxyChartLayout.ToChart(_mapX[i], _mapY[i], _scale, _viewState, out float cx, out float cy);
                _chartX[i] = cx;
                _chartY[i] = cy;
                if (i < _marks.Count)
                {
                    var p = new Vector2(cx, cy);
                    foreach (var (rt, offset) in _marks[i].Parts)
                    {
                        rt.anchoredPosition = p + offset;
                    }
                }
            }

            foreach (var line in _lines)
            {
                PlaceLine(line);
            }

            PlaceSelection();
            ApplyLabels();
            if (notify)
            {
                ViewChanged?.Invoke();
            }
        }

        private void PlaceLine(LineMark line)
        {
            var a = new Vector2(_chartX[line.A], _chartY[line.A]);
            var b = new Vector2(_chartX[line.B], _chartY[line.B]);
            var d = b - a;
            var perp = d.sqrMagnitude > 1e-6f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;
            var p0 = a + d * line.T0 + perp * line.S0;
            var p1 = a + d * line.T1 + perp * line.S1;
            var seg = p1 - p0;
            line.Rt.anchoredPosition = (p0 + p1) * 0.5f;
            line.Rt.sizeDelta = new Vector2(seg.magnitude, line.Rt.sizeDelta.y);
            line.Rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(seg.y, seg.x) * Mathf.Rad2Deg);
        }

        /// <summary>Shows the labels that fit (#3129): the current system first, then the selection, other players, known
        /// systems and the unknown "?" — a label that would cover a more important one waits until the stars spread.</summary>
        private void ApplyLabels()
        {
            int n = _marks.Count;
            if (n == 0)
            {
                return;
            }

            // Two boxes per star (name below, players above): index 2i = name, 2i + 1 = players.
            var cx = new float[n * 2];
            var cy = new float[n * 2];
            var w = new float[n * 2];
            var h = new float[n * 2];
            var order = new List<int>(n * 2);
            for (int i = 0; i < n; i++)
            {
                var m = _marks[i];
                var p = new Vector2(_chartX[i], _chartY[i]);
                SetBox(m.Label, p + m.LabelOffset, 2 * i, cx, cy, w, h);
                SetBox(m.Players, p + m.PlayersOffset, 2 * i + 1, cx, cy, w, h);
            }

            for (int rank = 0; rank <= 4; rank++)
            {
                for (int i = 0; i < n; i++)
                {
                    int priority = i == _selectedIndex ? System.Math.Min(_marks[i].Priority, 1) : _marks[i].Priority;
                    if (priority == rank)
                    {
                        order.Add(2 * i);
                        order.Add(2 * i + 1);
                    }
                }
            }

            var shown = GalaxyChartLayout.LabelsShown(cx, cy, w, h, order.ToArray());
            for (int i = 0; i < n; i++)
            {
                ShowLabel(_marks[i].Label, shown[2 * i], new Vector2(cx[2 * i], cy[2 * i]));
                ShowLabel(_marks[i].Players, shown[2 * i + 1], new Vector2(cx[2 * i + 1], cy[2 * i + 1]));
            }
        }

        private static void SetBox(Text t, Vector2 pos, int k, float[] cx, float[] cy, float[] w, float[] h)
        {
            cx[k] = pos.x;
            cy[k] = pos.y;
            if (t == null || string.IsNullOrEmpty(t.text))
            {
                return; // width 0 = no box
            }

            w[k] = t.preferredWidth;
            h[k] = t.fontSize + 2f;
        }

        private static void ShowLabel(Text t, bool shown, Vector2 pos)
        {
            if (t == null)
            {
                return;
            }

            t.rectTransform.anchoredPosition = pos;
            t.enabled = shown;
        }

        // ---- input ----
        // The chart sits in the travel screen's scrolling detail pane: a wheel notch or a drag it cannot use (fully zoomed
        // out there is nothing to pan, and nothing further out to zoom to) goes on to the pane, so the page still scrolls
        // with the pointer over the chart — on touch the drag is the only way to scroll.

        public void OnScroll(PointerEventData e)
        {
            if (!_interactive || _ids.Count == 0 || !TryLocal(e.position, e.enterEventCamera, out var lp))
            {
                return;
            }

            float factor = e.scrollDelta.y > 0f ? WheelStep : e.scrollDelta.y < 0f ? 1f / WheelStep : 1f;
            float zoom = Mathf.Clamp(_viewState.Zoom * factor, 1f, _maxZoom);
            if (Mathf.Approximately(zoom, _viewState.Zoom))
            {
                PassOn(e, ExecuteEvents.scrollHandler);
                return;
            }

            ZoomAbout(zoom, lp);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _forwardDrag = _interactive && _viewState.Zoom <= 1.0001f; // the whole galaxy shows: nothing to pan
            if (_forwardDrag)
            {
                PassOn(e, ExecuteEvents.initializePotentialDrag);
                PassOn(e, ExecuteEvents.beginDragHandler);
                return;
            }

            _dragging = _interactive && TryLocal(e.position, e.pressEventCamera, out _dragLast);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_forwardDrag)
            {
                PassOn(e, ExecuteEvents.dragHandler);
                return;
            }

            if (!_dragging || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var lp))
            {
                return;
            }

            var local = lp - _rt.rect.center;
            var delta = local - _dragLast;
            _dragLast = local;
            _viewState = GalaxyChartLayout.Pan(_viewState, delta.x, delta.y, _scale);
            ApplyView(notify: true);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_forwardDrag)
            {
                PassOn(e, ExecuteEvents.endDragHandler);
            }

            _forwardDrag = false;
            _dragging = false;
        }

        /// <summary>A click that did not turn into a drag picks the star under it. uGUI still sends the click after a
        /// drag that started and ended on the chart (press and drag target are the same object) — and before the drag
        /// ends — so a drag released over a star must not pick it.</summary>
        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging || _dragging || _forwardDrag)
            {
                return;
            }

            if (_interactive && e.button == PointerEventData.InputButton.Left && TryPick(e.position, e.pressEventCamera, out var id))
            {
                StarClicked?.Invoke(id);
            }
        }

        /// <summary>Hands an event the chart does not use to the nearest handler above it (the scroll view of a page).</summary>
        private void PassOn<T>(PointerEventData e, ExecuteEvents.EventFunction<T> handler)
            where T : IEventSystemHandler
        {
            if (transform.parent != null)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, handler);
            }
        }

        private void ZoomAbout(float zoom, Vector2 about)
        {
            zoom = Mathf.Clamp(zoom, 1f, _maxZoom);
            _viewState = GalaxyChartLayout.ZoomAbout(_viewState, zoom, about.x, about.y, _scale);
            ApplyView(notify: true);
        }

        /// <summary>The button row, bottom right inside the chart: − / + zoom about the middle, "centre" puts the
        /// selected (or the current) star in the middle and zooms in a little, "all" shows the whole galaxy again.
        /// Real buttons, so a pad reaches them by stick and touch by tap.</summary>
        private void BuildZoomButtons(System.Func<string, string> localize)
        {
            float size = _rt.rect.width;
            const float h = 40f, gap = 6f, small = 48f, wide = 104f;
            float y = size - h - 10f;
            float x = size - (small * 2f + wide * 2f + gap * 3f) - 10f;
            AddButton(x, y, small, h, "−", () => ZoomAbout(_viewState.Zoom / ButtonStep, Vector2.zero));
            x += small + gap;
            AddButton(x, y, small, h, "+", () => ZoomAbout(_viewState.Zoom * ButtonStep, Vector2.zero));
            x += small + gap;
            AddButton(x, y, wide, h, localize("ui.chart.centre"), CentreOnFocus);
            x += wide + gap;
            AddButton(x, y, wide, h, localize("ui.chart.all"), () =>
            {
                _viewState = new GalaxyChartView(1f, (_minX + _maxX) * 0.5f, (_minY + _maxY) * 0.5f);
                ApplyView(notify: true);
            });
        }

        private void AddButton(float x, float y, float w, float h, string label, System.Action onClick)
        {
            var b = UiKit.AddButton(transform, x, y, w, h, label, onClick);
            UiKit.FitLabel(b.GetComponentInChildren<Text>(), 12, 18);
        }

        /// <summary>Puts the selected star — or, with none, the current one — in the middle, zoomed in at least a little.</summary>
        private void CentreOnFocus()
        {
            int i = _selectedIndex;
            if (i < 0)
            {
                for (int k = 0; k < _marks.Count && i < 0; k++)
                {
                    if (_marks[k].Priority == 0)
                    {
                        i = k;
                    }
                }
            }

            if (i < 0)
            {
                return;
            }

            _viewState = new GalaxyChartView(Mathf.Max(_viewState.Zoom, Mathf.Min(CentreZoom, _maxZoom)), _mapX[i], _mapY[i]);
            ApplyView(notify: true);
        }
    }
}
