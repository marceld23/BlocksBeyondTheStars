// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Where a blueprint stands for the player, in the order the Blueprints tab names the gates.</summary>
    public enum BlueprintResearchState
    {
        /// <summary>Already researched.</summary>
        Researched = 0,

        /// <summary>A prerequisite blueprint is still missing.</summary>
        Locked = 1,

        /// <summary>Prerequisites done, but the knowledge balance is below the cost.</summary>
        KnowledgeMissing = 2,

        /// <summary>Prerequisites and knowledge done, but research materials are missing.</summary>
        MaterialsMissing = 3,

        /// <summary>Everything is there — the player can research it right now.</summary>
        Researchable = 4,
    }

    /// <summary>
    /// The client's one research test (#2563): the card status on the Blueprints tab, its "Researchable now" filter, the
    /// header's "next" line and the Tech badge all ask this, so the filter can never show a card that reads "Materials
    /// missing" or "Locked". It mirrors the server's unlock gates (prerequisites, materials, knowledge — knowledge is a
    /// threshold, never spent); the server still decides. Pure: the caller hands in the researched set, the knowledge
    /// balance and how many of an item the player owns (inventory + cargo on the client).
    /// </summary>
    public static class BlueprintResearch
    {
        /// <summary>The state of <paramref name="bp"/>. Knowledge is named before materials: it is the slower faucet,
        /// so it is the one worth naming when both are short (#1191). With <paramref name="content"/> a cost that names an
        /// item group (#2958: any log) counts every member of it, as the server does.</summary>
        public static BlueprintResearchState StateOf(BlueprintDefinition bp, ICollection<string> researched, int knowledge,
            Func<string, int> owned, GameContent? content = null)
        {
            if (researched.Contains(bp.Key))
            {
                return BlueprintResearchState.Researched;
            }

            foreach (var pre in bp.Prerequisites)
            {
                if (!researched.Contains(pre))
                {
                    return BlueprintResearchState.Locked;
                }
            }

            if (knowledge < bp.KnowledgeCost)
            {
                return BlueprintResearchState.KnowledgeMissing;
            }

            foreach (var cost in bp.UnlockCost)
            {
                int have = owned(cost.Item);
                if (content != null)
                {
                    foreach (var member in content.GroupMembersOf(cost))
                    {
                        have += member == cost.Item ? 0 : owned(member);
                    }
                }

                if (have < cost.Count)
                {
                    return BlueprintResearchState.MaterialsMissing;
                }
            }

            return BlueprintResearchState.Researchable;
        }

        /// <summary>True when <paramref name="bp"/> can be researched right now: not yet researched, every prerequisite
        /// done, the knowledge balance covers the cost and every research material is owned.</summary>
        public static bool IsResearchableNow(BlueprintDefinition bp, ICollection<string> researched, int knowledge,
            Func<string, int> owned, GameContent? content = null)
            => StateOf(bp, researched, knowledge, owned, content) == BlueprintResearchState.Researchable;
    }
}
