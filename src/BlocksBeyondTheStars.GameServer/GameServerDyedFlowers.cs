// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #4012: flower pictures. A tester wanted to choose the colours of the flowers to plant whole pictures. A dyeable seed
/// (<see cref="ItemDefinition.DyedPlaces"/>: the flower seed) takes the Dye action like a building material — the colour
/// only. Dyed, it plants one fixed flower (the sun blossom) with the colour in the cell's tint, the client draws the colour
/// on the blossom alone, and picking the flower gives the dyed seed back instead of its fibre and berry — and nothing
/// regrows, so a picture can be rearranged. Partial of <see cref="GameServer"/>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The block a DYED copy of a seed plants (<see cref="ItemDefinition.DyedPlaces"/>), or null when the item has none
    /// or the intent's key carries no colour (a plain flower seed stays the mix).</summary>
    private BlockDefinition? DyedSeedBlock(ItemDefinition item, string itemKey)
        => !string.IsNullOrEmpty(item.DyedPlaces) && ItemKey.Tint(itemKey) != 0 ? _content.GetBlock(item.DyedPlaces!) : null;

    /// <summary>Whether a placement plants a dyed seed: its item's dyed flower is the block being placed.</summary>
    private bool PlantsDyedSeed(PlacementRequest r)
        => _content.GetItem(r.Intent.ItemKey)?.DyedPlaces is { } dyed && dyed == r.BlockDef.Key && ItemKey.Tint(r.Intent.ItemKey) != 0;

    /// <summary>The drop of a picked dyed flower — its dyed seed, once — or null for every other cell (a wild flower, an
    /// undyed one, a block that is no dyed seed's flower).</summary>
    private List<ItemAmount>? DyedFlowerYield(BlockDefinition def, ushort current, int tint)
    {
        if (tint == 0 || !IsFlora(current) || _content.DyedSeedFor(def.Key) is not { } seed)
        {
            return null;
        }

        return new List<ItemAmount> { new(ItemKey.Compose(seed.Key, tint, 0), 1) };
    }
}
