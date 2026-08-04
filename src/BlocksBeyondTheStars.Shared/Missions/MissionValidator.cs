// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>
/// Validates a mission definition against the loaded content. Shared by the in-game player
/// editor and the admin extension editor so the same rules apply everywhere. The rules: a
/// mission needs a non-empty id and at least one objective; every objective needs a supported
/// type (Collect/Mine/Deliver), a positive required count and a target that exists in the
/// loaded content (a block for Mine, an item otherwise); every reward must reference a known
/// item with a positive count. (Originally the internal design spec `anf_admin_blueprinf.md`
/// §10 — this summary is the public authority.)
/// </summary>
public static class MissionValidator
{
    private static readonly HashSet<MissionObjectiveType> SupportedTypes = new()
    {
        MissionObjectiveType.Collect,
        MissionObjectiveType.Mine,
        MissionObjectiveType.Deliver,
    };

    /// <summary>Returns a list of problems; empty means the mission is valid.</summary>
    public static List<string> Validate(MissionDefinition mission, GameContent content)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(mission.Id))
        {
            problems.Add("Mission id is empty.");
        }

        if (mission.Objectives.Count == 0)
        {
            problems.Add("Mission has no objectives.");
        }

        foreach (var obj in mission.Objectives)
        {
            if (!SupportedTypes.Contains(obj.Type))
            {
                problems.Add($"Objective type '{obj.Type}' is not supported yet.");
                continue;
            }

            if (obj.Required < 1)
            {
                problems.Add($"Objective '{obj.Target}' has a non-positive required count.");
            }

            bool targetExists = obj.Type == MissionObjectiveType.Mine
                ? content.GetBlock(obj.Target) is not null
                : content.GetItem(obj.Target) is not null;
            if (!targetExists)
            {
                problems.Add($"Objective references unknown target '{obj.Target}'.");
            }
        }

        foreach (var reward in mission.Rewards)
        {
            if (content.GetItem(reward.Item) is null)
            {
                problems.Add($"Reward references unknown item '{reward.Item}'.");
            }

            if (reward.Count < 1)
            {
                problems.Add($"Reward '{reward.Item}' has a non-positive count.");
            }
        }

        return problems;
    }

    public static bool IsValid(MissionDefinition mission, GameContent content) => Validate(mission, content).Count == 0;
}
