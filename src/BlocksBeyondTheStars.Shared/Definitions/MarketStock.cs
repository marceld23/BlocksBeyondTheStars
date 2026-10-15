// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// What a vendor has on offer, and from how far it trades (#2626, #2629, #2630) — THE one rule the server's barter check
/// and the client's vendor screen (and the crafting menu's market page) both call, so the screen never lists a deal the
/// server then refuses, and never hides one it would accept.
/// <list type="bullet">
/// <item><b>Stock.</b> A vendor's trade is its <c>theme</c> (<see cref="NpcProfession.Theme"/>, the <c>marketTheme</c> of
/// <c>data/recipes.json</c>). A themeless market recipe trades at every vendor and at the ship's own console; a themed one
/// only at a vendor of that theme, and a rotating one (<see cref="RecipeDefinition.MarketRotation"/>) only on its days.
/// The travelling merchant's theme is <see cref="MixedTheme"/>: one offer from each of <see cref="MixedThemeCount"/>
/// themes, picked afresh every in-game day (<see cref="MixedOffers"/>) — a pure function of the recipe table and the
/// day, so the client works out the very same list the server accepts without any protocol change.</item>
/// <item><b>Order.</b> The screen lists the vendor's own goods first, then the themeless deals every vendor has
/// (<see cref="ListFor"/>) — the iron ↔ titanium swaps used to come first and made the doctor's list read like the shop's.</item>
/// <item><b>Reach.</b> The server accepts a barter while the player stands within <see cref="TradeReach"/> of a trading
/// post, or within <see cref="VendorReach"/> of a vendor; a themed deal needs a vendor selling it within
/// <see cref="VendorReach"/>. Both measured the short way round the world's seams.</item>
/// </list>
/// </summary>
public static class MarketStock
{
    /// <summary>The theme of a vendor with a bit of everything (the travelling merchant, #2630).</summary>
    public const string MixedTheme = "mixed";

    /// <summary>How many themes the mixed stock draws one offer from each day.</summary>
    public const int MixedThemeCount = 4;

    /// <summary>The trading reach around a post (a settlement stall, a base or station post) — and the client's "Trade"
    /// prompt reach around a vendor: the server always accepts a barter from there.</summary>
    public const float TradeReach = 4f;

    /// <summary>How far the vendor itself may stand from the player: the 4-block stall reach plus room for the keeper's
    /// post leash. The server accepts a barter and a themed deal of a vendor within this reach; the client keeps an open
    /// trade screen bound to its vendor while it stays this close.</summary>
    public const float VendorReach = 6f;

