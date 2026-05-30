using Spacecraft.Shared.Definitions;

namespace Spacecraft.Shared.Missions;

/// <summary>
/// A mission proposal produced by the optional AI backend (technical requirements /
/// `anf_mission_editor.md` §6–7). It has a creative layer (narrative) and a rule layer
/// (server-validatable objectives/rewards). The authoritative server converts a valid
/// plan into a <see cref="MissionDefinition"/>; the AI never finalizes rewards or bypasses
/// rules.
/// </summary>
public sealed class MissionPlan
{
    // Creative layer.
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? GiverName { get; set; }
    public string? StartDialog { get; set; }
    public string? CompleteDialog { get; set; }
    public string Difficulty { get; set; } = "normal";

    // Rule layer (validated by the server).
    public List<MissionObjective> Objectives { get; set; } = new();
    public List<ItemAmount> SuggestedRewards { get; set; } = new();
}

/// <summary>Converts an AI <see cref="MissionPlan"/> into a server mission, clamping rewards.</summary>
public static class MissionPlanConverter
{
    /// <summary>Hard cap so the AI cannot propose runaway reward amounts (server stays in control).</summary>
    public const int MaxRewardCount = 25;

    public static MissionDefinition ToDefinition(MissionPlan plan, string id, MissionSource source = MissionSource.Admin)
    {
        return new MissionDefinition
        {
            Id = id,
            Source = source,
            Title = plan.Title,
            Description = plan.Description,
            Objectives = plan.Objectives.Select(o => new MissionObjective
            {
                Type = o.Type,
                Target = o.Target,
                Required = o.Required,
            }).ToList(),
            Rewards = plan.SuggestedRewards
                .Select(r => new ItemAmount(r.Item, System.Math.Min(MaxRewardCount, System.Math.Max(1, r.Count))))
                .ToList(),
            Active = true,
            Repeatable = false,
        };
    }
}
