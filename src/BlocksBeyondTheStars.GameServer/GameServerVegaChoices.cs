// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// VEGA's buttons (#2499): lines that carry <see cref="ShipAiLine.Choices"/> for a client that draws them
/// (<see cref="ClientFeatures.Choices1"/>, the Unity client). Two uses:
/// <list type="bullet">
/// <item><b>The first-start question.</b> When the basic training starts by itself for a real newcomer (an "Auto" world,
/// not a restart, not on request, no veteran or migrated progress), VEGA asks once — right after part 1's welcome card,
/// before the first lesson's line — "Want me to show you the basics?" with <c>[Let's go]</c> (the continue control: the
/// line just ends, nothing is sent — doing nothing means the training runs) and <c>[I know the game — skip]</c> (a deliberate
/// click, tap or hold; never Esc) = the ordinary <see cref="StoryPackActions.SkipPack"/> intent. The training does not wait
/// for the answer. A skip of that pack while the question is open gets one more line: the training waits in the Journal.</item>
/// <item><b>Course offers.</b> An offer line gets <c>[Later]</c> (the continue control: <see cref="StoryPackActions.DeclineChapter"/>)
/// and <c>[Start now]</c> (deliberate: <see cref="StoryPackActions.StartChapter"/>) — the Journal's own two buttons, right
/// in the panel. A client without <c>choices1</c> gets today's lines.</item>
/// </list>
/// Every choice comes back as an ordinary <see cref="StoryPackIntent"/>, validated like the Journal's (pause-whitelisted).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The first-start question (#2499) — a line of the tutorial pack's locales.</summary>
    internal const string AskSkipLine = "story.tutorial.ask_skip";

    /// <summary>VEGA's answer to "I know the game — skip" from the question (#2499): the training waits in the Journal.</summary>
    internal const string SkippedHintLine = "story.tutorial.skipped_hint";

    /// <summary>The course offer for a client that draws buttons (#2499, {0} = the chapter title): it asks "now?" — the
    /// Journal is the fallback, not the way in.</summary>
    internal const string ChapterOfferChoiceLine = "vega.chapter.offer_choice";

    /// <summary>The button labels (#2499, <c>data/locales</c>).</summary>
    internal const string ChoiceLetsGo = "ui.vega.choice.lets_go";

    internal const string ChoiceKnowGame = "ui.vega.choice.know_game";

    internal const string ChoiceStartNow = "ui.vega.choice.start_now";

    internal const string ChoiceLater = "ui.vega.choice.later";

    /// <summary>Whether the tutorial pack that is about to start by itself asks the first-start question (#2499): the
    /// official basic training on an "Auto" world, for a newcomer (the join snapshot: no onboarding before, no veteran
    /// evidence) whose client draws buttons, with nothing of the pack done or skipped yet (a migration found no progress)
    /// and a training that would not complete silently. Never on request (an opt-in world, "Start basic training"), never on
    /// a restart (the pack has started before) and never on a "Short courses only" world (it skips the training itself).</summary>
    private bool AsksBeforeTraining(PlayerSession s, CompiledPack pack)
        => pack.IsTutorial
           && string.Equals(pack.Id, OfficialTutorialPackId, StringComparison.Ordinal)
           && Rules.TutorialPolicy == TutorialPolicy.Auto
           && s.SupportsChoices && s.SupportsPacks
           && s.Join.Newcomer
           && !pack.StepsById.Values.Any(st => StepDoneOrSkipped(s, st))
           && !pack.Chapters.Any(ch => ChapterDoneOrSkipped(s, ch))
           && !PackWouldCompleteSilently(s, pack);

    /// <summary>Asks the first-start question (#2499) right before the first lesson's line — after the chapter's welcome card
    /// went out (or was queued), so the panel shows it once the card is gone. Once per start: the flag is spent here.</summary>
    private void AskSkipBeforeFirstStep(PlayerSession s, CompiledStep step)
    {
        if (s.Story.AskSkipDuePack.Length == 0 || !string.Equals(s.Story.AskSkipDuePack, step.Pack.Id, StringComparison.Ordinal))
        {
            return;
        }

        s.Story.AskSkipDuePack = string.Empty;
        s.Story.AskSkipOpenPack = step.Pack.Id;
        SendPackLine(s, step.Pack, AskSkipLine, choices: new[]
        {
            ShipAiChoices.Encode(ChoiceLetsGo, ShipAiChoices.Continue, step.Pack.Id),
            ShipAiChoices.Encode(ChoiceKnowGame, StoryPackActions.SkipPack, step.Pack.Id),
        });
    }

    /// <summary>A skip of <paramref name="pack"/> answers the open first-start question (#2499) when the question went out
    /// this session and the training still had something to skip — then VEGA says where the training waits. Spends the
    /// question either way.</summary>
    private bool AnswersAskSkip(PlayerSession s, CompiledPack pack)
    {
        if (!string.Equals(s.Story.AskSkipOpenPack, pack.Id, StringComparison.Ordinal))
        {
            return false;
        }

        s.Story.AskSkipOpenPack = string.Empty;
        return PackStarted(s, pack) && !MandatoryChaptersThrough(s, pack);
    }

    /// <summary>The two buttons of a course offer (#2499): the continue control says "Later" (the Journal's own Later — the
    /// course stays listed and startable, VEGA does not offer it again), the deliberate one "Start now". "Later" is the
    /// default on purpose: a player tapping through VEGA's lines never lands in a course they did not choose.</summary>
    private static string[] OfferChoices(CompiledChapter ch) => new[]
    {
        ShipAiChoices.Encode(ChoiceLater, StoryPackActions.DeclineChapter, ch.Pack.Id, ch.Id),
        ShipAiChoices.Encode(ChoiceStartNow, StoryPackActions.StartChapter, ch.Pack.Id, ch.Id),
    };
}
