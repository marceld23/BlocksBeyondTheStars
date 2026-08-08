// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Optional AI mission generation (technical requirements / `anf_mission_editor.md`). The
/// AI proposes a <see cref="MissionPlan"/>; the server validates and clamps it, then
/// publishes (Auto) or drafts (Suggest) it. Everything keeps working with AI Off or the
/// backend unreachable — failures fall back to "no mission".
///
/// The LLM call blocks up to the backend HTTP timeout, so at runtime generation runs OFF the
/// game thread (same pattern as NPC greetings / board mission texts): <see cref="RequestAiMission"/>
/// kicks it on a task and <see cref="TickAiMissions"/> validates + publishes the result on the tick.
/// Doing it inline on the tick thread would freeze the single-threaded server for every player until
/// the backend responds.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Finished generations awaiting validation + publish on the tick thread.</summary>
    private readonly ConcurrentQueue<(int ConnectionId, MissionPlan? Plan)> _aiMissionOutbox = new();

    /// <summary>Per-admin single-flight guard (keyed by connection): one pending generation at a time.</summary>
    private readonly ConcurrentDictionary<int, byte> _aiMissionInFlight = new();

    /// <summary>
    /// Requests an AI mission for the given context. Returns whether a mission was created
    /// and a human-readable status message. Never throws. SYNCHRONOUS — this blocks on the LLM
    /// call, so it is a test/util seam only; the runtime path is <see cref="RequestAiMission"/>,
    /// which generates off the tick thread.
    /// </summary>
    public (bool Ok, string Message) TryGenerateAiMission(string context)
    {
        if (AiMissionGate() is { } gate)
        {
            return gate;
        }

        MissionPlan? plan;
        try
        {
            plan = _ai.Generate(EnrichMissionContext(context));
        }
        catch
        {
            plan = null; // defensive: providers should not throw, but never crash the caller
        }

        return PublishAiMission(plan);
    }

    /// <summary>Runtime entrypoint for the <c>/ai_mission</c> admin command. Generates OFF the tick thread
    /// (the LLM call blocks up to the HTTP timeout — inline it would freeze the whole server) and enqueues
    /// the plan for <see cref="TickAiMissions"/> to validate + publish. Returns the acknowledgement to show
    /// the admin immediately; the published/rejected result is delivered when the drain runs.</summary>
    private string RequestAiMission(PlayerSession admin, string context)
    {
        if (AiMissionGate() is { } gate)
        {
            return gate.Message;
        }

        int connId = admin.ConnectionId;
        if (!_aiMissionInFlight.TryAdd(connId, 1))
        {
            return "@srv.ai.busy";
        }

        string enriched = EnrichMissionContext(context);
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            MissionPlan? plan;
            try
            {
                plan = _ai.Generate(enriched);
            }
            catch
            {
                plan = null;
            }

            _aiMissionOutbox.Enqueue((connId, plan));
        });

        return "@srv.ai.generating";
    }

    /// <summary>Drains finished AI mission generations: validates, publishes/drafts, and reports the result
    /// to the requesting admin — all on the tick thread (no cross-thread state access). Called per tick.</summary>
    private void TickAiMissions()
    {
        while (_aiMissionOutbox.TryDequeue(out var pending))
        {
            _aiMissionInFlight.TryRemove(pending.ConnectionId, out _);
            var (ok, message) = PublishAiMission(pending.Plan);
            if (_sessions.TryGetValue(pending.ConnectionId, out var session) && session.Joined)
            {
                Send(session, new ServerMessage { Text = message });
                CheatLog(session.State, ok ? "generated an AI mission" : $"AI mission request: {message}");
            }
        }
    }

    /// <summary>The AI-level gate shared by the sync + async entrypoints: a message when mission generation
    /// isn't available at this level, or null when it is.</summary>
    private (bool Ok, string Message)? AiMissionGate() => _config.AiLevel switch
    {
        AiLevel.Off => (false, "@srv.ai.disabled"),
        AiLevel.TextOnly => (false, "@srv.ai.text_only"),
        _ => null,
    };

    /// <summary>Validates an AI-proposed plan and publishes (Auto) or drafts (Suggest) it. Runs on the tick
    /// thread (or the sync test seam). A null plan (backend unavailable) falls back to "no mission".</summary>
    private (bool Ok, string Message) PublishAiMission(MissionPlan? plan)
    {
        if (plan is null)
        {
            _log.Warn("AI backend returned no mission (unavailable or disabled) — falling back to none.");
            return (false, "@srv.ai.unavailable");
        }

        var id = "ai_" + System.Guid.NewGuid().ToString("N");
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
        // Token + verbatim parameter: the title is LLM/admin-authored free text, so only the frame localizes.
        return (true, (def.Active ? "@srv.ai.published:" : "@srv.ai.drafted:") + $"{def.Title} ({id})");
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
