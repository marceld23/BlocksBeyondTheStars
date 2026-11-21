// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3910 — a story that grew after a player finished it (the Sounders' Song got chapters 9 to 11). The server derives
/// what the Journal shows from the chapters: while a new part runs the story is sent as running — not done, no badge —
/// and done again once its last part is through. The client reads exactly those flags, so the page never says "done" and
/// "running" at once: no done line and no star while a part is open, the skip button while the story runs.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class JournalGrownStoryTests
{
    private static string L(string key) => "<" + key + ">";

    private static NetStoryChapter Part(string id, string state) => new() { Id = id, TitleKey = "ch." + id, State = state, Skippable = true };

    /// <summary>The Sounders' Song as the server sends it to a player who finished chapter 8 before chapters 9–11 came.</summary>
    private static NetStoryPack GrownStory(bool lastPartThrough) => new()
    {
        Id = "sounders",
        Kind = StoryRegistry.KindSide,
        Scope = StoryRegistry.ScopePlayer,
        NameKey = "story.sounders.name",
        Enabled = true,
        Started = true,
        Done = lastPartThrough,
        Badge = lastPartThrough,
        MandatoryRunning = !lastPartThrough,
        Chapters = new[]
        {
            Part("s8", StoryChapterStates.Done),
            Part("s9", lastPartThrough ? StoryChapterStates.Done : StoryChapterStates.Active),
        },
    };

    [Fact]
    public void WhileANewPartRuns_TheStoryReadsRunning_NotDone()
    {
        var pack = GrownStory(lastPartThrough: false);

        Assert.Equal(string.Empty, JournalPackPage.Status(pack, L)); // neither "done" nor the badge line
        Assert.Contains(StoryPackActions.SkipPack, JournalPackPage.PackActions(pack).Select(a => a.Action));
        var row = JournalCategories.Build(new StoryPackStates { Packs = new[] { pack } }, "none", "x", false, L)
            .Single(c => c.Id == JournalCategories.PackCategory(pack.Id));
        Assert.False(row.Badge); // no star in the left list
    }

    [Fact]
    public void OnceTheLastPartIsThrough_TheStoryIsDoneAgain()
    {
        var pack = GrownStory(lastPartThrough: true);

        Assert.Equal("<ui.journal.status_badge>", JournalPackPage.Status(pack, L));
        Assert.DoesNotContain(StoryPackActions.SkipPack, JournalPackPage.PackActions(pack).Select(a => a.Action));
    }
}
