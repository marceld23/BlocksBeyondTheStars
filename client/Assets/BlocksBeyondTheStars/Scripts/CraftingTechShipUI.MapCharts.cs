// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The travel screen's charts (#3130): above the detail pane a <b>System | Galaxy</b> switch — the selected system's
    /// orrery (#2745) or the whole galaxy, the flight chart's hyperspace chart with its zoom (#3129). A click on a star
    /// picks that system exactly like the sidebar, so the body list and the place details — own names (#2952), colour
    /// marks, the planet scan, travel and "Hyperjump to this system" — work unchanged. And (#3132) "Set as destination":
    /// a body of the current system becomes the nav waypoint before the launch, so the radar and the autopilot already
    /// know where to go.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private const float GalaxyChartSize = 600f;

        private bool _mapGalaxy; // the Galaxy view is on (kept while the menu lives)
        private GalaxyChartView? _mapGalaxyView; // its zoom, kept across the detail pane's rebuilds

        /// <summary>The view switch and the chart under it. Returns the y below them.</summary>
        private float AddMapChart(StarMapData map, NetStarSystem sys, bool known, float y)
        {
            y = AddChartSwitch(y);
            if (_mapGalaxy)
            {
                return AddGalaxyChart(sys, y);
            }

            if (!known)
            {
                return y;
            }

            UiKit.AddText(_detail, 8, y, 600, 30, "★ " + sys.Shown(), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y = AddOriginalNameLine(y + 30f, sys.IsRenamed() ? sys.Name : null) + 6f;
            _systemMap = SystemMapWidget.Create(_detail, 100, y, 380, 380); // round chart, centred where the old one was
            string sel = !string.IsNullOrEmpty(_selected) && _selected.StartsWith("body:", System.StringComparison.Ordinal)
                ? _selected.Substring(5) : string.Empty;
            // #2178: live weather glyphs only for the system you are in (no other system has live data).
            bool inThisSystem = Game != null && Game.SystemWeather.SystemId == sys.Id;
            _systemMap.Show(sys.Bodies, map.ActiveLocationId, sel, MarkerColorOf,
                inThisSystem ? id => PlanetWeatherVisuals.GlyphMarkup(Game, id) : null);
            return y + 396f;
        }

        /// <summary>System | Galaxy — the flight chart's own tab names, the active one cyan.</summary>
        private float AddChartSwitch(float y)
        {
            var tabs = new[] { (false, "ui.spacemap.tab_system"), (true, "ui.spacemap.tab_hyperspace") };
            float x = 8f;
            foreach (var (galaxy, key) in tabs)
            {
                bool g = galaxy;
                var b = UiKit.AddButton(_detail, x, y, 200, 40, L(key), () =>
                {
                    if (_mapGalaxy != g)
                    {
                        _mapGalaxy = g;
                        RebuildDetail();
                    }
                });
                if (_mapGalaxy == galaxy)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }

                UiKit.FitLabel(b.GetComponentInChildren<Text>(), 12, 18);
                x += 208f;
            }

            return y + 50f;
        }

        /// <summary>The galaxy, the selected system ringed; a click on a star selects its system (sidebar, list, details).
        /// Under the chart the selected system's name as the player may read it (#1113: "Unknown system" until entered).</summary>
        private float AddGalaxyChart(NetStarSystem sys, float y)
        {
            var chart = GalaxyChartWidget.Create(_detail, (636f - GalaxyChartSize) * 0.5f, y, GalaxyChartSize);
            chart.EnableInteraction(L);
            var stars = GalaxyChartStars.Build(Game, CurrentSystemId(), HasRadarArray(), L, out var lanes);
            chart.Show(stars, lanes, sys?.Id, GalaxyChartStars.KnownWormholePairs(Game));
            if (_mapGalaxyView.HasValue)
            {
                chart.View = _mapGalaxyView.Value;
            }

            chart.ViewChanged += () => _mapGalaxyView = chart.View;
            chart.StarClicked += SelectSystemFromChart;
            y += GalaxyChartSize + 8f;

            if (sys != null)
            {
                bool known = sys.Id == CurrentSystemId() || Game.KnowsSystem(sys.Id);
                UiKit.AddText(_detail, 8, y, 600, 30, "★ " + SystemDisplayName(sys), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y = AddOriginalNameLine(y + 30f, known && sys.IsRenamed() ? sys.Name : null) + 6f;
            }

            return y;
        }

        /// <summary>A star clicked on the galaxy chart: the same as picking its system in the sidebar.</summary>
        private void SelectSystemFromChart(string systemId)
        {
            var picked = GalaxyChartStars.FindSystem(Game?.StarMap, systemId);
            if (picked == null)
            {
                return;
            }

            _category = "sys:" + picked.Name;
            _selected = string.Empty;
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
        }

        /// <summary>#3132: "Set as destination" for a body of the current system — or, when it already is the waypoint,
        /// the button that clears it — and the line saying what the waypoint does after the launch.</summary>
        private float AddDestinationRow(float y, NetBody body)
        {
            bool isDestination = Game.SpaceWaypointId == body.Id;
            var btn = UiKit.AddButton(_detail, 8, y, 330, 44, L(isDestination ? "ui.map.destination_clear" : "ui.map.destination_set"), () =>
            {
                Game.SpaceWaypointId = isDestination ? null : body.Id;
                Game.SpaceWaypointPos = null;
                RebuildDetail();
            });
            if (isDestination && btn.TryGetComponent<Image>(out var img))
            {
                img.color = Color.Lerp(img.color, new Color(1f, 0.85f, 0.3f), 0.45f); // the flight chart's waypoint gold
            }

            UiKit.FitLabel(btn.GetComponentInChildren<Text>(), 12, 18);
            y += 50f;
            if (isDestination)
            {
                var hint = UiKit.AddText(_detail, 8, y, 620, 48, HudUi.ExpandKeyTokens(Game.Localizer, L("ui.map.destination_hint")),
                    16, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 52f;
            }

            return y;
        }
    }
}
