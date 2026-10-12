// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2471 — the Journal's and the objective tracker's client rules (Client.Core, the Unity tab only draws them): the left
/// list (stories the player follows, VEGA's notes, the player's notes; the page it opens on), a story's page (part labels,
/// state words with what a locked part needs, the buttons per part, per task and per story, the status line, live progress
/// from the objective list), the chip label (the story's short name instead of "Objective"), the "Show all objectives"
/// lines, the Settings' tutorial switch, the station waypoint a VEGA line suggests, the "More" Codex chapter, the
/// <c>/tutorial</c> parsing — and a real client receiving the two new messages from a real server.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class JournalViewTests
{
    private static string L(string key) => "<" + key + ">";

    private static NetStoryPack Tutorial(params NetStoryChapter[] chapters) => new()
    {
        Id = "tutorial",
        Kind = StoryRegistry.KindTutorial,
        Scope = StoryRegistry.ScopePlayer,
        NameKey = "story.tutorial.name",
        ShortNameKey = "story.tutorial.short",
        Enabled = true,
        Started = true,
        MandatoryRunning = true,
        Chapters = chapters,
    };

    private static NetStoryChapter Chapter(string id, string state, bool optional = false, params string[] requires) => new()
    {
        Id = id,
        TitleKey = "ch." + id,
        State = state,
        Optional = optional,
        Skippable = true,
        RequiresKeys = requires,
    };

    // ---------------- The left list ----------------

    [Fact]
    public void TheLeftList_HoldsTheFollowedStories_ThenVegasNotes_ThenTheOwnNotes()
    {
        var states = new StoryPackStates
        {
            Packs = new[]
            {
                Tutorial(Chapter("b1", StoryChapterStates.Done), Chapter("c1", StoryChapterStates.Offered, optional: true)),
                new NetStoryPack { Id = "vega_protocol", Scope = StoryRegistry.ScopeWorld, NameKey = "story.vega_protocol.name", Startable = true },
                new NetStoryPack { Id = "side", Scope = StoryRegistry.ScopePlayer, NameKey = "side.name", Badge = true, Started = true, Done = true },
                new NetStoryPack { Id = "never", Scope = StoryRegistry.ScopePlayer, NameKey = "never.name" }, // not started, not startable
                new NetStoryPack { Id = "off_world", Kind = StoryRegistry.KindTutorial, Scope = StoryRegistry.ScopePlayer, Blocked = true, Startable = false },
            },
        };

        var list = JournalCategories.Build(states, "vega_protocol", "ignored", true, L);
        Assert.Equal(new[] { "pack:tutorial", "pack:vega_protocol", "pack:side", "tips", "notes" }, list.Select(c => c.Id).ToArray());
        Assert.Equal("<story.tutorial.name>", list[0].Label);
        Assert.True(list[0].Attention); // an offered part waits
        Assert.False(list[0].Badge);
        Assert.True(list[1].Attention); // "Start the story now"
        Assert.True(list[2].Badge);
        Assert.Equal("<ui.journal.cat_tips>", list[3].Label);
        Assert.Equal("<ui.notes.category>", list[4].Label);
    }

    [Fact]
    public void AnOlderServer_StillGetsTheWorldStoryPage_FromTheStoryState()
    {
        var list = JournalCategories.Build(null, "vega_protocol", "story.vega_protocol.name", true, L);
        Assert.Equal(new[] { "pack:vega_protocol", "tips", "notes" }, list.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { "tips", "notes" }, JournalCategories.Build(null, "none", "x", false, L).Select(c => c.Id).ToArray());
        Assert.Equal("vega_protocol", JournalCategories.Build(null, "vega_protocol", "missing.key", true, L, _ => false)[0].Label);
    }

    [Fact]
    public void TheJournal_OpensOnThePin_ElseTheChipsStory_ElseTheFirstStory_ElseVegasNotes()
    {
        var states = new StoryPackStates
        {
            Packs = new[] { Tutorial(), new NetStoryPack { Id = "side", Scope = StoryRegistry.ScopePlayer, Started = true } },
        };
        var list = JournalCategories.Build(states, null, null, false, L);
        var chipOnSide = new ObjectiveList { Items = new[] { new NetObjective { PackId = "side", Key = "k" } } };

        Assert.Equal("pack:tutorial", JournalCategories.Default(list, states, null));
        Assert.Equal("pack:side", JournalCategories.Default(list, states, chipOnSide));
        states.TrackedPackId = "tutorial";
        Assert.Equal("pack:tutorial", JournalCategories.Default(list, states, chipOnSide));
        Assert.Equal("tips", JournalCategories.Default(JournalCategories.Build(null, null, null, false, L), null, null));

        Assert.True(JournalCategories.TryPackId("pack:side", out string id));
        Assert.Equal("side", id);
        Assert.False(JournalCategories.TryPackId("tips", out _));
        Assert.Same(states.Packs[1], JournalCategories.Find(states, "pack:SIDE"));
    }

    // ---------------- A story's page ----------------

    [Fact]
    public void TutorialParts_ReadAsPartN_DeepDivesByTitle_AndALockedPartSaysWhatItNeeds()
    {
        var b1 = Chapter("b1", StoryChapterStates.Done);
        var c1 = Chapter("c1", StoryChapterStates.Locked, optional: true, "ch.b6", "blueprint.radio.name");
        var b2 = Chapter("b2", StoryChapterStates.Active);
        var pack = Tutorial(b1, c1, b2);

        Assert.Equal("<ui.journal.part_row>", JournalPackPage.ChapterLabel(pack, b1, L)); // no {0}: the template as is
        Assert.Equal("Part 2 — <ch.b2>", JournalPackPage.ChapterLabel(pack, b2, k => k == "ui.journal.part_row" ? "Part {0} — {1}" : L(k)));
        Assert.Equal("<ch.c1>", JournalPackPage.ChapterLabel(pack, c1, L));
        var side = new NetStoryPack { Kind = StoryRegistry.KindSide, Chapters = new[] { b1 } };
        Assert.Equal("<ch.b1>", JournalPackPage.ChapterLabel(side, b1, L));

        string needs(string k) => k == "ui.journal.needs" ? "needs: {0}" : L(k);
        Assert.Equal("<ui.journal.state.locked> — needs: <ch.b6>, <blueprint.radio.name>", JournalPackPage.StateText(c1, needs));
        Assert.Equal("<ui.journal.state.locked> — needs: <ch.b6>", JournalPackPage.StateText(c1, needs, k => k != "blueprint.radio.name"));
        Assert.Equal("<ui.journal.state.done>", JournalPackPage.StateText(b1, L));
        Assert.Equal("✔", JournalPackPage.Icon(StoryChapterStates.Done));
        Assert.Equal("●", JournalPackPage.Icon(StoryChapterStates.Active));
        Assert.Equal("○", JournalPackPage.Icon(StoryChapterStates.Locked));
    }

    [Fact]
    public void ThePartButtons_StartOrLaterForAnOffer_ReplayWhenThrough_NothingWhileTheStoryIsOff()
    {
        var offered = Chapter("c1", StoryChapterStates.Offered, optional: true);
        var declined = Chapter("c2", StoryChapterStates.Declined, optional: true);
        var done = Chapter("b1", StoryChapterStates.Done);
        var running = Chapter("b2", StoryChapterStates.Active);
        var pack = Tutorial(done, running, offered, declined);

        Assert.Equal(new[] { (StoryPackActions.StartChapter, "c1"), (StoryPackActions.DeclineChapter, "c1") },
            JournalPackPage.ChapterActions(pack, offered).Select(a => (a.Action, a.ChapterId)).ToArray());
        Assert.Equal(new[] { StoryPackActions.StartChapter }, JournalPackPage.ChapterActions(pack, declined).Select(a => a.Action).ToArray());
        Assert.Equal(new[] { StoryPackActions.ReplayChapter }, JournalPackPage.ChapterActions(pack, done).Select(a => a.Action).ToArray());
        Assert.Empty(JournalPackPage.ChapterActions(pack, running));
        Assert.Equal(new[] { StoryPackActions.SkipChapter, StoryPackActions.RestartChapter },
            JournalPackPage.RunningChapterActions(running).Select(a => a.Action).ToArray());
        Assert.Empty(JournalPackPage.RunningChapterActions(done));
        running.Skippable = false;
        Assert.Equal(new[] { StoryPackActions.RestartChapter }, JournalPackPage.RunningChapterActions(running).Select(a => a.Action).ToArray());

        pack.OptedOut = true;
        pack.Enabled = false;
        Assert.Empty(JournalPackPage.ChapterActions(pack, done));
    }

    [Fact]
    public void ATask_ReadsStepNOfM_WithLiveProgress_AndAnOpenExtraReadsExtra()
    {
        var pack = Tutorial(Chapter("b6", StoryChapterStates.Active));
        var dock = new NetStoryStep { Id = "b6.4", ChapterId = "b6", ChipKey = "chip.dock", Index = 4, Count = 8, Progress = 0, Target = 1, Skippable = true, OnChip = true, MoreGuide = "welcome" };
        var fly = new NetStoryStep { Id = "b6.2", ChapterId = "b6", ChipKey = "chip.fly", Index = 2, Count = 8, Progress = 40, Target = 300, Extra = true };
        pack.Steps = new[] { fly, dock };
        static string Loc(string k) => k switch
        {
            "ui.journal.step_row" => "Step {0} of {1}: {2}",
            "ui.journal.extra_row" => "Extra: {0}",
            _ => "<" + k + ">",
        };

        Assert.Equal("Step 4 of 8: <chip.dock>", JournalPackPage.StepLine(pack, dock, null, Loc));
        Assert.Equal("Extra: <chip.fly>  (40/300)", JournalPackPage.StepLine(pack, fly, null, Loc));
        var live = new ObjectiveList { Items = new[] { new NetObjective { PackId = "tutorial", StepId = "b6.2", Progress = 120, Target = 300 } } };
        Assert.Equal("Extra: <chip.fly>  (120/300)", JournalPackPage.StepLine(pack, fly, live, Loc)); // the list is fresher
        Assert.Equal("Step 4 of 8: [chip.dock]", JournalPackPage.StepLine(pack, dock, null, Loc, t => t.Replace('<', '[').Replace('>', ']')));

        Assert.Equal(new[] { "b6.4", "b6.2" }, JournalPackPage.StepsOf(pack, "b6").Select(s => s.Id).ToArray()); // the chip first, extras last
        var actions = JournalPackPage.StepActions(dock);
        Assert.Equal(new[] { StoryPackActions.SkipStep, string.Empty }, actions.Select(a => a.Action).ToArray());
        Assert.Equal("b6.4", actions[0].StepId);
        Assert.True(actions[1].OpensCodex);
        Assert.Equal("guide", JournalPackPage.CodexChapter(actions[1].Codex)); // an article: the Guide chapter
        Assert.Equal("lore", JournalPackPage.CodexChapter("lore"));
        Assert.Equal("guide", JournalPackPage.CodexChapter(null));
        Assert.Empty(JournalPackPage.StepActions(fly)); // an open extra counts silently — no skip
    }

    [Fact]
    public void TheStorySwitches_FollowItsState()
    {
        static string[] Words(NetStoryPack p) => JournalPackPage.PackActions(p).Select(a => a.Action).ToArray();

        var running = Tutorial();
        Assert.Equal(new[] { StoryPackActions.Track, StoryPackActions.SkipPack, StoryPackActions.RestartPack, StoryPackActions.OptOut }, Words(running));
        running.Tracked = true;
        Assert.Equal(StoryPackActions.Untrack, Words(running)[0]);
        Assert.Equal("ui.journal.tracked", JournalPackPage.PackActions(running)[0].LabelKey);

        var graduated = Tutorial();
        graduated.MandatoryRunning = false;
        graduated.Done = true;
        Assert.Equal(new[] { StoryPackActions.Track, StoryPackActions.RestartPack, StoryPackActions.OptOut }, Words(graduated));

        var off = Tutorial();
        off.OptedOut = true;
        off.Enabled = false;
        Assert.Equal(new[] { StoryPackActions.OptIn }, Words(off));

        var optInWorld = new NetStoryPack { Id = "tutorial", Kind = StoryRegistry.KindTutorial, Startable = true };
        Assert.Equal(new[] { StoryPackActions.StartPack }, Words(optInWorld));

        var blocked = new NetStoryPack { Id = "tutorial", Kind = StoryRegistry.KindTutorial, Blocked = true, Startable = true };
        Assert.Empty(Words(blocked));
    }

    [Fact]
    public void TheStatusLine_SaysOff_Blocked_NotStarted_Finished()
    {
        Assert.Equal("<ui.journal.status_blocked>", JournalPackPage.Status(new NetStoryPack { Blocked = true }, L));
        Assert.Equal("<ui.journal.status_off>", JournalPackPage.Status(new NetStoryPack { Started = true, OptedOut = true }, L));
        Assert.Equal("<ui.journal.status_not_started>", JournalPackPage.Status(new NetStoryPack { Enabled = true }, L));
        Assert.Equal("<ui.journal.status_badge>", JournalPackPage.Status(new NetStoryPack { Started = true, Enabled = true, Done = true, Badge = true }, L));
        Assert.Equal("<ui.journal.status_done>", JournalPackPage.Status(new NetStoryPack { Started = true, Enabled = true, Done = true }, L));
        Assert.Equal(string.Empty, JournalPackPage.Status(new NetStoryPack { Started = true, Enabled = true }, L));
    }

    // ---------------- The objective tracker ----------------

    [Fact]
    public void TheChipLabel_IsTheStorysShortName_ElseObjective()
    {
        var states = new StoryPackStates { Packs = new[] { Tutorial() } };
        var local = new[] { new StoryDefinition { Id = "vega_protocol", ShortNameKey = "story.vega.short" } };

        Assert.Equal("story.tutorial.short", ObjectiveTracker.ShortNameKey("tutorial", states, local));
        Assert.Equal("story.vega.short", ObjectiveTracker.ShortNameKey("vega_protocol", states, local)); // the client's own copy
        Assert.Equal(string.Empty, ObjectiveTracker.ShortNameKey("unknown", states, local));
        Assert.Equal(string.Empty, ObjectiveTracker.ShortNameKey(string.Empty, states, local));

        Assert.Equal("<story.vega.short>", ObjectiveTracker.Prefix("story.vega.short", L));
        Assert.Equal("<ui.vega.objective>", ObjectiveTracker.Prefix(string.Empty, L));
        Assert.Equal("<ui.vega.objective>", ObjectiveTracker.Prefix("story.side.short", L, _ => false)); // a key this build lacks
        Assert.Equal("Training: Dock at a station", ObjectiveTracker.ChipBody("Training", "Dock at a station"));
        Assert.Equal("(2/5)", ObjectiveTracker.Counter(2, 5));
        Assert.Equal("(5/5)", ObjectiveTracker.Counter(9, 5));
        Assert.Equal(string.Empty, ObjectiveTracker.Counter(0, 1));
    }

    [Fact]
    public void ShowAllObjectives_ListsTheOtherRows_NeverWhileChatting_AndAStoryRowFollowsTheHints()
    {
        var list = new ObjectiveList
        {
            Items = new[]
            {
                new NetObjective { Source = ObjectiveSources.Tutorial, PackId = "tutorial", Key = "a" },
                new NetObjective { Source = ObjectiveSources.Story, PackId = "vega_protocol", Key = "story.obj.search" },
                new NetObjective { Source = ObjectiveSources.Mission, PackId = "side", Key = "c" },
            },
        };

        Assert.Equal(new[] { "vega_protocol", "side" }, ObjectiveTracker.ExtraRows(list, showAll: true, chatOpen: false, hintsOn: true).Select(r => r.PackId).ToArray());
        Assert.Empty(ObjectiveTracker.ExtraRows(list, showAll: false, chatOpen: false, hintsOn: true));
        Assert.Empty(ObjectiveTracker.ExtraRows(list, showAll: true, chatOpen: true, hintsOn: true));
        Assert.Equal(new[] { "side" }, ObjectiveTracker.ExtraRows(list, showAll: true, chatOpen: false, hintsOn: false).Select(r => r.PackId).ToArray());
        Assert.Empty(ObjectiveTracker.ExtraRows(null, showAll: true, chatOpen: false, hintsOn: true));
    }

    [Fact]
    public void TheDockLessonsWaypoint_IsTakenOnlyWhenThePlayerHasNone()
    {
        Assert.Equal("sys0-st-local", ObjectiveTracker.WaypointToAdopt(null, false, "sys0-st-local"));
        Assert.Null(ObjectiveTracker.WaypointToAdopt("their-own", false, "sys0-st-local"));
        Assert.Null(ObjectiveTracker.WaypointToAdopt(null, true, "sys0-st-local")); // a free point of their own
        Assert.Null(ObjectiveTracker.WaypointToAdopt(null, false, string.Empty));
    }

    // ---------------- The Settings' tutorial switch ----------------

    [Fact]
    public void TheTutorialSwitch_ReadsOnOffBlocked_AndKeepsTheOldButtonForAnOlderServer()
    {
        Assert.Equal(TutorialSwitch.State.Unknown, TutorialSwitch.Of(null));
        Assert.Equal(TutorialSwitch.State.None, TutorialSwitch.Of(new StoryPackStates()));
        Assert.Equal(TutorialSwitch.State.On, TutorialSwitch.Of(new StoryPackStates { Packs = new[] { Tutorial() } }));

        var off = Tutorial();
        off.OptedOut = true;
        Assert.Equal(TutorialSwitch.State.Off, TutorialSwitch.Of(new StoryPackStates { Packs = new[] { off } }));
        var optInWorld = new NetStoryPack { Id = "tutorial", Kind = StoryRegistry.KindTutorial, Startable = true };
        Assert.Equal(TutorialSwitch.State.Off, TutorialSwitch.Of(new StoryPackStates { Packs = new[] { optInWorld } }));
        var blocked = new NetStoryPack { Id = "tutorial", Kind = StoryRegistry.KindTutorial, Blocked = true };
        Assert.Equal(TutorialSwitch.State.Blocked, TutorialSwitch.Of(new StoryPackStates { Packs = new[] { blocked } }));

        Assert.Equal(StoryPackActions.OptOut, TutorialSwitch.ActionFor(TutorialSwitch.State.On));
        Assert.Equal(StoryPackActions.OptIn, TutorialSwitch.ActionFor(TutorialSwitch.State.Off));
        Assert.Equal(string.Empty, TutorialSwitch.ActionFor(TutorialSwitch.State.Blocked));
        Assert.Equal(string.Empty, TutorialSwitch.ActionFor(TutorialSwitch.State.Unknown));
    }

    // ---------------- /tutorial, transient lines ----------------

    [Theory]
    [InlineData("/tutorial skip", "skip", "")]
    [InlineData("/tutorial restart mincraft Fan", "restart", "mincraft Fan")]
    [InlineData("/tutorial chapter b3", "chapter b3", "")]
    [InlineData("/tutorial chapter b3 Kid", "chapter b3", "Kid")]
    [InlineData("/tutorial policy OptIn", "policy optin", "")]
    [InlineData("/tutorial policy", "policy", "")]
    [InlineData("/tutorial chapter", "chapter", "")]
    [InlineData("/tutorial", "", "")]
    [InlineData("/tutorial bogus words", "bogus", "")]
    public void TheTutorialCommand_SplitsIntoTheServersVerbAndThePlayer(string line, string verb, string player)
    {
        Assert.Equal((verb, player), AdminChatCommand.Tutorial(line));
    }

    [Fact]
    public void LinesThatPointAtTheJournal_AreNotKeptInTheNotes()
    {
        Assert.True(VegaText.IsTransientLine(StoryNarratorLines.JournalNews));
        Assert.True(VegaText.IsTransientLine(StoryNarratorLines.StepMarkedStation));
        Assert.False(VegaText.IsTransientLine("story.vega.guardian_revealed"));
        Assert.False(VegaText.IsTransientLine(null));
    }

    // ---------------- A real client and a real server ----------------

    [Fact]
    public void ARealClient_ReceivesTheStoriesAndTheObjectives_AtTheJoinAndOnRequest()
    {
        using var h = new ClientServerHarness(BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));
        var states = new List<StoryPackStates>();
        var objectives = new List<ObjectiveList>();
        h.Client.StoryPackStatesReceived += states.Add;
        h.Client.ObjectiveListReceived += objectives.Add;
        h.Join("Kid");
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => states.Count > 0 && objectives.Count > 0));

        var tutorial = Assert.Single(states.Last().Packs, p => p.Id == "tutorial");
        Assert.True(tutorial.Started && tutorial.MandatoryRunning);
        Assert.Equal("story.tutorial.short", tutorial.ShortNameKey);
        Assert.Equal("tutorial", objectives.Last().Items[0].PackId);
        Assert.Equal("b1.1", objectives.Last().Items[0].StepId);
        Assert.Equal(TutorialSwitch.State.On, TutorialSwitch.Of(states.Last()));

        int before = states.Count;
        h.Client.SendJournalRequest();
        Assert.True(h.PumpUntil(() => states.Count > before));

        h.Client.SendStoryPack("tutorial", StoryPackActions.OptOut); // the Settings' "Tutorial: off"
        Assert.True(h.PumpUntil(() => TutorialSwitch.Of(states.Last()) == TutorialSwitch.State.Off));
    }
}
