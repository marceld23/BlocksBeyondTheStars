// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3405: the dialogue panel tells the robot's talk apart by its reserved id, names it in its light blue, and the
/// client announces the robot dialogue on its join.</summary>
public sealed class RobotTalkPanelTests
{
    [Fact]
    public void TheRobotsTalk_IsNamedInItsLightBlue_APersonKeepsThePanelsColour()
    {
        Assert.True(RobotTalkPanel.IsRobot(NpcDialogState.RobotNpcId));
        Assert.Equal(RobotChatter.SpeakerRgb, RobotTalkPanel.TitleRgb(NpcDialogState.RobotNpcId));
        Assert.False(RobotTalkPanel.IsRobot(12));
        Assert.Null(RobotTalkPanel.TitleRgb(12));
        Assert.Null(RobotTalkPanel.TitleRgb(0));
    }

    [Fact]
    public void TalksWithRobot_TogglesTheJoinWord()
    {
        using var client = new NetworkClient(new RecordingClientTransport());
        Assert.False(client.TalksWithRobot); // a headless client keeps the single line in VEGA's panel

        client.TalksWithRobot = true;
        Assert.Contains(ClientFeatures.RobotTalk1, client.Features);

        client.TalksWithRobot = false;
        Assert.DoesNotContain(ClientFeatures.RobotTalk1, client.Features);
    }
}
