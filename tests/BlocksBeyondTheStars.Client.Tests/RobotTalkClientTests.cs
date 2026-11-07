// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3394 — the talking robot on the client: E picks the own robot under the crosshair (after a person, before every block
/// behind it), and VEGA's panel names the robot as the speaker of its lines in its light blue — "VEGA" again for hers. Its
/// small talk badges nothing and stays out of VEGA's tips log.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class RobotTalkClientTests
{
    private const string Me = "Ada";

    private static NetCreature Robot(string owner = Me, float x = 2f, string name = "")
        => new NetCreature { Id = "c1", SpeciesId = CompanionAim.RobotSpeciesId, OwnerId = owner, CustomName = name, Name = "Robo", X = x, Y = 0f, Z = 0f };

    // ---------------- the speaker label ----------------

    [Fact]
    public void ALineWithoutASpeaker_IsVegas()
    {
        Assert.True(VegaSpeakerLabel.For(string.Empty, string.Empty).IsVega);
        Assert.True(VegaSpeakerLabel.For(null, null).IsVega);
        Assert.True(VegaSpeakerLabel.For("vega", string.Empty).IsVega); // a pack narrator id without a look of its own
        Assert.Equal(VegaSpeakerLabel.VegaNameKey, VegaSpeakerLabel.Vega.NameKey);
    }

    [Fact]
    public void TheRobotsLine_ShowsItsNameInItsLightBlue()
    {
        var who = VegaSpeakerLabel.For(RobotChatter.SpeakerId, "Blinky");

        Assert.False(who.IsVega);
        Assert.Equal("Blinky", who.Name);
        Assert.Equal(0x7FE8FF, who.Rgb);

        var nameless = VegaSpeakerLabel.For(RobotChatter.SpeakerId, "  ");
        Assert.False(nameless.IsVega);
        Assert.Equal(string.Empty, nameless.Name);
        Assert.Equal(VegaSpeakerLabel.RobotNameKey, nameless.NameKey); // "Hunter Robot"
    }

    [Fact]
    public void ANamedVoice_IsShownSafely_AndShort()
    {
        var who = VegaSpeakerLabel.For("guest", "<size=200>Huge name that is far too long for the slot");

        Assert.False(who.IsVega);
        Assert.Equal(VegaSpeakerLabel.GuestRgb, who.Rgb);
        Assert.DoesNotContain("<size", who.Name, System.StringComparison.Ordinal);
        Assert.True(who.Name.Length <= VegaSpeakerLabel.MaxNameLength + 1); // the defused tag adds one space
    }

    [Fact]
    public void AQueuedLine_KeepsItsSpeaker()
    {
        var robot = new VegaQueuedLine("Hi", false, "robot.talk.1", string.Empty, null, RobotChatter.SpeakerId, "Robo");
        var vega = new VegaQueuedLine("Hi", false, "vega.hint.night");

        Assert.Equal("Robo", robot.Who.Name);
        Assert.True(vega.Who.IsVega);
        Assert.True(default(VegaQueuedLine).Who.IsVega); // a default line is VEGA's, never a crash
    }

    [Fact]
    public void TheRobotsSmallTalk_BadgesNothing_AndStaysOutOfVegasLog()
    {
        var story = new StoryClientState();
        story.OnShipAiLine(new ShipAiLine { LineKey = "robot.talk.3", Kind = 2, Speaker = RobotChatter.SpeakerId, SpeakerName = "Robo" }, null);
        story.OnShipAiLine(new ShipAiLine { LineKey = "robot.vega.1.q", Kind = 1, LineArg = "Robo" }, null); // VEGA's question to it

        Assert.False(story.NewStoryUnseen);
        Assert.Empty(story.VegaLogKeys);
        Assert.True(VegaSpeakerLabel.IsCompanionChatter(new ShipAiLine { LineKey = "robot.talk.3" }));
        Assert.False(VegaSpeakerLabel.IsCompanionChatter(new ShipAiLine { LineKey = "vega.hint.night" }));
    }

    // ---------------- E at the own robot ----------------

    [Fact]
    public void TheOwnRobotLookedAt_IsTalkedTo_AfterAPersonButBeforeEveryBlockBehindIt()
    {
        var f = new InteractFacts { Crate = true, AimedDoor = true, TalkNear = true, AimedCompanion = true };
        Assert.Equal(InteractKind.AimedCompanion, InteractResolver.Resolve(f));
        Assert.True(InteractResolver.IsAimed(InteractKind.AimedCompanion));
        Assert.False(InteractResolver.IsPerson(InteractKind.AimedCompanion));
        Assert.False(InteractResolver.HasWorldLabel(InteractKind.AimedCompanion)); // the HUD names it ("Talk to Robo")

        f.AimedNpc = true; // a person under the crosshair stands first
        Assert.Equal(InteractKind.AimedTalk, InteractResolver.Resolve(f));

        f.Clear();
        Assert.False(f.AimedCompanion);
    }

    [Fact]
    public void TheAim_FindsOnlyTheOwnRobot_InReach()
    {
        // From the eye 1.6 above the feet at the origin, looking along +X: the robot 2 blocks ahead is hit.
        var own = Robot();
        var hit = CompanionAim.AimedOwnRobot(new[] { own }, Me, 0f, 1.6f, 0f, 1f, 0f, 0f, 6f, 0f, 0f, 0f, 0, out float distance);
        Assert.Same(own, hit);
        Assert.InRange(distance, 1f, 2f);

        Assert.Null(CompanionAim.AimedOwnRobot(new[] { Robot(owner: "Ben") }, Me, 0f, 1.6f, 0f, 1f, 0f, 0f, 6f, 0f, 0f, 0f, 0, out _));
        Assert.Null(CompanionAim.AimedOwnRobot(new[] { Robot(x: InteractionReach.CompanionTalk + 1f) }, Me, 0f, 1.6f, 0f, 1f, 0f, 0f, 20f, 0f, 0f, 0f, 0, out _));
        var pet = Robot();
        pet.SpeciesId = "sp3"; // a tamed animal is no talk target
        Assert.Null(CompanionAim.AimedOwnRobot(new[] { pet }, Me, 0f, 1.6f, 0f, 1f, 0f, 0f, 6f, 0f, 0f, 0f, 0, out _));
        Assert.Null(CompanionAim.AimedOwnRobot(new[] { own }, Me, 0f, 1.6f, 0f, -1f, 0f, 0f, 6f, 0f, 0f, 0f, 0, out _)); // looking away
    }

    [Fact]
    public void ThePrompt_NamesTheRobot_AndTheIntentCarriesItsCompanionId()
    {
        Assert.Equal("Robo", CompanionAim.DisplayName(Robot()));
        Assert.Equal("Blinky", CompanionAim.DisplayName(Robot(name: "Blinky")));

        var roster = new CompanionList
        {
            Companions = new[]
            {
                new NetCompanion { Id = "pet", Kind = string.Empty },
                new NetCompanion { Id = "tc-robot", Kind = "robot" },
            },
        };
        Assert.Equal("tc-robot", CompanionAim.RobotCompanionId(roster));
        Assert.Equal(string.Empty, CompanionAim.RobotCompanionId(null)); // the server then takes the sender's robot
    }
}
