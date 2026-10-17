// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2729 (Lyxette: "20 stacks of food — take 5 along as supplies, the other 15 into the cargo hold"): the arithmetic
    /// behind moving a single stack or a chosen amount between the backpack and the ship's hold. The menu shows the stacks
    /// one by one; this decides which slot "this stack" names when the snapshot moved under the player, and which amounts
    /// the "How many?" row offers. Unity-free so the rules are covered by plain .NET tests; the server stays the judge of
    /// what actually fits.
    /// </summary>
    public static class CargoStackPicker
    {
        /// <summary>The slot a "this stack" move should name: <paramref name="preferredSlot"/> while it still holds
        /// <paramref name="item"/> (the stack the player clicked), else the first stack of <paramref name="item"/>, else -1
        /// (nothing of it left on this side).</summary>
        public static int ResolveSlot(IReadOnlyList<NetItemStack>? stacks, string item, int preferredSlot)
        {
            if (stacks == null || string.IsNullOrEmpty(item))
            {
                return -1;
            }

            int first = -1;
            foreach (var s in stacks)
            {
                if (s == null || s.Count <= 0 || s.Item != item)
                {
                    continue;
                }

                if (s.Slot == preferredSlot)
                {
                    return s.Slot;
                }

                if (first < 0)
                {
                    first = s.Slot;
                }
            }

            return first;
        }

        /// <summary>How many items the stack in <paramref name="slot"/> holds (0 for an empty or unknown slot).</summary>
        public static int CountInSlot(IReadOnlyList<NetItemStack>? stacks, int slot)
        {
            if (stacks != null)
            {
                foreach (var s in stacks)
                {
                    if (s != null && s.Slot == slot)
                    {
                        return s.Count > 0 ? s.Count : 0;
                    }
                }
            }

            return 0;
        }

        /// <summary>How many of <paramref name="item"/> lie on this side, over all its stacks.</summary>
        public static int Total(IReadOnlyList<NetItemStack>? stacks, string item)
        {
            int total = 0;
            if (stacks != null && !string.IsNullOrEmpty(item))
            {
                foreach (var s in stacks)
                {
                    if (s != null && s.Count > 0 && s.Item == item)
                    {
                        total += s.Count;
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// The amounts the "How many?" row offers for <paramref name="total"/> items of one kind: 1, 5 and half (rounded
        /// up) — each only when it is less than <paramref name="total"/> (moving everything is the "All of this kind"
        /// button's job) and not a repeat of a smaller one, ascending. <c>Half</c> marks the half amount, which the menu
        /// labels as such. Empty for one item or none.
        /// </summary>
        public static List<(int Amount, bool Half)> Amounts(int total)
        {
            var amounts = new List<(int Amount, bool Half)>();
            Offer(amounts, 1, false, total);
            Offer(amounts, 5, false, total);
            Offer(amounts, (total + 1) / 2, true, total);
            amounts.Sort((a, b) => a.Amount.CompareTo(b.Amount));
            return amounts;
        }

        private static void Offer(List<(int Amount, bool Half)> amounts, int amount, bool half, int total)
        {
            if (amount < 1 || amount >= total)
            {
                return;
            }

            foreach (var a in amounts)
            {
                if (a.Amount == amount)
                {
                    return;
                }
            }

            amounts.Add((amount, half));
        }
    }
}
