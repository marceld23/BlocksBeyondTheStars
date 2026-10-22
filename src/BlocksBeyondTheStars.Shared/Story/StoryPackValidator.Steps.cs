// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>One chapter's steps, or (#2476) one story mission's: ids unique in the pack, the objective (event, mission
    /// bridge or — in a mission only — a lead bridge), conditions, alternatives, markers, cutscene hooks and the texts in
    /// budget.</summary>
    private static void ValidateSteps(Run run, in StepOwner owner)
    {
        for (int si = 0; si < owner.Steps.Count; si++)
        {
            var step = owner.Steps[si];
            string sp = $"{owner.Path}.steps[{si}]";
            ValidateStep(run, owner, step, sp);
        }
    }

    /// <summary>One step: its id, kind and tier, its objective, legacy key, conditions, alternative, marker, cutscene hooks,
    /// hint ids and texts.</summary>
    private static void ValidateStep(Run run, in StepOwner owner, StoryStep step, string sp)
    {
        if (string.IsNullOrEmpty(step.Id) || !IsKeySafeId(step.Id, allowDot: true) || !run.StepIds.Add(step.Id))
        {
            run.Error(sp, $"step id '{step.Id}' is empty, not key-safe or used twice in the pack");
        }

        string stepKind = (step.Kind ?? string.Empty).ToLowerInvariant();
        if (!StoryStepKinds.All.Contains(stepKind))
        {
            run.Error($"{sp}.kind", $"unknown kind '{step.Kind}' (do, read, react)");
        }
        else if (owner.Mission && stepKind != StoryStepKinds.Do)
        {
            run.Error($"{sp}.kind", "a mission's steps are tasks: kind 'do'");
        }

        if (!StoryStepTiers.All.Contains((step.Tier ?? string.Empty).ToLowerInvariant()))
        {
            run.Error($"{sp}.tier", $"unknown tier '{step.Tier}' (must, core, opt)");
        }

        ValidateStepObjective(run, sp, step, stepKind, owner.Mission);

        if (!string.IsNullOrEmpty(step.LegacyKey))
        {
            if (!run.Tutorial)
            {
                run.Error($"{sp}.legacyKey", "only a tutorial pack may name legacy keys");
            }
            else if (StoryLegacy.StageIdOf(step.LegacyKey) is null)
            {
                run.Error($"{sp}.legacyKey", $"'{step.LegacyKey}' is not one of the legacy stages ({StoryLegacy.StagePrefix}{string.Join("|", StoryLegacy.StageIds)})");
            }
            else if (!run.LegacyKeys.Add(step.LegacyKey))
            {
                run.Error($"{sp}.legacyKey", $"'{step.LegacyKey}' is used by two steps");
            }
        }

        run.CheckConditions($"{sp}.requires", step.Requires);
        run.CheckConditions($"{sp}.feasibleIf", step.FeasibleIf);
        run.CheckConditions($"{sp}.alreadyDoneIf", step.AlreadyDoneIf);
        if (!string.IsNullOrEmpty(step.Alternative)
            && (step.Alternative == step.Id || !owner.Steps.Any(s => s.Id == step.Alternative)))
        {
            run.Error($"{sp}.alternative", $"'{step.Alternative}' is not another step of {owner.Word} '{owner.Id}'");
        }

        string reveal = step.OnStart?.Reveal ?? string.Empty;
        if (reveal.Length > 0)
        {
            if (!StoryRevealProviders.IsKnown(reveal))
            {
                run.Error($"{sp}.onStart.reveal", $"unknown marker provider '{reveal}' ({StoryRevealProviders.NearestOre}:<block>, {StoryRevealProviders.NearestSettlement}, {StoryRevealProviders.NearestFoodPlant}, {StoryRevealProviders.NearestStation}, {StoryRevealProviders.UniqueSite}:<site>)");
            }
            else if (reveal.StartsWith(StoryRevealProviders.NearestOre + ":", StringComparison.Ordinal)
                     && run.Context.BlockExists is { } blockExists
                     && !blockExists(reveal.Substring(StoryRevealProviders.NearestOre.Length + 1)))
            {
                run.Error($"{sp}.onStart.reveal", $"block '{reveal.Substring(StoryRevealProviders.NearestOre.Length + 1)}' does not exist");
            }
        }

        run.Cutscene($"{sp}.onStart.cutscene", step.OnStart?.Cutscene);
        run.Cutscene($"{sp}.onComplete.cutscene", step.OnComplete?.Cutscene);
        foreach (var (hint, hi) in (step.HintRefs ?? new List<string>()).Select((h, i) => (h, i)))
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                run.Error($"{sp}.hintRefs[{hi}]", "an empty hint id");
            }
        }

        ValidateStepTexts(run, owner, step, sp, stepKind);
    }

    /// <summary>A step's objective: a mission bridge to an existing mission, a lead bridge (in a world story's missions only,
    /// #2476), or an event count with a published event — and a required count of at least 1.</summary>
    private static void ValidateStepObjective(Run run, string sp, StoryStep step, string stepKind, bool mission)
    {
        var objective = step.Objective ?? new StoryStepObjective();
        if (objective.IsMission)
        {
            if (string.IsNullOrEmpty(objective.Target))
            {
                run.Error($"{sp}.objective", "a mission bridge needs a target mission");
            }
            else if (run.Context.MissionExists is { } missionExists && !missionExists(objective.Target))
            {
                run.Error($"{sp}.objective", $"mission '{objective.Target}' does not exist in data/missions.json");
            }
        }
        else if (objective.IsLead)
        {
            // #2476: a lead bridge — a world story's mission step that is followed like the chapter lead it names.
            if (!mission)
            {
                run.Error($"{sp}.objective.type", "a lead bridge is read in a world story's missions only");
            }
            else if (StoryLeadTypes.Problem(objective.Target) is { } leadProblem)
            {
                run.Error($"{sp}.objective.target", leadProblem);
            }
            else if (!StoryObjectiveTypes.IsBridgeableLead(objective.Target))
            {
                run.Error($"{sp}.objective.target", $"'{objective.Target}' cannot be a mission step (explore is never done; the finale is chapter 7's own set piece)");
            }
            else
            {
                string leadKind = StoryLeadTypes.KindOf(objective.Target);
                string leadArg = StoryLeadTypes.ArgOf(objective.Target);
                if (leadKind == StoryLeadTypes.Research && run.Context.BlueprintExists is { } blueprintExists && !blueprintExists(leadArg))
                {
                    run.Error($"{sp}.objective.target", $"blueprint '{leadArg}' does not exist");
                }

                if (leadKind == StoryLeadTypes.Module && run.Context.ModuleExists is { } moduleExists && !moduleExists(leadArg))
                {
                    run.Error($"{sp}.objective.target", $"ship module '{leadArg}' does not exist");
                }
            }
        }
        else if (!string.IsNullOrEmpty(objective.Type))
        {
            run.Error($"{sp}.objective.type", $"unknown objective type '{objective.Type}' (Mission, Lead, or none for an event count)");
        }
        else if (string.IsNullOrEmpty(objective.Event))
        {
            if (stepKind != StoryStepKinds.Read)
            {
                run.Error($"{sp}.objective", "a do or react step needs an event or a mission bridge");
            }
        }
        else
        {
            CheckEvent(run, $"{sp}.objective.event", objective.Event);
        }

        if (objective.Required < 1)
        {
            run.Error($"{sp}.objective.required", "required must be at least 1");
        }
    }

    /// <summary>A step's texts and their variants, within the owner's line budget (a mission step needs a chip text).</summary>
    private static void ValidateStepTexts(Run run, in StepOwner owner, StoryStep step, string sp, string stepKind)
    {
        var explain = step.Explain ?? new StoryStepExplain();
        run.Key($"{sp}.explain.startKey", explain.StartKey, owner.LineEn, owner.LineDe);
        run.Key($"{sp}.explain.hintKey", explain.HintKey, owner.LineEn, owner.LineDe);
        run.Key($"{sp}.explain.doneKey", explain.DoneKey, owner.LineEn, owner.LineDe);
        run.Key($"{sp}.explain.midKey", explain.MidKey, owner.LineEn, owner.LineDe); // #2550: the half-way line
        run.Key($"{sp}.explain.chipKey", explain.ChipKey, ChipMaxEn, ChipMaxDe);
        if (owner.Mission && string.IsNullOrEmpty(explain.ChipKey))
        {
            run.Error($"{sp}.explain.chipKey", "a mission step needs a chip text — it is the step's line in the Journal");
        }
        else if (owner.Mission && run.Context.English?.Invoke(explain.ChipKey) is { } chipEn && Localization.LineText.HasSlot(chipEn))
        {
            run.Error($"{sp}.explain.chipKey", $"'{explain.ChipKey}' needs a {{0}} — a mission step's own chip text is shown without one (a lead bridge brings the lead's placed text itself)");
        }
        else if (stepKind == StoryStepKinds.Do && string.IsNullOrEmpty(explain.ChipKey) && step.Variants.All(v => string.IsNullOrEmpty(v.ChipKey)))
        {
            run.Warn($"{sp}.explain.chipKey", "a do step without a chip text leaves the chip to the next source");
        }

        foreach (var (variant, vi) in step.Variants.Select((v, i) => (v, i)))
        {
            string vp = $"{sp}.variants[{vi}]";
            run.CheckCondition($"{vp}.when", variant.When);
            run.Key($"{vp}.startKey", variant.StartKey, owner.LineEn, owner.LineDe);
            run.Key($"{vp}.hintKey", variant.HintKey, owner.LineEn, owner.LineDe);
            run.Key($"{vp}.doneKey", variant.DoneKey, owner.LineEn, owner.LineDe);
            run.Key($"{vp}.midKey", variant.MidKey, owner.LineEn, owner.LineDe);
            run.Key($"{vp}.chipKey", variant.ChipKey, ChipMaxEn, ChipMaxDe);
        }
    }

    /// <summary>An objective's event: a <c>ui:</c> teaching flag only in a player pack (and one a client reports), a wildcard
    /// only at the end, and an event the server raises (a warning otherwise).</summary>
    private static void CheckEvent(Run run, string path, string eventKey)
    {
        if (GameEventKeys.IsUiEvent(eventKey))
        {
            if (!run.PlayerPack)
            {
                run.Error(path, "'ui:' teaching flags exist only in a player pack's step objectives");
            }
            else if (eventKey.Length == GameEventKeys.UiPrefix.Length || !IsKeySafeId(eventKey.Substring(GameEventKeys.UiPrefix.Length)))
            {
                run.Error(path, $"'{eventKey}' is not a valid teaching flag (ui:<id>)");
            }
            else if (!GameEventKeys.IsKnownUiEventId(eventKey.Substring(GameEventKeys.UiPrefix.Length)))
            {
                run.Warn(path, $"'{eventKey}' is no flag a client reports ({string.Join(", ", GameEventKeys.UiEvents)})");
            }

            return;
        }

        int star = eventKey.IndexOf('*');
        if (star >= 0 && star != eventKey.Length - 1)
        {
            run.Error(path, $"'{eventKey}': a wildcard may only end the event ('craft:*')");
            return;
        }

        bool known = star >= 0
            ? GameEventKeys.All.Any(k => GameEventKeys.Matches(eventKey, k))
            : GameEventKeys.All.Contains(eventKey);
        if (!known)
        {
            run.Warn(path, $"event '{eventKey}' is not raised by the server yet (GameEventKeys)");
        }
    }

    /// <summary>Whose steps <see cref="ValidateSteps"/> checks: a chapter's or (#2476) a story mission's — its path, the word
    /// and id an alternative names, its steps, its line budget and whether the mission rules apply.</summary>
    private readonly struct StepOwner
    {
        public StepOwner(string path, string word, string id, List<StoryStep> steps, int lineEn, int lineDe, bool mission)
        {
            Path = path;
            Word = word;
            Id = id;
            Steps = steps;
            LineEn = lineEn;
            LineDe = lineDe;
            Mission = mission;
        }

        public string Path { get; }

        public string Word { get; }

        public string Id { get; }

        public List<StoryStep> Steps { get; }

        public int LineEn { get; }

        public int LineDe { get; }

        public bool Mission { get; }
    }
}
