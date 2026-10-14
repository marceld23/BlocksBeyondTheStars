// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2570 — the words of the planet map and the compass for VEGA's story help marker (labelled with the step's objective,
/// else "Goal") and the ping label that used to be hardcoded English.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class StoryMarkerTextTests
{
    private static string L(string key) => key switch
    {
        StoryMarkerText.GoalKey => "Ziel",
        StoryMarkerText.CompassKey => "Ziel {0} m",
        StoryMarkerText.PingKey => "Ping · {0}",
        _ => "<" + key + ">",
    };

    private static string StoryText(string key, string pack) => pack + "/" + key;

    [Fact]
    public void OnlyAMarkerFlaggedStory_IsAStoryMarker()
    {
        Assert.True(StoryMarkerText.IsStory(new NetMarker { Story = true, Ping = true }));
        Assert.False(StoryMarkerText.IsStory(new NetMarker { Ping = true })); // an ordinary ping, or an older server
        Assert.False(StoryMarkerText.IsStory(null));
    }

    [Fact]
    public void TheLabel_IsTheStepsObjective_ResolvedWithItsStory()
    {
        var marker = new NetMarker { Story = true, LabelKey = "story.tutorial.b2.berries.chip", PackId = "tutorial" };
        Assert.Equal("tutorial/story.tutorial.b2.berries.chip", StoryMarkerText.Label(marker, StoryText, L));
    }

    [Fact]
    public void WithoutAnObjective_TheLabelReadsGoal()
    {
        Assert.Equal("Ziel", StoryMarkerText.Label(new NetMarker { Story = true }, StoryText, L));
        Assert.Equal("Ziel", StoryMarkerText.Label(new NetMarker { Story = true, LabelKey = "k" }, (_, _) => "  ", L));
    }

    [Fact]
    public void TheCompassCaption_AndThePingLabel_AreLocalized_AndNeverThrow()
    {
        Assert.Equal("Ziel 120 m", StoryMarkerText.CompassCaption(120, L));
        Assert.Equal("Ping · Bloody Mary", StoryMarkerText.PingLabel("Bloody Mary", L));
        Assert.Equal("Ping · {weird}", StoryMarkerText.PingLabel("{weird}", L)); // a brace in a name is no format slot
        Assert.Equal("Ping Ann", StoryMarkerText.PingLabel("Ann", _ => "Ping")); // a translation without its slot
        Assert.Equal("Ping ·", StoryMarkerText.PingLabel(null, L).Trim());
    }
}
