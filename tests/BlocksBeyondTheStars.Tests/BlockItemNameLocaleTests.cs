// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3114: a block and the item that places it carry one name. The community languages had about 250 pairs where they
/// did not — the placed block read "Varco energetico" and the same thing in the inventory "Portale Energetico", so a
/// child looked for two different things. A pair is the block and the item of the same key, plus every item whose
/// <c>placesBlock</c> names another block (a seed and its plant). Where English names the two differently on purpose
/// (trunk and log, crust and salt, a seed and the plant it grows) the pair is left out. Checked in every language that
/// translates both names; a table that still lacks one of them shows the English fallback there, which is allowed.
/// </summary>
public sealed class BlockItemNameLocaleTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>Every language besides English whose base table exists on disk.</summary>
    public static TheoryData<string> TranslatedLocales()
    {
        var data = new TheoryData<string>();
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            string code = locale.Code();
            if (locale != GameLocale.English && File.Exists(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")))
            {
                data.Add(code);
            }
        }

        return data;
    }

    /// <summary>(item name key, block name key) for each item and the block it shares a key with or places.</summary>
    private static List<(string Item, string Block)> NamePairs()
    {
        var pairs = new List<(string Item, string Block)>();
        foreach (var item in Content.Value.Items.Values)
        {
            if (Content.Value.Blocks.TryGetValue(item.Key, out var sameKey))
            {
                pairs.Add((item.NameKey, sameKey.NameKey));
            }

            if (!string.IsNullOrEmpty(item.PlacesBlock) && item.PlacesBlock != item.Key
                && Content.Value.Blocks.TryGetValue(item.PlacesBlock!, out var placed))
            {
                pairs.Add((item.NameKey, placed.NameKey));
            }
        }

        return pairs;
    }

    // Read directly rather than through TestLocales: its cache is a plain Dictionary shared by test classes that run in parallel.
    private static Dictionary<string, string> Table(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")))
           ?? [];

    [Theory]
    [MemberData(nameof(TranslatedLocales))]
    public void BlockAndItem_ShareTheirName_WhereEnglishDoes(string code)
    {
        var en = Table("en");
        var table = Table(code);
        var compared = NamePairs()
            .Where(p => en.TryGetValue(p.Item, out var item) && en.TryGetValue(p.Block, out var block) && item == block)
            .ToList();
        var different = compared
            .Where(p => table.TryGetValue(p.Item, out var item) && table.TryGetValue(p.Block, out var block) && item != block)
            .Select(p => $"{p.Block} \"{table[p.Block]}\" / {p.Item} \"{table[p.Item]}\"")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        // A vacuous pass (no pairs found at all) would hide a broken pair lookup; the game has about 220 such pairs.
        Assert.True(compared.Count > 100, $"only {compared.Count} block/item name pairs found — the pair lookup is broken");
        Assert.True(
            different.Count == 0,
            $"{code}.json names {different.Count} block(s) differently from the item that places them, though English gives "
            + $"both one name — pick the right word for the language and use it on both keys: {string.Join("; ", different.Take(20))}");
    }
}
