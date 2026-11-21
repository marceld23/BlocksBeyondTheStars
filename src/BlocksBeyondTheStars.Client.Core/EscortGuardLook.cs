// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The sleeper's look (#3897): an NPC whose <c>NetNpc.Look</c> is <see cref="LookKey"/> is a guard of the escort wing —
    /// a human figure in the suit, wearing the full Guardian armour set exactly as a player wears it (the set a player
    /// forges from guardian plates IS the wing's old field armour, canon C20), but with its lights BLUE, as they were
    /// before the night the Guardian core took the network. The Guardian sword hangs in his hand at his side while he
    /// stands. Nothing of this rides on the wire beyond the look id: no gear mask, no presence field.
    /// </summary>
    public static class EscortGuardLook
    {
        /// <summary>The look id the server sends (<c>GameServer.SleeperLook</c>).</summary>
        public const string LookKey = "sps_guard";

        /// <summary>The item whose held model he carries.</summary>
        public const string SwordItemKey = "guardian_sword";

        /// <summary>The blue of his armour's lights — the blue lamp's light colour (<c>light_blue</c>, 0x3D8CFF).</summary>
        public const int LightRgb = 0x3D8CFF;

        /// <summary>What the NPC view holds for him when the server names no tool: the sword (a client-side id, never on
        /// the wire).</summary>
        public const string SwordHeldId = "escort_sword";

        /// <summary>True for the sleeper's look.</summary>
        public static bool Is(string? look) => look == LookKey;

        /// <summary>What the figure of <paramref name="look"/> holds: the server's <paramref name="held"/> for everybody,
        /// and for the guard standing (<paramref name="pose"/> 0) with nothing named, his sword
        /// (<see cref="SwordHeldId"/>). Sitting or asleep he has put it away.</summary>
        public static string HeldFor(string? look, int pose, string? held)
        {
            string h = held ?? string.Empty;
            return Is(look) && pose == 0 && h.Length == 0 ? SwordHeldId : h;
        }

        /// <summary>The Guardian sword's own held model with its lights turned blue: every glowing (<c>energy</c>) part's
        /// red and blue change places (#ff2a2a → #2a2aff, #ffe0e0 → #e0e0ff); the dark metal stays. Empty when the item
        /// has no model (the caller then draws the item as it is).</summary>
        public static List<HeldModelPart> SwordParts(ItemDefinition? sword)
        {
            var parts = new List<HeldModelPart>();
            if (sword?.HeldModel == null)
            {
                return parts;
            }

            foreach (var part in sword.HeldModel)
            {
                parts.Add(new HeldModelPart { P = part.P, S = part.S, G = part.G, M = part.M, C = part.M == "energy" ? SwapRedBlue(part.C) : part.C });
            }

            return parts;
        }

        /// <summary><c>#rrggbb</c> → <c>#bbggrr</c>; anything else is returned as it is.</summary>
        public static string SwapRedBlue(string? hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#')
            {
                return hex ?? string.Empty;
            }

            return "#" + hex.Substring(5, 2) + hex.Substring(3, 2) + hex.Substring(1, 2);
        }

        /// <summary>The gear mask of the full Guardian set — helmet, chest, legs and boots — built by the rule every
        /// player's mask is built by, so he wears exactly the pieces a player would.</summary>
        public static int GearMask
            => GearLook.Mask(key => key is "guardian_helmet" or "guardian_chest" or "guardian_legs" or "guardian_boots");
    }
}
