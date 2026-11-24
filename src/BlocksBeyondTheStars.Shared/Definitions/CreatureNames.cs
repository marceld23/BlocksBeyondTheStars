// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// How a species is named (#3973). A rolled species has a coined, language-neutral <see cref="CreatureSpecies.Name"/> ("Vexilth
/// Krool") and the generic <see cref="CreatureSpecies.NameKey"/>; a fixed species of a home world (the dog, the cat …) has a
/// <b>species key</b> (<c>creature.&lt;key&gt;.name</c>, from its record's <c>nameKey</c>) that every display localizes, and its
/// <see cref="CreatureSpecies.Name"/> is the English text of that key — the fallback for a log, a save and anything that cannot
/// localize. The server localizes for one player (the scan, the Codex, the taming panel, the companion list); the client for
/// what many players see (the nameplate, the thermal view).
/// </summary>
public static class CreatureNames
{
    /// <summary>The name key every rolled species carries — "a creature"; never a species key.</summary>
    public const string GenericKey = "creature.generic.name";

    /// <summary>The prefix and suffix of a species key.</summary>
    public const string KeyPrefix = "creature.";
    public const string KeySuffix = ".name";

    /// <summary>The species key of a record key: <c>creature.dog.name</c>.</summary>
    public static string KeyFor(string recordKey) => KeyPrefix + recordKey + KeySuffix;

    /// <summary>Whether <paramref name="nameKey"/> names a species of its own (a <c>creature.&lt;x&gt;.name</c> key other than the
    /// generic one).</summary>
    public static bool IsSpeciesKey(string? nameKey)
        => !string.IsNullOrEmpty(nameKey) && nameKey != GenericKey
            && nameKey!.StartsWith(KeyPrefix, StringComparison.Ordinal) && nameKey.EndsWith(KeySuffix, StringComparison.Ordinal);

    /// <summary>The name to show: the localized species key when there is one and <paramref name="has"/> knows it, else the
    /// species' own <paramref name="name"/>, else the localized <paramref name="nameKey"/> (the generic "creature").</summary>
    public static string Display(string? name, string? nameKey, Func<string, string> localize, Func<string, bool> has)
    {
        if (IsSpeciesKey(nameKey) && has(nameKey!))
        {
            return localize(nameKey!);
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name!;
        }

        return string.IsNullOrEmpty(nameKey) ? string.Empty : localize(nameKey!);
    }
}
