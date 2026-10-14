// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the crafting menu says about a recipe's station when it is out of reach (#2585): the reason line of the recipe
    /// page, the header hint and the name on the page's "Station:" line. Off the ship a station is a placed BLOCK (a forge
    /// for the refinery recipes), with the ship's module as the aboard alternative. Aboard, only the ship's MODULE counts —
    /// the server's <c>StationAvailable</c> never looks at a block there, a station block built into a ship is decoration
    /// (#2248) — so the texts name the module first and say where it is built, and a station no module provides (a
    /// campfire, an algae tank) says it works on a world only. A player aboard was told "needs a forge nearby", built one
    /// right beside the cabin's workbench and still could not make titanium plates.
    /// <para>Engine-free so the headless suite pins it; <c>CraftingTechShipUI</c> draws it. Station names are the server's
    /// lower-case <c>CraftingStation</c> names ("workshop", "refinery", "algaetank" …); the caller passes the BLOCK's
    /// player-facing name it already shows, and <paramref name="localize"/> resolves locale keys.</para>
    /// </summary>
    public static class CraftStationText
    {
        /// <summary>The ship module that provides a crafting station aboard, or null (mirrors the server's
        /// <c>GameServer.StationModuleFor</c>).</summary>
        public static string? ModuleKey(string? station) => station switch
        {
            "workshop" => "workshop",
            "refinery" => "refinery",
            "detoxifier" => "detoxifier",
            "transmuter" => "transmuter",
            "decontaminator" => "decontaminator", // #2067
            _ => null,
        };

        /// <summary>A station that works on a world only — no ship module provides it: the campfire and the algae tank
        /// (mirrors the server's <c>StationMissingReason</c>, which says "@srv.craft.world_only" for exactly these). The
        /// factory is no such station: a player-placed terminal never produces (#1265), only a spawned factory does.</summary>
        public static bool IsWorldOnly(string? station) => station is "campfire" or "algaetank";

        /// <summary>The reason line for a recipe whose station is out of reach. Off the ship: "Needs a Forge nearby — or
        /// your ship's Refinery module while you're aboard." (the module clause only for stations a module provides).
        /// Aboard: the module, where it is built and that the block in the ship is only decoration — or, for a
        /// <see cref="IsWorldOnly"/> station, that it works on a world only.</summary>
        public static string NeedStation(string station, string blockName, bool aboard, Func<string, string> localize)
        {
            string? module = ModuleKey(station);
            if (aboard && module != null)
            {
                return localize("ui.craft.need_module_aboard").Replace("{module}", ModuleName(module, localize)).Replace("{block}", blockName);
            }

            if (aboard && IsWorldOnly(station))
            {
                return localize("ui.craft.need_world_only").Replace("{block}", blockName);
            }

            string text = module != null ? localize("ui.craft.need_block_or_module") : localize("ui.craft.need_block");
            return text.Replace("{block}", blockName).Replace("{module}", module != null ? ModuleName(module, localize) : string.Empty);
        }

        /// <summary>The header hint for a recipe whose station is out of reach — one short line above the panels: "This
        /// recipe needs a Forge in reach." off the ship; aboard the module and its tab, or "only on a world".</summary>
        public static string Hint(string station, string blockName, bool aboard, Func<string, string> localize)
        {
            if (aboard && ModuleKey(station) is { } module)
            {
                return localize("ui.craft.hint_need_module").Replace("{module}", ModuleName(module, localize));
            }

            if (aboard && IsWorldOnly(station))
            {
                return localize("ui.craft.hint_world_only").Replace("{block}", blockName);
            }

            return localize("ui.craft.hint_need_block").Replace("{block}", blockName);
        }

        /// <summary>The header hint when NO station at all is in reach and no recipe asks for one: "hand recipes only — for
        /// everything else, stand at a Workbench" off the ship. Aboard a workbench does nothing (it is decoration in a
        /// ship), so the line names the ship's workshop module instead — the case of a ship without one, such as a
        /// commissioned self-built ship.</summary>
        public static string HandOnlyHint(string workbenchName, bool aboard, Func<string, string> localize)
            => aboard
                ? localize("ui.craft.hint_hand_only_aboard").Replace("{module}", ModuleName("workshop", localize))
                : localize("ui.craft.hint_hand_only").Replace("{block}", workbenchName);

        /// <summary>The name on the recipe page's "Station:" line: the block off the ship, the module aboard ("Refinery
        /// module") for a station a module provides.</summary>
        public static string StationLineName(string station, string blockName, bool aboard, Func<string, string> localize)
            => aboard && ModuleKey(station) is { } module
                ? localize("ui.craft.station_module").Replace("{module}", ModuleName(module, localize))
                : blockName;

        /// <summary>Whether the menu offers to craft the station's BLOCK ("Craft one →") while the station is missing. Not
        /// aboard for a station a module provides — the block would be decoration in the ship; that offer is how the forge
        /// ended up beside the cabin's workbench — nor for one that works on a world only, while the hint says exactly
        /// that.</summary>
        public static bool OffersStationBlock(string station, bool aboard)
            => !aboard || (ModuleKey(station) == null && !IsWorldOnly(station));

        private static string ModuleName(string module, Func<string, string> localize) => localize("module." + module + ".name");
    }
}
