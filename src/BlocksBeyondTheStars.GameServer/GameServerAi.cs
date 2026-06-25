// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

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
            plan = _ai.Generate(EnrichMissionContext(context));
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

    /// <summary>L0: appends the allowed objective targets and reward items (real content keys) to the
    /// admin's free-text context, so the LLM picks from them instead of hallucinating keys the
    /// validator would reject. Bounded lists keep the prompt small.</summary>
    private string EnrichMissionContext(string context)
    {
        var targets = new List<string>();
        foreach (var b in _content.Blocks.Values)
        {
            if (b.Mineable && b.Drops.Count > 0 && targets.Count < 40)
            {
                targets.Add(b.Key);
            }
        }

        var rewards = new List<string>();
        foreach (var item in _content.Items.Values)
        {
            if (item.Category is Shared.Definitions.ItemCategory.Component or Shared.Definitions.ItemCategory.Consumable
                && rewards.Count < 30)
            {
                rewards.Add(item.Key);
            }
        }

        return $"{context}\n" +
               $"Allowed objective targets (Mine/Collect/Deliver): {string.Join(", ", targets)}\n" +
               $"Allowed reward items: {string.Join(", ", rewards)}";
    }
}
