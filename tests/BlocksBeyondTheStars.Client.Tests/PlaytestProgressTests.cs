// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// This install's playtest progress: steps the game recognises count up and close by themselves, steps the player
/// closes by hand ("done" or "did not work" — both close), the status a task shows on the Test Pilot page, the badge
/// a sent report earns, the gold one a confirmation brings, a re-test, and the file surviving a restart.
/// </summary>
public sealed class PlaytestProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bbts_playtest_" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_dir, "playtests", "progress.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup (also: nothing was written, so there is no folder)
        }
    }

    private static PlaytestTask Task(int rev = 1, bool retest = false)
    {
        string json = $$"""
            { "id": "heavy-shot", "rev": {{rev}}, "retest": {{(retest ? "true" : "false")}},
              "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
              "steps": [
                { "id": "launch", "text": { "de": "a", "en": "a" }, "check": { "event": "space:enter", "arg": "launch" } },
                { "id": "rocks", "text": { "de": "a", "en": "a" }, "check": { "event": "asteroid:destroyed", "required": 3 } },
                { "id": "feel", "text": { "de": "a", "en": "a" } } ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));
        return task;
    }

    [Fact]
    public void ANewTask_IsNew_WithEveryStepOpen()
    {
        var progress = new PlaytestProgress(File);
        var task = Task();

        Assert.Equal(PlaytestTaskStatus.New, progress.StatusOf(task));
        Assert.Equal(3, progress.OpenSteps(task));
        Assert.Null(progress.Get(task.Id));
        Assert.Equal((0, 0), progress.BadgeCount());
    }

    [Fact]
    public void RecognisedSteps_CountUp_AndCloseByThemselves()
    {
        var progress = new PlaytestProgress(File);
        var task = Task();

        Assert.Empty(progress.CountEvent(task, "space:enter", "hyperjump", 1, 100)); // the wrong argument
        Assert.Equal(new[] { "launch" }, progress.CountEvent(task, "space:enter", "launch", 1, 100));
        Assert.Empty(progress.CountEvent(task, "space:enter", "launch", 1, 101)); // closed already
        Assert.Empty(progress.CountEvent(task, "asteroid:destroyed", "", 2, 102));
        Assert.Equal(2, progress.StepOf(task, "rocks").Count);
        Assert.Equal(PlaytestTaskStatus.InProgress, progress.StatusOf(task));
        Assert.Equal(new[] { "rocks" }, progress.CountEvent(task, "asteroid:destroyed", "", 5, 103));

        var rocks = progress.StepOf(task, "rocks");
        Assert.Equal(PlaytestStepStates.Done, rocks.State);
        Assert.True(rocks.Auto);
        Assert.Equal(3, rocks.Count); // never counts past what the step asked for
        Assert.Equal(1, progress.OpenSteps(task));
    }

    [Fact]
    public void ThePlayerClosesAStep_DoneOrDidNotWork_BothClose()
    {
        var progress = new PlaytestProgress(File);
        var task = Task();

        Assert.True(progress.SetStep(task, "feel", PlaytestStepStates.Done, "ignored for a done step", 100));
        Assert.True(progress.SetStep(task, "launch", PlaytestStepStates.Failed, "  Das Schiff hob nicht ab.  ", 100));
        Assert.True(progress.SetStep(task, "rocks", PlaytestStepStates.Failed, null, 100));
        Assert.False(progress.SetStep(task, "no-such-step", PlaytestStepStates.Done, null, 100));

        Assert.Equal(string.Empty, progress.StepOf(task, "feel").Note);
        Assert.Equal("Das Schiff hob nicht ab.", progress.StepOf(task, "launch").Note);
        Assert.False(progress.StepOf(task, "launch").Auto);
        Assert.Equal(0, progress.OpenSteps(task));
        Assert.Equal(PlaytestTaskStatus.ReadyToReport, progress.StatusOf(task)); // "did not work" is a result too

        Assert.True(progress.SetStep(task, "feel", PlaytestStepStates.Open, null, 101)); // changed their mind
        Assert.Equal(PlaytestTaskStatus.InProgress, progress.StatusOf(task));
    }

    [Fact]
    public void ASentReport_EarnsTheBadge_AConfirmationMakesItGold()
    {
        var progress = new PlaytestProgress(File);
        var task = Task();
        progress.SetStep(task, "feel", PlaytestStepStates.Done, null, 100);
        progress.SetDraft(task, new Dictionary<string, string> { ["fun"] = "4" }, "Der Ring ist schwer zu sehen.", 100);
        Assert.Equal("4", progress.Get(task.Id)!.Answers["fun"]);

        progress.RecordSent(task, "report-1", 200);

        Assert.Equal(PlaytestTaskStatus.Submitted, progress.StatusOf(task));
        Assert.Equal((1, 0), progress.BadgeCount());
        Assert.Empty(progress.Get(task.Id)!.Answers); // the draft is gone with the send
        Assert.Equal(string.Empty, progress.Get(task.Id)!.FreeText);
        Assert.False(progress.SetStep(task, "launch", PlaytestStepStates.Done, null, 201)); // a sent report's steps are a record
        Assert.Empty(progress.CountEvent(task, "space:enter", "launch", 1, 201));

        Assert.Null(progress.MarkConfirmed("someone-elses-report"));
        Assert.Equal("heavy-shot", progress.MarkConfirmed("report-1"));
        Assert.Null(progress.MarkConfirmed("report-1")); // told once
        Assert.Equal(PlaytestTaskStatus.Confirmed, progress.StatusOf(task));
        Assert.Equal((1, 1), progress.BadgeCount());
    }

    [Fact]
    public void AReopenedTask_AsksForARetest_AndCountsAsARunOfItsOwn()
    {
        var progress = new PlaytestProgress(File);
        var first = Task(rev: 1);
        progress.SetStep(first, "feel", PlaytestStepStates.Done, null, 100);
        progress.RecordSent(first, "report-1", 200);
        progress.MarkConfirmed("report-1");

        Assert.Equal(PlaytestTaskStatus.Confirmed, progress.StatusOf(Task(rev: 2))); // a changed text alone re-opens nothing
        var again = Task(rev: 2, retest: true);
        Assert.Equal(PlaytestTaskStatus.Retest, progress.StatusOf(again));

        progress.BeginRetest(again, 300);
        Assert.Equal(PlaytestTaskStatus.Retest, progress.StatusOf(again)); // opened, nothing done yet
        Assert.Equal(3, progress.OpenSteps(again));
        Assert.Equal((1, 1), progress.BadgeCount()); // the earned badge stays

        Assert.Equal(new[] { "launch" }, progress.CountEvent(again, "space:enter", "launch", 1, 301));
        Assert.Equal(PlaytestTaskStatus.InProgress, progress.StatusOf(again));
        progress.SetStep(again, "rocks", PlaytestStepStates.Failed, "kein Asteroid da", 302);
        progress.SetStep(again, "feel", PlaytestStepStates.Done, null, 302);
        progress.RecordSent(again, "report-2", 400);

        Assert.Equal(PlaytestTaskStatus.Submitted, progress.StatusOf(again));
        Assert.Equal(2, progress.Get(again.Id)!.Runs);
        Assert.Equal((1, 0), progress.BadgeCount()); // one task, one badge — waiting for its confirmation again
    }

    [Fact]
    public void AChangedTask_KeepsTheStepsItStillHas_AndDropsTheOthers()
    {
        var progress = new PlaytestProgress(File);
        var task = Task();
        progress.SetStep(task, "feel", PlaytestStepStates.Done, null, 100);
        progress.SetStep(task, "launch", PlaytestStepStates.Done, null, 100);

        var changed = Task(rev: 2);
        changed.Steps.RemoveAt(0); // "launch" is gone
        progress.SetStep(changed, "rocks", PlaytestStepStates.Done, null, 200);

        var entry = progress.Get(changed.Id)!;
        Assert.Equal(2, entry.Rev);
        Assert.False(entry.Steps.ContainsKey("launch"));
        Assert.Equal(PlaytestStepStates.Done, entry.Steps["feel"].State);
        Assert.Equal(PlaytestTaskStatus.ReadyToReport, progress.StatusOf(changed));
    }

    [Fact]
    public void Progress_SurvivesARestart()
    {
        var task = Task();
        var first = new PlaytestProgress(File);
        first.CountEvent(task, "asteroid:destroyed", "", 2, 100);
        first.SetStep(task, "launch", PlaytestStepStates.Failed, "ging nicht", 100);
        first.SetWorld(task, "Testpilot – Der schwere Schuss", commandsRan: true, 100);

        var second = new PlaytestProgress(File);

        Assert.Equal(2, second.StepOf(task, "rocks").Count);
        Assert.Equal("ging nicht", second.StepOf(task, "launch").Note);
        Assert.Equal("heavy-shot", second.TaskOfWorld("Testpilot – Der schwere Schuss"));
        Assert.True(second.Get(task.Id)!.CommandsRan);
        Assert.Null(second.TaskOfWorld("Meine Welt"));
    }

    [Fact]
    public void OpeningThePage_IsRemembered_SoALaterStartMayFetchTheCatalogue()
    {
        var first = new PlaytestProgress(File);
        Assert.False(first.Visited); // a fresh install has never opened the page: nothing is fetched at its start
        Assert.False(System.IO.File.Exists(File)); // and merely asking writes nothing

        first.MarkVisited();
        first.MarkVisited(); // a second visit changes nothing

        Assert.True(first.Visited);
        Assert.True(new PlaytestProgress(File).Visited); // it survives a restart
        Assert.Equal((0, 0), new PlaytestProgress(File).BadgeCount()); // a visit alone earns no badge
    }

    [Fact]
    public void ABrokenOrMissingFile_StartsEmpty_AndWithoutAPathNothingIsWritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, "{ this is not a list");
        var task = Task();

        var broken = new PlaytestProgress(File);
        Assert.Equal(PlaytestTaskStatus.New, broken.StatusOf(task));
        broken.SetStep(task, "feel", PlaytestStepStates.Done, null, 100); // the next save replaces the broken file
        Assert.Equal(PlaytestStepStates.Done, new PlaytestProgress(File).StepOf(task, "feel").State);

        var memoryOnly = new PlaytestProgress(string.Empty);
        memoryOnly.SetStep(task, "feel", PlaytestStepStates.Done, null, 100);
        Assert.Equal(PlaytestStepStates.Done, memoryOnly.StepOf(task, "feel").State);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 3)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 2, 10)]
    [InlineData(9, 2, 10)]
    [InlineData(10, 3, 25)]
    [InlineData(25, 4, 0)]
    [InlineData(99, 4, 0)]
    public void Ranks_FollowTheBadgeCount(int badges, int rank, int next)
    {
        Assert.Equal(rank, PlaytestRanks.RankFor(badges));
        Assert.Equal(next, PlaytestRanks.NextThreshold(badges));
    }
}
