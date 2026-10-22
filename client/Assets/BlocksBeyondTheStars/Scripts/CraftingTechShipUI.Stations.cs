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
    /// Station gates of <see cref="CraftingTechShipUI"/> (#1070/#1071/#1074) and the "Where?" locator (#1072):
    /// which station a recipe or tab needs, whether the player is in reach, the gate hints and the live
    /// distance/arrow readout.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private float _whereRefreshedAt;

        /// <summary>The gate row (#1071/#1072): hint ("Hand recipes only here — everything else needs a
        /// Workbench" / "Research happens at your ship's cockpit" / …), the live "where" readout, and the
        /// Show / craft-one buttons. Asks the server for the nearest station whenever a gate is missing.</summary>
        private void RefreshGateRow(bool production)
        {
            string missing = production ? MissingStation() : null;
            if (_mode == Mode.Map && !AboardShipNow())
            {
                _hint.text = L("ui.map.need_ship");
            }
            else
            {
                _hint.text = GateHint(missing);
            }

            if (missing != null)
            {
                RequestLocate(missing);
            }

            var loc = CurrentLocation(missing);
            _whereText.text = missing == null ? string.Empty : WhereText(missing);
            _whereShow.gameObject.SetActive(loc != null && loc.Found);
            // #2585: aboard, a station a module provides is not offered as a block to craft — built into the ship it would
            // be decoration (that is how a forge ended up beside the cabin's workbench); the hint names the module instead.
            _whereCraft.gameObject.SetActive(missing != null && loc != null && !loc.Found
                && CraftStationText.OffersStationBlock(missing, AboardShipNow()) && RecipeForStationBlock(missing) != null);
        }

        // ------------------------------------------------------------------------------------------------
        // Station gates (#1070/#1071/#1072/#1074).
        //
        // The SERVER decides which stations are in reach (Game.StationsAvailable / ResearchOk / ShipBuildOk,
        // from StationsInReach). The client used to guess from ship station markers only — so a base
        // workbench, a placed forge and even hand recipes read as blocked here while the server would have
        // crafted. It also gated per TAB ("go to the workshop") while the server gates per RECIPE (a forge
        // recipe needs a forge). Everything below names the BLOCK the player has to stand at, with its icon.
        // ------------------------------------------------------------------------------------------------

        /// <summary>The server's lower-case name of a recipe's station ("workshop", "refinery", …).</summary>
        private static string StationKeyOf(CraftingStation s) => s.ToString().ToLowerInvariant();

        /// <summary>The placed world block that provides a crafting station (mirrors the server's
        /// <c>StationBlockFor</c>); "research" and "shipbuild" are the two non-crafting gates.</summary>
        private static string StationBlockKey(string station) => station switch
        {
            "workshop" => "workbench",
            "refinery" => "forge",
            "detoxifier" => "detoxifier",
            "transmuter" => "matter_forge",
            "decontaminator" => "decontaminator", // #2067
            "algaetank" => "algae_tank",
            "campfire" => "campfire",
            "factory" => "factory_terminal",
            "research" => "data_cache", // the cockpit's terminal block (#1009)
            "shipbuild" => "workbench", // the workshop module's bench aboard
            _ => null,
        };

        /// <summary>Player-facing name of a station gate — the BLOCK's name (Workbench, Forge, …), the
        /// cockpit for research, the workshop module for ship building.</summary>
        private string StationName(string station)
        {
            switch (station)
            {
                case "research": return L("ui.station.cockpit");
                case "shipbuild": return L("module.workshop.name");
                default:
                    string block = StationBlockKey(station);
                    return block == null ? station : L("block." + block + ".name");
            }
        }

        /// <summary>The icon sprite for a station gate (the block's atlas tile / item icon), or null.</summary>
        private Sprite StationSprite(string station)
        {
            string block = StationBlockKey(station);
            return block == null ? null : IconResolver.Resolve(block, Game);
        }

        // A host from before #1070 never sends StationsInReach. Until the first one arrives the menu falls back
        // to the old ship-marker heuristic (workshop marker = crafting; research ungated; console/cockpit =
        // ship building) so a new client on an old LAN host isn't locked out of every bench.
        private bool LegacyAtWorkshop() => (Game.NearbyStation ?? string.Empty) == "workshop";

        private bool AnyStationInReach() => Game.StationsKnown ? Game.StationsAvailable.Count > 0 : LegacyAtWorkshop();

        private bool ResearchOkNow() => !Game.StationsKnown || Game.ResearchOk;

        private bool ShipBuildOkNow() => Game.StationsKnown ? Game.ShipBuildOk : (Game.NearbyStation ?? string.Empty) is "console" or "cockpit";

        /// <summary>True when the server says this recipe's station is usable right now (hand: always).</summary>
        private bool StationAvailable(CraftingStation s)
            => s == CraftingStation.Hand || (Game.StationsKnown ? Game.StationsAvailable.Contains(StationKeyOf(s)) : LegacyAtWorkshop());

        /// <summary>The station gate the current tab is missing, or null when nothing is missing: the selected
        /// recipe's station (Crafting), "research" (Tech) or "shipbuild" (Ship). With no recipe selected the
        /// Crafting tab falls back to the everyday bench when NO station at all is in reach.</summary>
        private string MissingStation()
        {
            switch (_mode)
            {
                case Mode.Tech:
                    return ResearchOkNow() ? null : "research";
                case Mode.Ship:
                    return ShipBuildOkNow() ? null : "shipbuild";
                case Mode.Crafting:
                    if (!string.IsNullOrEmpty(_selected) && Game.Content.Recipes.TryGetValue(_selected, out var r)
                        && r.Station != CraftingStation.Hand && r.Station != CraftingStation.Market
                        && r.Station != CraftingStation.Factory && !StationAvailable(r.Station))
                    {
                        return StationKeyOf(r.Station);
                    }

                    return !AnyStationInReach() ? "workshop" : null;
                default:
                    return null;
            }
        }

        /// <summary>The reason line for a recipe whose station is out of reach: "Needs a 🔧 Workbench nearby
        /// — or your ship's Workshop module." (module clause only for stations a module can provide). #2585: aboard,
        /// the ship's module comes first — where it is built, and that the block in the ship is only decoration
        /// (<see cref="CraftStationText.NeedStation"/>).</summary>
        private string NeedStationText(string station)
        {
            if (station == "research")
            {
                return L("ui.craft.need_research");
            }

            if (station == "shipbuild")
            {
                return AboardShipNow() ? L("ui.craft.hint_shipbuild_module") : L("ui.craft.hint_shipbuild_aboard");
            }

            return CraftStationText.NeedStation(station, StationName(station), AboardShipNow(), L);
        }

        /// <summary>Header hint for a tab whose gate is not met (empty when it is).</summary>
        private string GateHint(string missing)
        {
            switch (missing)
            {
                case null: return string.Empty;
                case "research": return L("ui.craft.hint_research");
                case "shipbuild":
                    return AboardShipNow() ? L("ui.craft.hint_shipbuild_module") : L("ui.craft.hint_shipbuild_aboard");
                default:
                    // No station at all in reach → "hand recipes only here"; a specific recipe's station → its block, or
                    // aboard (#2585) the ship's module / "only on a world" — and with no station aboard, the workshop
                    // module rather than a workbench, which is decoration in a ship.
                    return !AnyStationInReach() && missing == "workshop" && !SelectedRecipeNeeds(missing)
                        ? CraftStationText.HandOnlyHint(StationName(missing), AboardShipNow(), L)
                        : CraftStationText.Hint(missing, StationName(missing), AboardShipNow(), L);
            }
        }

        private bool SelectedRecipeNeeds(string station)
            => !string.IsNullOrEmpty(_selected) && Game.Content.Recipes.TryGetValue(_selected, out var r)
               && StationKeyOf(r.Station) == station;

        /// <summary>Footer: the stations the server says are in reach right now, by block name.</summary>
        private string InReachText()
        {
            if (!AnyStationInReach())
            {
                return L("ui.craft.in_reach_none");
            }

            var names = new System.Collections.Generic.List<string>();
            foreach (var s in new[] { "workshop", "refinery", "detoxifier", "transmuter", "decontaminator", "algaetank", "campfire", "factory" })
            {
                if (Game.StationsAvailable.Contains(s))
                {
                    names.Add(StationName(s));
                }
            }

            return L("ui.craft.in_reach").Replace("{list}", string.Join(", ", names));
        }

        // The player counts as "aboard" for travel when on/in the ship, piloting in space, or inside the ship
        // interior floating in space — i.e. not on foot out on a surface.
        private bool AboardShipNow() => Game.Aboard || Game.InSpace || Game.LoadingPlanetType == "ship_interior";

        // Whether a tab's function is usable right now. Used only to DIM out-of-reach tabs in the header — they
        // stay clickable so the player can still browse content (the action buttons inside enforce the gate).
        // Crafting dims only when NO station is in reach (hand recipes always work); Tech needs the cockpit,
        // Ship the workshop module aboard, Map the ship. Tabs without a context requirement are always available.
        private bool IsTabAvailable(Mode mode) => mode switch
        {
            Mode.Crafting => AnyStationInReach(),
            Mode.Tech => ResearchOkNow(),
            Mode.Ship => ShipBuildOkNow(),
            Mode.Map => AboardShipNow(),
            _ => true,
        };

        /// <summary>The station a dimmed tab is waiting for (icon badge on the tab button).</summary>
        private static string TabStation(Mode mode) => mode switch
        {
            Mode.Crafting => "workshop",
            Mode.Tech => "research",
            Mode.Ship => "shipbuild",
            _ => null,
        };

        // --- "Where?" locator (#1072) ---

        private string _locateStation;   // the station the last request asked about
        private float _locateSentAt = -99f;
        private Text _whereText;
        private Button _whereShow, _whereCraft;

        /// <summary>Asks the server where the nearest matching station is (once per station + every few
        /// seconds while the hint stays up, so walking around refreshes the distance).</summary>
        private void RequestLocate(string station)
        {
            if (Game?.Network == null || string.IsNullOrEmpty(station))
            {
                return;
            }

            if (station != _locateStation || Time.unscaledTime - _locateSentAt > 4f)
            {
                _locateStation = station;
                _locateSentAt = Time.unscaledTime;
                Game.Network.SendLocateStation(station);
            }
        }

        /// <summary>The locate answer that belongs to the station currently missing, or null.</summary>
        private StationLocation CurrentLocation(string missing)
        {
            var loc = Game?.LastStationLocation;
            return loc != null && !string.IsNullOrEmpty(missing) && loc.Station == missing ? loc : null;
        }

        /// <summary>"Workbench · 12 m ↗" / "aboard your ship · 40 m ←" / "none within 24 m", live-updated
        /// while the menu is open (distance + arrow follow the player).</summary>
        private string WhereText(string missing)
        {
            var loc = CurrentLocation(missing);
            if (loc == null)
            {
                return string.Empty;
            }

            if (!loc.Found)
            {
                return L("ui.craft.where_none");
            }

            var target = Game.ScenePos(loc.X + 0.5f, loc.Y, loc.Z + 0.5f);
            float dx = target.x - Game.PlayerPosition.x, dz = target.z - Game.PlayerPosition.z;
            int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            float rel = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg - Game.PlayerYaw; // 0 = straight ahead
            string arrow = DirectionArrow(rel);
            string what = loc.Kind == "ship" ? L("ui.craft.where_ship") : StationName(missing);
            return L("ui.craft.where").Replace("{what}", what).Replace("{dist}", dist.ToString()).Replace("{dir}", arrow);
        }

        /// <summary>One of eight arrows for a bearing relative to the player's facing (0 = ahead).</summary>
        private static string DirectionArrow(float relDeg)
        {
            int oct = Mathf.RoundToInt(Mathf.Repeat(relDeg, 360f) / 45f) % 8;
            return oct switch { 0 => "↑", 1 => "↗", 2 => "→", 3 => "↘", 4 => "↓", 5 => "↙", 6 => "←", _ => "↖" };
        }

        /// <summary>The unlocked recipe that produces the block a missing station needs, or null.</summary>
        private RecipeDefinition RecipeForStationBlock(string missing)
        {
            string block = StationBlockKey(missing);
            if (block == null || missing is "research" or "shipbuild" || Game?.Content == null)
            {
                return null;
            }

            foreach (var r in Game.Content.Recipes.Values)
            {
                foreach (var o in r.Outputs)
                {
                    if (o.Item == block && BlueprintOk(r.RequiredBlueprint))
                    {
                        return r;
                    }
                }
            }

            return null;
        }

        /// <summary>Puts the located station on the HUD compass (surface waypoint) and closes nothing —
        /// the player keeps browsing, the compass now points at the bench.</summary>
        private void MarkLocatedStation(string missing)
        {
            var loc = CurrentLocation(missing);
            if (loc == null || !loc.Found)
            {
                return;
            }

            Game.Waypoint = new Vector3(loc.X + 0.5f, 0f, loc.Z + 0.5f);
            Game.ShowMessage(L("ui.craft.where_marked"));
        }

        /// <summary>Called by GameMenu when the menu closes: if the player was just told where a station is,
        /// drop a through-wall marker on it for a few seconds so "over there" is visible in the world.</summary>
        public void OnMenuClosed()
        {
            var loc = Game?.LastStationLocation;
            if (loc == null || !loc.Found || _locateStation == null || loc.Station != _locateStation)
            {
                return;
            }

            var target = Game.ScenePos(loc.X + 0.5f, loc.Y, loc.Z + 0.5f);
            if ((target - Game.PlayerPosition).sqrMagnitude > 40f * 40f)
            {
                return; // too far to be worth a marker (and out of the locate radius anyway)
            }

            OreScanView.Instance?.ShowStationMarker(loc.X, loc.Y, loc.Z, 8f);
            _locateStation = null; // one marker per hint, not on every close
        }

        /// <summary>Jump to a recipe: the one that produces the missing station's block ("craft one →"), or the suit slot
        /// picker's "Show recipe" (#2289).</summary>
        private void JumpToRecipe(string recipeKey)
        {
            Menu?.OpenCrafting();
            // Switch the page NOW: GameMenu only shows the new tab on its next Update, and that ShowMode would clear the
            // selection set below when the jump comes from another tab (the Suit picker, a Blueprints/Ship where-hint).
            ShowMode(Mode.Crafting);
            _selected = recipeKey;
            _category = "all";
            _search = string.Empty;
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
        }

        /// <summary>Signature of the station gate state for the refresh hash (#1070).</summary>
        private int StationsSig()
        {
            int sig = (Game.ResearchOk ? 17 : 0) + (Game.ShipBuildOk ? 29 : 0);
            foreach (var s in Game.StationsAvailable)
            {
                unchecked { sig += s.GetHashCode(); }
            }

            var loc = Game.LastStationLocation;
            if (loc != null)
            {
                unchecked { sig += loc.Station.GetHashCode() * 3 + (loc.Found ? loc.X * 5 + loc.Y * 7 + loc.Z * 11 : 13); }
            }

            return sig;
        }
    }
}
