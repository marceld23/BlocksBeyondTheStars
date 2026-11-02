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
    /// The flight chart's Hyperspace tab (#1603): the galaxy chart (<see cref="GalaxyChartWidget"/>, zoomable since #3129)
    /// and the info panel of the selected star — its name, whether you have been there, who of the party is there, your
    /// station/base badges, why a jump is or is not possible, and (#3131) the place rows of a system you know: its
    /// generated name under an own name and "Your own name" to give it one, right here in flight.
    /// </summary>
    public sealed partial class SpaceMap
    {
        private static readonly Color JumpCol = new Color(0.30f, 0.18f, 0.46f); // the travel screen's hyperspace-violet button

        private RectTransform _hyperPlace; // the place rows under the info text, rebuilt per selection
        private GalaxyChartView? _galaxyView; // the player's zoom, kept across a rebuild of the open chart
        private bool _starMapSubscribed;

        private void BuildHyperspaceTab(Transform root)
        {
            const float ax = 40f, ay = 100f;
            UiKit.AddPanel(root, ax - 6, ay - 6, ChartSize + 12, ChartSize + 12, UiKit.Panel);
            _galaxy = GalaxyChartWidget.Create(root, ax, ay, ChartSize);
            _galaxy.EnableInteraction(L);
            var stars = GalaxyChartStars.Build(Game, _systemId, HasModule("radar_array"), L, out var lanes);
            _galaxy.Show(stars, lanes, _selectedSystemId, GalaxyChartStars.KnownWormholePairs(Game)); // #2242: the violet zigzags
            if (_galaxyView.HasValue)
            {
                _galaxy.View = _galaxyView.Value;
            }

            _galaxy.ViewChanged += () => _galaxyView = _galaxy != null ? _galaxy.View : _galaxyView;
            _galaxy.StarClicked += id =>
            {
                _selectedSystemId = id;
                _galaxy.Select(id);
                RefreshHyperInfo();
            };

            // Info panel on the right: the selected star, its place rows, and the jump button.
            UiKit.AddPanel(root, 980, 100, 900, 900, UiKit.Panel);
            float ix = 1010f;
            _hyperTitle = UiKit.AddText(root, ix, 130, 840, 28, string.Empty, 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _hyperInfo = UiKit.AddText(root, ix, 170, 840, 420, string.Empty, 20, UiKit.TextCol, TextAnchor.UpperLeft);
            _hyperInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hyperInfo.verticalOverflow = VerticalWrapMode.Truncate;

            var placeGo = new GameObject("HyperPlace", typeof(RectTransform));
            placeGo.transform.SetParent(root, false);
            UiKit.Place(placeGo, 0, 0, 1920, 1080);
            _hyperPlace = placeGo.GetComponent<RectTransform>();

            var hint = UiKit.AddText(root, ix, 740, 840, 120, L("ui.spacemap.hyper_hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            _jumpButton = UiKit.AddButton(root, ix, 880, 380, 50, L("ui.map.hyperjump_here"), JumpToSelected);
            UiKit.FitLabel(_jumpButton.GetComponentInChildren<Text>(), 12, 20);
            UiKit.AddButton(root, ix + 420, 880, 200, 50, L("ui.action.close"), Close);

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
            var sys = GalaxyChartStars.FindSystem(map, _selectedSystemId);
            RefreshHyperPlace(sys);
            if (sys == null)
            {
                _hyperTitle.text = L("ui.spacemap.hyper_none");
                _hyperInfo.text = L("ui.spacemap.hyper_select_hint");
                _jumpButton.gameObject.SetActive(false);
                return;
            }

            bool current = sys.Id == _systemId;
            bool known = Game.KnowsSystem(sys.Id);
            string title = GalaxyChartLayout.DisplayName(sys.Shown(), known, current, HasModule("radar_array"), L("ui.map.system_unknown"));
            if (sys.Tier >= 2)
            {
                title = $"{title} · {L("ui.map.frontier")}";
            }

            _hyperTitle.text = title;
            _hyperInfo.text = string.Join("\n", HyperInfoLines(map, sys, current, known));

            bool lane = GalaxyChartLayout.HasLane(Game.RelayNetwork?.LaneSystemA, Game.RelayNetwork?.LaneSystemB, _systemId, sys.Id);
            bool generator = HasModule("jump_generator");
            bool canJump = !current && (lane || generator) && Game.Network != null;
            _jumpButton.gameObject.SetActive(!current);
            _jumpButton.interactable = canJump;
            _jumpButton.GetComponent<Image>().color = canJump ? JumpCol : UiKit.TabLocked;
        }

        private List<string> HyperInfoLines(StarMapData map, NetStarSystem sys, bool current, bool known)
        {
            var lines = new List<string>
            {
                current ? L("ui.spacemap.hyper_here") : known ? L("ui.spacemap.hyper_known") : L("ui.spacemap.hyper_unknown"),
            };

            var there = new List<string>();
            if (map.Players != null)
            {
                foreach (var p in map.Players)
                {
                    if (p.Name != Game.PlayerName && GalaxyChartStars.SystemOfBody(map, p.LocationId)?.Id == sys.Id)
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

                string bases = Game.MyBasesLabel(b.Id, L); // #2996: several bases say how many
                if (!string.IsNullOrEmpty(bases))
                {
                    lines.Add(bases);
                }
            }

            bool lane = GalaxyChartLayout.HasLane(Game.RelayNetwork?.LaneSystemA, Game.RelayNetwork?.LaneSystemB, _systemId, sys.Id);
            if (!current)
            {
                lines.Add(string.Empty);
                lines.Add(lane ? "⇄ " + L("ui.map.lane_hint") : L("ui.map.hyperjump_hint"));
            }

            return lines;
        }

        /// <summary>#3131: a system you have been to (or are in) can carry your own name — the same row as the travel
        /// screen's, with the generated name in small type once you named it.</summary>
        private void RefreshHyperPlace(NetStarSystem sys)
        {
            if (_hyperPlace == null)
            {
                return;
            }

            for (int i = _hyperPlace.childCount - 1; i >= 0; i--)
            {
                Destroy(_hyperPlace.GetChild(i).gameObject);
            }

            if (sys == null || !(sys.Id == _systemId || Game.KnowsSystem(sys.Id)))
            {
                return;
            }

            float y = PlaceDetailRows.AddOriginalNameLine(_hyperPlace, 1010f, 604f, sys.IsRenamed() ? sys.Name : null, L);
            PlaceDetailRows.AddOwnNameRow(_hyperPlace, 1010f, y + 4f, sys.Id, sys.CustomName, L, Game.Network);
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

        /// <summary>#3131: while the chart is open, a new star map (a rename just confirmed, a friend moving on) redraws it —
        /// unless the player is typing a name, which a rebuild would throw away.</summary>
        private void SubscribeStarMap(bool on)
        {
            var net = Game != null ? Game.Network : null;
            if (net == null || on == _starMapSubscribed)
            {
                return;
            }

            if (on)
            {
                net.StarMapReceived += OnStarMapWhileOpen;
            }
            else
            {
                net.StarMapReceived -= OnStarMapWhileOpen;
            }

            _starMapSubscribed = on;
        }

        private void OnStarMapWhileOpen(StarMapData map)
        {
            if (_open && !UiKit.TextFieldFocused())
            {
                Build();
            }
        }
    }
}
