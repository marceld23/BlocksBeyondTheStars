// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
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
        private const float BeltGroupGap = 26f;   // flight units: asteroid orbit radii this close = one belt (#683)
        private const float BeltBandPad = 8f;     // chart units the belt band extends past its outermost member

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
        private Vector3 _centre; // scene point the chart centres on: the star (Vector3.zero without a star map)
        private string _systemName;
        private readonly Dictionary<string, NetBody> _sysBodies = new(); // current system, by body id
        private readonly List<(string Id, Vector2 Chart, string Name)> _targets = new(); // snap candidates
        private readonly List<Image> _entityBlips = new();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // Esc — or pad B (#1043) — closes the chart.
            if (_open && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)) && !Game.ChatTyping)
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
            Game.StarChartOpen = true;     // the music director switches to the star-map bed (#1174)
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
            Game.StarChartOpen = false;
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
            LoadCurrentSystem();

            _canvas = UiKit.CreateCanvas("SpaceMapUI");
            _canvas.sortingOrder = 60; // above the flight HUD, like the surface map
            UiNav.Enable(_canvas.gameObject); // pad: clear-waypoint / close reachable by stick (#1043); markers stay pointer-only
            var root = _canvas.transform;

            UiKit.AddImage(root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.92f));
            UiKit.AddLogo(root, 40, 24, 700, 40, L("ui.spacemap.title").ToUpperInvariant() + (string.IsNullOrEmpty(_systemName) ? string.Empty : "  —  " + _systemName), 26);

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
                    if (e.Kind == "SpaceStation")
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

        /// <summary>Caches the system the player is in: its name (for the title) and its bodies by id, so
        /// the per-body look-ups below don't re-walk the whole star map. The chart only ever shows this one
        /// system.</summary>
        private void LoadCurrentSystem()
        {
            _systemName = null;
            _sysBodies.Clear();
            var map = Game?.StarMap;
            if (map?.Systems == null)
            {
                return;
            }

            foreach (var sys in map.Systems)
            {
                bool mine = false;
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == map.ActiveLocationId)
                    {
                        mine = true;
                        break;
                    }
                }

                if (!mine)
                {
                    continue;
                }

                _systemName = sys.Name;
                foreach (var b in sys.Bodies)
                {
                    if (!string.IsNullOrEmpty(b.Id))
                    {
                        _sysBodies[b.Id] = b;
                    }
                }

                return;
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
