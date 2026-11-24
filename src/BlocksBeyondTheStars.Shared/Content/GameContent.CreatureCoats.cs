// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The coat variants of <c>data/creatures.json</c> (#3972).</summary>
public sealed partial class GameContent
{
    /// <summary>Every authored record's <c>coats</c>: a known pattern and hide name, a weight of at least 1.</summary>
    private void ValidateCreatureCoats(List<string> problems)
    {
        foreach (var record in _authoredCreatures.Values)
        {
            problems.AddRange(CoatVariants.Problems($"Authored creature '{record.Key}'", record.Coats));
        }
    }
}
