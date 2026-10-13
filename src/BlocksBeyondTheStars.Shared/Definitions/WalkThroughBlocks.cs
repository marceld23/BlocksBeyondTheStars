// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The blocks a body walks straight through although they are drawn (#2533): the mesher gives them no collider, so the
/// player strolls through a grass tuft, a torch, a ladder, a fire or an energy gate, and since #2184 through a tree crown.
/// Named once here for the client's collision (<c>PlayerController.IsCollidingKey</c>), the server's movement mirror
/// (<c>GameServer.IsWalkThroughProp</c>) and the player's weapon traces on both sides, which used to carry their own
/// (drifted) copies of the list. Several of these are <c>Solid</c> on paper (small flora and the crowns), which is why a
/// key list is needed at all: the <c>Solid</c> flag also decides sight and light.
/// </summary>
public static class WalkThroughBlocks
{
    /// <summary>The prefix every small plant (grass tufts, flowers, bushes, mushrooms) carries.</summary>
    public const string FloraPrefix = "flora_";

    /// <summary>A slim prop without a collider: small flora, the torch and lantern (lit or unlit, #809/#2520), the ladder
    /// a climber stands inside (#803), a fire and the energy gate's membrane. Tree crowns are NOT included — they are
    /// <see cref="TreeFoliage"/>, which ground creatures and NPCs still treat as a wall; see <see cref="IsWalkThrough"/>.</summary>
    public static bool IsProp(string? key)
        => key != null
           && (key.StartsWith(FloraPrefix, System.StringComparison.Ordinal)
               || key is "torch" or "lantern" or "torch_off" or "lantern_off" or "ladder" or "fire" or "energy_gate");

    /// <summary>Everything the PLAYER walks through: <see cref="IsProp"/> plus the tree crowns (#2184). The player's
    /// weapon and melee traces pass these cells too (#2533) — what you can walk through, you can shoot and hit through.</summary>
    public static bool IsWalkThrough(string? key) => IsProp(key) || TreeFoliage.IsKey(key);
}
