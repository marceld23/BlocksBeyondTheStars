// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world story's story missions (#2476, synthesis §4.8 option O3: "VEGA story missions per chapter — automatically accepted
/// steps, layered on the leads"). A chapter of the world story may list <see cref="StoryChapter.Missions"/>; they are compiled
/// once into a per-player sub-track of that story — a <see cref="CompiledPack"/> with the story's id whose optional chapters are
/// the missions — and run by the player-pack step runtime (<c>GameServerStoryPacks.cs</c>: activation, events, the mission
/// bridge, alternatives, escalation, completion). What is special lives here:
/// <list type="bullet">
/// <item><b>Handing out.</b> When a player's story track is in a chapter (derived, <c>max(1, world)</c>), VEGA starts that chapter's
/// first mission that is not through — one at a time, in data order — once no basic training runs for the player and no story
/// card or scene still waits in their cutscene queue (so the chapter's scene comes first). Its first step's line is the briefing;
/// the Journal's VEGA page lists it under "Missions in this chapter". A mission whose <c>feasibleIf</c> does not hold is skipped
/// silently and its <c>alternative</c> runs instead (no dead ends). Keys: <c>story:&lt;story&gt;:ch:&lt;mission&gt;:active|done|skipped</c>,
/// the steps' <c>story:&lt;story&gt;:step:&lt;step&gt;</c> — per player, so in multiplayer everyone does their own.</item>
/// <item><b>Letting go.</b> A mission never holds a beat back: the score keeps revealing them. When the player's chapter moves on,
/// a mission of an earlier chapter that is not finished is closed as skipped (its satisfied ones count as done first).</item>
/// <item><b>Lead bridges</b> (<see cref="StoryObjectiveTypes.Lead"/>): a step that is one of the chapter-lead words is followed
/// exactly like that lead (the lead listener's events, the lead's evidence, the heal tank's spot), shows the lead's own
/// place-aware text on the chip where it can be followed from here (its own chip text elsewhere) and marks the lead's place.</item>
/// <item><b>The chip.</b> A running mission takes the chapter lead's place (rank "VEGA lead" of synthesis §5.3, source
/// <c>story</c>, the story's id as its pack) — never over the open finale. Leads keep counting in the background.</item>
/// <item><b>The bonus.</b> Finishing a mission counts as following a lead of its chapter for the chapter bonus: once per save and
/// chapter (<see cref="PayLeadBonus"/>), never after the arc's last beat. No other reward.</item>
/// <item><b>Older clients</b> (no <c>packs1</c>) get no missions — no lines, no state; they keep the story chip they know.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    // ---------------------------------------------------------------------------------------------
    // Compile.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Compiles a world story's missions into its mission sub-track. Throws <see cref="FormatException"/> on a structural
    /// or condition error (the validators report the same things to authors).</summary>
    private CompiledPack CompileStoryMissions(StoryDefinition def)
    {
        var predicates = WorldPredicateNames;
        var pack = new CompiledPack(def) { IsStoryMissions = true };
        var byId = new Dictionary<string, CompiledMission>(StringComparer.Ordinal);
        for (int ci = 0; ci < def.Chapters.Count; ci++)
        {
            var storyChapter = def.Chapters[ci];
            foreach (var missionDef in storyChapter.Missions ?? new List<StoryMission>())
            {
                if (missionDef is null || string.IsNullOrEmpty(missionDef.Id) || byId.ContainsKey(missionDef.Id)
                    || def.Chapters.Any(c => c.Id == missionDef.Id))
                {
                    throw new FormatException($"mission id '{missionDef?.Id}' is empty, used twice or a chapter's id");
                }

                var chapterDef = new StoryChapter
                {
                    Id = missionDef.Id,
                    TitleKey = missionDef.TitleKey ?? string.Empty,
                    SummaryKey = missionDef.SummaryKey ?? string.Empty,
                    Optional = true,
                    Skippable = missionDef.Skippable,
                    Steps = missionDef.Steps ?? new List<StoryStep>(),
                };
                var chapter = new CompiledChapter(pack, chapterDef);
                pack.Chapters.Add(chapter);
                pack.ChaptersById[chapter.Id] = chapter;
                CompileSteps(pack, chapter, predicates);
                var mission = new CompiledMission(chapter, missionDef, storyChapter.Id, ci)
                {
                    FeasibleIf = StoryConditionParser.ParseList(missionDef.FeasibleIf, predicates),
                };
                pack.Missions.Add(mission);
                byId[mission.Id] = mission;
            }
        }

        LinkStepAlternatives(pack);
        foreach (var mission in pack.Missions)
        {
            string alt = mission.Def.Alternative ?? string.Empty;
            if (alt.Length == 0)
            {
                continue;
            }

            if (!byId.TryGetValue(alt, out var alternative) || alternative == mission || alternative.StoryChapterId != mission.StoryChapterId)
            {
                throw new FormatException($"mission '{mission.Id}' names the alternative '{alt}', which is not another mission of its chapter");
            }

            mission.Alternative = alternative;
            alternative.IsAlternative = true;
        }

        foreach (var step in pack.StepsById.Values)
        {
            if (step.IsLeadBridge && !StoryObjectiveTypes.IsBridgeableLead(step.LeadType))
            {
                throw new FormatException($"step '{step.Id}' bridges the lead '{step.LeadType}', which no player action follows");
            }

            if (!step.IsDo)
            {
                throw new FormatException($"step '{step.Id}' of a story mission is not a 'do' step");
            }
        }

        return pack;
    }

    // ---------------------------------------------------------------------------------------------
    // State readers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The active world story's mission sub-track, or null (no story, none defined).</summary>
    private CompiledPack? StoryMissionsPack
    {
        get
        {
            if (_story is null)
            {
                return null;
            }

            _ = PlayerPacks; // compiles the missions with the player packs (once)
            return _storyMissionPacks.TryGetValue(_story.Id, out var pack) ? pack : null;
        }
    }

    /// <summary>Whether story missions run for this player now: a current client (<c>packs1</c>) in the world, the world story has
    /// missions and the player's story track started. <paramref name="pack"/> is the sub-track.</summary>
    private bool StoryMissionsRunFor(PlayerSession s, out CompiledPack pack)
    {
        pack = null!;
        if (!s.Joined || s.Spectating || !s.SupportsPacks || StoryMissionsPack is not { } missions || !StoryTrackStarted(s))
        {
            return false;
        }

        pack = missions;
        return true;
    }

    /// <summary>The mission whose chapter <paramref name="chapter"/> is (or null for an ordinary pack chapter).</summary>
    private static CompiledMission? MissionOf(CompiledChapter chapter)
        => chapter.Pack.IsStoryMissions ? chapter.Pack.Missions.FirstOrDefault(m => m.Chapter == chapter) : null;

    // ---------------------------------------------------------------------------------------------
    // Handing out, walking, letting go.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Brings a player's story missions up to date: running missions walk (a step activates and speaks, a satisfied
    /// mission is done); a mission of a chapter the player has left is let go (done when satisfied, else skipped — never a new
    /// line for it); and when none of the current chapter's runs, the next one is handed out. Cheap (milestone lookups).</summary>
    private void AdvanceStoryMissions(PlayerSession s)
    {
        if (!StoryMissionsRunFor(s, out var pack))
        {
            return;
        }

        for (int pass = 0; pass < 16; pass++) // bounded: each changing pass finishes, lets go or starts a mission
        {
            bool changed = false;
            int current = TrackChapterIndex(s);
            foreach (var mission in pack.Missions)
            {
                if (!ChapterRuns(s, mission.Chapter))
                {
                    continue;
                }

                if (mission.StoryChapterIndex < current)
                {
                    // The story moved on: what is through counts, the rest is let go — no step of it speaks any more.
                    bool satisfied = ChapterSatisfied(s, mission.Chapter);
                    if (!satisfied)
                    {
                        SkipChapterSteps(s, mission.Chapter);
                    }

                    CompleteChapter(s, mission.Chapter, skipped: !satisfied, silent: true);
                    changed = true;
                }
                else
                {
                    changed |= WalkChapter(s, mission.Chapter);
                }
            }

            if (!changed)
            {
                changed = StartNextMission(s, pack, current);
            }

            if (!changed)
            {
                break;
            }
        }

        SyncStepListeners(); // cheap when nothing changed — a step activated in this walk listens from now on
    }

    /// <summary>Hands out the next mission of the player's chapter when none of it runs: the first one (in data order) that is not
    /// through and stands in for nobody; an infeasible one is skipped silently and its alternative starts instead (or is skipped
    /// too when it cannot be done either). Waits while the basic training runs and while a story card or scene still waits in the
    /// player's cutscene queue. Returns whether anything changed.</summary>
    private bool StartNextMission(PlayerSession s, CompiledPack pack, int current)
    {
        var missions = pack.Missions.Where(m => m.StoryChapterIndex == current).ToList();
        if (missions.Count == 0 || missions.Any(m => ChapterRuns(s, m.Chapter)) || !MissionStartAllowed(s))
        {
            return false;
        }

        bool changed = false;
        foreach (var mission in missions)
        {
            if (mission.IsAlternative || ChapterDoneOrSkipped(s, mission.Chapter))
            {
                continue;
            }

            if (EvaluateStoryCondition(mission.FeasibleIf, s))
            {
                BeginMission(s, mission);
                return true;
            }

            // No dead ends: what this world cannot offer is skipped silently — its alternative stands in.
            CompleteChapter(s, mission.Chapter, skipped: true, silent: true);
            changed = true;
            if (mission.Alternative is { } alt && !ChapterDoneOrSkipped(s, alt.Chapter))
            {
                if (EvaluateStoryCondition(alt.FeasibleIf, s))
                {
                    BeginMission(s, alt);
                    return true;
                }

                CompleteChapter(s, alt.Chapter, skipped: true, silent: true);
            }
        }

        return changed;
    }

    /// <summary>Whether VEGA may hand a mission out now: no basic training runs for the player (no story task during the
    /// lessons) and no story card or scene waits in their cutscene queue (the chapter's scene comes first, then the briefing).</summary>
    private bool MissionStartAllowed(PlayerSession s)
        => !TutorialPackRunning(s) && !s.Story.CutsceneQueue.Any(q => q.Tier >= CutsceneTiers.Card);

    /// <summary>Starts a mission for a player: it runs from now on (its first step activates in the same walk and speaks the
    /// briefing).</summary>
    private void BeginMission(PlayerSession s, CompiledMission mission)
    {
        MilestonesOf(s).Add(StoryPackKeys.ChapterActive(mission.Chapter.Pack.Id, mission.Id));
        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        _log.Info($"Story mission '{mission.Id}' (chapter '{mission.StoryChapterId}') handed to '{s.State.Name}'.");
    }

    /// <summary><see cref="CompleteChapter"/>'s hook for a mission: one finished by playing counts as following a lead of its
    /// chapter for the chapter bonus — once per save and chapter (no double pay with the leads), never after the last beat. A
    /// mission none of whose steps was done (each one impossible here and skipped) closes as skipped instead.</summary>
    private void OnStoryMissionClosed(PlayerSession s, CompiledChapter chapter, bool skipped)
    {
        if (skipped || MissionOf(chapter) is not { } mission)
        {
            return;
        }

        var m = MilestonesOf(s);
        string packId = chapter.Pack.Id;
        if (!chapter.Steps.Any(st => m.Contains(StoryPackKeys.StepDone(packId, st.Id))))
        {
            m.Remove(StoryPackKeys.ChapterDone(packId, mission.Id));
            m.Add(StoryPackKeys.ChapterSkipped(packId, mission.Id));
            _repo.SavePlayer(s.State);
            return;
        }

        _log.Info($"Story mission '{mission.Id}' done by '{s.State.Name}'.");
        PayLeadBonus(mission.StoryChapterId);
    }

    // ---------------------------------------------------------------------------------------------
    // The chip, lead bridges, events.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The running mission step VEGA's chip shows for this player — the first active step of the first running mission of
    /// their current chapter — or null.</summary>
    private CompiledStep? MissionChipStep(PlayerSession s)
    {
        if (!StoryMissionsRunFor(s, out var pack))
        {
            return null;
        }

        int current = TrackChapterIndex(s);
        foreach (var mission in pack.Missions)
        {
            if (mission.StoryChapterIndex != current || !ChapterRuns(s, mission.Chapter))
            {
                continue;
            }

            foreach (var step in mission.Chapter.Steps)
            {
                if (step.IsDo && StepActive(s, step) && !StepComplete(s, step))
                {
                    return step;
                }
            }
        }

        return null;
    }

    /// <summary>The story objective of a running mission (key, {0}, source), or null when none runs.</summary>
    private (string Key, string Arg, string Source)? MissionObjectiveFor(PlayerSession s)
    {
        if (MissionChipStep(s) is not { } step)
        {
            return null;
        }

        var (key, arg) = MissionStepText(s, step);
        return key.Length > 0 ? (key, arg, ObjectiveSources.Story) : null;
    }

    /// <summary>A mission step's text while it runs: a lead bridge that can be followed from here shows its lead's own placed text
    /// ("Visit the Sealed Observatory on Kepler-3 b", "Find the net fragment on …" — the fragment's map signal comes with it);
    /// anything else its own chip text.</summary>
    private (string Key, string Arg) MissionStepText(PlayerSession s, CompiledStep step)
    {
        if (step.IsLeadBridge && MissionLeadFor(s, step) is { Feasible: true } lead && lead.ObjKey.Length > 0)
        {
            return (lead.ObjKey, lead.ChipArg);
        }

        return (StepChipKey(s, step), string.Empty);
    }

    /// <summary>The lead a lead bridge resolves to for this player (as its chapter would resolve it), cached per session — resolved
    /// again only when the step or <see cref="StoryLeadSignature"/> changed.</summary>
    private ResolvedStoryLead? MissionLeadFor(PlayerSession s, CompiledStep step)
    {
        if (!step.IsLeadBridge || _story is null || MissionOf(step.Chapter) is not { } mission
            || mission.StoryChapterIndex < 0 || mission.StoryChapterIndex >= _story.Chapters.Count)
        {
            return null;
        }

        var cache = s.Story.StoryLeads;
        var signature = LeadSignatureOf(s, cache.Epoch);
        if (cache.MissionLead is null || cache.MissionLeadStep != step.ProgressKey || cache.MissionSignature != signature)
        {
            _missionLeadResolutions++;
            cache.MissionLead = ResolveLead(s, _story.Chapters[mission.StoryChapterIndex], new StoryLead { Type = step.LeadType }, 0, standIn: false);
            cache.MissionLeadStep = step.ProgressKey;
            cache.MissionSignature = signature;
        }

        return cache.MissionLead;
    }

    /// <summary>How many mission lead resolutions ran (the "no per-tick cost" guard of the tests).</summary>
    private int _missionLeadResolutions;

    /// <summary>A lead bridge's evidence: what follows its lead without an action now — the blueprint researched, the module fitted,
    /// the site landed on (or boarded) before.</summary>
    private bool LeadBridgeEvidence(PlayerSession s, CompiledStep step) => step.LeadKind switch
    {
        StoryLeadTypes.Research => s.State.UnlockedBlueprints.Contains(step.LeadArg),
        StoryLeadTypes.Module => ShipOf(s).HasModule(step.LeadArg),
        StoryLeadTypes.UniqueSite => UniqueSiteTarget(step.LeadArg) is { } site && s.State.LandedBodies.Contains(site),
        _ => false,
    };

    /// <summary>Whether a unique site can be followed by this player — the lead's rule: it exists in this save (a world stamped
    /// before the sites were added skipped it) and they can get there.</summary>
    private bool UniqueSiteFollowable(PlayerSession s, string kind)
        => UniqueSiteTarget(kind) is { } site && UniqueSiteExists(kind, site) && CanReachBody(s, site);

    /// <summary>The lead listener's second half (#2476): an action that follows a lead also finishes the player's active lead
    /// bridges of that lead word — after the chapter's own lead was booked, so the lead counts for the chapter they acted in.</summary>
    private void OnMissionLeadEvent(PlayerSession actor, string key, string arg)
    {
        if (actor.State.StepProgress.Count == 0 || !StoryMissionsRunFor(actor, out var pack))
        {
            return;
        }

        List<CompiledStep>? hits = null;
        foreach (string progressKey in actor.State.StepProgress.Keys)
        {
            if (_stepByProgressKey.TryGetValue(progressKey, out var step) && step.Pack == pack && step.IsLeadBridge
                && !StepComplete(actor, step)
                && LeadFulfilledBy(step.LeadKind, step.LeadArg, BridgeTarget(step), key, arg))
            {
                (hits ??= new List<CompiledStep>()).Add(step);
            }
        }

        if (hits is null)
        {
            return;
        }

        foreach (var step in hits)
        {
            CompleteMissionStep(actor, step);
        }

        AdvanceStoryMissions(actor);
        SendVegaObjective(actor);
    }

    /// <summary>The body or station a unique-site bridge points at (its arrival completes it), else empty.</summary>
    private string BridgeTarget(CompiledStep step)
        => step.LeadKind == StoryLeadTypes.UniqueSite ? UniqueSiteTarget(step.LeadArg) ?? string.Empty : string.Empty;

    /// <summary>Completes an active mission step — speaking its done line when it was the mission's current step.</summary>
    private void CompleteMissionStep(PlayerSession s, CompiledStep step)
    {
        if (!s.State.StepProgress.ContainsKey(step.ProgressKey))
        {
            return;
        }

        bool wasChip = ChipStepOf(s, step.Pack) == step;
        CompleteStep(s, step, silent: !wasChip);
    }

    // ---------------------------------------------------------------------------------------------
    // The tick and the join.
    // ---------------------------------------------------------------------------------------------

    /// <summary>1 Hz per player on a world (from <see cref="TickShipAi"/>, after the leads): hands missions out and lets them go;
    /// a running step whose world fact went away (the last fragment found by someone else) gives way like an infeasible one; a
    /// lead bridge is followed by its evidence or by standing at the heal tank; the step on the chip gets the help escalation
    /// (a hint line after a while without progress).</summary>
    private void TickStoryMissions(PlayerSession s)
    {
        if (!StoryMissionsRunFor(s, out _))
        {
            return;
        }

        if (PlayerPacks.Count == 0)
        {
            s.Story.StoryPackPlaySeconds += 1.0; // the escalation clock (TickStoryPacksFor runs it while player packs exist)
        }

        AdvanceStoryMissions(s);
        if (MissionChipStep(s) is not { } step)
        {
            return;
        }

        if (step.FeasibleIf is not null && !EvaluateStoryCondition(step.FeasibleIf, s))
        {
            MarkStepSkipped(s, step);
            if (step.Alternative is { } alt && !StepComplete(s, alt) && EvaluateStoryCondition(alt.FeasibleIf, s))
            {
                ActivateStep(s, alt);
            }

            AdvanceStoryMissions(s);
            SendVegaObjective(s);
            return;
        }

        if (step.IsLeadBridge && (LeadBridgeEvidence(s, step) || (MissionLeadFor(s, step) is { } lead && AtHealTank(s, lead))))
        {
            CompleteMissionStep(s, step);
            AdvanceStoryMissions(s);
            SendVegaObjective(s);
            return;
        }

        EscalateStep(s, step);
    }

    /// <summary>The join (after the story track's start rule): the running mission's steps listen again. A mission is never
    /// handed out at the join itself — the 1 Hz pass does that once the chapter's scene went out.</summary>
    private void StoryMissionsOnJoin(PlayerSession s)
    {
        if (StoryMissionsPack is null || s.Spectating)
        {
            return;
        }

        _stepListenersDirty = true;
        SyncStepListeners();
    }

    // ---------------------------------------------------------------------------------------------
    // The player's actions and the Journal.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The world story's <see cref="StoryPackIntent"/> actions besides the track: skipping a running mission
    /// (<see cref="StoryPackActions.SkipChapter"/> with the mission id) or one of its steps. Nothing else applies.</summary>
    private void ApplyStoryMissionIntent(PlayerSession s, StoryPackIntent intent, string action)
    {
        if (!StoryMissionsRunFor(s, out var pack))
        {
            return;
        }

        if (action == StoryPackActions.SkipChapter && pack.ChaptersById.TryGetValue(intent.ChapterId ?? string.Empty, out var mission))
        {
            SkipChapter(s, mission);
        }
        else if (action == StoryPackActions.SkipStep && pack.StepsById.TryGetValue(intent.StepId ?? string.Empty, out var step))
        {
            SkipStep(s, step);
        }
        else
        {
            return;
        }

        AdvanceStoryMissions(s);
        SendVegaObjective(s);
    }

    /// <summary>The Journal's mission rows of the VEGA page: the missions of the player's current chapter VEGA handed out — running
    /// or done — with their steps (done, running, ahead; a step that was skipped or never needed as a stand-in is left out).</summary>
    private NetStoryMission[] JournalMissions(PlayerSession s)
    {
        if (!StoryMissionsRunFor(s, out var pack))
        {
            return Array.Empty<NetStoryMission>();
        }

        int current = TrackChapterIndex(s);
        var chip = MissionChipStep(s);
        var m = MilestonesOf(s);
        var rows = new List<NetStoryMission>();
        foreach (var mission in pack.Missions)
        {
            if (mission.StoryChapterIndex != current)
            {
                continue;
            }

            bool done = m.Contains(StoryPackKeys.ChapterDone(pack.Id, mission.Id));
            bool running = ChapterRuns(s, mission.Chapter);
            if (!done && !running)
            {
                continue;
            }

            rows.Add(new NetStoryMission
            {
                Id = mission.Id,
                ChapterId = mission.StoryChapterId,
                TitleKey = mission.Def.TitleKey ?? string.Empty,
                SummaryKey = mission.Def.SummaryKey ?? string.Empty,
                State = done ? StoryChapterStates.Done : StoryChapterStates.Active,
                Skippable = running && (mission.Def.Skippable ?? true),
                Steps = JournalMissionSteps(s, mission, chip),
            });
        }

        return rows.ToArray();
    }

    private NetStoryMissionStep[] JournalMissionSteps(PlayerSession s, CompiledMission mission, CompiledStep? chip)
    {
        var m = MilestonesOf(s);
        string packId = mission.Chapter.Pack.Id;
        var rows = new List<NetStoryMissionStep>();
        foreach (var step in mission.Chapter.Steps)
        {
            bool done = m.Contains(StoryPackKeys.StepDone(packId, step.Id));
            bool active = StepActive(s, step) && !StepComplete(s, step);
            if ((!done && m.Contains(StoryPackKeys.StepSkipped(packId, step.Id))) || (step.IsAlternative && !done && !active))
            {
                continue; // skipped (its stand-in, if any, shows) — or a stand-in nobody needed
            }

            var (key, arg) = active ? MissionStepText(s, step) : (StepChipKey(s, step), string.Empty);
            s.State.StepProgress.TryGetValue(step.ProgressKey, out int progress);
            rows.Add(new NetStoryMissionStep
            {
                Id = step.Id,
                Key = key,
                Arg = arg,
                State = done ? StoryChapterStates.Done : active ? StoryChapterStates.Active : StoryChapterStates.Upcoming,
                Progress = done ? step.Required : progress,
                Target = step.Required,
                OnChip = step == chip,
            });
        }

        return rows.ToArray();
    }

    /// <summary>The missions part of <c>/story</c>: the running mission and its current step, and how many are done.</summary>
    private string StoryMissionStatus(PlayerSession who)
    {
        if (StoryMissionsPack is not { } pack || !who.SupportsPacks)
        {
            return string.Empty;
        }

        var m = MilestonesOf(who);
        var running = pack.Missions.FirstOrDefault(x => ChapterRuns(who, x.Chapter));
        int done = pack.Missions.Count(x => m.Contains(StoryPackKeys.ChapterDone(pack.Id, x.Id)));
        string step = running is null ? string.Empty : running.Chapter.Steps.FirstOrDefault(st => StepActive(who, st) && !StepComplete(who, st))?.Id ?? string.Empty;
        return $", mission={(running is null ? "-" : running.Id + (step.Length > 0 ? "/" + step : string.Empty))}, missionsDone={done}/{pack.Missions.Count(x => !x.IsAlternative)}";
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: the 1 Hz mission pass for one player.</summary>
    internal void TickStoryMissionsForTest(PlayerSession s) => TickStoryMissions(s);

    /// <summary>Test seam: the active world story's mission ids in data order (empty without any).</summary>
    internal IReadOnlyList<string> StoryMissionIdsForTest => StoryMissionsPack?.Missions.Select(x => x.Id).ToList() ?? new List<string>();

    /// <summary>Test seam: the id of the mission step on VEGA's chip for this player, or null.</summary>
    internal string? MissionChipStepForTest(PlayerSession s) => MissionChipStep(s)?.Id;

    /// <summary>Test seam: the mission ids running for this player now.</summary>
    internal IReadOnlyList<string> RunningMissionsForTest(PlayerSession s)
        => StoryMissionsPack is { } pack ? pack.Missions.Where(x => ChapterRuns(s, x.Chapter)).Select(x => x.Id).ToList() : new List<string>();

    /// <summary>Test seam: the Journal's mission rows for a player.</summary>
    internal NetStoryMission[] JournalMissionsForTest(PlayerSession s) => JournalMissions(s);

    /// <summary>Test seam: how many mission lead resolutions ran so far.</summary>
    internal int MissionLeadResolutionsForTest => _missionLeadResolutions;

    /// <summary>Test seam: the <c>/story</c> missions part for a player.</summary>
    internal string StoryMissionStatusForTest(PlayerSession s) => StoryMissionStatus(s);
}
