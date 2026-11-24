// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#4012: the dyeable seeds (<see cref="ItemDefinition.DyedPlaces"/>) — which items the Dye action takes, and which
/// seed a dyed flower gives back. One answer for the server's gate and every client menu that offers the action.</summary>
public sealed partial class GameContent
{
    /// <summary>Whether the Dye action takes this item: a building material whose block is <see cref="BlockDefinition.Tintable"/>,
    /// or a dyeable seed (<see cref="ItemDefinition.DyedPlaces"/>).</summary>
    public bool IsDyeable(ItemDefinition? item)
    {
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            return false;
        }

        return IsDyeOnly(item) || GetBlock(item.PlacesBlock!) is { Tintable: true };
    }

    /// <summary>Whether the item takes a colour and nothing else — no glow, no paint design, no form (the dyeable seeds).</summary>
    public static bool IsDyeOnly(ItemDefinition? item) => !string.IsNullOrEmpty(item?.DyedPlaces);

    /// <summary>The dyeable seed whose dyed copy plants <paramref name="blockKey"/> — the seed a dyed flower of that block gives
    /// back when picked — or null. Asked only when a coloured plant is picked, so a plain scan is enough.</summary>
    public ItemDefinition? DyedSeedFor(string blockKey)
    {
        foreach (var item in _items.Values)
        {
            if (item.DyedPlaces == blockKey)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>A dyeable seed plants a known flora block — a picture of flowers is planted on the ground like any flower.</summary>
    private void ValidateDyedSeed(ItemDefinition item, List<string> problems)
    {
        if (string.IsNullOrEmpty(item.DyedPlaces))
        {
            return;
        }

        if (!_blocks.TryGetValue(item.DyedPlaces!, out var block) || block.Category != "flora")
        {
            problems.Add($"Item '{item.Key}' plants '{item.DyedPlaces}' when dyed, which is no flora block.");
        }

        if (string.IsNullOrEmpty(item.PlacesBlock))
        {
            problems.Add($"Item '{item.Key}' has dyedPlaces but places no block.");
        }
    }
}
