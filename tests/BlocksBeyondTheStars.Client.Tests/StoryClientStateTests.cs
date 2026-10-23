// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2933: the client's story state (<see cref="StoryClientState"/>, moved out of GameBootstrap) — the Journal badge, the
/// beat list versus the VEGA log, the VEGA journal snapshot, the Story Log rebuilt from the story state, the reveals and the
/// world reset. These rules drive the player-visible Story badge and the Journal's pages.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class StoryClientStateTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static StoryDefinition Vega()
    {
        Assert.True(Content.TryGetStory(StoryRegistry.DefaultStoryId, out var vega));
        return vega;
    }

    private static NetStoryChapter Chapter(string id, string state, bool nothingLeft = false)
        => new() { Id = id, State = state, NothingLeft = nothingLeft };

    private static NetStoryMission Mission(string id, string state) => new() { Id = id, State = state };

    private static StoryPackStates States(NetStoryChapter[] chapters, params NetStoryMission[] missions)
        => new() { Packs = new[] { new NetStoryPack { Id = "tutorial", Chapters = chapters, Missions = missions } } };

    private static StoryStateMessage VegaState() => new() { StoryId = StoryRegistry.DefaultStoryId, Active = true };

    private static ShipAiLine Line(byte kind, string key) => new() { Kind = kind, LineKey = key };

    [Fact]
    public void JournalBadge_TheFirstBatchNeverBadges_ALaterNewOfferDoes()
    {
        var story = new StoryClientState();
        story.OnStoryPackStates(States(new[] { Chapter("c1", StoryChapterStates.Offered) }, Mission("m1", StoryChapterStates.Active)), Content);
        Assert.False(story.NewStoryUnseen); // the join's list is what the player already has
        Assert.Equal(1, story.JournalVersion);

        story.OnStoryPackStates(States(new[] { Chapter("c1", StoryChapterStates.Offered) }, Mission("m1", StoryChapterStates.Active)), Content);
        Assert.False(story.NewStoryUnseen); // the same offers again
        Assert.Equal(2, story.JournalVersion);

        story.OnStoryPackStates(States(new[] { Chapter("c1", StoryChapterStates.Offered), Chapter("c2", StoryChapterStates.Offered) }), Content);
        Assert.True(story.NewStoryUnseen); // a chapter offered since the last list
        Assert.Equal(3, story.JournalVersion);

        story.MarkSeen();
        Assert.False(story.NewStoryUnseen);
    }

    [Fact]
    public void JournalBadge_AnOfferWithNothingLeft_OrAChapterNotOffered_NeverBadges()
    {
        var story = new StoryClientState();
        story.OnStoryPackStates(States(System.Array.Empty<NetStoryChapter>()), Content);
        story.OnStoryPackStates(States(new[] { Chapter("c1", StoryChapterStates.Offered, nothingLeft: true) }), Content);
        Assert.False(story.NewStoryUnseen);

        story.OnStoryPackStates(States(new[] { Chapter("c2", StoryChapterStates.Active), Chapter("c3", StoryChapterStates.Done) }), Content);
        Assert.False(story.NewStoryUnseen);
    }

    [Fact]
    public void JournalBadge_AStoryMissionHandedOut_BadgesOnce()
    {
        var story = new StoryClientState();
        story.OnStoryPackStates(States(System.Array.Empty<NetStoryChapter>()), Content);
        story.OnStoryPackStates(States(System.Array.Empty<NetStoryChapter>(), Mission("m1", StoryChapterStates.Offered)), Content);
        Assert.False(story.NewStoryUnseen); // only an active mission counts

        story.OnStoryPackStates(States(System.Array.Empty<NetStoryChapter>(), Mission("m1", StoryChapterStates.Active)), Content);
        Assert.True(story.NewStoryUnseen);

        story.MarkSeen();
        story.OnStoryPackStates(States(System.Array.Empty<NetStoryChapter>(), Mission("m1", StoryChapterStates.Active)), Content);
        Assert.False(story.NewStoryUnseen); // once per session
    }

    [Fact]
    public void StoryPackStates_NoteAStoryThisClientLacks()
    {
        var story = new StoryClientState();
        story.OnStoryPackStates(new StoryPackStates { Packs = new[] { new NetStoryPack { Id = "tutorial" } } }, Content);
        Assert.False(story.UnknownStories.AnyMissing);

        story.OnStoryPackStates(new StoryPackStates { Packs = new[] { new NetStoryPack { Id = "newer_pack" } } }, Content);
        Assert.True(story.UnknownStories.AnyMissing);
        Assert.True(story.UnknownStories.NoticeDue);

        var noContent = new StoryClientState();
        noContent.OnStoryPackStates(new StoryPackStates { Packs = new[] { new NetStoryPack { Id = "newer_pack" } } }, null);
        Assert.False(noContent.UnknownStories.AnyMissing); // no content yet: no notice on a guess
    }

    [Fact]
    public void ShipAiLine_ABeat_BadgesAndGoesToTheBeats_NotTheVegaLog()
    {
        var story = new StoryClientState();
        story.OnStoryState(VegaState(), Content);
        string beat = Vega().Beats.First(b => !string.IsNullOrEmpty(b.TextKey)).TextKey;

        story.OnShipAiLine(Line(2, beat), Content);
        Assert.True(story.NewStoryUnseen);
        Assert.Contains(beat, story.Beats);
        Assert.DoesNotContain(beat, story.VegaLogKeys);
    }

    [Fact]
    public void ShipAiLine_ANarratorLineThatIsNoBeat_BadgesAndGoesToTheVegaLog()
    {
        var story = new StoryClientState();
        story.OnStoryState(VegaState(), Content);

        story.OnShipAiLine(Line(2, "vega.test.insight"), Content);
        Assert.True(story.NewStoryUnseen);
        Assert.DoesNotContain("vega.test.insight", story.Beats);
        Assert.Equal(new[] { "vega.test.insight" }, story.VegaLogKeys);
    }

    [Fact]
    public void ShipAiLine_WithoutThePack_EveryNarratorLineIsABeat()
    {
        var story = new StoryClientState();
        story.OnShipAiLine(Line(2, "vega.test.line"), null); // no story state, no content

        Assert.True(story.NewStoryUnseen);
        Assert.Equal(new[] { "vega.test.line" }, story.Beats);
        Assert.Empty(story.VegaLogKeys);
    }

    [Theory]
    [InlineData((byte)0)] // onboarding
    [InlineData((byte)1)] // advisor
    [InlineData((byte)3)] // system
    [InlineData((byte)5)]
    public void ShipAiLine_TheTipKinds_AreKeptOnce_WithoutABadge(byte kind)
    {
        var story = new StoryClientState();
        story.OnShipAiLine(Line(kind, "vega.hint.o2"), Content);
        story.OnShipAiLine(Line(kind, "vega.hint.o2"), Content);

        Assert.Equal(new[] { "vega.hint.o2" }, story.VegaLogKeys);
        Assert.Empty(story.Beats);
        Assert.False(story.NewStoryUnseen);
    }

    [Fact]
    public void ShipAiLine_ThePrologue_IsNotKept()
    {
        var story = new StoryClientState();
        story.OnShipAiLine(Line(4, "vega.prologue.test"), Content);

        Assert.Empty(story.VegaLogKeys);
        Assert.Empty(story.Beats);
        Assert.False(story.NewStoryUnseen);
    }

    [Fact]
    public void ShipAiLine_TheJournalNews_BadgesButIsNotKept()
    {
        var story = new StoryClientState();
        story.OnShipAiLine(Line(0, StoryNarratorLines.JournalNews), Content);
        Assert.True(story.NewStoryUnseen);
        Assert.Empty(story.VegaLogKeys);

        var transient = new StoryClientState();
        transient.OnShipAiLine(Line(0, StoryNarratorLines.StepMarkedStation), Content);
        Assert.False(transient.NewStoryUnseen);
        Assert.Empty(transient.VegaLogKeys); // a line that only sets a target is shown, never kept
    }

    [Fact]
    public void ShipAiLine_AnEmptyKey_ChangesNothing()
    {
        var story = new StoryClientState();
        story.OnShipAiLine(Line(2, string.Empty), Content);
        story.OnShipAiLine(Line(0, string.Empty), Content);

        Assert.False(story.NewStoryUnseen);
        Assert.Empty(story.VegaLogKeys);
        Assert.Empty(story.Beats);
    }

    [Fact]
    public void VegaJournal_ReplacesTheLiveKeys_AndMergesTheInsightsWithoutDuplicates()
    {
        var story = new StoryClientState();
        story.OnStoryState(new StoryStateMessage
        {
            StoryId = StoryRegistry.DefaultStoryId,
            Active = true,
            RelayInsightKeys = new[] { "vega.hint.o2", "relay.insight.a", "relay.insight.a", string.Empty },
            NoteKeys = new[] { "relay.insight.a", "story.note.b" },
        }, Content);
        story.OnShipAiLine(Line(0, "vega.hint.live"), Content);
        Assert.Contains("vega.hint.live", story.VegaLogKeys);

        story.OnVegaJournal(new VegaJournal { Milestones = new[] { "vega:hint:o2" } }, Content);
        Assert.Equal(new[] { "vega.hint.o2", "relay.insight.a", "story.note.b" }, story.VegaLogKeys);
    }

    [Fact]
    public void VegaLogKeys_IsOneListForTheWholeLife()
    {
        var story = new StoryClientState();
        var keys = story.VegaLogKeys;
        keys.Add("vega.panel.lesson"); // VEGA's panel appends its one-shot lessons to the same list

        story.OnVegaJournal(new VegaJournal { Milestones = new[] { "vega:hint:o2" } }, Content);
        story.OnStoryState(VegaState(), Content);
        story.ResetWorld();

        Assert.Same(keys, story.VegaLogKeys);
        Assert.Equal(new[] { "vega.hint.o2" }, keys);
    }

    [Fact]
    public void StoryState_WithAKnownPack_RebuildsTheLogsFromTheFoundKeys()
    {
        var vega = Vega();
        var fragment = vega.Fragments.First(f => !string.IsNullOrEmpty(f.TextKey));
        var memory = vega.Memories.First(mem => !string.IsNullOrEmpty(mem.TextKey));
        var lore = vega.LoreSites.First(site => !string.IsNullOrEmpty(site.TextKey));
        var beat = vega.Beats.First(b => !string.IsNullOrEmpty(b.TextKey));
        var story = new StoryClientState();
        story.RevealFragment(new NetFragmentRevealed { Category = "live", TextKey = "lore.live" });
        story.RevealLore(new LoreTextRevealed { Site = "live", TextKey = "lore.site.live" });

        var state = VegaState();
        state.FoundFragmentKeys = new[] { fragment.Key };
        state.PlayerMemoryKeys = new[] { memory.Key };
        state.FoundLoreKeys = new[] { lore.Key };
        state.HeardBeats = new[] { beat.Index };
        story.OnStoryState(state, Content);

        Assert.Same(state, story.Story);
        Assert.Same(vega, story.ActivePack(Content));
        Assert.Equal(new[] { (fragment.Category, fragment.TextKey) }, story.Fragments); // the server's set replaces the live one
        Assert.Equal(new[] { memory.TextKey }, story.Memories);
        Assert.Equal(new[] { (lore.Site, lore.TextKey) }, story.Lore);
        Assert.Equal(new[] { beat.Index }, story.HeardBeats);
        Assert.Equal(new[] { beat.TextKey }, story.Beats);
        Assert.False(story.UnknownStories.AnyMissing);
    }

    [Fact]
    public void StoryState_WithAnUnknownPack_NotesIt_AndKeepsTheLiveLogs()
    {
        var story = new StoryClientState();
        story.RevealFragment(new NetFragmentRevealed { Category = "live", TextKey = "lore.live" });
        story.RevealMemory(new PlayerMemoryRevealed { TextKey = "lore.mem.live" });

        story.OnStoryState(new StoryStateMessage { StoryId = "newer_world", Active = true, FoundFragmentKeys = new[] { "f1" } }, Content);

        Assert.Null(story.ActivePack(Content));
        Assert.True(story.UnknownStories.AnyMissing);
        Assert.Contains("newer_world", story.UnknownStories.Missing);
        Assert.Equal(new[] { ("live", "lore.live") }, story.Fragments);
        Assert.Equal(new[] { "lore.mem.live" }, story.Memories);
    }

    [Fact]
    public void Reveals_AreKeptOnce_AndBadgeTheStoryEntry()
    {
        var story = new StoryClientState();
        story.RevealFragment(new NetFragmentRevealed { Category = "vega", TextKey = "lore.frag.f1" });
        Assert.True(story.NewStoryUnseen);
        story.MarkSeen();
        story.RevealFragment(new NetFragmentRevealed { Category = "vega", TextKey = "lore.frag.f1" });
        Assert.True(story.NewStoryUnseen); // a repeat still badges

        story.MarkSeen();
        story.RevealMemory(new PlayerMemoryRevealed { TextKey = "lore.mem.m1" });
        story.RevealMemory(new PlayerMemoryRevealed { TextKey = "lore.mem.m1" });
        Assert.True(story.NewStoryUnseen);

        story.MarkSeen();
        story.RevealLore(new LoreTextRevealed { Site = "wreck", TextKey = "lore.site.l1" });
        story.RevealLore(new LoreTextRevealed { Site = "wreck", TextKey = "lore.site.l1" });
        story.RevealLore(new LoreTextRevealed { Site = "monument", TextKey = "lore.site.l1" });
        Assert.True(story.NewStoryUnseen);

        Assert.Equal(new[] { ("vega", "lore.frag.f1") }, story.Fragments);
        Assert.Equal(new[] { "lore.mem.m1" }, story.Memories);
        Assert.Equal(new[] { ("wreck", "lore.site.l1"), ("monument", "lore.site.l1") }, story.Lore);
    }

    [Fact]
    public void ResetWorld_DropsTheNetFragments_AndKeepsTheLogs()
    {
        var story = new StoryClientState();
        var state = VegaState();
        story.OnStoryState(state, Content);
        var relay = new RelayNetworkState();
        story.OnRelayNetwork(relay);
        story.OnNetFragments(new NetFragmentList { Fragments = new[] { new NetStoryFragment { Id = 7, Category = "vega" } } });
        story.RevealFragment(new NetFragmentRevealed { Category = "vega", TextKey = "lore.frag.f1" });
        story.RevealMemory(new PlayerMemoryRevealed { TextKey = "lore.mem.m1" });
        story.RevealLore(new LoreTextRevealed { Site = "wreck", TextKey = "lore.site.l1" });
        story.OnShipAiLine(Line(0, "vega.hint.o2"), Content);
        story.OnShipAiLine(Line(2, "vega.test.insight"), Content);
        var objectives = new ObjectiveList();
        story.OnObjectiveList(objectives);
        Assert.Single(story.NetFragments);

        story.ResetWorld();

        Assert.Empty(story.NetFragments);
        Assert.Same(state, story.Story);
        Assert.Same(relay, story.RelayNetwork);
        Assert.Same(objectives, story.Objectives);
        Assert.Equal(1, story.JournalVersion);
        Assert.True(story.NewStoryUnseen);
        Assert.Single(story.Fragments);
        Assert.Single(story.Memories);
        Assert.Single(story.Lore);
        Assert.Equal(new[] { "vega.hint.o2", "vega.test.insight" }, story.VegaLogKeys);
    }

    [Fact]
    public void ObjectivesRelayAndFragments_AreTakenAsSent()
    {
        var story = new StoryClientState();
        Assert.Null(story.Story);
        Assert.Null(story.RelayNetwork);
        Assert.Null(story.StoryPackStates);
        Assert.Null(story.Objectives);
        Assert.Empty(story.NetFragments);

        var objectives = new ObjectiveList();
        story.OnObjectiveList(objectives);
        Assert.Same(objectives, story.Objectives);
        Assert.Equal(1, story.JournalVersion);

        var list = new NetFragmentList { Fragments = new[] { new NetStoryFragment { Id = 1 } } };
        story.OnNetFragments(list);
        Assert.Same(list.Fragments, story.NetFragments);
    }

    [Fact]
    public void HasPack_WithoutContent_CountsEveryStoryAsKnown_AndActivePackNeedsContent()
    {
        Assert.True(StoryClientState.HasPack(null, "newer_pack"));
        Assert.True(StoryClientState.HasPack(null, string.Empty));
        Assert.True(StoryClientState.HasPack(Content, StoryRegistry.DefaultStoryId));
        Assert.False(StoryClientState.HasPack(Content, "newer_pack"));
        Assert.False(StoryClientState.HasPack(Content, string.Empty));

        var story = new StoryClientState();
        Assert.Null(story.ActivePack(Content)); // no story state yet
        story.OnStoryState(VegaState(), null);
        Assert.Null(story.ActivePack(null));
        Assert.Same(Vega(), story.ActivePack(Content));
    }
}
