// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2551 — VEGA's speech queue (<see cref="VegaLineQueue"/>). A player standing aboard her ship got the wall-climbing lesson
/// raised minutes earlier at a wall outside, and the training's next instruction waited behind tips. Now a client lesson
/// still waiting after <see cref="VegaLineQueue.LessonExpirySeconds"/> is dropped unseen (its once-flag is not spent, so it
/// comes again at the next wall), a step line goes ahead of waiting hints, and story lines are never dropped or reordered.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class VegaLineQueueTests
{
    private const string Tutorial = "tutorial";

    private static VegaQueuedLine Line(string key, string packId = "") => new(key, false, key, packId);

    private static string[] Keys(VegaLineQueue queue) => queue.Lines.Select(l => l.Key).ToArray();

    [Theory]
    [InlineData(1, "", VegaLineClass.Hint)]
    [InlineData(5, "", VegaLineClass.Hint)]
    [InlineData(1, Tutorial, VegaLineClass.Hint)]
    [InlineData(0, Tutorial, VegaLineClass.Step)]
    [InlineData(0, "vega_protocol", VegaLineClass.Step)]
    [InlineData(0, "", VegaLineClass.Story)] // VEGA's own onboarding / an older server's tutorial
    [InlineData(2, "", VegaLineClass.Story)] // a memory beat
    [InlineData(2, "vega_protocol", VegaLineClass.Story)]
    [InlineData(3, "", VegaLineClass.Story)] // a system notice
    [InlineData(4, "", VegaLineClass.Story)] // a prologue page
    public void ClassOf_ReadsTheLinesKindAndPack(int kind, string packId, VegaLineClass expected)
    {
        Assert.Equal(expected, VegaLineQueue.ClassOf((byte)kind, packId));
    }

    [Fact]
    public void ClassOf_WithoutAPack_IsNeverAStepLine()
    {
        Assert.Equal(VegaLineClass.Story, VegaLineQueue.ClassOf(0, null));
    }

    [Fact]
    public void AStepLine_GoesAheadOfTheWaitingHints_NeverAheadOfAStoryLineOrAnEarlierStep()
    {
        var q = new VegaLineQueue();
        q.Enqueue(Line("hint1"), VegaLineClass.Hint, 0);
        q.Enqueue(Line("beat1"), VegaLineClass.Story, 0);
        q.Enqueue(Line("lesson1"), VegaLineClass.Lesson, 0);
        q.Enqueue(Line("step1", Tutorial), VegaLineClass.Step, 0);
        Assert.Equal(new[] { "beat1", "step1", "hint1", "lesson1" }, Keys(q));

        q.Enqueue(Line("step2", Tutorial), VegaLineClass.Step, 0);
        Assert.Equal(new[] { "beat1", "step1", "step2", "hint1", "lesson1" }, Keys(q));

        // Later hints queue at the back; a story line arriving after them still keeps its place among the story and step
        // lines when the next step line pushes the hints back.
        q.Enqueue(Line("hint2"), VegaLineClass.Hint, 0);
        q.Enqueue(Line("beat2"), VegaLineClass.Story, 0);
        q.Enqueue(Line("step3", Tutorial), VegaLineClass.Step, 0);
        Assert.Equal(new[] { "beat1", "step1", "step2", "beat2", "step3", "hint1", "lesson1", "hint2" }, Keys(q));
    }

    [Fact]
    public void WithoutAStepLine_EverythingKeepsItsArrivalOrder()
    {
        var q = new VegaLineQueue();
        q.Enqueue(Line("hint1"), VegaLineClass.Hint, 0);
        q.Enqueue(Line("beat1"), VegaLineClass.Story, 0);
        q.Enqueue(Line("lesson1"), VegaLineClass.Lesson, 0);
        q.Enqueue(Line("break"), VegaLineClass.Story, 0);
        Assert.Equal(new[] { "hint1", "beat1", "lesson1", "break" }, Keys(q));

        // A step line behind no hint simply queues at the back.
        var steps = new VegaLineQueue();
        steps.Enqueue(Line("beat1"), VegaLineClass.Story, 0);
        steps.Enqueue(Line("step1", Tutorial), VegaLineClass.Step, 0);
        Assert.Equal(new[] { "beat1", "step1" }, Keys(steps));
    }

    [Fact]
    public void ALessonWaitingThirtySeconds_IsDroppedUnseen_AndNothingElseEverExpires()
    {
        int shown = 0;
        var q = new VegaLineQueue();
        q.Enqueue(Line("beat"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.climb"), VegaLineClass.Lesson, 0, () => shown++);
        q.Enqueue(Line("vega.hint.night"), VegaLineClass.Hint, 0);
        q.Enqueue(Line("step", Tutorial), VegaLineClass.Step, 0);

        Assert.Equal(0, q.Expire(VegaLineQueue.LessonExpirySeconds - 0.1));
        Assert.Equal(4, q.Count);
        Assert.Equal(1, q.Expire(VegaLineQueue.LessonExpirySeconds));
        Assert.DoesNotContain("vega.hint.climb", Keys(q));
        Assert.Equal(0, shown); // dropped unseen: the caller's once-flag is not spent

        Assert.Equal(0, q.Expire(10_000)); // story, hint and step lines wait as long as it takes
        Assert.Equal(new[] { "beat", "step", "vega.hint.night" }, Keys(q));
    }

    [Fact]
    public void ALessonAskedForAgain_KeepsItsPlace_AndItsWaitStartsOver()
    {
        var q = new VegaLineQueue();
        q.Enqueue(Line("beat"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.climb"), VegaLineClass.Lesson, 0);
        q.Enqueue(Line("beat2"), VegaLineClass.Story, 5);

        Assert.True(q.RefreshLesson("vega.hint.climb", 20)); // still at the wall
        Assert.Equal(new[] { "beat", "vega.hint.climb", "beat2" }, Keys(q)); // no second copy, same place
        Assert.Equal(0, q.Expire(35));
        Assert.Equal(1, q.Expire(50));

        // Only a waiting lesson counts: a server hint with the same key, an unknown key or none do not.
        q.Enqueue(Line("vega.hint.night"), VegaLineClass.Hint, 60);
        Assert.False(q.RefreshLesson("vega.hint.night", 61));
        Assert.False(q.RefreshLesson("vega.hint.climb", 61));
        Assert.False(q.RefreshLesson(null, 61));
        Assert.False(q.RefreshLesson(string.Empty, 61));
    }

    [Fact]
    public void TheShownCallback_RunsWhenTheLineComesUp_Once()
    {
        int shown = 0;
        var q = new VegaLineQueue();
        q.Enqueue(Line("beat"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.chart_waypoint"), VegaLineClass.Lesson, 0, () => shown++);

        Assert.True(q.TryDequeue(out var first));
        Assert.Equal("beat", first.Key);
        Assert.Equal(0, shown);
        Assert.True(q.TryDequeue(out var second));
        Assert.Equal("vega.hint.chart_waypoint", second.Key);
        Assert.Equal(1, shown);
        Assert.False(q.TryDequeue(out _));
        Assert.Equal(1, shown);
    }

    [Fact]
    public void TheReport_AClimbLessonFromOutside_NeverShowsAboard_AndComesAgainAtTheNextWall()
    {
        bool climbHintShown = false;
        void Spend() => climbHintShown = true;

        // VEGA's panel holds a folded line; a story beat waits behind it. Outside, at a terrain wall, the lesson queues.
        var q = new VegaLineQueue();
        q.Enqueue(Line("story.vega.beat"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.climb"), VegaLineClass.Lesson, 2, Spend);

        // She walks aboard and stands there; nobody presses Continue. Half a minute later the lesson is gone.
        q.Expire(31);
        q.Expire(32);
        Assert.Equal(new[] { "story.vega.beat" }, Keys(q));

        // She opens the folded line and reads on: the beat comes, the lesson never does — and it was not spent.
        Assert.True(q.TryDequeue(out var beat));
        Assert.Equal("story.vega.beat", beat.Key);
        Assert.False(q.TryDequeue(out _));
        Assert.False(climbHintShown);

        // The next wall: the lesson queues again and, with nothing ahead, comes up — now the flag is spent.
        q.Enqueue(Line("vega.hint.climb"), VegaLineClass.Lesson, 300, Spend);
        Assert.Equal(0, q.Expire(301));
        Assert.True(q.TryDequeue(out var lesson));
        Assert.Equal("vega.hint.climb", lesson.Key);
        Assert.True(climbHintShown);
    }

    [Fact]
    public void ProloguePages_GoFirst_AndTheSkipDropsOnlyThem()
    {
        var q = new VegaLineQueue();
        q.Enqueue(Line("vega.beat.systems_online"), VegaLineClass.Story, 0);
        q.Enqueue(new VegaQueuedLine("p1", true, "vega.prologue.1"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.night"), VegaLineClass.Hint, 0);
        q.Enqueue(new VegaQueuedLine("p2", true, "vega.prologue.2"), VegaLineClass.Story, 0);
        Assert.True(q.HasPrologue);

        Assert.True(q.PromotePrologue());
        Assert.Equal(new[] { "vega.prologue.1", "vega.prologue.2", "vega.beat.systems_online", "vega.hint.night" }, Keys(q));
        Assert.False(q.PromotePrologue()); // the first line is a page already

        Assert.Equal(2, q.DropLeadingPrologue());
        Assert.False(q.HasPrologue);
        Assert.Equal(new[] { "vega.beat.systems_online", "vega.hint.night" }, Keys(q));
        Assert.Equal(0, q.DropLeadingPrologue());
    }

    [Fact]
    public void Withdraw_AndClear_TakeLinesBackUnseen()
    {
        int shown = 0;
        var q = new VegaLineQueue();
        q.Enqueue(Line("story.vega.epilogue"), VegaLineClass.Story, 0);
        q.Enqueue(Line("vega.hint.night"), VegaLineClass.Hint, 0);
        q.Enqueue(Line("story.vega.epilogue"), VegaLineClass.Story, 0);
        Assert.Equal(2, q.Withdraw("story.vega.epilogue"));
        Assert.Equal(0, q.Withdraw(null));
        Assert.Equal(new[] { "vega.hint.night" }, Keys(q));

        q.Enqueue(Line("vega.hint.climb"), VegaLineClass.Lesson, 0, () => shown++);
        q.Clear();
        Assert.Empty(q.Lines);
        Assert.Equal(0, shown);
    }

    [Fact]
    public void NoCaller_SpendsALessonFlag_WhenTheLineIsOnlyQueued()
    {
        // The once-flags of the client lessons are spent by SayLocal's onShown callback — "SayLocal(…) == true" spent them on
        // queueing, so a lesson dropped unseen would never come again.
        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        foreach (string file in Directory.GetFiles(scripts, "*.cs"))
        {
            Assert.False(
                Regex.IsMatch(File.ReadAllText(file), @"SayLocal\([^;]*?\)\s*==\s*true", RegexOptions.None, TimeSpan.FromSeconds(5)),
                $"{Path.GetFileName(file)}: pass the once-flag setter to SayLocal as onShown instead of testing its result (#2551).");
        }

        // The panel runs its lines through the queue's rules.
        string panel = File.ReadAllText(Path.Combine(scripts, "VegaPanel.cs"));
        Assert.Contains("_queue.Expire(", panel);
        Assert.Contains("VegaLineQueue.ClassOf(m.Kind, m.PackId)", panel);
        Assert.Contains("VegaLineClass.Lesson", panel);
    }
}
