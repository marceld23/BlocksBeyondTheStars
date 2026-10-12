// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2470 — the Story tab's chapter list of the VEGA pack (<see cref="StoryChapterList"/>): reached chapters titled with the
/// beats heard in them, future ones "???", only the prologue before the player's story track starts, the beats an older
/// server sends without chapters; the row labels, the crew status line and "The story so far" on demand; and the heard
/// beat indexes the list groups (<see cref="StoryBeatLog.Heard"/>).
/// </summary>
public sealed class StoryChapterListTests
{
    private static readonly Lazy<StoryDefinition> Vega = new(() => JsonSerializer.Deserialize<StoryDefinition>(
        File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "stories", "vega_protocol", "story.json")), ContentLoader.JsonOptions)!);

    private static readonly Dictionary<string, string> Table = new()
    {
        ["ui.story.prologue"] = "Prologue",
        ["ui.story.chapter_row"] = "Chapter {0} — {1}",
        ["ui.story.chapter_locked"] = "???",
        ["ui.story.crew_chapter"] = "The crew of this world is in chapter {0}.",
        ["story.vega.ch.awakening"] = "Awakening",
        ["story.vega.ch.service"] = "The Service",
        ["story.vega.ch.verdict"] = "The Verdict",
    };

    private static string L(string key) => Table.TryGetValue(key, out var v) ? v : key;

    [Fact]
    public void BeforeTheTrackStarts_OnlyThePrologueIsReached()
    {
        var rows = StoryChapterList.Build(Vega.Value, new[] { 0 }, string.Empty, started: false, worldChapterId: "verdict");

        Assert.Equal(9, rows.Count);
        Assert.Equal(StoryChapterRowState.Current, rows[0].State);
        Assert.Equal(new[] { "story.vega.beat00" }, rows[0].BeatKeys);
        Assert.All(rows.Skip(1), r => Assert.Equal(StoryChapterRowState.Locked, r.State));
        Assert.Equal("Prologue — Awakening", StoryChapterList.Label(rows[0], L));
        Assert.Equal("Chapter 4 — ???", StoryChapterList.Label(rows[4], L)); // no spoiler
    }

    [Fact]
    public void AStartedPlayer_SeesTheChaptersUpToTheirs_WithTheBeatsGroupedUnderThem()
    {
        var rows = StoryChapterList.Build(Vega.Value, Enumerable.Range(0, 9), "verdict", started: true, worldChapterId: "verdict");

        Assert.Equal(new[] { StoryChapterRowState.Done, StoryChapterRowState.Done, StoryChapterRowState.Done, StoryChapterRowState.Done,
                StoryChapterRowState.Current, StoryChapterRowState.Locked, StoryChapterRowState.Locked, StoryChapterRowState.Locked,
                StoryChapterRowState.Locked },
            rows.Select(r => r.State));
        Assert.Equal(new[] { "story.vega.beat02", "story.vega.beat03", "story.vega.beat04" }, rows[2].BeatKeys);
        Assert.Equal(new[] { "story.vega.beat06", "story.vega.beat07", "story.vega.beat08" }, rows[4].BeatKeys);
        Assert.Equal("Chapter 2 — The Service", StoryChapterList.Label(rows[2], L));
        Assert.Equal("Chapter 4 — The Verdict", StoryChapterList.Label(rows[4], L));
    }

    [Fact]
    public void AnOlderServer_WithoutChapters_ListsWhatThePlayerHeard()
    {
        var rows = StoryChapterList.Build(Vega.Value, new[] { 0, 1, 2 }, string.Empty, started: false, worldChapterId: string.Empty);
        Assert.Equal(StoryChapterRowState.Current, rows[2].State);
        Assert.Equal(StoryChapterRowState.Locked, rows[3].State);
    }

    [Fact]
    public void ThePacksWithoutChapters_GetNoList()
        => Assert.Empty(StoryChapterList.Build(new StoryDefinition { Id = "x" }, new[] { 0 }, "a", true, "a"));

    [Fact]
    public void TheCrewLine_AndTheStorySoFar_OnDemand()
    {
        Assert.Equal("The crew of this world is in chapter 5.", StoryChapterList.CrewLine(Vega.Value, "what_you_are", L));
        Assert.Equal(string.Empty, StoryChapterList.CrewLine(Vega.Value, string.Empty, L));

        Assert.Equal(new[] { "story.vega.ch.signature", "story.vega.ch.service", "story.vega.ch.guardian", "story.vega.ch.verdict" },
            LineText.SplitArgs(StoryChapterList.RecapArg(Vega.Value, "verdict")));
        Assert.Equal(string.Empty, StoryChapterList.RecapArg(Vega.Value, "signature")); // nothing behind chapter 1 yet
    }

    [Fact]
    public void TheBeatLog_KnowsTheHeardIndexes_SnapshotAndLive()
    {
        var log = new StoryBeatLog();
        log.OnStoryState(Vega.Value, "vega_protocol", new[] { 0, 2 });
        log.OnStoryLine(Vega.Value, "story.vega.beat01");
        Assert.Equal(new[] { 0, 1, 2 }, log.Heard);
    }
}
