// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free diff over two personal-inventory snapshots, feeding the HUD pickup feed (#745).
    /// The server sends the whole inventory wholesale on every change, so "what did I just collect?"
    /// is the per-item total AFTER minus BEFORE — positive deltas only. Losses (placing, eating,
    /// selling) are not pickups and never surface. Lives here (not in the MonoBehaviour) so the
    /// aggregation rules are covered by plain .NET tests.
    /// </summary>
    public static class InventoryDiff
    {
        /// <summary>
        /// Aggregated item gains between two inventory snapshots, in first-seen slot order of the new
        /// snapshot. Totals are summed per BASE item key (<see cref="ItemKey.Base"/>), so a dyed or
        /// shaped variant counts toward its plain item and a pure slot move never reads as a pickup.
        /// </summary>
        public static List<(string Item, int Gained)> Gains(
            IReadOnlyList<NetItemStack?>? before, IReadOnlyList<NetItemStack?>? after)
        {
            var beforeTotals = Totals(before, order: null);
            var order = new List<string>();
            var afterTotals = Totals(after, order);

            var gains = new List<(string, int)>();
            foreach (string key in order)
            {
                beforeTotals.TryGetValue(key, out int had);
                int delta = afterTotals[key] - had;
                if (delta > 0)
                {
                    gains.Add((key, delta));
                }
            }

            return gains;
        }

        /// <summary>
        /// #2964: whether <paramref name="item"/> (a base key; dyed or shaped variants count) sits in the backpack and on
        /// none of the first <paramref name="quickSlots"/> slots, the quick-bar. A pickup of it tops up a backpack stack
        /// the hotbar cannot show, so the feed row says where it went — a child cut a birch and reported the logs lost.
        /// </summary>
        public static bool OnlyInBackpack(IReadOnlyList<NetItemStack?>? stacks, string item, int quickSlots)
        {
            if (stacks == null)
            {
                return false;
            }

            bool inBackpack = false;
            foreach (var s in stacks)
            {
                if (s == null || s.Count <= 0 || ItemKey.Base(s.Item) != item)
                {
                    continue;
                }

                if (s.Slot < quickSlots)
                {
                    return false;
                }

                inBackpack = true;
            }

            return inBackpack;
        }

        /// <summary>Per-base-item totals of one snapshot; records first-seen key order when asked.</summary>
        private static Dictionary<string, int> Totals(IReadOnlyList<NetItemStack?>? stacks, List<string>? order)
        {
            var totals = new Dictionary<string, int>();
            if (stacks == null)
            {
                return totals;
            }

            foreach (var s in stacks)
            {
                if (s == null || string.IsNullOrEmpty(s.Item) || s.Count <= 0)
                {
                    continue;
                }

                string key = ItemKey.Base(s.Item);
                if (!totals.TryGetValue(key, out int n))
                {
                    order?.Add(key);
                }

                totals[key] = n + s.Count;
            }

            return totals;
        }
    }
}
