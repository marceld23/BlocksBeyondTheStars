// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's vendor reach (#2626) — ONE rule for the "Trade" prompt, E's trade-or-talk, the vendor screen and the
    /// crafting menu's market page, mirroring the server's barter check (<see cref="MarketStock"/>): the server accepts a
    /// barter while the player stands within <see cref="MarketStock.TradeReach"/> (4) of a trading post or within
    /// <see cref="MarketStock.VendorReach"/> (6) of a vendor, and a themed deal while a vendor selling it stands within 6.
    /// The client does not know where every post is (a settlement's stall, a base's or a station's post), so it measures to
    /// the vendor alone, never more generously than the server:
    /// <list type="bullet">
    /// <item><b>Can trade here</b> (the prompt, E, opening the screen): the nearest vendor within 4 — the server always
    /// accepts that (a vendor within 6 counts), and 4 is the counter distance the prompt has always meant.</item>
    /// <item><b>The chosen vendor still serves</b> (the open screen): the vendor the player picked stays bound while it
    /// stands within 6 — exactly the server's vendor reach, so a keeper stepping about behind the counter no longer empties
    /// the list or swaps it for another vendor's.</item>
    /// </list>
    /// Every distance is measured the short way round both seams (<see cref="InteractReach.SeamDistanceSquared"/>): the
    /// server sends NPCs canonical while the player's scene position runs unbounded.
    /// </summary>
    public static class VendorReach
    {
        /// <summary>The NPC role every trader carries (the classic vendor, the trading professions, visiting pilots and the
        /// travelling merchant).</summary>
        public const string VendorRole = "vendor";

        /// <summary>The vendor the player can start trading with from (<paramref name="x"/>, <paramref name="y"/>,
        /// <paramref name="z"/>): the nearest vendor within <see cref="MarketStock.TradeReach"/>, or null.</summary>
        public static NetNpc? NearestVendor(IReadOnlyList<NetNpc>? npcs, float x, float y, float z, int circumference)
        {
            if (npcs == null)
            {
                return null;
            }

            NetNpc? best = null;
            double bestSq = MarketStock.TradeReach * (double)MarketStock.TradeReach;
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n == null || n.Role != VendorRole)
                {
                    continue;
                }

                double sq = InteractReach.SeamDistanceSquared(x, y, z, n.X, n.Y, n.Z, circumference);
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = n;
                }
            }

            return best;
        }

        /// <summary>The live NPC with id <paramref name="id"/>, or null (gone, or 0 = none).</summary>
        public static NetNpc? ById(IReadOnlyList<NetNpc>? npcs, int id)
        {
            if (npcs == null || id == 0)
            {
                return null;
            }

            for (int i = 0; i < npcs.Count; i++)
            {
                if (npcs[i] != null && npcs[i].Id == id)
                {
                    return npcs[i];
                }
            }

            return null;
        }

        /// <summary>Whether the vendor bound to an open trade screen still serves the player at (<paramref name="x"/>,
        /// <paramref name="y"/>, <paramref name="z"/>): it is a vendor and stands within <see cref="MarketStock.VendorReach"/>.</summary>
        public static bool StillServes(NetNpc? vendor, float x, float y, float z, int circumference)
            => vendor != null && vendor.Role == VendorRole
               && InteractReach.SeamDistanceSquared(x, y, z, vendor.X, vendor.Y, vendor.Z, circumference)
                  <= MarketStock.VendorReach * (double)MarketStock.VendorReach;

        /// <summary>The theme of the vendor the player can trade with right now ("" when none is in reach — aboard, the
        /// ship's console has no theme).</summary>
        public static string NearestVendorTheme(IReadOnlyList<NetNpc>? npcs, float x, float y, float z, int circumference)
            => NearestVendor(npcs, x, y, z, circumference)?.Theme ?? string.Empty;

        /// <summary>How a vendor is named on the trade-or-talk prompt and the trade screen's title: "Name · Role", or just
        /// the one that is known ("" when neither is).</summary>
        public static string Label(string? name, string? role)
            => string.IsNullOrEmpty(name) ? role ?? string.Empty
                : string.IsNullOrEmpty(role) ? name!
                : name + " · " + role;

        /// <summary>The locale key of the trade screen's header line for a vendor of <paramref name="theme"/> (#2629) —
        /// <c>ui.vendor.theme.&lt;theme&gt;</c>, lower case; "" for no theme (the ship's own console).</summary>
        public static string ThemeHeaderKey(string? theme)
            => string.IsNullOrEmpty(theme) ? string.Empty : "ui.vendor.theme." + theme!.ToLowerInvariant();
    }
}
