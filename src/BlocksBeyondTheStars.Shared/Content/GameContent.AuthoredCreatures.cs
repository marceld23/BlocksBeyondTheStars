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
    /// recorded calls, a favourite food that is an item, and (#3975) a water band that is not empty. A home naming an unknown type is only a warning
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
            ValidateWaterLife(record, ctx, problems);
            if (!string.IsNullOrEmpty(record.FavouriteFood) && !_items.ContainsKey(record.FavouriteFood))
            {
                problems.Add($"{ctx} loves unknown item '{record.FavouriteFood}'.");
            }

            ValidatePeopleAnimal(record, ctx, problems);
        }
    }

    /// <summary>#3975: a water band is not empty; #4017: only a water or amphibian species keeps to the sea.</summary>
    private static void ValidateWaterLife(AuthoredCreature record, string ctx, List<string> problems)
    {
        if (record.MaxWaterDepth > 0 && record.MaxWaterDepth < record.MinWaterDepth)
        {
            problems.Add($"{ctx} water band is empty (maxWaterDepth {record.MaxWaterDepth} < minWaterDepth {record.MinWaterDepth}).");
        }

        if (record.SeaOnly && record.Habitat is not (CreatureHabitat.Water or CreatureHabitat.Amphibian))
        {
            problems.Add($"{ctx} keeps to the sea but lives no water life (habitat {record.Habitat}).");
        }
    }

    /// <summary>#3976: a pet or pen species is a peaceful land animal (the pet rule would never pick another), a pen lays a known
    /// item, and the chance to live wild elsewhere is a share.</summary>
    private void ValidatePeopleAnimal(AuthoredCreature record, string ctx, List<string> problems)
    {
        if (!string.IsNullOrEmpty(record.Pen) && !_items.ContainsKey(record.Pen))
        {
            problems.Add($"{ctx} pen lays unknown item '{record.Pen}'.");
        }

        if ((record.Pet || !string.IsNullOrEmpty(record.Pen))
            && (record.Habitat != CreatureHabitat.Land
                || record.Temperament is not (CreatureTemperament.Passive or CreatureTemperament.Skittish)))
        {
            problems.Add($"{ctx} is a pet or pen animal but no peaceful land animal (habitat {record.Habitat}, {record.Temperament}).");
        }

        if (record.WildElsewhere is < 0.0 or > 1.0 || double.IsNaN(record.WildElsewhere))
        {
            problems.Add($"{ctx} wildElsewhere {record.WildElsewhere.ToString(System.Globalization.CultureInfo.InvariantCulture)} is no chance between 0 and 1.");
        }
    }

    /// <summary>#3976: the records people keep — a pet (<see cref="AuthoredCreature.Pet"/>) or a pen animal
    /// (<see cref="AuthoredCreature.Pen"/>) — in the order of <c>data/creatures.json</c>.</summary>
    public IReadOnlyList<AuthoredCreature> KeptAnimalRecords
        => _authoredCreatureOrder.FindAll(r => r.Pet || !string.IsNullOrEmpty(r.Pen));

    /// <summary>#3976: the records that may live wild away from their homes (<see cref="AuthoredCreature.WildElsewhere"/> above
    /// 0), in the order of <c>data/creatures.json</c> — the strays every roster caller passes to the generator.</summary>
    public IReadOnlyList<AuthoredCreature> StrayAnimalRecords
        => _authoredCreatureOrder.FindAll(r => r.WildElsewhere > 0.0);

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
