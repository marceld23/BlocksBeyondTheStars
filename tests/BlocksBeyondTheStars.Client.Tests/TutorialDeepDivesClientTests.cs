// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2479 — the tutorial's deep dives seen from a real client (a real <c>NetworkClient</c> against a real server): VEGA offers
/// the research course at its moment of need, the Journal's own buttons (<see cref="JournalPackPage"/>) put it off and start it
/// again, the course runs on the chip and in the Journal — tasks skipped from the Journal, the last one ticked by the client's
/// own teaching flag (the Achievements tab) — and ends with "Replay" as its only button.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class TutorialDeepDivesClientTests
{
    private const string Tutorial = "tutorial";

    private static NetStoryPack? TutorialOf(StoryPackStates? states) => states?.Packs.FirstOrDefault(p => p.Id == Tutorial);

    private static NetStoryChapter? ChapterOf(StoryPackStates? states, string id) => TutorialOf(states)?.Chapters.FirstOrDefault(c => c.Id == id);

    private static void Press(ClientServerHarness h, JournalAction button)
        => h.Client.SendStoryPack(Tutorial, button.Action, button.ChapterId, button.StepId);

    [Fact]
    public void ARealClient_TakesTheOfferedCourse_ThroughTheJournalsButtons_ToItsEnd()
    {
        using var h = new ClientServerHarness(
            BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()),
            c => c.Rules.StoryId = "none");
        var states = new List<StoryPackStates>();
        var lines = new List<ShipAiLine>();
        var objectives = new List<ObjectiveList>();
        h.Client.StoryPackStatesReceived += states.Add;
        h.Client.ShipAiLineReceived += lines.Add;
        h.Client.ObjectiveListReceived += objectives.Add;
        h.Join("Kid");
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => TutorialOf(states.LastOrDefault()) is not null));

        h.Client.SendStoryPack(Tutorial, StoryPackActions.SkipPack); // "I know the game": the courses are open
        Assert.True(h.PumpUntil(() => TutorialOf(states.LastOrDefault())?.Done == true));
        Assert.Equal(StoryChapterStates.Available, ChapterOf(states.Last(), "c2")!.State);

        // A data cache in reach (VEGA's tip pass sees it): the research course is offered, once, in VEGA's words.
        var session = h.Server.Sessions.Values.Single();
        for (int i = 0; i < 5 && !lines.Any(l => l.LineKey == "vega.chapter.offer"); i++)
        {
            session.VegaTipLive = new[] { "data_cache_near" };
            h.Tick(1.0); // one pass of the 1 Hz story tick
            h.PumpUntil(() => false, maxTicks: 2);
        }

        var offer = Assert.Single(lines, l => l.LineKey == "vega.chapter.offer");
        Assert.Equal("Knowledge and research", offer.LineArg);
        Assert.True(h.PumpUntil(() => ChapterOf(states.Last(), "c2")?.State == StoryChapterStates.Offered));

        // The Journal's buttons for an offer: Start and Later. "Later" — it stays listed, with Start.
        var buttons = JournalPackPage.ChapterActions(TutorialOf(states.Last())!, ChapterOf(states.Last(), "c2")!);
        Assert.Equal(new[] { StoryPackActions.StartChapter, StoryPackActions.DeclineChapter }, buttons.Select(b => b.Action));
        Press(h, buttons[1]);
        Assert.True(h.PumpUntil(() => ChapterOf(states.Last(), "c2")?.State == StoryChapterStates.Declined));
        buttons = JournalPackPage.ChapterActions(TutorialOf(states.Last())!, ChapterOf(states.Last(), "c2")!);
        Assert.Equal(StoryPackActions.StartChapter, Assert.Single(buttons).Action);

        // Start: the course runs — its first task on the chip and in the Journal.
        Press(h, buttons[0]);
        Assert.True(h.PumpUntil(() => ChapterOf(states.Last(), "c2")?.State == StoryChapterStates.Active));
        Assert.True(h.PumpUntil(() => objectives.LastOrDefault()?.Items.FirstOrDefault()?.StepId == "c2.1"));
        Assert.Equal(Tutorial, objectives.Last().Items[0].PackId);
        Assert.Contains(lines, l => l.LineKey == "story.tutorial.c2.kinds.start");

        // Its tasks: "Skip step" from the Journal for the field work, the Achievements tab opened for the last one.
        for (int guard = 0; guard < 12; guard++)
        {
            var pack = TutorialOf(states.Last())!;
            if (ChapterOf(states.Last(), "c2")!.State == StoryChapterStates.Done)
            {
                break;
            }

            var task = JournalPackPage.StepsOf(pack, "c2").FirstOrDefault();
            Assert.NotNull(task);
            int before = states.Count;
            if (task!.Id == "c2.7")
            {
                Assert.True(h.Client.SendUiTutorialEvent(GameEventKeys.UiProgressOpenId)); // the client's own teaching flag
            }
            else
            {
                Press(h, JournalPackPage.StepActions(task).First(b => b.Action == StoryPackActions.SkipStep));
            }

            Assert.True(h.PumpUntil(() => states.Count > before && JournalPackPage.StepsOf(TutorialOf(states.Last())!, "c2").FirstOrDefault()?.Id != task.Id));
        }

        var done = ChapterOf(states.Last(), "c2")!;
        Assert.Equal(StoryChapterStates.Done, done.State);
        Assert.Equal(StoryPackActions.ReplayChapter, Assert.Single(JournalPackPage.ChapterActions(TutorialOf(states.Last())!, done)).Action);
        Assert.Single(lines, l => l.LineKey == "vega.chapter.offer"); // never offered twice
    }
}
