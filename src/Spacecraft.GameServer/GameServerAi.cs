using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Missions;

namespace Spacecraft.GameServer;

/// <summary>
/// Optional AI mission generation (technical requirements / `anf_mission_editor.md`). The
/// AI proposes a <see cref="MissionPlan"/>; the server validates and clamps it, then
/// publishes (Auto) or drafts (Suggest) it. Everything keeps working with AI Off or the
/// backend unreachable — failures fall back to "no mission".
/// </summary>
public sealed partial class GameServer
{
    /// <summary>
    /// Requests an AI mission for the given context. Returns whether a mission was created
    /// and a human-readable status message. Never throws.
    /// </summary>
    public (bool Ok, string Message) TryGenerateAiMission(string context)
    {
        switch (_config.AiLevel)
        {
            case AiLevel.Off:
                return (false, "AI is disabled on this server.");
            case AiLevel.TextOnly:
                return (false, "AI level is text-only; full mission generation is disabled.");
        }

        MissionPlan? plan;
        try
        {
            plan = _ai.Generate(context);
        }
        catch
        {
            plan = null; // defensive: providers should not throw, but never crash the tick
        }

        if (plan is null)
        {
            _log.Warn("AI backend returned no mission (unavailable or disabled) — falling back to none.");
            return (false, "AI backend unavailable — no mission generated (fallback).");
        }

        var id = "ai_" + Guid.NewGuid().ToString("N");
        var def = MissionPlanConverter.ToDefinition(plan, id, MissionSource.Admin);

        var problems = MissionValidator.Validate(def, _content);
        if (problems.Count > 0)
        {
            _log.Warn($"AI mission rejected by validation: {string.Join("; ", problems)}");
            return (false, "AI mission rejected: " + string.Join("; ", problems));
        }

        // Suggest = store as an inactive draft for admin review; Auto = publish immediately.
        def.Active = _config.AiLevel == AiLevel.Auto;
        _repo.SaveMission(def);
        _missionDefs[id] = def;

        string verb = def.Active ? "published" : "drafted";
        _log.Info($"AI mission {verb}: '{def.Title}' ({id}).");
        return (true, $"AI mission {verb}: {def.Title} ({id}).");
    }
}
