// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>Which festivals a player celebrates (#3021): a preset, or their own list.</summary>
public enum FestivalPreset
{
    /// <summary>The big world festivals plus the ones of the player's language — the default.</summary>
    BigAndLanguage = 0,

    /// <summary>Every festival of the calendar (what v2026.10.31 did for everybody).</summary>
    All = 1,

    /// <summary>Only the festivals of the player's language community.</summary>
    Language = 2,

    /// <summary>No festival at all (the old "Festivals (calendar): Off").</summary>
    None = 3,

    /// <summary>The player's own list of festival keys.</summary>
    Custom = 4,
}

/// <summary>
/// "My festivals" (#3021, epic #3019): resolves a player's preset, language and own list into the set of festivals they
/// celebrate. Server and client run the same code — the server filters what it greets, what the challenge packs see and
/// where pumpkins grow; the client filters its menu card. With 53 festivals the calendar alone would put a festival on
/// almost every second day; the default keeps the big world festivals and the player's own, about one day in nine.
/// A festival the world admin forces (<c>/festival &lt;key&gt;</c>) shows for everybody regardless — that is the no-FOMO
/// promise, not a calendar default.
/// </summary>
public static class FestivalSelection
{
    /// <summary>A preset from its wire/settings number; anything unknown is the default.</summary>
    public static FestivalPreset Parse(int value)
        => Enum.IsDefined(typeof(FestivalPreset), value) ? (FestivalPreset)value : FestivalPreset.BigAndLanguage;

    /// <summary>The two-letter language of a locale code (<c>pt-BR</c> → <c>pt</c>), lower case; empty for none.</summary>
    public static string Language(string? locale)
    {
        string code = (locale ?? string.Empty).Trim();
        int cut = code.IndexOfAny(new[] { '-', '_' });
        return (cut > 0 ? code.Substring(0, cut) : code).ToLowerInvariant();
    }

    /// <summary>Whether a preset (and, for <see cref="FestivalPreset.Custom"/>, the own list) includes this festival.</summary>
    public static bool Includes(FestivalDefinition festival, FestivalPreset preset, string? locale, ICollection<string>? custom)
    {
        switch (preset)
        {
            case FestivalPreset.All:
                return true;
            case FestivalPreset.None:
                return false;
            case FestivalPreset.Language:
                return festival.CelebratedIn(Language(locale));
            case FestivalPreset.Custom:
                return custom is not null && custom.Contains(festival.Key);
            default:
                return festival.Big || festival.CelebratedIn(Language(locale));
        }
    }

    /// <summary>The keys of every festival the selection includes, in the file's order.</summary>
    public static HashSet<string> Resolve(IEnumerable<FestivalDefinition> festivals, FestivalPreset preset, string? locale,
        IEnumerable<string>? custom)
    {
        var own = custom is null ? null : new HashSet<string>(custom, StringComparer.Ordinal);
        return new HashSet<string>(festivals.Where(f => Includes(f, preset, locale, own)).Select(f => f.Key), StringComparer.Ordinal);
    }

    /// <summary>Orders festival keys for the chip and the mood (#3022): higher <see cref="FestivalDefinition.Priority"/> first;
    /// equal priorities keep the given order (the calendar's: the one that started last first).</summary>
    public static IReadOnlyList<string> ByPriority(IEnumerable<string> keys, Func<string, FestivalDefinition?> find)
        => keys.Select((k, i) => (Key: k, Index: i, Priority: find(k)?.Priority ?? 0))
            .OrderByDescending(e => e.Priority).ThenBy(e => e.Index).Select(e => e.Key).ToList();

    /// <summary>A custom list from its settings text (<c>halloween,diwali</c>); unknown keys are kept — a newer file may know them.</summary>
    public static string[] ParseCustom(string? text)
        => (text ?? string.Empty).Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
}
