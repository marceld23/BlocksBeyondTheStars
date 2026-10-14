// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The radios as the client sees them (#2567/#2569): which one is worn — text and voice comms work only while a
    /// radio sits in a module slot of the suit (#2110), holding one does nothing — what it reaches, and which carried one
    /// the radio tab's "Put on radio" button wears. Engine-free so the headless suite pins it; the authoritative gate is
    /// the server's (<c>GameServer.HasAnyRadio</c>).
    /// </summary>
    public static class RadioGear
    {
        /// <summary>The radio item keys, widest reach first (the tiers stack: the widest worn one decides the reach).</summary>
        public static readonly IReadOnlyList<string> Keys = new[] { "galaxy_radio", "system_radio", "comm_radio" };

        /// <summary>True for any radio (a lab-changed piece too: the base key decides).</summary>
        public static bool IsRadio(string? item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return false;
            }

            string key = ItemKey.Base(item!);
            foreach (var radio in Keys)
            {
                if (key == radio)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The widest-reaching radio worn, or null when none is. <paramref name="wears"/> answers "is the piece
        /// with this base key worn" (<c>GameBootstrap.Wears</c>).</summary>
        public static string? Worn(Func<string, bool>? wears)
        {
            if (wears == null)
            {
                return null;
            }

            foreach (var radio in Keys)
            {
                if (wears(radio))
                {
                    return radio;
                }
            }

            return null;
        }

        /// <summary>Locale key of a radio's reach — the suit picker's effect line: the planet, the star system, the galaxy.</summary>
        public static string ReachLabelKey(string? radio) => radio == null ? "ui.equip.effect.radio_planet" : ItemKey.Base(radio) switch
        {
            "galaxy_radio" => "ui.equip.effect.radio_galaxy",
            "system_radio" => "ui.equip.effect.radio_system",
            _ => "ui.equip.effect.radio_planet",
        };

        /// <summary>The radio to put on: the widest-reaching one in the backpack (quick-bar included), else in the ship's
        /// hold — the client knows the hold only aboard, and only aboard may the server wear from it. Null when none is
        /// carried.</summary>
        public static (NetItemStack Stack, bool FromCargo)? Carried(IEnumerable<NetItemStack>? personal, IEnumerable<NetItemStack>? cargo)
        {
            foreach (var radio in Keys)
            {
                if (Find(personal, radio) is { } stack)
                {
                    return (stack, false);
                }
            }

            foreach (var radio in Keys)
            {
                if (Find(cargo, radio) is { } stack)
                {
                    return (stack, true);
                }
            }

            return null;
        }

        private static NetItemStack? Find(IEnumerable<NetItemStack>? stacks, string radio)
        {
            if (stacks == null)
            {
                return null;
            }

            foreach (var s in stacks)
            {
                if (s != null && s.Count > 0 && !string.IsNullOrEmpty(s.Item) && ItemKey.Base(s.Item) == radio)
                {
                    return s;
                }
            }

            return null;
        }
    }
}
