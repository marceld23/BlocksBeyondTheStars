// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight system chart (#597): a top-down map of the CURRENT system opened while cruising
    /// (FlightMap action, default M) — the space sibling of the surface world map (M, #592). Drawn from
    /// the REAL flight coordinates (<see cref="SpaceView.Landables"/> + the live space entities), i.e.
    /// exactly the positions the ship flies between, with the ship marker + heading on it.
    /// Clicking a body/station marker targets it; clicking empty space sets a free waypoint there. The
    /// waypoint shows on the space radar with a distance readout, and the VEGA autopilot steers to it.
    /// Opening takes menu ownership (the cursor arbiter frees the mouse and the ship holds position, as
    /// with the Tab travel screen); Esc or M closes.
    /// <para>The chart is centred on the system's STAR and draws the orbital path of every body that
    /// circles it (#623), so it reads as a chart of a star system rather than a scatter of discs. The
    /// paths come from the bodies' PROJECTED positions (<see cref="SystemChartLayout"/>) — never from
    /// their star-map orbit radii, which the render layout deliberately distorts — so every ring passes
    /// exactly through its own marker. The markers themselves never move: the flight scene is a t=0
    /// snapshot, so an animated body would drift away from the position the ship can actually fly to.</para>
    /// <para>A second tab, <b>Hyperspace</b> (#1603), swaps the system for the whole galaxy: a stars-only
    /// chart (<see cref="GalaxyChartWidget"/>) of every star system at its real star-map position. Click a
    /// star to read about it; a jump generator aboard (or a relay jump lane, #1125) lets you hyperjump to
    /// it straight from the chart. M always opens on the System tab; LB/RB step the tabs on a pad. A
    /// hyperjump closes the chart — the flight scene it drew belongs to the system being left.</para>
    /// </summary>
    public sealed class SpaceMap : MonoBehaviour
    {
        public GameBootstrap Game;
        public SpaceView SpaceView;
        public Camera Camera;

        private const float ChartSize = 900f;     // square chart area, canvas units (1920×1080 reference)
        private const float ChartHalf = ChartSize * 0.5f - 30f; // usable radius: margin keeps rim markers inside
        private const float SnapRadius = 26f;     // click-snap distance to a marker, canvas units
        private const float OrbitThickness = 2f;  // orbit-path line weight, canvas units (radius-independent)
        private const float OrbitAlpha = 0.34f;   // faint enough to stay behind the markers it connects
        private const float MaxBodyDisc = 46f;    // body discs are clamped to this diameter, however big the body
        private const float MarkerPad = MaxBodyDisc * 0.5f + 8f; // chart room a rim body's disc + backing needs
        // Flight units: asteroid orbit radii this close = one belt (#683). Stated in system units (160 ≈ 1.3× a
        // belt's full radial jitter of 120) so a change of the view scale (#1600) cannot split a belt in two.
        private const float BeltGroupGap = 160f * SystemBodyLayout.FlightViewScale;
        private const float BeltBandPad = 8f;     // chart units the belt band extends past its outermost member

        /// <summary>The finale system's id (#1605) — mirrors the server's reserved id; the chart gives it
        /// the hyperspace-violet accent once the story has revealed it.</summary>
        private const string FinaleSystemId = "guardian_finale";

        private static readonly Color WaypointCol = new Color(1f, 0.85f, 0.3f);
        private static readonly Color WreckCol = new Color(0.85f, 0.65f, 0.35f);          // the radar's wreck amber (#1664)
        private static readonly Color DiscCol = new Color(0.01f, 0.03f, 0.07f, 0.78f); // WorldMap's backing disc
        private static readonly Color StarFallbackCol = new Color(1f, 0.94f, 0.74f);   // an older server sends no star colour
        private static readonly Color JumpCol = new Color(0.30f, 0.18f, 0.46f);        // the travel screen's hyperspace-violet button

        private enum Tab { System, Hyperspace }

        private bool _open;
        private int _openedFrame = -1; // frame Open() ran — the SAME key-down must not instantly close it
        private Canvas _canvas;
        private Tab _tab;
        private bool _selectTabOnBuild; // a shoulder-button tab step lands the pad ON the new tab (#1409)
        private bool _hyperjumpSubscribed;

        // System tab.
        private RectTransform _chart;
        private Text _info;
        private RectTransform _ship;
        private RectTransform _waypoint;
        private float _scale; // scene units → chart canvas units
        private Vector3 _centre; // scene point the chart centres on: the star (Vector3.zero without a star map)
        private string _systemId;
        private string _systemName;
        private readonly Dictionary<string, NetBody> _sysBodies = new(); // current system, by body id
        private readonly List<(string Id, Vector2 Chart, string Name)> _targets = new(); // snap candidates
        private readonly List<Image> _entityBlips = new();

        // Hyperspace tab.
        private GalaxyChartWidget _galaxy;
        private Text _hyperTitle;
        private Text _hyperInfo;
        private Button _jumpButton;
        private string _selectedSystemId;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // Esc — or pad B (#1043) — closes the chart.
            if (_open && InputMap.Down(InputAction.UiCancel) && !Game.ChatTyping)
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

            if (!_open)
            {
                return;
            }

            // LB / RB step between the two tabs (#1409's console convention) — an open chart freezes the
            // ship, so the flight bindings of those buttons are free here.
            if (!UiKit.TextFieldFocused() && (InputMap.PadDown(PadButton.Rb) || InputMap.PadDown(PadButton.Lb)))
            {
                _selectTabOnBuild = true;
                SwitchTab(_tab == Tab.System ? Tab.Hyperspace : Tab.System);
                return;
            }

            if (_tab == Tab.System)
            {
                RefreshDynamic();
                HandleClick();
            }
            else
            {
                HandleGalaxyClick();
            }
        }

        /// <summary>Opened by <see cref="SpaceView"/> on the FlightMap action while cruising. Always opens on
        /// the System tab — the key's meaning is "where am I flying"; the galaxy is one tab away.</summary>
        public void Open()
        {
            if (_open)
            {
                return;
            }

            _open = true;
            _openedFrame = Time.frameCount;
            _tab = Tab.System;
            _selectedSystemId = null;
            Game.SetMenuOwner(this, true); // frees the cursor; UpdateCruise holds the ship while set (#413)
            Game.StarChartOpen = true;     // the music director switches to the star-map bed (#1174)
            if (!_hyperjumpSubscribed)
            {
                // A hyperjump tears the flight scene down (warp overlay, new system) while InSpace stays true —
                // the chart would otherwise survive it with a stale snapshot of the system just left, and keep
                // the star-map music bed on. Jumping FROM the chart makes that path an everyday one (#1603).
                Game.HyperjumpStarted += OnHyperjump;
                _hyperjumpSubscribed = true;
            }

            Build();

            // #1663: the first time the chart ever opens, VEGA says what it is for — nothing else in the game
            // told a new pilot that clicking a disc here sets the waypoint the radar and autopilot follow.
            // A client-side one-shot (a UI lesson, not world progress), remembered in the client settings.
            var settings = Game.Settings;
            if (settings != null && !settings.ChartWaypointHintShown)
            {
                settings.ChartWaypointHintShown = true;
                settings.Save();
                VegaPanel.Instance?.SayLocal("vega.hint.chart_waypoint");
            }
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Game.SetMenuOwner(this, false);
            Game.StarChartOpen = false;
            if (_hyperjumpSubscribed)
            {
                Game.HyperjumpStarted -= OnHyperjump;
                _hyperjumpSubscribed = false;
            }

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

            if (_hyperjumpSubscribed && Game != null)
            {
                Game.HyperjumpStarted -= OnHyperjump;
                _hyperjumpSubscribed = false;
            }
        }

        private void OnHyperjump() => Close();

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;

        private void SwitchTab(Tab tab)
        {
            if (_tab == tab)
            {
                return;
            }

            _tab = tab;
            Build();
        }

        private void Build()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            _targets.Clear();
            _entityBlips.Clear();
            _chart = null;
            _galaxy = null;
            _info = null;
            _hyperInfo = null;
            _jumpButton = null;
            LoadCurrentSystem();

            _canvas = UiKit.CreateCanvas("SpaceMapUI");
            _canvas.sortingOrder = 60; // above the flight HUD, like the surface map
            UiNav.Enable(_canvas.gameObject); // pad: tabs / clear-waypoint / jump / close reachable by stick (#1043); markers stay pointer-only
            UiNav.AddHint(_canvas.gameObject, "ui.pad.tabs", PadButton.Lb, PadButton.Rb);
            var root = _canvas.transform;

            UiKit.AddImage(root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.92f));
            string title = _tab == Tab.System
                ? L("ui.spacemap.title").ToUpperInvariant() + (string.IsNullOrEmpty(_systemName) ? string.Empty : "  —  " + _systemName)
                : L("ui.spacemap.hyper_title").ToUpperInvariant();
            UiKit.AddLogo(root, 40, 24, 900, 40, title, 26);
            BuildTabRow(root);

            if (_tab == Tab.System)
            {
                BuildSystemTab(root);
            }
            else
            {
                BuildHyperspaceTab(root);
            }
        }

        /// <summary>The two tabs, top right, in the travel screen's idiom: the active one cyan.</summary>
        private void BuildTabRow(Transform root)
        {
            const float tw = 220f, step = 230f;
            float x = 1880f - 2f * step + (step - tw);
            var tabs = new[] { (Tab.System, "ui.spacemap.tab_system"), (Tab.Hyperspace, "ui.spacemap.tab_hyperspace") };
            foreach (var (tab, key) in tabs)
            {
                var captured = tab;
                var b = UiKit.AddButton(root, x, 24, tw, 44, L(key), () => SwitchTab(captured));
                if (tab == _tab)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                    if (_selectTabOnBuild)
                    {
                        _selectTabOnBuild = false;
                        EventSystem.current?.SetSelectedGameObject(b.gameObject);
                        _canvas.GetComponent<UiNavFocus>()?.NoteSelection(b.gameObject);
                    }
                }

                UiKit.FitLabel(b.GetComponentInChildren<Text>(), 12, 20);
                x += step;
            }
        }

        // ------------------------------------------------------------------ System tab

        private void BuildSystemTab(Transform root)
        {
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

            // The chart centres on the system's STAR, so an orbit is a real circle around the middle of the
            // chart (#623) and the star no longer has to be pinned to the rim. It costs some zoom where the
            // launch body sits inside a far-flung asteroid's orbit — measured at up to 1.72× across real
            // systems, bounded by a test, and absorbed by the 12-unit minimum disc size. Without a star map
            // there is no star and nothing to orbit — fall back to the launch-body-centred chart.
            var landables = SpaceView != null ? SpaceView.Landables : null;
            bool starCentred = SpaceView != null && SpaceView.HasStar;
            _centre = starCentred ? SpaceView.StarPosition : Vector3.zero;

            // Everything whose position has to fit: the bodies, the stations and the ship. Marker sizes are
            // held back as a flat chart-unit margin instead of per-body scene radii — see FitScale.
            var fitX = new List<float>();
            var fitZ = new List<float>();
            if (landables != null)
            {
                foreach (var b in landables)
                {
                    fitX.Add(b.Pos.x);
                    fitZ.Add(b.Pos.z);
                }
            }

            if (Game.Space != null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    if (e.Kind == "SpaceStation" || e.Kind == "Wreck") // both are fixed chart markers
                    {
                        fitX.Add(e.X);
                        fitZ.Add(e.Z);
                    }
                }
            }

            if (Camera != null)
            {
                fitX.Add(Camera.transform.localPosition.x);
                fitZ.Add(Camera.transform.localPosition.z);
            }

            _scale = SystemChartLayout.FitScale(
                ChartHalf, fitX.ToArray(), fitZ.ToArray(), _centre.x, _centre.z, MarkerPad);

            // The star, at the true centre with a soft corona. Drawn before the orbit paths so the rings
            // read on top of the glow rather than being swallowed by it.
            if (starCentred)
            {
                Centered(_chart, Vector2.zero, new Vector2(64f, 64f), UiKit.DiscSprite, new Color(1f, 0.86f, 0.5f, 0.14f));
                Centered(_chart, Vector2.zero, new Vector2(38f, 38f), UiKit.DiscSprite, new Color(1f, 0.89f, 0.58f, 0.45f));
                Centered(_chart, Vector2.zero, new Vector2(24f, 24f), UiKit.DiscSprite, new Color(1f, 0.94f, 0.74f));
            }

            // Asteroid belts (#683): belt members share (nearly) one orbit radius, so per-member rings
            // would stack into a smeared blob. Group the star-orbiting asteroid bodies by projected
            // orbit radius; three or more within a belt-tight spread read as ONE belt — drawn as a
            // single translucent band with one localized label — and their own rings are suppressed.
            // Legacy scattered systems rarely group and simply keep their per-body rings.
            var beltMembers = new HashSet<string>();
            if (starCentred && landables != null)
            {
                var fields = new List<(string Id, float R)>();
                foreach (var b in landables)
                {
                    var fnb = BodyFor(b.Id);
                    if (fnb != null && fnb.Kind == "AsteroidField" && string.IsNullOrEmpty(fnb.ParentId))
                    {
                        fields.Add((b.Id, new Vector2(b.Pos.x - _centre.x, b.Pos.z - _centre.z).magnitude));
                    }
                }

                fields.Sort((a, c) => a.R.CompareTo(c.R));
                int start = 0;
                for (int k = 1; k <= fields.Count; k++)
                {
                    if (k < fields.Count && fields[k].R - fields[k - 1].R <= BeltGroupGap)
                    {
                        continue; // same annulus — keep extending the group
                    }

                    if (k - start >= 3)
                    {
                        float rMin = fields[start].R * _scale, rMax = fields[k - 1].R * _scale;
                        float outer = rMax + BeltBandPad;
                        var bandCol = new Color(0.78f, 0.72f, 0.6f, 0.12f);
                        UiOrbitRing.Create(_chart, Vector2.zero, new Vector2(outer * 2f, outer * 2f),
                            bandCol, rMax - rMin + BeltBandPad * 2f);
                        Label(_chart, new Vector2(0f, outer + 12f), L("ui.map.belt"));
                        for (int m = start; m < k; m++)
                        {
                            beltMembers.Add(fields[m].Id);
                        }
                    }

                    start = k;
                }
            }

            // Orbit paths: one ring per body that circles the star — planets and the landable asteroid
            // bodies. Moons are deliberately left out: they are re-laddered onto clearance slots just
            // outside their parent's drawn radius, so their rings would collapse into the planet's disc
            // and turn the chart into noise. Each radius comes from the body's own projected position, so
            // the ring is guaranteed to pass through its marker whatever the layout passes did to it.
            // Belt members are covered by their belt's band above instead of a ring each.
            if (starCentred && landables != null)
            {
                foreach (var b in landables)
                {
                    if (beltMembers.Contains(b.Id) || !OrbitsStar(BodyFor(b.Id)))
                    {
                        continue;
                    }

                    var p = ToChart(b.Pos);
                    float r = SystemChartLayout.OrbitRadius(p.x, p.y);
                    if (!SystemChartLayout.ShowRing(r))
                    {
                        continue;
                    }

                    var ringCol = BodyColor(b.Id);
                    ringCol.a = OrbitAlpha;
                    UiOrbitRing.Create(_chart, Vector2.zero, new Vector2(r * 2f, r * 2f), ringCol, OrbitThickness);
                }
            }

            // Bodies: a coloured disc on a dark backing, radius-proportional, named — the same legibility
            // rules as the reworked surface map (#592: white-on-dark, no thin cyan hairlines).
            if (landables != null)
            {
                foreach (var b in landables)
                {
                    var p = ToChart(b.Pos);
                    float size = Mathf.Clamp(b.Radius * _scale * 2f, 12f, MaxBodyDisc);
                    var col = BodyColor(b.Id);
                    Centered(_chart, p, new Vector2(size + 8f, size + 8f), UiKit.DiscSprite, DiscCol);
                    Centered(_chart, p, new Vector2(size, size), UiKit.DiscSprite, col);

                    // A ringed planet (#596) wears its rings on the chart too: a flattened ellipse over the
                    // disc, which is what a ring system looks like seen near-edge-on.
                    var nb = BodyFor(b.Id);
                    if (nb != null && nb.RingSeed != 0)
                    {
                        var glyph = Color.Lerp(col, Color.white, 0.5f);
                        glyph.a = 0.85f;
                        UiOrbitRing.Create(_chart, p, new Vector2(size * 2f, Mathf.Max(9f, size * 0.7f)), glyph);
                    }

                    // #678: wrecks + asteroid fields carry coined proper names ("Skarrak") with no kind
                    // word baked in — pair the name with the LOCALIZED kind here so the chart still says
                    // what the dot is (planets/moons/stations stay name-only, their glyphs read clearly).
                    string label = b.Name;
                    string kind = nb?.Kind ?? string.Empty;
                    if (kind == "Wreck" || kind == "AsteroidField")
                    {
                        label = $"{b.Name} · {L("ui.map.kind_" + kind.ToLowerInvariant())}";
                    }

                    Label(_chart, p + new Vector2(0f, -size * 0.5f - 12f), label);
                    _targets.Add((string.IsNullOrEmpty(b.Id) ? SpaceView.HomeWaypointId : b.Id, p, b.Name));
                }
            }

            // The system's derelict (#1664): a static wreck entity at its chart position — a small disc of
            // scorched plating with its coined name + the localized kind, and a snap target, so a click sets it
            // as the waypoint (the autopilot then flies you into salvage range). It never moves, so it is drawn
            // once here rather than as a live blip.
            if (Game.Space != null)
            {
                foreach (var e in Game.Space.Entities)
                {
                    if (e.Kind != "Wreck")
                    {
                        continue;
                    }

                    var p = Clamp(ToChart(new Vector3(e.X, e.Y, e.Z)));
                    Centered(_chart, p, new Vector2(20f, 20f), UiKit.DiscSprite, DiscCol);
                    Centered(_chart, p, new Vector2(12f, 12f), UiKit.DiscSprite, WreckCol);
                    Label(_chart, p + new Vector2(0f, -18f), $"{e.Name} · {L("ui.map.kind_wreck")}");
                    _targets.Add((e.Id, p, e.Name));
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
                    if (!station && !e.Hostile)
                    {
                        // Asteroids/drops would dust the chart — radar range covers those; the wreck is a
                        // static named marker drawn once in Build (#1664).
                        continue;
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
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_chart, Input.mousePosition, null, out var local))
            {
                return;
            }

            // Re-base the click onto the frame the markers live in. ScreenPointToLocalPointInRectangle
            // measures from the rect's PIVOT, which UiKit.Place puts at the TOP-LEFT, while every marker is
            // anchored around the chart's CENTRE — so the two frames sat half a chart apart (a click in the
            // middle came out as (450, -450)). That put every free waypoint in the wrong place and pushed
            // marker snapping ~636 units out of range, so clicking a body to target it almost never took.
            var lp = local - _chart.rect.center;

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
                var scene = FromChart(lp);
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
                    + "\n" + string.Format(L("ui.spacemap.distance_fmt"), BlocksBeyondTheStars.Client.Core.SpaceDistance.Group(BlocksBeyondTheStars.Client.Core.SpaceDistance.Km(dist))) // km, #1599
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

        // ------------------------------------------------------------------ Hyperspace tab

        private void BuildHyperspaceTab(Transform root)
        {
            const float ax = 40f, ay = 100f;
            UiKit.AddPanel(root, ax - 6, ay - 6, ChartSize + 12, ChartSize + 12, UiKit.Panel);
            _galaxy = GalaxyChartWidget.Create(root, ax, ay, ChartSize);
            var stars = BuildStars(out var lanes);
            _galaxy.Show(stars, lanes, _selectedSystemId);

            // Info panel on the right: the selected star, and the jump button.
            UiKit.AddPanel(root, 980, 100, 900, 900, UiKit.Panel);
            float ix = 1010f;
            _hyperTitle = UiKit.AddText(root, ix, 130, 840, 28, string.Empty, 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _hyperInfo = UiKit.AddText(root, ix, 170, 840, 560, string.Empty, 20, UiKit.TextCol, TextAnchor.UpperLeft);
            _hyperInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hyperInfo.verticalOverflow = VerticalWrapMode.Truncate;

            var hint = UiKit.AddText(root, ix, 740, 840, 120, L("ui.spacemap.hyper_hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            _jumpButton = UiKit.AddButton(root, ix, 880, 380, 50, L("ui.map.hyperjump_here"), JumpToSelected);
            UiKit.FitLabel(_jumpButton.GetComponentInChildren<Text>(), 12, 20);
            UiKit.AddButton(root, ix + 420, 880, 200, 50, L("ui.action.close"), Close);

            RefreshHyperInfo();
        }

        /// <summary>Every system of the star map as the chart should draw it: name gated by the #1113 rule
        /// (a bare "?" for a system never entered, unless a radar array is aboard), the frontier tag, the
        /// server's star colour (#1604) with a warm-yellow fallback, the party's whereabouts, and the finale
        /// accent. Lanes come from the relay network state (#1125).</summary>
        private List<GalaxyChartWidget.Star> BuildStars(out List<(string A, string B)> lanes)
        {
            var stars = new List<GalaxyChartWidget.Star>();
            lanes = new List<(string, string)>();
            var map = Game?.StarMap;
            if (map?.Systems == null)
            {
                return stars;
            }

            bool radar = HasModule("radar_array");
            string frontier = L("ui.map.frontier");

            // Other players by system, so a star can carry "Anna, Ben" above it.
            var playersBySystem = new Dictionary<string, List<string>>();
            if (map.Players != null)
            {
                foreach (var p in map.Players)
                {
                    if (p.Name == Game.PlayerName)
                    {
                        continue; // the current star's ring already says where I am
                    }

                    var sys = SystemOfBody(map, p.LocationId);
                    if (sys == null)
                    {
                        continue;
                    }

                    if (!playersBySystem.TryGetValue(sys.Id, out var names))
                    {
                        playersBySystem[sys.Id] = names = new List<string>();
                    }

                    names.Add(p.Name);
                }
            }

            foreach (var sys in map.Systems)
            {
                bool current = sys.Id == _systemId;
                bool known = Game.KnowsSystem(sys.Id);
                string label = GalaxyChartLayout.DisplayName(sys.Name, known, current, radar, "?");
                if (sys.Tier >= 2 && label != "?")
                {
                    label = $"{label} · {frontier}";
                }

                stars.Add(new GalaxyChartWidget.Star
                {
                    Id = sys.Id,
                    MapX = sys.MapX,
                    MapY = sys.MapY,
                    Label = label,
                    Color = sys.StarColor != 0 ? Rgb(sys.StarColor) : StarFallbackCol,
                    Current = current,
                    Known = known,
                    Finale = sys.Id == FinaleSystemId,
                    Players = playersBySystem.TryGetValue(sys.Id, out var there) ? string.Join(", ", there) : string.Empty,
                });
            }

            var net = Game.RelayNetwork;
            if (net?.LaneSystemA != null && net.LaneSystemB != null)
            {
                int n = Mathf.Min(net.LaneSystemA.Length, net.LaneSystemB.Length);
                for (int i = 0; i < n; i++)
                {
                    lanes.Add((net.LaneSystemA[i], net.LaneSystemB[i]));
                }
            }

            return stars;
        }

        private void HandleGalaxyClick()
        {
            if (_galaxy == null || !Input.GetMouseButtonDown(0) || !_galaxy.TryPick(Input.mousePosition, out var id))
            {
                return;
            }

            _selectedSystemId = id;
            _galaxy.Select(id);
            RefreshHyperInfo();
        }

        /// <summary>The info panel for the selected star: name (or "unknown system"), frontier tag, whether
        /// you have been there, who of the party is there, your station/base badges, and why a jump is or
        /// is not possible — the same hints the travel screen gives. The jump button shows for any other
        /// system and is enabled only with a jump generator aboard or a lane (the server enforces both).</summary>
        private void RefreshHyperInfo()
        {
            if (_hyperInfo == null || _hyperTitle == null)
            {
                return;
            }

            var map = Game?.StarMap;
            var sys = FindSystem(map, _selectedSystemId);
            if (sys == null)
            {
                _hyperTitle.text = L("ui.spacemap.hyper_none");
                _hyperInfo.text = L("ui.spacemap.hyper_select_hint");
                _jumpButton.gameObject.SetActive(false);
                return;
            }

            bool current = sys.Id == _systemId;
            bool known = Game.KnowsSystem(sys.Id);
            string title = GalaxyChartLayout.DisplayName(sys.Name, known, current, HasModule("radar_array"), L("ui.map.system_unknown"));
            if (sys.Tier >= 2)
            {
                title = $"{title} · {L("ui.map.frontier")}";
            }

            _hyperTitle.text = title;

            var lines = new List<string>
            {
                current ? L("ui.spacemap.hyper_here") : known ? L("ui.spacemap.hyper_known") : L("ui.spacemap.hyper_unknown"),
            };

            var there = new List<string>();
            if (map.Players != null)
            {
                foreach (var p in map.Players)
                {
                    if (p.Name != Game.PlayerName && SystemOfBody(map, p.LocationId)?.Id == sys.Id)
                    {
                        there.Add(p.Name);
                    }
                }
            }

            if (there.Count > 0)
            {
                lines.Add(string.Format(L("ui.spacemap.hyper_players"), string.Join(", ", there)));
            }

            foreach (var b in sys.Bodies)
            {
                if (Game.HasMyStation(b.Id))
                {
                    lines.Add(L("ui.map.station_here"));
                }

                string baseName = Game.MyBaseName(b.Id);
                if (!string.IsNullOrEmpty(baseName))
                {
                    lines.Add(L("ui.map.base_here") + ": " + baseName);
                }
            }

            bool lane = GalaxyChartLayout.HasLane(Game.RelayNetwork?.LaneSystemA, Game.RelayNetwork?.LaneSystemB, _systemId, sys.Id);
            bool generator = HasModule("jump_generator");
            if (!current)
            {
                lines.Add(string.Empty);
                lines.Add(lane ? "⇄ " + L("ui.map.lane_hint") : L("ui.map.hyperjump_hint"));
            }

            _hyperInfo.text = string.Join("\n", lines);

            bool canJump = !current && (lane || generator) && Game.Network != null;
            _jumpButton.gameObject.SetActive(!current);
            _jumpButton.interactable = canJump;
            _jumpButton.GetComponent<Image>().color = canJump ? JumpCol : UiKit.TabLocked;
        }

        private void JumpToSelected()
        {
            if (string.IsNullOrEmpty(_selectedSystemId) || _selectedSystemId == _systemId)
            {
                return;
            }

            // The server checks the generator/lane and the flight rules; on success the client's
            // HyperjumpStarted fires and OnHyperjump closes the chart. A rejection leaves the chart open
            // with the server's message in the chat.
            Game.Network?.SendHyperjumpSystem(_selectedSystemId);
        }

        private bool HasModule(string module)
            => Game?.ShipCombat?.Modules != null && System.Array.IndexOf(Game.ShipCombat.Modules, module) >= 0;

        private static NetStarSystem FindSystem(StarMapData map, string id)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                if (sys.Id == id)
                {
                    return sys;
                }
            }

            return null;
        }

        private static NetStarSystem SystemOfBody(StarMapData map, string bodyId)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == bodyId)
                    {
                        return sys;
                    }
                }
            }

            return null;
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        // ------------------------------------------------------------------ shared helpers

        /// <summary>Caches the system the player is in: its id + name (for the title and the galaxy chart)
        /// and its bodies by id, so the per-body look-ups below don't re-walk the whole star map.</summary>
        private void LoadCurrentSystem()
        {
            _systemId = null;
            _systemName = null;
            _sysBodies.Clear();
            var map = Game?.StarMap;
            if (map?.Systems == null)
            {
                return;
            }

            var mine = SystemOfBody(map, map.ActiveLocationId);
            if (mine == null)
            {
                return;
            }

            _systemId = mine.Id;
            _systemName = mine.Name;
            foreach (var b in mine.Bodies)
            {
                if (!string.IsNullOrEmpty(b.Id))
                {
                    _sysBodies[b.Id] = b;
                }
            }
        }

        /// <summary>The star-map body behind a <see cref="SpaceView.Landables"/> entry, or null if the map
        /// doesn't know it. The launch body's entry carries an EMPTY id (the scene is centred on it), so it
        /// resolves through the active location — which is also why it now gets its real planet colour.</summary>
        private NetBody BodyFor(string landableId)
        {
            string id = string.IsNullOrEmpty(landableId) ? Game?.StarMap?.ActiveLocationId : landableId;
            return !string.IsNullOrEmpty(id) && _sysBodies.TryGetValue(id, out var body) ? body : null;
        }

        /// <summary>Whether this body circles the star itself and so gets an orbit path drawn: planets and
        /// the landable asteroid bodies. A moon carries its parent's id and is deliberately excluded.</summary>
        private static bool OrbitsStar(NetBody body)
            => body != null
            && string.IsNullOrEmpty(body.ParentId)
            && (body.Kind == "Planet" || body.Kind == "AsteroidField");

        private Color BodyColor(string id)
        {
            var body = BodyFor(id);
            return body != null
                ? SystemMapWidget.PlanetColor(body.PlanetType, body.Kind)
                : new Color(0.6f, 0.85f, 0.7f); // unknown to the star map
        }

        private Vector2 ToChart(Vector3 scene)
        {
            SystemChartLayout.ToChart(scene.x, scene.z, _scale, _centre.x, _centre.z, out float x, out float y);
            return new Vector2(x, y);
        }

        /// <summary>The flight-scene point under a chart position — the exact inverse of
        /// <see cref="ToChart"/>, so a click resolves to the spot it was made on.</summary>
        private Vector3 FromChart(Vector2 chart)
        {
            SystemChartLayout.FromChart(chart.x, chart.y, _scale, _centre.x, _centre.z, out float x, out float z);
            return new Vector3(x, 0f, z);
        }

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
