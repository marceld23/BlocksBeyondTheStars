// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Map tab of <see cref="CraftingTechShipUI"/>: the star-system list and system chart, the flight and
    /// travel actions, the planet scan (also shown in the Ship detail), the relay meter and the coloured planet marks.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>The id of the star system the player is currently in (contains the active location).</summary>
        private string CurrentSystemId()
        {
            var map = Game.StarMap;
            if (map?.Systems == null)
            {
                return string.Empty;
            }

            foreach (var sys in map.Systems)
            {
                if (sys.Bodies.Any(b => b.Id == map.ActiveLocationId))
                {
                    return sys.Id;
                }
            }

            return map.Systems.Length > 0 ? map.Systems[0].Id : string.Empty;
        }

        /// <summary>The system selected in the travel sidebar — the one keyed by <see cref="_category"/>,
        /// defaulting to the player's current system.</summary>
        private NetStarSystem SelectedSystem()
        {
            var map = Game.StarMap;
            if (map?.Systems == null || map.Systems.Length == 0)
            {
                return null;
            }

            var byCat = map.Systems.FirstOrDefault(s => "sys:" + s.Name == _category);
            if (byCat != null)
            {
                return byCat;
            }

            string curId = CurrentSystemId();
            return map.Systems.FirstOrDefault(s => s.Id == curId) ?? map.Systems[0];
        }

        /// <summary>A never-entered system's name reads "Unknown system" (#1113) — the galaxy is discovered,
        /// not read off a chart. A fitted radar array decodes the beacon signals and shows real names.
        /// Frontier systems (#1122) carry a tag either way: the far rim is richer, and that is worth
        /// telegraphing BEFORE the player has been there — it is the reason to fly far.</summary>
        private string SystemDisplayName(NetStarSystem sys)
        {
            if (sys == null)
            {
                return string.Empty;
            }

            string name = Game.KnowsSystem(sys.Id) || sys.Id == CurrentSystemId() || HasRadarArray()
                ? sys.Shown() // #2952: the player's own name for it
                : L("ui.map.system_unknown");
            if (sys.Tier >= 2)
            {
                name = $"{name} · {L("ui.map.frontier")}";
            }

            // A jump lane to the CURRENT system (#1125 F-2): worth telegraphing wherever the name shows —
            // this destination is one generator-less hop away on the relay network.
            return HasClientLane(CurrentSystemId(), sys.Id) ? name + " ⇄" : name;
        }

        /// <summary>Whether the relay network links these two systems with a jump lane (server-reported,
        /// unordered). False until the join snapshot arrives or when relays are absent from the data.</summary>
        private bool HasClientLane(string systemA, string systemB)
        {
            var net = Game?.RelayNetwork;
            if (net?.LaneSystemA == null || net.LaneSystemB == null
                || string.IsNullOrEmpty(systemA) || string.IsNullOrEmpty(systemB) || systemA == systemB)
            {
                return false;
            }

            for (int i = 0; i < net.LaneSystemA.Length && i < net.LaneSystemB.Length; i++)
            {
                if ((net.LaneSystemA[i] == systemA && net.LaneSystemB[i] == systemB)
                    || (net.LaneSystemA[i] == systemB && net.LaneSystemB[i] == systemA))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the active ship carries a radar_array module (server-reported fit).</summary>
        private bool HasRadarArray()
            => Game?.ShipCombat?.Modules != null
                && System.Array.IndexOf(Game.ShipCombat.Modules, "radar_array") >= 0;

        /// <summary>#2140: true when the active ship carries the planet scanner.</summary>
        private bool HasPlanetScanner()
            => Game?.ShipCombat?.Modules != null
                && System.Array.IndexOf(Game.ShipCombat.Modules, "planet_scanner") >= 0;

        /// <summary>#2140 ("Ressourcenscan") and #2239: the "Planetenscan" button for a body, and — once the server
        /// answered for this body — the report under it. Every ship scans now (the cockpit's tier 1: the overview); the
        /// resources need the Deep scanner (tier 2), which the report says instead of an empty list.</summary>
        private float AddPlanetScanSection(float y, string bodyId)
        {
            var btn = UiKit.AddButton(_detail, 8, y, 330, 44, L("ui.planetscan.button"), () =>
            {
                ClientAudio.Instance?.Cue("terrain_scan");
                Game.Network?.SendPlanetScan(bodyId);
            });
            SetInteractable(btn, AboardShipNow());
            y += 52f;

            BlocksBeyondTheStars.Networking.Messages.PlanetScanResult scan = null;
            if (!string.IsNullOrEmpty(bodyId))
            {
                Game.PlanetOverviews.TryGetValue(bodyId, out scan);
            }
            else
            {
                scan = Game.LastPlanetScan;
            }

            return scan != null ? AddPlanetScanReport(y, scan) : y;
        }

        /// <summary>The planet report: the overview rows (#2239), then the world's richness, every vein (how common,
        /// from which depth, which drill) and the extras — or, below the Deep scanner's tier, a line saying so.</summary>
        private float AddPlanetScanReport(float y, BlocksBeyondTheStars.Networking.Messages.PlanetScanResult scan)
        {
            UiKit.AddText(_detail, 8, y, 620, 30, string.Format(L("ui.planetscan.title"), scan.BodyName), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 36f;

            if (scan.Rows != null && scan.Rows.Length > 0)
            {
                var dangerCol = PlanetOverviewCard.LevelColor(scan.Danger);
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.overview.danger_" + Mathf.Clamp(scan.Danger, 0, 2)), 19, dangerCol, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                foreach (var row in scan.Rows)
                {
                    var col = PlanetOverviewCard.LevelColor(row.Level);
                    var icon = UiKit.Icon(PlanetOverviewCard.TopicIcon(row.Topic));
                    if (icon != null)
                    {
                        UiKit.AddIconSprite(_detail, 8, y + 2, 22, icon, col);
                    }

                    string value = L(row.ValueKey) + (string.IsNullOrEmpty(row.Extra) ? string.Empty : " (" + row.Extra + ")")
                                   + (string.IsNullOrEmpty(row.DetailKey) ? string.Empty : " · " + L(row.DetailKey));
                    UiKit.AddText(_detail, 38, y, 150, 26, L("ui.overview.topic." + row.Topic), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
                    UiKit.AddText(_detail, 190, y, 440, 26, value, 17, col, TextAnchor.UpperLeft);
                    y += 28f;
                }

                y += 8f;
            }

            if (scan.ResourcesLocked)
            {
                var locked = UiKit.AddText(_detail, 8, y, 620, 52, L("ui.overview.resources_locked"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                locked.horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 56f;
            }

            UiKit.AddText(_detail, 8, y, 620, 26, $"{L("ui.planetscan.richness")}: {L("ui.planetscan.richness_" + scan.Richness)}", 19, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 32f;

            if (scan.Ores.Length == 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.planetscan.no_ores"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 30f;
            }

            foreach (var ore in scan.Ores)
            {
                string name = Game.Localizer != null && Game.Localizer.Has($"block.{ore.Block}.name") ? L($"block.{ore.Block}.name") : ore.Block;
                string line = string.Format(L("ui.planetscan.ore_line"), name, L("ui.planetscan.abundance_" + ore.Abundance), ore.MinDepth);
                if (ore.RareTier)
                {
                    line += "  ·  " + L("ui.planetscan.tier2");
                }

                Color col = ore.Abundance >= 2 ? UiKit.Cyan : ore.Abundance == 1 ? UiKit.TextCol : UiKit.CyanDim;
                UiKit.AddText(_detail, 20, y, 608, 26, line, 18, col, TextAnchor.UpperLeft);
                y += 28f;
            }

            y += 6f;
            foreach (var (on, key) in new[]
                     {
                         (scan.GasWorld, "ui.planetscan.gas_world"),
                         (scan.SurfaceOutcrops, "ui.planetscan.outcrops"),
                         (scan.CraterMetals, "ui.planetscan.crater_metals"),
                         (scan.OilPockets, "ui.planetscan.oil"),
                         (scan.DataCaches, "ui.planetscan.data_caches"),
                     })
            {
                if (!on)
                {
                    continue;
                }

                var t = UiKit.AddText(_detail, 8, y, 620, 48, "• " + L(key), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 50f;
            }

            return y + 8f;
        }

        private float BuildMapList()
        {
            var map = Game.StarMap;
            if (map == null || map.Systems.Length == 0)
            {
                UiKit.AddText(_listContent, 8, 0, 700, 30, L("ui.map.loading"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return 40f;
            }

            var sys = SelectedSystem();
            if (sys == null)
            {
                return 0f;
            }

            bool isCurrent = sys.Id == CurrentSystemId();
            float y = 0f;

            // The flight context action (launch into space / take helm / leave space) lives in the CURRENT
            // system view — that's where you enter/leave flight from.
            if (isCurrent)
            {
                y = BuildFlightAction();
            }

            // Every OTHER system is a "hyperjump here" target — you arrive in flight and fly to its worlds.
            // A system you've never entered hides its bodies, so the jump entry is all it shows. A KNOWN system
            // (entered before, #1638) lists its bodies below, but with Instant Travel off they stay locked until
            // you have LANDED on them — so a system you only ever jumped into would otherwise be unreachable again.
            if (!isCurrent)
            {
                bool knownSystem = Game.KnowsSystem(sys.Id);
                UiKit.AddText(_listContent, 8, y, 760, 56, L(knownSystem ? "ui.map.system_known_jump" : "ui.map.system_unexplored"), 19, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 64f;
                y = AddSystemJumpButton(_listContent, 0, y, 760, sys.Id);
                if (HasClientLane(CurrentSystemId(), sys.Id))
                {
                    UiKit.AddText(_listContent, 8, y, 760, 24, "⇄ " + L("ui.map.lane_hint"), 16, UiKit.Cyan, TextAnchor.UpperLeft);
                    y += 30f;
                }

                if (!knownSystem)
                {
                    return y;
                }

                y += 12f; // then the system's bodies (landed ones are quick-travel targets)
            }

            // The selected system's bodies (reachable targets).
            foreach (var b in sys.Bodies)
            {
                bool here = b.Id == map.ActiveLocationId;
                bool isStation = b.Kind == "SpaceStation";
                string kindLabel = isStation ? StationKindLabel(b.Id) : IdLabel("ui.map.kind_", b.Kind);
                string status = here ? L("ui.map.here") : $"{kindLabel}  {IdLabel("ui.map.status_", b.Status)}";

                // A space station shows whose it is: yours, another player's, or none (procedural/NPC).
                if (isStation && !string.IsNullOrEmpty(b.OwnerName))
                {
                    status += b.OwnerName == Game.LocalPlayerId
                        ? "   ◆ " + L("ui.map.your_station")
                        : "   ◆ " + L("ui.map.station_of") + " " + b.OwnerName;
                }

                // Show the party: which players are currently on this body.
                if (map.Players != null)
                {
                    var names = map.Players.Where(p => p.LocationId == b.Id).Select(p => p.Name).ToList();
                    if (names.Count > 0)
                    {
                        status += "   ◈ " + string.Join(", ", names);
                    }
                }

                // Fixed landing pads (item 38): show free/total, or a FULL warning when every pad is taken.
                if (b.PadsTotal > 0)
                {
                    status += b.PadsFree == 0
                        ? "   ⊕ " + L("ui.map.pads_full")
                        : $"   ⊕ {b.PadsFree}/{b.PadsTotal}";
                }

                // The player's own colour mark, so a marked planet is spottable straight from the list.
                int listMark = MarkerColorOf(b.Id);
                if (listMark >= 0 && listMark < PlanetMarkerPalette.Count)
                {
                    status += "   ● " + L(PlanetMarkerPalette.NameKeys[listMark]);
                }

                // Your own claims on this body: a station orbiting it, and/or a founded base on it.
                if (Game.HasMyStation(b.Id))
                {
                    status += "   ◆ " + L("ui.map.station_here");
                }

                string mapBase = Game.MyBaseName(b.Id);
                if (mapBase != null)
                {
                    status += "   ⌂ " + L("ui.map.base_here") + (string.IsNullOrEmpty(mapBase) ? string.Empty : ": " + mapBase);
                }

                // Locked: a place you can't quick-travel to yet. Surfaces unlock by landing; stations by docking once.
                if (!here && !TravelUnlocked(b))
                {
                    if (isStation)
                    {
                        status += "   · " + L("ui.map.visit_to_unlock");
                    }
                    else if (!string.IsNullOrEmpty(b.PlanetType))
                    {
                        status += "   · " + L("ui.map.fly_to_unlock");
                    }
                    else if (b.Kind == "Wreck")
                    {
                        // #1664: a derelict is flown to, never travelled to — and once salvaged to nothing (#2354) it says so.
                        status += "   · " + L(b.Salvaged ? "ui.map.wreck_salvaged" : "ui.map.wreck_hint");
                    }
                    else if (b.Kind == "DebrisField")
                    {
                        status += "   · " + L("ui.map.debris_hint"); // #2353: a debris field is flown to as well
                    }
                }

                AddCard(y, b.Shown(), "cat_planet", status, here ? UiKit.Cyan : UiKit.CyanDim,
                    "body:" + b.Id, () => { _selected = "body:" + b.Id; RebuildDetail(); });
                y += 88f;
            }

            return y;
        }

        /// <summary>True when the travel screen may quick-travel to this body: Instant Travel is on, or the
        /// player has already landed there (the current body is always reachable).</summary>
        private bool TravelUnlocked(NetBody b)
        {
            var currentSystemId = CurrentSystemId();
            var targetSystem = Game.StarMap?.Systems.FirstOrDefault(s => s.Bodies.Any(body => body.Id == b.Id));
            bool crossSystem = targetSystem != null && targetSystem.Id != currentSystemId;

            // Cross-system travel ALWAYS requires having landed on the body before.
            if (crossSystem)
            {
                return Game.HasLandedOn(b.Id);
            }

            // Same-system travel: Instant Travel, currently docked/active, or landed previously.
            return Game.InstantTravel || b.Id == Game.StarMap?.ActiveLocationId || Game.HasLandedOn(b.Id);
        }

        /// <summary>The flight context action at the top of the Map tab: launch into space from a surface,
        /// take the helm again from inside the parked ship (switch back to the flight view with NO take-off,
        /// since you never landed), or leave space to land on the body you're orbiting. Returns the y below it.</summary>
        private float BuildFlightAction()
        {
            string label;
            System.Action act;
            if (Game.InSpace)
            {
                label = L("ui.space.leave");
                act = () => Game.Network?.SendLeaveSpace();
            }
            else if (Game.Visiting)
            {
                // #2725: a guest in a docked friend's ship never takes THEIR helm — the button takes us back to our own
                // ship instead (the same EnterShipIntent as U), veiled at once like every descent-less step (B34).
                label = L("ui.visit.back");
                act = () =>
                {
                    Menu?.CloseFromUi();
                    Game.BeginWorldTransition();
                    Game.Network?.SendEnterShip();
                };
            }
            else if (Game.LoadingPlanetType == "ship_interior")
            {
                // Inside the ship while it floats in space: take the helm again — this just switches back to
                // the flight view (no planet take-off — you never landed), so you simply fly on.
                label = L("ui.station.helm");
                act = () => Game.Network?.SendExitShip();
            }
            else
            {
                label = L("ui.space.enter");
                act = () => Game.Network?.SendEnterSpace();
            }

            var btn = UiKit.AddButton(_listContent, 0, 0, 760, 60, label, act);
            btn.GetComponent<Image>().color = new Color(0.13f, 0.34f, 0.52f); // space-blue accent = the primary action
            return 76f;
        }

        private SystemMapWidget _systemMap; // the small system chart; rebuilt each time the detail pane is

        private float DetailMap()
        {
            var map = Game.StarMap;
            var sys = SelectedSystem();
            float y = 0f;

            // The selected system's chart — its real layout, like the flight chart (#2745) — only once you've been to the
            // system (an unexplored one shows nothing but its single "hyperjump here" entry in the list).
            bool known = sys != null && (sys.Id == CurrentSystemId() || Game.KnowsSystem(sys.Id));
            if (known)
            {
                UiKit.AddText(_detail, 8, y, 600, 30, "★ " + sys.Shown(), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y = AddOriginalNameLine(y + 30f, sys.IsRenamed() ? sys.Name : null) + 6f;
                _systemMap = SystemMapWidget.Create(_detail, 100, y, 380, 380); // round chart, centred where the old one was
                string sel = !string.IsNullOrEmpty(_selected) && _selected.StartsWith("body:", System.StringComparison.Ordinal)
                    ? _selected.Substring(5) : string.Empty;
                // #2178: live weather glyphs only for the system you are in (no other system has live data).
                bool inThisSystem = Game != null && Game.SystemWeather.SystemId == sys.Id;
                _systemMap.Show(sys.Bodies, map.ActiveLocationId, sel, MarkerColorOf,
                    inThisSystem ? id => PlanetWeatherVisuals.GlyphMarkup(Game, id) : null);
                y += 396f;
            }

            // Below the map: the selected body's detail + a (gated) travel button. With no body picked, a hint.
            if (string.IsNullOrEmpty(_selected) || !_selected.StartsWith("body:", System.StringComparison.Ordinal))
            {
                UiKit.AddText(_detail, 8, y, 600, 30, L("ui.map.pick_destination"), 19, UiKit.CyanDim, TextAnchor.UpperLeft);
                return known ? AddPlaceNameRow(y + 40f, sys.Id, sys.CustomName) : y + 40f; // #2952: name the system you know
            }

            string id = _selected.Substring(5);
            var body = map?.Systems.SelectMany(s => s.Bodies).FirstOrDefault(b => b.Id == id);
            if (body == null)
            {
                return y;
            }

            bool isStation = body.Kind == "SpaceStation";
            UiKit.AddText(_detail, 8, y, 620, 40, body.Shown(), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y = AddOriginalNameLine(y + 40f, body.IsRenamed() ? body.Name : null) + 8f; // #2952: the real name, small
            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.kind")}: {(isStation ? StationKindLabel(body.Id) : IdLabel("ui.map.kind_", body.Kind))}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 32f;
            if (!string.IsNullOrEmpty(body.PlanetType))
            {
                UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.type")}: {L($"planet.{body.PlanetType}.name")}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 32f;
            }

            bool here = body.Id == map.ActiveLocationId;
            UiKit.AddText(_detail, 8, y, 620, 28, here ? L("ui.map.here") : IdLabel("ui.map.status_", body.Status), 20, here ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 40f;
            if (body.Kind is "Planet" or "Moon" && (here || Game.HasLandedOn(body.Id)))
            {
                y = AddPlaceNameRow(y, body.Id, body.CustomName); // #2952: a world you have been to can carry your own name
            }

            // Colour-mark this body. Asked for by a player who wanted to mark planets in space in different
            // colours: unlike the single surface waypoint, any number of bodies can carry a mark at once, and the
            // star map haloes each in its colour. A fixed named palette (not a colour picker) keeps it readable
            // and translatable. Purely local — no server involvement.
            int mark = MarkerColorOf(body.Id);
            string markLabel = mark >= 0 && mark < PlanetMarkerPalette.Count
                ? L("ui.marker.marked") + ": " + L(PlanetMarkerPalette.NameKeys[mark])
                : L("ui.marker.unmarked");
            var markBtn = UiKit.AddButton(_detail, 8, y, 330, 44, markLabel, () => CyclePlanetMarker(body.Id));
            if (mark >= 0 && mark < PlanetMarkerPalette.Count && markBtn != null)
            {
                // Tint the button itself so the current choice is visible without reading the label.
                var img = markBtn.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = Color.Lerp(img.color, PlanetMarkerPalette.Colors[mark], 0.55f);
                }
            }

            if (mark >= 0)
            {
                UiKit.AddButton(_detail, 346, y, 180, 44, L("ui.marker.clear"), () => SetPlanetMarker(body.Id, -1));
            }

            y += 52f;

            // #2140: the planet scanner — which resources this body holds. Bodies of the current star system only (the
            // server checks the same); stations and ship worlds have no ground to survey.
            if (!isStation && !string.IsNullOrEmpty(body.PlanetType) && sys != null && sys.Id == CurrentSystemId())
            {
                y = AddPlanetScanSection(y, body.Id);
            }

            // A space station: show its owner, board it (if visited), and rename it (if it's yours).
            if (isStation)
            {
                // #2632: a derelict station — what it is and what waits aboard (no air where the hull is broken).
                if (BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsDerelictId(body.Id))
                {
                    UiKit.AddText(_detail, 8, y, 600, 110, L("ui.map.derelict_detail"), 18, new Color(0.85f, 0.65f, 0.35f), TextAnchor.UpperLeft);
                    y += 118f;
                }

                bool mine = !string.IsNullOrEmpty(body.OwnerName) && body.OwnerName == Game.LocalPlayerId;
                if (!string.IsNullOrEmpty(body.OwnerName))
                {
                    UiKit.AddText(_detail, 8, y, 620, 28,
                        mine ? "◆ " + L("ui.map.your_station") : $"{L("ui.map.owner")}: {body.OwnerName}",
                        20, mine ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 36f;
                }

                if (!here)
                {
                    if (TravelUnlocked(body))
                    {
                        var boardBtn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.map.board"), () => Game.Network?.SendTravel(body.Id));
                        if (!AboardShipNow())
                        {
                            SetInteractable(boardBtn, false); // board your ship before travelling
                        }

                        y += 64f;
                    }
                    else
                    {
                        UiKit.AddText(_detail, 8, y, 600, 50, L("ui.map.visit_to_unlock"), 18, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft);
                        y += 58f;
                    }
                }

                if (mine)
                {
                    y = AddRenameRow(y, body.Name, L("ui.map.rename"), name => Game.Network?.SendSetStationName(body.Id, name));
                }

                y = BuildRelaySection(y, body);
                return y;
            }

            // A landable world: show your own claims on it (a station orbiting and/or a base on the surface).
            if (Game.HasMyStation(body.Id))
            {
                UiKit.AddText(_detail, 8, y, 620, 28, "◆ " + L("ui.map.station_here"), 20, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 32f;
            }

            string myBase = Game.MyBaseName(body.Id);
            if (myBase != null)
            {
                string label = "⌂ " + L("ui.map.base_here") + (string.IsNullOrEmpty(myBase) ? string.Empty : ": " + myBase);
                UiKit.AddText(_detail, 8, y, 620, 28, label, 20, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 36f;
                y = AddRenameRow(y, myBase, L("ui.map.rename_base"), name => Game.Network?.SendSetBaseName(body.Id, name));
            }

            // The system's derelict (#1664): not a place you travel to but one you fly to — its manifest is
            // read on approach and its plating carved in flight. Say so instead of silently offering nothing.
            if (body.Kind == "Wreck")
            {
                UiKit.AddText(_detail, 8, y, 600, 90, HudUi.ExpandKeyTokens(Game?.Localizer, L(body.Salvaged ? "ui.map.wreck_salvaged_detail" : "ui.map.wreck_detail")), 18, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft); // "flight chart ({key:FlightMap})" (#2502)
                return y + 98f;
            }

            // A debris field (#2353): the same kind of place — flown to, read on approach, carved and pulled in flight.
            if (body.Kind == "DebrisField")
            {
                UiKit.AddText(_detail, 8, y, 600, 110, HudUi.ExpandKeyTokens(Game?.Localizer, L("ui.map.debris_detail")), 18, new Color(0.95f, 0.7f, 0.45f), TextAnchor.UpperLeft);
                return y + 118f;
            }

            if (here || string.IsNullOrEmpty(body.PlanetType))
            {
                return y; // you're already here, or it isn't a landable world (belts dock differently)
            }

            if (TravelUnlocked(body))
            {
                // A reachable destination — quick-travel (a cross-system one is a hyperspace jump).
                var destSystem = map.Systems.FirstOrDefault(s => s.Bodies.Any(b => b.Id == body.Id));
                bool crossSystem = destSystem != null && destSystem.Id != CurrentSystemId();
                var travelBtn = UiKit.AddButton(_detail, 8, y, 280, 56, crossSystem ? L("ui.map.hyperjump") : L("ui.map.travel"), () => Game.Network?.SendTravel(body.Id));
                if (!AboardShipNow())
                {
                    SetInteractable(travelBtn, false); // travel happens from your ship — board it first
                }

                y += 64f;
                if (crossSystem)
                {
                    // A relay jump lane waives the generator (#1125 F-2) — say so instead of demanding one.
                    bool lane = destSystem != null && HasClientLane(CurrentSystemId(), destSystem.Id);
                    UiKit.AddText(_detail, 8, y, 620, 24,
                        lane ? "⇄ " + L("ui.map.lane_hint") : L("ui.map.hyperjump_hint"),
                        16, lane ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 30f;
                }
            }
            else
            {
                // Locked: never landed here + Instant Travel off — you must fly there and land manually. In
                // ANOTHER system "fly there" means: hyperjump into the system first (#1638) — offer that jump here,
                // so a world you've only ever seen from orbit still has a way back onto the screen.
                var lockedSystem = map.Systems.FirstOrDefault(s => s.Bodies.Any(b => b.Id == body.Id));
                bool lockedCrossSystem = lockedSystem != null && lockedSystem.Id != CurrentSystemId();
                UiKit.AddText(_detail, 8, y, 600, 56, L(lockedCrossSystem ? "ui.map.locked_cross_hint" : "ui.map.locked_hint"), 18, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft);
                y += 64f;
                if (lockedCrossSystem)
                {
                    y = AddSystemJumpButton(_detail, 8, y, 280, lockedSystem.Id);
                }
            }

            return y;
        }

        /// <summary>The violet "Hyperjump to this system" button (arrive in flight, then fly to its worlds) — the
        /// same entry the system list and the locked-body detail share (#1638). Disabled off the ship. Returns the
        /// y below it.</summary>
        private float AddSystemJumpButton(RectTransform parent, float x, float y, float width, string systemId)
        {
            var jump = UiKit.AddButton(parent, x, y, width, 60, L("ui.map.hyperjump_here"), () => Game.Network?.SendHyperjumpSystem(systemId));
            jump.GetComponent<Image>().color = new Color(0.30f, 0.18f, 0.46f); // hyperspace-violet accent
            if (!AboardShipNow())
            {
                SetInteractable(jump, false); // travel happens from your ship — board it first
            }

            return y + 76f;
        }

        /// <summary>An inline name field + confirm button for renaming an owned station or base from the Map detail
        /// pane (no modal, so it never desyncs the menu's own open/cursor state). Sends the typed name on click.</summary>
        private float AddRenameRow(float y, string current, string buttonLabel, System.Action<string> onConfirm, string placeholder = null)
        {
            var field = UiKit.AddInput(_detail, 8, y, 400, 48, current ?? string.Empty, null, placeholder ?? L("ui.base.placeholder"));
            field.characterLimit = 24;
            field.lineType = InputField.LineType.SingleLine;
            UiKit.AddButton(_detail, 416, y, 180, 48, buttonLabel, () => onConfirm?.Invoke(field.text ?? string.Empty));
            return y + 56f;
        }

        /// <summary>#2952: under a place the player named for themselves, its generated name in small type — friends still
        /// meet up on the real name. Nothing when <paramref name="original"/> is null.</summary>
        private float AddOriginalNameLine(float y, string original)
        {
            if (string.IsNullOrEmpty(original))
            {
                return y;
            }

            UiKit.AddText(_detail, 8, y, 620, 24, string.Format(L("ui.map.original_name"), original), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            return y + 24f;
        }

        /// <summary>#2952: "Your own name" for a visited system, planet or moon — private, only this player sees it; an
        /// empty field gives the place its generated name back. The server checks the visit again.</summary>
        private float AddPlaceNameRow(float y, string placeId, string current)
        {
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.map.own_name"), 18, UiKit.Cyan, TextAnchor.UpperLeft);
            return AddRenameRow(y + 28f, current, L("ui.map.name_place"), name => Game.Network?.SendSetPlaceName(placeId, name),
                L("ui.map.own_name_placeholder"));
        }

        /// <summary>The SPS relay meter for a commissioned player station in the Map detail pane (#1125):
        /// conversion progress per cost line + co-op "Deliver" buttons (any player may contribute; the server
        /// clamps to what is missing and held, and enforces being AT the station). Completed relays show as
        /// active. Hidden entirely when the data ships no relay definition or no meter exists yet.</summary>
        private float BuildRelaySection(float y, NetBody body)
        {
            var net = Game?.RelayNetwork;
            if (net == null || !net.Enabled || net.Relays == null)
            {
                return y;
            }

            var relay = System.Array.Find(net.Relays, r => r != null && r.StationId == body.Id);
            if (relay == null)
            {
                return y;
            }

            y += 6f;
            if (relay.Completed)
            {
                UiKit.AddText(_detail, 8, y, 620, 28, "⛭ " + L("ui.relay.active"), 20, UiKit.Cyan, TextAnchor.UpperLeft);
                return y + 36f;
            }

            int req = 0, got = 0;
            for (int i = 0; i < relay.Items.Length && i < relay.Required.Length && i < relay.Contributed.Length; i++)
            {
                req += relay.Required[i];
                got += Mathf.Min(relay.Contributed[i], relay.Required[i]);
            }

            int pct = req > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * got / req), 0, 100) : 0;
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.relay.title").Replace("{pct}", pct.ToString()), 20, UiKit.Cyan, TextAnchor.UpperLeft);
            y += 32f;
            UiKit.AddText(_detail, 8, y, 620, 44, L("ui.relay.hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 50f;

            for (int i = 0; i < relay.Items.Length && i < relay.Required.Length && i < relay.Contributed.Length; i++)
            {
                string item = relay.Items[i];
                int have = relay.Contributed[i];
                int need = relay.Required[i];
                UiKit.AddText(_detail, 8, y, 360, 26, ItemName(item) + ": " + have.ToString("N0") + " / " + need.ToString("N0"),
                    17, have >= need ? UiKit.Cyan : UiKit.TextCol, TextAnchor.MiddleLeft);
                if (have < need)
                {
                    // "Give everything I have" — the server clamps to missing AND held (and rejects from afar).
                    UiKit.AddButton(_detail, 376, y - 6, 220, 40, L("ui.relay.give"),
                        () => Game.Network?.SendContributeRelay(body.Id, item, int.MaxValue));
                }

                y += 44f;
            }

            return y + 4f;
        }

        // --- Coloured planet marks in the star map ---------------------------------------------------------
        // A player wanted to mark planets in space, each in its own colour — several at once, unlike the single
        // surface waypoint. Stored locally in ClientSettings (never sent to the server) and grouped by world, so
        // marks from one save don't bleed into another (body ids like "sys0-p5" repeat across saves).

        /// <summary>The save these marks belong to. Set when a local world is started or hosted; for a remote
        /// server it keeps the last local name, which only ever groups marks slightly oddly — they stay local
        /// cosmetics either way.</summary>
        private string MarkerWorld => Menu?.Settings?.LastWorld ?? string.Empty;

        /// <summary>The colour index marking a body, or -1 when unmarked.</summary>
        private int MarkerColorOf(string bodyId)
            => Menu?.Settings?.GetPlanetMarker(MarkerWorld, bodyId) ?? -1;

        /// <summary>Steps a body through the palette and off the end back to unmarked, so one button both marks
        /// and recolours — the fewest controls for the youngest player.</summary>
        private void CyclePlanetMarker(string bodyId)
        {
            int next = MarkerColorOf(bodyId) + 1;
            SetPlanetMarker(bodyId, next >= PlanetMarkerPalette.Count ? -1 : next);
        }

        private void SetPlanetMarker(string bodyId, int color)
        {
            if (Menu?.Settings == null)
            {
                return;
            }

            Menu.Settings.SetPlanetMarker(MarkerWorld, bodyId, color);
            Menu.Settings.Save();
            RebuildDetail(); // redraw the button label/tint and re-halo the orrery
        }
    }
}
