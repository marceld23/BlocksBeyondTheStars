// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A species' name for one player (#3973, <see cref="CreatureNames"/>): a fixed species of a home world (the dog, the cat …)
/// is named in that player's language — on the scan, in the Codex (which keeps the scan's name), in the taming panel and in
/// the companion list; every other species keeps its coined, language-neutral name.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The name of <paramref name="sp"/> in the language <paramref name="locale"/> (a locale code).</summary>
    private string SpeciesDisplayName(CreatureSpecies sp, string? locale)
    {
        if (!CreatureNames.IsSpeciesKey(sp.NameKey))
        {
            return sp.Name;
        }

        var loc = LocalizerFor(locale ?? string.Empty);
        return CreatureNames.Display(sp.Name, sp.NameKey, loc.Get, loc.Has);
    }
}
