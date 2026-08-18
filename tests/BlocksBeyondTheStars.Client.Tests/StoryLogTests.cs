// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #1110/#1111: the Story tab's logs rebuild from the server's found-keys snapshot — pack order, unknown
/// keys dropped, empty inputs safe — so a rejoin never resets what the player has read.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class StoryLogTests
{
    private static StoryDefinition Pack() => new()
    {
        Id = "test",
        Fragments =
        {
            new StoryFragment { Key = "f1", Category = "vega", TextKey = "lore.frag.f1" },
            new StoryFragment { Key = "f2", Category = "sps", TextKey = "lore.frag.f2" },
            new StoryFragment { Key = "f3", Category = "settler", TextKey = "lore.frag.f3" },
        },
        Memories =
        {
            new StoryMemory { Key = "m1", TextKey = "lore.mem.m1" },
            new StoryMemory { Key = "m2", TextKey = "lore.mem.m2" },
        },
        LoreSites =
        {
            new LoreSite { Key = "l1", Site = "monument", TextKey = "lore.site.l1" },
            new LoreSite { Key = "l2", Site = "wreck", TextKey = "lore.site.l2" },
        },
    };

    [Fact]
    public void Fragments_ResolveInPackOrder_AndDropUnknownKeys()
    {
        var rows = StoryLog.Fragments(Pack(), new[] { "f3", "gone", "f1" });
        Assert.Equal(new[] { ("vega", "lore.frag.f1"), ("settler", "lore.frag.f3") }, rows);
    }

    [Fact]
    public void Memories_ResolveInUnlockOrder()
    {
        var rows = StoryLog.Memories(Pack(), new[] { "m2", "m1" });
        Assert.Equal(new[] { "lore.mem.m1", "lore.mem.m2" }, rows);
    }

    [Fact]
    public void Lore_CarriesTheSiteForTheTitle()
    {
        var rows = StoryLog.Lore(Pack(), new[] { "l2" });
        Assert.Equal(new[] { ("wreck", "lore.site.l2") }, rows);
    }

    [Fact]
    public void EmptyInputs_YieldEmptyLogs()
    {
        Assert.Empty(StoryLog.Fragments(null, new[] { "f1" }));
        Assert.Empty(StoryLog.Fragments(Pack(), null));
        Assert.Empty(StoryLog.Memories(Pack(), System.Array.Empty<string>()));
        Assert.Empty(StoryLog.Lore(Pack(), System.Array.Empty<string>()));
    }
}