    /// <summary>Whether <paramref name="theme"/> is the mixed stock of <see cref="MixedTheme"/>.</summary>
    public static bool IsMixed(string? theme) => string.Equals(theme, MixedTheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>A market recipe that is a real trade (something given, something got).</summary>
    public static bool IsTrade(RecipeDefinition recipe)
        => recipe.Station == CraftingStation.Market && recipe.Inputs.Count > 0 && recipe.Outputs.Count > 0;

    /// <summary>Whether a vendor of <paramref name="vendorTheme"/> deals in this kind of goods at all on day
    /// <paramref name="day"/> — the server's "wrong vendor" test. A themeless deal: always; the mixed stock: when today's
    /// pick holds it; else the themes must match. "" (no vendor: the ship's console) deals only in themeless goods.</summary>
    public static bool SellsKind(IEnumerable<RecipeDefinition> recipes, string? vendorTheme, RecipeDefinition recipe, long day)
    {
        if (string.IsNullOrEmpty(recipe.MarketTheme))
        {
            return true;
        }

        if (IsMixed(vendorTheme))
        {
            foreach (var r in MixedOffers(recipes, day))
            {
                if (ReferenceEquals(r, recipe) || r.Key == recipe.Key)
                {
                    return true;
                }
            }

            return false;
        }

        return string.Equals(recipe.MarketTheme, vendorTheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a vendor of <paramref name="vendorTheme"/> has <paramref name="recipe"/> in stock on day
    /// <paramref name="day"/>: <see cref="SellsKind"/> and the recipe's own rotation.</summary>
    public static bool Offers(IEnumerable<RecipeDefinition> recipes, string? vendorTheme, RecipeDefinition recipe, long day)
        => recipe.Station == CraftingStation.Market && SellsKind(recipes, vendorTheme, recipe, day) && recipe.OfferedOnDay(day);

    /// <summary>The vendor screen's rows for a vendor of <paramref name="vendorTheme"/> on day <paramref name="day"/>: its
    /// own (themed) goods first, then the themeless deals, each group in the data's order.</summary>
    public static List<RecipeDefinition> ListFor(IEnumerable<RecipeDefinition> recipes, string? vendorTheme, long day)
    {
        var own = new List<RecipeDefinition>();
        var common = new List<RecipeDefinition>();
        var mixed = IsMixed(vendorTheme) ? MixedOffers(recipes, day) : null;
        foreach (var r in recipes)
        {
            if (!IsTrade(r) || !r.OfferedOnDay(day))
            {
                continue;
            }

            if (string.IsNullOrEmpty(r.MarketTheme))
            {
                common.Add(r);
            }
            else if (mixed != null ? mixed.Contains(r) : string.Equals(r.MarketTheme, vendorTheme, StringComparison.OrdinalIgnoreCase))
            {
                own.Add(r);
            }
        }

        own.AddRange(common);
        return own;
    }

    /// <summary>Every market theme the recipe table names, sorted (ordinal) — the header line keys
    /// (<c>ui.vendor.theme.&lt;theme&gt;</c>) and the mixed stock's pool.</summary>
    public static List<string> Themes(IEnumerable<RecipeDefinition> recipes)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in recipes)
        {
            if (r.Station == CraftingStation.Market && !string.IsNullOrEmpty(r.MarketTheme))
            {
                set.Add(r.MarketTheme);
            }
        }

        return new List<string>(set);
    }

    /// <summary>The mixed stock of day <paramref name="day"/>: <see cref="MixedThemeCount"/> themes drawn from the table's
    /// themes, and from each one of its trades in stock that day — both draws a stable hash of the day (FNV-1a, never
    /// <c>string.GetHashCode</c>, which differs per process), so the client and the server pick the same.</summary>
    public static List<RecipeDefinition> MixedOffers(IEnumerable<RecipeDefinition> recipes, long day)
    {
        var byTheme = new SortedDictionary<string, List<RecipeDefinition>>(StringComparer.Ordinal);
        foreach (var r in recipes)
        {
            if (!IsTrade(r) || string.IsNullOrEmpty(r.MarketTheme) || !r.OfferedOnDay(day))
            {
                continue;
            }

            if (!byTheme.TryGetValue(r.MarketTheme, out var list))
            {
                byTheme[r.MarketTheme] = list = new List<RecipeDefinition>();
            }

            list.Add(r);
        }

        // Order the themes by a per-day hash and take the first few — a fresh mix every day.
        var themes = new List<string>(byTheme.Keys);
        themes.Sort((a, b) =>
        {
            int c = Hash(day, a, "theme").CompareTo(Hash(day, b, "theme"));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });

        var picks = new List<RecipeDefinition>();
        for (int i = 0; i < themes.Count && i < MixedThemeCount; i++)
        {
            var offers = byTheme[themes[i]];
            offers.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            picks.Add(offers[(int)(Hash(day, themes[i], "offer") % (uint)offers.Count)]);
        }

        return picks;
    }

    /// <summary>A stable per-day hash of a theme and a purpose (FNV-1a over the text).</summary>
    private static uint Hash(long day, string theme, string purpose)
    {
        uint hash = 2166136261;
        foreach (char c in day.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + theme + ":" + purpose)
        {
            hash = unchecked((hash ^ c) * 16777619);
        }

        // A final avalanche so neighbouring days spread across the whole range instead of differing in the low bits only.
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7feb352d);
        hash ^= hash >> 15;
        return hash;
    }
}
