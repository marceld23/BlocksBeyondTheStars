// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2500 — a server runs a story this client does not have (a newer build's pack): the client notes it from the story list
/// and the story state, makes a one-time notice due ("update the game to follow it") and shows the neutral "needs a newer
/// version" note for that story's keys in VEGA's panel, on the chip and in the Journal — never a raw <c>[key]</c>. A missing
/// key of a story the client has stays visible (a content bug, not an older build).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class UnknownStoryPacksTests
{
    private static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase) { "tutorial", "vega_protocol" };

    private static bool ClientHas(string id) => Installed.Contains(id);

    private static string L(string key) => key switch
    {
        "story.tutorial.b1.chip" => "Mine a block",
        UnknownStoryPacks.FallbackKey => "(needs a newer version)",
        _ => "[" + key + "]",
    };

    private static bool Has(string key) => key is "story.tutorial.b1.chip" or UnknownStoryPacks.FallbackKey;

    private static StoryPackStates States(params string[] ids)
        => new() { Packs = ids.Select(id => new NetStoryPack { Id = id }).ToArray() };

    [Fact]
    public void AStoryThisClientLacks_MakesTheNoticeDue_OncePerSession()
    {
        var unknown = new UnknownStoryPacks();
        Assert.False(unknown.Observe(UnknownStoryPacks.PackIdsOf(States("tutorial", "vega_protocol")), ClientHas));
        Assert.False(unknown.AnyMissing);
        Assert.False(unknown.TakeNotice());

        Assert.True(unknown.Observe(UnknownStoryPacks.PackIdsOf(States("tutorial", "deep_space")), ClientHas));
        Assert.Equal(new[] { "deep_space" }, unknown.Missing.ToArray());
        Assert.True(unknown.NoticeDue);
        Assert.True(unknown.TakeNotice());
        Assert.False(unknown.TakeNotice()); // once

        // The same story again, or another one later in the session: no second notice.
        Assert.False(unknown.Observe(UnknownStoryPacks.PackIdsOf(States("DEEP_SPACE")), ClientHas));
        Assert.True(unknown.Observe(UnknownStoryPacks.PackIdsOf(States("second_newer")), ClientHas));
        Assert.False(unknown.TakeNotice());
        Assert.Equal(2, unknown.Missing.Count);
        Assert.Equal("ui.story.pack_missing", UnknownStoryPacks.NoticeKey);
    }

    [Fact]
    public void TheWorldStory_IsReadFromTheStoryState_ButNeverTheSandboxOrAnInactiveStory()
    {
        Assert.Equal(new[] { "newer_world" }, UnknownStoryPacks.PackIdsOf(new StoryStateMessage { StoryId = "newer_world", Active = true }).ToArray());
        Assert.Empty(UnknownStoryPacks.PackIdsOf(new StoryStateMessage { StoryId = StoryRegistry.NoneStoryId, Active = true }));
        Assert.Empty(UnknownStoryPacks.PackIdsOf(new StoryStateMessage { StoryId = "newer_world", Active = false }));
        Assert.Empty(UnknownStoryPacks.PackIdsOf((StoryStateMessage?)null));
        Assert.Empty(UnknownStoryPacks.PackIdsOf((StoryPackStates?)null));

        var unknown = new UnknownStoryPacks();
        Assert.True(unknown.Observe(UnknownStoryPacks.PackIdsOf(new StoryStateMessage { StoryId = "newer_world", Active = true }), ClientHas));
        Assert.True(unknown.TakeNotice());

        // Reserved words and empty ids never name a story.
        var quiet = new UnknownStoryPacks();
        Assert.False(quiet.Observe(new[] { "", null, "none", "vega", "obj" }, ClientHas));
        Assert.False(quiet.NoticeDue);
        Assert.False(quiet.Observe(null, ClientHas));
    }

    [Fact]
    public void AKeyOfAStoryThisClientLacks_ReadsTheNeutralNote_NeverARawKey()
    {
        var unknown = new UnknownStoryPacks();

        // A line of an unknown story — even before the story list arrived (the line names its pack).
        Assert.Equal("(needs a newer version)", unknown.Text("story.deep_space.c1.start", "deep_space", L, Has, ClientHas));
        // A key the client has reads as itself, whatever the story.
        Assert.Equal("Mine a block", unknown.Text("story.tutorial.b1.chip", "deep_space", L, Has, ClientHas));
        // A missing key of a story this client HAS stays visible: a content bug, not an older build.
        Assert.Equal("[story.tutorial.typo]", unknown.Text("story.tutorial.typo", "tutorial", L, Has, ClientHas));
        // VEGA's own lines carry no pack: as before while no story is missing …
        Assert.Equal("[story.newer_world.beat_1]", unknown.Text("story.newer_world.beat_1", "", L, Has, ClientHas));
        Assert.Equal(string.Empty, unknown.Text("", "deep_space", L, Has, ClientHas));

        // … and the neutral note once the world story is one this client lacks (its beats carry no pack id).
        unknown.Observe(UnknownStoryPacks.PackIdsOf(new StoryStateMessage { StoryId = "newer_world", Active = true }), ClientHas);
        Assert.Equal("(needs a newer version)", unknown.Text("story.newer_world.beat_1", "", L, Has, ClientHas));
        Assert.Equal("(needs a newer version)", unknown.Text("story.newer_world.c1.chip", "newer_world", L, Has)); // noted: no lookup needed
        Assert.True(unknown.BelongsToMissing("NEWER_WORLD"));
        Assert.False(unknown.BelongsToMissing("tutorial", ClientHas));
        Assert.False(unknown.BelongsToMissing("vega", ClientHas)); // a reserved word is no story
    }
}
