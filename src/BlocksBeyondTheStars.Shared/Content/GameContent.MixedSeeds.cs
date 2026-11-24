// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#4001: the load-time check of a mixed seed (<see cref="ItemDefinition.PlacesAnyOf"/>).</summary>
public sealed partial class GameContent
{
    /// <summary>A mixed seed names only known flora blocks, and its face (<see cref="ItemDefinition.PlacesBlock"/>) is one of
    /// them — the server plants one of the list, the icon and the placement preview show the face.</summary>
    private void ValidateMixedSeed(ItemDefinition item, List<string> problems)
    {
        if (item.PlacesAnyOf is not { } pool)
        {
            return;
        }

        if (pool.Count == 0 || string.IsNullOrEmpty(item.PlacesBlock) || !pool.Contains(item.PlacesBlock!))
        {
            problems.Add($"Item '{item.Key}' plants one of {pool.Count} blocks, but its placesBlock is not one of them.");
        }

        foreach (string key in pool)
        {
            if (!_blocks.TryGetValue(key, out var block) || block.Category != "flora")
            {
                problems.Add($"Item '{item.Key}' plants '{key}', which is no flora block.");
            }
        }
    }
}
