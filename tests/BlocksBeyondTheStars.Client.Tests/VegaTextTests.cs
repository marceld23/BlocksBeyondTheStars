// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The logic behind VEGA's speech-panel paging (#736) and the re-readable tips log (#737): pages must
/// never lose a character of the line, and the milestone → locale-key mapping must cover every id the
/// server persists — including the irregular bandit briefing.
/// </summary>
public sealed class VegaTextTests
{
    // ---- PageRanges (#736): grouping wrapped lines into panel-height pages ----

    [Fact]
    public void ShortLine_IsOnePage()
    {
        var pages = VegaText.PageRanges(new[] { 0, 10 }, new[] { 26f, 26f }, 20, 116f);
        Assert.Single(pages);
        Assert.Equal((0, 20), pages[0]);
    }

    [Fact]
    public void LongLine_SplitsOnWrapBoundaries_AndCoversEveryCharacter()
    {
        // Six wrapped lines of 26 px against a 116 px box → 4 lines fit per page → pages of 4 + 2 lines.
        var starts = new[] { 0, 50, 100, 150, 200, 250 };
        var heights = Enumerable.Repeat(26f, 6).ToArray();
        var pages = VegaText.PageRanges(starts, heights, 300, 116f);

        Assert.Equal(2, pages.Count);
        Assert.Equal((0, 200), pages[0]);   // lines 0–3
        Assert.Equal((200, 100), pages[1]); // lines 4–5
        Assert.Equal(300, pages.Sum(p => p.Length)); // nothing truncated — the point of the fix
    }

    [Fact]
    public void PageBreaks_AlwaysLandOnLineStarts()
    {
        var starts = new[] { 0, 40, 80, 120, 160, 200, 240 };
        var heights = Enumerable.Repeat(30f, 7).ToArray();
        var pages = VegaText.PageRanges(starts, heights, 260, 100f); // 3 lines per page

        foreach (var (start, _) in pages)
        {
            Assert.Contains(start, starts);
        }

        Assert.Equal(3, pages.Count);
        Assert.Equal(260, pages.Sum(p => p.Length));
    }

    [Fact]
    public void LineTallerThanTheBox_StillGetsItsOwnPage()
    {
        // Degenerate but must not loop or drop text: every line exceeds maxHeight on its own.
        var pages = VegaText.PageRanges(new[] { 0, 100 }, new[] { 200f, 200f }, 180, 116f);
        Assert.Equal(2, pages.Count);
        Assert.Equal(180, pages.Sum(p => p.Length));
    }

    [Fact]
    public void EmptyText_YieldsNoPages()
        => Assert.Empty(VegaText.PageRanges(new[] { 0 }, new[] { 26f }, 0, 116f));

    [Fact]
    public void MissingLayoutInfo_FallsBackToASinglePage()
    {
        var pages = VegaText.PageRanges(System.Array.Empty<int>(), System.Array.Empty<float>(), 42, 116f);
        Assert.Single(pages);
        Assert.Equal((0, 42), pages[0]);
    }

    // ---- JournalKeys (#737): milestones → locale keys ----

    [Fact]
    public void Intro_MapsToAllIntroLines()
    {
        var keys = VegaText.JournalKeys(new[] { "vega:intro" });
        Assert.Equal(new[] { "vega.intro.1", "vega.intro.2", "vega.intro.menu", "vega.intro.codex" }, keys);
    }

    [Fact]
    public void Stages_AppearInLessonOrder_RegardlessOfSetOrder()
    {
        var keys = VegaText.JournalKeys(new[] { "vega:stage:eat", "vega:stage:mine" });
        Assert.Equal(new[] { "vega.s.mine.start", "vega.s.mine.done", "vega.s.eat.start", "vega.s.eat.done" }, keys);
    }

    [Fact]
    public void CompletedChain_AddsTheSendOff()
    {
        var all = new[] { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" }
            .Select(id => "vega:stage:" + id);
        var keys = VegaText.JournalKeys(all);
        Assert.Equal("vega.done", keys.Last());
    }

    [Fact]
    public void IncompleteChain_HasNoSendOff()
        => Assert.DoesNotContain("vega.done", VegaText.JournalKeys(new[] { "vega:stage:mine" }));

    [Fact]
    public void Hints_MapToHintKeys_WithWorldFlavourAndBanditBriefing()
    {
        var keys = VegaText.JournalKeys(new[]
        {
            "vega:hint:cold",
            "vega:hint:world:ice",
            "vega:hint:bandit_brief", // burned directly by the bandit system — irregular line key
        });

        Assert.Equal(new[] { "vega.hint.cold", "vega.hint.world.ice", "vega.brief.bandits" }, keys);
    }

    [Fact]
    public void UnknownHintIds_MapGenerically_SoFutureServerHintsSurvive()
    {
        var keys = VegaText.JournalKeys(new[] { "vega:hint:jetpack", "vega:hint:world:crystal" });
        Assert.Contains("vega.hint.jetpack", keys);
        Assert.Contains("vega.hint.world.crystal", keys);
    }

    [Fact]
    public void MemoryFragmentsAndForeignMilestones_AreExcluded()
    {
        var keys = VegaText.JournalKeys(new[] { "vega:mem:3", "story:vega_protocol:beat:1", "vega:intro" });
        Assert.Equal(new[] { "vega.intro.1", "vega.intro.2", "vega.intro.menu", "vega.intro.codex" }, keys);
    }

    [Fact]
    public void Journal_IsDeterministic_AcrossSetOrderings()
    {
        var milestones = new[]
        {
            "vega:hint:night", "vega:hint:o2", "vega:stage:craft", "vega:stage:mine",
            "vega:intro", "vega:hint:world:ocean", "vega:hint:zz_future", "vega:hint:aa_future",
        };

        var forward = VegaText.JournalKeys(milestones);
        var reversed = VegaText.JournalKeys(milestones.Reverse());
        Assert.Equal(forward, reversed);
    }
}
