// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The trader discount (#3292, the flower set) — THE one price rule the server's barter (<c>HandleCraft</c>) and the
/// client's vendor screen, crafting page and craft gate all call, so a shown price never disagrees with the one the
/// server takes. The market is pure barter: a discount makes a trade's INPUTS smaller (what the player gives), never its
/// outputs, and only on market trades. Each trade is rounded on its own (half up) and never drops below one piece, so a
/// one-piece trade stays a one-piece trade; a batch of N trades pays N times the discounted trade.
/// </summary>
public static class MarketPricing
{
    /// <summary>What <paramref name="count"/> pieces cost at a discount of <paramref name="discount"/> (0..1):
    /// <c>max(1, n − round(n·d))</c> with half rounding up; <paramref name="count"/> itself at no discount (and for a
    /// count of 0 or less, which no trade has).</summary>
    public static int Discounted(int count, float discount)
    {
        if (discount <= 0f || count <= 0)
        {
            return count;
        }

        int off = (int)Math.Floor((count * (double)discount) + 0.5);
        return Math.Max(1, count - off);
    }

    /// <summary>A discount as the whole percent the server sends (<c>TraderDiscountInfo.Percent</c>), 0..100.</summary>
    public static int ToPercent(float discount) => (int)Math.Round(Math.Clamp(discount, 0f, 1f) * 100.0, MidpointRounding.AwayFromZero);

    /// <summary>The discount of a whole percent — what the server prices with and the client shows, so both read the same
    /// number.</summary>
    public static float FromPercent(int percent) => Math.Clamp(percent, 0, 100) / 100f;

    /// <summary>The inputs of ONE <paramref name="recipe"/> at <paramref name="discount"/>: the discounted copies for a market
    /// trade (groups kept), the recipe's own list for anything else or at no discount.</summary>
    public static IReadOnlyList<ItemAmount> Inputs(RecipeDefinition recipe, float discount)
    {
        if (discount <= 0f || recipe.Station != CraftingStation.Market)
        {
            return recipe.Inputs;
        }

        var list = new List<ItemAmount>(recipe.Inputs.Count);
        foreach (var input in recipe.Inputs)
        {
            list.Add(new ItemAmount(input.Item, Discounted(input.Count, discount)) { Group = input.Group });
        }

        return list;
    }
}
