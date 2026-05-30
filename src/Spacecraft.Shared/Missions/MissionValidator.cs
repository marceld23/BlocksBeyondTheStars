using Spacecraft.Shared.Content;

namespace Spacecraft.Shared.Missions;

/// <summary>
/// Validates a mission definition against the loaded content (technical requirements /
/// `anf_admin_blueprinf.md` §10). Shared by the in-game player editor and the admin
/// extension editor so the same rules apply everywhere.
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
