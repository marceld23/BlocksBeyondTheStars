// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    /// <summary>
    /// #3413: why space around the reporter's body may (or may not) hold ambient hostiles. A report "there are still
    /// guardians in space although I finished the game" showed three ambient drones in the reporter's HOME system after the
    /// win — which the Remnant Protocol (#1206, <see cref="RemnantSpaceHostile"/>) allows only in a pirate haven or the
    /// frontier-danger tier. The snapshot could not say which rule let them in; this block names the system's archetype,
    /// its frontier tier, the world rule and the verdict with its reason.
    /// </summary>
    private object BumpSpaceHostilitySnapshot(string? bodyId)
    {
        var body = string.IsNullOrEmpty(bodyId) ? null : _galaxy?.FindBody(bodyId!);
        var archetype = SystemArchetypeOf(body?.SystemId);
        int tier = FrontierTierForBody(bodyId);
        bool remnant = _storyState.GuardianDefeated;
        bool allowed = archetype != SystemArchetype.Desolate && RemnantSpaceHostile(archetype, bodyId ?? string.Empty)
                       && ActivityCount(Rules.SpaceNpcEnemies) > 0;
        return new
        {
            systemId = body?.SystemId ?? string.Empty,
            systemArchetype = archetype.ToString(),
            frontierTier = tier,
            frontierDanger = Rules.FrontierDanger,
            spaceNpcEnemies = Rules.SpaceNpcEnemies.ToString(),
            remnantEra = remnant,
            ambientHostilesAllowed = allowed,
            reason = HostilityReason(archetype, tier, remnant, allowed),
        };
    }

    /// <summary>The one rule that decided <see cref="BumpSpaceHostilitySnapshot"/>'s verdict, as a short tag.</summary>
    private string HostilityReason(SystemArchetype archetype, int tier, bool remnant, bool allowed)
    {
        if (ActivityCount(Rules.SpaceNpcEnemies) == 0)
        {
            return "rule_off";
        }

        if (archetype == SystemArchetype.Desolate)
        {
            return "desolate";
        }

        if (!remnant)
        {
            return "before_finale";
        }

        if (!allowed)
        {
            return "remnant_calm";
        }

        return archetype == SystemArchetype.PirateHaven ? "remnant_pirate_haven" : "remnant_frontier_tier_" + tier;
    }
}
