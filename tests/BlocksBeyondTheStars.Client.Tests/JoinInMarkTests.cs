// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The main menu's "Join in" button and the playtest tasks behind it: a 12-pixel dot marked a new task and nothing
/// marked tasks under way. Now the button shows a quiet amber frame while tasks are under way, and the frame with a
/// light running around it plus their number while some are new. Pinned here: which mark the counts give, what the
/// tracker counts, and the running light's maths (the Unity side only turns it into a mesh).
/// </summary>
public sealed class JoinInMarkTests
{
    private const string TwoTasks = """
        { "tasks": [
          { "id": "quick-bar", "title": { "de": "Schnellleiste", "en": "Quick bar" },
            "steps": [ { "id": "pick", "text": { "de": "a", "en": "a" } }, { "id": "feel", "text": { "de": "a", "en": "a" } } ] },
          { "id": "heavy-shot", "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
            "steps": [ { "id": "launch", "text": { "de": "a", "en": "a" } } ] } ] }
        """;

    [Fact]
    public void NewTasksWin_OpenOnesGetTheQuietMark_NoneGetsNothing()
    {
        Assert.Equal(JoinInMarkKind.None, JoinInMark.Kind(newCount: 0, openCount: 0));
        Assert.Equal(JoinInMarkKind.Open, JoinInMark.Kind(newCount: 0, openCount: 2));
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(newCount: 1, openCount: 0));
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(newCount: 1, openCount: 3));
    }

    [Fact]
    public void AnInstallThatNeverOpenedThePage_IsInvited_UntilItsFirstVisit()
    {
        // It fetches no catalogue at start-up, so it knows no task: the button invites by itself, without a number.
        Assert.Equal(JoinInMarkKind.Invite, JoinInMark.Kind(newCount: 0, openCount: 0, visited: false, canFetch: true));

        // After the first visit the counts decide — with nothing waiting the button is plain again.
        Assert.Equal(JoinInMarkKind.None, JoinInMark.Kind(newCount: 0, openCount: 0, visited: true, canFetch: true));
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(newCount: 2, openCount: 0, visited: true, canFetch: true));
        Assert.Equal(JoinInMarkKind.Open, JoinInMark.Kind(newCount: 0, openCount: 1, visited: true, canFetch: true));

        // A build without the inbox key cannot fetch: its page would only say so — no invitation to a dead end.
        Assert.Equal(JoinInMarkKind.None, JoinInMark.Kind(newCount: 0, openCount: 0, visited: false, canFetch: false));

        // Known tasks win over the invitation (a catalogue left in the cache): they come with their number.
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(newCount: 3, openCount: 0, visited: false, canFetch: true));
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(newCount: 3, openCount: 0, visited: false, canFetch: false));
    }

    [Fact]
    public void TheVisit_IsRemembered_SoTheInvitationEndsForGood()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_joinin_" + Guid.NewGuid().ToString("N"));
        string file = System.IO.Path.Combine(dir, "playtests", "progress.json");
        try
        {
            var progress = new PlaytestProgress(file);
            Assert.False(progress.Visited);
            Assert.Equal(JoinInMarkKind.Invite, JoinInMark.Kind(0, 0, progress.Visited, canFetch: true));

            progress.MarkVisited(); // the player opened the Test Pilot page
            Assert.Equal(JoinInMarkKind.None, JoinInMark.Kind(0, 0, progress.Visited, canFetch: true));
            Assert.True(new PlaytestProgress(file).Visited); // the next start reads it from the file
        }
        finally
        {
            if (System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void TheNumber_StaysTwoCharactersWide()
    {
        Assert.Equal("1", JoinInMark.CountLabel(1));
        Assert.Equal("9", JoinInMark.CountLabel(9));
        Assert.Equal("9+", JoinInMark.CountLabel(10));
        Assert.Equal("9+", JoinInMark.CountLabel(250));
        Assert.Equal("0", JoinInMark.CountLabel(-3));
    }

    [Fact]
    public void TheTracker_CountsNewAndOpenTasksApart()
    {
        Assert.True(PlaytestCatalogJson.TryReadCatalog(TwoTasks, out var catalog));
        var progress = new PlaytestProgress(string.Empty);
        var tracker = new PlaytestTracker(progress);
        tracker.SetCatalog(catalog, "0.1.0-dev");
        Assert.Equal((2, 0), (tracker.NewCount, tracker.OpenCount));
        Assert.Equal(JoinInMarkKind.New, JoinInMark.Kind(tracker.NewCount, tracker.OpenCount));

        // One step of the first task closed: it is under way, the other one is still new.
        var first = tracker.Tasks[0];
        Assert.True(progress.SetStep(first, "pick", PlaytestStepStates.Done, null, 100));
        Assert.Equal(PlaytestTaskStatus.InProgress, progress.StatusOf(first));
        Assert.Equal((1, 1), (tracker.NewCount, tracker.OpenCount));

        // Every step closed: ready for the report — still "under way" for the menu.
        Assert.True(progress.SetStep(first, "feel", PlaytestStepStates.Failed, "Nichts passiert.", 110));
        Assert.Equal(PlaytestTaskStatus.ReadyToReport, progress.StatusOf(first));
        Assert.Equal((1, 1), (tracker.NewCount, tracker.OpenCount));

        // The second task under way too: nothing is new any more, the quiet mark stays.
        Assert.True(progress.SetStep(tracker.Tasks[1], "launch", PlaytestStepStates.Done, null, 120));
        Assert.Equal((0, 2), (tracker.NewCount, tracker.OpenCount));
        Assert.Equal(JoinInMarkKind.Open, JoinInMark.Kind(tracker.NewCount, tracker.OpenCount));

        // Both reports sent: the button is quiet again.
        progress.RecordSent(first, "r1", 130);
        progress.RecordSent(tracker.Tasks[1], "r2", 140);
        Assert.Equal((0, 0), (tracker.NewCount, tracker.OpenCount));
        Assert.Equal(JoinInMarkKind.None, JoinInMark.Kind(tracker.NewCount, tracker.OpenCount));
    }

    [Fact]
    public void TheOutline_IsAClosedRoundedRect_WalkedClockwiseInSmallSteps()
    {
        const float W = 178f, H = 50f, R = 14f, MaxStep = 6f;
        var outline = RunningLight.Outline(W, H, R, MaxStep);

        // Closed: it ends where it began, at 0 and at 1.
        var first = outline[0];
        var last = outline[^1];
        Assert.Equal((first.X, first.Y), (last.X, last.Y));
        Assert.Equal(0f, first.At);
        Assert.Equal(1f, last.At);
        Assert.Equal((-W / 2f + R, H / 2f), (first.X, first.Y)); // the left end of the top edge

        float turned = 0f;
        for (int i = 1; i < outline.Length; i++)
        {
            var a = outline[i - 1];
            var b = outline[i];
            Assert.True(b.At > a.At, $"point {i} does not move on along the outline");
            float step = MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            Assert.True(step <= MaxStep + 0.01f, $"step {i} is {step} long — a light in its middle would not show");

            // Every point lies on the frame (inside the rect), and its normal is a unit vector pointing out of it.
            Assert.InRange(b.X, (-W / 2f) - 0.01f, (W / 2f) + 0.01f);
            Assert.InRange(b.Y, (-H / 2f) - 0.01f, (H / 2f) + 0.01f);
            Assert.Equal(1f, MathF.Sqrt((b.NormalX * b.NormalX) + (b.NormalY * b.NormalY)), 3);
            turned += (a.NormalX * b.NormalY) - (a.NormalY * b.NormalX); // > 0 would be a turn to the left
            Assert.True((b.X * b.NormalX) + (b.Y * b.NormalY) > 0f, $"the normal of point {i} points into the button");
        }

        Assert.True(turned < -3f, "the outline does not turn clockwise all the way round");

        // The four straight edges and the corners' ends are hit exactly.
        Assert.Contains(outline, p => MathF.Abs(p.X - (W / 2f)) < 0.01f && MathF.Abs(p.Y - ((H / 2f) - R)) < 0.01f);
        Assert.Contains(outline, p => MathF.Abs(p.X + (W / 2f)) < 0.01f && MathF.Abs(p.Y + ((H / 2f) - R)) < 0.01f);
    }

    [Fact]
    public void ARadiusLargerThanTheButton_IsCutDown_AndAnEmptyRectStillClosesItsOutline()
    {
        var pill = RunningLight.Outline(60f, 20f, 50f);
        foreach (var p in pill)
        {
            Assert.InRange(p.Y, -10.01f, 10.01f);
            Assert.InRange(p.X, -30.01f, 30.01f);
        }

        var empty = RunningLight.Outline(0f, 0f, 14f);
        Assert.True(empty.Length >= 2);
        Assert.Equal(1f, empty[^1].At);
        Assert.All(empty, p => Assert.False(float.IsNaN(p.At)));
    }

    [Fact]
    public void TheLight_IsBrightestAtTheHead_FadesAlongTheTail_AndIsDarkElsewhere()
    {
        const float Tail = 0.3f;
        Assert.Equal(1f, RunningLight.Glow(0.5f, 0.5f, Tail));
        float near = RunningLight.Glow(0.45f, 0.5f, Tail), far = RunningLight.Glow(0.25f, 0.5f, Tail);
        Assert.True(near < 1f && near > far && far > 0f, $"the tail does not fade: {near}, {far}");
        Assert.Equal(0f, RunningLight.Glow(0.19f, 0.5f, Tail)); // behind the tail's end
        Assert.Equal(0f, RunningLight.Glow(0.7f, 0.5f, Tail));  // far ahead of the head
        Assert.InRange(RunningLight.Glow(0.51f, 0.5f, Tail), 0.4f, 0.6f); // the short lead ahead of the head

        // The tail wraps over the outline's start.
        Assert.True(RunningLight.Glow(0.95f, 0.05f, Tail) > 0.3f);
        Assert.Equal(1f, RunningLight.Glow(0f, 1f, Tail)); // 0 and 1 are the same point
    }

    [Fact]
    public void TheHead_GoesRoundOncePerPeriod()
    {
        Assert.Equal(0f, RunningLight.Head(0.0, 2.6f));
        Assert.Equal(0.5f, RunningLight.Head(1.3, 2.6f), 4);
        Assert.Equal(0.25f, RunningLight.Head(2.6 * 7.25, 2.6f), 3);
        Assert.Equal(0f, RunningLight.Head(5.0, 0f)); // no period: the light stands at the start
    }
}
