// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Companions tab's row decisions (#3344): "Follows me / Stays home / On board", the toggle's wording, "set home here"
/// only for a planet pet at home that is here, the "On this world" page keeps the companions waiting on board, and the
/// release button per kind (a story companion has none). The server's <c>Roams</c>/<c>Kind</c> decide what travels; an
/// older server's robot and Rudolpho are still recognised by their body plan.
/// </summary>
public sealed class CompanionSelectionTests
{
    private static NetCompanion Pet(bool following = true, bool present = true)
        => new() { Following = following, Present = present, BodyPlan = "Standard" };

    private static NetCompanion Traveller(string kind, bool following = true, bool present = true)
        => new() { Kind = kind, Roams = true, Following = following, Present = present, BodyPlan = "Standard" };

    [Fact]
    public void APlanetPet_FollowsOrStaysHome()
    {
        Assert.Equal(CompanionFollowState.Follows, CompanionSelection.StateOf(Pet()));
        Assert.Equal("ui.companions.follows", CompanionSelection.StateKey(Pet()));
        Assert.Equal("ui.companions.leave_home", CompanionSelection.ToggleKey(Pet()));

        var home = Pet(following: false);
        Assert.Equal(CompanionFollowState.StaysHome, CompanionSelection.StateOf(home));
        Assert.Equal("ui.companions.stays_home", CompanionSelection.StateKey(home));
        Assert.Equal("ui.companions.take_along", CompanionSelection.ToggleKey(home));
        Assert.Equal("ui.companions.release", CompanionSelection.ReleaseKey(home));
    }

    [Fact]
    public void ATravellingCompanion_FollowsOrWaitsOnBoard()
    {
        var soundi = Traveller("soundi");
        Assert.True(CompanionSelection.Travels(soundi));
        Assert.Equal("ui.companions.wait_on_board", CompanionSelection.ToggleKey(soundi));

        var onBoard = Traveller("soundi", following: false, present: false);
        Assert.Equal(CompanionFollowState.OnBoard, CompanionSelection.StateOf(onBoard));
        Assert.Equal("ui.companions.on_board", CompanionSelection.StateKey(onBoard));
        Assert.Equal("ui.companions.take_along", CompanionSelection.ToggleKey(onBoard));
    }

    [Fact]
    public void SetHomeHere_IsOfferedForAPetAtHomeThatIsHere_Only()
    {
        Assert.True(CompanionSelection.CanSetHome(Pet(following: false)));
        Assert.False(CompanionSelection.CanSetHome(Pet(following: false, present: false))); // on its own world, not here
        Assert.False(CompanionSelection.CanSetHome(Pet()));                                  // it follows — no home to set
        Assert.False(CompanionSelection.CanSetHome(Traveller("reindeer", following: false)));
    }

    [Fact]
    public void OnThisWorld_ListsThePresentAndThoseWaitingOnBoard()
    {
        Assert.True(CompanionSelection.ShownHere(Pet()));
        Assert.True(CompanionSelection.ShownHere(Traveller("robot", following: false, present: false)));
        Assert.False(CompanionSelection.ShownHere(Pet(following: false, present: false))); // at home on another world
    }

    [Fact]
    public void TheReleaseButton_SaysWhatHappensPerKind_AndAStoryCompanionHasNone()
    {
        Assert.Equal("ui.companions.power_down", CompanionSelection.ReleaseKey(Traveller("robot")));
        Assert.Equal("ui.companions.send_home", CompanionSelection.ReleaseKey(Traveller("reindeer")));
        Assert.Null(CompanionSelection.ReleaseKey(Traveller("soundi")));
        Assert.True(CompanionSelection.IsStory(Traveller("soundi")));
    }

    [Fact]
    public void AnOlderServer_StillHasItsRobotAndRudolphoRecognisedByTheirBodyPlan()
    {
        var robot = new NetCompanion { BodyPlan = "Robot" };     // no Kind, no Roams, Following true by default
        var reindeer = new NetCompanion { BodyPlan = "Reindeer" };

        Assert.True(CompanionSelection.Travels(robot));
        Assert.True(CompanionSelection.IsRobot(robot));
        Assert.True(CompanionSelection.IsReindeer(reindeer));
        Assert.False(CompanionSelection.IsStory(robot));
        Assert.Equal(CompanionFollowState.Follows, CompanionSelection.StateOf(robot));
    }
}
