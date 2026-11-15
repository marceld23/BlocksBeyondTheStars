// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3686: story scenes that play late or at the wrong place. A scene waiting in a player's queue (the cooldown, the client
/// not in the world yet, a re-offer at the join) is dropped unsent — stamped seen, the lines it held go out — once the
/// player's story has moved past its moment (<see cref="IsOvertakenCutscene"/>), and the next scene of the same story
/// follows after <see cref="CutsceneSamePackCooldownSeconds"/> instead of the full cooldown. A scene the client already
/// received cannot be recalled; the client plays place-bound scenes where they happen (<c>playInPlace</c>), so its own
/// wait stays short.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3686: the spacing between two unrequested T2+ scenes of the same story pack — one story's moments that come
    /// close together (the landing, then the Eldest) follow each other after a breath, not after
    /// <see cref="CutsceneCooldownSeconds"/>. Two different stories keep the full cooldown.</summary>
    internal const double CutsceneSamePackCooldownSeconds = 15.0;

    /// <summary>The cooldown <paramref name="item"/> waits for after the last unrequested T2+ scene: the short one when that
    /// scene was of the same pack (#3686), else <see cref="CutsceneCooldownSeconds"/>.</summary>
    private static double CutsceneCooldownFor(PlayerSession session, PendingCutscene item)
        => string.Equals(session.Story.CutsceneLastSentPack, item.PackId, StringComparison.Ordinal)
            ? CutsceneSamePackCooldownSeconds
            : CutsceneCooldownSeconds;

    /// <summary>Whether a queued scene's moment is past for the player, so it is dropped instead of sent: a player pack's
    /// chapter opener whose chapter is through (#2473, <see cref="IsStaleChapterOpener"/>), the running world story's chapter
    /// opener once a later chapter has begun (<see cref="IsStaleWorldChapterOpener"/>), or — unless its moment was past
    /// already when it was committed (<see cref="PendingCutscene.MayBeOvertaken"/>) — a scene a step has overtaken
    /// (<see cref="IsOvertakenByStep"/>).</summary>
    private bool IsOvertakenCutscene(PlayerSession session, PendingCutscene item)
        => IsStaleChapterOpener(session, item)
           || IsStaleWorldChapterOpener(session, item)
           || (item.MayBeOvertaken && IsOvertakenByStep(session, item));

    /// <summary>
    /// #3686: a queued opening of the running world story's chapter (VEGA's <c>ch2.open</c> …) once a later chapter has begun
    /// for the player — the player's chapter is derived from the world's beats (<see cref="TrackChapterIndex"/>; several beats
    /// revealed at once, or a re-offer at the join after the world moved on). Never the pack's intro (the cold open), never a
    /// beat card or a finale scene (no chapter's <c>openCutscene</c>), never the player's current chapter's opening (the track
    /// start plays it after the recap on purpose) and never a hosted or arcade newcomer's personal chapter 1 (their story
    /// begins there, whatever chapter the crew is in).
    /// </summary>
    private bool IsStaleWorldChapterOpener(PlayerSession session, PendingCutscene item)
    {
        var story = _story;
        if (story is null || item.DefinitionId == StoryCutsceneIds.Recap
            || !string.Equals(story.Id, item.PackId, StringComparison.OrdinalIgnoreCase)
            || story.FindCutscene(item.Id) is not { } def
            || string.Equals(def.Id, story.IntroCutscene, StringComparison.Ordinal)
            || ChapterOfCutscene(story, def, item.Id) is not { } chapter
            || !string.Equals(chapter.OpenCutscene, def.Id, StringComparison.Ordinal))
        {
            return false;
        }

        int index = story.Chapters.IndexOf(chapter);
        if (index < 0 || (PersonalChapterOneFor(session) && index == StoryChapters.TrackChapterIndex(story, 0)))
        {
            return false;
        }

        return index < TrackChapterIndex(session);
    }

    /// <summary>
    /// #3686: whether a step of the player's pack has overtaken a scene: its <c>staleAfterStep</c> option names a step that is
    /// through (a scene no step binds — one the server's code plays), or the scene is a step's own (<c>onStart</c> /
    /// <c>onComplete</c>) and the pack's next step after it is through (<see cref="NextStoryStep"/>). Player packs only (the
    /// world story's steps are its missions; its chapter openings have <see cref="IsStaleWorldChapterOpener"/>).
    /// </summary>
    private bool IsOvertakenByStep(PlayerSession session, PendingCutscene item)
    {
        if (item.DefinitionId == StoryCutsceneIds.Recap
            || PlayerPacks.FirstOrDefault(p => string.Equals(p.Id, item.PackId, StringComparison.Ordinal)) is not { } pack
            || pack.Def.FindCutscene(item.Id) is not { } def)
        {
            return false;
        }

        string staleAfter = def.Options?.StaleAfterStep ?? string.Empty;
        if (staleAfter.Length > 0 && pack.StepsById.TryGetValue(staleAfter, out var marker) && StepOvertakes(session, marker))
        {
            return true;
        }

        return BoundStepOf(session, pack, def, item.Id) is { } bound && NextStoryStep(bound) is { } next && StepOvertakes(session, next);
    }

    /// <summary>The step whose hook played the scene: for a template its owner (the instance's <c>@step</c>), else the step
    /// latest in the pack's order that binds it (<c>onStart</c> or <c>onComplete</c>) and that the player has reached (active,
    /// done or skipped) — a step and its stand-in bind the same scene. Null for a scene no step binds.</summary>
    private static CompiledStep? BoundStepOf(PlayerSession session, CompiledPack pack, CutsceneDefinition def, string id)
    {
        if (def.Template)
        {
            return pack.StepsById.TryGetValue(StoryCutsceneIds.OwnerOf(id), out var owner) && BindsCutscene(owner, def.Id) ? owner : null;
        }

        CompiledStep? found = null;
        foreach (var step in pack.Chapters.SelectMany(c => c.Steps))
        {
            if (BindsCutscene(step, def.Id) && (StepActive(session, step) || StepDoneOrSkipped(session, step)))
            {
                found = step;
            }
        }

        return found;
    }

    private static bool BindsCutscene(CompiledStep step, string cutsceneId)
        => string.Equals(step.Def.OnStart?.Cutscene, cutsceneId, StringComparison.Ordinal)
           || string.Equals(step.Def.OnComplete?.Cutscene, cutsceneId, StringComparison.Ordinal);

    /// <summary>The pack's next step after <paramref name="bound"/> that marks the player moving on
    /// (<see cref="MarksStoryProgress"/>): the rest of its chapter, then — for a mandatory chapter — the following mandatory
    /// chapters (an optional chapter is a course of its own: the next course is not "later"). Null when none follows.</summary>
    private static CompiledStep? NextStoryStep(CompiledStep bound)
    {
        var chapter = bound.Chapter;
        var next = chapter.Steps.SkipWhile(s => s != bound).Skip(1).FirstOrDefault(MarksStoryProgress);
        if (next is not null || chapter.Optional)
        {
            return next;
        }

        return chapter.Pack.Chapters.SkipWhile(c => c != chapter).Skip(1)
            .Where(c => !c.Optional)
            .SelectMany(c => c.Steps)
            .FirstOrDefault(MarksStoryProgress);
    }

    /// <summary>A step whose completion means the story moved on: a do step (a read step completes on "continue" a moment
    /// later, a react step whenever its situation comes) that is no stand-in and holds its chapter up — not an <c>opt</c>
    /// extra of a mandatory chapter.</summary>
    private static bool MarksStoryProgress(CompiledStep step)
        => step.IsDo && !step.IsAlternative && (step.Chapter.Optional || step.Tier != StoryStepTiers.Opt);

    /// <summary>Whether <paramref name="step"/> is behind the player: done, its chapter through, or skipped — but a step
    /// skipped as infeasible while its stand-in runs is not behind them until the stand-in is.</summary>
    private static bool StepOvertakes(PlayerSession session, CompiledStep step)
    {
        if (MilestonesOf(session).Contains(StoryPackKeys.StepDone(step.Pack.Id, step.Id)) || ChapterDoneOrSkipped(session, step.Chapter))
        {
            return true;
        }

        return MilestonesOf(session).Contains(StoryPackKeys.StepSkipped(step.Pack.Id, step.Id))
               && !(step.Alternative is { } stand && StepActive(session, stand) && !StepDoneOrSkipped(session, stand));
    }
}
