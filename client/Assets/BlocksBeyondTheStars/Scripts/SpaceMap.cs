// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight system chart (#597): a top-down map of the CURRENT system opened while cruising
    /// (FlightMap action, default M) — the space sibling of the surface world map (M, #592). Drawn from
    /// the REAL flight coordinates (<see cref="SpaceView.Landables"/> + the live space entities), i.e.
    /// exactly the positions the ship flies between, with the ship marker + heading in the middle of it.
    /// Clicking a body/station marker targets it; clicking empty space sets a free waypoint there. The
    /// waypoint shows on the space radar with a distance readout, and the VEGA autopilot steers to it.
    /// Opening takes menu ownership (the cursor arbiter frees the mouse and the ship holds position, as
    /// with the Tab travel screen); Esc or M closes.
    /// </summary>
    public sealed class SpaceMap : MonoBehaviour
    {
        public GameBootstrap Game;
        public SpaceView SpaceView;
        public Camera Camera;

        private const float ChartSize = 900f;     // square chart area, canvas units (1920×1080 reference)
        private const float ChartHalf = ChartSize * 0.5f - 30f; // usable radius: margin keeps rim markers inside
        private const float SnapRadius = 26f;     // click-snap distance to a marker, canvas units

        private static readonly Color WaypointCol = new Color(1f, 0.85f, 0.3f);
        private static readonly Color DiscCol = new Color(0.01f, 0.03f, 0.07f, 0.78f); // WorldMap's backing disc

        private bool _open;
        private int _openedFrame = -1; // frame Open() ran — the SAME key-down must not instantly close it
        private Canvas _canvas;
        private RectTransform _chart;
        private Text _info;
        private RectTransform _ship;
        private RectTransform _waypoint;
        private float _scale; // scene units → chart canvas units
        private readonly List<(string Id, Vector2 Chart, string Name)> _targets = new(); // snap candidates
        private readonly List<Image> _entityBlips = new();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (_open && Input.GetKeyDown(KeyCode.Escape) && !Game.ChatTyping)
            {
                Game.MarkMenuInputHandled(); // consumed — don't also pop the quit prompt (#413)
                Close();
                return;
            }

            // Toggle-close on the map key — but not on the very key-down that opened it: SpaceView's
            // UpdateCruise runs earlier in the same frame, so without the frame guard the one press
            // opened the chart there and closed it here before it ever rendered.
            if (_open && Time.frameCount != _openedFrame && InputMap.Down(InputAction.FlightMap) && !Game.ChatTyping)
            {
                Close();
                return;
            }

            // Leaving space (landing/boarding finished a transition) with the chart still up: close it —
            // its coordinates belong to the flight scene.
            if (_open && !Game.InSpace)
            {
                Close();
                return;
            }

            if (_open)
            {
                RefreshDynamic();
                HandleClick();
            }
        }

        /// <summary>Opened by <see cref="SpaceView"/> on the FlightMap action while cruising.</summary>
        public void Open()
        {
            if (_open)
            {
                return;
            }

            _open = true;
            _openedFrame = Time.frameCount;
            Game.SetMenuOwner(this, true); // frees the cursor; UpdateCruise holds the ship while set (#413)
            Build();
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Game.SetMenuOwner(this, false);
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
        }

        private void OnDestroy()
        {
            if (_open)
            {
                Game?.SetMenuOwner(this, false);
            }
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;

        private void Build()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            _targets.Clear();
            _entityBlips.Clear();

            _canvas = UiKit.CreateCanvas("SpaceMapUI");
            _canvas.sortingOrder = 60; // above the flight HUD, like the surface map
            var root = _canvas.transform;

            UiKit.AddImage(root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.92f));
            string sysName = CurrentSystemName();
            UiKit.AddLogo(root, 40, 24, 700, 40, L("ui.spacemap.title").ToUpperInvariant() + (string.IsNullOrEmpty(sysName) ? string.Empty : "  —  " + sysName), 26);

            // Square chart on the left; a faint projection disc gives it the orrery look.
            const float ax = 40f, ay = 100f;
            UiKit.AddPanel(root, ax - 6, ay - 6, ChartSize + 12, ChartSize + 12, UiKit.Panel);
            var chartGo = new GameObject("Chart", typeof(RectTransform));
            chartGo.transform.SetParent(root, false);
            UiKit.Place(chartGo, ax, ay, ChartSize, ChartSize);
            var bg = chartGo.AddComponent<Image>();
            bg.sprite = UiKit.SolidSprite;
            bg.color = new Color(0.01f, 0.02f, 0.05f, 0.9f);
            bg.raycastTarget = false;
            _chart = chartGo.GetComponent<RectTransform>();
            Centered(_chart, Vector2.zero, new Vector2(ChartSize - 8f, ChartSize - 8f), UiKit.DiscSprite, new Color(0.10f, 0.16f, 0.24f, 0.35f));

            // Scale: fit every body (plus its rendered radius) and the ship into the chart. The star may
            // sit far outside — it rim-pins rather than dictating the scale.
            float extent = 60f;
            var landables = SpaceView != null ? SpaceView.Landables : null;
            if (landables != null)
            {
                foreach (var b in landables)
                {
                    extent = Mathf.Max(extent, new Vector2(b.Pos.x, b.Pos.z).magnitude + b.Radius);
                }
            }

            if (Game.Space != null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    if (e.Kind == "SpaceStation")
                    {
                        extent = Mathf.Max(extent, new Vector2(e.X, e.Z).magnitude);
                    }
                }
            }

            if (Camera != null)
            {
                extent = Mathf.Max(extent, new Vector2(Camera.transform.localPosition.x, Camera.transform.localPosition.z).magnitude);
            }

            _scale = ChartHalf / (extent * 1.05f);

            // The star (rim-pinned when outside the chart).
            if (SpaceView != null && SpaceView.HasStar)
            {
                var sp = ToChart(SpaceView.StarPosition);
                if (sp.magnitude > ChartHalf)
                {
                    sp = sp.normalized * ChartHalf;
                }

                Centered(_chart, sp, new Vector2(26f, 26f), UiKit.DiscSprite, new Color(1f, 0.86f, 0.5f));
            }

            // Bodies: a coloured disc on a dark backing, radius-proportional, named — the same legibility
            // rules as the reworked surface map (#592: white-on-dark, no thin cyan hairlines).
            if (landables != null)
            {
                foreach (var b in landables)
                {
                    var p = ToChart(b.Pos);
                    float size = Mathf.Clamp(b.Radius * _scale * 2f, 12f, 46f);
                    var col = BodyColor(b.Id);
                    Centered(_chart, p, new Vector2(size + 8f, size + 8f), UiKit.DiscSprite, DiscCol);
                    Centered(_chart, p, new Vector2(size, size), UiKit.DiscSprite, col);
                    Label(_chart, p + new Vector2(0f, -size * 0.5f - 12f), b.Name);
                    _targets.Add((string.IsNullOrEmpty(b.Id) ? SpaceView.HomeWaypointId : b.Id, p, b.Name));
                }
            }

            // Ship marker (updated live), on top of the bodies.
            _ship = Centered(_chart, Vector2.zero, new Vector2(30f, 30f), UiKit.Icon("map_ship") ?? UiKit.SolidSprite, new Color(0.5f, 0.9f, 1f)).rectTransform;

            // Waypoint marker (shown when set).
            _waypoint = Centered(_chart, Vector2.zero, new Vector2(34f, 34f), UiKit.Icon("map_waypoint") ?? UiKit.SolidSprite, WaypointCol).rectTransform;
            _waypoint.gameObject.SetActive(false);

            // Info panel on the right.
            UiKit.AddPanel(root, 980, 100, 900, 900, UiKit.Panel);
            float ix = 1010f;
            UiKit.AddText(root, ix, 130, 840, 28, L("ui.spacemap.waypoint"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _info = UiKit.AddText(root, ix, 170, 840, 620, string.Empty, 20, UiKit.TextCol, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Truncate;

            UiKit.AddText(root, ix, 800, 840, 60, L("ui.spacemap.click_hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);

            UiKit.AddButton(root, ix, 880, 300, 50, L("ui.map.clear_waypoint"), () =>
            {
                Game.SpaceWaypointId = null;
                Game.SpaceWaypointPos = null;
                RefreshInfo();
            });
            UiKit.AddButton(root, ix + 340, 880, 200, 50, L("ui.action.close"), Close);

            RefreshDynamic();
            RefreshInfo();
        }

        /// <summary>Live bits: the ship marker (with heading), the waypoint marker, and the space-entity
        /// blips (stations cyan, hostiles red — matching the radar's colour language).</summary>
        private void RefreshDynamic()
        {
            if (_chart == null || Camera == null)
            {
                return;
            }

            var camPos = Camera.transform.localPosition;
            _ship.anchoredPosition = Clamp(ToChart(camPos));
            var f = Camera.transform.forward;
            _ship.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);

            int i = 0;
            if (Game.Space != null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    bool station = e.Kind == "SpaceStation";
                    bool wreck = e.Kind == "Wreck";
                    if (!station && !wreck && !e.Hostile)
                    {
                        continue; // asteroids/drops would dust the chart — radar range covers those
                    }

                    var blip = EntityBlip(i++);
                    blip.rectTransform.anchoredPosition = Clamp(ToChart(new Vector3(e.X, e.Y, e.Z)));
                    blip.rectTransform.sizeDelta = station ? new Vector2(14f, 14f) : new Vector2(9f, 9f);
                    blip.color = station ? new Color(0.4f, 0.85f, 1f)
                        : e.Hostile ? new Color(1f, 0.35f, 0.35f)
                        : new Color(0.8f, 0.8f, 0.9f);
                    blip.gameObject.SetActive(true);
                }
            }

            for (; i < _entityBlips.Count; i++)
            {
                if (_entityBlips[i].gameObject.activeSelf)
                {
                    _entityBlips[i].gameObject.SetActive(false);
                }
            }

            bool haveWp = SpaceView != null && SpaceView.TryResolveSpaceWaypoint(out var wp, out _);
            if (_waypoint.gameObject.activeSelf != haveWp)
            {
                _waypoint.gameObject.SetActive(haveWp);
            }

            if (haveWp)
            {
                SpaceView.TryResolveSpaceWaypoint(out var wpPos, out _);
                _waypoint.anchoredPosition = Clamp(ToChart(wpPos));
            }
        }

        private void HandleClick()
        {
            if (!Input.GetMouseButtonDown(0) || _chart == null)
            {
                return;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(_chart, Input.mousePosition, null)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_chart, Input.mousePosition, null, out var lp))
            {
                return;
            }

            // Snap to the nearest body/station marker; otherwise a free waypoint at the clicked spot,
            // clamped into the flight volume so the autopilot can actually reach it.
            string snapId = null;
            string snapName = null;
            float bestSq = SnapRadius * SnapRadius;
            foreach (var t in _targets)
            {
                float sq = (t.Chart - lp).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    snapId = t.Id;
                    snapName = t.Name;
                }
            }

            if (Game.Space != null && snapId == null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    float sq = (ToChart(new Vector3(e.X, e.Y, e.Z)) - lp).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        snapId = e.Id;
                        snapName = e.Name;
                    }
                }
            }

            if (snapId != null)
            {
                Game.SpaceWaypointId = snapId;
                Game.SpaceWaypointPos = null;
            }
            else
            {
                var scene = new Vector3(lp.x / _scale, 0f, lp.y / _scale);
                float bounds = SpaceView != null ? SpaceView.FlightBounds : 130f;
                if (scene.magnitude > bounds)
                {
                    scene = scene.normalized * bounds;
                }

                Game.SpaceWaypointId = null;
                Game.SpaceWaypointPos = scene;
            }

            _ = snapName; // name shows via RefreshInfo's resolution
            RefreshInfo();
        }

        private void RefreshInfo()
        {
            if (_info == null)
            {
                return;
            }

            if (SpaceView != null && SpaceView.TryResolveSpaceWaypoint(out var wp, out _))
            {
                float dist = Camera != null ? Vector3.Distance(Camera.transform.localPosition, wp) : 0f;
                string name = WaypointName();
                _info.text = (string.IsNullOrEmpty(name) ? L("ui.spacemap.free_point") : name)
                    + "\n" + string.Format(L("ui.spacemap.distance_fmt"), Mathf.RoundToInt(dist))
                    + "\n\n" + L(Game.AiCoreTier >= 2 ? "ui.spacemap.autopilot_ready" : "ui.spacemap.autopilot_needs_core");
            }
            else
            {
                _info.text = L("ui.spacemap.none");
            }
        }

        /// <summary>Display name for the current snap waypoint (empty for a free point).</summary>
        private string WaypointName()
        {
            string id = Game.SpaceWaypointId;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var t in _targets)
            {
                if (t.Id == id)
                {
                    return t.Name;
                }
            }

            if (Game.Space != null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    if (e.Id == id)
                    {
                        return e.Name;
                    }
                }
            }

            return null;
        }

        private string CurrentSystemName()
        {
            var map = Game?.StarMap;
            if (map?.Systems == null)
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == map.ActiveLocationId)
                    {
                        return sys.Name;
                    }
                }
            }

            return null;
        }

        private Color BodyColor(string id)
        {
            var map = Game?.StarMap;
            if (!string.IsNullOrEmpty(id) && map?.Systems != null)
            {
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == id)
                        {
                            return SystemMapWidget.PlanetColor(b.PlanetType, b.Kind);
                        }
                    }
                }
            }

            return new Color(0.6f, 0.85f, 0.7f); // the launch body (empty id) / unknown
        }

        private Vector2 ToChart(Vector3 scene) => new Vector2(scene.x, scene.z) * _scale;

        private static Vector2 Clamp(Vector2 p) => p.magnitude > ChartHalf ? p.normalized * ChartHalf : p;

        private Image EntityBlip(int index)
        {
            while (index >= _entityBlips.Count)
            {
                _entityBlips.Add(Centered(_chart, Vector2.zero, new Vector2(9f, 9f), UiKit.SolidSprite, Color.white));
            }

            return _entityBlips[index];
        }

        /// <summary>A centre-pivot image child (chart markers position around the chart middle, unlike
        /// <see cref="UiKit.Place"/>'s top-left rects).</summary>
        private static Image Centered(Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(parent, false);
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

        private void Label(Transform parent, Vector2 pos, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 20f);
            rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = 14;
            t.color = UiKit.CyanDim;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
        }
    }
}
