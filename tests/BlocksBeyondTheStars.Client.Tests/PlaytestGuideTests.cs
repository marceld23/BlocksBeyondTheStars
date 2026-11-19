// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// What the game says about the task being worked on while it is played (#3846, #3848, #3851): the HUD card names
/// the step, its number and what the tester has to do about it, stays when every step is closed, a task announces
/// itself when a world comes up, the last recognised step says that the report is due — and a step the game
/// recognises can still be closed with "did not work".
/// </summary>
public sealed class PlaytestGuideTests
{
    private const long Now = 1_800_000_000;

    private static PlaytestTask Task(string area = "ingame", string id = "space-moves")
    {
        string editor = area == "editor" ? "\"editor\": \"ship\"," : string.Empty;
        string json = $$"""
            { "id": "{{id}}", "rev": 1, "area": "{{area}}", {{editor}}
              "title": { "de": "Fassrolle und Boost", "en": "Barrel roll and boost" },
              "steps": [
                { "id": "launch", "text": { "de": "a", "en": "a" }, "check": { "event": "space:enter" } },
                { "id": "rocks", "text": { "de": "b", "en": "b" }, "check": { "event": "asteroid:destroyed", "required": 3 } },
                { "id": "feel", "text": { "de": "c", "en": "c" } } ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));
        return task;
    }

    private static PlaytestTracker Tracker(params PlaytestTask[] tasks)
    {
        var tracker = new PlaytestTracker(new PlaytestProgress(string.Empty));
        var catalog = new PlaytestCatalog();
        catalog.Tasks.AddRange(tasks);
        foreach (var task in tasks)
        {
            task.Enabled = true;
        }

        tracker.SetCatalog(catalog, "2026.11.25");
        return tracker;
    }

    [Fact]
    public void NoTaskBeingWorkedOn_HasNoCard()
    {
        var tracker = Tracker(Task());

        Assert.Null(PlaytestGuide.CardFor(tracker, null));
    }

    [Fact]
    public void TheCard_NamesTheFirstOpenStep_ItsNumber_AndThatTheGameRecognisesIt()
    {
        var tracker = Tracker(Task());
        Assert.True(tracker.Activate("space-moves", Now));

        var card = PlaytestGuide.CardFor(tracker, null);

        Assert.NotNull(card);
        Assert.Equal("launch", card!.Step!.Id);
        Assert.Equal(1, card.StepNumber);
        Assert.Equal(3, card.StepCount);
        Assert.Equal(PlaytestStepHint.Recognised, card.Hint);
        Assert.False(card.Counts);
        Assert.False(card.AllDone);
    }

    [Fact]
    public void ACountingStep_ShowsHowFarItIs()
    {
        var tracker = Tracker(Task());
        tracker.Activate("space-moves", Now);
        tracker.OnEvent("space:enter", "launch", 1, Now);
        tracker.OnEvent("asteroid:destroyed", string.Empty, 2, Now);

        var card = PlaytestGuide.CardFor(tracker, null)!;

        Assert.Equal("rocks", card.Step!.Id);
        Assert.Equal(2, card.StepNumber);
        Assert.True(card.Counts);
        Assert.Equal(2, card.Count);
        Assert.Equal(3, card.Required);
    }

    [Fact]
    public void AStepWithoutACheck_AsksTheTesterToTickItOff()
    {
        var tracker = Tracker(Task());
        tracker.Activate("space-moves", Now);
        tracker.OnEvent("space:enter", "launch", 1, Now);
        tracker.OnEvent("asteroid:destroyed", string.Empty, 3, Now);

        var card = PlaytestGuide.CardFor(tracker, null)!;

        Assert.Equal("feel", card.Step!.Id);
        Assert.Equal(3, card.StepNumber);
        Assert.Equal(PlaytestStepHint.TickOff, card.Hint);
        Assert.False(card.Counts);
    }

    [Fact]
    public void AServerThatDoesNotReportTheEvent_MakesTheStepOneToTickOff()
    {
        var tracker = Tracker(Task());
        tracker.Activate("space-moves", Now);

        var card = PlaytestGuide.CardFor(tracker, pattern => pattern == "space:enter" ? false : (bool?)null)!;

        Assert.Equal("launch", card.Step!.Id);
        Assert.Equal(PlaytestStepHint.TickOff, card.Hint);
    }

    [Fact]
    public void AnEditorEvent_IsTheClientsOwn_WhateverAServerSays()
    {
        var task = Task("editor", "first-ship");
        task.Steps[0].Check!.Event = PlaytestEditorEvents.Opened;

        Assert.False(PlaytestGuide.IsServerEvent(PlaytestEditorEvents.Opened));
        Assert.True(PlaytestGuide.Recognised(task.Steps[0], _ => false));
        Assert.False(PlaytestGuide.Recognised(task.Steps[2], null)); // no check at all
    }

    [Fact]
    public void WithEveryStepClosed_TheCardStays_AndSaysTheReportIsDue()
    {
        var tracker = Tracker(Task());
        tracker.Activate("space-moves", Now);
        tracker.OnEvent("space:enter", "launch", 1, Now);
        tracker.OnEvent("asteroid:destroyed", string.Empty, 3, Now);
        tracker.Progress.SetStep(tracker.Active!, "feel", PlaytestStepStates.Done, null, Now);

        var card = PlaytestGuide.CardFor(tracker, null);

        Assert.NotNull(card);
        Assert.True(card!.AllDone);
        Assert.Null(card.Step);
        Assert.Equal(3, card.StepCount);
    }

    [Fact]
    public void ASentReport_AndAnEditorTask_HaveNoCardInAWorld()
    {
        var ingame = Task();
        var editor = Task("editor", "first-ship");
        var tracker = Tracker(ingame, editor);

        tracker.Activate("first-ship", Now);
        Assert.Null(PlaytestGuide.CardFor(tracker, null));

        tracker.Activate("space-moves", Now);
        foreach (var step in ingame.Steps)
        {
            tracker.Progress.SetStep(ingame, step.Id, PlaytestStepStates.Done, null, Now);
        }

        tracker.Progress.RecordSent(ingame, "report-1", Now);
        Assert.Null(PlaytestGuide.CardFor(tracker, null));
    }

    [Fact]
    public void TheLastRecognisedStep_SaysTheTaskIsReady_ButAHandTickedOneDoesNot()
    {
        var task = Task();
        var tracker = Tracker(task);
        var ready = new List<string>();
        tracker.TaskReady += t => ready.Add(t.Id);
        tracker.Activate("space-moves", Now);

        // The hand-ticked step first: closing it by hand never raises the event.
        tracker.Progress.SetStep(task, "feel", PlaytestStepStates.Done, null, Now);
        tracker.OnEvent("space:enter", "launch", 1, Now);
        Assert.Empty(ready);

        tracker.OnEvent("asteroid:destroyed", string.Empty, 3, Now);
        Assert.Equal(new[] { "space-moves" }, ready);

        tracker.OnEvent("asteroid:destroyed", string.Empty, 1, Now); // nothing closes any more
        Assert.Single(ready);
    }

    [Fact]
    public void ARecognisedStep_CanBeClosedAsDidNotWork_TakenBack_AndIsRecognisedAgain()
    {
        var task = Task();
        var tracker = Tracker(task);
        tracker.Activate("space-moves", Now);
        tracker.OnEvent("asteroid:destroyed", string.Empty, 1, Now);

        Assert.True(tracker.Progress.SetStep(task, "rocks", PlaytestStepStates.Failed, "found no asteroid", Now));
        var failed = tracker.Progress.StepOf(task, "rocks");
        Assert.Equal(PlaytestStepStates.Failed, failed.State);
        Assert.False(failed.Auto);
        Assert.Equal("found no asteroid", failed.Note);

        // Closed is closed: the event no longer counts for it, and the card has moved on.
        Assert.Equal(0, tracker.OnEvent("asteroid:destroyed", string.Empty, 5, Now));
        Assert.Equal(PlaytestStepStates.Failed, tracker.Progress.StepOf(task, "rocks").State);
        Assert.Equal("launch", PlaytestGuide.CardFor(tracker, null)!.Step!.Id);

        // Taken back, the game recognises it again.
        Assert.True(tracker.Progress.SetStep(task, "rocks", PlaytestStepStates.Open, null, Now));
        Assert.Equal(1, tracker.OnEvent("asteroid:destroyed", string.Empty, 3, Now));
        var done = tracker.Progress.StepOf(task, "rocks");
        Assert.Equal(PlaytestStepStates.Done, done.State);
        Assert.True(done.Auto);
    }

    [Fact]
    public void AStepThatDidNotWork_IsInTheReport_WithItsNote()
    {
        var task = Task();
        var tracker = Tracker(task);
        tracker.Activate("space-moves", Now);
        tracker.OnEvent("space:enter", "launch", 1, Now);
        tracker.Progress.SetStep(task, "rocks", PlaytestStepStates.Failed, "found no asteroid", Now);
        tracker.Progress.SetStep(task, "feel", PlaytestStepStates.Done, null, Now);

        var built = PlaytestReport.Build(task, tracker.Progress, new PlaytestReportForm(), "2026.11.25", "reply-key", "session",
            "WindowsPlayer", System.DateTime.UtcNow, null);

        Assert.NotNull(built.Report);
        Assert.Contains("found no asteroid", built.Report!.Description);
    }

    [Fact]
    public void AWorldCreatedForTheTask_GetsTheStartCard_AnyOtherWorldALine()
    {
        var task = Task();
        var tracker = Tracker(task);
        Assert.Equal(PlaytestStartNotice.None, PlaytestGuide.StartNoticeFor(tracker, true, "My world"));

        tracker.Activate("space-moves", Now);
        string testWorld = PlaytestWorldPlan.WorldNamePrefix + "Barrel roll and boost";
        tracker.Progress.SetWorld(task, testWorld, commandsRan: false, Now);

        Assert.Equal(PlaytestStartNotice.Card, PlaytestGuide.StartNoticeFor(tracker, true, testWorld));
        Assert.Equal(PlaytestStartNotice.Line, PlaytestGuide.StartNoticeFor(tracker, true, "My world"));
        Assert.Equal(PlaytestStartNotice.Line, PlaytestGuide.StartNoticeFor(tracker, false, testWorld)); // someone else's server
        Assert.Equal(PlaytestStartNotice.Line, PlaytestGuide.StartNoticeFor(tracker, true, PlaytestWorldPlan.WorldNamePrefix + "another task"));
    }

    [Fact]
    public void AnEditorTask_OrATaskWithNothingLeftToDo_AnnouncesNothing()
    {
        var ingame = Task();
        var editor = Task("editor", "first-ship");
        var tracker = Tracker(ingame, editor);

        tracker.Activate("first-ship", Now);
        Assert.Equal(PlaytestStartNotice.None, PlaytestGuide.StartNoticeFor(tracker, true, "My world"));

        tracker.Activate("space-moves", Now);
        foreach (var step in ingame.Steps)
        {
            tracker.Progress.SetStep(ingame, step.Id, PlaytestStepStates.Done, null, Now);
        }

        Assert.Equal(PlaytestStartNotice.None, PlaytestGuide.StartNoticeFor(tracker, true, "My world"));
    }
}
