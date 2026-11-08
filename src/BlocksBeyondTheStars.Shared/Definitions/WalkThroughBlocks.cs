// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The blocks a body walks straight through although they are drawn (#2533): the mesher gives them no collider, so the
/// player strolls through a grass tuft (or one of Soaniri's singing plants, #3422), a torch, a ladder, a fire or an energy
/// gate, and since #2184 through a tree crown.
/// Named once here for the client's collision (<c>PlayerController.IsCollidingKey</c>), the server's movement mirror
/// (<c>GameServer.IsWalkThroughProp</c>) and the player's weapon traces on both sides, which used to carry their own
/// (drifted) copies of the list. Several of these are <c>Solid</c> on paper (small flora and the crowns), which is why a
/// key list is needed at all: the <c>Solid</c> flag also decides sight and light.
/// </summary>
public static class WalkThroughBlocks
{
    /// <summary>The prefix the classic small plants (grass tufts, flowers, bushes, mushrooms) carry. Not every plant has it
    /// (#3422: Soaniri's singing plants), so <see cref="IsProp"/> asks <see cref="FloraCatalog.IsPlantKey"/> instead.</summary>
    public const string FloraPrefix = "flora_";

    /// <summary>A slim prop without a collider: small flora, the torch and lantern (lit or unlit, #809/#2520), the ladder
    /// a climber stands inside (#803), a fire, the energy gate's membrane and every <see cref="IsOpenTwin">open twin</see>
    /// (#3174: an open lift gate's empty frame is walked through to board the platform; #3422: an open phase block, a
    /// switched-off force field, an open trapdoor). Tree crowns are NOT included — they are <see cref="TreeFoliage"/>,
    /// which ground creatures and NPCs still treat as a wall; see <see cref="IsWalkThrough"/>.
    /// "Small flora" is every plant the mesher draws as one (<see cref="FloraCatalog.IsPlantKey"/>): the <c>flora_*</c> keys
    /// and Soaniri's singing plants, whose keys carry no prefix (#3422). With the bare prefix test the client's out-of-world
    /// guard (<c>PlayerController.GuardAgainstFallingOut</c>) read a chime reed or bell bloom in the body's cell as "stuck
    /// in a block" — the mesher gives them no collider, so the body walks in — and after a few frames snapped the player
    /// back to the last spot before the plant: on Soaniri's meadows nobody could walk forward.</summary>
    public static bool IsProp(string? key)
        => key != null
           && (FloraCatalog.IsPlantKey(key)
               || key is "torch" or "lantern" or "torch_off" or "lantern_off" or "ladder" or "fire" or "energy_gate"
               || IsOpenTwin(key));

    /// <summary>Everything the PLAYER walks through: <see cref="IsProp"/> plus the tree crowns (#2184). The player's
    /// weapon and melee traces pass these cells too (#2533) — what you can walk through, you can shoot and hit through.</summary>
    public static bool IsWalkThrough(string? key) => IsProp(key) || TreeFoliage.IsKey(key);

    /// <summary>The open twin of a door-like block, drawn (a shimmer, an empty frame) but walked or fallen through: the
    /// Crystal Net's open phase block, switched-off force field and open trapdoor (#2264), and the open lift gate (#3174).
    /// Part of <see cref="IsProp"/> and of <see cref="CubeHasCollider"/>, so the mesher and the player's out-of-world guard
    /// read one list: the guard used to know only the lift gate and snapped a player standing in an open phase-block door
    /// back to the spot before it (#3422).</summary>
    public static bool IsOpenTwin(string? key)
        => key is "phase_block_open" or "force_field_off" or "trapdoor_open" or "lift_gate_open";

    /// <summary>Whether the client's chunk mesher gives a full-cube cell of this block a collider (<c>TraitCollidable</c>):
    /// everything but water, fire, the energy gate's membrane, a <paramref name="liquid"/> (oil, the gas sea; #2106), a tree
    /// crown (#2184) and an <see cref="IsOpenTwin">open twin</see>. Lava DOES collide — you stand on its surface. Plants,
    /// torches and the ladder draw their own collider-less geometry before the cube path (<see cref="IsProp"/>). Every
    /// block without a collider here must be one the out-of-world guard never reads as "stuck in a block"
    /// (<c>WalkThroughColliderTests</c>, #3422).</summary>
    public static bool CubeHasCollider(string? key, bool liquid)
        => key is not ("water" or "fire" or "energy_gate") && !liquid && !TreeFoliage.IsKey(key) && !IsOpenTwin(key);
}
