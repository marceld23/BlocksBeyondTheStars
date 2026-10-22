// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>The chapters, in order: ids, titles, conditions, cutscene hooks, how a world pack's arc reaches them (#2470),
    /// their leads (#2475), their steps and their story missions (#2476).</summary>
    private static void ValidateChapters(Run run)
    {
        run.AllChapterIds = new HashSet<string>(run.Def.Chapters.Where(c => c is not null).Select(c => c.Id ?? string.Empty), StringComparer.Ordinal);
        int lastFromBeat = -1;
        for (int ci = 0; ci < run.Def.Chapters.Count; ci++)
        {
            var ch = run.Def.Chapters[ci];
            string cp = $"chapters[{ci}]";
            if (string.IsNullOrEmpty(ch.Id) || !IsKeySafeId(ch.Id, allowDot: true) || !run.ChapterIds.Add(ch.Id))
            {
                run.Error(cp, $"chapter id '{ch.Id}' is empty, not key-safe or used twice");
            }

            if (string.IsNullOrEmpty(ch.TitleKey))
            {
                run.Error(cp, "the chapter has no titleKey");
            }

            run.Key($"{cp}.titleKey", ch.TitleKey);
            run.Key($"{cp}.summaryKey", ch.SummaryKey);
            run.CheckConditions($"{cp}.requires", ch.Requires);
            run.CheckConditions($"{cp}.offerWhen", ch.OfferWhen);
            run.CheckConditions($"{cp}.autoCompleteIf", ch.AutoCompleteIf);
            run.CheckConditions($"{cp}.visibleIf", ch.VisibleIf);
            if (ch.OfferWhen.Count > 0 && !ch.Optional)
            {
                run.Warn($"{cp}.offerWhen", "only an optional chapter is offered");
            }

            if (ch.VisibleIf.Count > 0 && !ch.Optional)
            {
                run.Error($"{cp}.visibleIf", "only an optional chapter can be left out (#2482) — a mandatory one would block the pack");
            }

            run.Cutscene($"{cp}.openCutscene", ch.OpenCutscene);
            run.Cutscene($"{cp}.closeCutscene", ch.CloseCutscene);
            if (!run.PlayerPack)
            {
                // #2470: a world pack's chapters are reached by the arc — a beat or an arc event.
                if (ch.FromBeat < -1 || ch.FromBeat >= run.Def.Beats.Count)
                {
                    run.Error($"{cp}.fromBeat", $"beat {ch.FromBeat} does not exist (0–{run.Def.Beats.Count - 1}, or -1 for none)");
                }

                if (!string.IsNullOrEmpty(ch.FromEvent) && !StoryChapterEvents.All.Contains(ch.FromEvent))
                {
                    run.Error($"{cp}.fromEvent", $"unknown arc event '{ch.FromEvent}' ({string.Join(", ", StoryChapterEvents.All)})");
                }

                if (ch.FromBeat < 0 && string.IsNullOrEmpty(ch.FromEvent))
                {
                    run.Warn(cp, "a world-pack chapter without fromBeat or fromEvent is never reached");
                }

                if (ch.FromBeat >= 0 && ch.FromBeat < lastFromBeat)
                {
                    run.Error($"{cp}.fromBeat", $"beat {ch.FromBeat} comes before the previous chapter's beat {lastFromBeat} — chapters follow the beats in order");
                }

                lastFromBeat = Math.Max(lastFromBeat, ch.FromBeat);
            }
            else if (ch.FromBeat >= 0 || !string.IsNullOrEmpty(ch.FromEvent))
            {
                run.Warn(cp, "fromBeat / fromEvent are read for world packs only");
            }

            ValidateChapterLeads(run, cp, ch);

            if (run.PlayerPack && ch.Steps.Count == 0)
            {
                run.Warn(cp, "a chapter of a player pack without steps completes at once");
            }

            bool basic = run.Tutorial && !ch.Optional;
            int lineEn = basic ? BasicLineMaxEn : LineMaxEn;
            int lineDe = basic ? BasicLineMaxDe : LineMaxDe;
            ValidateSteps(run, new StepOwner(cp, "chapter", ch.Id, ch.Steps, lineEn, lineDe, mission: false));
            ValidateMissions(run, cp, ch);
        }
    }

    /// <summary>One chapter's leads (#2475).</summary>
    private static void ValidateChapterLeads(Run run, string cp, StoryChapter ch)
    {
        foreach (var (lead, li) in ch.Leads.Select((l, i) => (l, i)))
        {
            // #2475: the lead vocabulary v1 — a known word, its argument where it needs one, and that argument exists.
            string lp = $"{cp}.leads[{li}]";
            if (lead is null)
            {
                run.Error(lp, "an empty lead");
                continue;
            }

            if (StoryLeadTypes.Problem(lead.Type) is { } leadProblem)
            {
                run.Error($"{lp}.type", leadProblem);
                continue;
            }

            if (run.PlayerPack)
            {
                run.Warn(lp, "leads are read for world packs only");
            }

            string leadKind = StoryLeadTypes.KindOf(lead.Type);
            string leadArg = StoryLeadTypes.ArgOf(lead.Type);
            if (leadKind == StoryLeadTypes.Research && run.Context.BlueprintExists is { } blueprintExists && !blueprintExists(leadArg))
            {
                run.Error($"{lp}.type", $"blueprint '{leadArg}' does not exist");
            }

            if (leadKind == StoryLeadTypes.Module && run.Context.ModuleExists is { } moduleExists && !moduleExists(leadArg))
            {
                run.Error($"{lp}.type", $"ship module '{leadArg}' does not exist");
            }

            if (!string.IsNullOrEmpty(lead.ObjKey))
            {
                run.Key($"{lp}.objKey", lead.ObjKey, ChipMaxEn, ChipMaxDe);
            }
            else
            {
                run.Key($"{lp}.type", StoryLeads.ObjKey(lead, hasPlace: true, needsJump: false), ChipMaxEn, ChipMaxDe); // the built-in text
            }
        }
    }

    /// <summary>#2476: a world chapter's story missions — ids unique in the pack and never a chapter's id (they share the
    /// chapter keys), a title, feasibility, an alternative of the same chapter, and steps by the mission rules (do steps with a
    /// chip text; lead bridges allowed).</summary>
    private static void ValidateMissions(Run run, string cp, StoryChapter ch)
    {
        var missions = ch.Missions ?? new List<StoryMission>();
        for (int mi = 0; mi < missions.Count; mi++)
        {
            var m = missions[mi];
            string mp = $"{cp}.missions[{mi}]";
            if (m is null)
            {
                run.Error(mp, "an empty mission");
                continue;
            }

            if (run.PlayerPack)
            {
                run.Warn(mp, "story missions are read for world packs only");
            }

            if (string.IsNullOrEmpty(m.Id) || !IsKeySafeId(m.Id, allowDot: true) || run.AllChapterIds.Contains(m.Id) || !run.MissionIds.Add(m.Id))
            {
                run.Error(mp, $"mission id '{m.Id}' is empty, not key-safe, used twice or a chapter's id");
            }

            if (string.IsNullOrEmpty(m.TitleKey))
            {
                run.Error(mp, "the mission has no titleKey");
            }

            run.Key($"{mp}.titleKey", m.TitleKey);
            run.Key($"{mp}.summaryKey", m.SummaryKey);
            run.CheckConditions($"{mp}.feasibleIf", m.FeasibleIf);
            if (!string.IsNullOrEmpty(m.Alternative) && (m.Alternative == m.Id || !missions.Any(o => o is not null && o.Id == m.Alternative)))
            {
                run.Error($"{mp}.alternative", $"'{m.Alternative}' is not another mission of chapter '{ch.Id}'");
            }

            var steps = m.Steps ?? new List<StoryStep>();
            if (steps.Count == 0)
            {
                run.Error($"{mp}.steps", "a mission needs at least one step");
            }

            ValidateSteps(run, new StepOwner(mp, "mission", m.Id, steps, LineMaxEn, LineMaxDe, mission: true));
        }
    }
}
