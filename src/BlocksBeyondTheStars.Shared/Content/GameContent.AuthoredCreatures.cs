// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The fixed species of a home world in <c>data/creatures.json</c> (#3973): their names, homes, climate zones, voices
/// and favourite foods.</summary>
public sealed partial class GameContent
{
    /// <summary>Gives every record with a name key its English fallback name (<see cref="AuthoredCreature.EnglishName"/>) —
    /// the key itself when the English table does not have it (validation reports that).</summary>
    private void ResolveAuthoredCreatureNames()
    {
        Dictionary<string, string>? english = null;
        foreach (var record in _authoredCreatureOrder)
        {
            if (string.IsNullOrEmpty(record.NameKey))
            {
                continue;
            }

            english ??= LocaleTable(GameLocale.English) ?? new Dictionary<string, string>();
            record.SetEnglishName(english.TryGetValue(record.NameKey, out var text) && !string.IsNullOrWhiteSpace(text) ? text : record.NameKey);
        }
    }

    /// <summary>The #3973 fields of every record: a name key of the species form that English knows, known climate zones, known
    /// recorded calls, and a favourite food that is an item. A home naming an unknown type is only a warning
    /// (<see cref="WarnAuthoredCreatureHomes"/>): the type may come with another pack.</summary>
    private void ValidateAuthoredCreatureSpecies(List<string> problems)
    {
        var english = LocaleTable(GameLocale.English);
        foreach (var record in _authoredCreatureOrder)
        {
            string ctx = $"Authored creature '{record.Key}'";
            if (!string.IsNullOrEmpty(record.NameKey))
            {
                if (!CreatureNames.IsSpeciesKey(record.NameKey))
                {
                    problems.Add($"{ctx} nameKey '{record.NameKey}' is not a species key ({CreatureNames.KeyFor("<key>")}).");
                }
                else if (english is { Count: > 0 } && !english.ContainsKey(record.NameKey))
                {
                    problems.Add($"{ctx} nameKey '{record.NameKey}' is missing from the English locale.");
                }
            }

            foreach (var theme in record.FloraThemes)
            {
                if (!FloraThemes.IsKnown(theme))
                {
                    problems.Add($"{ctx} names unknown flora theme '{theme}'.");
                }
            }

            problems.AddRange(AnimalCalls.Problems(ctx, record.Voices));
            if (!string.IsNullOrEmpty(record.FavouriteFood) && !_items.ContainsKey(record.FavouriteFood))
            {
                problems.Add($"{ctx} loves unknown item '{record.FavouriteFood}'.");
            }
        }
    }

    /// <summary>#3973: one warning per home that names a planet type the content does not have — never a failed load (the
    /// type may come with another pack, or later). Returns how many there were.</summary>
    public int WarnAuthoredCreatureHomes(System.Action<string>? warn)
    {
        int count = 0;
        foreach (var record in _authoredCreatureOrder)
        {
            foreach (var home in record.Homes)
            {
                if (!_planets.ContainsKey(home))
                {
                    count++;
                    warn?.Invoke($"Authored creature '{record.Key}' lives on planet type '{home}', which the content does not have — "
                        + "it joins no roster there until the type exists.");
                }
            }
        }

        return count;
    }
}
